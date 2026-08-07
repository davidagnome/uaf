using UAF.Rules;
using UAF.Scripting;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers which stats a script can actually write back to a character.</summary>
public class GameScriptHostWriteTests
{
    [Fact]
    public void Only_the_permanent_layer_of_a_score_is_writable()
    {
        // There is no SET_CHAR_ADJ_STR: an adjusted score is the sum of effects, and a script
        // changes it by adding one rather than by assignment.
        Assert.Equal(AbilityScore.Strength,
                     AbilityLayers.PermanentScore(GpdlCharStat.PermanentStrength));

        Assert.Null(AbilityLayers.PermanentScore(GpdlCharStat.AdjustedStrength));
        Assert.Null(AbilityLayers.PermanentScore(GpdlCharStat.LimitedStrength));
    }

    [Fact]
    public void Every_score_has_a_writable_permanent_form()
    {
        var writable = new[]
        {
            GpdlCharStat.PermanentStrength, GpdlCharStat.PermanentStrengthMod,
            GpdlCharStat.PermanentIntelligence, GpdlCharStat.PermanentWisdom,
            GpdlCharStat.PermanentDexterity, GpdlCharStat.PermanentConstitution,
            GpdlCharStat.PermanentCharisma,
        };

        Assert.All(writable, stat => Assert.NotNull(AbilityLayers.PermanentScore(stat)));

        // And each names a different score, so a crossed pair would show up here.
        Assert.Equal(writable.Length,
                     writable.Select(AbilityLayers.PermanentScore).Distinct().Count());
    }

    [Fact]
    public void A_stat_that_is_not_a_score_names_none()
    {
        Assert.Null(AbilityLayers.PermanentScore(GpdlCharStat.HitPoints));
        Assert.Null(AbilityLayers.PermanentScore(GpdlCharStat.Morale));
        Assert.Null(AbilityLayers.PermanentScore(GpdlCharStat.Name));
    }
}
