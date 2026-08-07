namespace UAF.Scripting;

/// <summary>
/// The primitives (<c>Kf_*</c> and the <c>Kf[]</c> table, <c>Forth.cpp:140</c>).
/// </summary>
public sealed partial class ForthMachine
{
    /// <summary>
    /// The table, in the order <c>+p</c> hands out indices.
    /// </summary>
    /// <remarks>
    /// <b>Index 0 is null and index <i>n</i> is the <i>n</i>th <c>PRIM</c> in the kernel source.</b>
    /// <c>+p</c> stamps <c>nextPrim++</c> into the word just created, so this table and
    /// <see cref="ForthKernel.Source"/> are a single ordered pair — nothing names anything. The
    /// first twenty entries are created by bare <c>CREATE … +p</c> lines and the rest by the
    /// <c>PRIM</c> word defined from them.
    /// </remarks>
    private void BuildPrimitiveTable()
    {
        primitives.Clear();
        primitives.Add(() => throw new InvalidOperationException(
            "Forth primitive 0 is the null entry; a word whose code field is 0 was never given " +
            "one by +p."));

        primitives.AddRange(
        [
            Create,                                                     //  1 CREATE
            PlusP,                                                      //  2 +p
            Tick,                                                       //  3 '
            Lit,                                                        //  4 LIT
            () => m.SetStack(0, m.Cell(m.Stack(0))),                    //  5 @
            () => { },                                                  //  6 NOP
            SetImmediate,                                               //  7 SETIMMEDIATE
            () => m.SetCell(ForthMemory.StateAddress, 0),               //  8 [
            () => m.SetCell(ForthMemory.StateAddress, 1),               //  9 ]
            () => m.SetStack(0, m.Stack(0) < 0 ? -1 : 0),               // 10 0<
            () => m.SetStack(0, m.Byte(m.Stack(0))),                    // 11 C@
            CStore,                                                     // 12 C!
            PlusStore,                                                  // 13 +!
            Store,                                                      // 14 !
            () => m.SetStack(0, m.Stack(0) - 1),                        // 15 1-
            () => m.Push(m.Stack(0)),                                   // 16 DUP
            Rot,                                                        // 17 ROT
            () => exit = 1,                                             // 18 EXIT
            QuestionExit,                                               // 19 ?EXIT
            StackEffect,                                                // 20 SP+-
            DoColon,                                                    // 21 docolon
            () => m.Pc = (ushort)m.Cell(m.Pc),                          // 22 BRANCH
            QBranch,                                                    // 23 ?BRANCH
            () => m.Push(m.Stack(2)),                                   // 24 OVER
            Head,                                                       // 25 HEAD
            Word,                                                       // 26 WORD
            Find,                                                       // 27 FIND
            Swap,                                                       // 28 SWAP
            () => m.PushReturn(m.Pop()),                                // 29 >R
            () => m.Push(m.PopReturn()),                                // 30 R>
            () => { int t = (ushort)m.Pop(); m.SetStack(0, (ushort)m.Stack(0) | t); }, // 31 OR
            () => { int t = m.Pop(); m.SetStack(0, m.Stack(0) ^ t); },  // 32 XOR
            () => { int t = m.Pop(); m.SetStack(0, m.Stack(0) & t); },  // 33 AND
            () => { if (m.Stack(0) < 0) { m.SetStack(0, -m.Stack(0)); } }, // 34 ABS
            () => m.Sp += 2,                                            // 35 DROP
            () => { int t = m.Pop(); m.SetStack(0, m.Stack(0) + t); },  // 36 +
            () => { int t = m.Pop(); m.SetStack(0, m.Stack(0) - t); },  // 37 -
            () => m.SetStack(0, -m.Stack(0)),                           // 38 NEGATE
            () => m.PushDouble(-m.PopDouble()),                         // 39 DNEGATE
            UmStar,                                                     // 40 UM*
            MStar,                                                      // 41 M*
            () => Compare((a, b) => b == a),                            // 42 =
            () => m.SetCell(m.Sp, m.Cell(m.Sp) != 0 ? 0 : -1),          // 43 NOT
            () => Compare((a, b) => b != a),                            // 44 !=
            () => Compare((a, b) => b < a),                             // 45 <
            () => Compare((a, b) => b > a),                             // 46 >
            QDup,                                                       // 47 ?DUP
            MuSlashMod,                                                 // 48 MU/MOD
            Debug,                                                      // 49 DEBUG
            Execute,                                                    // 50 EXECUTE
            () => m.Push(m.Cell(m.Cfa + 2)),                            // 51 docon
        ]);

        AddGameWords();                                 // 52..72, see ForthGameWords
    }

    /// <summary>
    /// The twenty-one words that read a <c>COMBAT_SUMMARY</c>, in kernel order.
    /// </summary>
    /// <remarks>
    /// The order is the contract: the kernel's <c>PRIM</c> lines name them in exactly this
    /// sequence, so this list is what a test can check the dictionary against.
    /// </remarks>
    public static readonly string[] GameWords =
    [
        "Me", "He", "A", "B",
        "A:Type", "A:Damage",
        "W:Type", "W:Range", "W:Protection", "W:Damage", "W:ROF", "W:AttackBonus", "W:Priority",
        "Shield.Next", "Shield.Ready!", "Fleeing@",
        "C:State", "C:Distance", "C:Friendly", "C:AIBaseclass", "C:HasLineOfSight",
    ];

    // ---- the inner interpreter ------------------------------------------------------------------

    /// <summary>
    /// Runs a colon definition (<c>Kf_docolon</c>, <c>Forth.cpp:1939</c>).
    /// </summary>
    private void DoColon()
    {
        ushort cfa = m.Cfa;

        m.PushReturn(m.Sp + m.StackEffectOf(cfa));
        m.PushReturn(m.Pc);
        m.Pc = (ushort)(cfa + 2);

        for (;;)
        {
            m.Cfa = (ushort)m.Cell(m.Pc);
            m.Pc += 2;
            primitives[m.OpcodeOf(m.Cfa)]();

            if (exit != 0) { break; }
        }

        exit--;

        if (m.Sp != (ushort)m.Cell(m.Rp + 2))
        {
            int by = ((ushort)m.Cell(m.Rp + 2) - m.Sp) / 2;
            throw new ForthStackException(
                by > 0
                    ? $"The Forth word '{NameEnclosing(m.Pc)}' added {by} too many entries to the data stack"
                    : $"The Forth word '{NameEnclosing(m.Pc)}' removed {-by} too many entries from the data stack");
        }

        m.Pc = (ushort)m.PopReturn();
        m.Rp += 2;
    }

    /// <summary>The word whose body contains an address — how the errors name themselves.</summary>
    private string NameEnclosing(int address)
    {
        int at = m.Latest;
        while (at > address) { at = m.Cell(at); }
        return at > 0 ? m.NameOf(at) : "?";
    }

    private void Execute()
    {
        m.Cfa = (ushort)m.Pop();
        primitives[m.OpcodeOf(m.Cfa)]();
    }

    private void Abort() => exit = 999999;

    // ---- the dictionary builders ------------------------------------------------------------------

    /// <summary>
    /// Lays a header down for the counted string at the given address
    /// (<c>Kf_HEAD</c>, <c>Forth.cpp:1730</c>).
    /// </summary>
    /// <remarks>
    /// <b>The padding exists so the link field lands on an even address.</b> The reference computes
    /// it as <c>(22222-(len+1))%2</c> — which is <c>(len+1)%2</c>, written with a large even
    /// constant so C's <c>%</c> cannot return a negative. Kept in that shape because the magic
    /// number is the only clue to why the expression is not simply <c>(len+1) &amp; 1</c>.
    /// </remarks>
    private void Head()
    {
        int origin = m.Pop();
        int here = m.Here;
        int length = m.Byte(origin);
        int offset = (22222 - (length + 1)) % 2;

        for (int i = 0; i < length; i++)
        {
            m.SetByte(here + offset + i, m.Byte(origin + 1 + i));
        }

        for (int i = 0; i < offset; i++) { m.SetByte(here + i, 0); }

        m.SetByte(here + offset + length, length);
        m.SetCell(here + offset + length + 1, 0);       // assume no change to the data stack
        m.SetByte(here + offset + length + 3, 0);       // flags
        m.SetByte(here + offset + length + 4, 0);       // code field
        m.SetCell(here + offset + length + 5, m.Latest);
        m.Latest = (short)(here + offset + length + 5);
        m.Here = (short)(here + offset + length + 7);
    }

    /// <summary>Stamps the next primitive index into the word just created.</summary>
    private void PlusP() => m.SetByte(m.Latest - 1, nextPrimitive++);

    private void Create()
    {
        m.Push(' ');
        Word();
        Head();
    }

    /// <summary>
    /// Reads the next delimited word from the source into <c>HERE</c>
    /// (<c>Kf_WORD</c>, <c>Forth.cpp:1750</c>).
    /// </summary>
    /// <remarks>
    /// <b>A space delimiter also matches a tab, and nothing else does.</b> The reference sets a
    /// second delimiter to <c>'\t'</c> only when the first is <c>' '</c>. A newline is not a
    /// delimiter at all, which is why <c>ExpandKernel</c> replaces every one in
    /// <c>AI_Script.BLK</c> with a space before interpreting the line.
    /// </remarks>
    private void Word()
    {
        char c = (char)(byte)m.Pop();
        char c1 = c == ' ' ? '\t' : c;

        int remaining = m.Cell(ForthMemory.SourceAddress + 2) - m.Cell(ForthMemory.ToInAddress);
        int at = m.Cell(ForthMemory.SourceAddress) + m.Cell(ForthMemory.ToInAddress);

        m.SetByte(m.Here, 0);

        while (remaining > 0 && (m.Byte(at) == c || m.Byte(at) == c1))
        {
            remaining--;
            at++;
        }

        while (remaining > 0 && m.Byte(at) != c && m.Byte(at) != c1)
        {
            m.SetByte(m.Here, m.Byte(m.Here) + 1);
            m.SetByte(m.Here + m.Byte(m.Here), m.Byte(at));
            remaining--;
            at++;
        }

        m.SetByte(m.Here + m.Byte(m.Here) + 1, ' ');

        if (m.Byte(m.Here) != 0) { at++; }

        m.SetCell(ForthMemory.ToInAddress, at - m.Cell(ForthMemory.SourceAddress));
        m.Push(m.Here);
    }

    /// <summary>
    /// Looks a counted string up (<c>Kf_FIND</c>, <c>Forth.cpp:1716</c>):
    /// <c>addr → addr 0</c> when absent, <c>cfa 1</c> when immediate, <c>cfa -1</c> otherwise.
    /// </summary>
    /// <remarks>
    /// <b>It does not skip hidden words.</b> <c>HIDE</c> sets bit 2 of the flags and <c>:</c> uses
    /// it while a definition is being compiled, but nothing here reads that bit — so a word can
    /// find itself mid-definition and recursion compiles rather than referring to the previous
    /// meaning. The flag is written, checked by nothing, and load-bearing only as documentation.
    /// </remarks>
    private void Find()
    {
        int address = m.Pop();
        int length = m.Byte(address);

        for (int cur = m.Latest; cur > 0; cur = m.Cell(cur))
        {
            if (length != m.Byte(cur - 5)) { continue; }

            bool same = true;
            for (int i = 0; i < length && same; i++)
            {
                same = m.Byte(address + 1 + i) == m.Byte(cur - 5 - length + i);
            }

            if (!same) { continue; }

            m.Push(cur);
            m.Push(m.IsImmediate(cur) ? 1 : -1);
            return;
        }

        m.Push(address);
        m.Push(0);
    }

    /// <summary>
    /// <c>'</c> — find the next word or abort (<c>Kf_tick</c>).
    /// </summary>
    private void Tick()
    {
        m.Push(' ');
        Word();
        Find();

        if (m.Pop() == 0)
        {
            Count();
            int count = m.Pop();
            Output.Add(TextAt(m.Pop(), count) + "?");
            Abort();
        }
    }

    private void Count()
    {
        int address = m.Stack(0);
        m.SetStack(0, address + 1);
        m.Push(m.Byte(address));
    }

    // ---- the rest ---------------------------------------------------------------------------------

    private void Lit()
    {
        m.Push(m.Cell(m.Pc));
        m.Pc += 2;
    }

    private void QBranch()
    {
        if (m.Pop() != 0) { m.Pc += 2; } else { m.Pc = (ushort)m.Cell(m.Pc); }
    }

    /// <summary>
    /// <c>?EXIT</c> — leaves the flag <i>and</i> returns when it is true, drops it when false.
    /// </summary>
    /// <remarks>
    /// <b>The truth value stays on the stack on the exiting path.</b> That is what makes
    /// <c>THINK</c>'s chain of <c>TestX ?EXIT</c> work: a test that decides returns its own answer
    /// as the word's result, and one that does not leaves nothing behind.
    /// </remarks>
    private void QuestionExit()
    {
        if (m.Cell(m.Sp) != 0) { exit = 1; } else { m.Pop(); }
    }

    /// <summary>
    /// <c>SP+-</c> — declares the running word's net effect on the data stack, in cells.
    /// </summary>
    private void StackEffect() => m.SetCell(m.Latest - 4, -m.Pop() * 2);

    private void SetImmediate()
    {
        int address = m.Pop();
        m.SetByte(address - 2, m.Byte(address - 2) | 1);
    }

    private void Store()
    {
        m.SetCell(m.Stack(0), m.Stack(2));
        m.Sp += 4;
    }

    private void CStore()
    {
        int address = m.Pop();
        m.SetByte(address, m.Pop());
    }

    private void PlusStore()
    {
        int address = m.Pop();
        int value = m.Pop();
        m.SetCell(address, m.Cell(address) + value);
    }

    private void Swap()
    {
        int n = m.Stack(0);
        m.SetStack(0, m.Stack(2));
        m.SetStack(2, n);
    }

    private void Rot()
    {
        int n = m.Stack(0);
        m.SetStack(0, m.Stack(4));
        m.SetStack(4, m.Stack(2));
        m.SetStack(2, n);
    }

    private void QDup()
    {
        int n = m.Stack(0);
        if (n != 0) { m.Push(n); }
    }

    /// <summary>A comparison, which yields Forth's <c>-1</c> for true rather than <c>1</c>.</summary>
    private void Compare(Func<short, short, bool> test)
    {
        short a = m.Pop();
        short b = m.Pop();
        m.Push(test(a, b) ? -1 : 0);
    }

    private void UmStar()
    {
        uint t = (uint)((ushort)m.Pop() * (ushort)m.Pop());
        m.PushDouble((int)t);
    }

    /// <summary>
    /// <c>M*</c> — declared as a signed mixed multiply and <b>written as an unsigned one</b>.
    /// </summary>
    /// <remarks>
    /// <c>ui32 t = POPSP()*POPSP();</c> multiplies two <c>i16</c> as <c>int</c> and then stores the
    /// result unsigned, so it agrees with a signed <c>M*</c> for every product that fits and
    /// differs from <c>UM*</c> only in that the operands are sign-extended first. Nothing in the
    /// kernel calls it.
    /// </remarks>
    private void MStar()
    {
        int t = m.Pop() * m.Pop();
        m.PushDouble(t);
    }

    private void MuSlashMod()
    {
        uint b = (uint)m.PopDouble();
        uint t = (uint)m.PopDouble();
        m.PushDouble((int)(t / b));
        m.PushDouble((int)(t % b));
    }

    /// <summary>Dumps the data stack to the log (<c>Kf_DEBUG</c>). Kept, since a script may call it.</summary>
    private void Debug()
    {
        Output.Add($"DEBUG DUMP of AI_Script function \"{NameEnclosing(m.Pc)}\"");

        for (int sp = m.Sp; sp < ForthMemory.DataStackBase; sp += 2)
        {
            Output.Add($" [SP+{sp - m.Sp:00}] = 0x{(ushort)m.Cell(sp):x4}");
        }
    }

    /// <summary>
    /// <c>?NUMBER</c> (<c>Kf_QNUMBER</c>, <c>Forth.cpp:1836</c>):
    /// <c>counted-addr → d -1</c> when it parses, <c>c-addr 0</c> when it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A quoted prefix overrides the base for one number.</b> <c>H'…'</c> is hexadecimal,
    /// <c>D'…'</c> decimal and <c>O'…'</c> octal — the quotes are syntax, not a string. The kernel
    /// writes <c>H'20' CONSTANT BL</c>. Any other letter before the quote is not an error and not a
    /// number either; it simply fails to parse.
    /// </para>
    /// <para>
    /// <b>The prefix test needs the closing quote at exactly <c>addr+len</c></b>, so it is the last
    /// character of the word. A number with a trailing quote and no leading one is rejected by the
    /// digit scan instead.
    /// </para>
    /// <para>
    /// <b>Both signs are accepted</b>, <c>+</c> as well as <c>-</c>. And the whole word must be
    /// consumed: <c>CONVERT</c> stops at the first character that is not a digit in the base, and
    /// anything left over makes the parse fail rather than truncate.
    /// </para>
    /// </remarks>
    private void QuestionNumber()
    {
        int address = m.Pop();
        int length = m.Byte(address);

        if (length == 0)
        {
            m.Push(address);
            m.Push(0);
            return;
        }

        int radix = m.Cell(ForthMemory.BaseAddress);
        int column = 0;
        int sign = 1;

        if (length > 3 && (char)(byte)m.Byte(address + 2) == '\''
                       && (char)(byte)m.Byte(address + length) == '\'')
        {
            radix = (char)(byte)m.Byte(address + 1) switch
            {
                'H' => 16,
                'D' => 10,
                'O' => 8,
                _ => 0,
            };

            if (radix == 0)
            {
                m.Push(address);
                m.Push(0);
                return;
            }

            column += 2;
            length -= 3;
        }

        column++;                               // skip the count byte

        if (length > 0)
        {
            char lead = (char)(byte)m.Byte(address + column);
            if (lead == '+') { column++; length--; }
            else if (lead == '-') { column++; length--; sign = -1; }
        }

        int value = 0;
        int at = address + column;
        int end = address + column + length;

        while (true)
        {
            char c = (char)(byte)m.Byte(at);
            if (c >= 'a') { c = (char)(c - 'a' + 'A'); }
            int digit = c >= 'A' ? c - 'A' + 10 : c - '0';

            if (digit < 0 || digit >= radix) { break; }

            value = (value * radix) + digit;
            at++;
        }

        if (at != end)
        {
            m.Push(address);
            m.Push(0);
            return;
        }

        m.PushDouble(sign < 0 ? -value : value);
        m.Push(-1);
    }
}
