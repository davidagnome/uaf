using UAF.Media.Sdl;
using UAF.Rules;
using UAF.Scripting;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Timed attribute changes on a live character.
/// </summary>
/// <remarks>
/// <c>$MODIFY_CHAR_ATTRIBUTE</c> adds a real <see cref="ActiveSpellEffect"/> rather than a note:
/// it has to be cumulative, script-made and <b>timed</b>, because the timed flag is what
/// <c>$REMOVE_CHAR_MODIFICATION</c> filters on.
/// </remarks>
public class GameScriptHostModificationTests
{
    private static Game? Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? root = dir is null
            ? null
            : Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");

        if (root is null || !Directory.Exists(root))
        {
            return null;
        }

        var design = LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer());
        return new Game(design, levelIndex: 1) { Dice = _ => 20 };
    }

    /// <summary>The effect lands on the character with the flags that make it findable.</summary>
    [Fact]
    public void A_modification_becomes_a_timed_script_effect()
    {
        var game = Load();

        if (game is null || game.Party.Members.Count == 0)
        {
            return;
        }

        var host = new GameScriptHost(game);
        var who = game.Party.Members[Math.Max(game.Party.ActiveCharacter, 0)];
        int before = who.Effects.Count;

        game.SetMinutes(100);
        host.ModifyCharacterAttribute("STR", 2, 30, "Bulls Strength", "potion");

        Assert.Equal(before + 1, who.Effects.Count);

        var added = who.Effects.Effects[^1];

        Assert.Equal("STR", added.Attribute);
        Assert.Equal(2, added.Effect.Change);
        Assert.Equal("potion", added.SourceSpell);
        Assert.True(added.FromScript);

        // The clock plus the duration, in minutes.
        Assert.Equal(130, added.StopTime);

        // All three flags: the timed one is what makes it removable again.
        Assert.True((added.Effect.Flags & SpellEffectFlags.TimedSpecialAbility) != 0);
        Assert.True((added.Effect.Flags & SpellEffectFlags.Script) != 0);
        Assert.True((added.Effect.Flags & SpellEffectFlags.Cumulative) != 0);
    }

    /// <summary>And it actually changes the attribute it names.</summary>
    /// <remarks>
    /// The flags and the stop time could all be right while the effect did nothing; this is what
    /// separates "recorded" from "applied".
    /// </remarks>
    [Fact]
    public void A_modification_changes_the_attribute()
    {
        var game = Load();

        if (game is null || game.Party.Members.Count == 0)
        {
            return;
        }

        var host = new GameScriptHost(game);
        var who = game.Party.Members[Math.Max(game.Party.ActiveCharacter, 0)];

        double before = who.Effects.Apply(10, "STR");
        host.ModifyCharacterAttribute("STR", 3, 30, "text", "potion");

        Assert.Equal(before + 3, who.Effects.Apply(10, "STR"));
    }

    /// <summary>Removing takes one match, and leaves the rest.</summary>
    [Fact]
    public void Removing_takes_one_and_leaves_the_others()
    {
        var game = Load();

        if (game is null || game.Party.Members.Count == 0)
        {
            return;
        }

        var host = new GameScriptHost(game);
        var who = game.Party.Members[Math.Max(game.Party.ActiveCharacter, 0)];
        int before = who.Effects.Count;

        host.ModifyCharacterAttribute("STR", 1, 30, "a", "potion");
        host.ModifyCharacterAttribute("DEX", 1, 30, "b", "potion");

        Assert.True(host.RemoveCharacterModification("potion"));
        Assert.Equal(before + 1, who.Effects.Count);

        Assert.True(host.RemoveCharacterModification("potion"));
        Assert.Equal(before, who.Effects.Count);

        // Nothing left that matches.
        Assert.False(host.RemoveCharacterModification("potion"));
    }

    /// <summary>
    /// A mask that does not match leaves everything alone.
    /// </summary>
    /// <remarks>
    /// <c>MatchMask</c> is a word matcher, so a prefix of a word is not a match — this is the
    /// case that would silently remove the wrong effect if it were treated as a glob.
    /// </remarks>
    [Fact]
    public void A_non_matching_mask_removes_nothing()
    {
        var game = Load();

        if (game is null || game.Party.Members.Count == 0)
        {
            return;
        }

        var host = new GameScriptHost(game);
        var who = game.Party.Members[Math.Max(game.Party.ActiveCharacter, 0)];

        host.ModifyCharacterAttribute("STR", 1, 30, "a", "potion");
        int after = who.Effects.Count;

        Assert.False(host.RemoveCharacterModification("pot"));
        Assert.False(host.RemoveCharacterModification("elixir"));
        Assert.Equal(after, who.Effects.Count);

        // But a whole-word wildcard does match it.
        Assert.True(host.RemoveCharacterModification("*"));
    }
}
