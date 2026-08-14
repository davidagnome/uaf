using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The three <c>$CHAR_*</c> calls: remove-all-spells, dispel-magic and remove-item-curse.
/// </summary>
/// <remarks>
/// The first two look alike and are not: a dispel takes only what its spell allows to be
/// dispelled, and reaches item special abilities at level 12 (<c>Char.cpp:15425</c>).
/// </remarks>
public class GpdlCharSpellTests
{
    private sealed class SpellHost : GpdlUnhostedEnvironment
    {
        public List<(string Actor, int Level)> Removed { get; } = [];

        public List<(string Actor, int Level)> Dispelled { get; } = [];

        public List<string> Uncursed { get; } = [];

        public override int RemoveSpellEffects(string actor, int level)
        {
            Removed.Add((actor, level));
            return level * 10;
        }

        public override int DispelSpellEffects(string actor, int level)
        {
            Dispelled.Add((actor, level));
            return level;
        }

        public override bool RemoveItemCurses(string actor)
        {
            Uncursed.Add(actor);
            return actor != NullActor;
        }
    }

    private static string Run(string body, GpdlUnhostedEnvironment host)
    {
        var compiler = new GpdlCompiler();
        string source = "$PUBLIC $FUNC f() { " + body + " } f;";
        Assert.True(compiler.Compile(source) == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);
        string value = vm.Execute("f");
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        return value;
    }

    /// <summary>Remove-all reaches the host with the actor and the level, and returns a count.</summary>
    /// <remarks>
    /// <b>The arguments must not be crossed.</b> Both are on the stack and the rightmost pops
    /// first, so a level of 3 has to arrive as 3 rather than as the actor.
    /// </remarks>
    [Fact]
    public void Remove_all_spells_passes_the_actor_and_level()
    {
        var host = new SpellHost();
        string result = Run("$RETURN $CHAR_REMOVEALLSPELLS($MOST_DAMAGED_ENEMY(), 3);", host);

        var (actor, level) = Assert.Single(host.Removed);
        Assert.Equal(host.NullActor, actor);
        Assert.Equal(3, level);

        // The count comes back as a string, not a boolean.
        Assert.Equal("30", result);
    }

    /// <summary>Dispel is a different call, not the same one under another name.</summary>
    [Fact]
    public void Dispel_magic_is_its_own_call()
    {
        var host = new SpellHost();
        string result = Run("$RETURN $CHAR_DISPELMAGIC($MOST_DAMAGED_ENEMY(), 5);", host);

        Assert.Empty(host.Removed);
        var (_, level) = Assert.Single(host.Dispelled);
        Assert.Equal(5, level);
        Assert.Equal("5", result);
    }

    /// <summary>Remove-item-curse takes only an actor, and answers a boolean.</summary>
    /// <remarks>
    /// <b>True once the actor resolves</b>, whether or not anything was cursed — the reference
    /// pushes true after calling <c>ClearAllItemCursedFlags</c> unconditionally.
    /// </remarks>
    [Fact]
    public void Remove_item_curse_answers_a_boolean()
    {
        var host = new SpellHost();
        string result = Run("$RETURN $CHAR_REMOVEALLITEMCURSE($MOST_DAMAGED_ENEMY());", host);

        Assert.Single(host.Uncursed);

        // The unhosted environment's actor is the null one, which this host refuses -- and GPDL's
        // false is the EMPTY STRING (m_false), not "0". A count and a boolean look nothing alike.
        Assert.Equal(string.Empty, result);
    }

    /// <summary>An unimplemented host answers zero rather than throwing.</summary>
    /// <remarks>
    /// The base environment models a game that is not running; all three are side effects it
    /// cannot perform, so they report having done nothing.
    /// </remarks>
    [Fact]
    public void An_unhosted_environment_does_nothing()
    {
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal("0", Run("$RETURN $CHAR_REMOVEALLSPELLS($MOST_DAMAGED_ENEMY(), 9);", host));
        Assert.Equal("0", Run("$RETURN $CHAR_DISPELMAGIC($MOST_DAMAGED_ENEMY(), 9);", host));

        // A boolean, so empty rather than "0" -- see Remove_item_curse_answers_a_boolean.
        Assert.Equal(string.Empty,
                     Run("$RETURN $CHAR_REMOVEALLITEMCURSE($MOST_DAMAGED_ENEMY());", host));
    }
}
