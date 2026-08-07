using UAF.Rules;
using UAF.Scripting;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the three layers of an ability score as a script sees them.</summary>
public class AbilityLayersTests
{
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static Character Member(AbilityScores scores)
    {
        var record = new CharacterRecord(
            0, 0, 0, "human", 0, "fighter", 0, 0, 0, "", 0, "Aramil", "aramil",
            0, 0, 0, 0, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, scores,
            0, 0, 0, 0, 0, 0, [], [], [], 0, 0, 0, null, 0,
            null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, new ItemList([], new ReadyItems([])),
            new SpecabBlock([], [], []), []);

        return new Character(record, MoneyRules.Default);
    }

    /// <summary>Strength 16 and a percentile of 50, with the rest middling.</summary>
    private static Character Strong() =>
        Member(new AbilityScores(Strength: 16, StrengthMod: 50, Intelligence: 12, Wisdom: 11,
                                 Dexterity: 14, Constitution: 13, Charisma: 10));

    private static string Read(Character who, GpdlCharStat stat) =>
        AbilityLayers.Read(who, stat) is { } score
            ? score.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

    // ---- the three layers ------------------------------------------------------------------------

    [Fact]
    public void With_no_effects_all_three_layers_agree()
    {
        var who = Strong();

        Assert.Equal("16", Read(who, GpdlCharStat.PermanentStrength));
        Assert.Equal("16", Read(who, GpdlCharStat.AdjustedStrength));
        Assert.Equal("16", Read(who, GpdlCharStat.LimitedStrength));
    }

    [Fact]
    public void An_effect_moves_the_adjusted_score_but_not_the_permanent_one()
    {
        var who = Strong();
        who.Effects.Add(new ActiveSpellEffect(
            new UAF.Rules.SpellEffect("$CHAR_ADJUSTED_STR", 3, SpellEffectFlags.Cumulative),
            StopTime: null, SourceSpell: "bulls strength", Parent: 1));

        Assert.Equal("16", Read(who, GpdlCharStat.PermanentStrength));
        Assert.Equal("19", Read(who, GpdlCharStat.AdjustedStrength));
    }

    [Fact]
    public void The_adjusted_score_is_unbounded_and_the_limited_one_is_not()
    {
        // Which is the point of exposing all three: a script asking for the adjusted form can see
        // a value the rules would never act on.
        var who = Strong();
        who.Effects.Add(new ActiveSpellEffect(
            new UAF.Rules.SpellEffect("$CHAR_ADJUSTED_STR", 40, SpellEffectFlags.Cumulative),
            StopTime: null, SourceSpell: "giant strength", Parent: 1));

        Assert.Equal("56", Read(who, GpdlCharStat.AdjustedStrength));
        Assert.Equal("25", Read(who, GpdlCharStat.LimitedStrength));
    }

    [Fact]
    public void A_drained_score_is_floored_at_three()
    {
        var who = Strong();
        who.Effects.Add(new ActiveSpellEffect(
            new UAF.Rules.SpellEffect("$CHAR_ADJUSTED_STR", -40, SpellEffectFlags.Cumulative),
            StopTime: null, SourceSpell: "weakness", Parent: 1));

        Assert.Equal("-24", Read(who, GpdlCharStat.AdjustedStrength));
        Assert.Equal("3", Read(who, GpdlCharStat.LimitedStrength));
    }

    [Fact]
    public void An_effect_written_against_the_plain_name_reaches_nothing()
    {
        // The reference passes CHAR_ADJUSTED_STR to ApplySpellEffectAdjustments; the commented-out
        // line above it used "$CHAR_STR" and does not any more.
        var who = Strong();
        who.Effects.Add(new ActiveSpellEffect(
            new UAF.Rules.SpellEffect("$CHAR_STR", 5, SpellEffectFlags.Cumulative),
            StopTime: null, SourceSpell: "mislabelled", Parent: 1));

        Assert.Equal("16", Read(who, GpdlCharStat.AdjustedStrength));
    }

    // ---- the scores are not crossed ---------------------------------------------------------------

    [Fact]
    public void Each_score_reads_its_own_field()
    {
        var who = Strong();

        Assert.Equal("16", Read(who, GpdlCharStat.PermanentStrength));
        Assert.Equal("50", Read(who, GpdlCharStat.PermanentStrengthMod));
        Assert.Equal("12", Read(who, GpdlCharStat.PermanentIntelligence));
        Assert.Equal("11", Read(who, GpdlCharStat.PermanentWisdom));
        Assert.Equal("14", Read(who, GpdlCharStat.PermanentDexterity));
        Assert.Equal("13", Read(who, GpdlCharStat.PermanentConstitution));
        Assert.Equal("10", Read(who, GpdlCharStat.PermanentCharisma));
    }

    [Fact]
    public void The_percentile_clamps_to_its_own_range_not_the_scores()
    {
        var who = Strong();
        who.Effects.Add(new ActiveSpellEffect(
            new UAF.Rules.SpellEffect("$CHAR_ADJUSTED_STRMOD", 500, SpellEffectFlags.Cumulative),
            StopTime: null, SourceSpell: "gauntlets", Parent: 1));

        Assert.Equal("550", Read(who, GpdlCharStat.AdjustedStrengthMod));
        Assert.Equal("100", Read(who, GpdlCharStat.LimitedStrengthMod));
    }

    [Fact]
    public void A_stat_that_is_not_an_ability_score_is_not_answered_here()
    {
        // The host falls through to this only after its own switch, so a stat it already knows
        // must not be claimed by the layering.
        Assert.Null(AbilityLayers.Read(Strong(), GpdlCharStat.HitPoints));
        Assert.Null(AbilityLayers.Read(Strong(), GpdlCharStat.Name));
    }
}
