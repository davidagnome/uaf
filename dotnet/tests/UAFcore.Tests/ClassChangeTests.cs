using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers who may change class, and what changing it does.</summary>
public class ClassChangeTests
{
    private static readonly DicePlus NoDice = new("DP2", "", "", 0, 0, 0, 0, 0, 0, []);

    private static RaceRecord Race(int canChangeClass) =>
        new(0, "Human", NoDice, NoDice, NoDice, NoDice, [], NoDice,
            canChangeClass, 0, 0, 0, 0, [], [], [], [], [], [],
            new SpecabBlock([], [], []));

    private static Character Who(string classId = "Fighter", byte type = 1,
                                 params (string Id, int Level, int Previous)[] baseclasses)
    {
        var record = NewCharacter.Blank with
        {
            Name = "Aramil",
            Type = type,
            Race = "Human",
            ClassId = classId,
            BaseclassStats =
            [
                .. (baseclasses.Length > 0 ? baseclasses : [("fighter", 3, 0)])
                    .Select(b => new BaseclassStats(b.Id, b.Level, b.Previous, 0, 1200)),
            ],
        };

        return new Character(record, MoneyRules.Default);
    }

    private static readonly string[] Classes = ["Fighter", "Cleric", "Magic User"];

    /// <summary>A scripting layer that says yes to everything.</summary>
    private static bool Anything(string from, string to) => true;

    // ---- who may change --------------------------------------------------------------------------

    [Fact]
    public void With_no_scripts_nothing_qualifies()
    {
        // Not a stub: hookParameters[0] starts empty and a class with no CanChangeToClass script
        // leaves it empty, which fails != 'Y'. Every shipped design is in this state.
        var options = ClassChange.Options(Who(), Race(1), Classes, ClassChange.NoScripts,
                                          out var refusal);

        Assert.Empty(options);
        Assert.Equal(ClassChangeRefusal.NoClassQualifies, refusal);
    }

    [Fact]
    public void The_characters_own_class_is_never_offered()
    {
        var options = ClassChange.Options(Who("Fighter"), Race(1), Classes, Anything, out var why);

        Assert.Equal(["Cleric", "Magic User"], options);
        Assert.Equal(ClassChangeRefusal.None, why);
    }

    [Fact]
    public void A_race_that_cannot_change_class_is_refused()
    {
        var options = ClassChange.Options(Who(), Race(0), Classes, Anything, out var refusal);

        Assert.Empty(options);
        Assert.Equal(ClassChangeRefusal.RaceCannot, refusal);
    }

    [Fact]
    public void A_race_the_design_does_not_have_is_refused_rather_than_allowed()
    {
        var options = ClassChange.Options(Who(), race: null, Classes, Anything, out var refusal);

        Assert.Empty(options);
        Assert.Equal(ClassChangeRefusal.RaceUnknown, refusal);
    }

    [Fact]
    public void A_monster_never_changes_class()
    {
        var options = ClassChange.Options(Who(type: ClassChange.MonsterType), Race(1), Classes,
                                          Anything, out var refusal);

        Assert.Empty(options);
        Assert.Equal(ClassChangeRefusal.Monster, refusal);
    }

    [Fact]
    public void The_monster_type_is_three_and_an_npc_may_still_change()
    {
        // CHAR_TYPE 1, NPC_TYPE 2, MONSTER_TYPE 3 -- guessing 2 for a monster blocks NPCs and lets
        // monsters through.
        Assert.Equal(3, ClassChange.MonsterType);

        var options = ClassChange.Options(Who(type: 2), Race(1), Classes, Anything, out _);
        Assert.NotEmpty(options);
    }

    [Fact]
    public void A_record_saved_in_a_party_is_still_recognised_as_a_monster()
    {
        // type is a kind in the low bits and an in-party flag in the top one; GetType() masks the
        // flag off before comparing.
        var options = ClassChange.Options(
            Who(type: ClassChange.MonsterType | EventNpc.InPartyFlag),
            Race(1), Classes, Anything, out var refusal);

        Assert.Equal(ClassChangeRefusal.Monster, refusal);
        Assert.Empty(options);
    }

    [Fact]
    public void Changing_class_twice_is_refused()
    {
        var dual = Who("Cleric", 1, ("fighter", 0, 3), ("cleric", 1, 0));

        Assert.True(ClassChange.IsDualClass(dual));

        var options = ClassChange.Options(dual, Race(1), Classes, Anything, out var refusal);
        Assert.Empty(options);
        Assert.Equal(ClassChangeRefusal.AlreadyDualClassed, refusal);
    }

    [Fact]
    public void The_menu_entry_follows_the_list()
    {
        Assert.False(ClassChange.CanChangeClass(Who(), Race(1), Classes, ClassChange.NoScripts));
        Assert.True(ClassChange.CanChangeClass(Who(), Race(1), Classes, Anything));
    }

    // ---- what changing does ----------------------------------------------------------------------

    [Fact]
    public void The_old_baseclass_drops_to_zero_and_remembers_its_level()
    {
        var who = Who("Fighter", 1, ("fighter", 5, 0));

        ClassChange.Apply(who, "Cleric", ["cleric"]);

        var fighter = who.Baseclass("fighter")!;
        Assert.Equal(0, fighter.CurrentLevel);
        Assert.Equal(5, fighter.PreviousLevel);
        Assert.Equal("Cleric", who.ClassId);
    }

    [Fact]
    public void The_change_is_one_way_because_of_the_row_it_leaves_behind()
    {
        var who = Who("Fighter", 1, ("fighter", 5, 0));
        ClassChange.Apply(who, "Cleric", ["cleric"]);

        Assert.True(ClassChange.IsDualClass(who));
        Assert.Equal(ClassChangeRefusal.AlreadyDualClassed,
                     Refusal(who));

        static ClassChangeRefusal Refusal(Character c)
        {
            ClassChange.Options(c, Race(1), Classes, Anything, out var refusal);
            return refusal;
        }
    }

    [Fact]
    public void The_new_baseclass_starts_at_level_one_with_no_experience()
    {
        var who = Who("Fighter", 1, ("fighter", 5, 0));
        ClassChange.Apply(who, "Cleric", ["cleric"]);

        var cleric = who.Baseclass("cleric")!;
        Assert.Equal(1, cleric.CurrentLevel);
        Assert.Equal(0, cleric.Experience);
        Assert.Equal(0, cleric.PreviousLevel);
    }

    [Fact]
    public void The_old_baseclass_keeps_its_experience()
    {
        var who = Who("Fighter", 1, ("fighter", 5, 0));
        ClassChange.Apply(who, "Cleric", ["cleric"]);

        Assert.Equal(1200, who.Baseclass("fighter")!.Experience);
    }

    [Fact]
    public void A_baseclass_the_character_already_has_is_not_added_twice()
    {
        // And this is where the reference reads the wrong index: with more new baseclasses than
        // existing rows its inner loop indexes past the end.
        var who = Who("Fighter", 1, ("fighter", 5, 0));

        ClassChange.Apply(who, "Fighter Mage", ["fighter", "magicuser"]);

        Assert.Equal(2, who.Baseclasses.Count);
        Assert.Equal(0, who.Baseclass("fighter")!.CurrentLevel);      // kept, and zeroed
        Assert.Equal(1, who.Baseclass("magicuser")!.CurrentLevel);    // added
    }

    [Fact]
    public void Everything_carried_is_unreadied()
    {
        var who = Who();
        who.Items.Add(new ItemInstance(1, "Sword", 0, ReadiedLocation.WeaponHand, 1, 0, 0, 0, 0));
        who.Items.Add(new ItemInstance(2, "Shield", 0, ReadiedLocation.ShieldHand, 1, 0, 0, 0, 0));

        ClassChange.Apply(who, "Cleric", ["cleric"]);

        Assert.All(who.Items, i => Assert.Equal(ReadiedLocation.NotReady, i.ReadyLocation));
    }
}
