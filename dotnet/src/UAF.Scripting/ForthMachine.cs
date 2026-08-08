namespace UAF.Scripting;

/// <summary>Raised when a Forth word leaves the data stack at a depth it did not declare.</summary>
/// <remarks>
/// <b>The reference calls <c>die()</c> here</b> (<c>DataStackError</c>, <c>Forth.cpp:1918</c>) —
/// it takes the whole game down rather than continue with a corrupt stack. A design's AI script
/// really can do this, so it is an exception rather than an assert.
/// </remarks>
public sealed class ForthStackException(string message) : Exception(message);

/// <summary>
/// The Forth VM (<c>UAFWin/Forth.cpp</c>): an indirect-threaded kernel that builds its own
/// dictionary by interpreting <see cref="ForthKernel.Source"/>, and then runs the design's
/// <c>AI_Script.BLK</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The inner interpreter is C recursion, not a threaded loop.</b> <c>docolon</c> runs a
/// <c>for(;;)</c> over the word's body and calls each child primitive directly; a child that is
/// itself a colon word re-enters <c>docolon</c>, so Forth's return stack and the host's call stack
/// grow together. That is why <c>EXIT</c> is a flag rather than a jump.
/// </para>
/// <para>
/// <b><c>EXIT</c> and <c>ABORT</c> are the same mechanism at different magnitudes.</b> <c>EXIT</c>
/// sets the flag to 1; <c>docolon</c> breaks its loop and decrements it back to 0, so one level
/// returns. <c>ABORT</c> sets it to 999999, and every level in turn breaks and decrements — the
/// whole nest unwinds and the count is the depth budget. There is no other unwinding path.
/// </para>
/// <para>
/// <b>Every colon word is stack-effect checked at run time.</b> A definition declares its effect
/// with <c>n SP+-</c>, which writes <c>-n*2</c> into the header; <c>docolon</c> records
/// <c>SP + effect</c> on entry and compares on exit. That is what all the <c>1 SP+-</c> and
/// <c>-2 SP+-</c> tails in the kernel are for, and it is why a mistyped AI script fails loudly
/// rather than drifting.
/// </para>
/// </remarks>
public sealed partial class ForthMachine
{
    private readonly ForthMemory m = new();

    private readonly List<Action> primitives = [];

    private int nextPrimitive = 1;

    /// <summary>
    /// The unwind counter. 0 runs, 1 returns one level, 999999 aborts everything.
    /// </summary>
    private int exit;

    public ForthMachine()
    {
        BuildPrimitiveTable();
    }

    /// <summary>The machine's memory, for tests and for the game bindings.</summary>
    public ForthMemory Memory => m;

    /// <summary>Whatever <c>TYPE</c>, <c>EMIT</c> and <c>CR</c> printed, and any error text.</summary>
    public List<string> Output { get; } = [];

    /// <summary>Whether the last <see cref="Interpret"/> ended in an abort.</summary>
    public bool Aborted { get; private set; }

    /// <summary>
    /// Builds the dictionary by interpreting <see cref="ForthKernel.Source"/>
    /// (<c>ExpandKernel</c>, <c>Forth.cpp:2260</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The source is moved to the top of memory first, and the bottom is cleared.</b> The kernel
    /// text ships <i>in</i> the memory array, starting at offset 0 — where the dictionary is about
    /// to be built. <c>ExpandKernel</c> moves it up to just below the data stack and zeroes what it
    /// left behind, so the dictionary grows upwards into the space the text used to occupy.
    /// </para>
    /// <para>
    /// <b>Only <c>CREATE</c> and <c>+p</c> are bootstrapped by hand</b>, twice each: the first pair
    /// defines <c>CREATE</c> itself and the second defines <c>+p</c>. From there the text is
    /// ordinary input, which is why what remains on the stack afterwards — the source address and
    /// what is left of its count — has to be pushed manually before <c>INTERPRET</c> is called.
    /// </para>
    /// </remarks>
    /// <returns>Whether the kernel built without aborting.</returns>
    public bool Bootstrap()
    {
        byte[] source = System.Text.Encoding.ASCII.GetBytes(ForthKernel.Source);
        int length = source.Length;

        m.Sp = ForthMemory.DataStackBase;
        m.Rp = ForthMemory.ReturnStackBase;

        int at = m.Sp - length - 100;
        source.CopyTo(m.Bytes, at);
        Array.Clear(m.Bytes, 0, at);

        m.SetCell(ForthMemory.DpAddress, ForthMemory.DictionaryStart);
        m.SetCell(ForthMemory.SourceAddress, at);
        m.SetCell(ForthMemory.SourceAddress + 2, length);
        m.SetCell(ForthMemory.LatestAddress, 0);
        m.SetCell(ForthMemory.ToInAddress, 0);
        nextPrimitive = 1;

        Create(); PlusP();
        Create(); PlusP();

        // What CREATE consumed of the source, handed to INTERPRET as its buffer.
        m.SetCell(ForthMemory.SourceAddress,
                  m.Cell(ForthMemory.SourceAddress) + m.Cell(ForthMemory.ToInAddress));
        m.Push(m.Cell(ForthMemory.SourceAddress));
        m.SetCell(ForthMemory.SourceAddress + 2,
                  m.Cell(ForthMemory.SourceAddress + 2) - m.Cell(ForthMemory.ToInAddress));
        m.Push(m.Cell(ForthMemory.SourceAddress + 2));

        m.SetCell(ForthMemory.StateAddress, 0);
        m.SetCell(ForthMemory.BaseAddress, 10);
        exit = 0;

        Interpret();
        return !Aborted;
    }

    /// <summary>
    /// Interprets one line of Forth against the dictionary already built.
    /// </summary>
    /// <remarks>
    /// This is how <c>AI_Script.BLK</c> is loaded — a line at a time, each one a whole buffer.
    /// </remarks>
    public bool Evaluate(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        byte[] text = System.Text.Encoding.ASCII.GetBytes(line);
        int at = m.Sp - 200;
        text.CopyTo(m.Bytes, at);

        m.SetCell(ForthMemory.SourceAddress, at);
        m.SetCell(ForthMemory.SourceAddress + 2, text.Length);
        m.Push(at);
        m.Push(text.Length);

        exit = 0;
        Interpret();
        return !Aborted;
    }

    /// <summary>
    /// Loads a design's <c>AI_Script.BLK</c> on top of the built kernel
    /// (<c>ExpandKernel</c>'s second half, <c>Forth.cpp:2294</c>–<c>:2336</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A line at a time, each one its own input buffer.</b> The reference never reads the file
    /// whole: it <c>fgets</c> into the same 200-byte scratch below the data stack that
    /// <see cref="Evaluate"/> uses, and interprets each line on its own. Compile state survives
    /// between calls, which is the only reason a colon definition may span lines.
    /// </para>
    /// <para>
    /// <b>The newline becomes a space rather than being dropped.</b> Only a space or a tab
    /// delimits a token, so a line whose terminator was simply removed would run its last word
    /// into the next line's first. A carriage return is not a delimiter either — the reference
    /// never meets one because it opens the file in text mode on Windows, and this port has to
    /// strip it.
    /// </para>
    /// <para>
    /// <b>One bad line stops the script, and quietly.</b> There is no skip-and-continue: the
    /// reference breaks out of the read loop on the first abort, leaving everything defined before
    /// it in the dictionary. So a typo shortens an AI script rather than changing what the
    /// monsters do — and if it lands before <c>THINK</c>, <see cref="RunThink"/> then scores every
    /// pair 0.
    /// </para>
    /// <para>
    /// <b>The reference also chunks at 119 characters</b> — <c>fgets(buf, 120, f)</c> — which would
    /// split a token across two buffers. No shipped script comes close: the longest line in either
    /// version is 81 characters, so the chunking is unobservable and is not reproduced.
    /// </para>
    /// </remarks>
    /// <returns>Whether every line interpreted without aborting.</returns>
    public bool LoadScript(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (string line in text.Split('\n'))
        {
            if (!Evaluate(line.TrimEnd('\r') + ' '))
            {
                return false;
            }
        }

        return true;
    }

    // ---- the outer interpreter ------------------------------------------------------------------

    /// <summary>
    /// <c>Kf_INTERPRET</c> (<c>Forth.cpp:1969</c>): read a word, find it, run or compile it.
    /// </summary>
    /// <remarks>
    /// <b>A word that is neither in the dictionary nor a number aborts.</b> The engine build logs
    /// the offending text and calls <c>ABORT</c>; there is no "skip it and carry on" path, which is
    /// why a typo in <c>AI_Script.BLK</c> stops the whole script.
    /// </remarks>
    private void Interpret()
    {
        Aborted = false;

        m.SetCell(ForthMemory.SourceAddress + 2, m.Pop());
        m.SetCell(ForthMemory.SourceAddress, m.Pop());
        m.SetCell(ForthMemory.ToInAddress, 0);

        for (;;)
        {
            m.Push(' ');
            Word();

            int wordAddress = m.Stack(0);
            if (m.Byte(wordAddress) == 0) { break; }

            Find();

            if (m.Stack(0) != 0)
            {
                if (m.Pop() > 0 || m.Cell(ForthMemory.StateAddress) == 0)
                {
                    m.Cfa = (ushort)m.Pop();
                    primitives[m.OpcodeOf(m.Cfa)]();

                    if (exit != 0)
                    {
                        Aborted = true;
                        return;
                    }
                }
                else
                {
                    // Compiling: lay the word's address into the definition being built.
                    m.SetCell(m.Here, m.Pop());
                    m.Here += 2;
                }
            }
            else
            {
                m.Pop();
                QuestionNumber();

                if (m.Pop() == 0)
                {
                    Count();
                    int count = m.Pop();
                    int address = m.Pop();
                    Output.Add(TextAt(address, count) + "?");
                    Abort();
                    Aborted = true;
                    return;
                }

                m.Sp += 2;                      // single precision only, so drop the high half

                if (m.Cell(ForthMemory.StateAddress) != 0)
                {
                    CompileLiteral();
                }
            }
        }

        m.Pop();
    }

    /// <summary>
    /// Lays down <c>LIT</c> and the value.
    /// </summary>
    /// <remarks>
    /// <b>The reference looks <c>LIT</c> up by name every time</b>, building a counted string on
    /// the stack to do it (<c>Forth.cpp:2043</c>) rather than caching the address. Transcribed as a
    /// name lookup for the same reason: <c>LIT</c> is an ordinary dictionary entry and a script
    /// could in principle redefine it.
    /// </remarks>
    private void CompileLiteral()
    {
        int value = m.Pop();
        int lit = Lookup("LIT");

        m.SetCell(m.Here, lit);
        m.SetCell(m.Here + 2, value);
        m.Here += 4;
    }

    /// <summary>The link address of a word, or 0.</summary>
    public int Lookup(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var (link, found) in m.Words())
        {
            if (string.Equals(found, name, StringComparison.Ordinal))
            {
                return link;
            }
        }

        return 0;
    }

    /// <summary>Runs a defined word by name, for tests and for the game entry points.</summary>
    public void Run(string name)
    {
        int link = Lookup(name);

        if (link == 0)
        {
            throw new ArgumentException($"no Forth word named '{name}'", nameof(name));
        }

        exit = 0;
        m.Cfa = (ushort)link;
        primitives[m.OpcodeOf(link)]();
        if (exit > 0) { exit--; }
    }

    private string TextAt(int address, int count)
    {
        var text = new char[count];

        for (int i = 0; i < count; i++)
        {
            text[i] = (char)(byte)m.Byte(address + i);
        }

        return new string(text);
    }
}
