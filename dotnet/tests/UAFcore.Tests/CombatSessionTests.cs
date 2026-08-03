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
            MonsterMorale: 50, TurningMod: 0, RandomMonster: 0, PartyNoExperience: 0,
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

    /// <summary>A spell that takes two rounds to cast, so it lands on the clock rather than at once.</summary>
    private static SpellRecord SlowSpell(string id) =>
        new(0, id, string.Empty, string.Empty, [],
            Level: 1, CastingTime: 2, CastingTimeType: (int)SpellCastingTime.Rounds,
            CanTargetFriend: 0, CanTargetEnemy: 1, IsCumulative: 0, Restrictions: 0,
            CanBeDispelled: 1, CanMemorize: 1, AllowScribe: 0, AutoScribe: 0,
            Lingers: 0, LingerOnceOnly: 0, SaveVersus: 0, SaveResult: 0, Targeting: 0,
            DurationRate: 0, CastCost: 0, CastPriority: 0,
            Parameters: [], Effects: [], CastArt: null, Art: [], Sounds: [],
            CastMessage: string.Empty, Scripts: [], EffectDuration: null,
            SpecialAbilities: null!, Attributes: []);

    /// <summary>An item that casts a spell when used, with the given number of charges.</summary>
    private static ItemRecord Wand(string spellId, int charges = 3) =>
        new(new ItemNames(0, spellId, "wand", "Wand of Shielding", string.Empty, string.Empty,
                          string.Empty),
            HitArt: null, MissileArt: null,
            new ItemScalars(string.Empty, 0, 0, 0, 0, 0, 1, charges),
            new ItemCombat(0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0),
            new ItemTail(0, 0, 0, [], 0, 0, 0, string.Empty, string.Empty, 0, 0, null, 0, 0,
                         null!, []));

    private static ItemInstance Carried(string itemId, int charges = 3) =>
        new(Key: 1, itemId, LegacyItemId: 0, ReadyLocation: 0, Quantity: 1, Identified: 1,
            charges, Cursed: 0, Paid: 0);

    /// <summary>A player-run fight whose party carries a wand.</summary>
    private static CombatSession WandSession(int charges = 3)
    {
        var session = CombatSession.Begin(Event(2), EmptyLevel(), WallSets(), 5, 5, Facing.North,
                                          Party(2, auto: false), _ => Orc(), Roll(10),
                                          spellInfo: SelfSpell);
        session.ItemInfo = _ => Wand("shield", charges);
        foreach (var c in session.Combatants.Where(c => c.IsFriendly))
        {
            c.Items.Add(Carried("wand", charges));
        }

        return session;
    }

    /// <summary>A spell that lands on the caster at once, with one effect.</summary>
    private static SpellRecord SelfSpell(string id) =>
        new(0, id, string.Empty, string.Empty, [],
            Level: 1, CastingTime: 0, CastingTimeType: (int)SpellCastingTime.Rounds,
            CanTargetFriend: 1, CanTargetEnemy: 0, IsCumulative: 0, Restrictions: 0,
            CanBeDispelled: 1, CanMemorize: 1, AllowScribe: 0, AutoScribe: 0,
            Lingers: 0, LingerOnceOnly: 0, SaveVersus: 0,
            SaveResult: (int)UAF.Rules.SaveResult.NoSave,
            Targeting: (int)SpellTargeting.Self,
            DurationRate: (int)UAF.Rules.SpellDurationRate.InRounds, CastCost: 0, CastPriority: 0,
            Parameters: [], Effects: [SelfEffect()], CastArt: null, Art: [], Sounds: [],
            CastMessage: string.Empty, Scripts: [],
            EffectDuration: Dice("3"), SpecialAbilities: null!, Attributes: []);

    private static DicePlus Dice(string text) =>
        new("DP2", text, string.Empty, 0, 0, 0, 0, 0, 1, []);

    private static UAF.Serialization.SpellEffect SelfEffect() =>
        new("$CHAR_AC", (uint)UAF.Rules.SpellEffectFlags.Target, 0, string.Empty, 0, 0, [], 0, 0,
            Dice("-2"));

    /// <summary>A spell that makes the player name two enemies.</summary>
    private static SpellRecord PickTwoSpell(string id) =>
        new(0, id, string.Empty, string.Empty, [],
            Level: 1, CastingTime: 0, CastingTimeType: (int)SpellCastingTime.Rounds,
            CanTargetFriend: 0, CanTargetEnemy: 1, IsCumulative: 0, Restrictions: 0,
            CanBeDispelled: 1, CanMemorize: 1, AllowScribe: 0, AutoScribe: 0,
            Lingers: 0, LingerOnceOnly: 0, SaveVersus: 0,
            SaveResult: (int)UAF.Rules.SaveResult.NoSave,
            Targeting: (int)SpellTargeting.SelectedByCount,
            DurationRate: (int)UAF.Rules.SpellDurationRate.InRounds, CastCost: 0, CastPriority: 0,
            Parameters: [], Effects: [SelfEffect()], CastArt: null, Art: [], Sounds: [],
            CastMessage: string.Empty, Scripts: [],
            EffectDuration: Dice("3"), SpecialAbilities: null!, Attributes: []);

    /// <summary>A circle centred on the cursor that stays on the map afterwards.</summary>
    private static SpellRecord CloudSpell(string id) =>
        new(0, id, string.Empty, string.Empty, [],
            Level: 1, CastingTime: 0, CastingTimeType: (int)SpellCastingTime.Rounds,
            CanTargetFriend: 1, CanTargetEnemy: 1, IsCumulative: 1, Restrictions: 0,
            CanBeDispelled: 1, CanMemorize: 1, AllowScribe: 0, AutoScribe: 0,
            Lingers: 1, LingerOnceOnly: 0, SaveVersus: 0,
            SaveResult: (int)UAF.Rules.SaveResult.NoSave,
            Targeting: (int)SpellTargeting.AreaCircle,
            DurationRate: (int)UAF.Rules.SpellDurationRate.InRounds, CastCost: 0, CastPriority: 0,
            Parameters: [], Effects: [CumulativeEffect()], CastArt: null, Art: [], Sounds: [],
            CastMessage: string.Empty, Scripts: [],
            EffectDuration: Dice("9"), SpecialAbilities: null!, Attributes: []);

    private static UAF.Serialization.SpellEffect CumulativeEffect() =>
        new("$CHAR_HITPOINTS",
            (uint)(UAF.Rules.SpellEffectFlags.Target | UAF.Rules.SpellEffectFlags.Cumulative),
            0, string.Empty, 0, 0, [], 0, 0, Dice("-1"));

    /// <summary>A player-run fight whose spell leaves a cloud behind.</summary>
    private static CombatSession CloudSession() =>
        CombatSession.Begin(Event(2), EmptyLevel(), WallSets(), 5, 5, Facing.North,
                            Party(2, auto: false), _ => Orc(), Roll(10),
                            spellInfo: CloudSpell);

    /// <summary>A player-run fight whose spell needs its targets naming.</summary>
    private static CombatSession PickTwoSession() =>
        CombatSession.Begin(Event(2), EmptyLevel(), WallSets(), 5, 5, Facing.North,
                            Party(2, auto: false), _ => Orc(), Roll(10),
                            spellInfo: PickTwoSpell);

    /// <summary>Runs the fight until the given condition holds, ENDing every player turn.</summary>
    private static void RunUntil(CombatSession session, Func<bool> until, int steps = 2000)
    {
        for (int step = 0; step < steps && !until() && session.IsActive; step++)
        {
            if (!session.AwaitingPlayer)
            {
                session.Update();
            }
            else if (CombatMenu.At(session.Menu.ActiveItem) == CombatCommand.End)
            {
                session.Update(InputEvent.KeyDown(VirtualKey.Return));
            }
            else
            {
                session.Update(InputEvent.KeyDown(VirtualKey.Right));
            }
        }
    }

    /// <summary>A player-run fight whose spells resolve on the caster.</summary>
    private static CombatSession SelfSpellSession() =>
        CombatSession.Begin(Event(2), EmptyLevel(), WallSets(), 5, 5, Facing.North,
                            Party(2, auto: false), _ => Orc(), Roll(10),
                            spellInfo: SelfSpell);

    /// <summary>A player-run fight whose spells run on the casting clock.</summary>
    private static CombatSession SpellSession() =>
        CombatSession.Begin(Event(2), EmptyLevel(), WallSets(), 5, 5, Facing.North,
                            Party(2, auto: false), _ => Orc(), Roll(10),
                            spellInfo: SlowSpell);

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
        // SPEED is the game-speed setting -- a presentation control rather than a combat rule, and
        // deliberately not ported.
        var session = Start(party: 2, monsters: 2, auto: false);
        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        var actor = session.Combatants[session.Acting];
        while (CombatMenu.At(session.Menu.ActiveItem) != CombatCommand.Speed)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Contains("not implemented", session.Message);
        Assert.False(actor.TurnIsDone);
    }

    [Fact]
    public void View_reports_the_acting_combatant_without_ending_its_turn()
    {
        var session = AtCommand(Start(party: 2, monsters: 2, auto: false), CombatCommand.View);
        var actor = session.Combatants[session.Acting];

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Contains(actor.Name, session.Message);
        Assert.Contains("hp", session.Message);
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

    // ---- casting ---------------------------------------------------------------------------

    /// <summary>Gives every friendly combatant one memorised spell, so CAST is offered.</summary>
    private static CombatSession WithSpells(CombatSession session, string spellId = "sleep")
    {
        foreach (var c in session.Combatants.Where(c => c.IsFriendly))
        {
            c.Book.Add(spellId, level: 1, memorized: 1);
        }

        return session;
    }

    [Fact]
    public void Cast_is_offered_only_when_there_is_something_memorised()
    {
        var bare = Start(party: 2, monsters: 2, auto: false);
        while (!bare.AwaitingPlayer)
        {
            bare.Update();
        }

        Assert.False(bare.Menu.Items[(int)CombatCommand.Cast - 1].Enabled);

        var armed = WithSpells(Start(party: 2, monsters: 2, auto: false));
        while (!armed.AwaitingPlayer)
        {
            armed.Update();
        }

        Assert.True(armed.Menu.Items[(int)CombatCommand.Cast - 1].Enabled);
    }

    [Fact]
    public void Cast_opens_the_spell_list()
    {
        var session = AtCommand(WithSpells(Start(party: 2, monsters: 2, auto: false)),
                                CombatCommand.Cast);

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(CombatMenuMode.ChoosingSpell, session.Mode);
        Assert.Equal(CombatMenu.CastLabels.Length, session.Menu.Count);
        Assert.Single(session.SpellChoices);
        Assert.Contains("sleep", session.Message);
    }

    [Fact]
    public void Leaving_the_spell_list_costs_neither_the_spell_nor_the_turn()
    {
        var session = AtCommand(WithSpells(Start(party: 2, monsters: 2, auto: false)),
                                CombatCommand.Cast);
        var actor = session.Combatants[session.Acting];
        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        while ((CastCommand)(session.Menu.ActiveItem + 1) != CastCommand.Exit)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }
        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(CombatMenuMode.Command, session.Mode);
        Assert.False(actor.TurnIsDone);
        Assert.Equal(1, actor.Book.Find("sleep")!.Memorized);
    }

    /// <summary>Drives the acting player to CAST and casts the first spell in the book.</summary>
    private static Combatant CastFirstSpell(CombatSession session)
    {
        AtCommand(session, CombatCommand.Cast);
        var actor = session.Combatants[session.Acting];
        session.Update(InputEvent.KeyDown(VirtualKey.Return));   // into the spell list
        session.Update(InputEvent.KeyDown(VirtualKey.Return));   // CAST
        return actor;
    }

    [Fact]
    public void Casting_spends_the_memorised_copy_and_ends_the_turn()
    {
        // Spent when the spell is begun, not when it lands -- which is what makes interrupting a
        // caster worth doing.
        var session = WithSpells(Start(party: 2, monsters: 2, auto: false));
        var actor = CastFirstSpell(session);

        Assert.Equal(0, actor.Book.Find("sleep")!.Memorized);
        Assert.True(actor.TurnIsDone);
        Assert.Equal(CombatantState.Casting, actor.State);
    }

    [Fact]
    public void A_spell_with_no_casting_time_never_reaches_the_pending_list()
    {
        // No spell record is supplied here, so casting time is zero and the type is Immediate.
        var session = WithSpells(Start(party: 2, monsters: 2, auto: false));
        var actor = CastFirstSpell(session);

        Assert.Equal(0, session.Pending.Count);
        Assert.False(actor.IsSpellPending);
    }

    [Fact]
    public void A_spell_on_the_clock_leaves_the_caster_casting_until_it_lands()
    {
        var session = WithSpells(SpellSession());
        var actor = CastFirstSpell(session);

        Assert.True(actor.IsSpellPending);
        Assert.Equal(1, session.Pending.Count);
        Assert.Contains("begins casting", session.Message);

        // Run the fight on until the clock gives the caster its turn back. Every player is told
        // to END, so the rounds actually roll over instead of stalling on the menu.
        for (int step = 0; step < 2000 && session.Pending.Count > 0 && session.IsActive; step++)
        {
            if (!session.AwaitingPlayer)
            {
                session.Update();
            }
            else if (CombatMenu.At(session.Menu.ActiveItem) == CombatCommand.End)
            {
                session.Update(InputEvent.KeyDown(VirtualKey.Return));
            }
            else
            {
                session.Update(InputEvent.KeyDown(VirtualKey.Right));
            }
        }

        Assert.True(session.Round.Round >= 3, $"only reached round {session.Round.Round}");

        Assert.Equal(0, session.Pending.Count);
        Assert.Equal(-1, actor.PendingSpellKey);
    }

    [Fact]
    public void Damage_voids_a_spell_being_cast()
    {
        var session = WithSpells(SpellSession());
        var actor = CastFirstSpell(session);
        Assert.True(actor.IsSpellPending);

        Casting.OnDamaged(actor, damage: 3, session.Pending, session.Round.Queue);

        Assert.Equal(0, session.Pending.Count);
        Assert.Equal(-1, actor.PendingSpellKey);
        Assert.Null(actor.SpellBeingCast);
        Assert.Equal(CombatantState.None, actor.State);
        Assert.Equal(0, actor.Book.Find("sleep")!.Memorized);   // not refunded
    }

    [Fact]
    public void A_caster_hurt_by_its_own_spell_finishes_casting()
    {
        var session = WithSpells(SpellSession());
        var actor = CastFirstSpell(session);

        Casting.OnDamaged(actor, damage: 3, session.Pending, session.Round.Queue, fromSelf: true);

        Assert.True(actor.IsSpellPending);
        Assert.Equal(1, session.Pending.Count);
    }

    [Fact]
    public void A_caster_dropped_to_zero_loses_the_spell_even_from_its_own_blast()
    {
        var session = WithSpells(SpellSession());
        var actor = CastFirstSpell(session);
        actor.HitPoints = 0;

        Casting.OnDamaged(actor, damage: 3, session.Pending, session.Round.Queue, fromSelf: true);

        Assert.Equal(0, session.Pending.Count);
        Assert.Equal(CombatantState.None, actor.State);
    }

    [Fact]
    public void A_spell_that_comes_due_resolves_on_its_target()
    {
        // The whole casting stack end to end: choose from the book, spend the copy, wait out the
        // casting time, get the turn back, and land the effect.
        var session = WithSpells(SelfSpellSession(), "shield");
        var actor = CastFirstSpell(session);

        Assert.True(actor.IsSpellPending);
        Assert.Equal(0, actor.Effects.Count);

        for (int step = 0; step < 2000 && actor.Effects.Count == 0 && session.IsActive; step++)
        {
            if (!session.AwaitingPlayer)
            {
                session.Update();
            }
            else if (CombatMenu.At(session.Menu.ActiveItem) == CombatCommand.End)
            {
                session.Update(InputEvent.KeyDown(VirtualKey.Return));
            }
            else
            {
                session.Update(InputEvent.KeyDown(VirtualKey.Right));
            }
        }

        Assert.Equal(1, actor.Effects.Count);
        Assert.Equal("$CHAR_AC", actor.Effects.Effects[0].Attribute);
        Assert.Equal(-2, actor.Effects.Effects[0].Effect.Change);
        Assert.Equal("shield", actor.Effects.Effects[0].SourceSpell);
        Assert.Null(actor.SpellBeingCast);
        Assert.Contains("1 of 1 affected", session.Message);
    }

    [Fact]
    public void A_spell_that_needs_targets_hands_the_cursor_to_the_player()
    {
        var session = WithSpells(PickTwoSession(), "magic missile");
        var actor = CastFirstSpell(session);

        RunUntil(session, () => session.Selecting is not null);

        Assert.NotNull(session.Selecting);
        Assert.Equal(CombatMenuMode.SpellAiming, session.Mode);
        Assert.Equal(CombatMenu.AimLabels.Length, session.Menu.Count);
        Assert.Contains("CHOOSE", session.Message);
        Assert.False(actor.TurnIsDone);
    }

    [Fact]
    public void Naming_the_last_target_resolves_the_spell_and_ends_the_turn()
    {
        var session = WithSpells(PickTwoSession(), "magic missile");
        var actor = CastFirstSpell(session);
        RunUntil(session, () => session.Selecting is not null);

        // The setup asks for one target, so the first TARGET finishes the selection.
        while ((AimCommand)(session.Menu.ActiveItem + 1) != AimCommand.Target)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }
        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Null(session.Selecting);
        Assert.Equal(CombatMenuMode.Command, session.Mode);
        Assert.True(actor.TurnIsDone);
        Assert.Contains("affected", session.Message);
    }

    [Fact]
    public void Leaving_the_target_menu_with_nobody_chosen_abandons_the_spell()
    {
        var session = WithSpells(PickTwoSession(), "magic missile");
        var actor = CastFirstSpell(session);
        RunUntil(session, () => session.Selecting is not null);

        while ((AimCommand)(session.Menu.ActiveItem + 1) != AimCommand.Exit)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }
        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Null(session.Selecting);
        Assert.Contains("abandons", session.Message);
        Assert.True(actor.TurnIsDone);
    }

    [Fact]
    public void A_computer_run_caster_picks_its_own_targets()
    {
        // The reference runs the design's Forth script here; this takes what it can legally reach,
        // so a monster casting is at least a real cast rather than a no-op.
        var session = CombatSession.Begin(Event(2), EmptyLevel(), WallSets(), 5, 5, Facing.North,
                                          Party(2), _ => Orc(), Roll(10),
                                          spellInfo: PickTwoSpell);
        var caster = session.Combatants[0];
        caster.Book.Add("magic missile", level: 1, memorized: 1);

        Casting.Begin(caster, "magic missile", castingTime: 1, SpellCastingTime.Rounds,
                      session.Pending, session.Round.Round, session.Round.Queue);
        caster.TurnIsDone = true;

        RunUntil(session, () => session.Combatants.Any(c => c.Effects.Count > 0));

        Assert.Contains(session.Combatants, c => c.Effects.Count > 0);
        Assert.Null(session.Selecting);
    }

    [Fact]
    public void A_lingering_spell_stays_on_the_map_and_catches_people_later()
    {
        var session = WithSpells(CloudSession(), "stinking cloud");
        var actor = CastFirstSpell(session);

        // Wait for the cast to come due and resolve; it leaves a cloud where the cursor was.
        RunUntil(session, () => session.Lingering.Count > 0);
        Assert.Equal(1, session.Lingering.Count);

        var cloud = session.Lingering.Spells[0];
        Assert.NotEmpty(cloud.Squares);
        Assert.Equal("stinking cloud", cloud.SpellId);

        // Stand somebody in it who was not there when it went off, and let a round roll over.
        var victim = session.Combatants.First(c => c.Index != actor.Index
                                                   && !cloud.Caught.Contains(c.Index));
        var (cx, cy) = cloud.Squares.First();
        session.Map.Remove(victim.X, victim.Y, victim.Icon.Width, victim.Icon.Height);
        victim.X = cx;
        victim.Y = cy;
        session.Map.Place(cx, cy, victim.Index, victim.Icon.Width, victim.Icon.Height);
        int before = victim.Effects.Count;

        int round = session.Round.Round;
        RunUntil(session, () => session.Round.Round > round && victim.Effects.Count > before);

        Assert.Contains(victim.Index, cloud.Caught);
        Assert.True(victim.Effects.Count > before);
    }

    [Fact]
    public void A_spell_that_does_not_linger_leaves_nothing_behind()
    {
        var session = WithSpells(SelfSpellSession(), "shield");
        CastFirstSpell(session);

        RunUntil(session, () => session.Combatants.Any(c => c.Effects.Count > 0));

        Assert.Equal(0, session.Lingering.Count);
    }

    // ---- the turn-ordering commands ----------------------------------------------------------

    [Fact]
    public void Delaying_moves_the_combatant_later_in_the_round_without_ending_its_turn()
    {
        var session = AtCommand(Start(party: 2, monsters: 2, auto: false), CombatCommand.Delay);
        var actor = session.Combatants[session.Acting];
        int before = actor.Initiative;

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(before + 1, actor.Initiative);
        Assert.False(actor.TurnIsDone);
        Assert.Contains("delays", session.Message);
    }

    [Fact]
    public void A_delayed_combatant_gets_its_turn_again_at_the_new_slot()
    {
        var session = AtCommand(Start(party: 2, monsters: 2, auto: false), CombatCommand.Delay);
        int who = session.Acting;
        int round = session.Round.Round;

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        // Every other player ENDs, so the walk carries on rather than stalling on the menu.
        for (int step = 0; step < 200 && session.Acting != who
                           && session.Round.Round == round; step++)
        {
            if (!session.AwaitingPlayer)
            {
                session.Update();
            }
            else if (CombatMenu.At(session.Menu.ActiveItem) == CombatCommand.End)
            {
                session.Update(InputEvent.KeyDown(VirtualKey.Return));
            }
            else
            {
                session.Update(InputEvent.KeyDown(VirtualKey.Right));
            }
        }

        Assert.Equal(who, session.Acting);
        Assert.Equal(round, session.Round.Round);
    }

    [Fact]
    public void Delaying_is_refused_at_the_last_initiative_slot()
    {
        var session = AtCommand(Start(party: 2, monsters: 2, auto: false), CombatCommand.Delay);
        var actor = session.Combatants[session.Acting];
        actor.Initiative = CombatRound.NeverInitiative - 1;

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(CombatRound.NeverInitiative - 1, actor.Initiative);
        Assert.Contains("cannot delay", session.Message);
    }

    [Fact]
    public void Quick_only_ever_hands_the_combatant_to_the_ai()
    {
        // The menu calls Quick(TRUE) and nothing else -- there is no menu route back.
        var session = AtCommand(Start(party: 2, monsters: 2, auto: false), CombatCommand.Quick);
        var actor = session.Combatants[session.Acting];

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.True(actor.IsAuto);
        Assert.Contains("on automatic", session.Message);
    }

    [Fact]
    public void Space_takes_a_party_member_back_off_automatic()
    {
        // Bound to the space bar rather than to a menu entry, and undoing whatever the AI had the
        // combatant doing.
        var session = AtCommand(Start(party: 2, monsters: 2, auto: false), CombatCommand.Quick);
        var actor = session.Combatants[session.Acting];
        session.Update(InputEvent.KeyDown(VirtualKey.Return));
        Assert.True(actor.IsAuto);

        actor.Target = 3;
        actor.State = CombatantState.Attacking;
        session.Update(InputEvent.KeyDown(VirtualKey.Space));

        Assert.False(actor.IsAuto);
        Assert.Equal(CombatMap.NoDude, actor.Target);
        Assert.Equal(CombatantState.None, actor.State);
        Assert.Contains("off automatic", session.Message);
    }

    [Fact]
    public void A_combatant_denied_player_control_cannot_be_put_on_automatic()
    {
        var session = AtCommand(Start(party: 2, monsters: 2, auto: false), CombatCommand.Quick);
        var actor = session.Combatants[session.Acting];
        actor.AllowPlayerControl = false;

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.False(actor.IsAuto);
        Assert.Contains("cannot be controlled", session.Message);
    }

    // ---- turning -------------------------------------------------------------------------------

    [Fact]
    public void Turn_is_offered_only_to_a_combatant_that_can_turn()
    {
        var session = Start(party: 2, monsters: 2, auto: false);
        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        Assert.False(session.Menu.Items[(int)CombatCommand.Turn - 1].Enabled);

        session.Combatants[session.Acting].TurnLevel = 3;
        session.Update(InputEvent.KeyDown(VirtualKey.Right));   // rebuilds nothing by itself
    }

    [Fact]
    public void Turning_with_no_script_answer_turns_nothing()
    {
        // The AD&D table is dead code; turning is entirely design-scripted, so without GPDL there
        // is nothing to ask and nothing happens.
        var session = Start(party: 2, monsters: 2, auto: false);
        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        var actor = session.Combatants[session.Acting];
        actor.TurnLevel = 3;
        CombatMenu.Build(session.Menu, new CombatOptions(CanTurnUndead: true));
        while (CombatMenu.At(session.Menu.ActiveItem) != CombatCommand.Turn)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Contains("turns nothing", session.Message);
        Assert.True(actor.TurnIsDone);
    }

    [Fact]
    public void Turning_with_a_script_answer_sends_the_undead_running()
    {
        var session = Start(party: 2, monsters: 2, auto: false);
        session.TurnDataOf = c => c.IsFriendly ? null : new TurnData("skeleton", 9, false);
        session.TurnAttempt = _ => new Dictionary<string, int> { ["skeleton"] = 1 };

        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        var actor = session.Combatants[session.Acting];
        actor.TurnLevel = 3;
        CombatMenu.Build(session.Menu, new CombatOptions(CanTurnUndead: true));
        while (CombatMenu.At(session.Menu.ActiveItem) != CombatCommand.Turn)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Contains("turns 1", session.Message);
        Assert.Contains(session.Combatants, c => !c.IsFriendly && c.IsTurned);
    }

    // ---- using an item -------------------------------------------------------------------------

    [Fact]
    public void Use_lists_what_the_combatant_can_actually_invoke()
    {
        var session = AtCommand(WandSession(), CombatCommand.Use);
        var actor = session.Combatants[session.Acting];

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(CombatMenuMode.ChoosingItem, session.Mode);
        Assert.Equal(CombatMenu.UseLabels.Length, session.Menu.Count);
        Assert.Single(session.UsableItems(actor));
        Assert.Contains("wand", session.Message);
    }

    [Fact]
    public void An_item_with_no_charges_left_is_not_usable()
    {
        var session = AtCommand(WandSession(charges: 0), CombatCommand.Use);
        var actor = session.Combatants[session.Acting];

        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Empty(session.UsableItems(actor));
        Assert.Contains("nothing to use", session.Message);
        Assert.Equal(CombatMenuMode.Command, session.Mode);
    }

    [Fact]
    public void An_item_that_names_no_spell_is_not_usable()
    {
        // The spell id is only on the wire from design version 0.999647; an older design's items
        // name nothing, so USE has nothing to offer.
        var session = WandSession();
        session.ItemInfo = _ => Wand(string.Empty);
        while (!session.AwaitingPlayer)
        {
            session.Update();
        }

        Assert.Empty(session.UsableItems(session.Combatants[session.Acting]));
    }

    [Fact]
    public void Using_an_item_spends_a_charge_and_begins_its_spell()
    {
        var session = AtCommand(WandSession(charges: 3), CombatCommand.Use);
        var actor = session.Combatants[session.Acting];
        session.Update(InputEvent.KeyDown(VirtualKey.Return));   // into the item list
        session.Update(InputEvent.KeyDown(VirtualKey.Return));   // USE

        Assert.Equal(2, actor.Items[0].Charges);
        Assert.Equal("shield", actor.SpellBeingCast);
        Assert.Equal("shield", actor.ItemSpellBeingCast);
        Assert.Equal(CombatantState.Using, actor.State);
        Assert.True(actor.TurnIsDone);
        Assert.Contains("uses wand", session.Message);
    }

    [Fact]
    public void An_item_spell_never_touches_the_spell_book()
    {
        // The item's charges are the resource -- CastItemSpell has no book lookup and no
        // DecMemorized.
        var session = WithSpells(WandSession(), "shield");
        AtCommand(session, CombatCommand.Use);
        var actor = session.Combatants[session.Acting];
        int memorised = actor.Book.Find("shield")!.Memorized;

        session.Update(InputEvent.KeyDown(VirtualKey.Return));
        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(memorised, actor.Book.Find("shield")!.Memorized);
    }

    [Fact]
    public void Leaving_the_item_list_costs_neither_a_charge_nor_the_turn()
    {
        var session = AtCommand(WandSession(), CombatCommand.Use);
        var actor = session.Combatants[session.Acting];
        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        while ((CastCommand)(session.Menu.ActiveItem + 1) != CastCommand.Exit)
        {
            session.Update(InputEvent.KeyDown(VirtualKey.Right));
        }
        session.Update(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(CombatMenuMode.Command, session.Mode);
        Assert.Equal(3, actor.Items[0].Charges);
        Assert.False(actor.TurnIsDone);
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
