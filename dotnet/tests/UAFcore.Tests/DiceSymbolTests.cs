using UAF.Rules;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the name resolver a dice expression consults.
/// </summary>
public class DiceSymbolTests
{
    private static readonly DiceSymbols Elf =
        new(Male: true, RaceId: "Elf", ClassId: "Ranger", Level: 7);

    [Fact]
    public void Gender_and_level_are_matched_without_regard_to_case()
    {
        // LookupRefKey uses CompareNoCase for these three, and the corpus writes both "level" and
        // "LEVEL" in spell fields of the same design.
        Assert.Equal(1, Elf.Resolve("Male"));
        Assert.Equal(1, Elf.Resolve("male"));
        Assert.Equal(0, Elf.Resolve("FEMALE"));
        Assert.Equal(7, Elf.Resolve("level"));
        Assert.Equal(7, Elf.Resolve("LEVEL"));
    }

    [Fact]
    public void A_female_character_answers_the_other_way()
    {
        var her = Elf with { Male = false };

        Assert.Equal(0, her.Resolve("Male"));
        Assert.Equal(1, her.Resolve("Female"));
    }

    [Fact]
    public void A_race_test_is_one_when_it_matches_and_zero_when_it_does_not()
    {
        Assert.Equal(1, Elf.Resolve("Race_Elf"));
        Assert.Equal(0, Elf.Resolve("Race_Dwarf"));
        Assert.Equal(1, Elf.Resolve("Class_Ranger"));
        Assert.Equal(0, Elf.Resolve("Class_Thief"));
    }

    [Fact]
    public void The_prefix_is_case_sensitive_but_the_name_is_the_whole_rest()
    {
        // memcmp against "Race", not CompareNoCase -- so a lowercase prefix is not a race test at
        // all and the name goes unresolved.
        Assert.Null(Elf.Resolve("race_Elf"));
        Assert.Equal(0, Elf.Resolve("Race_Half-Orc"));
    }

    [Fact]
    public void A_bare_prefix_is_not_a_test()
    {
        // The guards are refNameLen > 5 and > 6, so there has to be something after the underscore.
        Assert.Null(Elf.Resolve(DiceFormula.RacePrefix));
        Assert.Null(Elf.Resolve(DiceFormula.ClassPrefix));
    }

    [Fact]
    public void A_race_no_design_has_resolves_to_zero_rather_than_failing()
    {
        // Neither prefix is checked against a database, which is what keeps designs corrupted by
        // the editor's re-encoding bug loadable.
        Assert.Equal(0, Elf.Resolve("Race_Race_Race_Elf"));
        Assert.Equal(0, Elf.Resolve("Race_Nonesuch"));
    }

    [Fact]
    public void Anything_else_is_unresolved_rather_than_zero()
    {
        // Including the names the reference does find -- abilities, spellgroups and traits -- all
        // of which its interpreter then scores zero. Telling those from a misspelling needs the
        // design's databases, so this port refuses them by name instead.
        Assert.Null(Elf.Resolve("Strength"));
        Assert.Null(Elf.Resolve("Baseclass_Fighter"));
        Assert.Null(Elf.Resolve(""));
    }

    [Fact]
    public void A_character_with_no_race_matches_no_race_test()
    {
        var nobody = new DiceSymbols(Male: false, RaceId: null, ClassId: null, Level: 0);

        Assert.Equal(0, nobody.Resolve("Race_Elf"));
        Assert.Equal(0, nobody.Resolve("Class_Ranger"));
        Assert.Equal(0, nobody.Resolve("level"));
    }
}
