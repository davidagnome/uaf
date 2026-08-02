using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the start-of-round passes and bandaging
/// (<c>CheckDyingCombatants</c>, <c>CheckMorale</c>, <c>COMBAT_DATA::Bandage</c>).
/// </summary>
public class CombatUpkeepTests
{
    private static Combatant Wounded(int index, int hitPoints, CharacterStatus status) =>
        new(index, isFriendly: true, new CombatantIcon(1, 1), $"c{index}")
        {
            X = index,
            Y = 0,
            HitPoints = hitPoints,
            MaxHitPoints = 12,
            Status = status,
        };

    [Fact]
    public void A_dying_combatant_loses_a_point_a_round()
    {
        var c = Wounded(0, -3, CharacterStatus.Dying);

        CombatUpkeep.CheckDyingCombatants([c]);

        Assert.Equal(-4, c.HitPoints);
        Assert.Equal(CharacterStatus.Dying, c.Status);
    }

    [Fact]
    public void Nine_rounds_of_bleeding_kills()
    {
        // This is what gives the -1..-9 band its meaning: without the pass a combatant knocked
        // below zero would stay there forever.
        var c = Wounded(0, -1, CharacterStatus.Dying);

        for (int round = 0; round < 8; round++)
        {
            Assert.Empty(CombatUpkeep.CheckDyingCombatants([c]));
        }

        Assert.Equal(-9, c.HitPoints);
        Assert.Equal(CharacterStatus.Dying, c.Status);

        var died = CombatUpkeep.CheckDyingCombatants([c]);

        Assert.Equal(-10, c.HitPoints);
        Assert.Equal(CharacterStatus.Dead, c.Status);
        Assert.Equal([c], died);
    }

    [Fact]
    public void Only_dying_combatants_bleed()
    {
        var okay = Wounded(0, 8, CharacterStatus.Okay);
        var unconscious = Wounded(1, 0, CharacterStatus.Unconscious);
        var dead = Wounded(2, -10, CharacterStatus.Dead);
        var fled = Wounded(3, 5, CharacterStatus.Fled);

        CombatUpkeep.CheckDyingCombatants([okay, unconscious, dead, fled]);

        Assert.Equal(8, okay.HitPoints);
        Assert.Equal(0, unconscious.HitPoints);
        Assert.Equal(-10, dead.HitPoints);
        Assert.Equal(5, fled.HitPoints);
    }

    [Fact]
    public void A_bandaged_combatant_is_out_of_the_loop_permanently()
    {
        var c = Wounded(0, -3, CharacterStatus.Dying);
        c.IsBandaged = true;

        for (int round = 0; round < 20; round++)
        {
            CombatUpkeep.CheckDyingCombatants([c]);
        }

        Assert.Equal(-3, c.HitPoints);
        Assert.Equal(CharacterStatus.Dying, c.Status);
    }

    // ---- bandaging -------------------------------------------------------------------------

    [Fact]
    public void Bandaging_stabilises_rather_than_heals()
    {
        // Zero hit points and unconscious: out of the dying loop, but not back on its feet.
        var c = Wounded(0, -6, CharacterStatus.Dying);

        var bandaged = CombatUpkeep.Bandage([c]);

        Assert.Same(c, bandaged);
        Assert.True(c.IsBandaged);
        Assert.Equal(0, c.HitPoints);
        Assert.Equal(CharacterStatus.Unconscious, c.Status);
    }

    [Fact]
    public void The_worst_hurt_dying_combatant_is_chosen()
    {
        var healthy = Wounded(0, 10, CharacterStatus.Okay);
        var hurt = Wounded(1, -2, CharacterStatus.Dying);
        var worse = Wounded(2, -8, CharacterStatus.Dying);

        var bandaged = CombatUpkeep.Bandage([healthy, hurt, worse]);

        Assert.Same(worse, bandaged);
        Assert.False(hurt.IsBandaged);
        Assert.Equal(CharacterStatus.Okay, healthy.Status);
    }

    [Fact]
    public void Exactly_one_combatant_is_bandaged_per_action()
    {
        var a = Wounded(0, -4, CharacterStatus.Dying);
        var b = Wounded(1, -4, CharacterStatus.Dying);

        CombatUpkeep.Bandage([a, b]);

        Assert.Equal(1, new[] { a, b }.Count(c => c.IsBandaged));
    }

    [Fact]
    public void Ties_go_to_the_later_combatant()
    {
        // The reference compares with <=, so the last one found at the lowest hit points wins.
        var first = Wounded(0, -5, CharacterStatus.Dying);
        var second = Wounded(1, -5, CharacterStatus.Dying);

        Assert.Same(second, CombatUpkeep.Bandage([first, second]));
    }

    [Fact]
    public void With_nobody_dying_bandaging_does_nothing()
    {
        var okay = Wounded(0, 10, CharacterStatus.Okay);
        var unconscious = Wounded(1, 0, CharacterStatus.Unconscious);

        Assert.Null(CombatUpkeep.Bandage([okay, unconscious]));
        Assert.False(okay.IsBandaged);
        Assert.False(unconscious.IsBandaged);
    }

    [Fact]
    public void Bandaging_takes_a_combatant_out_of_the_bleeding_loop()
    {
        // The two halves together: bleed, bandage, then bleeding stops.
        var c = Wounded(0, -2, CharacterStatus.Dying);

        CombatUpkeep.CheckDyingCombatants([c]);
        Assert.Equal(-3, c.HitPoints);

        CombatUpkeep.Bandage([c]);
        CombatUpkeep.CheckDyingCombatants([c]);

        Assert.Equal(0, c.HitPoints);
        Assert.Equal(CharacterStatus.Unconscious, c.Status);
    }

    // ---- morale ----------------------------------------------------------------------------

    [Fact]
    public void The_morale_pass_does_nothing_which_is_the_point()
    {
        // Morale was switched off deliberately -- the reference hard-codes Flee = FALSE with the
        // roll commented out, above a quoted email from the designer asking for exactly that.
        // Nothing downstream is reachable, so nothing is ported.
        var outnumbered = new List<Combatant>
        {
            Wounded(0, 1, CharacterStatus.Okay),
            new(1, isFriendly: false, new CombatantIcon(1, 1), "m1"),
            new(2, isFriendly: false, new CombatantIcon(1, 1), "m2"),
            new(3, isFriendly: false, new CombatantIcon(1, 1), "m3"),
            new(4, isFriendly: false, new CombatantIcon(1, 1), "m4"),
        };

        CombatUpkeep.CheckMorale(outnumbered);

        Assert.All(outnumbered, c => Assert.False(c.IsFleeing));
        Assert.All(outnumbered, c => Assert.NotEqual(CharacterStatus.Running, c.Status));
    }
}
