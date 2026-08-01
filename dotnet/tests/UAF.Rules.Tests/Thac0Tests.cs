using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers <see cref="Thac0"/>.</summary>
public class Thac0Tests
{
    /// <summary>A fighter's table: 20 at level 1, improving by one every level.</summary>
    private static byte[] Fighter()
    {
        var table = new byte[Thac0.HighestLevel];
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = (byte)Math.Max(20 - i, 1);
        }
        return table;
    }

    /// <summary>A magic user's: 20 at level 1, improving every third level.</summary>
    private static byte[] MagicUser()
    {
        var table = new byte[Thac0.HighestLevel];
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = (byte)Math.Max(20 - (i / 3), 1);
        }
        return table;
    }

    private static BaseclassStanding At(int level, byte[] table) => new(level, 0, table);

    [Fact]
    public void A_character_with_no_baseclass_is_unskilled()
    {
        Assert.Equal(Thac0.Unskilled, Thac0.ForCharacter([]));
    }

    [Fact]
    public void The_table_is_indexed_by_level_minus_one()
    {
        Assert.Equal(20, Thac0.ForCharacter([At(1, Fighter())]));
        Assert.Equal(19, Thac0.ForCharacter([At(2, Fighter())]));
        Assert.Equal(11, Thac0.ForCharacter([At(10, Fighter())]));
    }

    [Fact]
    public void The_best_baseclass_wins_because_lower_is_better()
    {
        // A level-10 fighter/mage attacks as the fighter. The walk keeps the minimum, starting
        // from 20 -- so a class that is worse than unskilled cannot make things worse either.
        var character = new[] { At(10, Fighter()), At(10, MagicUser()) };

        Assert.Equal(11, Thac0.ForCharacter(character));
        Assert.Equal(17, Thac0.ForCharacter([At(10, MagicUser())]));
    }

    [Fact]
    public void A_level_above_the_tables_length_is_clamped_rather_than_overrunning()
    {
        var table = Fighter();
        Assert.Equal(table[Thac0.HighestLevel - 1], Thac0.ForCharacter([At(999, table)]));
    }

    [Fact]
    public void A_truncated_table_contributes_nothing_instead_of_throwing()
    {
        // Designs really do ship odd data, and a short table must not take the character down.
        Assert.Equal(Thac0.Unskilled, Thac0.ForCharacter([At(10, [20, 19])]));
    }

    [Fact]
    public void A_drained_baseclass_keeps_the_attack_number_it_had()
    {
        // currentLevel 0 with previousLevel 10: the level used is the previous one, so the
        // character does not drop to unskilled. Whether it counts at all is CanUse's question --
        // here a second, higher current baseclass releases it.
        var drained = new BaseclassStanding(0, 10, Fighter());
        var current = new BaseclassStanding(11, 0, MagicUser());

        Assert.Equal(11, Thac0.ForCharacter([drained, current]));
    }

    [Fact]
    public void A_previous_baseclass_counts_only_once_a_current_one_has_passed_it()
    {
        // The dual-class rule. A fighter 10 who turned mage attacks as the mage until the mage
        // level exceeds 10 -- at which point the old fighter table comes back.
        var oldFighter = new BaseclassStanding(0, 10, Fighter());

        var stillLearning = new[] { oldFighter, At(9, MagicUser()) };
        Assert.Equal(18, Thac0.ForCharacter(stillLearning));   // the mage's own number at 9

        var past = new[] { oldFighter, At(11, MagicUser()) };
        Assert.Equal(11, Thac0.ForCharacter(past));            // the fighter's, now usable
    }

    [Fact]
    public void Equalling_the_previous_level_is_not_enough()
    {
        // The comparison is strictly greater: "a current baseclass level is GREATER than this
        // previous baseclass level".
        var oldFighter = new BaseclassStanding(0, 10, Fighter());

        Assert.False(Thac0.CanUse(oldFighter, [oldFighter, At(10, MagicUser())]));
        Assert.True(Thac0.CanUse(oldFighter, [oldFighter, At(11, MagicUser())]));
    }

    [Fact]
    public void Only_an_undrained_baseclass_can_release_a_previous_one()
    {
        // The loop skips any baseclass that is itself drained, so two abandoned halves cannot
        // release each other.
        var first = new BaseclassStanding(0, 5, Fighter());
        var second = new BaseclassStanding(0, 20, MagicUser());

        Assert.False(Thac0.CanUse(first, [first, second]));
        Assert.Equal(Thac0.Unskilled, Thac0.ForCharacter([first, second]));
    }
}
