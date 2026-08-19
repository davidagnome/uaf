using System.Globalization;
using System.Text.Json;

namespace UAF.Serialization;

/// <summary>
/// Reads the JSON character format — <c>CHARACTER::Export</c>/<c>Import</c>
/// (<c>Shared/Char.cpp:3128</c>, <c>:3303</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A design can ship its characters in either of two unrelated formats.</b>
/// <see cref="CharacterFileReader"/> reads the binary <c>.chr</c>; this reads the JSON one, which
/// <c>SomethingWild</c> uses for <c>Data/Uril Kabo.CHAR</c>. Before this the port rejected that
/// file as "declares version 0.563, below the 0.93 floor" — which reads like a corrupt file and is
/// not one, because the first eight bytes of <c>{"charVersion"…</c> happen to decode as a plausible
/// double.
/// </para>
/// <para>
/// <b>Every value is a string, including the numbers and the booleans.</b> <c>JWriter</c> emits
/// everything through <c>NameAndValue(name, …)</c> after formatting, so <c>"33"</c>, <c>"true"</c>
/// and <c>"51.000000"</c> are all strings on the wire. Nothing here should be read as a JSON
/// number.
/// </para>
/// <para>
/// <b>Four fields are enum <i>names</i>, not indices</b> — gender, alignment, size and status —
/// and their spellings are the editor's tables (<c>Globtext.cpp</c>), which differ in case from the
/// ones GPDL answers with: this format says "Chaotic Neutral" where <c>$Alignment</c> says
/// "CHAOTIC NEUTRAL". An unrecognised name reads as index 0 rather than failing, matching the
/// reference's own tolerance for a table it has outgrown.
/// </para>
/// </remarks>
public static class JsonCharacterReader
{
    /// <summary><c>genderText</c> (<c>Globtext.cpp:630</c>).</summary>
    private static readonly string[] Genders = ["Male", "Female", "Bishop"];

    /// <summary><c>alignmentText</c> (<c>Globtext.cpp:602</c>).</summary>
    private static readonly string[] Alignments =
    [
        "Lawful Good", "Neutral Good", "Chaotic Good",
        "Lawful Neutral", "True Neutral", "Chaotic Neutral",
        "Lawful Evil", "Neutral Evil", "Chaotic Evil",
    ];

    /// <summary><c>CharStatusTypeText</c> (<c>Globtext.cpp:615</c>).</summary>
    private static readonly string[] Statuses =
    [
        "OKAY", "UNCONSCIOUS", "DEAD", "FLED", "PETRIFIED",
        "GONE", "ANIMATED", "TEMP GONE", "RUNNING", "DYING",
    ];

    /// <summary>
    /// <c>CreatureSizeText</c> (<c>Globtext.cpp:112</c>).
    /// </summary>
    /// <remarks>
    /// <b>Three entries, and the enum starts at zero</b>, so "Small" is 0 — there is no "Tiny".
    /// </remarks>
    private static readonly string[] Sizes = ["Small", "Medium", "Large"];

    /// <summary><c>SurfaceType</c> (<c>SurfaceMgr.h:25</c>) — a flag set, written as a list.</summary>
    private static readonly (string Name, int Value)[] SurfaceTypes =
    [
        ("BogusDib", 0), ("CommonDib", 1), ("CombatDib", 2), ("WallDib", 4), ("DoorDib", 8),
        ("BackGndDib", 16), ("OverlayDib", 32), ("IconDib", 64), ("OutdoorCombatDib", 128),
        ("BigPicDib", 256), ("MapDib", 512), ("SmallPicDib", 1024), ("SpriteDib", 2048),
        ("TitleDib", 4096), ("BufferDib", 8192), ("FontDib", 16384), ("MouseDib", 32768),
        ("TransBufferDib", 65536), ("SpecialGraphicsOpaqueDib", 0x20000),
        ("SpecialGraphicsTransparentDib", 0x40000),
    ];

    /// <summary>Whether a file is this format rather than the binary one.</summary>
    /// <remarks>
    /// A cheap look at the first non-space byte. Both formats use the same <c>.CHR</c> and
    /// <c>.CHAR</c> extensions, so the name cannot decide it.
    /// </remarks>
    public static bool IsJson(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        long at = stream.Position;
        try
        {
            for (int b = stream.ReadByte(); b >= 0; b = stream.ReadByte())
            {
                if (!char.IsWhiteSpace((char)b))
                {
                    return b == '{';
                }
            }

            return false;
        }
        finally
        {
            stream.Position = at;
        }
    }

    /// <summary>Reads a whole JSON character file.</summary>
    /// <exception cref="InvalidDataException">When it is not valid JSON, or has no name.</exception>
    public static CharacterRecord Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Not a JSON character file: {e.Message}", e);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"A JSON character file is an object; this is a {root.ValueKind}.");
            }

            return Build(root);
        }
    }

    private static CharacterRecord Build(JsonElement c) =>
        new(CharacterVersion: Int(c, "charVersion"),
            PreSpellNamesKey: 0,
            Type: (byte)Int(c, "type"),
            Race: Str(c, "race"),
            Gender: Index(Str(c, "gender"), Genders),
            ClassId: Str(c, "class"),
            Alignment: Index(Str(c, "alignment"), Alignments),
            AllowInCombat: Int(c, "allowInCombat"),
            Status: Index(Str(c, "status"), Statuses),
            UndeadType: Str(c, "undeadType"),
            CreatureSize: Index(Str(c, "size"), Sizes),
            Name: Str(c, "name"),
            CharacterId: Str(c, "characterID"),
            Thac0: Int(c, "thac0"),
            Morale: Int(c, "morale"),
            Encumbrance: Int(c, "encumbrance"),
            MaxEncumbrance: Int(c, "maxencumbrance"),
            ArmorClass: Int(c, "ac"),
            HitPoints: Int(c, "HP"),
            MaxHitPoints: Int(c, "maxHP"),
            NumberOfHitDice: Real(c, "nbrHitDice"),
            Age: Int(c, "age"),
            MaxAge: Int(c, "maxAge"),
            Birthday: Int(c, "birthday"),
            MaxCureDisease: Int(c, "maxCureDisease"),
            UnarmedDieSmall: Int(c, "unarmedDiceS"),
            UnarmedNumberDieSmall: Int(c, "unarmedNbrDiceS"),
            UnarmedBonus: Int(c, "unarmedDiceBonus"),
            UnarmedDieLarge: Int(c, "unarmedDiceL"),
            UnarmedNumberDieLarge: Int(c, "unarmedNbrDiceL"),
            MaxMovement: (byte)Int(c, "maxMovement"),
            ReadyToTrain: Int(c, "readyToTrain"),
            CanTradeItems: Int(c, "canTradeItems"),
            Abilities: new AbilityScores(
                (byte)Int(c, "str"), (byte)Int(c, "strMod"), (byte)Int(c, "int"),
                (byte)Int(c, "wis"), (byte)Int(c, "dex"), (byte)Int(c, "con"),
                (byte)Int(c, "cha")),
            OpenDoors: (byte)Int(c, "openDoors"),
            OpenMagicDoors: (byte)Int(c, "openMagicDoors"),
            BendBarsLiftGates: (byte)Int(c, "bblg"),
            HitBonus: Int(c, "hitBonus"),
            DamageBonus: Int(c, "dmgBonus"),
            MagicResistance: Int(c, "magicResistance"),
            BaseclassStats: [.. Each(c, "baseclassStats").Select(Baseclass)],
            SkillAdjustments: [.. Each(c, "skillAdjustments").Select(Skill)],
            SpellAdjustments: [.. Each(c, "spellAdjustments").Select(Spell)],
            IsPreGenerated: Int(c, "isPregen"),
            CanBeSaved: Int(c, "canBeSaved"),
            HasLayedOnHandsToday: Int(c, "hasLayedOnHandsToday"),
            Money: Money(c),
            NumberOfAttacks: (float)Real(c, "nbrAttacks"),
            Icon: Pic(Child(c, "icon")),
            IconIndex: Int(c, "iconIndex"),
            OriginalIndex: Int(c, "origIndex"),
            UniquePartyId: (byte)Int(c, "uniquePartyID"),
            DisableTalkIfDead: Int(c, "disableTalkIfDead"),
            TalkEvent: (uint)Int(c, "talkEvent"),
            TalkLabel: Str(c, "talkLabel"),
            ExamineEvent: (uint)Int(c, "examineEvent"),
            ExamineLabel: Str(c, "examineLabel"),
            SpellBook: Book(c),
            DetectingInvisible: Int(c, "detectingInvisible"),
            DetectingTraps: Int(c, "detectingTraps"),
            SpellEffects: [],
            Blockages: [],
            SmallPic: Pic(Child(c, "smallPic")) ?? PicDataWriter.Empty,
            Items: Possessions(c),
            SpecialAbilities: new SpecabBlock([], [], []),
            Attributes: []);

    private static BaseclassStats Baseclass(JsonElement e) =>
        new(Str(e, "baseclassID"), Int(e, "currentLevel"), Int(e, "previousLevel"),
            Int(e, "preDrainLevel"), Int(e, "experience"));

    private static SkillAdjustment Skill(JsonElement e) =>
        new(Str(e, "skillID"), Str(e, "adjustmentID"), Int(e, "value"), (sbyte)Int(e, "type"));

    private static SpellAdjustment Spell(JsonElement e) =>
        new(Str(e, "schoolID"), Str(e, "adjustmentID"), Int(e, "firstLevel"),
            Int(e, "lastLevel"), Int(e, "percent"), Int(e, "bonus"));

    private static MoneySack Money(JsonElement c)
    {
        var money = Child(c, "money");

        return new MoneySack(
            [.. Each(money, "coins").Select(m => Int(m, "qty"))],
            [.. Each(money, "gems").Select(g => new GemType(Int(g, "id"), Int(g, "value")))],
            [.. Each(money, "jewels").Select(j => new GemType(Int(j, "id"), Int(j, "value")))]);
    }

    private static SpellBook Book(JsonElement c)
    {
        var book = Child(c, "spellbook");

        return new SpellBook(
            Int(book, "useLimits"),
            [.. Each(book, "spellList").Select(s => new CharacterSpell(
                Str(s, "name"), Int(s, "memorized"), Int(s, "level"), Int(s, "selected")))]);
    }

    private static ItemList Possessions(JsonElement c) =>
        new([.. Each(c, "possessions").Select(p => new ItemInstance(
                Int(p, "key"), Str(p, "itemID"), LegacyItemId: 0, (uint)Int(p, "ready"),
                Int(p, "quantity"), Int(p, "identified"), Int(p, "charges"),
                (byte)Int(p, "cursed"), Int(p, "paid")))],
            new ReadyItems([]));

    /// <summary>
    /// A <c>PIC_DATA</c>, whose type is a <b>list of flag names</b> rather than a number.
    /// </summary>
    private static PicRecord? Pic(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        int type = 0;
        if (e.TryGetProperty("picType", out var flags) && flags.ValueKind == JsonValueKind.Array)
        {
            foreach (var flag in flags.EnumerateArray())
            {
                string name = flag.GetString() ?? string.Empty;
                foreach (var (surface, value) in SurfaceTypes)
                {
                    if (string.Equals(surface, name, StringComparison.Ordinal))
                    {
                        type |= value;
                    }
                }
            }
        }

        return new PicRecord(
            type, Str(e, "filename"), Int(e, "timeDelay"), Int(e, "numFrame"),
            Int(e, "width"), Int(e, "height"), (uint)Int(e, "flags"), (uint)Int(e, "maxLoops"),
            (uint)Int(e, "style"), Bool(e, "useAlpha") ? 1u : 0u, (ushort)Int(e, "alpha"), 0);
    }

    // -- primitives --------------------------------------------------------------------------

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// A number, which is on the wire as a string.
    /// </summary>
    /// <remarks>
    /// Parsed through <c>double</c> and truncated, because a field declared <c>int</c> here is
    /// sometimes written with a decimal part — <c>nbrHitDice</c> is "51.000000" — and a plain
    /// integer parse would reject it.
    /// </remarks>
    private static int Int(JsonElement e, string name) => (int)Real(e, name);

    private static double Real(JsonElement e, string name) =>
        double.TryParse(Str(e, name), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double parsed)
            ? parsed
            : 0;

    private static bool Bool(JsonElement e, string name) =>
        Str(e, name).Equals("true", StringComparison.OrdinalIgnoreCase);

    private static JsonElement Child(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) ? value : default;

    private static IEnumerable<JsonElement> Each(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty(name, out var list)
        && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
            : [];

    /// <summary>An enum name's index, or 0 when the table does not have it.</summary>
    private static int Index(string name, string[] table)
    {
        for (int i = 0; i < table.Length; i++)
        {
            if (string.Equals(table[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }
}
