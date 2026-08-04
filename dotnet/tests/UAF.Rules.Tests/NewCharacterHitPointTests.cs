using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>
/// Covers a new character's hit points, and the seeded generator behind them.
/// </summary>
/// <remarks>
/// Two things in this function disagree with the rest of the engine, and both are transcribed
/// rather than corrected — a design's hit points are balanced against what the engine does, not
/// against what it meant.
/// </remarks>
public class NewCharacterHitPointTests
{
    /// <summary>A baseclass whose every level rolls the same dice.</summary>
    private static (int, int, Func<int, LevelHitDice>) Baseclass(
        int level, int sides = 8, int count = 1, int constant = 0, int bonus = 0) =>
        (level, bonus, _ => new LevelHitDice(sides, count, constant));

    // ---- the seeded generator ------------------------------------------------------------------

    [Fact]
    public void The_same_seed_gives_the_same_dice()
    {
        // The whole reason it exists: re-rolling ability scores must not re-roll the dice.
        int first = NewCharacterHitPoints.Roll([Baseclass(level: 5)], seed: 12345);
        int again = NewCharacterHitPoints.Roll([Baseclass(level: 5)], seed: 12345);

        Assert.Equal(first, again);
    }

    [Fact]
    public void A_different_seed_gives_different_dice()
    {
        int a = NewCharacterHitPoints.Roll([Baseclass(level: 8)], seed: 1);
        int b = NewCharacterHitPoints.Roll([Baseclass(level: 8)], seed: 2);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Only_the_ability_bonus_moves_when_the_seed_is_held()
    {
        // Change constitution and the same dice come up with a different bonus, which is exactly
        // what the comment above the function promises.
        int plain = NewCharacterHitPoints.Roll([Baseclass(level: 4, bonus: 0)], seed: 999);
        int bonused = NewCharacterHitPoints.Roll([Baseclass(level: 4, bonus: 2)], seed: 999);

        Assert.Equal(plain + (4 * 2), bonused);
    }

    [Fact]
    public void A_die_with_no_sides_rolls_nothing_but_keeps_the_bonus()
    {
        var random = new LittleRandom(7);

        Assert.Equal(3, random.Roll(sides: 0, count: 5, bonus: 3));
    }

    [Fact]
    public void A_zero_seed_still_generates()
    {
        // Init guards against a zero low word, so seed 0 is a valid seed and not a dead one.
        Assert.True(NewCharacterHitPoints.Roll([Baseclass(level: 3)], seed: 0) > 0);
    }

    // ---- the two quirks ------------------------------------------------------------------------

    [Fact]
    public void A_baseclass_with_dice_never_gets_its_per_level_constant()
    {
        // HP += (numDice>0) ? ran.Roll(...) : 0 + constant;  -- ?: binds looser than +, so the
        // constant is the else branch. The training path writes it correctly and gets it.
        int without = NewCharacterHitPoints.Roll(
            [Baseclass(level: 3, count: 1, constant: 0)], seed: 42);

        int with = NewCharacterHitPoints.Roll(
            [Baseclass(level: 3, count: 1, constant: 100)], seed: 42);

        Assert.Equal(without, with);
    }

    [Fact]
    public void A_baseclass_with_no_dice_gets_only_its_constant()
    {
        // The other half of the same line: with numDice at zero the else branch runs and the
        // constant is all there is.
        int total = NewCharacterHitPoints.Roll(
            [Baseclass(level: 3, count: 0, constant: 5)], seed: 42);

        Assert.Equal(15, total);
    }

    [Fact]
    public void Multiple_baseclasses_are_summed_not_averaged()
    {
        // Twenty lines of the function explain that they should be averaged, and numBaseclass is
        // counted for it -- then the result is max(1, totalHP) and the count is never read.
        int one = NewCharacterHitPoints.Roll([Baseclass(level: 2)], seed: 5);

        int two = NewCharacterHitPoints.Roll(
            [Baseclass(level: 2), Baseclass(level: 2)], seed: 5);

        Assert.True(two > one, $"two baseclasses gave {two}, one gave {one}");
    }

    // ---- the floor -----------------------------------------------------------------------------

    [Fact]
    public void A_character_always_has_at_least_one_hit_point()
    {
        int total = NewCharacterHitPoints.Roll(
            [Baseclass(level: 1, sides: 0, count: 0, constant: -50)], seed: 3);

        Assert.Equal(1, total);
    }

    [Fact]
    public void A_character_with_no_baseclasses_still_has_one()
    {
        Assert.Equal(1, NewCharacterHitPoints.Roll([], seed: 3));
    }

    [Fact]
    public void Every_level_from_one_is_rolled_not_just_the_last()
    {
        // The creation path rolls the whole ladder; the training path rolls only what was gained.
        int first = NewCharacterHitPoints.Roll([Baseclass(level: 1)], seed: 11);
        int tenth = NewCharacterHitPoints.Roll([Baseclass(level: 10)], seed: 11);

        Assert.True(tenth > first * 5, $"level 10 gave {tenth}, level 1 gave {first}");
    }
}
