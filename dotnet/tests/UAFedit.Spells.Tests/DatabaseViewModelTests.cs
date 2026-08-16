using UAF.Data;
using UAF.Serialization;

namespace UAFedit.Spells.Tests;

/// <summary>The two master lists: filtering, ordering, selection and dirtiness.</summary>
public class DatabaseViewModelTests
{
    private static SpellRecord Spell(string name, string school, int level, int cost,
                                     params string[] casters) => new(
        PreSpellNameKey: -1, Name: name, CastSound: string.Empty, SchoolId: school,
        AllowedBaseclasses: casters, Level: level, CastingTime: 0, CastingTimeType: 0,
        CanTargetFriend: 0, CanTargetEnemy: 1, IsCumulative: 0, Restrictions: 0,
        CanBeDispelled: 0, CanMemorize: 0, AllowScribe: 0, AutoScribe: 0,
        Lingers: 0, LingerOnceOnly: 0, SaveVersus: 0, SaveResult: 0, Targeting: 0,
        DurationRate: 0, CastCost: cost, CastPriority: 0,
        Parameters: [], Effects: [], CastArt: null, Art: [], Sounds: [],
        CastMessage: string.Empty,
        Scripts: [.. Enumerable.Repeat(SpellScript.Empty, SpellRecordReader.SpellScriptCount)],
        EffectDuration: null, SpecialAbilities: new SpecabBlock([], [], []), Attributes: []);

    private static SpellDatabaseViewModel Database() => new(
        [
            Spell("Sleep", "Magic User", 1, 5, "magicUser"),
            Spell("Bless", "Cleric", 1, 3, "cleric"),
            Spell("Fireball", "Magic User", 3, 20, "magicUser"),
        ],
        ["cleric", "magicUser", "thief"]);

    [Fact]
    public void The_list_starts_in_the_designs_own_order_with_the_first_spell_selected()
    {
        using var db = Database();

        Assert.Equal(["Sleep", "Bless", "Fireball"], db.Spells.Select(s => s.Name));
        Assert.Equal("Sleep", db.SelectedSpell?.Name);
        Assert.False(db.IsDirty);
    }

    /// <remarks>
    /// The reference builds its School combo by collecting the distinct values across the loaded
    /// database — nothing in the design enumerates the schools.
    /// </remarks>
    [Fact]
    public void The_schools_are_the_distinct_values_in_the_database()
    {
        using var db = Database();

        Assert.Equal(["Cleric", "Magic User"], db.Schools);
    }

    [Fact]
    public void Searching_matches_name_school_and_caster()
    {
        using var db = Database();

        db.Search = "ball";
        Assert.Equal(["Fireball"], db.Spells.Select(s => s.Name));

        db.Search = "cleric";
        Assert.Equal(["Bless"], db.Spells.Select(s => s.Name));

        db.Search = string.Empty;
        Assert.Equal(3, db.Spells.Count);
    }

    [Fact]
    public void Sorting_orders_the_view_and_leaves_the_database_alone()
    {
        using var db = Database();

        db.Sort = SpellSort.Name;
        Assert.Equal(["Bless", "Fireball", "Sleep"], db.Spells.Select(s => s.Name));

        db.SortDescending = true;
        Assert.Equal(["Sleep", "Fireball", "Bless"], db.Spells.Select(s => s.Name));

        db.Sort = SpellSort.CastCost;
        db.SortDescending = false;
        Assert.Equal(["Bless", "Sleep", "Fireball"], db.Spells.Select(s => s.Name));

        // A spell's position in the file is what an index into the database means, so the edited
        // list must never come out in the order the view happens to be showing.
        Assert.Equal(["Sleep", "Bless", "Fireball"], db.EditedSpells.Select(s => s.Name));
    }

    /// <remarks>
    /// Without this the detail pane blanks out while the user is still typing a search that
    /// matches what they were looking at.
    /// </remarks>
    [Fact]
    public void The_selection_survives_a_filter_that_still_matches_it()
    {
        using var db = Database();
        db.SelectedSpell = db.Spells[2];

        db.Search = "fire";
        Assert.Equal("Fireball", db.SelectedSpell?.Name);

        db.Search = "bless";
        Assert.Equal("Bless", db.SelectedSpell?.Name);
    }

    [Fact]
    public void An_edit_to_any_spell_makes_the_database_dirty()
    {
        using var db = Database();

        db.Spells[1].Level = 4;

        Assert.True(db.IsDirty);
        Assert.Equal(["Bless"], db.Edited.Select(s => s.Name));
        Assert.Equal(4, db.EditedSpells[1].Level);

        // ...and the untouched ones are still the records that were read.
        Assert.Equal("Sleep", db.EditedSpells[0].Name);
    }

    /// <remarks>
    /// One editor per spell rather than one for the selection, which is what lets a renamed spell
    /// show its new name in the master list as it is typed.
    /// </remarks>
    [Fact]
    public void Renaming_a_spell_shows_in_the_list_immediately()
    {
        using var db = Database();
        var sleep = db.Spells[0];

        sleep.Name = "Slumber";

        Assert.Equal("Slumber", db.Spells[0].Name);
        Assert.Equal("Slumber", db.EditedSpells[0].Name);
    }

    private static SpecialAbilityDatabaseViewModel Abilities() => new(
    [
        new SpecialAbility("Bless",
        [
            new SpecialAbilityEntry("Activation", "$RETURN 1;", SpecialAbilityEntryKind.Script),
        ]),
        new SpecialAbility("Curse",
        [
            new SpecialAbilityEntry("Attempt", "$RETURN 0;", SpecialAbilityEntryKind.Script),
            new SpecialAbilityEntry("power", "3", SpecialAbilityEntryKind.Variable),
        ]),
        new SpecialAbility("Plain",
        [
            new SpecialAbilityEntry("note", "nothing", SpecialAbilityEntryKind.Constant),
        ]),
    ]);

    /// <remarks>
    /// Searching the entry names matters more than searching the ability names: the entry name is
    /// what the engine looks a hook up by, while the ability's own name is often a private label.
    /// </remarks>
    [Fact]
    public void Searching_abilities_matches_entry_names_too()
    {
        using var db = Abilities();

        db.Search = "Attempt";
        Assert.Equal(["Curse"], db.Abilities.Select(a => a.Name));

        db.Search = "bless";
        Assert.Equal(["Bless"], db.Abilities.Select(a => a.Name));
    }

    [Fact]
    public void Compiling_every_ability_counts_only_the_ones_carrying_scripts()
    {
        using var db = Abilities();

        var report = db.CompileAllScripts();

        // Two of the three abilities carry a script; the third is a constant and is not counted.
        Assert.Equal(2, report.Scripts);
        Assert.Equal(2, report.Owners);
        Assert.True(report.AllCompiled);
        Assert.False(db.IsDirty);
        Assert.Same(report, db.Report);
    }

    [Fact]
    public void A_failing_script_is_named_in_the_report()
    {
        using var db = new SpecialAbilityDatabaseViewModel(
        [
            new SpecialAbility("Broken",
            [
                new SpecialAbilityEntry("Attempt", "$WHILE (", SpecialAbilityEntryKind.Script),
            ]),
        ]);

        var report = db.CompileAllScripts();

        Assert.False(report.AllCompiled);
        var failure = Assert.Single(report.Failures);
        Assert.Equal("Broken", failure.Owner);
        Assert.Equal("Attempt", failure.Script);
        Assert.NotEmpty(failure.Errors);
        Assert.Contains("1 failed", db.Status);
    }

    /// <remarks>
    /// An empty slot is skipped rather than reported valid — the wrapper round nothing compiles, so
    /// checking it would put a tick on six slots the designer never filled in.
    /// </remarks>
    [Fact]
    public void Compiling_every_spell_skips_the_empty_slots()
    {
        var scripts = new SpellScript[SpellRecordReader.SpellScriptCount];
        Array.Fill(scripts, SpellScript.Empty);
        scripts[(int)SpellScriptSlot.Begin] = new SpellScript("$RETURN 1;", string.Empty);

        using var db = new SpellDatabaseViewModel(
            [Spell("Sleep", "Magic User", 1, 5) with { Scripts = scripts },
             Spell("Bless", "Cleric", 1, 3)],
            []);

        var report = db.CompileAllScripts();

        Assert.Equal(1, report.Scripts);
        Assert.Equal(1, report.Owners);
        Assert.True(report.AllCompiled);
    }
}
