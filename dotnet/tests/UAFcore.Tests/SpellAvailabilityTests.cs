using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers which spells a character is offered at creation.
/// </summary>
public class SpellAvailabilityTests
{
    private static SpellRecord Spell(string name, string school = "magic", int level = 1,
                                     int allowScribe = 1, params string[] baseclasses) =>
        new(0, name, "", school, baseclasses.Length == 0 ? ["mage"] : baseclasses,
            level, 0, 0, 0, 0, 0, 0, 0, 0, allowScribe, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            [], [], null, [], [], "", [], null, new SpecabBlock([], [], []), []);

    /// <summary>A character who may know anything in "magic" up to level 3.</summary>
    private static bool Mage(string school, int level) => school == "magic" && level <= 3;

    private static readonly string[] MageClass = ["mage"];

    private static (List<AvailableSpell> Spells, int MaxLevel) Offer(
        IEnumerable<SpellRecord> spells, Func<SpellRecord, int?>? probability = null) =>
        SpellAvailability.For(spells, MageClass, Mage, probability);

    // ---- the four filters ----------------------------------------------------------------------

    [Fact]
    public void A_spell_that_cannot_be_scribed_is_never_offered()
    {
        var (offered, _) = Offer([Spell("Magic Missile"),
                                  Spell("Wish", allowScribe: 0)]);

        Assert.Equal(["Magic Missile"], offered.Select(s => s.Spell.Name));
    }

    [Fact]
    public void A_spell_beyond_the_characters_ability_is_not_offered()
    {
        var (offered, _) = Offer([Spell("Sleep", level: 1), Spell("Meteor", level: 9)]);

        Assert.Equal(["Sleep"], offered.Select(s => s.Spell.Name));
    }

    [Fact]
    public void A_school_the_character_has_no_ability_in_is_not_offered()
    {
        var (offered, _) = Offer([Spell("Cure", school: "clerical"), Spell("Sleep")]);

        Assert.Equal(["Sleep"], offered.Select(s => s.Spell.Name));
    }

    [Fact]
    public void The_class_must_share_a_baseclass_with_the_spell()
    {
        var (offered, _) = Offer([Spell("Sleep", baseclasses: "mage"),
                                  Spell("Bless", baseclasses: "cleric")]);

        Assert.Equal(["Sleep"], offered.Select(s => s.Spell.Name));
    }

    [Fact]
    public void A_spell_allowing_no_baseclass_is_offered_to_nobody()
    {
        var none = Spell("Orphan") with { AllowedBaseclasses = [] };

        Assert.Empty(Offer([none]).Spells);
    }

    // ---- the hook ------------------------------------------------------------------------------

    [Fact]
    public void With_no_script_every_offered_spell_is_certain()
    {
        // The hook runs on the class first and the spell second, and an empty reply from both
        // means 100. So the absence of scripting is more generous than a scripted design and
        // never less.
        var (offered, _) = Offer([Spell("Sleep")]);

        Assert.Equal(SpellAvailability.CertainProbability, offered[0].Probability);
    }

    [Fact]
    public void A_probability_of_zero_removes_the_spell_rather_than_offering_it()
    {
        // if (probability != 0) guards the add, so a hook can hide a spell as well as make it
        // unlikely.
        var (offered, _) = Offer([Spell("Sleep"), Spell("Hidden")],
                                 s => s.Name == "Hidden" ? 0 : 100);

        Assert.Equal(["Sleep"], offered.Select(s => s.Spell.Name));
    }

    [Fact]
    public void A_hook_can_make_a_spell_merely_unlikely()
    {
        var (offered, _) = Offer([Spell("Sleep")], _ => 15);

        Assert.Equal(15, offered[0].Probability);
    }

    // ---- the maximum level ---------------------------------------------------------------------

    [Fact]
    public void The_maximum_level_is_taken_from_what_is_offered_not_from_what_exists()
    {
        // Meteor is level 9 and filtered out, so it does not raise the ceiling.
        var (_, max) = Offer([Spell("Sleep", level: 1), Spell("Fireball", level: 3),
                              Spell("Meteor", level: 9)]);

        Assert.Equal(3, max);
    }

    [Fact]
    public void Nothing_on_offer_is_a_maximum_of_zero()
    {
        var (offered, max) = Offer([Spell("Meteor", level: 9)]);

        Assert.Empty(offered);
        Assert.Equal(0, max);
    }

    // ---- grouping for the acquisition rules ----------------------------------------------------

    [Fact]
    public void Index_zero_is_the_totals_row_and_holds_no_spells()
    {
        // The acquisition rules read index 0 for the global floor and ceiling and start their
        // loop at 1, so the slot has to be reserved rather than packed from the first level.
        var (offered, max) = Offer([Spell("Sleep", level: 1), Spell("Fireball", level: 3)]);

        var levels = SpellAvailability.ByLevel(offered, max);

        Assert.Equal(4, levels.Count);              // 0..3
        Assert.Empty(levels[0]);
        Assert.Single(levels[1]);
        Assert.Empty(levels[2]);
        Assert.Single(levels[3]);
    }

    [Fact]
    public void An_empty_offer_still_has_its_totals_row()
    {
        var levels = SpellAvailability.ByLevel([], maxLevel: 0);

        Assert.Single(levels);
        Assert.Empty(levels[0]);
    }
}
