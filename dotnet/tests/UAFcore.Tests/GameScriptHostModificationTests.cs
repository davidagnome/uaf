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


    /// <summary>
    /// The attribute form falls back to the character's own attributes.
    /// </summary>
    /// <remarks>
    /// <b>This is the behaviour the name hides.</b> The reference searches the effects first and,
    /// finding nothing, returns whether the <i>character's</i> ASL holds the name — so an innate
    /// attribute answers true with no spell involved at all.
    /// </remarks>
    [Fact]
    public void An_innate_attribute_counts_as_affected()
    {
        var game = Load();

        if (game is null || game.Party.Members.Count == 0)
        {
            return;
        }

        var host = new GameScriptHost(game);
        var who = game.Party.Members[Math.Max(game.Party.ActiveCharacter, 0)];

        Assert.False(host.IsAffectedBySpellAttribute(who.Name, "BLESSED"));

        who.Attributes.Insert("BLESSED", "yes");

        // No spell was cast, and yet.
        Assert.True(host.IsAffectedBySpellAttribute(who.Name, "BLESSED"));
    }

    /// <summary>A spell the design does not have is refused before anything is searched.</summary>
    [Fact]
    public void An_unknown_spell_is_refused()
    {
        var game = Load();

        if (game is null || game.Party.Members.Count == 0)
        {
            return;
        }

        var host = new GameScriptHost(game);
        string actor = game.Party.Members[Math.Max(game.Party.ActiveCharacter, 0)].Name;

        Assert.False(host.IsAffectedBySpell(actor, "No Such Spell"));
        Assert.False(host.IsAffectedBySpell(actor, string.Empty));
    }


    /// <summary>
    /// A character with no special abilities runs nothing, rather than failing.
    /// </summary>
    /// <remarks>
    /// The common case: most characters carry no abilities at all, and the family has to be quiet
    /// about it rather than treating an empty set as an error.
    /// </remarks>
    [Fact]
    public void A_character_without_abilities_runs_nothing()
    {
        var game = Load();

        if (game is null || game.Party.Members.Count == 0)
        {
            return;
        }

        var host = new GameScriptHost(game);
        string actor = game.Party.Members[Math.Max(game.Party.ActiveCharacter, 0)].Name;

        Assert.Equal(string.Empty, host.RunCharacterScripts(actor, "Ability"));
        Assert.Equal(string.Empty, host.RunSpellEffectScripts(actor, "Ability"));
    }

    /// <summary>An ability the design does not have runs nothing.</summary>
    [Fact]
    public void An_unknown_ability_runs_nothing()
    {
        var game = Load();

        if (game is null)
        {
            return;
        }

        var host = new GameScriptHost(game);

        Assert.Equal(string.Empty, host.CallGlobalScript("no such ability", "Ability"));
    }

    /// <summary>
    /// A real ability from the design compiles and runs through the whole chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the end-to-end check the five sub-opcodes were blocked on</b>: design database →
    /// ability lookup → wrapper → compile → execute.
    /// </para>
    /// <para>
    /// <b>What it asserts is that the script COMPILES and starts</b>, not that it finishes. Real
    /// design scripts reach sub-opcodes this port has not implemented — the first one tried hits
    /// <c>$CharacterContext</c> — and that throw is proof the chain is connected rather than a
    /// failure of it. A compile error would be the real failure, and is what this rules out.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_designs_own_script_compiles_and_runs()
    {
        var game = Load();

        if (game is null)
        {
            return;
        }

        // Any ability the design ships that carries a script.
        var withScript = game.Design.SpecialAbilities
            .Select(a => (a.Name, Entry: a.Entries.FirstOrDefault(
                e => e.Kind == UAF.Data.SpecialAbilityEntryKind.Script)))
            .FirstOrDefault(x => x.Entry is not null);

        if (withScript.Entry is null)
        {
            return;
        }

        var host = new GameScriptHost(game);

        try
        {
            host.CallGlobalScript(withScript.Name, withScript.Entry.Name);
        }
        catch (NotSupportedException)
        {
            // Reached execution and hit a sub-opcode this port has not implemented. That is the
            // chain working -- compilation and dispatch both happened.
        }

        // The failure that would matter: the script never compiled. SpecialAbilityScripts logs
        // that through onError, and nothing else writes this line.
        Assert.DoesNotContain(host.DebugLog, line => line.Contains("Script Error",
                                                                  StringComparison.Ordinal));
    }
}
