using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the combatant a script's selector picks — quirks included.</summary>
public class CombatSelectorsTests
{
    private static Combatant Fighter(int index, bool friendly, int x, int y, int hitPoints)
    {
        var who = new Combatant(index, friendly, new CombatantIcon(1, 1), $"c{index}")
        {
            X = x,
            Y = y,
            HitPoints = hitPoints,
        };

        return who;
    }

    /// <summary>Two friends at 0,0 and 5,0; two monsters at 2,0 and 9,0.</summary>
    private static List<Combatant> Field() =>
    [
        Fighter(0, friendly: true, 0, 0, hitPoints: 30),
        Fighter(1, friendly: true, 5, 0, hitPoints: 12),
        Fighter(2, friendly: false, 2, 0, hitPoints: 7),
        Fighter(3, friendly: false, 9, 0, hitPoints: 40),
    ];

    // ---- nearest ---------------------------------------------------------------------------------

    [Fact]
    public void Nearest_always_answers_the_combatant_it_was_asked_about()
    {
        // The loop has no `i != self` guard and the distance from anyone to themselves is zero,
        // which nothing can beat because the comparison is strictly <. The function is useless as
        // written, and a design's script was written against what it does.
        var field = Field();

        Assert.All(field, who => Assert.Same(who, CombatSelectors.Nearest(field, who)));
    }

    [Fact]
    public void Nearest_enemy_answers_the_asker_when_the_asker_is_an_enemy()
    {
        // Same self-inclusion, narrowed to the unfriendly side.
        var field = Field();

        Assert.Same(field[2], CombatSelectors.NearestEnemy(field, field[2]));
    }

    [Fact]
    public void Enemy_means_not_friendly_rather_than_the_other_side_from_you()
    {
        // The filter is !GetIsFriendly() with no reference to the asker, so a monster asking for
        // its nearest enemy is handed a monster. Here the far one, since the near one is itself.
        var field = Field();

        // The monster at 9,0 asking: the candidates are the two monsters, and it is its own
        // nearest -- so even "the other monster" never wins.
        Assert.Same(field[3], CombatSelectors.NearestEnemy(field, field[3]));

        // A party member gets the answer the name promises.
        Assert.Same(field[2], CombatSelectors.NearestEnemy(field, field[0]));
    }

    [Fact]
    public void A_party_member_gets_the_closer_of_the_two_monsters()
    {
        var field = Field();

        // From 5,0 the monsters are at 2,0 and 9,0 -- three away and four away.
        Assert.Same(field[2], CombatSelectors.NearestEnemy(field, field[1]));
    }

    [Fact]
    public void Nearest_enemy_answers_nobody_when_there_are_none()
    {
        var friends = Field().Where(c => c.IsFriendly).ToList();

        Assert.Null(CombatSelectors.NearestEnemy(friends, friends[0]));
    }

    // ---- by hit points ---------------------------------------------------------------------------

    [Fact]
    public void Most_damaged_means_lowest_hit_points_not_most_damage_taken()
    {
        // A goblin at full health with four hit points is "more damaged" than a fighter on 60 of
        // 100: the comparison never looks at the maximum.
        var field = new List<Combatant>
        {
            Fighter(0, friendly: true, 0, 0, hitPoints: 60),
            Fighter(1, friendly: true, 1, 0, hitPoints: 4),
        };

        field[0].MaxHitPoints = 100;
        field[1].MaxHitPoints = 4;

        Assert.Same(field[1], CombatSelectors.ByHitPoints(field, friendly: true, lowest: true));
    }

    [Fact]
    public void Each_side_is_searched_separately()
    {
        var field = Field();

        Assert.Same(field[1], CombatSelectors.ByHitPoints(field, friendly: true, lowest: true));
        Assert.Same(field[2], CombatSelectors.ByHitPoints(field, friendly: false, lowest: true));
        Assert.Same(field[0], CombatSelectors.ByHitPoints(field, friendly: true, lowest: false));
        Assert.Same(field[3], CombatSelectors.ByHitPoints(field, friendly: false, lowest: false));
    }

    [Fact]
    public void The_first_of_a_tie_wins_in_both_directions()
    {
        // The comparisons are strict, so a later combatant with the same hit points never
        // displaces an earlier one.
        var field = new List<Combatant>
        {
            Fighter(0, friendly: true, 0, 0, hitPoints: 10),
            Fighter(1, friendly: true, 1, 0, hitPoints: 10),
        };

        Assert.Same(field[0], CombatSelectors.ByHitPoints(field, friendly: true, lowest: true));
        Assert.Same(field[0], CombatSelectors.ByHitPoints(field, friendly: true, lowest: false));
    }

    [Fact]
    public void An_empty_side_answers_nobody()
    {
        var friends = Field().Where(c => c.IsFriendly).ToList();

        Assert.Null(CombatSelectors.ByHitPoints(friends, friendly: false, lowest: true));
    }

    [Fact]
    public void A_combatant_on_no_hit_points_is_still_a_candidate()
    {
        // Nothing filters on being alive: the selector is a plain minimum over the side.
        var field = new List<Combatant>
        {
            Fighter(0, friendly: true, 0, 0, hitPoints: 20),
            Fighter(1, friendly: true, 1, 0, hitPoints: -6),
        };

        Assert.Same(field[1], CombatSelectors.ByHitPoints(field, friendly: true, lowest: true));
    }
}
