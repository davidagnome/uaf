using System.Globalization;
using System.Text;

namespace UAF.Serialization;

/// <summary>
/// Writes the JSON character format — <c>CHARACTER::Export</c> (<c>Shared/Char.cpp:3128</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This one can be byte-identical, and that is unusual here.</b> Every binary format in the
/// port restamps its version on save, so no shipped file comes back unchanged (docs/PORTING-PLAN.md
/// §12). JSON carries its version as an ordinary field, so a character written back is the same
/// bytes — which makes it the only format where the round trip can assert the strongest thing.
/// </para>
/// <para>
/// <b>The layout is <c>JWriter</c>'s and is reproduced deliberately.</b> Three spaces per level,
/// no space after a colon, one member per line — except <c>spellList</c>, whose entries are
/// written one to a line whole. Two array styles is not tidiness on the reference's part; it is
/// what its <c>StartList</c>/<c>Value</c> pair happens to produce, and matching it is the whole
/// difference between identical bytes and a diff on every line.
/// </para>
/// <para>
/// <b>Three fields are boolean words and two are fixed-point.</b> <c>useLimits</c>,
/// <c>allowCentering</c> and <c>useAlpha</c> are "true"/"false"; <c>nbrHitDice</c> and
/// <c>nbrAttacks</c> are <c>%f</c>, so 51 goes out as "51.000000". Everything else is a decimal
/// integer or a bare string.
/// </para>
/// </remarks>
public static class JsonCharacterWriter
{
    /// <summary>Writes a character as the reference's exporter would.</summary>
    public static string Write(CharacterRecord character)
    {
        ArgumentNullException.ThrowIfNull(character);

        var text = new StringBuilder();
        var w = new Writer(text);

        w.Open();

        w.Value("charVersion", character.CharacterVersion);
        w.Value("type", character.Type);
        w.Value("race", character.Race);
        w.Name("gender", Lookup(character.Gender, Genders));
        w.Value("class", character.ClassId);
        w.Name("alignment", Lookup(character.Alignment, Alignments));
        w.Value("allowInCombat", character.AllowInCombat);
        w.Value("undeadType", character.UndeadType);
        w.Name("size", Lookup(character.CreatureSize, Sizes));
        w.Value("name", character.Name);
        w.Value("characterID", character.CharacterId);
        w.Value("thac0", character.Thac0);
        w.Value("morale", character.Morale);
        w.Value("encumbrance", character.Encumbrance);
        w.Value("maxencumbrance", character.MaxEncumbrance);
        w.Value("ac", character.ArmorClass);
        w.Value("HP", character.HitPoints);
        w.Name("status", Lookup(character.Status, Statuses));
        w.Value("maxHP", character.MaxHitPoints);
        w.Real("nbrHitDice", character.NumberOfHitDice);
        w.Value("age", character.Age);
        w.Value("maxAge", character.MaxAge);
        w.Value("birthday", character.Birthday);
        w.Value("maxCureDisease", character.MaxCureDisease);
        w.Value("unarmedDiceS", character.UnarmedDieSmall);
        w.Value("unarmedNbrDiceS", character.UnarmedNumberDieSmall);
        w.Value("unarmedDiceBonus", character.UnarmedBonus);
        w.Value("unarmedDiceL", character.UnarmedDieLarge);
        w.Value("unarmedNbrDiceL", character.UnarmedNumberDieLarge);
        w.Value("maxMovement", character.MaxMovement);
        w.Value("readyToTrain", character.ReadyToTrain);
        w.Value("canTradeItems", character.CanTradeItems);
        w.Value("str", character.Abilities.Strength);
        w.Value("strMod", character.Abilities.StrengthMod);
        w.Value("int", character.Abilities.Intelligence);
        w.Value("wis", character.Abilities.Wisdom);
        w.Value("dex", character.Abilities.Dexterity);
        w.Value("con", character.Abilities.Constitution);
        w.Value("cha", character.Abilities.Charisma);
        w.Value("openDoors", character.OpenDoors);
        w.Value("openMagicDoors", character.OpenMagicDoors);
        w.Value("bblg", character.BendBarsLiftGates);
        w.Value("hitBonus", character.HitBonus);
        w.Value("dmgBonus", character.DamageBonus);
        w.Value("magicResistance", character.MagicResistance);

        w.Array("baseclassStats", character.BaseclassStats, (x, b) =>
        {
            x.Value("baseclassID", b.BaseclassId);
            x.Value("currentLevel", b.CurrentLevel);
            x.Value("previousLevel", b.PreviousLevel);
            x.Value("preDrainLevel", b.PreDrainLevel);
            x.Value("experience", b.Experience);
        });

        w.Array("skillAdjustments", character.SkillAdjustments, (x, a) =>
        {
            x.Value("skillID", a.SkillId);
            x.Value("adjustmentID", a.AdjustmentId);
            x.Value("value", a.Value);
            x.Value("type", a.Type);
        });

        w.Array("spellAdjustments", character.SpellAdjustments, (x, a) =>
        {
            x.Value("schoolID", a.SchoolId);
            x.Value("adjustmentID", a.AdjustmentId);
            x.Value("firstLevel", a.FirstLevel);
            x.Value("lastLevel", a.LastLevel);
            x.Value("percent", a.Percent);
            x.Value("bonus", a.Bonus);
        });

        w.Value("isPregen", character.IsPreGenerated);
        w.Value("canBeSaved", character.CanBeSaved);
        w.Value("hasLayedOnHandsToday", character.HasLayedOnHandsToday);

        w.Object("money", x =>
        {
            // No "coins": the exporter writes only the two gem lists.
            x.Array("gems", character.Money?.Gems ?? [], Gem);
            x.Array("jewels", character.Money?.Jewelry ?? [], Gem);
        });

        w.Real("nbrAttacks", character.NumberOfAttacks);
        w.Object("icon", x => Pic(x, character.Icon));
        w.Value("iconIndex", character.IconIndex);
        w.Value("origIndex", character.OriginalIndex);
        w.Value("uniquePartyID", character.UniquePartyId);
        w.Value("disableTalkIfDead", character.DisableTalkIfDead);
        w.Value("talkEvent", character.TalkEvent);
        w.Value("talkLabel", character.TalkLabel);
        w.Value("examineEvent", character.ExamineEvent);
        w.Value("examineLabel", character.ExamineLabel);

        w.Object("spellbook", x =>
        {
            x.Bool("useLimits", character.SpellBook.UseLimits != 0);

            // The one array written an entry to a line rather than a member to a line.
            x.CompactArray("spellList", character.SpellBook.Spells, s =>
                $"\"name\":\"{Escape(s.SpellId)}\",\"memorized\":\"{s.Memorized}\","
                + $"\"level\":\"{s.Level}\",\"selected\":\"{s.Selected}\"");
        });

        w.Value("detectingInvisible", character.DetectingInvisible);
        w.Value("detectingTraps", character.DetectingTraps);

        w.Array("spellEffects", character.SpellEffects, (_, _) => { });
        w.Array("blockageStatus", character.Blockages, (_, _) => { });

        w.Object("smallPic", x => Pic(x, character.SmallPic));

        w.Array("possessions", character.Items.Items, (x, i) =>
        {
            x.Value("key", i.Key);
            x.Value("itemID", i.ItemId);
            x.Value("ready", i.ReadyLocation);
            x.Value("quantity", i.Quantity);
            x.Value("identified", i.Identified);
            x.Value("charges", i.Charges);
            x.Value("cursed", i.Cursed);
            x.Value("paid", i.Paid);
        });

        w.Array("specialAbilities", character.SpecialAbilities.Pairs, (x, p) =>
        {
            x.Value("key", p.Key);
            x.Value("value", p.Value);
        });

        w.Close();
        return text.ToString();
    }

    private static void Gem(Writer w, GemType gem)
    {
        w.Value("id", gem.Id);
        w.Value("value", gem.Value);
    }

    private static void Pic(Writer w, PicRecord? pic)
    {
        var art = pic ?? PicDataWriter.Empty;

        w.CompactArray("picType", Flags(art.PicType), f => $"\"{f}\"");
        w.Value("filename", art.FileName);
        w.Value("timeDelay", art.TimeDelay);
        w.Value("numFrame", art.NumFrames);
        w.Value("width", art.FrameWidth);
        w.Value("height", art.FrameHeight);
        w.Value("flags", art.Flags);
        w.Value("maxLoops", art.MaxLoops);
        w.Value("style", art.Style);

        // The reference always writes this true; the port carries no field for it.
        w.Bool("allowCentering", true);
        w.Bool("useAlpha", art.UseAlpha != 0);
        w.Value("alpha", art.AlphaValue);
    }

    /// <summary>The <c>SurfaceType</c> names a flag word stands for, lowest bit first.</summary>
    private static IReadOnlyList<string> Flags(int type)
    {
        var names = new List<string>();

        foreach (var (name, value) in JsonCharacterNames.SurfaceTypes)
        {
            if (value != 0 && (type & value) == value)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static string Lookup(int index, string[] table) =>
        index >= 0 && index < table.Length ? table[index] : table[0];

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string[] Genders => JsonCharacterNames.Genders;

    private static string[] Alignments => JsonCharacterNames.Alignments;

    private static string[] Statuses => JsonCharacterNames.Statuses;

    private static string[] Sizes => JsonCharacterNames.Sizes;

    /// <summary>
    /// <c>JWriter</c>'s layout: three spaces a level, no space after a colon, a comma before every
    /// member but the first.
    /// </summary>
    private sealed class Writer(StringBuilder text)
    {
        private int depth;
        private bool first = true;

        public void Open()
        {
            text.Append('{');
            depth++;
        }

        public void Close()
        {
            depth--;
            text.Append('\n').Append('}');
        }

        public void Value(string name, string value) => Member(name, $"\"{Escape(value)}\"");

        public void Value(string name, long value) =>
            Member(name, $"\"{value.ToString(CultureInfo.InvariantCulture)}\"");

        public void Name(string name, string value) => Value(name, value);

        public void Bool(string name, bool value) => Member(name, value ? "\"true\"" : "\"false\"");

        /// <summary>A <c>%f</c> field: six decimal places, as C's default.</summary>
        public void Real(string name, double value) =>
            Member(name, $"\"{value.ToString("F6", CultureInfo.InvariantCulture)}\"");

        public void Object(string name, Action<Writer> body)
        {
            Separator();
            text.Append('"').Append(name).Append("\":{");

            depth++;
            first = true;
            body(this);
            depth--;

            text.Append('\n').Append(Indent()).Append('}');
            first = false;
        }

        /// <summary>An array whose elements are objects, each written a member to a line.</summary>
        public void Array<T>(string name, IReadOnlyList<T> items, Action<Writer, T> body)
        {
            Separator();
            text.Append('"').Append(name).Append("\":[");

            if (items.Count == 0)
            {
                // An empty list is written on the one line, brackets touching.
                text.Append(']');
                first = false;
                return;
            }

            depth++;

            for (int i = 0; i < items.Count; i++)
            {
                text.Append(i == 0 ? string.Empty : ",").Append('\n').Append(Indent()).Append('{');

                depth++;
                first = true;
                body(this, items[i]);
                depth--;

                text.Append('\n').Append(Indent()).Append('}');
            }

            depth--;
            text.Append('\n').Append(Indent()).Append(']');
            first = false;
        }

        /// <summary>An array whose elements are written whole, one to a line.</summary>
        public void CompactArray<T>(string name, IReadOnlyList<T> items, Func<T, string> body)
        {
            Separator();
            text.Append('"').Append(name).Append("\":[");

            if (items.Count == 0)
            {
                text.Append(']');
                first = false;
                return;
            }

            // A one-element list of scalars stays on its line; anything longer, or anything whose
            // elements are objects, breaks. picType is the former and spellList the latter.
            bool objects = body(items[0]).StartsWith('"') && body(items[0]).Contains(':');

            depth++;
            for (int i = 0; i < items.Count; i++)
            {
                if (objects)
                {
                    text.Append(i == 0 ? string.Empty : ",").Append('\n').Append(Indent())
                        .Append('{').Append(body(items[i])).Append('}');
                }
                else
                {
                    text.Append(i == 0 ? string.Empty : ",").Append(body(items[i]));
                }
            }

            depth--;

            if (objects)
            {
                text.Append('\n').Append(Indent());
            }

            text.Append(']');
            first = false;
        }

        private void Member(string name, string value)
        {
            Separator();
            text.Append('"').Append(name).Append("\":").Append(value);
            first = false;
        }

        private void Separator()
        {
            if (!first)
            {
                text.Append(',');
            }

            text.Append('\n').Append(Indent());
            first = false;
        }

        private string Indent() => new(' ', depth * 3);
    }
}

/// <summary>The name tables the JSON format uses, shared by its reader and writer.</summary>
internal static class JsonCharacterNames
{
    public static string[] Genders { get; } = ["Male", "Female", "Bishop"];

    public static string[] Alignments { get; } =
    [
        "Lawful Good", "Neutral Good", "Chaotic Good",
        "Lawful Neutral", "True Neutral", "Chaotic Neutral",
        "Lawful Evil", "Neutral Evil", "Chaotic Evil",
    ];

    public static string[] Statuses { get; } =
    [
        "OKAY", "UNCONSCIOUS", "DEAD", "FLED", "PETRIFIED",
        "GONE", "ANIMATED", "TEMP GONE", "RUNNING", "DYING",
    ];

    public static string[] Sizes { get; } = ["Small", "Medium", "Large"];

    public static (string Name, int Value)[] SurfaceTypes { get; } =
    [
        ("BogusDib", 0), ("CommonDib", 1), ("CombatDib", 2), ("WallDib", 4), ("DoorDib", 8),
        ("BackGndDib", 16), ("OverlayDib", 32), ("IconDib", 64), ("OutdoorCombatDib", 128),
        ("BigPicDib", 256), ("MapDib", 512), ("SmallPicDib", 1024), ("SpriteDib", 2048),
        ("TitleDib", 4096), ("BufferDib", 8192), ("FontDib", 16384), ("MouseDib", 32768),
        ("TransBufferDib", 65536), ("SpecialGraphicsOpaqueDib", 0x20000),
        ("SpecialGraphicsTransparentDib", 0x40000),
    ];
}
