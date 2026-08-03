using UAF.Data;
using UAF.Scripting;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// A design's own monster-placement script, and the two sub-opcodes it needs.
/// </summary>
public class CombatPlacementScriptTests
{
    private static GlobalScripts Library(params (string Hook, string Source)[] scripts) =>
        new(SpecialAbilitiesFile.Parse(
        [
            "\\(BEGIN)",
            $"name = {CombatPlacementScript.AbilityName}",
            .. scripts.Select(s => $"[{s.Hook}] = {s.Source}"),
            "\\(END)",
        ]));

    // ---- which hook ------------------------------------------------------------------------------

    [Theory]
    [InlineData(EncounterDistance.UpClose, "PlaceMonsterClose")]
    [InlineData(EncounterDistance.Nearby, "PlaceMonsterNear")]
    [InlineData(EncounterDistance.FarAway, "PlaceMonsterFar")]
    public void The_hook_is_chosen_by_encounter_distance_alone(EncounterDistance distance,
                                                               string expected)
    {
        Assert.Equal(expected, CombatPlacementScript.HookFor(distance));
    }

    [Fact]
    public void Only_the_far_hook_has_a_built_in_default()
    {
        // The other two exist solely in the CombatPlacement ability that shipped designs carry, so
        // a design with no specialAbilities.txt has no script for an up-close encounter at all.
        var none = new CombatPlacementScript(new GlobalScripts([]));

        Assert.True(none.Has(EncounterDistance.FarAway));
        Assert.False(none.Has(EncounterDistance.UpClose));
        Assert.False(none.Has(EncounterDistance.Nearby));
    }

    [Fact]
    public void A_design_that_authors_a_hook_is_preferred_over_the_default()
    {
        var placement = new CombatPlacementScript(
            Library(("PlaceMonsterClose", "$RETURN \"\";")));

        Assert.True(placement.Has(EncounterDistance.UpClose));
    }

    // ---- the two sub-opcodes ---------------------------------------------------------------------

    private static string Run(string body, GpdlUnhostedEnvironment host)
    {
        var scripts = new GlobalScripts(SpecialAbilitiesFile.Parse(
            ["\\(BEGIN)", "name = A", $"[S] = {body}", "\\(END)"]));

        return scripts.Run("A", "S", host);
    }

    [Fact]
    public void Party_facing_reaches_a_script_as_a_bare_number()
    {
        // The built-in placement script branches on `$GET_PARTY_FACING() >=# 2`, which is south
        // or west -- so this has to be the ordinal, not a name.
        var host = new CombatPlacementHost(new MonsterArrangement(), new CombatMap(8, 8), [],
                                           Facing.South);

        Assert.Equal("2", Run("$RETURN $GET_PARTY_FACING();", host));
    }

    [Fact]
    public void Monster_placement_hands_its_program_to_the_turtle()
    {
        var host = new CombatPlacementHost(new MonsterArrangement(), new CombatMap(8, 8), [],
                                           Facing.North);

        Run("$MonsterPlacement(\"bPV500E\");", host);

        Assert.Equal(["bPV500E"], host.Programs);
    }

    [Fact]
    public void A_script_calling_monster_placement_outside_a_placement_gets_zero()
    {
        // The reference guards on monsterArrangement.active and answers "0" with a debug
        // complaint rather than refusing -- a design error, not a port gap.
        Assert.Equal("0", Run("$RETURN $MonsterPlacement(\"bPV500E\");",
                              new GpdlUnhostedEnvironment()));
    }

    [Fact]
    public void An_unhosted_script_sees_a_facing_of_zero()
    {
        Assert.Equal("0", Run("$RETURN $GET_PARTY_FACING();", new GpdlUnhostedEnvironment()));
    }

    // ---- the built-in program, run through a real script ------------------------------------------

    [Fact]
    public void The_built_in_far_script_picks_its_program_by_facing()
    {
        // This is the one entry in defaultGlobalScripts, run for real: it branches on
        // $GET_PARTY_FACING() >=# 2 and puts the turtle one square further out for south or west.
        var scripts = new GlobalScripts([]);

        foreach (var (facing, expected) in
                 new[] { (Facing.North, "16FbPV500E"), (Facing.South, "17FbPV500E") })
        {
            var host = new CombatPlacementHost(new MonsterArrangement(), new CombatMap(16, 16),
                                               [], facing);

            scripts.Run(CombatPlacementScript.AbilityName, "PlaceMonsterFar", host);

            Assert.Equal([expected], host.Programs);
        }
    }
}
