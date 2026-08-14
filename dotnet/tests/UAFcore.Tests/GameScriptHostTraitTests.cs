using UAF.Media.Sdl;
using UAF.Scripting;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The sixteen creature traits, answered from a live monster rather than from a default.
/// </summary>
/// <remarks>
/// <b>They are the one stat family that does not read off a <see cref="Character"/>.</b> The flags
/// live on the monster record and reach a fight through <see cref="Combatant"/>, so
/// <c>GameScriptHost.Resolve</c> — which only ever finds party members — cannot answer them.
/// </remarks>
public class GameScriptHostTraitTests
{
    private static Game? Fight(out CombatEvent? encounter)
    {
        encounter = null;

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

        encounter = design.Level(1)?.Events.OfType<CombatEvent>().FirstOrDefault();

        if (encounter is null)
        {
            design.Dispose();
            return null;
        }

        return new Game(design, levelIndex: 1) { Dice = _ => 20 };
    }

    /// <summary>A monster in a fight answers from its own flags.</summary>
    /// <remarks>
    /// The design's monsters are ordinary ones, so most flags are clear — what this pins is that
    /// the answer comes from the record at all. <see cref="Combatant.FormType"/> and its three
    /// siblings were dropped on the floor by <c>EncounterBuilder</c> until now, so every monster
    /// answered the non-monster literals.
    /// </remarks>
    [Fact]
    public void A_monster_answers_from_its_own_flags()
    {
        var game = Fight(out var encounter);

        if (game is null)
        {
            return;
        }

        game.StartEvent(encounter!);

        if (game.Combat is not { } session)
        {
            return;
        }

        var host = new GameScriptHost(game);
        int monsters = 0;

        for (int i = 0; i < session.Combatants.Count; i++)
        {
            var who = session.Combatants[i];

            if (who.Kind != CombatantKind.Monster)
            {
                continue;
            }

            monsters++;
            string actor = i.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Measured: SomethingWild's monster carries FormType 3 (Mammal|Animal) and
            // MiscOptionsType 1 (CanBeHeldCharmed). Pinned rather than compared against the
            // combatant's own field -- a self-comparison would pass just as well if the host
            // always returned "0", which is what it did before the flags were carried across.
            Assert.Equal(3u, who.FormType);
            Assert.Equal(1u, who.MiscOptionsType);

            Assert.Equal("1", host.GetCharStat(actor, GpdlCharStat.IsMammal));
            Assert.Equal("1", host.GetCharStat(actor, GpdlCharStat.IsAnimal));
            Assert.Equal("1", host.GetCharStat(actor, GpdlCharStat.CanBeHeldOrCharmed));

            // And the ones its record does not set stay clear.
            Assert.Equal("0", host.GetCharStat(actor, GpdlCharStat.IsSnake));
            Assert.Equal("0", host.GetCharStat(actor, GpdlCharStat.HasPoisonImmunity));
            Assert.Equal("0", host.GetCharStat(actor, GpdlCharStat.HasDeathImmunity));
            Assert.Equal("0", host.GetCharStat(actor, GpdlCharStat.AffectedByDispelEvil));
        }

        Assert.True(monsters > 0, "the fight fielded no monsters");
    }

    /// <summary>
    /// A party member still answers the reference's literals, including the two true ones.
    /// </summary>
    /// <remarks>
    /// This is the case that matters most: a character is a mammal and can be held or charmed, and
    /// answering false would make those spells fail against the whole party.
    /// </remarks>
    [Fact]
    public void A_party_member_answers_the_non_monster_literals()
    {
        var game = Fight(out _);

        if (game is null || game.Party.Members.Count == 0)
        {
            return;
        }

        var host = new GameScriptHost(game);
        string actor = game.Party.Members[0].Name;

        Assert.Equal("1", host.GetCharStat(actor, GpdlCharStat.IsMammal));
        Assert.Equal("1", host.GetCharStat(actor, GpdlCharStat.CanBeHeldOrCharmed));
        Assert.Equal("0", host.GetCharStat(actor, GpdlCharStat.IsSnake));
        Assert.Equal("0", host.GetCharStat(actor, GpdlCharStat.HasDeathImmunity));
    }

    /// <summary>An actor nobody recognises gets the literals too, not the empty string.</summary>
    /// <remarks>
    /// Outside combat there is no combatant to resolve, so every trait falls to the literal — the
    /// same path a party member takes.
    /// </remarks>
    [Fact]
    public void An_unknown_actor_gets_the_literals()
    {
        var game = Fight(out _);

        if (game is null)
        {
            return;
        }

        var host = new GameScriptHost(game);

        Assert.Equal("1", host.GetCharStat("no such actor", GpdlCharStat.IsMammal));
        Assert.Equal("0", host.GetCharStat("no such actor", GpdlCharStat.IsGiant));
    }
}
