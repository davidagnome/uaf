using UAF.Serialization;

namespace UAFedit.Spells.Tests;

/// <summary>
/// The spell detail form, over records built by hand.
/// </summary>
/// <remarks>
/// Hand-built records rather than corpus ones, so these run everywhere and so each one can put a
/// single field in the state it is about. The corpus tests next door prove the same view model
/// survives 377 real spells.
/// </remarks>
public class SpellEditorTests
{
    /// <summary>A spell with every list populated, so nothing under test is vacuously empty.</summary>
    private static SpellRecord Spell(string name = "Magic Missile") => new(
        PreSpellNameKey: -1,
        Name: name,
        CastSound: "cast.wav",
        SchoolId: "Magic User",
        AllowedBaseclasses: ["magicUser"],
        Level: 1,
        CastingTime: 1,
        CastingTimeType: 1,
        CanTargetFriend: 0,
        CanTargetEnemy: 1,
        IsCumulative: 0,
        Restrictions: 0x03,
        CanBeDispelled: 1,
        CanMemorize: 1,
        AllowScribe: 1,
        AutoScribe: 0,
        Lingers: 0,
        LingerOnceOnly: 0,
        SaveVersus: 3,
        SaveResult: 1,
        Targeting: 4,
        DurationRate: 0,
        CastCost: 5,
        CastPriority: 0,
        Parameters: [Dice("3"), Dice("1"), Dice("2"), Dice("30"), Dice(""), Dice("")],
        Effects: [],
        CastArt: null,
        Art: [],
        Sounds: ["missile.wav", "cover.wav", "hit.wav", "linger.wav"],
        CastMessage: "casts a spell",
        Scripts: [.. Enumerable.Repeat(SpellScript.Empty, SpellRecordReader.SpellScriptCount)],
        EffectDuration: Dice("1"),
        SpecialAbilities: new SpecabBlock([], [], []),
        Attributes: []);

    private static DicePlus Dice(string text) =>
        new(DicePlusReader.TagText, text, "binary", 0, 0, 0, 0, 0, 0, []);

    [Fact]
    public void An_untouched_spell_is_not_dirty()
    {
        var editor = new SpellEditorViewModel(Spell());

        Assert.False(editor.IsDirty);
        Assert.Equal("Magic Missile", editor.Name);
    }

    /// <remarks>
    /// The whole reason <see cref="EditableViewModel"/> derives dirtiness from change notification:
    /// a field added later is covered without anyone remembering to flag it.
    /// </remarks>
    [Fact]
    public void Editing_any_field_makes_the_spell_dirty()
    {
        var editor = new SpellEditorViewModel(Spell());

        editor.CastPriority = 9;

        Assert.True(editor.IsDirty);
        Assert.Equal(9, editor.ToRecord().CastPriority);
    }

    /// <remarks>
    /// The parts are separate view models, and nothing but the editor's own subscription joins
    /// their dirtiness to the spell's — the case a hand-written setter cannot cover.
    /// </remarks>
    [Fact]
    public void Editing_a_script_or_a_dice_expression_makes_the_spell_dirty()
    {
        var editor = new SpellEditorViewModel(Spell());
        editor.Scripts[0].Source = "$RETURN 1;";
        Assert.True(editor.IsDirty);

        var second = new SpellEditorViewModel(Spell());
        second.Parameters[1].Text = "4";
        Assert.True(second.IsDirty);

        var third = new SpellEditorViewModel(Spell());
        third.Baseclasses[0].IsAllowed = false;
        Assert.True(third.IsDirty);
    }

    /// <summary>
    /// An untouched spell round-trips through the editor unchanged, field for field.
    /// </summary>
    /// <remarks>
    /// <b>The <c>with</c> on the left is what makes this exhaustive.</b> Substituting the four
    /// containers the editor rebuilds and then comparing the whole record by value covers every
    /// scalar field at once, including any added later — which is the point, since a field the
    /// editor forgets to carry through is silently dropped otherwise.
    /// </remarks>
    [Fact]
    public void An_untouched_spell_round_trips()
    {
        var original = Spell();
        var edited = new SpellEditorViewModel(original).ToRecord();

        Assert.Equal(original.AllowedBaseclasses, edited.AllowedBaseclasses);
        Assert.Equal(original.Parameters, edited.Parameters);
        Assert.Equal(original.Sounds, edited.Sounds);
        Assert.Equal(original.Scripts, edited.Scripts);

        Assert.Equal(original, edited with
        {
            AllowedBaseclasses = original.AllowedBaseclasses,
            Parameters = original.Parameters,
            Sounds = original.Sounds,
            Scripts = original.Scripts,
        });
    }

    /// <remarks>
    /// The unmodelled halves of the record — art, effects, the specab block and the ASL — must come
    /// through by identity. Rebuilding what the editor does not model is how a round trip loses
    /// data.
    /// </remarks>
    [Fact]
    public void The_fields_the_editor_does_not_model_come_through_untouched()
    {
        var original = Spell();
        var editor = new SpellEditorViewModel(original);
        editor.Name = "Renamed";

        var edited = editor.ToRecord();

        Assert.Same(original.Effects, edited.Effects);
        Assert.Same(original.Art, edited.Art);
        Assert.Same(original.Attributes, edited.Attributes);
        Assert.Same(original.SpecialAbilities, edited.SpecialAbilities);
        Assert.Equal(-1, edited.PreSpellNameKey);
    }

    /// <remarks>
    /// The reason <c>Flag</c> exists: a design storing 2 in a <c>BOOL</c> field would be normalised
    /// to 1 by a naive trip through <c>bool</c>, turning a whole database into a diff.
    /// </remarks>
    [Fact]
    public void An_unchanged_boolean_keeps_whatever_odd_value_the_design_stored()
    {
        var original = Spell() with { CanMemorize = 2 };
        var editor = new SpellEditorViewModel(original);

        Assert.True(editor.CanMemorize);
        Assert.Equal(2, editor.ToRecord().CanMemorize);

        editor.CanMemorize = false;
        Assert.Equal(0, editor.ToRecord().CanMemorize);
    }

    /// <remarks>
    /// <c>restrictions</c> is a <c>BOOL</c> used as a bitmask; bits the port does not name are
    /// preserved rather than cleared.
    /// </remarks>
    [Fact]
    public void Restriction_bits_the_editor_does_not_name_are_preserved()
    {
        var editor = new SpellEditorViewModel(Spell() with { Restrictions = 0x83 });

        Assert.True(editor.AllowedInCamp);
        Assert.True(editor.AllowedInCombat);

        editor.AllowedInCamp = false;

        Assert.Equal(0x82, editor.ToRecord().Restrictions);
    }

    /// <remarks>
    /// The reference does this on OK (<c>SpellDBDlgEx.cpp:760</c>): a lingering effect is placed on
    /// a map square and there is no map in camp.
    /// </remarks>
    [Fact]
    public void Turning_on_lingering_clears_in_camp()
    {
        var editor = new SpellEditorViewModel(Spell() with { Restrictions = 0x03 });

        editor.Lingers = true;

        Assert.False(editor.AllowedInCamp);
        Assert.Equal(SpellChoices.RestrictionInCombat, editor.ToRecord().Restrictions);
    }

    /// <summary>
    /// The targeting type renames five of the six dice parameters.
    /// </summary>
    /// <remarks>
    /// The single most misleading thing an editor could get wrong here: <c>Parameters[1]</c> is a
    /// Quantity for a circle and a Width for a cone, and the record cannot tell you which.
    /// </remarks>
    [Fact]
    public void The_targeting_type_relabels_the_parameters()
    {
        var editor = new SpellEditorViewModel(Spell());

        editor.Targeting = 4;                       // Area: Circle
        Assert.Equal("Quantity", editor.Parameters[1].Label);
        Assert.Equal("Radius", editor.Parameters[2].Label);
        Assert.True(editor.Parameters[2].IsUsed);

        editor.Targeting = 9;                       // Area: Cone
        Assert.Equal("Width", editor.Parameters[1].Label);
        Assert.Equal("Length", editor.Parameters[2].Label);

        editor.Targeting = 0;                       // Self
        Assert.Equal(string.Empty, editor.Parameters[1].Label);
        Assert.False(editor.Parameters[1].IsUsed);

        // Relabelling is the view following a field, and must not itself count as an edit --
        // but the targeting change that caused it does.
        Assert.True(editor.IsDirty);
        Assert.False(editor.Parameters[1].IsDirty);
    }

    /// <remarks>Duration keeps its caption and goes dark, which the label alone cannot express.</remarks>
    [Fact]
    public void A_permanent_duration_disables_the_duration_field_without_unlabelling_it()
    {
        var editor = new SpellEditorViewModel(Spell());

        Assert.True(editor.Parameters[0].IsUsed);

        editor.DurationRate = 4;                    // permanent

        Assert.False(editor.Parameters[0].IsUsed);
        Assert.Equal("Duration", editor.Parameters[0].Label);
    }

    [Fact]
    public void The_save_versus_category_is_disabled_when_no_save_is_rolled()
    {
        var editor = new SpellEditorViewModel(Spell());

        editor.SaveResult = 1;                      // Save Negates
        Assert.True(editor.IsSaveVersusUsed);

        editor.SaveResult = 0;                      // No Save
        Assert.False(editor.IsSaveVersusUsed);

        editor.SaveResult = 3;                      // Use Player THAC0
        Assert.False(editor.IsSaveVersusUsed);
    }

    [Fact]
    public void Lingering_is_only_offered_for_durations_that_are_spans_of_time()
    {
        var editor = new SpellEditorViewModel(Spell());

        foreach (int rate in new[] { 0, 2, 3 })
        {
            editor.DurationRate = rate;
            Assert.True(editor.IsLingerAvailable);
        }

        foreach (int rate in new[] { 1, 4, 5 })
        {
            editor.DurationRate = rate;
            Assert.False(editor.IsLingerAvailable);
        }
    }

    /// <remarks>
    /// A stale binary beside a changed expression is a state the engine is careful never to be in —
    /// it empties every one on load to force a recompile.
    /// </remarks>
    [Fact]
    public void Editing_an_expression_or_a_script_clears_its_compiled_binary()
    {
        var editor = new SpellEditorViewModel(Spell());

        Assert.Equal("binary", editor.ToRecord().Parameters[1].Binary);

        editor.Parameters[1].Text = "9";
        Assert.Equal(string.Empty, editor.ToRecord().Parameters[1].Binary);

        var second = new SpellEditorViewModel(Spell() with
        {
            Scripts = [.. Enumerable.Repeat(new SpellScript("$RETURN 1;", "bin"),
                                            SpellRecordReader.SpellScriptCount)],
        });

        second.Scripts[0].Source = "$RETURN 2;";
        Assert.Equal(string.Empty, second.ToRecord().Scripts[0].Binary);

        // ...and a slot nobody touched keeps its binary, so an untouched spell stays byte-identical.
        Assert.Equal("bin", second.ToRecord().Scripts[1].Binary);
    }

    /// <summary>
    /// The scripts are offered in the dialog's order, but written back in the wire's.
    /// </summary>
    /// <remarks>
    /// <c>SpellScriptSlot</c> follows the stream, where Initiation and Termination are 2 and 3; the
    /// original's dropdown listed them last. Confusing the two swaps two scripts on save, which no
    /// test of the list alone would catch.
    /// </remarks>
    [Fact]
    public void The_script_picker_uses_the_dialog_order_and_writes_back_in_wire_order()
    {
        var scripts = new SpellScript[SpellRecordReader.SpellScriptCount];
        for (int i = 0; i < scripts.Length; i++)
        {
            scripts[i] = new SpellScript($"slot{i}", string.Empty);
        }

        var editor = new SpellEditorViewModel(Spell() with { Scripts = scripts });

        Assert.Equal(
            ["Spell Begin Script", "Spell End Script", "Saving Throw Script",
             "Saving Throw Succeeded Script", "Saving Throw Failed Script",
             "Spell Initiation Script", "Spell Termination Script"],
            editor.Scripts.Select(s => s.Name));

        // Third in the picker is the saving-throw script, which is slot 4 on the wire.
        Assert.Equal("slot4", editor.Scripts[2].Source);

        editor.Scripts[2].Source = "changed";

        var written = editor.ToRecord().Scripts;
        Assert.Equal("changed", written[(int)SpellScriptSlot.SavingThrow].Source);
        Assert.Equal("slot2", written[(int)SpellScriptSlot.Initiation].Source);
    }

    [Fact]
    public void Reverting_puts_every_field_back()
    {
        var original = Spell();
        var editor = new SpellEditorViewModel(original);

        editor.Name = "Other";
        editor.Level = 9;
        editor.Lingers = true;
        editor.Scripts[0].Source = "$RETURN 1;";
        editor.Parameters[1].Text = "99";
        editor.Baseclasses[0].IsAllowed = false;
        editor.Sounds[0].FileName = "other.wav";

        editor.Revert();

        Assert.False(editor.IsDirty);
        Assert.Equal(original.AllowedBaseclasses, editor.ToRecord().AllowedBaseclasses);
        Assert.Equal(original, editor.ToRecord() with
        {
            AllowedBaseclasses = original.AllowedBaseclasses,
            Parameters = original.Parameters,
            Sounds = original.Sounds,
            Scripts = original.Scripts,
        });
    }

    /// <remarks>
    /// The list is rebuilt from tick boxes, and the order it comes out in is the order the reader
    /// produced — otherwise an untouched spell's caster list would be reordered on save.
    /// </remarks>
    [Fact]
    public void The_caster_list_keeps_the_spells_own_order_and_offers_the_designs_others()
    {
        var editor = new SpellEditorViewModel(
            Spell() with { AllowedBaseclasses = ["druid", "cleric"] },
            ["cleric", "fighter", "magicUser"]);

        Assert.Equal(["druid", "cleric", "fighter", "magicUser"],
                     editor.Baseclasses.Select(b => b.Name));
        Assert.Equal(["druid", "cleric"], editor.ToRecord().AllowedBaseclasses);

        editor.Baseclasses[2].IsAllowed = true;
        Assert.Equal(["druid", "cleric", "fighter"], editor.ToRecord().AllowedBaseclasses);
    }

    /// <remarks>
    /// A design too old for <c>EffectDuration</c> has none, and a form that assumed one would throw
    /// on opening it.
    /// </remarks>
    [Fact]
    public void A_record_without_an_effect_duration_still_opens()
    {
        var editor = new SpellEditorViewModel(Spell() with { EffectDuration = null });

        Assert.Null(editor.EffectDuration);
        Assert.Null(editor.ToRecord().EffectDuration);
    }

    /// <remarks>
    /// Three parameters rather than six is what a design between 0.670 and 0.999432 carries.
    /// </remarks>
    [Fact]
    public void A_record_with_only_three_parameters_still_opens()
    {
        var editor = new SpellEditorViewModel(
            Spell() with { Parameters = [Dice("3"), Dice("1"), Dice("2")] });

        Assert.Equal(3, editor.Parameters.Count);
        Assert.Equal(3, editor.ToRecord().Parameters.Count);
    }

    /// <remarks>
    /// Only <c>DP2</c> carries text. Offering a text box over a <c>DP1</c> would let a designer type
    /// an expression the record has nowhere to put.
    /// </remarks>
    [Fact]
    public void A_legacy_dice_form_is_shown_but_not_edited()
    {
        var packed = new DicePlus(DicePlusReader.TagPacked, string.Empty, string.Empty,
                                  2, 6, 1, 0, 0, 0, []);
        var editor = new SpellEditorViewModel(Spell() with { Parameters = [packed] });

        Assert.False(editor.Parameters[0].IsEditable);
        Assert.Contains("2d6+1", editor.Parameters[0].LegacyValue);
    }
}
