using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the character generator's spine and the rules behind what each step offers.
/// </summary>
/// <remarks>
/// The first wizard in the port: ten screens in sequence, each writing into one shared character.
/// Four of the ten run.
/// </remarks>
public class CharacterCreationTests
{
    // ---- the delimited string ------------------------------------------------------------------

    [Fact]
    public void A_delimited_string_is_length_prefixed_not_separated()
    {
        // Each element is a count, a full stop, then exactly that many characters -- so an element
        // may contain a full stop or a digit, which is the point.
        Assert.Equal(["Dwarf", "Elf"], DelimitedString.Parse("5.Dwarf3.Elf"));
        Assert.Equal(["a.b", "7"], DelimitedString.Parse("3.a.b1.7"));
    }

    [Fact]
    public void An_empty_string_is_legal_and_contains_nothing()
    {
        // The two answer differently on purpose, and callers lean on it: an absent AllowedClass
        // allows every class, an empty one allows none.
        Assert.True(DelimitedString.IsLegal(""));
        Assert.False(DelimitedString.Contains("", "Fighter"));
    }

    [Fact]
    public void A_malformed_string_is_not_legal()
    {
        Assert.False(DelimitedString.IsLegal("Dwarf"));       // no length prefix
        Assert.False(DelimitedString.IsLegal("9.Elf"));       // longer than what follows
        Assert.Empty(DelimitedString.Parse("Dwarf"));
    }

    [Fact]
    public void Formatting_and_parsing_are_inverses()
    {
        string[] elements = ["Fighter", "Magic User", "a.b"];

        Assert.Equal(elements, DelimitedString.Parse(DelimitedString.Format(elements)));
    }

    // ---- the wizard ----------------------------------------------------------------------------

    [Fact]
    public void The_steps_run_race_gender_class_alignment()
    {
        // The order is a dependency chain: race and gender both narrow the classes on offer, so
        // class cannot come first.
        var making = new CharacterCreation();
        Assert.Equal(CreationStep.Race, making.Step);

        making.Choose("Elf");
        Assert.Equal(CreationStep.Gender, making.Step);
        Assert.Equal("Elf", making.RaceId);

        making.Choose(nameof(Gender.Female));
        Assert.Equal(CreationStep.Class, making.Step);
        Assert.Equal(Gender.Female, making.Gender);

        making.Choose("Ranger");
        Assert.Equal(CreationStep.Alignment, making.Step);
        Assert.Equal("Ranger", making.ClassId);

        making.Choose("4");
        Assert.Equal(CreationStep.Stats, making.Step);
        Assert.Equal(4, making.Alignment);
    }

    [Fact]
    public void Aborting_ends_it_rather_than_stepping_back()
    {
        // Every picker's EXIT sets m_AbortCharCreation and unwinds the whole thing; none of them
        // goes back one, so a wrong race means starting again.
        var making = new CharacterCreation();
        making.Choose("Elf");

        making.Abort();

        Assert.True(making.Aborted);
        Assert.Equal(CreationStep.Done, making.Step);
    }

    // ---- what each step offers -----------------------------------------------------------------

    private static readonly SpecabBlock NoSpecabs = new([], [], []);

    private static readonly DicePlus NoDice = new("", "", "", 0, 0, 0, 0, 0, 0, []);

    private static RaceRecord Race(string name, string? allowedClasses = null) =>
        new(0, name, NoDice, NoDice, NoDice,
            NoDice, [], NoDice, 0, 0, 0, 0, 0,
            allowedClasses is null
                ? []
                : [new AslEntry(CreationChoices.AllowedClassAttribute, 0, allowedClasses)],
            [], [], [], [], [], NoSpecabs);

    private static ClassRecord Class(string name, params string[] baseclasses) =>
        new("ClassV1", 0, name, baseclasses, NoSpecabs, [], NoDice,
            new ItemList([], ReadyItems.Empty), "");

    private static BaseclassRecord Baseclass(string name, params string[] allowedRaces) =>
        new(name, 0, name, [], allowedRaces, [], 0, [], "", [], [], NoSpecabs, [], [], [], [], [],
            [], []);

    private static readonly Dictionary<string, BaseclassRecord> Baseclasses = new()
    {
        ["fighter"] = Baseclass("fighter", "Human", "Dwarf"),
        ["mage"] = Baseclass("mage", "Human", "Elf"),
    };

    private static readonly Dictionary<string, ClassRecord> Classes = new()
    {
        ["Fighter"] = Class("Fighter", "fighter"),
        ["Mage"] = Class("Mage", "mage"),
        ["Fighter/Mage"] = Class("Fighter/Mage", "fighter", "mage"),
    };

    private static List<CreationChoice> Offered(RaceRecord race, string id = "Human") =>
        CreationChoices.ClassesFor(id, Classes,
                                   new Dictionary<string, RaceRecord> { [id] = race },
                                   Baseclasses);

    [Fact]
    public void A_race_with_no_AllowedClass_may_take_every_class()
    {
        // IsAllowedClass returns true outright when the attribute is missing, and that is the
        // FIRST gate in IsRaceAllowed -- so the baseclass rule never runs and even a multi-class
        // is offered. Most designs write no such attribute, so this is the usual case.
        Assert.Equal(["Fighter", "Fighter/Mage", "Mage"],
                     Offered(Race("Human")).Select(c => c.Name).Order());
    }

    [Fact]
    public void A_malformed_AllowedClass_disables_the_filter_the_same_way()
    {
        // "if (!ds.IsLegal()) return true" -- a value that is not a delimited string is treated
        // as no restriction rather than as no classes.
        Assert.Equal(3, Offered(Race("Human", "Fighter")).Count);   // no length prefix
    }

    [Fact]
    public void An_empty_AllowedClass_is_the_opposite_of_an_absent_one()
    {
        // Legal, and contains nothing -- so every class falls through to the second gate, which
        // is where the baseclass table finally gets a say. Absent and empty are opposite answers
        // from adjacent lines.
        var offered = Offered(Race("Human", ""));

        Assert.Equal(["Fighter", "Mage"], offered.Select(c => c.Name).Order());
    }

    [Fact]
    public void The_second_gate_refuses_a_class_whose_baseclass_refuses_the_race()
    {
        // The mage baseclass names Human and Elf, not Dwarf.
        var offered = CreationChoices.ClassesFor(
            "Dwarf", Classes,
            new Dictionary<string, RaceRecord> { ["Dwarf"] = Race("Dwarf", "") }, Baseclasses);

        Assert.Equal(["Fighter"], offered.Select(c => c.Name));
    }

    [Fact]
    public void The_second_gate_never_admits_a_multi_class()
    {
        // "allowed if we have a single Base Class and the Base Class allows this race, or the
        // race explicitly allows this class" -- so once a race writes an AllowedClass list at
        // all, a multi-class is offered only if that list names it. Both its baseclasses
        // permitting the race counts for nothing.
        var restricted = Race("Human", "");
        Assert.DoesNotContain(Offered(restricted), c => c.Name == "Fighter/Mage");

        var naming = Race("Human", DelimitedString.Format(["Fighter/Mage"]));
        Assert.Contains(Offered(naming), c => c.Name == "Fighter/Mage");
    }

    [Fact]
    public void A_race_naming_one_class_still_gets_the_single_class_fallback()
    {
        // Naming Fighter/Mage does not hide Fighter and Mage: they pass the second gate on their
        // own baseclasses. A design writing this list to mean "only these" would be surprised.
        var naming = Race("Human", DelimitedString.Format(["Fighter/Mage"]));

        Assert.Equal(["Fighter", "Fighter/Mage", "Mage"],
                     Offered(naming).Select(c => c.Name).Order());
    }

    [Fact]
    public void There_are_two_genders_and_nine_alignments()
    {
        // Restrictions passes "M" or "F" to the baseclass hook, so the generator's notion of
        // gender is these two whatever the genderType enum holds elsewhere.
        Assert.Equal(["MALE", "FEMALE"], CreationChoices.Genders.Select(g => g.Name));
        Assert.Equal(9, CreationChoices.Alignments.Length);
        Assert.Equal("TRUE NEUTRAL", CreationChoices.Alignments[4].Name);
    }

    [Fact]
    public void A_design_with_no_race_table_offers_no_races()
    {
        Assert.Empty(CreationChoices.Races(null));
        Assert.Empty(CreationChoices.ClassesFor("Human", null, null, null));
        Assert.Empty(CreationChoices.ClassesFor(null, Classes, null, Baseclasses));
    }

    [Fact]
    public void Races_are_offered_in_name_order()
    {
        var races = new Dictionary<string, RaceRecord>
        {
            ["Human"] = Race("Human"),
            ["Dwarf"] = Race("Dwarf"),
        };

        Assert.Equal(["Dwarf", "Human"], CreationChoices.Races(races).Select(r => r.Id));
    }
}
