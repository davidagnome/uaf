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

        CombatMenu.Build(Menu, OptionsFor(actor), acting: !actor.IsAuto);
    }

    /// <summary>
    /// What the acting combatant may do.
    /// </summary>
    /// <remarks>
    /// Guarding and delaying are always offered; the rest follow from what is ported. Turning
    /// undead, bandaging and casting stay off because none of the three has an implementation yet —
    /// offering a command that does nothing is worse than greying it.
    /// </remarks>
    private CombatOptions OptionsFor(Combatant actor) => new(
        CanMove: actor.Movement < actor.MaxMovement,
        CanCast: false,
        ZoneAllowsMagic: true,
        CanTurnUndead: false,
        CanGuard: true,
        CanDelay: true,
        CanBandage: combatants.Any(c => c.Status == CharacterStatus.Dying),
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
        switch (key)
        {
            case VirtualKey.Left:
                Menu.PrevItem();
                return true;

            case VirtualKey.Right:
                Menu.NextItem();
                return true;

            case VirtualKey.Return:
                return Choose(actor, CombatMenu.At(Menu.ActiveItem));

            default:
                return false;
        }
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
            {
                int target = Cursor.Next(combatants, actor);
                if (target == actor.Index)
                {
                    Message = "No target.";
                    return true;
                }

                if (!CanAttack(actor, target))
                {
                    Message = $"Cannot reach {combatants[target].Name}.";
                    return true;
                }

                Strike(actor, combatants[target]);
                EndTurn(actor, CombatantState.None);
                return true;
            }

            case CombatCommand.Guard:
                Message = $"{actor.Name} guards.";
                EndTurn(actor, CombatantState.Guarding);
                return true;

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

    /// <summary>How many squares fit across the combat view, at the default screen size.</summary>
    private const int VisibleTilesAcross = 10;

    /// <inheritdoc cref="VisibleTilesAcross"/>
    private const int VisibleTilesDown = 8;

    /// <summary>Draws the map and everybody on it.</summary>
    public void Render(Surface screen, Surface? sheet, SurfaceRect area,
                       Func<Combatant, (Surface Sheet, SurfaceRect Source)?>? iconFor = null)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (sheet is not null)
        {
            Renderer.DrawTerrain(screen, Map, sheet, area);
        }

        Renderer.DrawCombatants(screen, combatants, iconFor ?? IconFor, area);
    }
}
