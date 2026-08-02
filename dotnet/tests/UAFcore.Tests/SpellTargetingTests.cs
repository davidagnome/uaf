using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers what a spell may target, before anything is picked.</summary>
public class SpellTargetingTests
{
    private static SpellTargetingSetup Setup(SpellTargeting targeting, int targets = 3,
                                             int range = 5, int width = 2, int height = 4,
                                             int partySize = 6, bool inCombat = true) =>
        SpellTargets.Setup(targeting, targets, range, width, height, partySize, inCombat);

    // ---- whether anything has to be picked -----------------------------------------------------

    [Theory]
    [InlineData(SpellTargeting.Self)]
    [InlineData(SpellTargeting.WholeParty)]
    public void Self_and_whole_party_never_need_a_selection(SpellTargeting targeting)
    {
        Assert.False(SpellTargets.NeedsSelection(targeting, inCombat: true));
        Assert.False(SpellTargets.NeedsSelection(targeting, inCombat: false));
    }

    [Theory]
    [InlineData(SpellTargeting.SelectedByCount)]
    [InlineData(SpellTargeting.TouchedTargets)]
    [InlineData(SpellTargeting.SelectByHitDice)]
    public void Individually_chosen_spells_always_need_a_selection(SpellTargeting targeting)
    {
        Assert.True(SpellTargets.NeedsSelection(targeting, inCombat: true));
        Assert.True(SpellTargets.NeedsSelection(targeting, inCombat: false));
    }

    [Theory]
    [InlineData(SpellTargeting.AreaCircle)]
    [InlineData(SpellTargeting.AreaSquare)]
    [InlineData(SpellTargeting.AreaCone)]
    [InlineData(SpellTargeting.AreaLinePickStart)]
    [InlineData(SpellTargeting.AreaLinePickEnd)]
    public void An_area_spell_needs_a_square_in_combat_and_nothing_outside_it(
        SpellTargeting targeting)
    {
        Assert.True(SpellTargets.NeedsSelection(targeting, inCombat: true));
        Assert.False(SpellTargets.NeedsSelection(targeting, inCombat: false));
    }

    // ---- the setup -----------------------------------------------------------------------------

    [Fact]
    public void Self_targets_one_at_no_range()
    {
        var setup = Setup(SpellTargeting.Self);

        Assert.Equal(1, setup.MaxTargets);
        Assert.Equal(0, setup.MaxRange);
        Assert.True(setup.SelectingUnits);
        Assert.False(setup.IsArea);
    }

    [Fact]
    public void Whole_party_takes_the_party_size_as_its_cap()
    {
        var setup = Setup(SpellTargeting.WholeParty, targets: 2, partySize: 6);

        Assert.Equal(6, setup.MaxTargets);
        Assert.Equal(0, setup.MaxRange);
    }

    [Fact]
    public void A_range_of_zero_means_unlimited_rather_than_nothing()
    {
        var setup = Setup(SpellTargeting.SelectedByCount, range: 0);

        Assert.Equal(SpellTargetingSetup.Unlimited, setup.MaxRange);
    }

    [Fact]
    public void Touched_targets_gets_a_range_of_nine_thousand_not_one()
    {
        // The comment says "within range 1" and the line setting 1 is commented out; the reach is
        // enforced by a one-square box instead.
        var setup = Setup(SpellTargeting.TouchedTargets, targets: 2);

        Assert.Equal(SpellTargetingSetup.TouchRange, setup.MaxRange);
        Assert.Equal(2, setup.MaxTargets);
    }

    [Fact]
    public void Select_by_hit_dice_replaces_the_target_count_with_a_budget()
    {
        var setup = Setup(SpellTargeting.SelectByHitDice, targets: 8);

        Assert.Equal(0, setup.MaxTargets);
        Assert.Equal(8, setup.MaxHitDice);
    }

    [Fact]
    public void An_area_spell_in_combat_picks_squares_and_keeps_its_dimensions()
    {
        var setup = Setup(SpellTargeting.AreaSquare, targets: 3, range: 5, width: 2, height: 4);

        Assert.False(setup.SelectingUnits);
        Assert.True(setup.IsArea);
        Assert.Equal(3, setup.MaxTargets);
        Assert.Equal(5, setup.MaxRange);
        Assert.Equal(2, setup.Width);
        Assert.Equal(4, setup.Height);
    }

    [Theory]
    [InlineData(SpellTargeting.AreaCircle)]
    [InlineData(SpellTargeting.AreaSquare)]
    [InlineData(SpellTargeting.AreaCone)]
    [InlineData(SpellTargeting.AreaLinePickStart)]
    [InlineData(SpellTargeting.AreaLinePickEnd)]
    public void Out_of_combat_every_area_spell_becomes_the_whole_party(SpellTargeting targeting)
    {
        // Each area branch has an else whose comment reads "acts like ttype=WholeParty". The
        // width and height the design supplied are dropped.
        var setup = Setup(targeting, targets: 3, range: 5, width: 2, height: 4,
                          partySize: 6, inCombat: false);

        Assert.True(setup.SelectingUnits);
        Assert.Equal(6, setup.MaxTargets);
        Assert.Equal(0, setup.MaxRange);
        Assert.Equal(0, setup.Width);
        Assert.Equal(0, setup.Height);
    }

    [Fact]
    public void Only_the_area_shapes_are_areas()
    {
        Assert.False(Setup(SpellTargeting.Self).IsArea);
        Assert.False(Setup(SpellTargeting.SelectedByCount).IsArea);
        Assert.False(Setup(SpellTargeting.WholeParty).IsArea);
        Assert.False(Setup(SpellTargeting.TouchedTargets).IsArea);
        Assert.False(Setup(SpellTargeting.SelectByHitDice).IsArea);
        Assert.True(Setup(SpellTargeting.AreaCircle).IsArea);
    }

    // ---- who may be targeted -------------------------------------------------------------------

    [Fact]
    public void Friend_means_the_casters_own_side_not_the_party()
    {
        // A monster casting a friends-only spell reaches other monsters.
        Assert.True(SpellTargets.CanTarget(selectingUnits: true, casterIsFriendly: false,
                                           targetIsFriendly: false,
                                           canTargetFriend: true, canTargetEnemy: false));

        Assert.False(SpellTargets.CanTarget(selectingUnits: true, casterIsFriendly: false,
                                            targetIsFriendly: true,
                                            canTargetFriend: true, canTargetEnemy: false));
    }

    [Fact]
    public void An_enemy_only_spell_refuses_the_casters_own_side()
    {
        Assert.False(SpellTargets.CanTarget(true, casterIsFriendly: true, targetIsFriendly: true,
                                            canTargetFriend: false, canTargetEnemy: true));

        Assert.True(SpellTargets.CanTarget(true, casterIsFriendly: true, targetIsFriendly: false,
                                           canTargetFriend: false, canTargetEnemy: true));
    }

    [Fact]
    public void A_cast_picking_a_square_refuses_every_combatant()
    {
        // SelectingUnits is the first test in the reference, before either side check.
        Assert.False(SpellTargets.CanTarget(selectingUnits: false, casterIsFriendly: true,
                                            targetIsFriendly: false,
                                            canTargetFriend: true, canTargetEnemy: true));
    }
}
