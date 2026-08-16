using UAF.Serialization;

namespace UAFedit.Spells.Tests;

/// <summary>The label tables, and the one projection that is easy to get backwards.</summary>
public class SpellChoicesTests
{
    /// <summary>
    /// The enumerators are in the reference's order, which is not a tidy one.
    /// </summary>
    /// <remarks>
    /// A view binds <c>SelectedIndex</c> straight to the stored field, so sorting these lists — or
    /// grouping the area shapes, which is the tempting tidy-up — silently rewrites every spell in
    /// the design. <c>Selected by Hit Dice</c> at 5, between the circle and the line variants, is
    /// the position that gives the game away.
    /// </remarks>
    [Fact]
    public void The_targeting_types_keep_the_references_own_order()
    {
        Assert.Equal("Self", SpellChoices.Targeting[0]);
        Assert.Equal("Area: Circle", SpellChoices.Targeting[4]);
        Assert.Equal("Selected by Hit Dice", SpellChoices.Targeting[5]);
        Assert.Equal("Area: Line, Pick Start", SpellChoices.Targeting[6]);
        Assert.Equal("Area: Cone", SpellChoices.Targeting[9]);
    }

    /// <remarks>
    /// Designs do carry values outside the enum — nothing validates them on the way in — and a list
    /// column that showed a blank would hide one.
    /// </remarks>
    [Fact]
    public void A_value_outside_the_enum_is_shown_as_a_number_rather_than_hidden()
    {
        Assert.Equal("? (42)", SpellChoices.Label(SpellChoices.Targeting, 42));
        Assert.Equal("? (-1)", SpellChoices.Label(SpellChoices.SaveResult, -1));
        Assert.Equal("Save for Half", SpellChoices.Label(SpellChoices.SaveResult, 2));
    }

    [Fact]
    public void An_unknown_targeting_type_falls_back_to_the_bare_placeholders()
    {
        var labels = SpellChoices.ParameterLabels(99);

        Assert.Equal(["Duration", "P1", "P2", "P3", "P4", "P5"], labels);
    }

    /// <remarks>
    /// P4 and P5 are empty-labelled for all ten targeting types — edit boxes with no reachable
    /// meaning, kept because the record carries them.
    /// </remarks>
    [Fact]
    public void The_last_two_parameters_are_unused_by_every_targeting_type()
    {
        for (int targeting = 0; targeting < SpellChoices.Targeting.Count; targeting++)
        {
            var labels = SpellChoices.ParameterLabels(targeting);

            Assert.Equal(string.Empty, labels[4]);
            Assert.Equal(string.Empty, labels[5]);
            Assert.Equal("Duration", labels[0]);
        }
    }

    /// <summary>
    /// An effect's activation script is <c>String2</c>; <c>Scripts[0]</c> is its compiled binary.
    /// </summary>
    /// <remarks>
    /// <b>The port's field names invite exactly the wrong guess.</b> <c>m_string2</c> is
    /// <c>ActivationScript</c> and <c>m_string3</c> — the first entry of the <c>Scripts</c> list —
    /// is <c>ActivationBinary</c> (<c>Shared/class.h:2410</c>, <c>:2415</c>), so the list starts
    /// with a binary and the source that goes with it is not in the list at all. Both corpus
    /// designs show it plainly: the first effect of the first spell carrying one has 327 characters
    /// of source in <c>String2</c> and 157 of compiled code in <c>Scripts[0]</c>.
    /// </remarks>
    [Fact]
    public void An_effects_activation_column_reads_the_source_not_the_binary()
    {
        var effect = new SpellEffect(
            IndexKey: "$CHAR_MORALE",
            Flags: SpellEffectViewModel.CumulativeFlag,
            ChangeResult: 0,
            String2: "$RETURN 1;\r\n\t$RETURN 2;",
            SourceOfEffect: 0,
            Parent: 0,
            Scripts: ["compiled-binary-not-source"],
            StopTime: 0,
            Data: 0,
            ChangeData: new DicePlus(DicePlusReader.TagText, "2d6", string.Empty,
                                     0, 0, 0, 0, 0, 0, []));

        var row = new SpellEffectViewModel(effect);

        Assert.Equal("$CHAR_MORALE", row.Affected);
        Assert.Equal("2d6", row.ChangeBy);
        Assert.Equal("Yes", row.Cumulative);

        // The source, with the line breaks flattened out as the reference's list column does.
        Assert.Equal("$RETURN 1;$RETURN 2;", row.Activation);
    }
}
