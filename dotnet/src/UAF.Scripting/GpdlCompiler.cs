using System.Globalization;

namespace UAF.Scripting;

/// <summary>
/// Port of <c>GPDLCOMP</c> (GPDLcomp.cpp) — the GPDL source-to-bytecode compiler.
/// </summary>
/// <remarks>
/// <para>
/// A single pass, no intermediate tree: statements emit code words directly into
/// <see cref="CodeSegment"/> and forward jumps are back-patched by address. The output is three
/// independent segments — code, the global pool (string constants and global variables), and the
/// public-function index — written in that order by <see cref="GpdlBinaryWriter"/>.
/// </para>
/// <para>
/// Structural things that must not be "cleaned up":
/// </para>
/// <list type="bullet">
/// <item><description>
/// Every list in the symbol table is <b>prepended</b> to (GPDLcomp.cpp:1070, :1117, :1127), so
/// iteration order is reverse-declaration order. The public-function index is written in that
/// order, so the file layout depends on it.
/// </description></item>
/// <item><description>
/// Formal parameters are therefore stored last-declared-first, and
/// <see cref="DefineFormalParameters"/> numbers them from that end. That is deliberate, not a bug:
/// actual parameters are pushed left to right onto a downward-growing stack and the frame pointer
/// lands on the last one, so frame offset 0 is the rightmost parameter either way.
/// </description></item>
/// <item><description>
/// Address 0 always holds <c>SUBOP_NOOP</c> (GPDLcomp.cpp:3543) so that no function can start
/// there — 0 is the "function not found" sentinel in <c>GPDL::INDEX::lookup</c>.
/// </description></item>
/// </list>
/// </remarks>
public sealed class GpdlCompiler
{
    /// <summary><c>MAXSTACK</c>, GPDLcomp.h:7 — the expression operator stack depth.</summary>
    private const int MaxStack = 20;

    /// <summary><c>ATTRIBUTE_PUBLIC</c>, GPDLcomp.h:11.</summary>
    private const uint AttributePublic = 1;

    // ---------------------------------------------------------------- code segment

    /// <summary>Port of <c>CODE</c> (GPDLcomp.cpp:757): a growable array of 32-bit code words.</summary>
    public sealed class CodeSegment
    {
        private uint[] _code = [];
        private int _here;

        public int Here => _here;

        public uint Peek(int addr) => _code[addr];

        public void Poke(int addr, uint value) => _code[addr] = value;

        public void Comma(uint word)
        {
            if (_here >= _code.Length)
            {
                // The growth schedule (m_allocated*3/2+1) cannot affect output, so the exact
                // formula is not reproduced.
                Array.Resize(ref _code, Math.Max(16, _code.Length * 2));
            }
            _code[_here++] = word;
        }

        public void Clear()
        {
            _code = [];
            _here = 0;
        }

        public uint[] ToArray() => _code[.._here];
    }

    // ---------------------------------------------------------------- symbol table

    /// <summary>Port of <c>DEFINITION</c> (GPDLcomp.cpp:874).</summary>
    public sealed class Definition
    {
        public Definition? Next;
        public string Name = string.Empty;

        public bool IsPrototype;
        public bool IsFunction;
        public bool IsFramePointerRelative;
        public bool IsLocalVariable;
        public bool IsSystem;
        public bool IsPublic;
        public bool IsGlobalVariable;

        /// <summary>Head of the formal parameter list — the <b>last</b> parameter declared.</summary>
        public Definition? FormalParams;

        /// <summary>Head of the local variable list — the last one declared.</summary>
        public Definition? LocalVariables;

        public string DefaultValue = string.Empty;

        /// <summary>
        /// Code address for a function, frame offset for a parameter or local, global-pool index
        /// for a global variable, or the pre-shifted opcode for a system function. Initialised to
        /// 0xffffffff, which is why <c>findUserFunc</c> can distinguish "never placed".
        /// </summary>
        public uint IntValue = 0xffffffff;

        public GpdlContexts RequiredContexts;

        /// <summary>Return type followed by parameter types; see <see cref="GpdlSystemFunction"/>.</summary>
        public int[] Types = new int[GpdlSystemFunctions.MaxFuncParameters + 1];

        public Definition AddFormalParam(string name)
        {
            var p = new Definition { Name = name, Next = FormalParams };
            FormalParams = p;
            return p;
        }

        public Definition AddLocalVariable(string name)
        {
            var v = new Definition { Name = name, Next = LocalVariables };
            LocalVariables = v;
            return v;
        }

        /// <summary>
        /// <c>checkProtoParam</c> (GPDLcomp.cpp:1085): non-zero when the zero-based nth parameter
        /// does <b>not</b> exist.
        /// </summary>
        public int CheckProtoParam(int n)
        {
            var param = FormalParams;
            for (; n >= 0; n--)
            {
                if (param is null) { return 1; }
                param = param.Next;
            }
            return 0;
        }

        public int NumParam()
        {
            int result = 0;
            for (var p = FormalParams; p is not null; p = p.Next) { result++; }
            return result;
        }

        public int NumLocals()
        {
            int result = 0;
            for (var v = LocalVariables; v is not null; v = v.Next) { result++; }
            return result;
        }

        /// <summary><c>publicUserFunc</c> — a user-defined function declared <c>$PUBLIC</c>.</summary>
        public bool IsPublicUserFunc => IsFunction && !IsSystem && IsPublic;
    }

    /// <summary>
    /// Port of <c>DICTIONARY</c> (GPDLcomp.cpp:927). Named Scope here only to avoid colliding with
    /// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>.
    /// </summary>
    public sealed class Scope(string name)
    {
        public string Name { get; } = name;
        public Scope? Parent;
        public Scope? NextSibling;
        public Scope? Offspring;
        public Definition? Definitions;

        public Definition AddFunction(string name)
        {
            var def = new Definition { Name = name, Next = Definitions, IsFunction = true };
            Definitions = def;
            return def;
        }

        public Definition AddDefinition(string name)
        {
            var def = new Definition { Name = name, Next = Definitions };
            Definitions = def;
            return def;
        }

        public Scope AddScope(string name)
        {
            var s = new Scope(name) { NextSibling = Offspring, Parent = this };
            Offspring = s;
            return s;
        }

        /// <summary>Searches this scope only.</summary>
        public Definition? Lookup(string name)
        {
            for (var def = Definitions; def is not null; def = def.Next)
            {
                if (string.Equals(def.Name, name, StringComparison.Ordinal)) { return def; }
            }
            return null;
        }

        /// <summary><c>findDictionary</c> (GPDLcomp.cpp:1142): self and ancestors' offspring.</summary>
        public Scope? FindScope(string name)
        {
            for (var parent = this; parent is not null; parent = parent.Parent)
            {
                for (var kid = parent.Offspring; kid is not null; kid = kid.NextSibling)
                {
                    if (string.Equals(kid.Name, name, StringComparison.Ordinal)) { return kid; }
                }
            }
            return null;
        }

        public int CountPublicFunctions()
        {
            int count = 0;
            for (var def = Definitions; def is not null; def = def.Next)
            {
                if (def.IsPublicUserFunc) { count++; }
            }
            for (var kid = Offspring; kid is not null; kid = kid.NextSibling)
            {
                count += kid.CountPublicFunctions();
            }
            return count;
        }

        /// <summary>
        /// <c>m_writeFunctionIndex</c> (GPDLcomp.cpp:1177): flattens public functions to
        /// <c>outer@inner@name</c>, this scope's definitions first, then each offspring in turn.
        /// </summary>
        public void CollectPublicFunctions(string prefix, List<(string Name, uint Address)> into)
        {
            for (var def = Definitions; def is not null; def = def.Next)
            {
                if (def.IsPublicUserFunc) { into.Add((prefix + def.Name, def.IntValue)); }
            }
            for (var kid = Offspring; kid is not null; kid = kid.NextSibling)
            {
                kid.CollectPublicFunctions(prefix + kid.Name + '@', into);
            }
        }

        /// <summary>
        /// <c>findUserFunc</c> (GPDLcomp.cpp:1258): the user function starting at an address, for
        /// the listing writer. Empty string when none.
        /// </summary>
        public string FindUserFunc(uint address)
        {
            for (var def = Definitions; def is not null; def = def.Next)
            {
                if (def.IntValue == address && def.IsFunction && !def.IsSystem) { return def.Name; }
            }
            for (var kid = Offspring; kid is not null; kid = kid.NextSibling)
            {
                string result = kid.FindUserFunc(address);
                if (result.Length != 0) { return result; }
            }
            return string.Empty;
        }
    }

    /// <summary>Port of <c>GLOBAL</c> (GPDLcomp.cpp:959): a pooled constant or global variable.</summary>
    public readonly record struct GlobalEntry(char Type, string Value);

    /// <summary>
    /// Port of <c>GLOBALS</c> (GPDLcomp.cpp:972): one flat pool holding both string constants
    /// ('C') and global variables ('V'). <b>Index 0 is never used</b> (GPDLcomp.cpp:1791) — a
    /// reference to global 0 would be indistinguishable from an unresolved one.
    /// </summary>
    public sealed class GlobalPool
    {
        private readonly List<GlobalEntry> _values = [new GlobalEntry(' ', string.Empty)];

        /// <summary><c>m_used</c> — the count including the unused zeroth entry.</summary>
        public int Used => _values.Count;

        public string GetValue(int index) =>
            index < _values.Count ? _values[index].Value : "Undefined";

        public char GetType(int index) => index < _values.Count ? _values[index].Type : ' ';

        // The original's SearchConstant/SearchVariable (GPDLcomp.cpp:1869, :1881) advance with
        // `result = result++`, which leaves the pointer unchanged -- so as literally written they
        // spin forever the moment the pool holds two entries and the first does not match. The
        // shipped Release build cannot have behaved that way (the compiler works), so MSVC's
        // optimiser must have kept the increment and dropped the self-assignment. A working
        // forward scan is therefore the only behaviour consistent with a compiler that runs, and it
        // is what is implemented here: the pool de-duplicates.
        //
        // If the Windows oracle ever shows a talk.bin with duplicate pool entries, this is the
        // first place to look -- it would mean MSVC produced the non-advancing form and every
        // InsertConstant call after the first added a fresh slot.
        private int Search(char type, string value)
        {
            for (int i = 1; i < _values.Count; i++)
            {
                if (_values[i].Type == type && string.Equals(_values[i].Value, value, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Interns a string constant and returns its pool index.</summary>
        public int InsertConstant(string value)
        {
            int found = Search('C', value);
            if (found >= 0) { return found; }
            _values.Add(new GlobalEntry('C', value));
            return _values.Count - 1;
        }

        /// <summary>Interns a global variable slot and returns its pool index.</summary>
        public int InsertVariable(string name)
        {
            int found = Search('V', name);
            if (found >= 0) { return found; }
            _values.Add(new GlobalEntry('V', name));
            return _values.Count - 1;
        }

        /// <summary>
        /// What <c>GLOBALS::write</c> (GPDLcomp.cpp:1905) puts on disk: constants keep their text,
        /// variables are written as the empty string (their names are compile-time only, and the VM
        /// initialises the slot at run time).
        /// </summary>
        public IEnumerable<string> SerializedValues()
        {
            for (int i = 0; i < _values.Count; i++)
            {
                yield return _values[i].Type == 'V' ? string.Empty : _values[i].Value;
            }
        }
    }

    // ---------------------------------------------------------------- compiler state

    private readonly CodeSegment _code = new();
    private readonly Scope _systemScope = new("system");
    private Scope _root;
    private Scope _current;
    private Scope _context;
    private Definition? _activeFunction;
    private GlobalPool _globals = new();
    private GpdlLexer _lexer = new(Array.Empty<string>());

    /// <summary>Head of the pending <c>$BREAK</c> chain; 0 means empty inside a loop.</summary>
    private uint _breaklist = 0xffffffff;

    /// <summary>Head of the pending <c>$CONTINUE</c> chain.</summary>
    private uint _continuelist = 0xffffffff;

    private bool _compilingScript;

    /// <summary>
    /// <c>AllFunctionsArePublic</c> (GPDLcomp.cpp:1995). In the C++ this is a file-scope static
    /// that <c>InitializeGPDLcompiler</c> never clears, so one <c>#PUBLIC</c> pragma makes every
    /// function of every <i>subsequent</i> compilation public too. Instance-scoped here, which is
    /// the same thing for the single global <c>GPDLcomp</c> object the editor uses, and does not
    /// leak between processes.
    /// </summary>
    private bool _allFunctionsArePublic;

    /// <summary>
    /// <c>m_availableContexts</c> — the contexts the calling hook provides. Defaults to "all", as
    /// the C++ constructor does (GPDLcomp.cpp:2311).
    /// </summary>
    public GpdlContexts AvailableContexts { get; set; } = (GpdlContexts)0xffffffff;

    /// <summary>Diagnostics accumulated during the last compile.</summary>
    public IReadOnlyList<string> Errors => _lexer.Errors;

    public GpdlCompiler()
    {
        _root = new Scope("root") { Parent = _systemScope };
        _current = _root;
        _context = _root;
    }

    /// <summary>The compiled code words, valid after a successful <see cref="Compile"/>.</summary>
    public uint[] Code => _code.ToArray();

    /// <summary>The global pool, valid after a successful <see cref="Compile"/>.</summary>
    public GlobalPool Globals => _globals;

    /// <summary>The root scope, for listing and index generation.</summary>
    public Scope Root => _root;

    private void Initialize()
    {
        _compilingScript = false;
        _code.Clear();
        _activeFunction = null;
        _root = new Scope("root") { Parent = _systemScope };
        _globals = new GlobalPool();
        _breaklist = 0xffffffff;
        _continuelist = 0xffffffff;
        _current = _root;
        _context = _root;
    }

    // ---------------------------------------------------------------- lexer access

    /// <summary>
    /// <c>GPDLCOMP::GetToken</c> (GPDLcomp.cpp:2324): pragmas are consumed here, never returned to
    /// the parser. <c>#PUBLIC</c> is the only one recognised; any other pragma is silently dropped.
    /// </summary>
    private GpdlTokenType GetToken()
    {
        GpdlTokenType result;
        while ((result = _lexer.NextToken()) == GpdlTokenType.TKN_PRAGMA)
        {
            if (string.Equals(_lexer.Token, "#PUBLIC", StringComparison.Ordinal))
            {
                _allFunctionsArePublic = true;
            }
        }
        return result;
    }

    private void Error(string msg) => _lexer.Error(msg);

    // ---------------------------------------------------------------- symbol lookup

    /// <summary>
    /// <c>DICTIONARY::localLookup</c> (GPDLcomp.cpp:1707): walks the scope chain, and when it
    /// reaches the system scope installs the system function on demand.
    /// </summary>
    /// <remarks>
    /// The original carries a <c>local</c> flag meant to hide a parent's frame-relative names from
    /// an inner scope, but the line that would clear it is commented out (GPDLcomp.cpp:1731), so
    /// the filter never fires. Not reproduced, because reproducing dead code is misleading — but
    /// the consequence is real: an inner function <i>can</i> see an outer function's parameters by
    /// name, and will compile a frame-relative fetch against its own frame.
    /// </remarks>
    private Definition? LocalLookup(Scope from, string vname)
    {
        int at = vname.IndexOf('@', StringComparison.Ordinal);
        if (at < 0)
        {
            for (var scope = from; scope is not null; scope = scope.Parent)
            {
                var def = scope.Lookup(vname);
                if (def is not null) { return def; }

                if (ReferenceEquals(scope, _systemScope))
                {
                    var sys = GpdlSystemFunctions.Find(vname);
                    if (sys is null) { return null; }
                    return InstallSystemFunction(vname, sys);
                }
            }
            return null;
        }

        string outer = MfcString.Left(vname, at);
        var target = from.FindScope(outer);
        if (target is null) { return null; }
        return LocalLookup(target, MfcString.Right(vname, vname.Length - at - 1));
    }

    private Definition InstallSystemFunction(string name, GpdlSystemFunction sys)
    {
        var def = _systemScope.AddFunction(name);
        def.IntValue = GpdlCode.ShiftedSubOp | (uint)sys.SubOp;
        def.IsSystem = true;
        def.RequiredContexts = GpdlSystemFunctions.RequiredContexts(sys.SubOp);

        // Formal parameter names are "a", "aa", "aaa", ... -- they exist only so that
        // compileActualParameters can count them.
        string pname = "a";
        for (int j = 0; j < sys.ParameterCount; j++)
        {
            def.AddFormalParam(pname);
            pname += "a";
        }

        // Only six of the eight type slots are copied (GPDLcomp.cpp:1766). Parameters 6 and 7 of a
        // seven-parameter system function therefore stay untyped and skip the ACTOR check.
        for (int j = 0; j < 6; j++) { def.Types[j] = sys.Types[j]; }
        return def;
    }

    // ---------------------------------------------------------------- code emission helpers

    private void CompileFalse() => _code.Comma(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_FALSE);

    private void CompilePop() => _code.Comma(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_POP);

    /// <summary>Emits an unresolved jump and returns the address of the cell to patch.</summary>
    private int CompileJump()
    {
        int address = _code.Here;
        _code.Comma((uint)BinOp.BINOP_JUMP << 24);
        return address;
    }

    private void CompileJumpTo(uint destination) =>
        _code.Comma(((uint)BinOp.BINOP_JUMP << 24) | destination);

    private int CompileJumpFalse()
    {
        int address = _code.Here;
        _code.Comma((uint)BinOp.BINOP_JUMPFALSE << 24);
        return address;
    }

    /// <summary>
    /// <c>resolveJump</c> (GPDLcomp.cpp:2156): ORs the current address into a previously emitted
    /// jump. It ORs rather than assigns, which is what lets a cell be both a jump target and a
    /// break-list link (see <see cref="CompileWhile"/>).
    /// </summary>
    private void ResolveJump(int address) =>
        _code.Poke(address, _code.Peek(address) | (uint)_code.Here);

    private void AddToBreakList()
    {
        _code.Poke(_code.Here - 1, _code.Peek(_code.Here - 1) | _breaklist);
        _breaklist = (uint)(_code.Here - 1);
    }

    private void AddToContinueList()
    {
        _code.Poke(_code.Here - 1, _code.Peek(_code.Here - 1) | _continuelist);
        _continuelist = (uint)(_code.Here - 1);
    }

    private void ResolveBreaks(uint addr)
    {
        while (_breaklist != 0)
        {
            int temp = (int)_breaklist;
            _breaklist = _code.Peek(temp) & 0xffffff;
            _code.Poke(temp, (_code.Peek(temp) & 0xff000000) | addr);
        }
    }

    private void ResolveContinues(uint addr)
    {
        while (_continuelist != 0)
        {
            int temp = (int)_continuelist;
            _continuelist = _code.Peek(temp) & 0xffffff;
            _code.Poke(temp, (_code.Peek(temp) & 0xff000000) | addr);
        }
    }

    private void ReferenceGlobal(int index) =>
        _code.Comma(((uint)BinOp.BINOP_ReferenceGLOBAL << 24) | (uint)index);

    // ---------------------------------------------------------------- declarations

    private int CompileVariableReference(Definition def)
    {
        if (def.IsFramePointerRelative)
        {
            _code.Comma(((uint)BinOp.BINOP_FETCH_FP << 24) | (def.IntValue & 0xffffff));
            return 0;
        }
        if (def.IsGlobalVariable)
        {
            _code.Comma(((uint)BinOp.BINOP_ReferenceGLOBAL << 24) | (def.IntValue & 0xffffff));
            return 0;
        }
        Error("Reference to variable that is not Frame-Relative");
        return 1;
    }

    private int CompileVariableStore(Definition def)
    {
        if (def.IsFramePointerRelative)
        {
            _code.Comma(((uint)BinOp.BINOP_STORE_FP << 24) | (def.IntValue & 0xffffff));
            return 0;
        }
        if (def.IsGlobalVariable)
        {
            _code.Comma(((uint)BinOp.BINOP_ReferenceGLOBAL << 24)
                        | (def.IntValue & 0xffffff) | GpdlCode.GlobalStoreBit);
            return 0;
        }
        Error("Reference to variable that is not Frame-Relative");
        return 1;
    }

    private int CompileForceNumeric()
    {
        _code.Comma(((uint)BinOp.BINOP_SUBOP << 24) | (uint)SubOp.SUBOP_FORCENUMERIC);
        return 0;
    }

    /// <summary>
    /// <c>compileVariableDecl</c> (GPDLcomp.cpp:2443). Outside a function <c>$VAR</c> makes a
    /// global-pool slot; inside one it bumps the count in the function's <c>BINOP_LOCALS</c> cell,
    /// which is why locals must precede all executable code.
    /// </summary>
    private int CompileVariableDecl()
    {
        if (!string.Equals(_lexer.Token, "$VAR", StringComparison.Ordinal))
        {
            Error("Internal compileVariableDecl error");
            return 1;
        }

        if (_activeFunction is null)
        {
            if (GetToken() != GpdlTokenType.TKN_NAME)
            {
                Error("Expected a variable name following '$VAR'");
                return 1;
            }
            var globalVar = _context.AddDefinition(_lexer.Token);
            globalVar.IsGlobalVariable = true;
            globalVar.IntValue = (uint)_globals.InsertVariable(globalVar.Name);
            return 0;
        }

        if (_code.Here == (int)_activeFunction.IntValue + 1)
        {
            // First local in this function: plant the allocation cell, count zero for now.
            _code.Comma((uint)BinOp.BINOP_LOCALS << 24);
        }
        if (_code.Here != (int)_activeFunction.IntValue + 2
            || GpdlCode.OpOf(_code.Peek(_code.Here - 1)) != BinOp.BINOP_LOCALS)
        {
            Error("Variables must be declared before any executable code in a function");
            return 1;
        }
        _code.Poke(_code.Here - 1, _code.Peek(_code.Here - 1) + 1);

        if (GetToken() != GpdlTokenType.TKN_NAME)
        {
            Error("Expected a variable name following '$VAR'");
            return 1;
        }
        var newVar = _context.AddDefinition(_lexer.Token);
        _activeFunction.AddLocalVariable(_lexer.Token);

        // The count is masked to 10 bits (GPDLcomp.cpp:2499), so the 1024th local silently
        // aliases offset 0 -- i.e. the first actual parameter.
        int numvar = (int)(_code.Peek(_code.Here - 1) & 0x3ff);
        newVar.IntValue = unchecked((uint)(-numvar));
        newVar.IsFramePointerRelative = true;
        newVar.IsLocalVariable = true;
        return 0;
    }

    // ---------------------------------------------------------------- expressions

    private int CompileAtomicElement()
    {
        var tokenType = GetToken();
        var unaryOperator = SubOp.SUBOP_ILLEGAL;

        // Only one unary operator is allowed here; a second '-#' is a syntax error.
        if (tokenType == GpdlTokenType.TKN_nMINUS)
        {
            unaryOperator = SubOp.SUBOP_nNEGATE;
            tokenType = GetToken();
        }

        if (tokenType == GpdlTokenType.TKN_OPENPAREN)
        {
            int result = CompileExpression();
            if (result != 0) { return result; }
            if (GetToken() != GpdlTokenType.TKN_CLOSEPAREN)
            {
                Error("Expected close parenthesis");
                return 1;
            }
        }
        else if (tokenType == GpdlTokenType.TKN_STRING)
        {
            // Adjacent string literals concatenate, as in C.
            string token = string.Empty;
            while (tokenType == GpdlTokenType.TKN_STRING)
            {
                token += _lexer.Token;
                tokenType = GetToken();
            }
            _lexer.BackspaceToken();
            ReferenceGlobal(_globals.InsertConstant(token));
        }
        else if (tokenType == GpdlTokenType.TKN_INTEGER)
        {
            // Integers become string constants -- "all variables are strings"
            // (src/GPDL/language.txt:1).
            ReferenceGlobal(_globals.InsertConstant(
                _lexer.Integer.ToString(CultureInfo.InvariantCulture)));
        }
        else if (tokenType == GpdlTokenType.TKN_NAME)
        {
            string token = _lexer.Token;
            var def = LocalLookup(_context, token);
            if (def is null)
            {
                // 'true' and 'false' are not keywords; they are recognised only after the symbol
                // lookup fails, so a variable named 'true' shadows the literal.
                if (string.Equals(token, "true", StringComparison.Ordinal))
                {
                    ReferenceGlobal(_globals.InsertConstant("1"));
                }
                else if (string.Equals(token, "false", StringComparison.Ordinal))
                {
                    ReferenceGlobal(_globals.InsertConstant(string.Empty));
                }
                else
                {
                    Error("Undefined symbol in expression term");
                    return 1;
                }
            }
            else if (def.IsFunction)
            {
                if (def.IsSystem && def.Types[0] != 0)
                {
                    Error("Expected a function that returns a string value");
                    return 1;
                }
                int result = CompileFunctionCall(def);
                if (result != 0) { return result; }
            }
            else
            {
                int result = CompileVariableReference(def);
                if (result != 0) { return result; }
            }
        }
        else
        {
            Error("Unrecognized syntax for a term in an expression");
            return 1;
        }

        if (unaryOperator != SubOp.SUBOP_ILLEGAL)
        {
            _code.Comma(GpdlCode.ShiftedSubOp | (uint)unaryOperator);
        }
        return 0;
    }

    private readonly record struct OperDef(int Priority, GpdlTokenType TokenType, uint Opcode);

    /// <summary>
    /// <c>operDef[]</c> (GPDLcomp.cpp:2617). Higher number binds tighter. Note there is no
    /// operator for plain multiplication or division of strings — only the <c>#</c> (numeric)
    /// forms, and <c>+</c> which concatenates.
    /// </summary>
    private static readonly OperDef[] OperDefs =
    [
        new(5, GpdlTokenType.TKN_LOR, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_LOR),
        new(10, GpdlTokenType.TKN_LAND, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_LAND),
        new(15, GpdlTokenType.TKN_nOR, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nOR),
        new(20, GpdlTokenType.TKN_nXOR, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nXOR),
        new(25, GpdlTokenType.TKN_nAND, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nAND),
        new(30, GpdlTokenType.TKN_ISEQUAL, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_ISEQUAL),
        new(30, GpdlTokenType.TKN_nISEQUAL, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nISEQUAL),
        new(30, GpdlTokenType.TKN_NOTEQUAL, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_NOTEQUAL),
        new(30, GpdlTokenType.TKN_nNOTEQUAL, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nNOTEQUAL),
        new(35, GpdlTokenType.TKN_LESS, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_LESS),
        new(35, GpdlTokenType.TKN_nLESS, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nLESS),
        new(35, GpdlTokenType.TKN_LESSEQUAL, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_LESSEQUAL),
        new(35, GpdlTokenType.TKN_nLESSEQUAL, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nLESSEQUAL),
        new(35, GpdlTokenType.TKN_GREATER, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_GREATER),
        new(35, GpdlTokenType.TKN_nGREATER, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nGREATER),
        new(35, GpdlTokenType.TKN_GREATEREQUAL, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_GREATEREQUAL),
        new(35, GpdlTokenType.TKN_nGREATEREQUAL, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nGREATEREQUAL),
        new(40, GpdlTokenType.TKN_PLUS, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_PLUS),
        new(40, GpdlTokenType.TKN_nPLUS, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nPLUS),
        new(40, GpdlTokenType.TKN_nMINUS, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nMINUS),
        new(45, GpdlTokenType.TKN_nGEAR, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nTIMES),
        new(45, GpdlTokenType.TKN_nSLASH, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nSLASH),
        new(45, GpdlTokenType.TKN_nPERCENT, GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nPERCENT),
    ];

    /// <summary>
    /// <c>compileExpression</c> (GPDLcomp.cpp:2646) — a precedence-climbing loop over an explicit
    /// operator stack. A leading <c>!</c> or <c>-#</c> is pushed with priority 999 so it is emitted
    /// as soon as any binary operator arrives.
    /// </summary>
    private int CompileExpression()
    {
        uint[] opcodes = new uint[MaxStack];
        int[] priority = new int[MaxStack];
        int stackLen = 0;
        int result;

        var tokenType = GetToken();
        if (tokenType == GpdlTokenType.TKN_NOT)
        {
            priority[stackLen] = 999;
            opcodes[stackLen] = GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_NOT;
            stackLen++;
        }
        else if (tokenType == GpdlTokenType.TKN_nMINUS)
        {
            priority[stackLen] = 999;
            opcodes[stackLen] = GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_nNEGATE;
            stackLen++;
        }
        else
        {
            _lexer.BackspaceToken();
        }

        for (; ; )
        {
            result = CompileAtomicElement();
            if (result != 0) { break; }
            tokenType = GetToken();
            if (tokenType is GpdlTokenType.TKN_COMMA or GpdlTokenType.TKN_CLOSEPAREN
                or GpdlTokenType.TKN_SEMICOLON or GpdlTokenType.TKN_COLON)
            {
                _lexer.BackspaceToken();
                break;
            }

            int j = 0;
            for (; j < OperDefs.Length; j++)
            {
                if (tokenType == OperDefs[j].TokenType) { break; }
            }
            if (j == OperDefs.Length)
            {
                Error("Unknown operator symbol");
                return 1;
            }
            if (stackLen >= MaxStack - 1)
            {
                Error("compileExpression stack overflow");
                return 1;
            }
            priority[stackLen] = OperDefs[j].Priority;
            opcodes[stackLen] = OperDefs[j].Opcode;
            stackLen++;

            while (stackLen > 1 && priority[stackLen - 2] >= priority[stackLen - 1])
            {
                _code.Comma(opcodes[stackLen - 2]);
                stackLen--;
                priority[stackLen - 1] = priority[stackLen];
                opcodes[stackLen - 1] = opcodes[stackLen];
            }
        }

        if (result == 0)
        {
            while (stackLen > 0)
            {
                stackLen--;
                _code.Comma(opcodes[stackLen]);
            }
        }
        return result;
    }

    private int CompileVarAssignment()
    {
        if (GetToken() != GpdlTokenType.TKN_NAME)
        {
            Error("Assignment statement starts badly");
            return 1;
        }
        var def = LocalLookup(_context, _lexer.Token);
        if (def is null)
        {
            Error("Assignment statement starts with undefined variable name");
            return 1;
        }
        if (!def.IsFramePointerRelative && !def.IsGlobalVariable)
        {
            Error("Attempt to assign value to non-variable");
            return 1;
        }
        var tokenType = GetToken();
        if (tokenType != GpdlTokenType.TKN_EQUAL && tokenType != GpdlTokenType.TKN_nEQUAL)
        {
            Error("Assignment statement missing an equal sign");
            return 1;
        }
        int result = CompileExpression();
        if (result == 0)
        {
            if (tokenType == GpdlTokenType.TKN_nEQUAL) { CompileForceNumeric(); }
            result = CompileVariableStore(def);
        }
        return result;
    }

    /// <summary>
    /// <c>compileTypedSystemFunctionCall</c> (GPDLcomp.cpp:2787): an ACTOR-typed parameter cannot
    /// be an arbitrary expression — it must be one system function call of the matching type.
    /// </summary>
    private int CompileTypedSystemFunctionCall(int type)
    {
        if (GetToken() != GpdlTokenType.TKN_NAME)
        {
            Error("Expected a system function name");
            return 1;
        }
        var def = LocalLookup(_context, _lexer.Token);
        if (def is null)
        {
            Error("Undefined symbol in system function parameter");
            return 1;
        }
        if ((def.RequiredContexts & AvailableContexts) != def.RequiredContexts)
        {
            Error("System Function expects a context that is not provided by this hook");
            return 1;
        }
        if (!def.IsFunction)
        {
            Error("Expected system function parameter to be a function call");
            return 1;
        }
        if (!def.IsSystem)
        {
            Error("Expected system function parameter to be a system function call");
            return 1;
        }
        if (def.Types[0] != type)
        {
            Error("System function parameter not of correct data type");
            return 1;
        }
        return CompileFunctionCall(def);
    }

    private int CompileActualParameters(Definition def)
    {
        if (GetToken() != GpdlTokenType.TKN_OPENPAREN)
        {
            Error("Expected open parenthesis before actual parameters");
            return 1;
        }
        var pactual = def.FormalParams;
        int parameterNumber = 0;
        while (true)
        {
            var tokenType = GetToken();
            if (tokenType == GpdlTokenType.TKN_CLOSEPAREN) { break; }
            if (pactual is null)
            {
                Error("Too many parameters");
                return 1;
            }
            _lexer.BackspaceToken();

            int result = def.IsSystem && def.Types[parameterNumber + 1] != 0
                ? CompileTypedSystemFunctionCall(def.Types[parameterNumber + 1])
                : CompileExpression();
            if (result != 0) { return result; }

            pactual = pactual.Next;
            tokenType = GetToken();
            if (tokenType == GpdlTokenType.TKN_COMMA)
            {
                parameterNumber++;
                continue;
            }
            if (tokenType == GpdlTokenType.TKN_CLOSEPAREN) { break; }
            Error("Unexpected actual parameter separator");
            return 1;
        }
        if (pactual is not null)
        {
            Error("Not enough parameters");
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// <c>compileFunctionCall</c> (GPDLcomp.cpp:3052). A user function gets a default result
    /// pushed <i>before</i> its parameters, so the callee can overwrite that slot with
    /// <c>STORE_FP numParam</c>; a system function returns its own value.
    /// </summary>
    private int CompileFunctionCall(Definition func)
    {
        if (!func.IsFunction)
        {
            Error("Name does not represent a function");
            return 1;
        }
        if (!func.IsSystem) { CompileFalse(); }
        int result = CompileActualParameters(func);
        if (result != 0) { return result; }
        _code.Comma(func.IsSystem
            ? func.IntValue
            : func.IntValue | ((uint)BinOp.BINOP_CALL << 24));
        return 0;
    }

    // ---------------------------------------------------------------- statements

    private int CompileWhile()
    {
        uint saveContinue = _continuelist;
        _continuelist = 0;
        uint saveBreak = _breaklist;
        _breaklist = 0;
        try
        {
            if (GetToken() != GpdlTokenType.TKN_OPENPAREN)
            {
                Error("Expected open parenthesis after $WHILE");
                return 1;
            }
            uint continueAddr = (uint)_code.Here;
            if (CompileExpression() != 0) { return 1; }
            int doneJump = CompileJumpFalse();

            // The JUMPFALSE cell doubles as the head of the break chain. ResolveBreaks assigns the
            // loop-exit address into its low bits and ResolveJump then ORs the same value in, so
            // the two are idempotent -- but only because both resolve to the same address.
            AddToBreakList();

            if (GetToken() != GpdlTokenType.TKN_CLOSEPAREN)
            {
                Error("Expected close parenthesis after $WHILE expression");
                return 1;
            }
            if (CompileBlock(string.Empty) != 0) { return 1; }
            CompileJumpTo(continueAddr);
            ResolveBreaks((uint)_code.Here);
            ResolveContinues(continueAddr);
            ResolveJump(doneJump);
            return 0;
        }
        finally
        {
            _breaklist = saveBreak;
            _continuelist = saveContinue;
        }
    }

    private int CompileIf()
    {
        if (GetToken() != GpdlTokenType.TKN_OPENPAREN)
        {
            Error("Expected open parenthesis after $IF");
            return 1;
        }
        if (CompileExpression() != 0) { return 1; }
        int jumpaddr = CompileJumpFalse();
        if (GetToken() != GpdlTokenType.TKN_CLOSEPAREN)
        {
            Error("Expected close parenthesis after $IF expression");
            return 1;
        }
        if (GetToken() != GpdlTokenType.TKN_OPENBRACE)
        {
            Error("Block following $IF must be enclosed in braces");
            return 1;
        }
        _lexer.BackspaceToken();
        if (CompileBlock(string.Empty) != 0) { return 1; }

        if (GetToken() == GpdlTokenType.TKN_NAME
            && string.Equals(_lexer.Token, "$ELSE", StringComparison.Ordinal))
        {
            if (GetToken() != GpdlTokenType.TKN_OPENBRACE)
            {
                Error("Block following $ELSE must be enclosed in braces");
                return 1;
            }
            _lexer.BackspaceToken();
            int elseaddr = CompileJump();
            ResolveJump(jumpaddr);
            if (CompileBlock(string.Empty) != 0) { return 1; }
            ResolveJump(elseaddr);
            return 0;
        }
        _lexer.BackspaceToken();
        ResolveJump(jumpaddr);
        return 0;
    }

    private int CompileContinue()
    {
        CompileJump();
        AddToContinueList();
        return 0;
    }

    private int CompileBreak()
    {
        CompileJump();
        AddToBreakList();
        return 0;
    }

    /// <summary>
    /// <c>compileSwitch</c> (GPDLcomp.cpp:3079). Each <c>$CASE</c> duplicates the switch value and
    /// tests it; <c>$GCASE</c> greps it instead. Fall-through between cases is supported by
    /// jumping over the following case's test code.
    /// </summary>
    private int CompileSwitch()
    {
        uint saveBreak = _breaklist;
        _breaklist = 0;
        try
        {
            if (GetToken() != GpdlTokenType.TKN_OPENPAREN)
            {
                Error("Expected open parenthesis following '$SWITCH'");
                return 1;
            }
            int result = CompileExpression();
            if (result != 0) { return result; }
            if (GetToken() != GpdlTokenType.TKN_CLOSEPAREN)
            {
                Error("Expected close parenthesis after switch expression");
                return 1;
            }

            int prevCaseJump = 0;
            if (GetToken() != GpdlTokenType.TKN_OPENBRACE)
            {
                Error("Expected open brace to start $CASE body");
                return 1;
            }
            bool haveDefault = false;
            bool caseSeen = false;
            while (true)
            {
                var tokenType = GetToken();
                if (tokenType == GpdlTokenType.TKN_CLOSEBRACE) { break; }
                if (tokenType == GpdlTokenType.TKN_NAME)
                {
                    string token = _lexer.Token;
                    if (string.Equals(token, "$CASE", StringComparison.Ordinal)
                        || string.Equals(token, "$GCASE", StringComparison.Ordinal))
                    {
                        if (haveDefault)
                        {
                            Error("A $CASE or $DEFAULT follows a $DEFAULT");
                            return 1;
                        }
                        int prevCodeJump = caseSeen ? CompileJump() : 0;
                        if (prevCaseJump != 0) { ResolveJump(prevCaseJump); prevCaseJump = 0; }
                        _code.Comma(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_DUP);
                        result = CompileExpression();
                        if (result != 0) { return result; }
                        if (GetToken() != GpdlTokenType.TKN_COLON)
                        {
                            Error("Expected colon following $CASE expression");
                            return 1;
                        }
                        if (string.Equals(token, "$CASE", StringComparison.Ordinal))
                        {
                            _code.Comma(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_ISEQUAL);
                            prevCaseJump = CompileJumpFalse();
                        }
                        else
                        {
                            // $GCASE: the switch value is the text and the case value the pattern,
                            // so they must be swapped before $GREP(pattern, string).
                            _code.Comma(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_SWAP);
                            _code.Comma(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_GREP);
                            prevCaseJump = CompileJumpFalse();
                        }
                        if (prevCodeJump != 0) { ResolveJump(prevCodeJump); }
                        caseSeen = true;
                        continue;
                    }
                    if (string.Equals(token, "$DEFAULT", StringComparison.Ordinal))
                    {
                        if (haveDefault)
                        {
                            Error("A $CASE or $DEFAULT follows a $DEFAULT");
                            return 1;
                        }
                        if (prevCaseJump != 0) { ResolveJump(prevCaseJump); prevCaseJump = 0; }
                        if (GetToken() != GpdlTokenType.TKN_COLON)
                        {
                            Error("Expected colon following $CASE expression");
                            return 1;
                        }
                        haveDefault = true;
                        continue;
                    }
                }
                if (!caseSeen)
                {
                    Error("Switch statement should begin with $CASE or $GCASE");
                    return 1;
                }
                _lexer.BackspaceToken();
                result = CompileStatement();
                if (result != 0) { return result; }
            }

            if (GetToken() != GpdlTokenType.TKN_NAME
                || !string.Equals(_lexer.Token, "$ENDSWITCH", StringComparison.Ordinal))
            {
                Error("Expected '$ENDSWITCH' sentinel at end of $SWITCH statement");
                return 1;
            }
            if (prevCaseJump != 0) { ResolveJump(prevCaseJump); }
            ResolveBreaks((uint)_code.Here);
            return 0;
        }
        finally
        {
            _breaklist = saveBreak;
        }
    }

    /// <summary>
    /// <c>compileReturn</c> (GPDLcomp.cpp:3197). The operand packs the local count into bits 12-23
    /// and the parameter count into bits 0-11.
    /// </summary>
    private int CompileReturn()
    {
        if (_activeFunction is null)
        {
            Error("$RETURN outside function body");
            return 1;
        }
        int numParam = _activeFunction.NumParam();
        int numLocals = _activeFunction.NumLocals();
        var tokenType = GetToken();
        _lexer.BackspaceToken();
        if (tokenType == GpdlTokenType.TKN_SEMICOLON)
        {
            _code.Comma(((uint)BinOp.BINOP_RETURN << 24) | ((uint)numLocals << 12) | (uint)numParam);
            return 0;
        }
        int result = CompileExpression();
        if (result != 0) { return result; }
        _code.Comma(((uint)BinOp.BINOP_STORE_FP << 24) | (uint)numParam);
        _code.Comma(((uint)BinOp.BINOP_RETURN << 24) | ((uint)numLocals << 12) | (uint)numParam);
        return 0;
    }

    /// <summary>
    /// <c>compileRespond</c> (GPDLcomp.cpp:3224) — the <c>$RESPOND(pattern, text)</c> macro, which
    /// expands to grep the listen text, say the reply, and <c>$CONTINUE</c>. It only makes sense
    /// inside a <c>$WHILE</c>, because the <c>$CONTINUE</c> needs a loop to jump to.
    /// </summary>
    private int CompileRespond()
    {
        if (GetToken() != GpdlTokenType.TKN_OPENPAREN)
        {
            Error("Expected open parenthesis following '$RESPOND'");
            return 1;
        }
        int result = CompileExpression();
        if (result != 0) { return result; }
        _code.Comma(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_LISTENTEXT);
        _code.Comma(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_GREP);
        int jump = CompileJumpFalse();
        if (GetToken() != GpdlTokenType.TKN_COMMA)
        {
            Error("Expected a comma between pattern and text of $RESPOND arguments");
            return 1;
        }
        result = CompileExpression();
        if (result != 0) { return result; }
        _code.Comma(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_SAY);
        _code.Comma(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_POP);
        CompileContinue();
        ResolveJump(jump);
        if (GetToken() != GpdlTokenType.TKN_CLOSEPAREN)
        {
            Error("Expected close parenthesis after $RESPOND's two parameters");
            return 1;
        }
        return 0;
    }

    private int CompileStatement()
    {
        if (GetToken() != GpdlTokenType.TKN_NAME)
        {
            Error($"Statement starts badly: '{_lexer.Token}'");
            return 1;
        }

        string token = _lexer.Token;
        int result;
        switch (token)
        {
            case "$VAR": result = CompileVariableDecl(); break;
            case "$FUNC": result = CompileFunctionDecl(); break;
            case "$PUBLIC": result = CompilePublicDecl(); break;
            case "$BREAK": result = CompileBreak(); break;
            case "$WHILE": result = CompileWhile(); break;
            case "$CONTINUE": result = CompileContinue(); break;
            case "$SWITCH": result = CompileSwitch(); break;
            case "$RETURN": result = CompileReturn(); break;
            case "$IF": result = CompileIf(); break;
            case "$RESPOND": result = CompileRespond(); break;
            default:
                {
                    var def = LocalLookup(_context, token);
                    if (def is null)
                    {
                        Error($"Undefined name '{token}' at start of statement");
                        return 1;
                    }
                    if (def.IsFunction)
                    {
                        result = CompileFunctionCall(def);
                        // A statement-level call discards its result; every GPDL function returns
                        // one, so the POP is mandatory or the stack drifts.
                        if (result == 0) { CompilePop(); }
                    }
                    else if (def.IsFramePointerRelative || def.IsGlobalVariable)
                    {
                        _lexer.BackspaceToken();
                        result = CompileVarAssignment();
                    }
                    else
                    {
                        Error($"Unknown token '{token}' at start of statement");
                        return 1;
                    }
                    break;
                }
        }

        if (result != 0) { return result; }

        if (GetToken() != GpdlTokenType.TKN_SEMICOLON)
        {
            Error($"Expected semi-colon at end of statement: '{token}'");
            return 1;
        }
        return 0;
    }

    private int CompileBlock(string blockname)
    {
        if (GetToken() != GpdlTokenType.TKN_OPENBRACE)
        {
            Error("Expected open brace to start a code block");
            return 1;
        }
        while (GetToken() != GpdlTokenType.TKN_CLOSEBRACE)
        {
            _lexer.BackspaceToken();
            int result = CompileStatement();
            if (result != 0) { return result; }
        }
        if (blockname.Length != 0)
        {
            if (GetToken() != GpdlTokenType.TKN_NAME
                || !string.Equals(_lexer.Token, blockname, StringComparison.Ordinal))
            {
                Error($"Expected ending block name - {blockname}");
                return 1;
            }
        }
        return 0;
    }

    /// <summary>
    /// <c>defineFormalParameters</c> (GPDLcomp.cpp:3362). Offsets count up from the head of the
    /// (reversed) parameter list, so the last-declared parameter is frame offset 0.
    /// </summary>
    private static void DefineFormalParameters(Definition func, Scope scope)
    {
        uint paramOffset = 0;
        for (var param = func.FormalParams; param is not null; param = param.Next)
        {
            var def = scope.AddDefinition(param.Name);
            def.IsFramePointerRelative = true;
            def.IntValue = paramOffset++;
        }
    }

    private int CompilePublicDecl()
    {
        if (GetToken() != GpdlTokenType.TKN_NAME
            || !string.Equals(_lexer.Token, "$FUNC", StringComparison.Ordinal))
        {
            Error("Expected \"$FUNC\" following \"$PUBLIC\"");
            return 1;
        }
        return CompileFunctionDecl(AttributePublic);
    }

    /// <summary>
    /// <c>compileFunctionDecl</c> (GPDLcomp.cpp:3389). Functions may nest; a nested definition
    /// emits a jump around its own body so that the enclosing function's code stays contiguous.
    /// </summary>
    private int CompileFunctionDecl(uint attributes = 0)
    {
        var saveCurrent = _current;
        var saveContext = _context;
        try
        {
            if (_allFunctionsArePublic) { attributes |= AttributePublic; }

            if (GetToken() != GpdlTokenType.TKN_NAME)
            {
                Error("Expected to find the name of a function");
                return 1;
            }
            string token = _lexer.Token;

            var existing = LocalLookup(_current, token);
            bool proto;
            if (existing is null) { proto = false; }
            else if (existing.IsPrototype) { proto = true; }
            else
            {
                Error("Function already defined");
                return 1;
            }

            // A fresh DEFINITION is added even when a prototype exists, so the prototype's
            // parameter list -- and its default values -- are the ones checked, while the new
            // definition is the one that gets the code address.
            var newFunc = _current.AddFunction(token);
            _current = _current.AddScope(token);

            if (GetToken() != GpdlTokenType.TKN_OPENPAREN)
            {
                Error("Expected open parenthesis after function name");
                return 1;
            }

            int param = 0;
            Definition? formalParam = null;
            while (true)
            {
                var tokenType = GetToken();
                if (tokenType == GpdlTokenType.TKN_CLOSEPAREN) { break; }
                if (tokenType != GpdlTokenType.TKN_NAME)
                {
                    Error("Expected a formal parameter name");
                    return 1;
                }
                token = _lexer.Token;
                if (proto)
                {
                    // GPDLcomp.cpp:3439 checks the parameter list of newFunc -- the definition
                    // just created, whose list is still empty because the `else` below is the only
                    // thing that fills it. So this test fails for the first parameter of any
                    // prototyped function, and a prototype with parameters cannot be defined at
                    // all. Reproduced: a design that relies on it not erroring does not exist,
                    // and "fixing" it would let source through that the real compiler rejects.
                    if (newFunc.CheckProtoParam(param) != 0)
                    {
                        Error("prototype has fewer parameters");
                        return 1;
                    }
                }
                else { formalParam = newFunc.AddFormalParam(token); }
                param++;

                tokenType = GetToken();
                if (tokenType == GpdlTokenType.TKN_COMMA) { continue; }
                if (tokenType == GpdlTokenType.TKN_EQUAL)
                {
                    if (proto)
                    {
                        Error("Default value should be specified in prototype");
                        return 1;
                    }
                    if (GetToken() != GpdlTokenType.TKN_STRING)
                    {
                        Error("Expected literal string as default value of parameter");
                        return 1;
                    }
                    formalParam!.DefaultValue = _lexer.Token;
                    tokenType = GetToken();
                    if (tokenType == GpdlTokenType.TKN_COMMA) { continue; }
                }
                if (tokenType == GpdlTokenType.TKN_CLOSEPAREN) { break; }
                Error("Expected comma or close parenthesis at end of formal parameters");
                return 1;
            }

            if ((attributes & AttributePublic) != 0) { newFunc.IsPublic = true; }

            if (proto && newFunc.CheckProtoParam(param) == 0)
            {
                Error("Prototype had more parameters");
                return 1;
            }

            var next = GetToken();
            if (next == GpdlTokenType.TKN_SEMICOLON)
            {
                if (proto)
                {
                    Error("Function prototype already declared");
                    return 1;
                }
                newFunc.IsPrototype = true;
                _current = _current.Parent!;    // discardCurrent
                return 0;
            }
            if (next != GpdlTokenType.TKN_OPENBRACE)
            {
                Error("Expected open brace or semi-colon following function parameters");
                return 1;
            }
            _lexer.BackspaceToken();

            int jump = -1;
            if (_activeFunction is not null) { jump = CompileJump(); }
            token = newFunc.Name;
            newFunc.IntValue = (uint)_code.Here;

            DefineFormalParameters(newFunc, _current);
            var saveActive = _activeFunction;
            _activeFunction = newFunc;
            _context = _current;
            int result;
            try
            {
                // The cell AT the entry address is not code: it is a global reference to a debug
                // marker "name(numParams)". BINOP_CALL skips it (GPDLexec.cpp:2359), and
                // GPDL::BeginExecute parses the parenthesised count out of it to validate the
                // caller's argument list (GPDLexec.cpp:1250). When compiling an embedded script the
                // marker is the empty string, which is why BeginExecute(int) does not check counts.
                string entryDebug = _compilingScript
                    ? string.Empty
                    : $"{newFunc.Name}({newFunc.NumParam().ToString(CultureInfo.InvariantCulture)})";
                ReferenceGlobal(_globals.InsertConstant(entryDebug));

                result = CompileBlock(token);
                if (result != 0) { return result; }

                // Every function gets a trailing $RETURN whether or not the source has one.
                CompileReturn();
            }
            finally
            {
                _activeFunction = saveActive;
            }

            if (jump >= 0) { ResolveJump(jump); }
            return result;
        }
        finally
        {
            _current = saveCurrent;
            _context = saveContext;
        }
    }

    // ---------------------------------------------------------------- entry points

    /// <summary>
    /// <c>CompileProgram</c> (GPDLcomp.cpp:3533). At global level only <c>$PUBLIC</c>,
    /// <c>$FUNC</c> and <c>$VAR</c> are legal, each terminated by a semicolon.
    /// </summary>
    /// <param name="lines">
    /// Source lines, each ending in <c>\n</c> — see <see cref="GpdlLexer.SplitLines"/>.
    /// </param>
    /// <param name="compilingScript">
    /// True for the embedded-script path, which suppresses the function-entry debug markers.
    /// </param>
    /// <returns>0 on success, 1 if any error was reported.</returns>
    public int Compile(IEnumerable<string> lines, bool compilingScript = false)
    {
        Initialize();
        _compilingScript = compilingScript;
        _lexer = new GpdlLexer(lines);

        // Address 0 must never be a function entry point: 0 is the "not found" answer from
        // INDEX::lookup, so a function there would be unreachable.
        _code.Comma(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_NOOP);

        GpdlTokenType tokenType;
        while ((tokenType = GetToken()) != GpdlTokenType.TKN_NONE)
        {
            int result;
            if (tokenType == GpdlTokenType.TKN_NAME)
            {
                string token = _lexer.Token;
                if (string.Equals(token, "$PUBLIC", StringComparison.Ordinal))
                {
                    result = CompilePublicDecl();
                    if (result == 0 && GetToken() != GpdlTokenType.TKN_SEMICOLON)
                    {
                        Error("Expected semi-colon after $PUBLIC statement");
                        result = 1;
                    }
                }
                else if (string.Equals(token, "$FUNC", StringComparison.Ordinal))
                {
                    result = CompileFunctionDecl();
                    if (result == 0 && GetToken() != GpdlTokenType.TKN_SEMICOLON)
                    {
                        Error("Expected semi-colon after function definition");
                        result = 1;
                    }
                }
                else if (string.Equals(token, "$VAR", StringComparison.Ordinal))
                {
                    result = CompileVariableDecl();
                    if (result == 0 && GetToken() != GpdlTokenType.TKN_SEMICOLON)
                    {
                        Error("Expected semi-colon after variable declaration");
                        result = 1;
                    }
                }
                else
                {
                    Error("Illegal statement at global level");
                    result = 1;
                }
            }
            else
            {
                Error("Illegal statement type at global level");
                result = 1;
            }
            if (result != 0) { return 1; }
        }
        return 0;
    }

    /// <summary>Convenience overload taking whole source text.</summary>
    public int Compile(string text, bool compilingScript = false) =>
        Compile(GpdlLexer.SplitLines(text), compilingScript);

    /// <summary>
    /// The public-function index as written by <c>DICTIONARY::write</c> (GPDLcomp.cpp:1229), in
    /// file order.
    /// </summary>
    public List<(string Name, uint Address)> PublicFunctionIndex()
    {
        var list = new List<(string, uint)>();
        _root.CollectPublicFunctions(string.Empty, list);
        return list;
    }
}
