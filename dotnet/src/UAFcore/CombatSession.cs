using UAF.Media;
using UAF.Serialization;

namespace UAFcore;

/// <summary>How a fight ended.</summary>
public enum CombatOutcome
{
    /// <summary>Still going.</summary>
    Running,

    /// <summary>Every enemy is down or gone.</summary>
    PartyWon,

    /// <summary>Every party member is down or gone.</summary>
    PartyLost,

    /// <summary>Neither side achieved anything for long enough (<c>MAX_COMBAT_IDLE_ROUNDS</c>).</summary>
    Stalemate,
}

/// <summary>
/// One fight, from the encounter event to its conclusion.
/// </summary>
/// <remarks>
/// <para>
/// The join that makes everything else reachable: <see cref="EncounterBuilder"/> makes the
/// combatants, <see cref="CombatSetup"/> places them, <see cref="CombatRound"/> orders the turns,
/// <see cref="MonsterAi"/> decides for the computer-run ones, and <see cref="CombatRenderer"/>
/// draws it. It lives apart from <see cref="Game"/> so a fight can be driven in a test without a
/// loaded design.
/// </para>
/// <para>
/// <b>The reference has no object like this</b> — its combat lives in <c>COMBAT_DATA</c>, a global,
/// driven by the <c>CProcinp</c> task scheduler through <c>RunEvent.cpp</c>'s state machine. The
/// engine here is still a synchronous loop (§7 Phase 4 item 5), so the session owns the fight
/// directly and the scheduler stays unported.
/// </para>
/// </remarks>
public sealed class CombatSession
{
    private readonly List<Combatant> combatants;
    private readonly Dictionary<int, List<(int X, int Y)>> paths = [];
    private readonly Func<int, int> dice;

    private UAF.Rules.Surprise surprise;

    /// <summary>Loaded icon sheets and footprints, by combatant name.</summary>
    private readonly Dictionary<string, (Surface Sheet, CombatantIcon Icon)> icons = [];

    /// <summary>
    /// The sheet and source rectangle to draw a combatant with, or null when it has no art.
    /// </summary>
    /// <remarks>
    /// The attacking pose is used while a combatant is mid-attack, which is the whole of the
    /// reference's animation state this port models — <c>NeedHitAnimation</c> and its neighbours
    /// drive the rest and are not ported.
    /// </remarks>
    public (Surface Sheet, SurfaceRect Source)? IconFor(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        if (!icons.TryGetValue(combatant.Name, out var loaded))
        {
            return null;
        }

        return (loaded.Sheet,
                CombatIcons.PoseRect(loaded.Sheet, loaded.Icon,
                                     attacking: combatant.State == CombatantState.Attacking));
    }

    private CombatSession(IReadOnlyList<Combatant> combatants, CombatSetupResult setup,
                          Func<int, int> dice, UAF.Rules.Surprise surprise)
    {
        this.combatants = [.. combatants];
        this.dice = dice;
        this.surprise = surprise;
        Setup = setup;
        Round = new CombatRound(i => this.combatants[i].State);
        Renderer = new CombatRenderer();
        Cursor = new AimCursor();
        Menu = new Menu();
    }

    /// <summary>The map and where everybody started.</summary>
    public CombatSetupResult Setup { get; }

    /// <summary>The grid.</summary>
    public CombatMap Map => Setup.Map;

    /// <summary>The round clock and turn queue.</summary>
    public CombatRound Round { get; }

    /// <summary>The screen.</summary>
    public CombatRenderer Renderer { get; }

    /// <summary>The targeting cursor.</summary>
    public AimCursor Cursor { get; }

    /// <summary>The player's action menu, rebuilt each time a player-run combatant acts.</summary>
    public Menu Menu { get; }

    /// <summary>What the menu is currently asking for.</summary>
    public CombatMenuMode Mode { get; private set; } = CombatMenuMode.Command;

    /// <summary>Everybody in the fight, party first.</summary>
    public IReadOnlyList<Combatant> Combatants => combatants;

    /// <summary>Whose turn it is, or <see cref="CombatMap.NoDude"/>.</summary>
    public int Acting { get; private set; } = CombatMap.NoDude;

    /// <summary>How it ended, or <see cref="CombatOutcome.Running"/>.</summary>
    public CombatOutcome Outcome { get; private set; } = CombatOutcome.Running;

    /// <summary>Whether the fight is still going.</summary>
    public bool IsActive => Outcome == CombatOutcome.Running;

    /// <summary>Whether the acting combatant is waiting for the player.</summary>
    public bool AwaitingPlayer =>
        IsActive && Acting != CombatMap.NoDude && !combatants[Acting].IsAuto;

    /// <summary>What happened last, for the message line.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>
    /// Builds a fight from a combat event.
    /// </summary>
    /// <param name="party">The party, which goes in first and keeps its order.</param>
    /// <param name="dice">A roller: given sides, returns 1..sides.</param>
    public static CombatSession Begin(CombatEvent combat, Map level,
                                      IReadOnlyList<WallSetSlot> wallSets,
                                      int levelX, int levelY, Facing facing,
                                      IReadOnlyList<Combatant> party,
                                      Func<string, MonsterRecord?> monsterInfo,
                                      Func<int, int> dice,
                                      Func<string, Surface?>? art = null,
                                      IReadOnlyDictionary<string, (Surface Sheet,
                                                                   CombatantIcon Icon)>? partyIcons
                                          = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(dice);

        // A monster's footprint is measured off its loaded icon, so the art has to be resolved
        // before placement rather than at draw time.
        var icons = new Dictionary<string, (Surface Sheet, CombatantIcon Icon)>();
        CombatantIcon SizeOf(MonsterRecord record)
        {
            if (art is null || record.Icon is null)
            {
                return new CombatantIcon(1, 1);
            }

            if (!icons.TryGetValue(record.Name, out var loaded))
            {
                var found = CombatIcons.Load(record.Icon.FileName, record.Icon.NumFrames, art);
                if (found is null)
                {
                    return new CombatantIcon(1, 1);
                }

                loaded = found.Value;
                icons[record.Name] = loaded;
            }

            return loaded.Icon;
        }

        var all = EncounterBuilder.Build(combat, party, RollDice(dice), monsterInfo,
                                         iconSize: SizeOf);

        var setup = CombatSetup.Begin(level, wallSets, levelX, levelY, facing, all,
                                      (EncounterDirection)combat.Direction,
                                      (EncounterDistance)combat.Distance,
                                      outdoor: combat.Outdoors != 0);

        var session = new CombatSession(all, setup, dice, (UAF.Rules.Surprise)combat.Surprise);

        // Centre on the party before the first frame. The reference does this as combat opens
        // (PlaceCursorOnCurrentDude); without it the very first frame looks at the map's corner,
        // which is a screenful of empty floor a long way from the fight.
        session.Cursor.CenterOn(all.FirstOrDefault(c => c.IsFriendly) ?? all[0]);
        session.Renderer.ScrollX = setup.PartyX - (session.VisibleTilesAcross / 2);
        session.Renderer.ScrollY = setup.PartyY - (session.VisibleTilesDown / 2);
        foreach (var (name, loaded) in icons)
        {
            session.icons[name] = loaded;
        }

        if (partyIcons is not null)
        {
            foreach (var (name, loaded) in partyIcons)
            {
                session.icons[name] = loaded;
            }
        }
        session.BeginRound();
        return session;
    }

    /// <summary>Adapts a single-die roller to <c>RollDice(sides, times, bonus)</c>.</summary>
    private static Func<int, int, int, int> RollDice(Func<int, int> dice) =>
        (sides, times, bonus) =>
        {
            if (sides <= 0 || times <= 0)
            {
                return bonus;
            }

            int total = bonus;
            for (int i = 0; i < times; i++)
            {
                total += dice(sides);
            }
            return total;
        };

    /// <summary>
    /// Advances the fight, either by running a computer turn or by acting on the player's input.
    /// </summary>
    /// <param name="input">
    /// The player's key, or null to let the fight run itself — which is what a caller does between
    /// player turns.
    /// </param>
    /// <returns>Whether anything changed.</returns>
    public bool Update(InputEvent? input = null)
    {
        if (!IsActive)
        {
            return false;
        }

        if (Acting == CombatMap.NoDude)
        {
            return Advance();
        }

        var actor = combatants[Acting];

        if (actor.IsAuto)
        {
            RunAutoTurn(actor);
            return true;
        }

        return input is { Kind: InputEventKind.KeyDown } key && HandlePlayerKey(actor, key.Key);
    }

    /// <summary>
    /// Picks the next combatant, rolling the round over when the current one is spent.
    /// </summary>
    private bool Advance()
    {
        Acting = Round.Advance(i => combatants[i].IsDone(),
                               i => combatants[i].Initiative, combatants.Count);

        if (Acting != CombatMap.NoDude)
        {
            Round.Queue.NotStartOfTurn();
            OnTurnBegan();
            return true;
        }

        // Nobody left this round. The fight is called off only when nothing has attacked at all
        // for twenty rounds -- one combatant still swinging keeps everybody in it.
        Round.CheckIdleTime(combatants.Select(c => c.LastAttackRound));
        if (CheckOutcome() != CombatOutcome.Running)
        {
            return true;
        }

        BeginRound();
        return true;
    }

    private void BeginRound()
    {
        Round.BeginRound();
        Round.IsStartingNewRound = false;

        CombatUpkeep.CheckDyingCombatants(combatants);
        CombatUpkeep.CheckMorale(combatants);

        foreach (var c in combatants)
        {
            // Initiative is rolled fresh every round (DetermineCombatInitiative, called from
            // StartNewRound). Without it a combatant sits at zero and the round's 1..22 walk never
            // reaches it -- which is exactly what kept every monster out of the first fight this
            // session ran end to end.
            c.Initiative = UAF.Rules.Initiative.Roll(surprise, c.IsFriendly, dice(10));

            c.TurnIsDone = true;
            c.BeginRound(Math.Max(1, c.TotalAttacks), isAuto: c.IsAuto);
        }

        // Surprise is a first-round effect only; the reference clears it after rolling
        // (Combatants.cpp:1500).
        surprise = UAF.Rules.Surprise.Neither;

        Acting = CombatMap.NoDude;
        CheckOutcome();
    }

    /// <summary>Sets up the screen and, for a player, the menu.</summary>
    private void OnTurnBegan()
    {
        var actor = combatants[Acting];
        Renderer.EnsureVisible(Map, actor.X, actor.Y, VisibleTilesAcross, VisibleTilesDown);
        Cursor.CenterOn(actor);

        Mode = CombatMenuMode.Command;
        CombatMenu.Build(Menu, OptionsFor(actor), acting: !actor.IsAuto);

        // Name whose turn it is, so a player knows who the menu belongs to. The reference puts
        // this in the same text box, through FormatCombatMoveText and the menu title.
        Message = $"{actor.Name}'s turn.";
    }

    /// <summary>
    /// What the acting combatant may do.
    /// </summary>
    /// <remarks>
    /// <b><c>CanBandage</c> is just <c>!IsDone()</c> in the reference</b> (<c>Combatant.cpp:7074</c>)
    /// — the entry is offered whenever the combatant can act, and the action itself finds a target
    /// or does nothing. Gating the menu on somebody being dying would be stricter than the
    /// original.
    /// <para>
    /// Casting and turning stay off because neither has an implementation: offering a command that
    /// silently does nothing is worse than greying it.
    /// </para>
    /// </remarks>
    private CombatOptions OptionsFor(Combatant actor) => new(
        CanMove: actor.Movement < actor.MaxMovement,
        CanCast: false,
        ZoneAllowsMagic: true,
        CanTurnUndead: false,
        CanGuard: true,
        CanDelay: true,
        CanBandage: !actor.IsDone(),
        IsEditor: false,
        SpecialActionName: string.Empty);

    private void RunAutoTurn(Combatant actor)
    {
        var plan = MonsterAi.Think(actor, combatants, Map, CanAttack);

        switch (plan.Decision)
        {
            case AiDecision.Attack:
                Strike(actor, combatants[plan.Target]);
                break;

            case AiDecision.Move:
                Walk(actor, plan.Path!);
                break;

            case AiDecision.LeaveMap:
                actor.Status = CharacterStatus.Fled;
                Map.Remove(actor.X, actor.Y, actor.Icon.Width, actor.Icon.Height);
                Message = $"{actor.Name} has fled.";
                break;

            case AiDecision.Flee:
                Walk(actor, plan.Path!);
                break;

            default:
                Message = $"{actor.Name} guards.";
                break;
        }

        EndTurn(actor, plan.Decision == AiDecision.Guard
            ? CombatantState.Guarding
            : CombatantState.None);
    }

    private bool HandlePlayerKey(Combatant actor, VirtualKey key)
    {
        // Manual aiming steers with the arrows, so the menu cannot also use them.
        if (Mode == CombatMenuMode.AimingManual && Steer(key))
        {
            return true;
        }

        switch (key)
        {
            case VirtualKey.Left:
                Menu.PrevItem();
                return true;

            case VirtualKey.Right:
                Menu.NextItem();
                return true;

            case VirtualKey.Return:
                return Mode switch
                {
                    CombatMenuMode.Aiming => ChooseAim(actor, (AimCommand)(Menu.ActiveItem + 1)),
                    CombatMenuMode.AimingManual =>
                        ChooseAimManual(actor, (AimManualCommand)(Menu.ActiveItem + 1)),
                    _ => Choose(actor, CombatMenu.At(Menu.ActiveItem)),
                };

            default:
                return false;
        }
    }

    /// <summary>
    /// Moves the cursor a square, while manual aiming (<c>COMBAT_AIM_MANUAL_MENU_DATA</c>'s arrow
    /// cases, <c>RunEvent.cpp:20110</c>).
    /// </summary>
    /// <returns>Whether the key was a direction.</returns>
    private bool Steer(VirtualKey key)
    {
        var (dx, dy) = key switch
        {
            VirtualKey.Left => (-1, 0),
            VirtualKey.Right => (1, 0),
            VirtualKey.Up => (0, -1),
            VirtualKey.Down => (0, 1),
            _ => (0, 0),
        };

        if ((dx, dy) == (0, 0))
        {
            return false;
        }

        Cursor.MoveBy(Map, dx, dy);
        Renderer.EnsureVisible(Map, Cursor.X, Cursor.Y, VisibleTilesAcross, VisibleTilesDown);
        return true;
    }

    /// <summary>
    /// The AIM submenu (<c>COMBAT_AIM_MENU_DATA::OnKeypress</c>, <c>RunEvent.cpp:19952</c>).
    /// </summary>
    /// <remarks>
    /// <b>TARGET only commits when the attack is actually possible</b>; otherwise the reference
    /// clears the target and stays in the menu, so a player pointing at something unreachable is
    /// told rather than silently having their turn end.
    /// </remarks>
    private bool ChooseAim(Combatant actor, AimCommand command)
    {
        switch (command)
        {
            case AimCommand.Next:
                Cursor.Next(combatants, actor);
                Renderer.EnsureVisible(Map, Cursor.X, Cursor.Y,
                                       VisibleTilesAcross, VisibleTilesDown);
                return true;

            case AimCommand.Previous:
                Cursor.Previous(combatants, actor);
                Renderer.EnsureVisible(Map, Cursor.X, Cursor.Y,
                                       VisibleTilesAcross, VisibleTilesDown);
                return true;

            case AimCommand.Manual:
                Mode = CombatMenuMode.AimingManual;
                CombatMenu.BuildAimManual(Menu);
                Message = "Move the cursor.";
                return true;

            case AimCommand.Center:
                Renderer.EnsureVisible(Map, actor.X, actor.Y,
                                       VisibleTilesAcross, VisibleTilesDown);
                return true;

            case AimCommand.Target:
                return Commit(actor);

            default:
                Mode = CombatMenuMode.Command;
                Cursor.CenterOn(actor);
                CombatMenu.Build(Menu, OptionsFor(actor));
                return true;
        }
    }

    /// <summary>
    /// The manual-aim submenu (<c>COMBAT_AIM_MANUAL_MENU_DATA::OnKeypress</c>,
    /// <c>RunEvent.cpp:20052</c>).
    /// </summary>
    private bool ChooseAimManual(Combatant actor, AimManualCommand command)
    {
        if (command == AimManualCommand.Target)
        {
            return Commit(actor);
        }

        Mode = CombatMenuMode.Command;
        Cursor.CenterOn(actor);
        CombatMenu.Build(Menu, OptionsFor(actor));
        return true;
    }

    /// <summary>Attacks whatever the cursor is on, if that is possible.</summary>
    private bool Commit(Combatant actor)
    {
        int target = Map.OccupantAt(Cursor.X, Cursor.Y, ignoreCombatant: actor.Index);

        if (target == CombatMap.NoDude)
        {
            Message = "Nothing there.";
            return true;
        }

        if (!CanAttack(actor, target))
        {
            Message = $"Cannot reach {combatants[target].Name}.";
            return true;
        }

        Strike(actor, combatants[target]);
        Mode = CombatMenuMode.Command;
        EndTurn(actor, CombatantState.None);
        return true;
    }

    /// <summary>
    /// Carries out a chosen command.
    /// </summary>
    /// <remarks>
    /// <b>Only a few commands do anything yet.</b> MOVE walks toward the cursor, AIM attacks what
    /// it is on, GUARD and END finish the turn. USE, CAST, TURN, BANDAGE, QUICK, DELAY, VIEW,
    /// SPEED and the special action are offered by the menu rules where the reference offers them,
    /// but have no implementation — so they are refused here rather than silently ending the turn.
    /// </remarks>
    private bool Choose(Combatant actor, CombatCommand command)
    {
        switch (command)
        {
            case CombatCommand.Move:
            {
                var finder = new CombatPathFinder(Map) { IgnoreCombatant = actor.Index };
                var route = finder.To(actor.X, actor.Y, Cursor.X, Cursor.Y);
                if (route is null)
                {
                    Message = "No path there.";
                    return true;
                }

                Walk(actor, route);
                EndTurn(actor, CombatantState.None);
                return true;
            }

            case CombatCommand.Aim:
                // Opens the submenu rather than attacking outright: the player picks the target.
                Mode = CombatMenuMode.Aiming;
                CombatMenu.BuildAim(Menu);
                Cursor.Next(combatants, actor);
                Renderer.EnsureVisible(Map, Cursor.X, Cursor.Y,
                                       VisibleTilesAcross, VisibleTilesDown);
                return true;

            case CombatCommand.Guard:
                Message = $"{actor.Name} guards.";
                EndTurn(actor, CombatantState.Guarding);
                return true;

            case CombatCommand.Bandage:
            {
                // The reference stabilises the worst-hurt dying combatant and sets the bandager's
                // state so the status line names what it did (COMBAT_DATA::Bandage,
                // Combatants.cpp:1271). It does nothing at all when nobody is dying.
                var helped = CombatUpkeep.Bandage(combatants);
                Message = helped is null
                    ? "Nobody needs bandaging."
                    : $"{actor.Name} bandages {helped.Name}.";

                if (helped is not null)
                {
                    EndTurn(actor, CombatantState.Bandaging);
                }

                return true;
            }

            case CombatCommand.End:
                EndTurn(actor, CombatantState.None);
                return true;

            default:
                Message = $"{CombatMenu.Labels[(int)command - 1]} is not implemented.";
                return true;
        }
    }

    private void Walk(Combatant actor, CombatPath route)
    {
        var remaining = route.Steps.ToList();
        paths[actor.Index] = remaining;

        int steps = 0;
        while (remaining.Count > 0
               && CombatMovement.TakeNextStep(actor, Map, remaining, canAttack: CanAttack)
                  == MoveOutcome.Moved)
        {
            steps++;
        }

        Message = steps > 0 ? $"{actor.Name} moves." : $"{actor.Name} cannot move.";
    }

    private void Strike(Combatant actor, Combatant target)
    {
        var result = Attack.Resolve(actor, target, Map, dice, new DamageRoll(1, 8, 0),
                                    new ReadiedWeapon(WeaponClass.HandCutting, Range: 1),
                                    attackerThac0: 18, targetArmorClass: 6,
                                    currentRound: Round.Round);

        if (!result.Happened)
        {
            Message = $"{actor.Name} cannot attack.";
            return;
        }

        if (!result.Hit)
        {
            Message = $"{actor.Name} misses {target.Name}.";
            return;
        }

        target.HitPoints = Attack.ApplyDamage(target, target.HitPoints, result.Damage,
                                              target.MaxHitPoints);
        Message = $"{actor.Name} hits {target.Name} for {result.Damage}.";

        if (!target.IsOnCombatMap())
        {
            Map.Remove(target.X, target.Y, target.Icon.Width, target.Icon.Height);
            Message += $" {target.Name} falls.";
        }
    }

    private void EndTurn(Combatant actor, CombatantState state)
    {
        actor.EndTurn(Round.Queue, state);
        paths.Remove(actor.Index);
        Acting = CombatMap.NoDude;
        CheckOutcome();
    }

    private bool CanAttack(Combatant attacker, int target)
    {
        var t = target >= 0 && target < combatants.Count ? combatants[target] : null;
        return Targeting.CanAttack(attacker, t, Map,
                                   new ReadiedWeapon(WeaponClass.HandCutting, Range: 1))
               == AttackRefusal.None;
    }

    /// <summary>
    /// Decides whether the fight is over.
    /// </summary>
    /// <remarks>
    /// A side is beaten when nobody on it is still on the map — which covers dead, unconscious,
    /// fled and gone alike, because <see cref="Combatant.IsOnCombatMap"/> names them all.
    /// </remarks>
    private CombatOutcome CheckOutcome()
    {
        if (Outcome != CombatOutcome.Running)
        {
            return Outcome;
        }

        bool anyFriend = combatants.Any(c => c.IsFriendly && c.IsOnCombatMap());
        bool anyFoe = combatants.Any(c => !c.IsFriendly && c.IsOnCombatMap());

        Outcome = !anyFoe ? CombatOutcome.PartyWon
                : !anyFriend ? CombatOutcome.PartyLost
                : Round.IsOver ? CombatOutcome.Stalemate
                : CombatOutcome.Running;

        return Outcome;
    }

    /// <summary>
    /// Where on screen the map is drawn, which decides both the renderer's origin and how many
    /// squares are visible.
    /// </summary>
    /// <remarks>
    /// <b>The reference draws combat on its own full screen; this port draws it in the dungeon
    /// viewport</b>, so <c>CombatScreenX/Y</c> (14, 16) are not the right origin — the view sits
    /// wherever <c>VIEWPORT_RECT</c> puts it, which in `SomethingWild` is (48,54). Leaving the
    /// default meant terrain drawn 34 pixels up and left of where it belonged, clipped rather than
    /// aligned, and a cursor that fell outside the view and was silently dropped.
    /// </remarks>
    public SurfaceRect ViewArea
    {
        get => viewArea;
        set
        {
            viewArea = value;
            Renderer.OriginX = value.Left;
            Renderer.OriginY = value.Top;
        }
    }

    private SurfaceRect viewArea = new(0, 0, 10 * CombatMap.TileWidth, 8 * CombatMap.TileHeight);

    /// <summary>How many whole squares fit across the view.</summary>
    internal int VisibleTilesAcross => Math.Max(1, viewArea.Width / CombatMap.TileWidth);

    /// <inheritdoc cref="VisibleTilesAcross"/>
    internal int VisibleTilesDown => Math.Max(1, viewArea.Height / CombatMap.TileHeight);

    /// <summary>
    /// Draws the map, the cursor and everybody on it.
    /// </summary>
    /// <param name="cursorArt">
    /// The cursor frame, or null to leave it undrawn. Only shown while the player is being asked
    /// for orders — a computer turn has nothing to aim.
    /// </param>
    /// <remarks>
    /// Order matters: terrain, then the cursor, then combatants. The reference draws the cursor
    /// alpha-blended and then redraws the occupant's sprite over it (<c>Combatants.cpp:4772</c>),
    /// which is the same result as drawing combatants last.
    /// </remarks>
    public void Render(Surface screen, Surface? sheet, SurfaceRect area,
                       Func<Combatant, (Surface Sheet, SurfaceRect Source)?>? iconFor = null,
                       Surface? cursorArt = null)
    {
        ArgumentNullException.ThrowIfNull(screen);

        // Keep the origin and the visible extent in step with wherever the caller is drawing.
        if (area != viewArea)
        {
            ViewArea = area;
            Renderer.EnsureVisible(Map, Cursor.X, Cursor.Y, VisibleTilesAcross, VisibleTilesDown);
        }

        if (sheet is not null)
        {
            Renderer.DrawTerrain(screen, Map, sheet, area);
        }

        if (cursorArt is not null && AwaitingPlayer)
        {
            int under = Map.OccupantAt(Cursor.X, Cursor.Y);
            Renderer.DrawCursor(screen, cursorArt, Cursor.X, Cursor.Y, area,
                                under != CombatMap.NoDude ? combatants[under] : null);
        }

        Renderer.DrawCombatants(screen, combatants, iconFor ?? IconFor, area);
    }
}
