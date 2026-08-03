using UAF.Data;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Scripted teleporter destinations — a transfer whose <c>destEP</c> is −3.
/// </summary>
public class TeleporterDestinationsTests
{
    // ---- the script name carries the arguments ----------------------------------------------------

    [Fact]
    public void The_script_name_is_the_source_square_with_a_one_based_level()
    {
        // There is no parameter passing: the design authors one script per source square and the
        // name IS the argument list. The reference formats destLevel + 1.
        Assert.Equal("/1/5/6", TeleporterDestinations.ScriptName(level: 0, x: 5, y: 6));
        Assert.Equal("/4/0/0", TeleporterDestinations.ScriptName(level: 3, x: 0, y: 0));
    }

    // ---- parsing the answer -----------------------------------------------------------------------

    [Fact]
    public void The_answer_is_read_as_slash_level_slash_x_slash_y()
    {
        // ...and the level comes back one-based too, so both directions carry the same off-by-one
        // and a script written against the displayed level number is correct.
        Assert.Equal((0, 5, 6), TeleporterDestinations.Parse("/1/5/6"));
        Assert.Equal((3, 12, 20), TeleporterDestinations.Parse("/4/12/20"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1/5/6")]                                // no leading slash
    [InlineData("/1/5")]                                 // only two numbers
    [InlineData("/1//6")]                                // a missing number, not a zero
    [InlineData("/a/b/c")]
    [InlineData("nowhere")]
    public void An_answer_that_does_not_parse_yields_nothing(string answer)
    {
        // sscanf(...) == 3 is the reference's test, so a partial answer changes nothing at all
        // rather than partially applying.
        Assert.Null(TeleporterDestinations.Parse(answer));
    }

    [Fact]
    public void Trailing_text_after_the_third_number_is_ignored()
    {
        // sscanf reports three conversions and stops looking.
        Assert.Equal((0, 5, 6), TeleporterDestinations.Parse("/1/5/6 and then some"));
    }

    [Fact]
    public void Whitespace_and_signs_are_accepted_as_scanf_accepts_them()
    {
        Assert.Equal((0, 5, 6), TeleporterDestinations.Parse("/ 1/ 5/ 6"));
        Assert.Equal((-2, 5, 6), TeleporterDestinations.Parse("/-1/5/6"));
    }

    // ---- end to end -------------------------------------------------------------------------------

    private static GlobalScripts Design(string scriptName, string answer) =>
        new(SpecialAbilitiesFile.Parse(
        [
            "\\(BEGIN)",
            $"name = {TeleporterDestinations.AbilityName}",
            $"[{scriptName}] = $RETURN \"{answer}\";",
            "\\(END)",
        ]));

    [Fact]
    public void A_design_that_authors_the_script_gets_its_destination()
    {
        var scripts = Design("/1/5/6", "/2/10/11");

        Assert.True(scripts.Has(TeleporterDestinations.AbilityName, "/1/5/6"));
        Assert.Equal((1, 10, 11), TeleporterDestinations.Parse(
            scripts.Run(TeleporterDestinations.AbilityName, "/1/5/6",
                        new UAF.Scripting.GpdlUnhostedEnvironment())));
    }

    [Fact]
    public void A_design_that_authors_none_has_nothing_to_offer()
    {
        // TeleporterDestinations is NOT among the built-in defaults -- there is exactly one of
        // those and it is CombatPlacement -- so a design without the ability resolves nothing.
        var scripts = new GlobalScripts([]);

        Assert.False(scripts.Has(TeleporterDestinations.AbilityName, "/1/5/6"));
    }

    [Fact]
    public void A_square_the_design_did_not_write_a_script_for_resolves_nothing()
    {
        var scripts = Design("/1/5/6", "/2/10/11");

        Assert.False(scripts.Has(TeleporterDestinations.AbilityName, "/1/9/9"));
    }
}
