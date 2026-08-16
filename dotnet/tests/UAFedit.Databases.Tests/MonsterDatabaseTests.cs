using UAF.Serialization;
using UAFedit.Databases;

namespace UAFedit.Databases.Tests;

/// <summary>
/// The monster editor against a real design.
/// </summary>
/// <remarks>
/// Same contract as the item tests: every corpus test returns early without a shipped design, and
/// <see cref="The_corpus_design_has_a_monster_database"/> is what stops the file passing vacuously.
/// </remarks>
public class MonsterDatabaseTests
{
    /// <summary><b>The premise</b>, over both shipped designs.</summary>
    [Theory]
    [InlineData("SomethingWild.dsn")]
    [InlineData("Case.dsn")]
    public void The_corpus_design_has_a_monster_database(string name)
    {
        using var design = DatabaseCorpus.Open(name);
        if (design is null)
        {
            return;
        }

        var editor = new MonsterDatabaseViewModel(design);

        Assert.True(editor.IsReadable, $"{name} has no readable monsters.dat");
        Assert.True(editor.Count > 0, $"{name}'s monsters.dat read as empty");
        Assert.Equal(editor.Count, editor.Records.Count);
        Assert.Contains(editor.All, e => e.Name.Length > 0);

        // Every monster reaches the editor with at least one attack -- the reader invents one for
        // a record that has none, at every version.
        Assert.All(editor.All, e => Assert.True(e.HasAttacks));

        var changed = editor.All.Where(e => e.IsDirty).Select(e => e.Title).ToList();
        Assert.True(changed.Count == 0,
                    $"{name}: records changed by merely loading them: "
                    + string.Join(", ", changed));
    }

    /// <inheritdoc cref="ItemDatabaseTests.Nothing_is_dirty_before_anything_is_edited"/>
    [Fact]
    public void Nothing_is_dirty_before_anything_is_edited()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new MonsterDatabaseViewModel(design);

        var changed = editor.All.Where(e => e.IsDirty).Select(e => e.Title).ToList();

        Assert.True(changed.Count == 0,
                    $"records changed by merely loading them: {string.Join(", ", changed)}");
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void The_list_populates_and_everything_is_visible_by_default()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new MonsterDatabaseViewModel(design);

        Assert.Equal(editor.Count, editor.Visible.Count);
        Assert.NotNull(editor.Selected);
    }

    [Fact]
    public void An_edit_marks_the_record_dirty_and_shows_up_in_the_read_back()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new MonsterDatabaseViewModel(design);
        var monster = editor.All[0];
        int was = monster.Thac0;

        monster.Thac0 = was + 3;

        Assert.True(monster.IsDirty);
        Assert.True(editor.IsDirty);
        Assert.Equal(was + 3, editor.Records[0].Thac0);
        Assert.All(editor.All.Skip(1), e => Assert.False(e.IsDirty));
    }

    /// <summary>A field goes through the form and comes back the same.</summary>
    [Fact]
    public void A_field_round_trips_through_the_form()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new MonsterDatabaseViewModel(design);
        var monster = editor.All[0];

        monster.Name = "Round Trip";
        monster.HitDice = 4.5f;
        monster.ArmorClass = -2;
        monster.UndeadType = "Wight";

        var built = editor.Records[0];

        Assert.Equal("Round Trip", built.Name);
        Assert.Equal(4.5f, built.HitDice);
        Assert.Equal(-2, built.ArmorClass);
        Assert.Equal("Wight", built.UndeadType);

        // and the fields nobody touched are the ones the file had.
        Assert.Equal(monster.Original.Movement, built.Movement);
        Assert.Same(monster.Original.SpecialAbilities, built.SpecialAbilities);
        Assert.Same(monster.Original.Items, built.Items);
        Assert.Same(monster.Original.Money, built.Money);
    }

    [Fact]
    public void Searching_and_sorting_leave_the_records_alone()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null || design.Monsters is not { Count: > 1 })
        {
            return;
        }

        var editor = new MonsterDatabaseViewModel(design);
        int total = editor.Count;

        editor.Sort = editor.Sorts.First(s => s.Label == "Armour class");
        var ascending = editor.Visible.Select(e => e.ArmorClass).ToList();
        Assert.Equal(ascending.Order(), ascending);

        editor.Search = editor.All[0].Name;
        Assert.NotEmpty(editor.Visible);
        Assert.Equal(total, editor.Records.Count);

        editor.Search = string.Empty;
        Assert.Equal(total, editor.Visible.Count);
    }

    [Fact]
    public void Adding_and_deleting_change_the_read_back_collection()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new MonsterDatabaseViewModel(design);
        int before = editor.Count;

        editor.Add();
        Assert.Equal(before + 1, editor.Records.Count);
        Assert.Equal("New Monster", editor.Selected!.Name);

        editor.Delete();
        Assert.Equal(before, editor.Records.Count);
    }

    // ---- Synthetic records ---------------------------------------------------------------------

    /// <summary>
    /// The race comes out of the attribute list, and editing it writes back into the attribute list.
    /// </summary>
    /// <remarks>
    /// <c>MONSTER_DATA</c> has no serialized race member; the value lives in <c>mon_asl</c> under
    /// <c>$SYS$Race</c>. This is the test that says the port's <see cref="MonsterRecord"/> is not
    /// missing a field.
    /// </remarks>
    [Fact]
    public void The_race_lives_in_the_attribute_list()
    {
        var record = MonsterEditorViewModel.NewRecord("Orc");
        var editor = new MonsterEditorViewModel(record);

        Assert.Equal("Human", editor.Race);
        Assert.False(editor.IsDirty);

        editor.Race = "Orcish";

        var entry = Assert.Single(editor.Record.Attributes,
                                  a => a.Key == MonsterEditorViewModel.RaceAttributeKey);

        Assert.Equal("Orcish", entry.Value);
        Assert.True(editor.IsDirty);
    }

    /// <summary>A record with no race attribute reads as Human and is not dirtied by saying so.</summary>
    [Fact]
    public void A_monster_with_no_race_attribute_defaults_to_human_without_becoming_dirty()
    {
        var record = MonsterEditorViewModel.NewRecord("Ghost") with { Attributes = [] };
        var editor = new MonsterEditorViewModel(record);

        Assert.Equal("Human", editor.Race);
        Assert.False(editor.IsDirty);
        Assert.Empty(editor.Record.Attributes);

        editor.Race = "Undead";
        Assert.True(editor.IsDirty);
        Assert.Single(editor.Record.Attributes);
    }

    /// <summary>Flag bits with no checkbox survive one that has.</summary>
    /// <remarks>
    /// <c>FormUndead=32</c> is commented out of the enum and its control id survives with no
    /// control, so a design that sets bit 5 has a value no editor can see. It must still round-trip.
    /// </remarks>
    [Fact]
    public void An_unnamed_form_bit_survives_a_checkbox()
    {
        var record = MonsterEditorViewModel.NewRecord("Wraith") with { FormType = 32 };
        var editor = new MonsterEditorViewModel(record);

        Assert.All(editor.FormFlags, f => Assert.False(f.IsSet));
        Assert.Equal(32u, editor.Record.FormType);

        editor.FormFlags[0].IsSet = true;                 // FormMammal

        Assert.Equal(33u, editor.Record.FormType);
        Assert.True(editor.IsDirty);

        editor.FormFlags[0].IsSet = false;

        Assert.Equal(32u, editor.Record.FormType);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Every_flag_group_maps_onto_its_own_word()
    {
        var editor = new MonsterEditorViewModel(MonsterEditorViewModel.NewRecord("Test"));

        editor.PenaltyFlags[4].IsSet = true;              // PenaltyRangerDmg = 16
        editor.ImmunityFlags[3].IsSet = true;             // ImmuneVorpal = 8
        editor.MiscFlags[1].IsSet = true;                 // OptionAffectedByDispelEvil = 2

        var built = editor.Record;

        Assert.Equal(0u, built.FormType);
        Assert.Equal(16u, built.PenaltyType);
        Assert.Equal(8u, built.ImmunityType);
        Assert.Equal(2u, built.MiscOptionsType);
    }

    /// <summary>An attack edited and put back leaves the record clean.</summary>
    /// <remarks>
    /// The attack list is the other collection member <c>Canonical</c> exists for.
    /// </remarks>
    [Fact]
    public void An_attack_edited_back_to_its_old_value_leaves_the_record_clean()
    {
        var editor = new MonsterEditorViewModel(MonsterEditorViewModel.NewRecord("Kobold"));
        var attack = editor.Attacks[0];
        int sides = attack.Sides;

        attack.Sides = sides + 2;
        Assert.True(editor.IsDirty);
        Assert.Equal(sides + 2, editor.Record.Attacks[0].Sides);

        attack.Sides = sides;
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Attacks_can_be_added_and_removed()
    {
        var editor = new MonsterEditorViewModel(MonsterEditorViewModel.NewRecord("Hydra"));

        editor.AddAttackCommand.Execute(null);

        Assert.Equal(2, editor.Record.Attacks.Count);
        Assert.True(editor.IsDirty);

        editor.RemoveAttackCommand.Execute(editor.Attacks[1]);

        Assert.Single(editor.Record.Attacks);
        Assert.False(editor.IsDirty);

        editor.RemoveAttackCommand.Execute(editor.Attacks[0]);

        Assert.False(editor.HasAttacks);
        Assert.Empty(editor.Record.Attacks);
    }

    /// <summary>An attack's spell class and level are carried even though nothing edits them.</summary>
    [Fact]
    public void An_attacks_spell_class_and_level_are_carried()
    {
        var record = MonsterEditorViewModel.NewRecord("Caster") with
        {
            Attacks = [new AttackDetails(6, 1, 0, "attacks", "Magic Missile", 12, 3, 5)],
        };

        var editor = new MonsterEditorViewModel(record);
        editor.Attacks[0].Bonus = 2;

        var built = editor.Record.Attacks[0];

        Assert.Equal(2, built.Bonus);
        Assert.Equal("Magic Missile", built.SpellId);
        Assert.Equal(12, built.LegacySpellId);
        Assert.Equal(3, built.SpellClass);
        Assert.Equal(5, built.SpellLevel);
    }

    /// <summary>An attack with no dice and no spell does nothing, and says so.</summary>
    [Fact]
    public void A_damageless_attack_is_flagged()
    {
        var editor = new MonsterEditorViewModel(MonsterEditorViewModel.NewRecord("Ghoul"));
        var attack = editor.Attacks[0];

        Assert.False(attack.IsDamageless);

        attack.Nbr = 0;
        Assert.True(attack.IsDamageless);

        attack.SpellId = "Paralysis";
        Assert.False(attack.IsDamageless);
    }

    /// <summary>The hit-dice field's meaning flips with the flag, and the bonus is not destroyed.</summary>
    [Fact]
    public void Switching_to_hit_points_reports_the_dead_bonus_rather_than_zeroing_it()
    {
        var record = MonsterEditorViewModel.NewRecord("Golem") with
        {
            HitDice = 8, UseHitDice = 1, HitDiceBonus = 4,
        };

        var editor = new MonsterEditorViewModel(record);

        Assert.Equal("Hit dice", editor.HitDiceCaption);
        Assert.True(editor.IsHitDiceBonusEffective);

        editor.UsesHitDice = false;

        Assert.Equal("Hit points", editor.HitDiceCaption);
        Assert.False(editor.IsHitDiceBonusEffective);
        Assert.Equal(4, editor.Record.HitDiceBonus);
        Assert.Equal(0, editor.Record.UseHitDice);
    }

    /// <summary>A new record carries the constructor's defaults, not the dialog's dead ones.</summary>
    [Fact]
    public void A_new_monster_gets_the_references_defaults()
    {
        var record = MonsterEditorViewModel.NewRecord("Rat");

        Assert.Equal(20, record.Thac0);
        Assert.Equal(1, record.Size);                     // Medium
        Assert.Equal(1, record.UseHitDice);
        Assert.Equal("Fighter", record.ClassId);
        Assert.Equal(string.Empty, record.UndeadType);
        Assert.Single(record.Attacks);
        Assert.Equal("Human", MonsterEditorViewModel.RaceOf(record.Attributes));
    }

    [Fact]
    public void A_missing_database_reads_as_unreadable_rather_than_empty()
    {
        var editor = new MonsterDatabaseViewModel(monsters: null, knownClasses: null);

        Assert.False(editor.IsReadable);
        Assert.Equal(0, editor.Count);
        Assert.Empty(editor.Records);
        Assert.False(editor.IsDirty);
    }

    /// <summary>A class the design no longer defines stays selectable rather than vanishing.</summary>
    [Fact]
    public void A_class_the_design_does_not_define_is_still_offered()
    {
        var record = MonsterEditorViewModel.NewRecord("Oddity") with { ClassId = "Warlock" };
        var editor = new MonsterEditorViewModel(record, ["Fighter", "Cleric"]);

        Assert.NotNull(editor.ClassChoice);
        Assert.Equal("Warlock", editor.ClassChoice!.Value);
        Assert.Contains("not in this design", editor.ClassChoice.Label, StringComparison.Ordinal);
        Assert.False(editor.IsDirty);
    }
}
