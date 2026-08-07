namespace UAF.Scripting;

/// <summary>
/// The Forth VM's one block of memory, and the two stacks and the dictionary that live in it
/// (<c>char m[MAX_MEM]</c> and the <c>iMEM</c>/<c>cMEM</c> macros, <c>Forth.cpp:1645</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Ten thousand bytes, addressed as 16-bit words, and that is the whole machine.</b> Cells are
/// signed 16-bit little-endian; addresses are <c>ui16</c> and wrap rather than fault. Everything —
/// source text, dictionary, both stacks — is at some offset in this array.
/// </para>
/// <para>
/// <b>The stacks grow downwards from the top and are not bounds-checked.</b> The data stack starts
/// at <see cref="DataStackBase"/> (1,000 bytes below the top) and the return stack at the very top,
/// so a data stack more than 500 cells deep runs into the return stack. The reference does not
/// notice; the port does not either, because noticing would change which programs run.
/// </para>
/// <para>
/// <b>A dictionary header is laid out backwards from its link field.</b> Given the address the
/// link occupies — which is what <c>LATEST</c> holds and what <c>FIND</c> answers:
/// </para>
/// <code>
///   cur-5-len …    the name, len bytes
///   cur-5          the name's length
///   cur-4, cur-3   the declared stack effect, in bytes, as a cell
///   cur-2          flags: bit 0 immediate, bit 2 hidden
///   cur-1          the primitive index, or the index of docolon
///   cur, cur+1     the link to the previous word
///   cur+2 …        the body
/// </code>
/// <para>
/// <b>So a word is found by its link and read by counting backwards.</b> There is no separate name
/// field pointer and no header record; the length byte at <c>cur-5</c> is what makes the name
/// findable at all.
/// </para>
/// </remarks>
public sealed class ForthMemory
{
    /// <summary>Bytes of memory (<c>MAX_MEM</c>).</summary>
    public const int Size = 10000;

    /// <summary>The dictionary pointer's own address (<c>m_DP</c>). <c>HERE</c> is its contents.</summary>
    public const int DpAddress = 2;

    /// <summary>Where the most recently defined word's link field is (<c>m_LATEST</c>).</summary>
    public const int LatestAddress = 4;

    /// <summary>The input source: an address at 6 and a length at 8 (<c>m_tickSOURCE</c>).</summary>
    public const int SourceAddress = 6;

    /// <summary>How far the interpreter has read into the source (<c>m_2IN</c>, i.e. <c>&gt;IN</c>).</summary>
    public const int ToInAddress = 10;

    /// <summary>0 interpreting, 1 compiling (<c>m_STATE</c>).</summary>
    public const int StateAddress = 12;

    /// <summary>The number base (<c>m_BASE</c>). Set to 10 at boot and never changed by the kernel.</summary>
    public const int BaseAddress = 14;

    /// <summary>Where the dictionary starts (<c>DP0</c>) — immediately after the variables above.</summary>
    public const int DictionaryStart = 16;

    /// <summary>The empty data stack pointer (<c>SP0</c>).</summary>
    public const int DataStackBase = Size - 1000;

    /// <summary>The empty return stack pointer (<c>RP0</c>).</summary>
    public const int ReturnStackBase = Size;

    private readonly byte[] bytes = new byte[Size];

    /// <summary>The data stack pointer. Counts down as things are pushed.</summary>
    public ushort Sp { get; set; } = DataStackBase;

    /// <summary>The return stack pointer.</summary>
    public ushort Rp { get; set; } = ReturnStackBase;

    /// <summary>The instruction pointer, an address inside a colon word's body.</summary>
    public ushort Pc { get; set; }

    /// <summary>
    /// The word currently executing, by its link address.
    /// </summary>
    /// <remarks>
    /// Named for the code field it is nearly. <c>cfa-1</c> is the primitive index and
    /// <c>cfa+2</c> the body, so it is one byte past what the name suggests.
    /// </remarks>
    public ushort Cfa { get; set; }

    /// <summary>Raw bytes, for a test that wants to look.</summary>
    public byte[] Bytes => bytes;

    /// <summary>One byte (<c>cMEM</c>), signed as C's <c>char</c> is on this target.</summary>
    public sbyte Byte(int address) => (sbyte)bytes[(ushort)address];

    /// <inheritdoc cref="Byte"/>
    public void SetByte(int address, int value) => bytes[(ushort)address] = (byte)value;

    /// <summary>One 16-bit cell (<c>iMEM</c>), signed and little-endian.</summary>
    public short Cell(int address)
    {
        ushort at = (ushort)address;
        return (short)(bytes[at] | (bytes[(ushort)(at + 1)] << 8));
    }

    /// <inheritdoc cref="Cell"/>
    public void SetCell(int address, int value)
    {
        ushort at = (ushort)address;
        bytes[at] = (byte)value;
        bytes[(ushort)(at + 1)] = (byte)(value >> 8);
    }

    /// <summary>
    /// A double cell (<c>dFETCH</c>) — <b>high half first</b>, unlike the byte order within a cell.
    /// </summary>
    public int DoubleCell(int address) =>
        (Cell(address) << 16) | (ushort)Cell(address + 2);

    /// <inheritdoc cref="DoubleCell"/>
    public void SetDoubleCell(int address, int value)
    {
        SetCell(address, value >> 16);
        SetCell(address + 2, value);
    }

    // ---- the stacks -----------------------------------------------------------------------------

    /// <summary>The cell <paramref name="offset"/> bytes into the data stack (<c>iSTK</c>).</summary>
    public short Stack(int offset) => Cell(Sp + offset);

    /// <inheritdoc cref="Stack"/>
    public void SetStack(int offset, int value) => SetCell(Sp + offset, value);

    public short Pop()
    {
        Sp += 2;
        return Cell(Sp - 2);
    }

    public void Push(int value)
    {
        Sp -= 2;
        SetCell(Sp, value);
    }

    public int PopDouble()
    {
        Sp += 4;
        return (Cell(Sp - 4) << 16) | (ushort)Cell(Sp - 2);
    }

    public void PushDouble(int value)
    {
        Sp -= 4;
        SetCell(Sp + 2, value);
        SetCell(Sp, value >> 16);
    }

    public short PopReturn()
    {
        Rp += 2;
        return Cell(Rp - 2);
    }

    public void PushReturn(int value)
    {
        Rp -= 2;
        SetCell(Rp, value);
    }

    // ---- the dictionary -------------------------------------------------------------------------

    /// <summary>The dictionary pointer's contents — Forth's <c>HERE</c>.</summary>
    public short Here
    {
        get => Cell(DpAddress);
        set => SetCell(DpAddress, value);
    }

    /// <summary>The link address of the most recent definition.</summary>
    public short Latest
    {
        get => Cell(LatestAddress);
        set => SetCell(LatestAddress, value);
    }

    /// <summary>The name of the word whose link is at <paramref name="link"/>.</summary>
    public string NameOf(int link)
    {
        int length = Byte(link - 5);
        var name = new char[length];

        for (int i = 0; i < length; i++)
        {
            name[i] = (char)(byte)Byte(link - 5 - length + i);
        }

        return new string(name);
    }

    /// <summary>Whether the word runs while compiling (<c>cMEM(cur-2) &amp; 1</c>).</summary>
    public bool IsImmediate(int link) => (Byte(link - 2) & 1) != 0;

    /// <summary>Its primitive index, or the index of <c>docolon</c> for a colon definition.</summary>
    public int OpcodeOf(int link) => Byte(link - 1);

    /// <summary>
    /// Its declared stack effect in bytes (<c>iMEM(cur-4)</c>), which <c>docolon</c> checks on return.
    /// </summary>
    public short StackEffectOf(int link) => Cell(link - 4);

    /// <summary>Every word in the dictionary, newest first — for tests and for debugging.</summary>
    public IEnumerable<(int Link, string Name)> Words()
    {
        for (int cur = Latest; cur > 0; cur = Cell(cur))
        {
            yield return (cur, NameOf(cur));
        }
    }
}
