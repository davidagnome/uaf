using UAF.Data;
using UAF.Serialization;
using UAFcore;

namespace UAFedit.ViewModels;

/// <summary>
/// Projects an opened design into the categories the inspector browses.
/// </summary>
/// <remarks>
/// <para>
/// Read-only, and deliberately so: this is Phase 5's first milestone, whose stated point is to
/// validate Phase 1 end to end and give the editor its shell and navigation
/// (docs/PORTING-PLAN.md §7 Phase 5). <b>Nothing here opens a file.</b> Every byte arrives through
/// <see cref="LoadedDesign"/>, which is the whole reason the editor can exist without a second
/// reader that drifts from the engine's.
/// </para>
/// <para>
/// The categories are exactly what <see cref="LoadedDesign"/> exposes — levels, items, monsters,
/// spells, baseclasses, classes, races, abilities and special abilities. Its remaining surface is
/// either lookup by id, art and fonts, or rules (<c>LevelCap</c>, <c>IsReadyToTrain</c>), none of
/// which is a browsable list.
/// </para>
/// </remarks>
public static class DesignInspector
{
    /// <summary>Every category of the design, in the order the tree shows them.</summary>
    public static IReadOnlyList<DesignNodeViewModel> Categories(LoadedDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);

        return
        [
            Levels(design),
            Items(design),
            Monsters(design),
            Spells(design),
            Baseclasses(design),
            Classes(design),
            Races(design),
            Abilities(design),
            SpecialAbilities(design),
        ];
    }

    private static DesignNodeViewModel Levels(LoadedDesign design)
    {
        var files = design.LevelFiles;

        return DesignNodeViewModel.Category("Levels", files.Count, new RecordTable(
            [new("#", 40), new("Level", 55), new("File", 120), new("Name", 220),
             new("Size", 80), new("Events", 70), new("Wall Sets", 80)],
            () => files.Select((path, index) => LevelRow(design, index, path))));
    }

    private static IReadOnlyList<string> LevelRow(LoadedDesign design, int index, string path)
    {
        // Level() reads the file whole and returns null on an event type this port cannot read;
        // Map() reads only the self-delimiting grid and always succeeds. Asking for both means a
        // level with one unported event still reports its extent instead of a row of dashes.
        var level = design.Level(index);
        var map = level is null ? design.Map(index) : null;

        int number = LevelNumber(path);

        string size = level is not null ? $"{level.Width} x {level.Height}"
                    : map is not null ? $"{map.Width} x {map.Height}"
                    : Missing;

        return
        [
            index.ToString(),
            number < 0 ? Missing : number.ToString(),
            Path.GetFileName(path),
            LevelName(design, number),
            size,
            level is null ? Missing : level.EventCount.ToString(),
            level is null ? Missing : level.WallSets.Count.ToString(),
        ];
    }

    /// <summary>
    /// The design's own index for a level file, or -1 when the name breaks the convention.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The number in the file name is the level index plus one</b> —
    /// <c>"Level" + %.3i(LevelIndex+1) + ".lvl"</c> (<c>Shared/Level.cpp:3643</c>, and again at
    /// <c>:3669</c> and <c>:3229</c>). So <c>Level001.lvl</c> is level 0.
    /// </para>
    /// <para>
    /// <b>It is not the file's position in <see cref="LoadedDesign.LevelFiles"/>.</b> Designs ship
    /// gaps: <c>Case.dsn</c> has eleven levels numbered up to <c>Level255.lvl</c>, so its tenth
    /// file is level 254. The two agree only in a design numbered without holes, which is why the
    /// tree shows both columns rather than picking one.
    /// </para>
    /// </remarks>
    private static int LevelNumber(string path)
    {
        const string prefix = "Level";
        string name = Path.GetFileNameWithoutExtension(path);

        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(name.AsSpan(prefix.Length), out int number) && number > 0
            ? number - 1
            : -1;
    }

    /// <summary>
    /// The level's name from the design's own level table, keyed by its raw <c>stats[]</c> index.
    /// </summary>
    /// <remarks>
    /// The table is written out under that index (<c>GlobalData.cpp:3547</c>) — one-based level
    /// numbers belong to scripts, not to this table.
    /// </remarks>
    private static string LevelName(LoadedDesign design, int number)
    {
        if (number < 0 || design.Globals.Levels is not { } table
            || !table.Levels.TryGetValue((uint)number, out var stats))
        {
            return string.Empty;
        }

        return stats.Name;
    }

    private static DesignNodeViewModel Items(LoadedDesign design)
    {
        if (design.Items is not { } database)
        {
            return DesignNodeViewModel.Unreadable("Items");
        }

        return DesignNodeViewModel.Category("Items", database.Items.Count, new RecordTable(
            [new("Name", 170), new("Display Name", 170), new("Slot", 70), new("Cost", 70),
             new("Weight", 60), new("Hands", 55), new("Damage (S)", 90), new("Damage (L)", 90),
             new("AC", 45)],
            () => database.Items.Select(ItemRow)));
    }

    /// <remarks>
    /// <b>The name column is <c>UniqueName</c>, not <c>IdName</c>.</b> An item's id is its
    /// <c>m_uniqueName</c> (<c>Items.h:701</c>); <c>IdName</c> is the fuller display name, and the
    /// two differ in real designs. Showing only the latter would list an item under a name that
    /// resolves to nothing.
    /// </remarks>
    private static IReadOnlyList<string> ItemRow(ItemRecord item) =>
    [
        item.Names.UniqueName,
        item.Names.IdName,
        // The database's conversion table, not the carried item's -- they disagree on ordinal 3.
        ReadiedLocation.WordFor(Inventory.SlotFor(item)),
        item.Scalars.Cost.ToString(),
        item.Scalars.Encumbrance.ToString(),
        item.Combat.HandsToUse.ToString(),
        Damage(item.Combat.NbrDiceSm, item.Combat.DmgDiceSm, item.Combat.DmgBonusSm),
        Damage(item.Combat.NbrDiceLg, item.Combat.DmgDiceLg, item.Combat.DmgBonusLg),
        item.Combat.ProtectionBase.ToString(),
    ];

    private static DesignNodeViewModel Monsters(LoadedDesign design)
    {
        if (design.Monsters is not { } monsters)
        {
            return DesignNodeViewModel.Unreadable("Monsters");
        }

        return DesignNodeViewModel.Category("Monsters", monsters.Count, new RecordTable(
            [new("Name", 200), new("Hit Dice", 70), new("AC", 45), new("THAC0", 60),
             new("Move", 55), new("Magic Res.", 80), new("XP", 70), new("Attacks", 65),
             new("Class", 140)],
            () => monsters.Select(MonsterRow)));
    }

    /// <remarks>
    /// <c>Hit_Dice</c> is a <c>float</c> among longs (<c>Monster.h:410</c>) — half-dice monsters
    /// exist, so this is not an integer column that happens to be stored oddly.
    /// </remarks>
    private static IReadOnlyList<string> MonsterRow(MonsterRecord monster) =>
    [
        monster.Name,
        monster.HitDice.ToString("0.##"),
        monster.ArmorClass.ToString(),
        monster.Thac0.ToString(),
        monster.Movement.ToString(),
        monster.MagicResistance.ToString(),
        monster.ExperienceValue.ToString(),
        monster.Attacks.Count.ToString(),
        monster.ClassId,
    ];

    private static DesignNodeViewModel Spells(LoadedDesign design)
    {
        if (design.Spells is not { } spells)
        {
            return DesignNodeViewModel.Unreadable("Spells");
        }

        return DesignNodeViewModel.Category("Spells", spells.Count, new RecordTable(
            [new("Name", 200), new("School", 120), new("Level", 55), new("Cast Time", 80),
             new("Cost", 55), new("Effects", 65), new("Baseclasses", 220)],
            () => spells.Select(SpellRow)));
    }

    private static IReadOnlyList<string> SpellRow(SpellRecord spell) =>
    [
        spell.Name,
        // A SCHOOL_ID derives from CString (Externs.h:1350): a name, despite reading like a code.
        spell.SchoolId,
        spell.Level.ToString(),
        spell.CastingTime.ToString(),
        spell.CastCost.ToString(),
        spell.Effects.Count.ToString(),
        string.Join(", ", spell.AllowedBaseclasses),
    ];

    private static DesignNodeViewModel Baseclasses(LoadedDesign design)
    {
        if (design.Baseclasses is not { } baseclasses)
        {
            return DesignNodeViewModel.Unreadable("Baseclasses");
        }

        return DesignNodeViewModel.Category("Baseclasses", baseclasses.Count, new RecordTable(
            [new("Name", 170), new("Tag", 60), new("XP Levels", 80), new("Casting", 200),
             new("Races", 200), new("Skills", 60)],
            () => Ordered(baseclasses).Select(BaseclassRow)));
    }

    private static IReadOnlyList<string> BaseclassRow(BaseclassRecord baseclass) =>
    [
        baseclass.Name,
        baseclass.Tag,
        baseclass.ExperienceLevels.Count.ToString(),
        string.Join(", ", baseclass.Casting.Select(c => c.SchoolId).Where(s => s.Length > 0)),
        string.Join(", ", baseclass.AllowedRaces),
        baseclass.Skills.Count.ToString(),
    ];

    private static DesignNodeViewModel Classes(LoadedDesign design)
    {
        if (design.Classes is not { } classes)
        {
            return DesignNodeViewModel.Unreadable("Classes");
        }

        return DesignNodeViewModel.Category("Classes", classes.Count, new RecordTable(
            [new("Name", 170), new("Tag", 60), new("Baseclasses", 280),
             new("Hit Dice From", 150), new("Equipment", 80)],
            () => Ordered(classes).Select(ClassRow)));
    }

    /// <remarks>
    /// A class is a bundle of baseclasses; the experience split divides an award by that count
    /// (<c>Char.cpp:5798</c>), so the list is the load-bearing field rather than a label.
    /// </remarks>
    private static IReadOnlyList<string> ClassRow(ClassRecord record) =>
    [
        record.Name,
        record.Tag,
        string.Join(", ", record.Baseclasses),
        record.HitDiceBaseclassId,
        record.StartingEquipment.Items.Count.ToString(),
    ];

    private static DesignNodeViewModel Races(LoadedDesign design)
    {
        if (design.Races is not { } races)
        {
            return DesignNodeViewModel.Unreadable("Races");
        }

        return DesignNodeViewModel.Category("Races", races.Count, new RecordTable(
            [new("Name", 170), new("Movement", 110), new("Change Class", 100),
             new("Secret Doors", 100), new("Requirements", 100), new("Skills", 60)],
            () => Ordered(races).Select(RaceRow)));
    }

    private static IReadOnlyList<string> RaceRow(RaceRecord race) =>
    [
        race.Name,
        Dice(race.BaseMovement),
        YesNo(race.CanChangeClass),
        race.FindSecretDoor.ToString(),
        race.AbilityRequirements.Count.ToString(),
        race.Skills.Count.ToString(),
    ];

    private static DesignNodeViewModel Abilities(LoadedDesign design)
    {
        if (design.Abilities is not { } abilities)
        {
            return DesignNodeViewModel.Unreadable("Abilities");
        }

        return DesignNodeViewModel.Category("Abilities", abilities.Count, new RecordTable(
            [new("Name", 170), new("Abbreviation", 110), new("Roll", 140)],
            () => Ordered(abilities).Select(AbilityRow)));
    }

    private static IReadOnlyList<string> AbilityRow(AbilityRecord ability) =>
        [ability.Name, ability.Abbreviation, Dice(ability.Roll)];

    /// <remarks>
    /// Never unreadable: <c>SpecialAbilitiesFile.Load</c> answers an empty list for a design with
    /// no <c>specialAbilities.txt</c>, which is an ordinary design that overrides no hook.
    /// </remarks>
    private static DesignNodeViewModel SpecialAbilities(LoadedDesign design)
    {
        var abilities = design.SpecialAbilities;

        return DesignNodeViewModel.Category("Special Abilities", abilities.Count, new RecordTable(
            [new("Name", 260), new("Entries", 70), new("Scripts", 70), new("Parameters", 90),
             new("Tables", 70)],
            () => abilities.Select(SpecialAbilityRow)));
    }

    /// <remarks>
    /// The split by <see cref="SpecialAbilityEntryKind"/> is the useful column here: the scripts
    /// are the GPDL this file exists for, and the rest are the constants and parameters they read.
    /// </remarks>
    private static IReadOnlyList<string> SpecialAbilityRow(SpecialAbility ability) =>
    [
        ability.Name,
        ability.Entries.Count.ToString(),
        Count(ability, SpecialAbilityEntryKind.Script),
        Count(ability, SpecialAbilityEntryKind.Variable),
        Count(ability, SpecialAbilityEntryKind.IntegerTable),
    ];

    private static string Count(SpecialAbility ability, SpecialAbilityEntryKind kind) =>
        ability.Entries.Count(e => e.Kind == kind).ToString();

    /// <summary>What a column shows when the record cannot answer it.</summary>
    private const string Missing = "—";

    /// <summary>The tagged databases arrive as name-keyed maps; a list wants a stable order.</summary>
    private static IEnumerable<TRecord> Ordered<TRecord>(
        IReadOnlyDictionary<string, TRecord> records) =>
        records.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
               .Select(pair => pair.Value);

    private static string YesNo(int flag) => flag != 0 ? "yes" : "no";

    private static string Damage(int count, int sides, int bonus) =>
        count == 0 && sides == 0 ? string.Empty : $"{count}d{sides}{Signed(bonus)}";

    /// <summary>
    /// A dice expression as text.
    /// </summary>
    /// <remarks>
    /// <b>A modern design's dice are a string and nothing else.</b> <c>DP2</c> — which is every
    /// dice expression in a current design — is two strings, so its packed numeric fields are all
    /// zero (<see cref="DicePlusReader"/>). Formatting from those alone would show "0d0" for every
    /// race's movement; formatting from the text alone would show nothing for the older <c>DP0</c>
    /// and <c>DP1</c> forms, which carry no text.
    /// </remarks>
    private static string Dice(DicePlus? dice)
    {
        if (dice is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(dice.Text)
            ? $"{dice.NumDice}d{dice.NumSides}{Signed(dice.Bonus)}"
            : dice.Text.Trim();
    }

    private static string Signed(int bonus) => bonus switch
    {
        0 => string.Empty,
        > 0 => $"+{bonus}",
        _ => bonus.ToString(),
    };
}
