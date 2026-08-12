namespace UAF.Import.Frua;

/// <summary>
/// One of the six services a small town can contain.
/// </summary>
/// <remarks>
/// The values are the bits of the flags byte at offset 8.
/// </remarks>
[Flags]
public enum FruaTownServices
{
    None = 0,
    Temple = 1,
    TrainingHall = 2,
    Shop = 4,
    Inn = 8,
    Tavern = 16,
    Vault = 32,
}

/// <summary>
/// A <see cref="FruaEventType.SmallTown"/>'s payload
/// (<c>addSmallTownEvent</c>, <c>UAFWinEd/UAImport.cpp:2111</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a generator, not a plain payload.</b> One flag byte spawns up to six <i>child</i>
/// events — a temple, training hall, shop, inn, tavern and vault — each chained to the parent and
/// filled in from bytes scattered across the record. The child events are synthesised rather than
/// read: their prompts are hard-coded English in the reference, and a writer aiming at
/// byte-identity has to emit those exact strings.
/// </para>
/// <para>
/// <b>Its shop reuses the same three-byte stock groups as a standalone shop</b>, at offsets 15–17
/// and 18–20 — two groups where a full shop has four.
/// </para>
/// </remarks>
public sealed record FruaSmallTownEvent(
    ushort TextSlot, byte PictureSlot, bool PictureIsLarge,
    FruaTownServices Services,
    FruaCostFactor TempleCost, int TempleMaxLevel,
    FruaCostFactor TrainingCost, byte TrainingClassFlags,
    FruaCostFactor ShopCost, IReadOnlyList<byte> ShopItems,
    ushort TavernTaleSlot)
{
    /// <summary>The prompt the reference hard-codes for each child, verbatim.</summary>
    /// <remarks>
    /// These are not read from the design — the reference assigns them as C string literals. A
    /// writer must reproduce them exactly, including the capitals.
    /// </remarks>
    public const string TempleText = "WELCOME TO THE TEMPLE";

    /// <inheritdoc cref="TempleText"/>
    public const string TempleText2 = "HOW MAY WE AID YOU?";

    /// <inheritdoc cref="TempleText"/>
    public const string TrainingHallText = "WELCOME TO THE TRAINING HALL";

    /// <inheritdoc cref="TempleText"/>
    public const string ShopText = "WELCOME TO THE SHOP";

    /// <inheritdoc cref="TempleText"/>
    public const string InnText = "WELCOME TO THE INN";

    /// <inheritdoc cref="TempleText"/>
    public const string VaultText = "WELCOME TO THE VAULT";

    /// <summary>
    /// <b>The generated training hall's classes are decoded here too</b>, the same
    /// <c>NotImplemented</c> gap the standalone hall has (<c>NotImplemented(0x3cde3)</c>) and the
    /// same reason for closing it.
    /// </summary>
    public const string ClassesAreImportedHere =
        "addSmallTownEvent's generated hall has its class flags commented out; decoded here";

    /// <summary>Which classes the generated training hall teaches.</summary>
    public FruaTrainedClasses Trains => (FruaTrainedClasses)(TrainingClassFlags & 0x3F);

    /// <summary>The base price the training cost factor multiplies.</summary>
    public const int TrainingBaseCost = 1000;

    /// <summary>Reads the payload.</summary>
    /// <remarks>
    /// <para>
    /// <b>The temple's maximum spell level is a descending mask ladder in steps of 32</b> — 224,
    /// 192, 160, 128, 96, 64, 32 mapping to levels 7 down to 1 — which is a switch on the byte's
    /// top three bits. A byte below 32 leaves the level at whatever the event was constructed with,
    /// reported here as 0.
    /// </para>
    /// <para>
    /// <b>The shop's cost factor is masked to five bits</b> (<c>cost &amp; 31</c>) where every
    /// other cost byte in the format is taken whole — so a small town's shop cannot express the
    /// factors above 19 that <see cref="FruaCost.Factor"/> maps, though none of them are reachable
    /// anyway.
    /// </para>
    /// </remarks>
    public static FruaSmallTownEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);
        byte templeLevel = e.Byte(10);

        // The generated shop takes two three-byte groups, not the four a standalone shop reads.
        var items = new List<byte>();
        FruaShopEvent.DecodeGroup(items, e.Byte(15), e.Byte(16), e.Byte(17));
        FruaShopEvent.DecodeGroup(items, e.Byte(18), e.Byte(19), e.Byte(20));

        return new FruaSmallTownEvent(
            TextSlot: e.Word(5),
            PictureSlot: e.Byte(7),

            // The picture's high bit is read from the SAME byte as the service flags, and the
            // reference does not mask it off before testing them -- bit 128 is simply not a service.
            PictureIsLarge: (flags & 128) != 0,
            Services: (FruaTownServices)(flags & 0x3F),
            TempleCost: FruaCost.Factor(e.Byte(13)),
            TempleMaxLevel: MaxSpellLevel(templeLevel),
            TrainingCost: FruaCost.Factor(e.Byte(13)),
            TrainingClassFlags: e.Byte(9),
            ShopCost: FruaCost.Factor((byte)(e.Byte(10) & 31)),
            ShopItems: items,
            TavernTaleSlot: e.Word(11));
    }

    /// <summary>The training hall's cost, after its factor is applied.</summary>
    public double TrainingCostValue => FruaCost.Multiplier(TrainingCost) * TrainingBaseCost;

    private static int MaxSpellLevel(byte stored) => (stored & 224) switch
    {
        224 => 7,
        192 => 6,
        160 => 5,
        128 => 4,
        96 => 3,
        64 => 2,
        32 => 1,
        _ => 0,
    };
}
