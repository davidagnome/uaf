using System.Globalization;
using System.Text;

namespace UAF.Scripting;

/// <summary>
/// Port of <c>GPDL</c>'s interpreter (GPDLexec.cpp): a stack machine over 32-bit code words whose
/// only data type is the string.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope of this port.</b> The full <c>m_interpret</c> switch has roughly 300 arms; most reach
/// into <c>party</c>, <c>globalData</c>, <c>combatData</c>, <c>CHARACTER</c> or the special-ability
/// tables, none of which exist in the port yet. Implemented here are every primary opcode and every
/// sub-opcode whose behaviour is self-contained — control flow, the frame protocol, string
/// operations, both arithmetic families, the delimited-string family, and the handful routed through
/// <see cref="IGpdlHost"/>. Every other sub-opcode throws <see cref="NotSupportedException"/>
/// naming the source line, so an unported path can never be mistaken for a wrong answer.
/// </para>
/// <para>
/// <b>Two stacks, both growing downward from 1000.</b> <c>m_SP0 = m_RP0 = 1000</c>
/// (GPDLexec.cpp:508). Push pre-decrements, pop post-increments, and the guards are asymmetric:
/// push refuses below index 2, pop refuses at or above 1000. Overflow does not throw — it latches
/// a status and lets the current instruction finish, so a script that overflows produces a partial
/// result rather than an exception.
/// </para>
/// <para>
/// <b>The frame layout is unusual and the compiler depends on it.</b> A call pushes the default
/// result, then the actual parameters left to right, then <c>BINOP_CALL</c> saves PC and FP and sets
/// <c>FP = SP</c>. So <c>FP+0</c> is the <i>last</i> actual parameter, and the result slot is at
/// <c>FP+numParam</c> — which is why <c>$RETURN expr</c> compiles to
/// <c>STORE_FP numParam</c>. Locals are pushed below FP by <c>BINOP_LOCALS</c> and get negative
/// offsets. <c>BINOP_RETURN</c> discards them wholesale with <c>SP = FP</c> and then pops exactly
/// <c>operand &amp; 0xfff</c> parameters — the local count packed into bits 12-23 of the same
/// operand is <b>never read by the VM</b>, only written by the compiler.
/// </para>
/// </remarks>
public sealed class GpdlVirtualMachine
{
    /// <summary><c>m_SP0</c> / <c>m_RP0</c> (GPDLexec.cpp:508).</summary>
    private const int StackSize = 1000;

    /// <summary>
    /// <c>m_interpretCount</c> ceiling (GPDLexec.cpp:2293) — a runaway script yields
    /// <see cref="GpdlState.GPDL_EXCESSCPU"/> rather than hanging the game.
    /// </summary>
    public const int InterpretLimit = 1000000;

    private readonly GpdlProgram _program;
    private readonly IGpdlHost _host;
    private readonly string[] _dataStack = new string[StackSize + 1];
    private readonly uint[] _returnStack = new uint[StackSize + 1];

    /// <summary>Mutable copy of the global pool: <c>$VAR</c> slots are written at run time.</summary>
    private readonly string[] _globals;

    private uint _pc;
    private int _fp;
    private int _sp;
    private int _rp;
    private int _interpretCount;
    private GpdlState _interpStatus = GpdlState.GPDL_OK;

    private const string False = "";
    private const string True = "1";

    public GpdlVirtualMachine(GpdlProgram program, IGpdlHost? host = null)
    {
        _program = program ?? throw new ArgumentNullException(nameof(program));
        _host = host ?? new GpdlUnhostedEnvironment();
        _globals = [.. program.Globals];
        _sp = StackSize;
        _rp = StackSize;
        for (int i = 0; i < _dataStack.Length; i++) { _dataStack[i] = False; }
    }

    /// <summary>The status the last run ended with.</summary>
    public GpdlState Status { get; private set; } = GpdlState.GPDL_IDLE;

    /// <summary>Instructions executed by the last run.</summary>
    public int InstructionCount => _interpretCount;

    /// <summary>
    /// When set, each instruction is appended here before it executes. This is the trace the oracle
    /// compares: address, raw word, and the data stack from the top down.
    /// </summary>
    public List<string>? Trace { get; set; }

    /// <summary>
    /// Port of <c>GPDL::BeginExecute(int entryPoint)</c> (GPDLexec.cpp:1298): runs a public function
    /// that takes no arguments and returns its result string. An entry point of 0 — what
    /// <see cref="GpdlProgram.Lookup"/> gives for an unknown name — returns the empty string without
    /// executing anything.
    /// </summary>
    /// <remarks>
    /// Execution starts <b>at</b> the entry marker cell, not after it. Unlike <c>BINOP_CALL</c>,
    /// which skips it, this path lets it run as an ordinary global fetch that pushes the marker
    /// string onto the stack (GPDLexec.cpp:1300 sets <c>m_PC = entryPoint</c> and nothing else). The
    /// pushed string lands below the frame pointer and is discarded by <c>BINOP_RETURN</c>'s
    /// <c>SP = FP</c>, so it is harmless — but it is visible in a trace, and a port that "helpfully"
    /// skipped the cell would produce a shorter trace than the reference.
    /// </remarks>
    public string Execute(uint entryPoint)
    {
        _pc = entryPoint;
        if (_pc == 0) { return False; }
        _rp = StackSize;
        _sp = StackSize;
        PushRp(0xffffffff);          // sentinel PC, so RETURN knows it is done
        PushRp(0xffffffff);          // sentinel old frame pointer
        PushSp(False);               // the default result
        _fp = _sp;
        _interpretCount = 0;
        Status = Interpret();
        if (Status != GpdlState.GPDL_IDLE) { return False; }
        return PopSp();
    }

    /// <summary>Runs a public function by name.</summary>
    public string Execute(string functionName) => Execute(_program.Lookup(functionName));

    /// <summary>
    /// Port of <c>GPDL::BeginExecute(CString&amp;, GPDL_EVENT*)</c> (GPDLexec.cpp:1219) restricted
    /// to already-parsed arguments. The C++ parses <c>name("a","b")</c> out of a text-event string
    /// and validates the count against the parenthesised number in the function's entry marker; that
    /// count check is reproduced here because a mismatch leaves the stack skewed rather than
    /// erroring at run time.
    /// </summary>
    public string Execute(string functionName, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        _pc = _program.Lookup(functionName);
        if (_pc == 0) { Status = GpdlState.GPDL_NOSUCHNAME; return False; }

        int needed = EntryMarkerParameterCount(_pc);
        if (needed != arguments.Count)
        {
            Status = GpdlState.GPDL_NOSUCHNAME;   // what the C++ returns for a bad call, oddly
            return False;
        }

        _rp = StackSize;
        _sp = StackSize;
        PushRp(0xffffffff);
        PushRp(0xffffffff);
        PushSp(False);
        foreach (string arg in arguments) { PushSp(arg); }
        _fp = _sp;
        _interpretCount = 0;
        Status = Interpret();
        if (Status != GpdlState.GPDL_IDLE) { return False; }
        // BINOP_RETURN has already reset SP to the old frame pointer and popped the parameters, so
        // the stack top IS the result slot. Reading _dataStack[_fp + n] would be wrong here: _fp has
        // been restored to the 0xffffffff sentinel by then.
        return PopSp();
    }

    /// <summary>
    /// Reads the parameter count out of a function's entry marker. The marker is the global-pool
    /// string <c>"name(n)"</c> referenced by the cell <i>at</i> the entry address; GPDLexec.cpp:1250
    /// scans backwards from the character before the last for digits. An embedded script compiles
    /// the marker as the empty string, in which case this yields 0.
    /// </summary>
    private int EntryMarkerParameterCount(uint entryPoint)
    {
        string marker = GlobalAt(_program.Code[entryPoint] & 0xffffff);
        int needed = 0;
        int multiplier = 1;
        for (int i = marker.Length - 2; i >= 0; i--)
        {
            char c = marker[i];
            if (c < '0' || c > '9') { break; }
            needed += multiplier * (c - '0');
            multiplier *= 10;
        }
        return needed;
    }

    private string GlobalAt(uint index) =>
        index < (uint)_globals.Length ? _globals[index] : "Undefined";

    // ---------------------------------------------------------------- stacks

    private void PushRp(uint n)
    {
        if (_rp < 2)
        {
            if (_interpStatus == GpdlState.GPDL_OK) { _interpStatus = GpdlState.GPDL_OVER_RP; }
            return;
        }
        _returnStack[--_rp] = n;
    }

    private uint PopRp()
    {
        if (_rp >= StackSize)
        {
            if (_interpStatus == GpdlState.GPDL_OK) { _interpStatus = GpdlState.GPDL_UNDER_RP; }
            return 0;
        }
        return _returnStack[_rp++];
    }

    private void PushSp(string val)
    {
        if (_sp < 2)
        {
            if (_interpStatus == GpdlState.GPDL_OK) { _interpStatus = GpdlState.GPDL_OVER_SP; }
            return;
        }
        _dataStack[--_sp] = val;
    }

    private string PopSp()
    {
        if (_sp >= StackSize)
        {
            if (_interpStatus == GpdlState.GPDL_OK) { _interpStatus = GpdlState.GPDL_UNDER_SP; }
            return False;
        }
        return _dataStack[_sp++];
    }

    /// <summary>
    /// <c>GPDL::m_popInteger</c> (GPDLexec.cpp:765). Not <c>int.Parse</c>: a leading <c>-</c> is a
    /// sign, <c>0x</c> switches to hex <b>only</b> at index 1, and a <c>.</c> after at least one
    /// digit truncates the fraction (so <c>"3.75"</c> is 3, and <c>"3.7a"</c> is 3 with a bad-integer
    /// status). The accumulator has no overflow check.
    /// </summary>
    private int PopInteger() => Parse(PopSp(), out _);

    /// <summary>
    /// <c>SUBOP_NUMERIC</c> asks only whether <see cref="Parse"/> would succeed
    /// (GPDLexec.cpp:5046). Sharing the parser is not just tidiness: two copies would drift on the
    /// hex and decimal-point edge cases, and <c>$NUMERIC</c> would start disagreeing with the
    /// arithmetic about what a number is.
    /// </summary>
    private bool PopIsNumeric()
    {
        Parse(PopSp(), out GpdlState status);
        return status == GpdlState.GPDL_OK;
    }

    private static int Parse(string str, out GpdlState status)
    {
        bool negative = false;
        uint n = 0;
        uint numberBase = 10;
        status = GpdlState.GPDL_OK;

        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            if (c == '-' && i == 0) { negative = true; continue; }
            if (c < '0' || c > '9')
            {
                if (c is 'x' or 'X')
                {
                    if (n != 0 || numberBase != 10 || i != 1)
                    {
                        status = GpdlState.GPDL_BADINTEGER;
                        break;
                    }
                    numberBase = 16;
                    continue;
                }
                if (numberBase == 10 && i != 0 && c == '.')
                {
                    // Everything after the point must be a digit, but is discarded either way.
                    for (i++; i < str.Length; i++)
                    {
                        if (str[i] < '0' || str[i] > '9') { status = GpdlState.GPDL_BADINTEGER; }
                    }
                }
                else { status = GpdlState.GPDL_BADINTEGER; }
                break;
            }
            // Note: hex digits a-f are unreachable. The original's letter handling sits below a
            // `break` (GPDLexec.cpp:808, marked "Unreachable"), so "0xff" parses as 0 then fails.
            n = unchecked(n * numberBase + (uint)(c - '0'));
        }

        if (status != GpdlState.GPDL_OK) { n = 0; }
        return negative ? unchecked(-(int)n) : unchecked((int)n);
    }

    private void PushInteger(int i) => PushSp(i.ToString(CultureInfo.InvariantCulture));

    private void PushUInteger(uint u) => PushSp(u.ToString(CultureInfo.InvariantCulture));

    // ---------------------------------------------------------------- interpreter

    private GpdlState Interpret()
    {
        _interpStatus = GpdlState.GPDL_OK;
        while (_interpStatus == GpdlState.GPDL_OK)
        {
            _interpretCount++;
            if (_interpretCount > InterpretLimit)
            {
                _interpStatus = GpdlState.GPDL_EXCESSCPU;
                return _interpStatus;
            }

            if (_pc >= (uint)_program.Code.Length)
            {
                _interpStatus = GpdlState.GPDL_EVENT_ERROR;
                return _interpStatus;
            }

            uint bincode = _program.Code[_pc];
            if (Trace is not null) { Trace.Add(FormatTraceLine(_pc, bincode)); }
            _pc++;

            var opcode = GpdlCode.OpOf(bincode);
            uint subop = GpdlCode.OperandOf(bincode);

            switch (opcode)
            {
                case BinOp.BINOP_LOCALS:
                    for (uint i = 0; i < subop; i++) { PushSp(False); }
                    break;

                case BinOp.BINOP_JUMP:
                    _pc = subop;
                    break;

                case BinOp.BINOP_ReferenceGLOBAL:
                    if ((subop & GpdlCode.GlobalStoreBit) != 0)
                    {
                        uint slot = subop & 0x7fffff;
                        string value = PopSp();
                        if (slot < (uint)_globals.Length) { _globals[slot] = value; }
                    }
                    else
                    {
                        // The fetch path does NOT mask off bit 23 (GPDLexec.cpp:2321) -- it cannot
                        // be set here, because the store branch already claimed it.
                        PushSp(GlobalAt(subop));
                    }
                    break;

                case BinOp.BINOP_FETCHTEXT:
                    throw new NotSupportedException(
                        "BINOP_FETCHTEXT (GPDLexec.cpp:2325) reads a NUL-terminated string out of " +
                        "the code buffer itself. It only appears in the embedded-script blob that " +
                        "GPDLCOMP::CompileScript builds (GPDLcomp.cpp:3626), where the constant " +
                        "pool is appended after the code as raw text and every " +
                        "BINOP_ReferenceGLOBAL is rewritten to a byte offset. That blob format is " +
                        "not ported; talk.bin keeps its constants in a separate segment and uses " +
                        "BINOP_ReferenceGLOBAL.");

                case BinOp.BINOP_CALL:
                    PushRp(_pc);
                    PushRp((uint)_fp);
                    _fp = _sp;
                    _pc = subop;
                    // Skip the entry marker cell -- it is a global reference, not an instruction.
                    _pc++;
                    break;

                case BinOp.BINOP_FETCH_FP:
                    PushSp(_dataStack[_fp + GpdlCode.SignExtend24(subop)]);
                    break;

                case BinOp.BINOP_JUMPFALSE:
                    {
                        string cond = PopSp();
                        // Both the empty string and the literal "0" are false. Any other string,
                        // including "00" and " ", is true.
                        if (cond.Length == 0 || string.Equals(cond, "0", StringComparison.Ordinal))
                        {
                            _pc = subop;
                        }
                        break;
                    }

                case BinOp.BINOP_RETURN:
                    {
                        _sp = _fp;
                        _fp = (int)PopRp();
                        _pc = PopRp();
                        // Only the low 12 bits (the parameter count) are consumed; the local count
                        // in bits 12-23 is never read, because SP = FP already dropped the locals.
                        for (uint n = subop; (n & 0xfff) != 0; n--) { PopSp(); }
                        if (_pc == 0xffffffff)
                        {
                            return GpdlState.GPDL_IDLE;
                        }
                        break;
                    }

                case BinOp.BINOP_STORE_FP:
                    _dataStack[_fp + GpdlCode.SignExtend24(subop)] = PopSp();
                    break;

                case BinOp.BINOP_SUBOP:
                    {
                        GpdlState? early = ExecuteSubOp((SubOp)subop);
                        if (early is not null) { return early.Value; }
                        break;
                    }

                default:
                    throw new NotSupportedException(
                        $"Illegal opcode 0x{(uint)opcode:x2} at address {_pc - 1} " +
                        "(GPDLexec.cpp:6610).");
            }
        }
        return _interpStatus;
    }

    /// <summary>
    /// Executes one sub-opcode. Returns non-null when the interpreter must yield — only
    /// <c>SUBOP_SAY</c> and <c>SUBOP_LISTEN</c> do that.
    /// </summary>
    private GpdlState? ExecuteSubOp(SubOp op)
    {
        string s1, s2, s3, s4;
        int i1, i2;

        switch (op)
        {
            // ---- stack shuffling
            case SubOp.SUBOP_NOOP:
                break;
            case SubOp.SUBOP_POP:
                PopSp();
                break;
            case SubOp.SUBOP_DUP:
                s1 = PopSp();
                PushSp(s1);
                PushSp(s1);
                break;
            case SubOp.SUBOP_SWAP:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(s1);
                PushSp(s2);
                break;
            case SubOp.SUBOP_OVER:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(s2);
                PushSp(s1);
                PushSp(s2);
                break;

            // ---- literals
            case SubOp.SUBOP_FALSE:
                PushSp(False);
                break;
            case SubOp.SUBOP_ONE:
                PushSp(True);
                break;
            case SubOp.SUBOP_ZERO:
                PushSp("0");
                break;

            // ---- string operations
            case SubOp.SUBOP_PLUS:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(s2 + s1);
                break;
            case SubOp.SUBOP_LENGTH:
                PushUInteger((uint)PopSp().Length);
                break;
            case SubOp.SUBOP_MIDDLE:
                i2 = PopInteger();      // count
                i1 = PopInteger();      // first index
                PushSp(MfcString.Mid(PopSp(), i1, i2));
                break;
            case SubOp.SUBOP_UpCase:
                PushSp(MfcString.MakeUpper(PopSp()));
                break;
            case SubOp.SUBOP_DownCase:
                PushSp(MfcString.MakeLower(PopSp()));
                break;
            case SubOp.SUBOP_Capitalize:
                PushSp(Capitalize(PopSp()));
                break;

            // ---- string relations (byte order, see MfcString.CompareBytes)
            case SubOp.SUBOP_ISEQUAL:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(string.Equals(s1, s2, StringComparison.Ordinal) ? True : False);
                break;
            case SubOp.SUBOP_NOTEQUAL:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(string.Equals(s1, s2, StringComparison.Ordinal) ? False : True);
                break;
            case SubOp.SUBOP_LESS:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(MfcString.CompareBytes(s2, s1) < 0 ? True : False);
                break;
            case SubOp.SUBOP_LESSEQUAL:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(MfcString.CompareBytes(s2, s1) <= 0 ? True : False);
                break;
            case SubOp.SUBOP_GREATER:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(MfcString.CompareBytes(s2, s1) > 0 ? True : False);
                break;
            case SubOp.SUBOP_GREATEREQUAL:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(MfcString.CompareBytes(s2, s1) >= 0 ? True : False);
                break;

            // ---- logical
            case SubOp.SUBOP_NOT:
                // Operates in place on the top of stack and pops nothing (GPDLexec.cpp:5008).
                if (_sp >= StackSize)
                {
                    _interpStatus = GpdlState.GPDL_UNDER_SP;
                    break;
                }
                _dataStack[_sp] = _dataStack[_sp].Length == 0 ? True : False;
                break;
            case SubOp.SUBOP_LAND:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(s1.Length != 0 && s2.Length != 0 ? True : False);
                break;
            case SubOp.SUBOP_LOR:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(s1.Length != 0 || s2.Length != 0 ? True : False);
                break;

            // ---- hardware integer arithmetic (the '#' operators)
            case SubOp.SUBOP_nPLUS:
                i1 = PopInteger();
                i2 = PopInteger();
                PushInteger(unchecked(i2 + i1));
                break;
            case SubOp.SUBOP_nMINUS:
                i1 = PopInteger();
                i2 = PopInteger();
                PushInteger(unchecked(i2 - i1));
                break;
            case SubOp.SUBOP_nTIMES:
                i1 = PopInteger();
                i2 = PopInteger();
                PushInteger(unchecked(i2 * i1));
                break;
            case SubOp.SUBOP_nSLASH:
                i1 = PopInteger();
                i2 = PopInteger();
                if (i1 == 0)
                {
                    // Divide by zero is a logged warning and a divisor of 1, not a fault
                    // (GPDLexec.cpp:5032). Note this differs from $DIV, which yields "999999".
                    _host.DebugWrite("Attempt to divide (/#) by zero ");
                    i1 = 1;
                }
                PushInteger(unchecked(i2 / i1));
                break;
            case SubOp.SUBOP_nPERCENT:
                i1 = PopInteger();
                i2 = PopInteger();
                // No zero guard on '%#' (GPDLexec.cpp:5017) -- the C++ takes a hardware exception.
                if (i1 == 0) { _interpStatus = GpdlState.GPDL_ILLPARAM; break; }
                PushInteger(unchecked(i2 % i1));
                break;
            case SubOp.SUBOP_nNEGATE:
                PushInteger(unchecked(-PopInteger()));
                break;
            case SubOp.SUBOP_nAND:
                i1 = PopInteger();
                i2 = PopInteger();
                PushInteger(i2 & i1);
                break;
            case SubOp.SUBOP_nOR:
                i1 = PopInteger();
                i2 = PopInteger();
                PushInteger(i2 | i1);
                break;
            case SubOp.SUBOP_nXOR:
                i1 = PopInteger();
                i2 = PopInteger();
                PushInteger(i2 ^ i1);
                break;
            case SubOp.SUBOP_nISEQUAL:
                i1 = PopInteger();
                i2 = PopInteger();
                PushSp(i1 == i2 ? True : False);
                break;
            case SubOp.SUBOP_nNOTEQUAL:
                i1 = PopInteger();
                i2 = PopInteger();
                PushSp(i1 != i2 ? True : False);
                break;
            case SubOp.SUBOP_nLESS:
                i1 = PopInteger();
                i2 = PopInteger();
                PushSp(i2 < i1 ? True : False);
                break;
            case SubOp.SUBOP_nLESSEQUAL:
                i1 = PopInteger();
                i2 = PopInteger();
                PushSp(i2 <= i1 ? True : False);
                break;
            case SubOp.SUBOP_nGREATER:
                i1 = PopInteger();
                i2 = PopInteger();
                PushSp(i2 > i1 ? True : False);
                break;
            case SubOp.SUBOP_nGREATEREQUAL:
                i1 = PopInteger();
                i2 = PopInteger();
                PushSp(i2 >= i1 ? True : False);
                break;
            case SubOp.SUBOP_FORCENUMERIC:
                PushInteger(PopInteger());
                break;
            case SubOp.SUBOP_NUMERIC:
                PushSp(PopIsNumeric() ? True : False);
                break;

            // ---- arbitrary-precision decimal-string arithmetic (the $ functions)
            case SubOp.SUBOP_iPLUS:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(GpdlLongArithmetic.Add(s1, s2));
                break;
            case SubOp.SUBOP_iMINUS:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(GpdlLongArithmetic.Subtract(s2, s1));
                break;
            case SubOp.SUBOP_iTIMES:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(GpdlLongArithmetic.Multiply(s1, s2));
                break;
            case SubOp.SUBOP_iDIV:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(GpdlLongArithmetic.Divide(s2, s1).Quotient);
                break;
            case SubOp.SUBOP_iMOD:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(GpdlLongArithmetic.Divide(s2, s1).Remainder);
                break;
            case SubOp.SUBOP_iEQUAL:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(GpdlLongArithmetic.Compare(s1, s2) == 0 ? True : False);
                break;
            case SubOp.SUBOP_iGREATER:
                s1 = PopSp();
                s2 = PopSp();
                // right - left < 0  =>  left > right. The operand order does the inverting.
                PushSp(GpdlLongArithmetic.Compare(s1, s2) < 0 ? True : False);
                break;
            case SubOp.SUBOP_iLESS:
                s1 = PopSp();
                s2 = PopSp();
                PushSp(GpdlLongArithmetic.Compare(s1, s2) > 0 ? True : False);
                break;

            // ---- delimited strings: the first character of the string IS the delimiter
            case SubOp.SUBOP_DelimitedStringCount:
                {
                    int count = 0;
                    s1 = PopSp();
                    if (s1.Length != 0)
                    {
                        char delimiter = s1[0];
                        int col = 1;
                        while (col > 0 && col < s1.Length)
                        {
                            col = MfcString.Find(s1, delimiter, col);
                            count++;
                            col++;
                        }
                    }
                    PushInteger(count);
                    break;
                }
            case SubOp.SUBOP_DelimitedStringSubstring:
                {
                    i1 = PopInteger();
                    s1 = PopSp();
                    s2 = string.Empty;
                    int len = s1.Length;
                    int start = 0;
                    if (len > 0 && i1 >= 0)
                    {
                        char delimiter = s1[0];
                        while (start >= 0 && start < len)
                        {
                            if (i1 == 0)
                            {
                                int end = MfcString.Find(s1, delimiter, start + 1);
                                if (end < 0) { end = len; }
                                s2 = MfcString.Mid(s1, start + 1, end - start - 1);
                                break;
                            }
                            i1--;
                            start = MfcString.Find(s1, delimiter, start + 1);
                        }
                    }
                    PushSp(s2);
                    break;
                }
            case SubOp.SUBOP_DelimitedStringHead:
                {
                    s1 = PopSp();
                    s2 = string.Empty;
                    if (s1.Length > 0)
                    {
                        int end = MfcString.Find(s1, s1[0], 1);
                        if (end < 0) { end = s1.Length; }
                        s2 = MfcString.Mid(s1, 1, end - 1);
                    }
                    PushSp(s2);
                    break;
                }
            case SubOp.SUBOP_DelimitedStringTail:
                {
                    s1 = PopSp();
                    s2 = string.Empty;
                    if (s1.Length > 0)
                    {
                        int end = MfcString.Find(s1, s1[0], 1);
                        if (end < 0) { end = s1.Length; }
                        s2 = MfcString.Right(s1, s1.Length - end);
                    }
                    PushSp(s2);
                    break;
                }
            case SubOp.SUBOP_DelimitedStringAdd:
                {
                    s3 = PopSp();   // possible delimiter
                    s2 = PopSp();   // new head
                    s1 = PopSp();   // original string
                    if (s1.Length == 0)
                    {
                        // '#' is the fallback delimiter when neither string supplies one.
                        s4 = s3.Length == 0 ? "#" : s3[0].ToString();
                    }
                    else
                    {
                        s4 = s1[0].ToString();
                    }
                    PushSp(s4 + s2 + s1);
                    break;
                }

            // ---- host services
            case SubOp.SUBOP_LISTENTEXT:
                PushSp(_host.HasDiscourse ? _host.ListenText : string.Empty);
                break;
            case SubOp.SUBOP_LISTEN:
                if (!_host.HasDiscourse)
                {
                    PushSp(string.Empty);
                    break;
                }
                return GpdlState.GPDL_WAIT_INPUT;
            case SubOp.SUBOP_SAY:
                // With no discourse event the argument is deliberately left on the stack; see
                // IGpdlHost.HasDiscourse.
                if (!_host.HasDiscourse) { break; }
                _host.Say(PopSp());
                PushSp(False);
                return GpdlState.GPDL_WAIT_ACK;
            case SubOp.SUBOP_SET_GLOBAL_ASL:
            case SubOp.SUBOP_SET_PARTY_ASL:
                {
                    // Value first, then key: GPDL pushes arguments left to right, so the last one
                    // is on top. The value is pushed back, so the expression yields what was set.
                    string value = PopSp();
                    string key = PopSp();
                    _host.SetAsl(ScopeOf(op), key, value);
                    PushSp(value);
                    break;
                }
            case SubOp.SUBOP_GET_GLOBAL_ASL:
            case SubOp.SUBOP_GET_PARTY_ASL:
                PushSp(_host.GetAsl(ScopeOf(op), PopSp()));
                break;
            case SubOp.SUBOP_IF_PARTY_ASL:
                PushSp(_host.HasAsl(GpdlAslScope.Party, PopSp()) ? True : False);
                break;
            case SubOp.SUBOP_DELETE_PARTY_ASL:
                // Pushes false, not the removed value -- the reference's own comment is "Must
                // supply a result" (GPDLexec.cpp:3383), so the push exists to balance the stack
                // rather than to say anything. A script testing the result of a delete always
                // sees false, whether or not the key was there.
                _host.DeleteAsl(GpdlAslScope.Party, PopSp());
                PushSp(False);
                break;
            case SubOp.SUBOP_GREP:
                {
                    string text = PopSp();
                    string pattern = PopSp();
                    PushSp(_host.Grep(pattern, text) ? True : False);
                    break;
                }
            case SubOp.SUBOP_Wiggle:
                PushSp(_host.Wiggle(PopInteger()));
                break;
            case SubOp.SUBOP_RANDOM:
                i1 = PopInteger();
                if (i1 <= 0)
                {
                    _interpStatus = GpdlState.GPDL_ILLPARAM;
                    break;
                }
                PushUInteger((uint)_host.Random(i1));
                break;
            case SubOp.SUBOP_DEBUG:
                s1 = PopSp();
                _host.Debug($"$DEBUG({s1})");
                PushSp(s1);
                break;
            case SubOp.SUBOP_DebugWrite:
                // Reads the top of stack without popping (GPDLexec.cpp:3355).
                if (_sp >= StackSize)
                {
                    _interpStatus = GpdlState.GPDL_UNDER_SP;
                    break;
                }
                _host.DebugWrite(_dataStack[_sp] + "\n");
                break;

            default:
                throw new NotSupportedException(BuildUnportedMessage(op));
        }
        return null;
    }

    /// <summary>
    /// <c>SUBOP_Capitalize</c> (GPDLexec.cpp:6058): lower-case everything, then upper-case the first
    /// character of each space-separated word. Only <c>' '</c> starts a new word — a tab or newline
    /// does not.
    /// </summary>
    private static string Capitalize(string input)
    {
        string upper = MfcString.MakeUpper(input);
        char[] lower = MfcString.MakeLower(input).ToCharArray();
        bool atWordStart = true;
        for (int j = 0; j < lower.Length; j++)
        {
            if (atWordStart)
            {
                lower[j] = upper[j];
                atWordStart = false;
            }
            if (lower[j] == ' ') { atWordStart = true; }
        }
        return new string(lower);
    }

    /// <summary>Which attribute store a sub-opcode reaches for.</summary>
    private static GpdlAslScope ScopeOf(SubOp op) => op switch
    {
        SubOp.SUBOP_SET_PARTY_ASL or SubOp.SUBOP_GET_PARTY_ASL
            or SubOp.SUBOP_IF_PARTY_ASL or SubOp.SUBOP_DELETE_PARTY_ASL => GpdlAslScope.Party,
        _ => GpdlAslScope.Global,
    };

    private static string BuildUnportedMessage(SubOp op)
    {
        var sb = new StringBuilder();
        sb.Append("GPDL sub-opcode ").Append(op).Append(" (0x").Append(((uint)op).ToString("x3", CultureInfo.InvariantCulture))
          .Append(") is not ported. ");
        var fn = GpdlSystemFunctions.FindBySubOp(op);
        if (fn is not null)
        {
            sb.Append("It implements ").Append(fn.Name).Append(", which needs game state that this ")
              .Append("phase of the port does not have (see the m_interpret switch in ")
              .Append("src/Shared/GPDLexec.cpp:2391 for the reference implementation).");
        }
        else
        {
            sb.Append("It has no entry in systemfunctions[], so it is compiler-internal; if a ")
              .Append("compiled program reaches it, the code generator emitted something this VM ")
              .Append("does not know about.");
        }
        return sb.ToString();
    }

    private string FormatTraceLine(uint address, uint bincode)
    {
        var sb = new StringBuilder();
        sb.Append(address.ToString("x6", CultureInfo.InvariantCulture))
          .Append(' ')
          .Append(bincode.ToString("x8", CultureInfo.InvariantCulture))
          .Append(" [");
        for (int i = _sp; i < StackSize; i++)
        {
            if (i != _sp) { sb.Append('|'); }
            sb.Append(_dataStack[i]);
        }
        sb.Append(']');
        return sb.ToString();
    }
}
