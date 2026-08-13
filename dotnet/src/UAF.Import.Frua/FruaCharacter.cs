namespace UAF.Import.Frua;

/// <summary>
/// A DOS FRUA <c>MONST###.DAT</c> record — one monster or NPC
/// (<c>ImportUACCH</c>, <c>UAFWinEd/UAImport.cpp:6038</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The reference reads this as a raw struct</b> — a single
/// <c>fread(&amp;cch, sizeof(cch), 1, fp)</c> — so the file layout <i>is</i> the C layout, padding
/// included. There is no <c>#pragma pack</c> anywhere in <c>UAImport.cpp</c>, so MSVC's natural
/// alignment applies: words on even offsets, dwords on multiples of four. That is why several
/// fields here sit at offsets a byte-by-byte reading of the declaration would not predict.
/// </para>
/// <para>
/// <b>The struct is 432 bytes and the file is 450, so the last 18 bytes are never read.</b>
/// <c>fread</c> asks for <c>sizeof(cch)</c> and stops; whatever FRUA stored past that is not part
/// of the import. The offsets below were derived mechanically from the declaration and then
/// checked against the shipped files rather than trusted — see
/// <c>FruaCharacterTests</c>, where <see cref="MonsterIndex"/> agreeing with the number in each
/// filename is the confirmation that matters.
/// </para>
/// </remarks>
public sealed record FruaCharacter(
    string Name,
    int MonsterIndex,
    byte Race,
    byte CombatMode,
    uint Experience,
    ushort Platinum,
    ushort Gems,
    ushort Jewelry,
    byte Level,
    byte ArmourClassRaw,
    byte HitPoints,
    byte AdjustedHitPoints,
    byte Move,
    byte AdjustedMove,
    byte Morale,
    byte AttacksPerTwoRounds,
    byte DamageDiceCount,
    byte DamageDiceSides,
    byte DamageBonus,
    IReadOnlyList<byte> SavingThrows,
    IReadOnlyList<byte> ClassLevels,
    IReadOnlyList<byte> ItemsCarried,
    IReadOnlyList<byte> ItemQuantities,

    // Everything below is read only by the conversion layer, not by the reader's own tests.
    byte CharClass,
    byte Undead,
    byte Gender,
    byte Alignment,
    byte Status,
    byte Thac0Raw,
    byte AdjustedThac0Raw,
    byte AdjustedArmourClassRaw,
    byte SizeRaw,
    byte MaxHitPoints,
    byte MaxCureDisease,
    byte MagicResistance,
    byte ReadyToTrain,
    byte UniquePartyId,
    byte IconId,
    byte SpecialAbilityFlags,
    byte SpecialAbilityFlags2,
    ushort Age,
    ushort ExperienceValue,
    ushort Encumbrance,
    FruaAbilities Abilities,
    IReadOnlyList<byte> ClassLevelsPreDrain,
    IReadOnlyList<byte> ClassLevelsPreClassChange)
{
    /// <summary>What the reference actually reads: <c>sizeof(ImportUACCH)</c>.</summary>
    public const int Length = 432;

    /// <summary>What the files on disk actually are. The difference is never read.</summary>
    public const int FileLength = 450;

    /// <summary>
    /// The race value that, with <see cref="CombatMode"/> 1, marks a monster rather than an NPC.
    /// </summary>
    /// <remarks>
    /// The reference's test is <c>(cch.race == 6) &amp;&amp; (cch.combatMode == 1)</c>. Anything
    /// else goes down the NPC path and becomes a character instead — <c>HEIRS.DSN</c> ships one:
    /// <c>xelez-dar</c> in <c>MONST109.DAT</c> has race 0.
    /// </remarks>
    public const byte MonsterRace = 6;

    /// <summary>Whether this record imports as a monster rather than as an NPC.</summary>
    public bool IsMonster => Race == MonsterRace && CombatMode == 1;

    /// <summary>
    /// Armour class, undoing the storage.
    /// </summary>
    /// <remarks>
    /// <b>Stored as <c>60 - AC</c></b>, which the declaration says out loud. A stored 59 is AC 1
    /// and a stored 50 is AC 10 — the shipped monsters bear that out, the three high-level ones
    /// sitting at 59 and the NPC at 50.
    /// </remarks>
    public int ArmourClass => 60 - ArmourClassRaw;

    /// <summary>Reads one record from a <c>MONST###.DAT</c>.</summary>
    public static FruaCharacter Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Length)
        {
            throw new InvalidDataException(
                $"a MONST###.DAT needs {Length} bytes to read; this one has {bytes.Length}");
        }

        return new FruaCharacter(
            Name: Text(bytes.Slice(96, 16)),
            MonsterIndex: bytes[397],
            Race: bytes[88],
            CombatMode: bytes[95],
            Experience: Dword(bytes, 68),
            Platinum: Word(bytes, 76),
            Gems: Word(bytes, 78),
            Jewelry: Word(bytes, 80),
            Level: bytes[137],
            ArmourClassRaw: bytes[179],
            HitPoints: bytes[184],
            AdjustedHitPoints: bytes[395],
            Move: bytes[136],
            AdjustedMove: bytes[396],

            // "128 + morale if non-zero", per the declaration -- carried raw.
            Morale: bytes[147],
            AttacksPerTwoRounds: bytes[171],
            DamageDiceCount: bytes[173],
            DamageDiceSides: bytes[175],
            DamageBonus: bytes[177],

            // Paralysis/poison/death, petrification/polymorph, rod/staff/wand, breath, spell.
            SavingThrows: [bytes[131], bytes[132], bytes[133], bytes[134], bytes[135]],

            // Cleric, knight, fighter, paladin, ranger, mage, thief -- the seven in file order.
            ClassLevels: bytes.Slice(157, 7).ToArray(),
            ItemsCarried: bytes.Slice(398, 16).ToArray(),
            ItemQuantities: bytes.Slice(414, 16).ToArray(),

            CharClass: bytes[89],
            Undead: bytes[90],
            Gender: bytes[92],
            Alignment: bytes[93],
            Status: bytes[94],

            // Both THAC0s are stored the same way the armour class is: 60 minus the value.
            Thac0Raw: bytes[127],
            AdjustedThac0Raw: bytes[384],
            AdjustedArmourClassRaw: bytes[385],

            SizeRaw: bytes[130],
            MaxHitPoints: bytes[129],
            MaxCureDisease: bytes[128],
            MagicResistance: bytes[196],
            ReadyToTrain: bytes[197],
            UniquePartyId: bytes[189],
            IconId: bytes[188],
            SpecialAbilityFlags: bytes[191],
            SpecialAbilityFlags2: bytes[192],
            Age: Word(bytes, 82),
            ExperienceValue: Word(bytes, 84),
            Encumbrance: Word(bytes, 86),

            // The seven scores are interleaved with their modifiers, so they are two apart.
            Abilities: new FruaAbilities(
                Strength: bytes[112], Intelligence: bytes[114], Wisdom: bytes[116],
                Dexterity: bytes[118], Constitution: bytes[120], Charisma: bytes[122],
                ExceptionalStrength: bytes[124]),

            ClassLevelsPreDrain: bytes.Slice(150, 7).ToArray(),
            ClassLevelsPreClassChange: bytes.Slice(164, 7).ToArray());
    }

    /// <summary>Base THAC0, undoing the same <c>60 - x</c> storage the armour class uses.</summary>
    public int Thac0 => 60 - Thac0Raw;

    /// <summary>THAC0 after equipment and bonuses.</summary>
    public int AdjustedThac0 => 60 - AdjustedThac0Raw;

    /// <summary>Armour class after equipment and bonuses.</summary>
    public int AdjustedArmourClass => 60 - AdjustedArmourClassRaw;

    /// <summary>
    /// Combat-icon size, 1&#8211;5, with the "large even if 1x1" flag stripped off.
    /// </summary>
    public int Size => SizeRaw & 0x7F;

    /// <summary>
    /// Whether the design forced this creature to count as large regardless of its icon.
    /// </summary>
    /// <remarks>The high bit of the size byte, which the declaration spells out.</remarks>
    public bool ForcedLarge => (SizeRaw & 0x80) != 0;

    /// <summary>
    /// Morale as a percentage, with the flag bit stripped and zero read as fifty.
    /// </summary>
    /// <remarks>
    /// <b>Stored as "128 + morale if non-zero".</b> Both import paths mask with <c>0x7F</c> and
    /// then substitute 50 for zero, so a creature never imports as unshakeably cowardly by
    /// accident.
    /// </remarks>
    public int MoraleValue => (Morale & 0x7F) is var m and > 0 ? m : 50;

    /// <summary>Attacks per round — the file stores attacks per <i>two</i> rounds.</summary>
    public double AttacksPerRound => AttacksPerTwoRounds / 2.0;

    /// <summary>
    /// Hit dice, which a level-zero creature gets half of rather than none.
    /// </summary>
    public double HitDice => Level <= 0 ? 0.5 : Level;

    /// <summary>
    /// Reads every <c>MONST###.DAT</c> a design carries, keyed by the number in its name.
    /// </summary>
    /// <remarks>
    /// <b>The reference enumerates the directory rather than counting</b>
    /// (<c>ImportFRUAData.cpp:159</c>), which is why this globs: the numbers are sparse — a design
    /// may ship 101, 102, 108 and 109 and nothing between.
    /// </remarks>
    public static IReadOnlyDictionary<int, FruaCharacter> ReadAll(string designDirectory)
    {
        ArgumentNullException.ThrowIfNull(designDirectory);

        var found = new Dictionary<int, FruaCharacter>();

        foreach (var (name, path) in FruaFiles.Index(designDirectory))
        {
            if (!name.StartsWith("MONST", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".DAT", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(name.AsSpan(5, name.Length - 9), out int number))
            {
                continue;
            }

            found[number] = Read(File.ReadAllBytes(path));
        }

        return found;
    }

    private static ushort Word(ReadOnlySpan<byte> b, int at) =>
        (ushort)(b[at] | (b[at + 1] << 8));

    private static uint Dword(ReadOnlySpan<byte> b, int at) =>
        (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24));

    private static string Text(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return FruaGameData.TextEncoding.GetString(field[..(end < 0 ? field.Length : end)]).Trim();
    }
}

/// <summary>
/// The seven ability scores a <c>MONST###.DAT</c> carries.
/// </summary>
/// <remarks>
/// Each score is followed in the file by a modifier byte, which no import path reads — the
/// modifiers are recomputed from race and class rather than carried across.
/// </remarks>
public sealed record FruaAbilities(
    byte Strength, byte Intelligence, byte Wisdom, byte Dexterity,
    byte Constitution, byte Charisma, byte ExceptionalStrength);
