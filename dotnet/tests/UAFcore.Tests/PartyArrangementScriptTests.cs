using UAF.Data;
using UAF.Scripting;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// A design's own party-formation hooks — <c>PartyArrangement</c> and the four
/// <c>PartyOrigin&lt;direction&gt;</c> scripts in its <c>Global_Combat</c> ability.
/// </summary>
public class PartyArrangementScriptTests
{
    private static GlobalScripts Library(params (string Hook, string Source)[] scripts) =>
        new(SpecialAbilitiesFile.Parse(
        [
            "\\(BEGIN)",
            $"name = {PartyArrangementScript.AbilityName}",
            .. scripts.Select(s => $"[{s.Hook}] = {s.Source}"),
            "\\(END)",
        ]));

    private static PartyArrangementScript With(params (string Hook, string Source)[] scripts) =>
        new(Library(scripts));

    // ---- which hook ------------------------------------------------------------------------------

    [Theory]
    [InlineData(Facing.North, "PartyOriginNorth")]
    [InlineData(Facing.East, "PartyOriginEast")]
    [InlineData(Facing.South, "PartyOriginSouth")]
    [InlineData(Facing.West, "PartyOriginWest")]
    public void The_origin_hook_is_chosen_by_facing(Facing facing, string expected)
    {
        Assert.Equal(expected, PartyArrangementScript.OriginHookFor(facing));
    }

    // ---- the origin hooks ------------------------------------------------------------------------

    [Fact]
    public void A_design_with_no_origin_hook_keeps_the_start_square()
    {
        Assert.Equal((0, 0), With().Origin(Facing.North));
    }

    [Fact]
    public void An_origin_hook_shifts_the_formation()
    {
        var script = With(("PartyOriginNorth",
                           "$SET_HOOK_PARAM(\"5\", \"3\"); $SET_HOOK_PARAM(\"6\", \"-2\");"));

        Assert.Equal((3, -2), script.Origin(Facing.North));
    }

    [Fact]
    public void An_origin_offset_is_clamped_to_eight()
    {
        var script = With(("PartyOriginEast",
                           "$SET_HOOK_PARAM(\"5\", \"99\"); $SET_HOOK_PARAM(\"6\", \"-99\");"));

        Assert.Equal((8, -8), script.Origin(Facing.East));
    }

    // ---- the arrangement hook --------------------------------------------------------------------

    [Fact]
    public void A_design_with_no_arrangement_hook_uses_the_built_in()
    {
        var script = With();

        Assert.Equal(PartyArrangements.Indoor, script.Arrangement(outdoor: false, Facing.North));
        Assert.Equal(PartyArrangements.Outdoor, script.Arrangement(outdoor: true, Facing.North));
    }

    [Fact]
    public void A_right_length_arrangement_hook_replaces_the_table()
    {
        string replacement = new('A', PartyArrangements.Indoor.Length);
        var script = With(("PartyArrangement", $"$RETURN \"{replacement}\";"));

        Assert.Equal(replacement, script.Arrangement(outdoor: false, Facing.North));
    }

    [Fact]
    public void A_wrong_length_arrangement_hook_is_ignored()
    {
        var script = With(("PartyArrangement", "$RETURN \"too short\";"));

        Assert.Equal(PartyArrangements.Indoor, script.Arrangement(outdoor: false, Facing.North));
    }

    // ---- ScriptAtoI ------------------------------------------------------------------------------

    [Theory]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    [InlineData("-2", -2)]
    [InlineData("+4", 4)]
    [InlineData("99", 8)]
    [InlineData("-99", -8)]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData("12xyz", 8)]
    public void ScriptAtoI_parses_a_numeric_prefix_and_clamps(string text, int expected)
    {
        Assert.Equal(expected, PartyArrangementScript.ScriptAtoI(text));
    }
}
