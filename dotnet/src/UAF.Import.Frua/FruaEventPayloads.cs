namespace UAF.Import.Frua;

/// <summary>
/// A <see cref="FruaEventType.TextStatement"/>'s payload
/// (<c>addTextEvent</c>, <c>UAFWinEd/UAImport.cpp:3540</c>).
/// </summary>
/// <remarks>
/// <b>The most common event by a wide margin</b> — 443 of the 1,040 in <c>HEIRS.DSN</c>.
/// </remarks>
/// <param name="Text">
/// The five string slots joined, with highlight markers already inserted.
/// </param>
/// <param name="ForceBackup">Whether the party is pushed back a square first.</param>
/// <param name="WaitForReturn">Whether the text waits on a keypress.</param>
/// <param name="PictureSlot">Which art slot is shown, 0 for none.</param>
/// <param name="PictureIsLarge">The high bit of the flags byte, which picks the large art.</param>
/// <param name="SoundSlot">Which sound plays, 0 for none.</param>
public sealed record FruaTextEvent(
    string Text, bool ForceBackup, bool WaitForReturn,
    byte PictureSlot, bool PictureIsLarge, byte SoundSlot)
{
    /// <summary>
    /// The marker the reference wraps a highlighted chunk in, at both ends.
    /// </summary>
    /// <remarks>
    /// Not an escape the format defines — it is the engine's own inline markup, inserted during
    /// import. A chunk with its bit set comes out as <c>/h…/h</c>.
    /// </remarks>
    public const string HighlightMarker = "/h";

    /// <summary>
    /// Reads the payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The text is five separate strings concatenated, not one.</b> Slots at offsets 9, 11, 13,
    /// 15 and 17 are looked up in turn and joined. Each string is capped at 228 characters by the
    /// decoder, so five is how FRUA expresses a long passage — and any of them may be absent, in
    /// which case it contributes nothing.
    /// </para>
    /// <para>
    /// <b>Each chunk has its own highlight bit</b>, in the flags byte at offset 8: 4, 8, 16, 32
    /// and 64 for chunks one to five. A set bit wraps that chunk in <c>/h</c> at both ends, so
    /// highlighting is per-chunk rather than per-event.
    /// </para>
    /// <para>
    /// <b><c>WaitForReturn</c> is any of five bits, not one.</b> The reference ORs masks 1, 2, 4,
    /// 8 and 16 of the byte at offset 5 — so any pause style at all means "wait".
    /// </para>
    /// </remarks>
    public static FruaTextEvent Read(FruaEvent e, FruaStringTable strings)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(strings);

        byte control = e.Byte(5);
        byte flags = e.Byte(8);
        bool large = (flags & 128) != 0;
        flags &= 127;

        var text = new System.Text.StringBuilder();

        foreach (var (at, bit) in new[] { (9, 4), (11, 8), (13, 16), (15, 32), (17, 64) })
        {
            string chunk = strings.Get(e.Word(at)) ?? string.Empty;
            bool highlight = (flags & bit) == bit;

            if (highlight)
            {
                text.Append(HighlightMarker);
            }

            text.Append(chunk);

            if (highlight)
            {
                text.Append(HighlightMarker);
            }
        }

        return new FruaTextEvent(
            Text: text.ToString(),
            ForceBackup: (control & 32) == 32,
            WaitForReturn: (control & (1 | 2 | 4 | 8 | 16)) != 0,
            PictureSlot: e.Byte(7),
            PictureIsLarge: large,
            SoundSlot: e.Byte(19));
    }
}

/// <summary>Where a transfer event sends the party facing.</summary>
public enum FruaTransferFacing
{
    North,
    East,
    South,
    West,
}

/// <summary>
/// The payload shared by <see cref="FruaEventType.Teleporter"/>,
/// <see cref="FruaEventType.Stairs"/> and <see cref="FruaEventType.TransferModule"/>
/// (<c>addTeleporterEvent</c>/<c>addStairsEvent</c>, <c>UAFWinEd/UAImport.cpp:3657</c>).
/// </summary>
/// <remarks>
/// <b>The three readers are the same code three times over.</b> They differ only in which event
/// class they cast to, so one reader serves all three here — 187 of <c>HEIRS.DSN</c>'s events
/// between them.
/// </remarks>
public sealed record FruaTransferEvent(
    ushort TextSlot, ushort ConfirmTextSlot,
    byte PictureSlot, bool PictureIsLarge,
    bool AskYesNo, bool TransferOnYes,
    FruaTransferFacing Facing,
    byte DestinationX, byte DestinationY,
    int DestinationEntryPoint, bool ExecuteDestinationEvent)
{
    /// <summary>Reads the payload.</summary>
    /// <remarks>
    /// <para>
    /// <b>The facing is a two-bit field tested as masks, and the order matters.</b> The reference
    /// asks for 12 first — both bits — then 4, then 8, falling through to north. So 4|8 is west,
    /// 4 alone east, 8 alone south. Testing 4 before 12 would turn every west into an east.
    /// </para>
    /// <para>
    /// <b><c>transferOnYes</c> is inverted</b>: the reference reads <c>((temp &amp; 64) == 0)</c>,
    /// so the bit being <i>set</i> means transfer on <i>no</i>.
    /// </para>
    /// <para>
    /// <b>An entry point of 0 becomes -1, i.e. none.</b> The stored value is decremented to make
    /// it zero-based, and the reference then maps the result 0 back to -1 — so entry point 1 and
    /// entry point 0 both mean "use the coordinates instead".
    /// </para>
    /// </remarks>
    public static FruaTransferEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);
        bool large = (flags & 128) != 0;
        flags &= 127;

        byte destination = e.Byte(13);
        int entryPoint = -1;

        if ((destination & 1) == 1)
        {
            entryPoint = e.Byte(14) - 1;
            if (entryPoint == 0)
            {
                entryPoint = -1;
            }
        }

        return new FruaTransferEvent(
            TextSlot: e.Word(5),
            ConfirmTextSlot: e.Word(11),
            PictureSlot: e.Byte(7),
            PictureIsLarge: large,
            AskYesNo: (flags & 32) == 32,
            TransferOnYes: (flags & 64) == 0,
            Facing: FacingOf(flags),
            DestinationX: e.Byte(10),
            DestinationY: e.Byte(9),
            DestinationEntryPoint: entryPoint,
            ExecuteDestinationEvent: (destination & 4) == 4);
    }

    private static FruaTransferFacing FacingOf(byte flags)
    {
        // 12 first: it is both bits, and either of the narrower tests would match it.
        if ((flags & 12) == 12) { return FruaTransferFacing.West; }
        if ((flags & 4) == 4) { return FruaTransferFacing.East; }
        if ((flags & 8) == 8) { return FruaTransferFacing.South; }
        return FruaTransferFacing.North;
    }
}
