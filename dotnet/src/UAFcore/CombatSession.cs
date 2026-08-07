using UAF.Media;
using UAF.Scripting;
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

    /// <summary>
    /// The auras placed on this map, and the reference stack their scripts run under.
    /// </summary>
    /// <remarks>
    /// <b>Per fight, like the reference's.</b> <c>m_auras</c> and <c>m_nextAuraID</c> are
    /// <c>COMBAT_DATA</c> members reset with the rest of it, so ids start at 1 in every fight and
    /// no aura outlives the combat that made it.
    /// <para>
    /// Each aura's cell mask is one byte per square of this map — which is the reference's
    /// <c>MAX_TERRAIN_WIDTH × MAX_TERRAIN_HEIGHT</c>, since the combat map <i>is</i> that size.
    /// </para>
    /// </remarks>
    public AuraStore Auras => auras ??= new AuraStore(Map.Width * Map.Height);

    private AuraStore? auras;

    /// <summary>How it ended, or <see cref="CombatOutcome.Running"/>.</summary>
    public CombatOutcome Outcome { get; private set; } = CombatOutcome.Running;

    /// <summary>Whether the fight is still going.</summary>
    public bool IsActive => Outcome == CombatOutcome.Running;

    /// <summary>Whether the acting combatant is waiting for the player.</summary>
    public bool AwaitingPlayer =>
        IsActive && Acting != CombatMap.NoDude && !combatants[Acting].IsAuto;

    /// <summary>What happened last, for the message line.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>Spells begun but not yet resolved.</summary>
    public PendingSpellList Pending { get; } = new();

    /// <summary>Spells left standing on the map.</summary>
    public LingeringSpellList Lingering { get; } = new();

    /// <summary>
    /// Whether the encounter forbids magic (<c>CombatEvent.NoMagic</c>). Greys out CAST.
    /// </summary>
    public bool NoMagic { get; private set; }

    /// <summary>Looks up a spell's record, for its casting time. Null when the design has none.</summary>
    private Func<string, SpellRecord?>? spellInfo;

    /// <summary>
    /// Stands in for the <c>TURN_ATTEMPT</c> script: which undead categories a cleric reaches, and
    /// how many of each.
    /// </summary>
    /// <remarks>
    /// <b>Turning is entirely design-scripted in the reference</b> — the AD&amp;D table is dead
    /// code (see <see cref="TurnUndead"/>) — so without GPDL there is nothing to ask and nothing
    /// is turned. Settable so a test, or a later script layer, can supply the answer.
    /// </remarks>
    public Func<Combatant, IReadOnlyDictionary<string, int>>? TurnAttempt
    {
        get => turnAttempt;
        set => turnAttempt = value;
    }

    private Func<Combatant, IReadOnlyDictionary<string, int>>? turnAttempt;

    /// <summary>
    /// Looks up a monster's record, for the AI's view of its natural attacks.
    /// </summary>
    public Func<string, MonsterRecord?>? MonsterInfo { get; set; }

    /// <summary>
    /// What the AI is told this combatant can attack with, or null to fall back to the simpler
    /// rule.
    /// </summary>
    /// <remarks>
    /// Null when neither database is available — a session built by a test without design data
    /// still fights, on <see cref="MonsterAi"/>'s own rule rather than the script's ordering.
    /// </remarks>
    private IReadOnlyList<AiWeapon>? WeaponsFor(Combatant actor)
    {
        if (ItemInfo is null && MonsterInfo is null)
        {
            return null;
        }

        return AiWeapons.For(actor, MonsterInfo?.Invoke(actor.Name), ItemInfo);
    }

    /// <summary>The design's turning record for a combatant. Null means it is not undead.</summary>
    public Func<Combatant, TurnData?>? TurnDataOf { get; set; }

    private TurnData? TurnDataFor(Combatant combatant) => TurnDataOf?.Invoke(combatant);

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
                                          = null,
                                      Func<string, SpellRecord?>? spellInfo = null)
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

        var session = new CombatSession(all, setup, dice, (UAF.Rules.Surprise)combat.Surprise)
        {
            NoMagic = combat.NoMagic != 0,
            spellInfo = spellInfo,
        };

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

        // Space takes a party member back off automatic, and is handled before the state check
        // -- the reference's own comment is "need to handle this regardless of state".
        if (input is { Kind: InputEventKind.KeyDown, Key: VirtualKey.Space } && TakeBackControl())
        {
            return true;
        }

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
                               i => combatants[i].Initiative, combatants.Count,
                               onInitiative: init => ServicePendingSpells(0, init));

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

    /// <summary>
    /// Ticks the casting clock, putting back on the queue anyone whose spell has landed
    /// (<c>SpellActivate</c>, <c>Combatant.cpp:867</c>).
    /// </summary>
    /// <remarks>
    /// <b>Activation does not resolve the spell — it gives the caster its turn back.</b> The
    /// reference clears <c>turnIsDone</c> and pushes the caster onto the tail of the queue; the
    /// spell's targets are chosen and its effects applied when that turn comes round, still in
    /// <see cref="CombatantState.Casting"/>. That is why a spell three rounds in the making
    /// interrupts the initiative order when it lands.
    /// </remarks>
    private void ServicePendingSpells(int roundInc, int initiative) =>
        Pending.Service(roundInc, initiative, Round.Round, spell =>
        {
            var caster = combatants[spell.Caster];
            caster.PendingSpellKey = -1;
            caster.TurnIsDone = false;
            Round.Queue.PushTail(caster.Index, affectStats: true);
        });

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

        // A round has passed, which forces through any spell waiting on an initiative slot that
        // the round just finished never reached (Combatants.cpp:6923, roundDelta > 0). After the
        // fresh turns are handed out, so the caster's restored turn is the one it keeps.
        ServicePendingSpells(roundInc: 1, Round.CurrentInitiative);

        // Anyone standing in a cloud is caught by it now, at the head of the round.
        ApplyLingeringSpells();

        Acting = CombatMap.NoDude;
        CheckOutcome();
    }

    /// <summary>Sets up the screen and, for a player, the menu.</summary>
    private void OnTurnBegan()
    {
        var actor = combatants[Acting];
        Renderer.EnsureVisible(Map, actor.X, actor.Y, VisibleTilesAcross, VisibleTilesDown);
        Cursor.CenterOn(actor);

        // A caster whose spell has come due resumes its turn to resolve it, not to take a fresh
        // one (RunEvent.cpp:17104). That happens here rather than through the menu.
        if (actor.State == CombatantState.Casting && actor.SpellBeingCast is not null
            && !actor.IsSpellPending)
        {
            ResolveSpell(actor);
            return;
        }

        Mode = CombatMenuMode.Command;
        CombatMenu.Build(Menu, OptionsFor(actor), acting: !actor.IsAuto);

        // Name whose turn it is, so a player knows who the menu belongs to. The reference puts
        // this in the same text box, through FormatCombatMoveText and the menu title.
        Message = $"{actor.Name}'s turn.";
    }

    /// <summary>
    /// Works out who a cast lands on and applies it
    /// (<c>TASK_CombatActivateSpell</c> and what it reaches, <c>RunEvent.cpp:15522</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three of the ten targeting modes need the player to pick individual combatants —
    /// <c>SelectedByCount</c>, <c>TouchedTargets</c> and <c>SelectByHitDice</c> — and that menu
    /// (<c>COMBAT_SPELL_AIM_MENU_DATA</c>) is not ported, so those say so rather than guessing.
    /// The rest need no picking or need only a square, which the aim cursor already is.
    /// </para>
    /// <para>
    /// <b>The area shapes are laid out from the caster towards the cursor.</b> That direction is
    /// what rotates the rectangle and cone; a cast on the caster's own square has no direction and
    /// the geometry degenerates, which is the reference's behaviour too.
    /// </para>
    /// </remarks>
    private void ResolveSpell(Combatant actor)
    {
        string spellId = actor.SpellBeingCast!;
        var record = spellInfo?.Invoke(spellId);
        actor.SpellBeingCast = null;

        if (record is null)
        {
            Message = $"{actor.Name}'s {spellId} fizzles.";
            EndTurn(actor, CombatantState.None);
            return;
        }

        var targeting = (SpellTargeting)record.Targeting;
        var setup = SpellTargets.Setup(targeting, targets: 1, range: 0, width: 1, height: 1,
                                       partySize: combatants.Count(c => c.IsFriendly));

        List<Combatant>? targets = TargetsFor(actor, targeting, setup);
        if (targets is null)
        {
            // The three unit-picking modes hand the cursor to the player, who names each target in
            // turn (COMBAT_SPELL_AIM_MENU_DATA). A computer-run caster has no such menu, so it
            // takes what it can reach in the order the combatant list holds -- the reference's AI
            // path for this is Forth and is not ported.
            if (actor.IsAuto)
            {
                targets = [.. AutoPick(actor, targeting, setup).Select(i => combatants[i])];
            }
            else
            {
                BeginSpellTargeting(actor, record, targeting, setup);
                return;
            }
        }

        int key = nextActiveSpellKey++;
        var hits = SpellResolution.InvokeAll(actor, targets, record, dice,
                                             elapsedMinutes: Round.Round,
                                             activeSpellKey: key, casterLevel: 1);

        LeaveLingering(actor, record, key);

        int landed = hits.Count(h => h.Outcome == SpellOutcome.Applied);
        Message = targets.Count == 0
            ? $"{actor.Name} casts {record.Name} at nothing."
            : $"{actor.Name} casts {record.Name}: {landed} of {targets.Count} affected.";

        EndTurn(actor, CombatantState.None);
    }

    /// <summary>One key per cast, so every target of it expires together.</summary>
    private int nextActiveSpellKey;

    /// <summary>The squares the last area cast covered, so a lingering one can keep them.</summary>
    private List<(int X, int Y)>? lastAreaSquares;

    /// <summary>
    /// Leaves an area cast standing on the map, when the spell says it should.
    /// </summary>
    /// <remarks>
    /// <b>Only a combat cast lingers.</b> The reference stores
    /// <c>IsCombatActive() ? pSdata-&gt;Lingers : FALSE</c> (<c>Char.cpp:16324</c>) — a spell cast
    /// in camp leaves nothing behind however its record is authored, because there is no map to
    /// leave it on. Every cast here is a combat cast, so the flag alone decides it.
    /// </remarks>
    private void LeaveLingering(Combatant actor, SpellRecord record, int key)
    {
        if (record.Lingers == 0 || lastAreaSquares is not { Count: > 0 } squares)
        {
            lastAreaSquares = null;
            return;
        }

        var spell = Lingering.Add(key, record.Name, actor.Index,
                                  record.LingerOnceOnly != 0, squares);

        // Whoever the cast just hit counts as already caught, so a once-only spell does not catch
        // them again at the head of the next round.
        foreach (int caught in SpellArea.CombatantsIn(Map, squares))
        {
            spell.Catch(caught);
        }

        lastAreaSquares = null;
    }

    /// <summary>
    /// Catches whoever is standing in a lingering spell at the head of a round
    /// (<c>Combatants.cpp:4605</c>).
    /// </summary>
    /// <remarks>
    /// <b>This runs per round, not per move.</b> A combatant that walks into a cloud and out again
    /// within one round is never caught by it; one that ends the round standing in it is caught at
    /// the start of the next.
    /// </remarks>
    private void ApplyLingeringSpells()
    {
        if (Lingering.Count == 0)
        {
            return;
        }

        foreach (var c in combatants.Where(c => c.IsOnCombatMap()))
        {
            foreach (var spell in Lingering.Catch(c.Index, c.X, c.Y,
                                                  c.Icon.Width, c.Icon.Height))
            {
                if (spellInfo?.Invoke(spell.SpellId) is { } record)
                {
                    SpellResolution.Invoke(combatants[spell.Caster], c, record, dice,
                                           elapsedMinutes: Round.Round,
                                           activeSpellKey: spell.Key);
                }
            }
        }
    }

    /// <summary>Which item the USE list is sitting on.</summary>
    public int SelectedItem { get; private set; }

    /// <summary>
    /// Looks up an item's record, for its spell. Null when the design has no item database.
    /// </summary>
    public Func<string, ItemRecord?>? ItemInfo { get; set; }

    /// <summary>
    /// What a combatant may invoke: what it carries that names a spell and has a charge left.
    /// </summary>
    /// <remarks>
    /// <b>An item's spell is the whole of the USE path</b>, and it is only on the wire from design
    /// version 0.999647 — an older design's items name no spell at all, so nothing is usable. The
    /// charge test is the item instance's own count, not the database's <c>NumCharges</c>, which is
    /// only the starting figure.
    /// </remarks>
    public List<ItemInstance> UsableItems(Combatant actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return [.. actor.Items.Where(
            i => i.Charges > 0
                 && ItemInfo?.Invoke(i.ItemId) is { } record
                 && !string.IsNullOrEmpty(record.Names.SpellId))];
    }

    private void ShowSelectedItem(Combatant actor)
    {
        var items = UsableItems(actor);
        Message = SelectedItem < items.Count
            ? $"{items[SelectedItem].ItemId} ({items[SelectedItem].Charges})"
            : "Nothing to use.";
    }

    /// <summary>
    /// The USE submenu — the same shape as CAST, over items rather than spells.
    /// </summary>
    private bool ChooseItem(Combatant actor, CastCommand command)
    {
        var items = UsableItems(actor);

        switch (command)
        {
            case CastCommand.Next:
                SelectedItem = Math.Min(SelectedItem + 1, Math.Max(0, items.Count - 1));
                ShowSelectedItem(actor);
                return true;

            case CastCommand.Previous:
                SelectedItem = Math.Max(0, SelectedItem - 1);
                ShowSelectedItem(actor);
                return true;

            case CastCommand.Cast:
            {
                if (SelectedItem >= items.Count
                    || ItemInfo?.Invoke(items[SelectedItem].ItemId) is not { } record)
                {
                    return true;
                }

                var item = items[SelectedItem];
                var spell = spellInfo?.Invoke(record.Names.SpellId);

                // A charge goes whether or not the spell resolves -- the item is used either way.
                actor.SpendCharge(item);

                Mode = CombatMenuMode.Command;
                Casting.BeginFromItem(actor, record.Names.SpellId,
                                      spell?.CastingTime ?? 0,
                                      (SpellCastingTime)(spell?.CastingTimeType ?? 0),
                                      Pending, Round.Round);

                Message = $"{actor.Name} uses {item.ItemId}.";
                EndTurn(actor, CombatantState.Using);
                return true;
            }

            default:
                Mode = CombatMenuMode.Command;
                CombatMenu.Build(Menu, OptionsFor(actor));
                return true;
        }
    }

    /// <summary>The cast a player is currently choosing targets for, or null.</summary>
    public SpellTargetSelection? Selecting { get; private set; }

    private SpellRecord? selectingSpell;

    /// <summary>
    /// Hands the cursor to the player to name each target
    /// (<c>COMBAT_SPELL_AIM_MENU_DATA</c>, <c>RunEvent.cpp:20176</c>).
    /// </summary>
    /// <remarks>
    /// The same six entries as the ordinary AIM menu — the reference builds both from
    /// <c>AimMenuData</c>. What differs is that TARGET does not end the turn: it takes a target,
    /// re-titles the menu with how many are still wanted, and steps the cursor on.
    /// </remarks>
    private void BeginSpellTargeting(Combatant actor, SpellRecord record,
                                     SpellTargeting targeting, SpellTargetingSetup setup)
    {
        var selection = new SpellTargetSelection(targeting, setup, combatants.Count);

        if (!selection.IsValid)
        {
            // The reference dies here and abandons the cast.
            Message = $"{actor.Name}'s {record.Name} cannot choose targets.";
            EndTurn(actor, CombatantState.None);
            return;
        }

        Selecting = selection;
        selectingSpell = record;
        Mode = CombatMenuMode.SpellAiming;
        CombatMenu.BuildAim(Menu);
        Cursor.Next(combatants, actor);
        Renderer.EnsureVisible(Map, Cursor.X, Cursor.Y, VisibleTilesAcross, VisibleTilesDown);
        Message = selection.RemainingText();
    }

    /// <summary>The spell-targeting submenu, which is the AIM menu doing a different job.</summary>
    private bool ChooseSpellAim(Combatant actor, AimCommand command)
    {
        var selection = Selecting!;

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
                Mode = CombatMenuMode.SpellAimingManual;
                CombatMenu.BuildAimManual(Menu);
                Message = "Move the cursor.";
                return true;

            case AimCommand.Center:
                Renderer.EnsureVisible(Map, actor.X, actor.Y,
                                       VisibleTilesAcross, VisibleTilesDown);
                return true;

            case AimCommand.Target:
                return TakeSpellTarget(actor, selection);

            default:
                // EXIT. An empty selection abandons the spell; anything else is a cast.
                if (selection.ExitWouldAbandon)
                {
                    Message = $"{actor.Name} abandons {selectingSpell!.Name}.";
                    FinishSpellTargeting(actor, cast: false);
                }
                else
                {
                    FinishSpellTargeting(actor, cast: true);
                }

                return true;
        }
    }

    /// <summary>Manual spell aiming: TARGET takes a target, EXIT goes back to the menu.</summary>
    private bool ChooseSpellAimManual(Combatant actor, AimManualCommand command)
    {
        if (command == AimManualCommand.Target)
        {
            return TakeSpellTarget(actor, Selecting!);
        }

        Mode = CombatMenuMode.SpellAiming;
        CombatMenu.BuildAim(Menu);
        Message = Selecting!.RemainingText();
        return true;
    }

    /// <summary>Takes whatever the cursor is on, and finishes when nothing more is wanted.</summary>
    private bool TakeSpellTarget(Combatant actor, SpellTargetSelection selection)
    {
        int dude = Map.OccupantAt(Cursor.X, Cursor.Y);

        if (dude == CombatMap.NoDude)
        {
            Message = "Nothing there.";
            return true;
        }

        var target = combatants[dude];
        int distance = CombatMap.Distance(actor.X, actor.Y, target.X, target.Y);

        if (!selection.Add(dude, target.HitDice, distance))
        {
            Message = $"Cannot target {target.Name}.";
            return true;
        }

        if (selection.AllChosen)
        {
            FinishSpellTargeting(actor, cast: true);
            return true;
        }

        Message = selection.RemainingText();
        Cursor.Next(combatants, actor);
        Renderer.EnsureVisible(Map, Cursor.X, Cursor.Y, VisibleTilesAcross, VisibleTilesDown);
        return true;
    }

    private void FinishSpellTargeting(Combatant actor, bool cast)
    {
        var selection = Selecting!;
        var record = selectingSpell!;
        Selecting = null;
        selectingSpell = null;
        Mode = CombatMenuMode.Command;

        if (cast)
        {
            var targets = selection.Targets.Select(i => combatants[i]).ToList();
            var hits = SpellResolution.InvokeAll(actor, targets, record, dice,
                                                 elapsedMinutes: Round.Round,
                                                 activeSpellKey: nextActiveSpellKey++,
                                                 casterLevel: 1);

            int landed = hits.Count(h => h.Outcome == SpellOutcome.Applied);
            Message = $"{actor.Name} casts {record.Name}: {landed} of {targets.Count} affected.";
        }

        EndTurn(actor, CombatantState.None);
    }

    /// <summary>
    /// What a computer-run caster targets when the mode would need a menu.
    /// </summary>
    /// <remarks>
    /// <b>Not the reference's rule</b> — its monster casting runs the design's Forth script, which
    /// is unported. This takes reachable combatants in list order, respecting the spell's friend
    /// and enemy flags and every limit the selection enforces, so a monster casting is at least
    /// legal rather than arbitrary. Replace it when the Forth VM lands.
    /// </remarks>
    private List<int> AutoPick(Combatant actor, SpellTargeting targeting,
                               SpellTargetingSetup setup)
    {
        var selection = new SpellTargetSelection(targeting, setup, combatants.Count);

        foreach (var c in combatants.Where(c => c.IsOnCombatMap()))
        {
            if (selection.AllChosen)
            {
                break;
            }

            if (!SpellTargets.CanTarget(setup.SelectingUnits, actor.IsFriendly, c.IsFriendly,
                                        canTargetFriend: true, canTargetEnemy: true))
            {
                continue;
            }

            selection.Add(c.Index, c.HitDice,
                          CombatMap.Distance(actor.X, actor.Y, c.X, c.Y));
        }

        return [.. selection.Targets];
    }

    /// <summary>
    /// Who a cast covers, or null when the mode needs a target menu this port does not have.
    /// </summary>
    private List<Combatant>? TargetsFor(Combatant actor, SpellTargeting targeting,
                                        SpellTargetingSetup setup)
    {
        switch (targeting)
        {
            case SpellTargeting.Self:
                return [actor];

            case SpellTargeting.WholeParty:
                return [.. combatants.Where(c => c.IsFriendly && c.IsOnCombatMap())];

            case SpellTargeting.SelectedByCount:
            case SpellTargeting.TouchedTargets:
            case SpellTargeting.SelectByHitDice:
                return null;

            default:
            {
                // An area, laid out from the caster towards whatever the cursor is on.
                int dirX = Math.Sign(Cursor.X - actor.X);
                int dirY = Math.Sign(Cursor.Y - actor.Y);

                var squares = targeting switch
                {
                    SpellTargeting.AreaCircle =>
                        SpellArea.Circle(Cursor.X, Cursor.Y, setup.Width, Map.Width, Map.Height),

                    SpellTargeting.AreaCone =>
                        SpellArea.Cone(actor.X, actor.Y, Cursor.X, Cursor.Y,
                                       setup.Height, setup.Width, forceNonZero: true,
                                       Map.Width, Map.Height),

                    SpellTargeting.AreaLinePickEnd =>
                        SpellArea.Line(actor.X, actor.Y, Cursor.X, Cursor.Y,
                                       Map.Width, Map.Height),

                    SpellTargeting.AreaLinePickStart =>
                        SpellArea.Line(Cursor.X, Cursor.Y,
                                       Cursor.X + (dirX * setup.MaxRange),
                                       Cursor.Y + (dirY * setup.MaxRange),
                                       Map.Width, Map.Height),

                    _ => SpellArea.Rectangle(Cursor.X, Cursor.Y, dirX, dirY,
                                             setup.Width, setup.Height, forceNonZero: true,
                                             Map.Width, Map.Height),
                };

                lastAreaSquares = squares;
                return [.. SpellArea.CombatantsIn(Map, squares).Select(i => combatants[i])];
            }
        }
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
        CanCast: Casting.CanCast(actor, noMagic: NoMagic),
        ZoneAllowsMagic: !NoMagic,
        CanTurnUndead: TurnUndead.CanTurn(actor, actor.TurnLevel),
        CanGuard: true,
        CanDelay: actor.CanDelay(),
        CanBandage: !actor.IsDone(),
        IsEditor: false,
        SpecialActionName: string.Empty);

    private void RunAutoTurn(Combatant actor)
    {
        var plan = MonsterAi.Think(actor, combatants, Map, CanAttack, WeaponsFor(actor),
                                   AiWeapons.AmmoFor(actor, ItemInfo));

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

    /// <summary>
    /// Puts a combatant on or off automatic (<c>COMBATANT::Quick</c>, <c>Combatant.cpp:7034</c>).
    /// </summary>
    /// <returns>Whether it was allowed — a spell can deny player control.</returns>
    /// <remarks>
    /// <b>Turning automatic off has to undo whatever the AI had it doing</b>: the path, the
    /// targets, the state and any spell in progress. Leaving those set would hand the player a
    /// combatant already committed to the computer's plan.
    /// </remarks>
    private bool SetAutomatic(Combatant actor, bool automatic)
    {
        if (!actor.AllowPlayerControl)
        {
            return false;
        }

        if (!automatic)
        {
            paths.Remove(actor.Index);
            actor.Target = CombatMap.NoDude;
            actor.State = CombatantState.None;
            Casting.Stop(actor, Pending, Round.Queue);
        }

        actor.IsAuto = automatic;
        return true;
    }

    /// <summary>
    /// Takes a party member back off automatic (<c>RunEvent.cpp:15129</c>).
    /// </summary>
    /// <remarks>
    /// <b>Bound to the space bar, not to a menu entry</b>, and handled before any state check —
    /// the reference's comment says "need to handle this regardless of state". It applies only to
    /// a party member currently on automatic, which is why there is no menu entry for it: the menu
    /// belongs to a combatant the player is already driving.
    /// </remarks>
    public bool TakeBackControl()
    {
        if (Acting == CombatMap.NoDude)
        {
            return false;
        }

        var actor = combatants[Acting];
        if (!actor.IsFriendly || !actor.IsAuto || !SetAutomatic(actor, false))
        {
            return false;
        }

        Message = $"{actor.Name} is off automatic.";
        CombatMenu.Build(Menu, OptionsFor(actor));
        return true;
    }

    private bool HandlePlayerKey(Combatant actor, VirtualKey key)
    {
        // Manual aiming steers with the arrows, so the menu cannot also use them.
        if (Mode is CombatMenuMode.AimingManual or CombatMenuMode.SpellAimingManual
            && Steer(key))
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
                    CombatMenuMode.ChoosingSpell =>
                        ChooseSpell(actor, (CastCommand)(Menu.ActiveItem + 1)),
                    CombatMenuMode.ChoosingItem =>
                        ChooseItem(actor, (CastCommand)(Menu.ActiveItem + 1)),
                    CombatMenuMode.SpellAiming =>
                        ChooseSpellAim(actor, (AimCommand)(Menu.ActiveItem + 1)),
                    CombatMenuMode.SpellAimingManual =>
                        ChooseSpellAimManual(actor, (AimManualCommand)(Menu.ActiveItem + 1)),
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

    /// <summary>
    /// Which spell the CAST list is sitting on. Indexes <see cref="SpellList.Castable"/>.
    /// </summary>
    public int SelectedSpell { get; private set; }

    /// <summary>The spells the CAST list is showing, or empty when not choosing one.</summary>
    public IReadOnlyList<SpellListEntry> SpellChoices =>
        Acting == CombatMap.NoDude ? [] : [.. combatants[Acting].Book.Castable];

    /// <summary>
    /// The CAST submenu (<c>CAST_MENU_DATA::OnKeypress</c>, <c>RunEvent.cpp:25754</c>).
    /// </summary>
    /// <remarks>
    /// <b>NEXT and PREV page the list, and they do not wrap.</b> The reference's
    /// <c>nextSpellPage</c> steps a pageful and stops at the ends. With no paging in this port —
    /// the whole book fits — they step one entry, and still stop rather than wrap.
    /// <para>
    /// <b>CAST on an unmemorised spell does nothing at all.</b> The reference guards the whole
    /// branch with <c>IsMemorized</c> and has no else, so the key press is simply swallowed. Here
    /// the list only ever holds memorised spells, so the guard cannot fail — but the same silence
    /// is kept for the empty book.
    /// </para>
    /// </remarks>
    private bool ChooseSpell(Combatant actor, CastCommand command)
    {
        var choices = SpellChoices;

        switch (command)
        {
            case CastCommand.Next:
                SelectedSpell = Math.Min(SelectedSpell + 1, Math.Max(0, choices.Count - 1));
                ShowSelectedSpell();
                return true;

            case CastCommand.Previous:
                SelectedSpell = Math.Max(0, SelectedSpell - 1);
                ShowSelectedSpell();
                return true;

            case CastCommand.Cast:
            {
                if (SelectedSpell >= choices.Count)
                {
                    return true;
                }

                var chosen = choices[SelectedSpell];
                var record = spellInfo?.Invoke(chosen.SpellId);

                Mode = CombatMenuMode.Command;
                Casting.Begin(actor, chosen.SpellId,
                              record?.CastingTime ?? 0,
                              (SpellCastingTime)(record?.CastingTimeType ?? 0),
                              Pending, Round.Round, Round.Queue);

                // Two outcomes, and the reference splits on exactly this test
                // (RunEvent.cpp:17104). A spell on the clock ends the turn at once -- the caster
                // stands there casting, interruptible, until it lands. One that resolves
                // immediately goes straight on to choosing targets in this same turn.
                Message = actor.IsSpellPending
                    ? $"{actor.Name} begins casting {chosen.SpellId}."
                    : $"{actor.Name} casts {chosen.SpellId}, but the effect is not implemented.";

                EndTurn(actor, CombatantState.Casting);
                return true;
            }

            default:
                Mode = CombatMenuMode.Command;
                CombatMenu.Build(Menu, OptionsFor(actor));
                return true;
        }
    }

    private void ShowSelectedSpell()
    {
        var choices = SpellChoices;
        Message = SelectedSpell < choices.Count
            ? $"{choices[SelectedSpell].SpellId} ({choices[SelectedSpell].Memorized})"
            : "No spells memorised.";
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

            case CombatCommand.Cast:
                Mode = CombatMenuMode.ChoosingSpell;
                SelectedSpell = 0;
                CombatMenu.BuildCast(Menu);
                ShowSelectedSpell();
                return true;

            case CombatCommand.Use:
            {
                if (UsableItems(actor).Count == 0)
                {
                    Message = $"{actor.Name} has nothing to use.";
                    return true;
                }

                Mode = CombatMenuMode.ChoosingItem;
                SelectedItem = 0;
                CombatMenu.BuildUse(Menu);
                ShowSelectedItem(actor);
                return true;
            }

            case CombatCommand.View:
                // VIEW is the character sheet, which the engine already builds; combat just shows
                // it for whoever is acting.
                Message = $"{actor.Name}: {actor.HitPoints}/{actor.MaxHitPoints} hp, "
                        + $"AC {actor.ArmorClass}.";
                return true;

            case CombatCommand.Turn:
            {
                // The design's TURN_ATTEMPT script says which undead categories this cleric
                // reaches; without GPDL there is nothing to ask, so nothing is turned.
                var reached = turnAttempt?.Invoke(actor) ?? new Dictionary<string, int>();
                var results = TurnUndead.Resolve(combatants, TurnDataFor, reached);
                TurnUndead.Apply(combatants, Map, Round.Queue, actor, results);

                Message = results.Count == 0
                    ? $"{actor.Name} turns nothing."
                    : $"{actor.Name} turns {results.Count}.";

                EndTurn(actor, CombatantState.Turning);
                return true;
            }

            case CombatCommand.Quick:
                // QUICK only ever turns automatic ON -- the menu calls Quick(TRUE) and nothing
                // else (RunEvent.cpp:15422). Taking a combatant back is the space bar's job; see
                // TakeBackControl.
                if (!SetAutomatic(actor, true))
                {
                    Message = $"{actor.Name} cannot be controlled.";
                    return true;
                }

                Message = $"{actor.Name} is on automatic.";
                CombatMenu.Build(Menu, OptionsFor(actor), acting: false);
                return true;

            case CombatCommand.Delay:
                if (!actor.DelayAction(Round.Queue))
                {
                    Message = $"{actor.Name} cannot delay.";
                    return true;
                }

                // Not an end of turn: the round's walk reaches this combatant again at its new,
                // later initiative.
                Message = $"{actor.Name} delays.";
                Acting = CombatMap.NoDude;
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

        // Any hit voids a spell the target was in the middle of.
        bool wasCasting = target.State == CombatantState.Casting;
        Casting.OnDamaged(target, result.Damage, Pending, Round.Queue);
        if (wasCasting && target.State != CombatantState.Casting)
        {
            Message += $" {target.Name}'s spell is lost.";
        }

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
