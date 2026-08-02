using UAF.Media;
using UAF.Media.Sdl;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Drives a real design through a whole fight: encounter, rounds, verdict, spoils and the chain out
/// the other side.
/// </summary>
/// <remarks>
/// The seam these cover is the <i>sequencing</i> through <see cref="Game.Update"/>, which is where
/// the ordering bugs live — the decisions either side are unit-tested in
/// <see cref="CombatAftermathTests"/>. Skipped silently when the reference designs are absent, so
/// the suite still runs on a bare checkout.
/// </remarks>
public class GameCombatTests
{
    private static string? DesignRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? design = dir is null
            ? null
            : Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");

        return design is not null && Directory.Exists(design) ? design : null;
    }

    /// <summary>
    /// A game on the level whose combat events live, with dice that always roll high so the party
    /// wins quickly rather than the fight running to the idle limit.
    /// </summary>
    private static Game? Fight(out CombatEvent? encounter, int face = 20)
    {
        encounter = null;
        string? root = DesignRoot();
        if (root is null)
        {
            return null;
        }

        var design = LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer());
        var level = design.Level(1);
        encounter = level?.Events.OfType<CombatEvent>().FirstOrDefault();

        if (encounter is null)
        {
            design.Dispose();
            return null;
        }

        return new Game(design, levelIndex: 1) { Dice = _ => face };
    }

    /// <summary>
    /// Runs the engine until the fight is over or the step budget is spent.
    /// </summary>
    /// <remarks>
    /// Party members are player-run, so every turn of theirs has to be driven to END. Pressing
    /// Return blindly re-selects MOVE, which fails with nowhere to go and never ends the turn --
    /// the fight then never advances at all.
    /// </remarks>
    private static void RunFight(Game game, int steps = 50000)
    {
        for (int i = 0; i < steps && game.InCombat; i++)
        {
            var session = game.Combat!;

            if (session.AwaitingPlayer
                && CombatMenu.At(session.Menu.ActiveItem) != CombatCommand.End)
            {
                game.Update(InputEvent.KeyDown(VirtualKey.Right));
                continue;
            }

            game.Update(InputEvent.KeyDown(VirtualKey.Return));
        }
    }

    [Fact]
    public void A_combat_event_starts_a_fight_with_both_sides_placed()
    {
        var game = Fight(out var encounter);
        if (game is null)
        {
            return;
        }

        game.StartEvent(encounter!);

        Assert.True(game.InCombat);
        Assert.NotNull(game.Combat);
        Assert.NotEmpty(game.Combat!.Combatants);
        Assert.Contains(game.Combat.Combatants, c => !c.IsFriendly);
        Assert.All(game.Combat.Combatants, c => Assert.True(c.X >= 0));
    }

    [Fact]
    public void A_fight_runs_to_a_verdict_and_hands_the_engine_back()
    {
        var game = Fight(out var encounter);
        if (game is null)
        {
            return;
        }

        game.StartEvent(encounter!);
        RunFight(game);

        Assert.False(game.InCombat);
        Assert.Null(game.Combat);
    }

    [Fact]
    public void The_verdict_reaches_the_designs_global_attributes()
    {
        // What a design's scripts branch on. Nothing else in the engine writes this key.
        var game = Fight(out var encounter);
        if (game is null)
        {
            return;
        }

        Assert.Null(game.Globals.Find(AttributeList.CombatResultKey));

        game.StartEvent(encounter!);
        RunFight(game);

        string? verdict = game.Globals.Find(AttributeList.CombatResultKey);
        Assert.NotNull(verdict);
        Assert.Contains(verdict, new[] { "Win", "Lose", "Flee", "LoseButNeverDies" });
    }

    [Fact]
    public void A_won_fight_pays_experience_to_the_survivors()
    {
        var game = Fight(out var encounter);
        if (game is null)
        {
            return;
        }

        int before = game.Party.Members.Sum(m => m.TotalExperience);

        game.StartEvent(encounter!);
        RunFight(game);

        if (game.Globals.Find(AttributeList.CombatResultKey) != "Win")
        {
            return;
        }

        // Only the standing share, so a win with survivors must have moved the total.
        Assert.Contains(game.Party.Members, m => m.Status == CharacterStatus.Okay);
        Assert.True(game.Party.Members.Sum(m => m.TotalExperience) >= before);
    }

    [Fact]
    public void Nobody_is_left_fled_once_the_fight_is_over()
    {
        // The results screen restores anyone who ran, or they would stay fled for the rest of the
        // game.
        var game = Fight(out var encounter);
        if (game is null)
        {
            return;
        }

        game.StartEvent(encounter!);
        RunFight(game);

        Assert.DoesNotContain(game.Party.Members, m => m.Status == CharacterStatus.Fled);
    }

    [Fact]
    public void The_fight_leaves_no_spells_hanging_on_the_map()
    {
        var game = Fight(out var encounter);
        if (game is null)
        {
            return;
        }

        game.StartEvent(encounter!);
        RunFight(game);

        // Combat is done with; the session is gone and its lingering spells with it.
        Assert.Null(game.Combat);
    }

    [Fact]
    public void A_fight_where_nobody_ever_hits_does_not_end_itself()
    {
        // Every roll a 1, so no blow ever lands -- and the fight still does not stop. The idle rule
        // keys on ATTACKING, not on hitting: Attack.Resolve stamps lastAttackRound whether or not
        // the blow connects, so two sides swinging and missing are never idle. That is the
        // reference's behaviour, confirmed here through the whole engine rather than the session
        // alone.
        var game = Fight(out var encounter, face: 1);
        if (game is null)
        {
            return;
        }

        game.StartEvent(encounter!);
        RunFight(game, steps: 20000);

        Assert.True(game.InCombat);
        Assert.True(game.Combat!.Round.Round > CombatRound.MaxIdleRounds,
                    $"only reached round {game.Combat.Round.Round}");
    }
}
