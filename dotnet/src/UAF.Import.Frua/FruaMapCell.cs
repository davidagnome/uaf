namespace UAF.Import.Frua;

/// <summary>
/// What stops a party leaving a square in one direction (<c>BlockageType</c>,
/// <c>Shared/Level.h:29</c>).
/// </summary>
/// <remarks>
/// <b>These values are the <i>engine's</i> ordering, not FRUA's.</b> The two disagree, and not
/// only at the edges: FRUA stores 2 for a locked door where the engine's enum has 4, and 14 for a
/// blocked wall where the engine has 2. Casting the stored nibble straight to this enum would
/// silently turn every locked door into a wall and every wall into a locked door.
/// <see cref="FruaMapCell.Blockage"/> does the mapping the reference does.
/// </remarks>
public enum FruaBlockage
{
    Open = 0,
    OpenSecret = 1,
    Blocked = 2,
    FalseDoor = 3,
    Locked = 4,
    LockedSecret = 5,
    LockedWizard = 6,
    LockedWizardSecret = 7,
    LockedKey1 = 8,
    LockedKey2 = 9,
    LockedKey3 = 10,
    LockedKey4 = 11,
    LockedKey5 = 12,
    LockedKey6 = 13,
    LockedKey7 = 14,
    LockedKey8 = 15,
}

/// <summary>
/// One square of a DOS FRUA level (<c>UAImportMapCell</c>, <c>UAFWinEd/UAImport.cpp:1602</c>).
/// </summary>
/// <remarks>
/// <para>
/// Six bytes: four walls in north, east, south, west order, then an event index and a byte packing
/// both the backdrop and the zone.
/// </para>
/// <para>
/// <b>Every wall byte carries two fields.</b> The low nibble is the wall slot — an index into the
/// level's three wall slots — and the high nibble is the blockage. A cell with no wall still has a
/// blockage nibble, which is how an invisible barrier is expressed.
/// </para>
/// </remarks>
/// <param name="NorthWall">Raw byte: slot in the low nibble, blockage in the high.</param>
/// <param name="EastWall">Raw byte.</param>
/// <param name="SouthWall">Raw byte.</param>
/// <param name="WestWall">Raw byte.</param>
/// <param name="EventIndex">Which of the level's 100 events sits here, or 0 for none.</param>
/// <param name="BackdropZone">Raw byte: backdrop in the low two bits, zone above them.</param>
public readonly record struct FruaMapCell(
    byte NorthWall, byte EastWall, byte SouthWall, byte WestWall,
    byte EventIndex, byte BackdropZone)
{
    /// <summary>Bytes per cell on disk.</summary>
    public const int Length = 6;

    /// <summary>How many cells a level file stores, whatever the level's dimensions.</summary>
    /// <remarks>
    /// A fixed 576 regardless of <c>width × height</c>; the largest shipped level,
    /// 38 × 15, uses 570 of them. The rest is padding the reference reads and never indexes.
    /// </remarks>
    public const int PerLevel = 576;

    /// <summary>Reads one cell.</summary>
    public static FruaMapCell Read(ReadOnlySpan<byte> bytes) =>
        new(bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5]);

    /// <summary>The raw wall byte for one direction.</summary>
    public byte WallByte(FruaFacing facing) => facing switch
    {
        FruaFacing.North => NorthWall,
        FruaFacing.East => EastWall,
        FruaFacing.South => SouthWall,
        FruaFacing.West => WestWall,
        _ => 0,
    };

    /// <summary>Which of the level's wall slots this side draws, 0 meaning none.</summary>
    public int WallSlot(FruaFacing facing) => WallByte(facing) & 0x0F;

    /// <summary>
    /// What blocks this side (<c>ConvertUABlockage</c>, <c>UAImport.cpp:1652</c>).
    /// </summary>
    /// <remarks>
    /// <b>The mapping is not the identity.</b> FRUA's nibble runs open, open-secret, locked,
    /// locked-secret, wizard, wizard-secret, then eight keyed doors, then blocked and false-door
    /// last; the engine's enum puts blocked and false-door third and fourth. The reference spells
    /// the correspondence out case by case and so does this.
    /// </remarks>
    public FruaBlockage Blockage(FruaFacing facing) => (WallByte(facing) >> 4) switch
    {
        0 => FruaBlockage.Open,
        1 => FruaBlockage.OpenSecret,
        2 => FruaBlockage.Locked,
        3 => FruaBlockage.LockedSecret,
        4 => FruaBlockage.LockedWizard,
        5 => FruaBlockage.LockedWizardSecret,
        6 => FruaBlockage.LockedKey1,
        7 => FruaBlockage.LockedKey2,
        8 => FruaBlockage.LockedKey3,
        9 => FruaBlockage.LockedKey4,
        10 => FruaBlockage.LockedKey5,
        11 => FruaBlockage.LockedKey6,
        12 => FruaBlockage.LockedKey7,
        13 => FruaBlockage.LockedKey8,
        14 => FruaBlockage.Blocked,
        15 => FruaBlockage.FalseDoor,
        _ => FruaBlockage.Open,
    };

    /// <summary>
    /// Whether an overland level blocks movement this way
    /// (<c>ConvertUAOverlandBlockage</c>, <c>UAImport.cpp:1695</c>).
    /// </summary>
    /// <remarks>
    /// <b>Overland levels recognise only two of the sixteen blockages.</b> A wilderness square is
    /// either passable or not — locks, secrets and keyed doors mean nothing there — so only 14 and
    /// 15 stop the party, and every other value is open however door-like it looks.
    /// </remarks>
    public bool IsOverlandBlocked(FruaFacing facing) => (WallByte(facing) >> 4) is 14 or 15;

    /// <summary>
    /// Which of the level's four backdrops this square draws, one-based
    /// (<c>ConvertUABackdropSlot</c>, <c>UAImport.cpp:1974</c>).
    /// </summary>
    /// <remarks>
    /// The low two bits, plus one — so the value is always 1 to 4 and every square names a
    /// backdrop. Turning that into art needs the level's own four backdrop slots.
    /// </remarks>
    public int BackdropIndex => (BackdropZone & 0x03) + 1;

    /// <summary>
    /// Which zone this square belongs to, 0 to 7 (<c>ConvertUAZone</c>, <c>UAImport.cpp:1638</c>).
    /// </summary>
    /// <remarks>
    /// <b>Read from the same byte as the backdrop, above its low two bits</b>, and the reference
    /// enumerates the seven cases rather than shifting — 4 is zone 1, 8 is zone 2, and so on to 28
    /// for zone 7. **Anything above 28 falls to zone 0**, which a plain shift would not do: the
    /// switch has a default and no case beyond 28.
    /// </remarks>
    public int Zone => (BackdropZone & 0xFC) switch
    {
        4 => 1,
        8 => 2,
        12 => 3,
        16 => 4,
        20 => 5,
        24 => 6,
        28 => 7,
        _ => 0,
    };
}
