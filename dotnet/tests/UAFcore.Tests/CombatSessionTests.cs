using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers a whole fight driven through <see cref="CombatSession"/>.
/// </summary>
public class CombatSessionTests
{
    private static Map EmptyLevel()
    {
        var cells = new AreaMapCell[100];
        Array.Fill(cells, new AreaMapCell(0, false, false, 0, 0, 0, 0, 0, false,
                                          [0, 0, 0, 0], [0, 0, 0, 0]));
        return new Map(10, 10, cells);
    }

    private static IReadOnlyList<WallSetSlot> WallSets() =>
        [.. Enumerable.Range(0, 8).Select(_ =>
            new WallSetSlot("wall.png", string.Empty, "overlay.png", string.Empty,
                            string.Empty, 1, 0, 0, string.Empty, 0, 0))];

    private static MonsterEvent Entry(string id, int quantity) =>
        new(quantity, Type: 3, id, CharacterId: string.Empty, Friendly: 0, MoraleAdjustment: 0,
            0, 0, 0, 0, Money: null, Items: new ItemList([], new ReadyItems([])));

    private static CombatEvent Event(int monsters, int distance = 0) =>
        new(Base: null!, string.Empty, string.Empty, string.Empty,
            distance, Direction: 0, Surprise: 0, AutoApproach: 0,
            Outdoors: 0, NoMonsterTreasure: 0, PartyNeverDies: 0, NoMagic: 0,
            MonsterMorale: 50, Terrain: 0, RandomMonster: 0, PartyNoExperience: 0,
            BackgroundSounds: null!, [Entry("orc", monsters)]);

    private static MonsterRecord Orc() =>
        new(0, "Orc", null, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            Intelligence: 8, ArmorClass: 6, Movement: 9, HitDice: 1, UseHitDice: 1,
            HitDiceBonus: 0, Thac0: 19,
            Attacks: [new AttackDetails(6, 1, 0, string.Empty, string.Empty, 0, 0, 0)],
            MagicResistance: 0, Size: 1, ClassId: string.Empty, Morale: 50, ExperienceValue: 15,
            FormType: 0, PenaltyType: 0, ImmunityType: 0, MiscOptionsType: 0,
            UndeadType: string.Empty, SpecialAbilities: null!, Attributes: [],
            Items: null, Money: null);

    private static List<Combatant> Party(int size, bool auto = true) =>
        [.. Enumerable.Range(0, size).Select(i =>
            new Combatant(i, isFriendly: true, new CombatantIcon(1, 1), $"hero{i}")
            {
                Kind = CombatantKind.Character,
                IsAuto = auto,
                MaxMovement = 12,
                HitPoints = 14,
                MaxHitPoints = 14,
                Initiative = i + 1,
            })];

    /// <summary>A roller with a fixed face, so a fight is deterministic.</summary>
    private static Func<int, int> Roll(int face) => _ => face;

    private static CombatSession Start(int party, int monsters, bool auto = true,
                                       int face = 10, int distance = 0) =>
        CombatSession.Begin(Event(monsters, distance), EmptyLevel(), WallSets(), 5, 5,
                            Facing.North, Party(party, auto), _ => Orc(), Roll(face));

    [Fact]
    public void A_session_starts_with_everybody_placed_and_a_round_under_way()
    {
        var session = Start(party: 4, monsters: 4);

        Assert.Equal(8, session.Combatants.Count);
        Assert.True(session.IsActive);
        Assert.Equal(1, session.Round.Round);
        Assert.All(session.Combatants, c => Assert.True(c.X >= 0));
    }

    [Fact]
    public void An_all_auto_fight_runs_itself_to_a_conclusion()
    {
        // The whole stack, driven by nothing but Update: round clock, AI, pathing, movement,
        // attack, the dying clock.
        var session = Start(party: 4, monsters: 4, face: 18);

        for (int step = 0; step < 5000 && session.IsActive; step++)
        {
            session.Update();
        }

        Assert.False(session.IsActive);
        Assert.NotEqual(CombatOutcome.Running, session.Outcome);
    }

    [Fact]
    public void The_party_wins_when_the_last_enemy_falls()
    {
        var session = Start(party: 4, monsters: 1, face: 20);

        for (int step = 0; step < 5000 && session.IsActive; step++)
        {
            session.Update();
        }

        Assert.Equal(CombatOutcome.PartyWon, session.Outcome);
        Assert.DoesNotContain(session.Combatants.Where(c => !c.IsFriendly),
                              c => c.IsOnCombatMap());
    }

    [Fact]
    public void Missing_forever_is_not_an_idle_fight()
    {
        // The idle rule keys on ATTACKING, not on hitting: Attack.Resolve stamps lastAttackRound
        // whether or not the blow lands. So two sides swinging and missing are not idle and the
        // fight does not end -- which is the reference's behaviour, not a defect here. A first
        // draft of this test assumed the opposite and was wrong about what "idle" means.
        var session = Start(party: 2, monsters: 2, face: 1);

        for (int step = 0; step < 4000 && session.IsActive; step++)
        {
            session.Update();
        }

        Assert.True(session.IsActive);
        Assert.True(session.Round.Round > CombatRound.MaxIdleRounds);
        Assert.Contains(session.Combatants, c => c.LastAttackRound > 0);
    }

    // ---- the player's turn -----------------------------------------------------------------

    [Fact]
    public void A_player_run_combatant_stops_the_fight_and_raises_a_menu()
    {
        var session = Start(party: 2, monsters: 2, auto: false);

        // Nobody moves until the player is asked.
        for (int step = 0; step < 20 && !session.AwaitingPlayer; step++)
        {
            session.Update();
        }

        Assert.True(session.AwaitingPlayer);
        Assert.Equal(15, session.Menu.Count);
        Assert.False(session.Combatants[session.Acting].IsAuto);
    }

    [Fact]
    public void Waiting_on_the_player_makes_no_progress_without_input()
    {
        var session = Start(party: 2, monsters: 2, auto: false);
        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        int acting = session.Acting;
        for (int i = 0; i < 50; i++)
        {
            session.Update();
        }

        Assert.Equal(acting, session.Acting);
    }

    [Fact]
    public void The_arrow_keys_move_the_menu_selection()
    {
        var session = Start(party: 2, monsters: 2, auto: false);
        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        int first = session.Menu.ActiveItem;
        session.Update(InputEvent.KeyDown(VirtualKey.Right));

        Assert.NotEqual(first, session.Menu.ActiveItem);
    }

    [Fact]
    public void Choosing_guard_ends_the_turn()
    {
        var session = Start(party: 2, monsters: 2, auto: false);
        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        var actor = session.Combatants[session.Acting];
        while (CombatMenu.At(session.Menu.ActiveItem) != CombatCommand.Guard)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(CombatantState.Guarding, actor.State);
        Assert.True(actor.TurnIsDone);
        Assert.Contains("guards", session.Message);
    }

    [Fact]
    public void An_unimplemented_command_says_so_rather_than_ending_the_turn()
    {
        // Offering a command that silently does nothing is worse than saying it is not there.
        var session = Start(party: 2, monsters: 2, auto: false);
        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        var actor = session.Combatants[session.Acting];
        while (CombatMenu.At(session.Menu.ActiveItem) != CombatCommand.View)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Contains("not implemented", session.Message);
        Assert.False(actor.TurnIsDone);
    }

    [Fact]
    public void A_player_fight_can_be_played_through_to_the_end()
    {
        // Guarding every turn lets the monsters do the work, which is enough to prove the loop
        // does not stall on a player turn.
        var session = Start(party: 2, monsters: 3, auto: false, face: 19);

        for (int step = 0; step < 20000 && session.IsActive; step++)
        {
            if (session.AwaitingPlayer)
            {
                while (CombatMenu.At(session.Menu.ActiveItem) != CombatCommand.Guard)
                {
                    session.Update(InputEvent.KeyDown(VirtualKey.Right));
                }
                session.Update(InputEvent.KeyDown(VirtualKey.Return));
            }
            else
            {
                session.Update();
            }
        }

        Assert.False(session.IsActive);
    }

    [Fact]
    public void Initiative_is_rolled_for_everybody_at_the_start_of_a_round()
    {
        // Without this every monster sits at zero and the round's 1..22 walk never reaches it, so
        // the fight runs itself out with only the party acting. That is exactly what happened the
        // first time a real encounter was driven end to end.
        var session = Start(party: 2, monsters: 3);

        Assert.All(session.Combatants,
                   c => Assert.InRange(c.Initiative, UAF.Rules.Initiative.First,
                                       UAF.Rules.Initiative.Last));
    }

    [Fact]
    public void Every_combatant_gets_a_turn_including_the_monsters()
    {
        var session = Start(party: 2, monsters: 2, face: 10);

        var acted = new HashSet<int>();
        for (int step = 0; step < 2000 && session.IsActive; step++)
        {
            if (session.Acting != CombatMap.NoDude)
            {
                acted.Add(session.Acting);
            }
            session.Update();
        }

        Assert.Contains(acted, i => !session.Combatants[i].IsFriendly);
        Assert.Contains(acted, i => session.Combatants[i].IsFriendly);
    }

    [Fact]
    public void Monsters_arrive_with_hit_points_rolled_from_their_dice()
    {
        var session = Start(party: 2, monsters: 2);

        Assert.All(session.Combatants.Where(c => !c.IsFriendly),
                   c => Assert.True(c.HitPoints > 0, "a monster arrived already dead"));
    }

    // ---- aiming ----------------------------------------------------------------------------

    /// <summary>Drives to a player turn and selects a command without choosing it.</summary>
    private static CombatSession AtCommand(CombatSession session, CombatCommand want)
    {
        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        while (CombatMenu.At(session.Menu.ActiveItem) != want)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }

        return session;
    }

    [Fact]
    public void Aim_opens_a_submenu_rather_than_attacking_outright()
    {
        // The player picks the target; AIM used to swing at whatever the cycle landed on.
        var session = AtCommand(Start(party: 2, monsters: 2, auto: false), CombatCommand.Aim);
        var actor = session.Combatants[session.Acting];

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(CombatMenuMode.Aiming, session.Mode);
        Assert.Equal(CombatMenu.AimLabels.Length, session.Menu.Count);
        Assert.False(actor.TurnIsDone);
    }

    [Fact]
    public void Manual_aiming_lets_the_arrows_steer_the_cursor()
    {
        var session = AtCommand(Start(party: 2, monsters: 2, auto: false), CombatCommand.Aim);
        session.Update(InputEvent.KeyDown(VirtualKey.Return));      // into Aiming

        while ((AimCommand)(session.Menu.ActiveItem + 1) != AimCommand.Manual)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }
        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(CombatMenuMode.AimingManual, session.Mode);

        var before = (session.Cursor.X, session.Cursor.Y);
        session.Update(InputEvent.KeyDown(VirtualKey.Down));

        Assert.Equal((before.Item1, before.Item2 + 1), (session.Cursor.X, session.Cursor.Y));
    }

    [Fact]
    public void Exiting_the_aim_menu_returns_to_the_commands_without_spending_the_turn()
    {
        var session = AtCommand(Start(party: 2, monsters: 2, auto: false), CombatCommand.Aim);
        var actor = session.Combatants[session.Acting];
        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        while ((AimCommand)(session.Menu.ActiveItem + 1) != AimCommand.Exit)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }
        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(CombatMenuMode.Command, session.Mode);
        Assert.Equal(CombatMenu.Labels.Length, session.Menu.Count);
        Assert.False(actor.TurnIsDone);
    }

    // ---- bandaging -------------------------------------------------------------------------

    [Fact]
    public void Bandaging_stabilises_a_dying_ally_and_ends_the_turn()
    {
        var session = Start(party: 3, monsters: 1, auto: false);
        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        var hurt = session.Combatants.Last(c => c.IsFriendly && c.Index != session.Acting);
        hurt.Status = CharacterStatus.Dying;
        hurt.HitPoints = -4;

        var actor = session.Combatants[session.Acting];
        while (CombatMenu.At(session.Menu.ActiveItem) != CombatCommand.Bandage)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }
        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(CharacterStatus.Unconscious, hurt.Status);
        Assert.Equal(0, hurt.HitPoints);
        Assert.True(actor.TurnIsDone);
        Assert.Contains("bandages", session.Message);
    }

    [Fact]
    public void Bandaging_with_nobody_dying_says_so_and_keeps_the_turn()
    {
        // CanBandage is just !IsDone in the reference, so the entry is offered regardless and the
        // action finds a target or does nothing.
        var session = AtCommand(Start(party: 2, monsters: 2, auto: false), CombatCommand.Bandage);
        var actor = session.Combatants[session.Acting];

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Contains("Nobody needs", session.Message);
        Assert.False(actor.TurnIsDone);
    }

    [Fact]
    public void A_turn_announces_whose_it_is()
    {
        var session = Start(party: 2, monsters: 2, auto: false);
        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        Assert.Contains(session.Combatants[session.Acting].Name, session.Message);
    }

    [Fact]
    public void The_view_scrolls_to_keep_the_acting_combatant_visible()
    {
        var session = Start(party: 2, monsters: 2);
        session.Update();

        var actor = session.Combatants[session.Acting >= 0 ? session.Acting : 0];
        Assert.InRange(actor.X - session.Renderer.ScrollX, 0, 9);
        Assert.InRange(actor.Y - session.Renderer.ScrollY, 0, 7);
    }

    [Fact]
    public void Rendering_without_art_draws_nothing_and_does_not_throw()
    {
        var session = Start(party: 2, monsters: 2);
        var screen = new Surface(640, 480);

        session.Render(screen, sheet: null, new SurfaceRect(14, 16, 400, 400));

        Assert.Equal(0u, screen[100, 100]);
    }
}
