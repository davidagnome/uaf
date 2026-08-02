using UAF.Data;
using UAF.Media;
using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>Which way the party faces. Masked with <c>&amp;3</c> by the original.</summary>
/// <remarks>
/// Four cardinal directions, not eight. The C++ never names an enum for this, but
/// <c>GlobalData.cpp:2359</c> rejects <c>facing &gt; 3</c> and <c>Viewport.cpp:3680</c> computes
/// the right-hand direction as <c>(facing + 1) &amp; 3</c>, which fixes both the range and the
/// rotation order.
/// </remarks>
public enum Facing
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
}

/// <summary>
/// The engine's state machine and renderer, with no knowledge of SDL, windows or timers.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is driven by <see cref="Update"/> and observed through <see cref="Render"/>, so
/// the whole engine can be exercised from a test with a recorded input source and a headless
/// presenter. That is the point of the split: the C++ engine's equivalent logic is entangled with
/// a live DirectX device, which is why it has no automated tests at all.
/// </para>
/// <para>
/// <b>Scope.</b> This walks a party around a level, runs the events that need only text and a
/// menu — text statements, the three question forms, NPC dialogue — plus the two that need no
/// input at all, and follows their chains. It does not run combat, shops, or anything needing
/// party state: those are named rather than executed, which is deliberate and visible on screen.
/// </para>
/// </remarks>
public sealed class Game
{
    private readonly LoadedDesign design;
    private readonly Surface screen;

    public Game(LoadedDesign design, int width = 640, int height = 480, int levelIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(design);

        this.design = design;
        screen = new Surface(width, height);
        LevelIndex = levelIndex;

        // A carried item names its m_uniqueName; the fuller m_idName is what a player should see,
        // so the treasure list cannot be built without the item database.
        Runner.ItemNames = id => design.Item(id)?.Names.IdName;

        // VIEW shows whoever is active. Built here rather than in the runner, which has no party.
        Runner.ActiveCharacterSheet = () => Party?.Active is { } active
            ? CharacterSheetBuilder.Build(active, design.Baseclasses, design.Item)
            : null;

        // The full level gives the wall sets, which sit after the event list; the map-only read is
        // the fallback for a level whose events cannot all be decoded, since movement needs the
        // grid and nothing else.
        var level = design.Level(levelIndex);
        Map = level is not null
            ? new Map(level.Width, level.Height, level.Cells)
            : design.Map(levelIndex);

        zones = level?.Zones;

        if (level is not null && Map is not null)
        {
            events = new EventLookup(level.Events);
            resolver = new WallResolver(Map, level.WallSets);
            wallFormats = WallFormatReader.ReadAll(design.Config);
            wallSets = level.WallSets;
        }

        // The engine's own defaults, from GLOBAL_STATS. A design says where a new party starts.
        X = design.Globals.StartX;
        Y = design.Globals.StartY;
        Facing = (Facing)(design.Globals.StartFacing & 3);
        Minutes = design.Globals.StartTime;

        World = WorldState.FromDesign(design.Globals.Quests, design.Globals.SpecialItems,
                                      design.Globals.Keys);

        Money = design.Globals.Money is { } currency
            ? MoneyRules.FromDesign(currency)
            : MoneyRules.Default;
        Party = new Party { Pooled = new Purse(Money) };

        // A stand-in party -- see the remarks on the property.
        foreach (var member in design.Globals.Characters.Take(6))
        {
            Party.Add(new Character(member, Money));
        }
    }

    private readonly EventLookup? events;
    private readonly WallResolver? resolver;
    private readonly IReadOnlyList<WallFormat> wallFormats = [];

    /// <summary>The current level's grid, or null when it could not be read.</summary>
    public Map? Map { get; }

    /// <summary>Resolves viewport slots to wall art, when the level's wall sets were readable.</summary>
    public WallResolver? Walls => resolver;

    /// <summary>The level's events, when it was readable past its event list.</summary>
    public EventLookup? Events => events;

    /// <summary>The event the party is standing on, or null.</summary>
    public IGameEvent? CurrentEvent { get; private set; }

    public int X { get; private set; }

    public int Y { get; private set; }

    public Facing Facing { get; private set; }

    /// <summary>Game time in minutes, which <c>GLOBAL_STATS::startTime</c> seeds.</summary>
    public int Minutes { get; private set; }

    public int Steps { get; private set; }

    public bool Running { get; private set; } = true;

    /// <summary>The last message drawn in the text box.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>
    /// <see cref="Message"/> wrapped to the text box, with paging state.
    /// </summary>
    /// <remarks>
    /// Built lazily on the first draw after <see cref="Message"/> changes rather than at every
    /// assignment site, since wrapping needs the font and the font needs the design loaded. Public
    /// so a test — or, later, the input handler that pages long event text — can drive it without
    /// going through the renderer.
    /// </remarks>
    public TextDisplayData MessageBox { get; } = new();

    /// <summary>The text box, once a font has been resolved to narrow it against.</summary>
    public TextBoxMetrics? TextBox { get; private set; }

    private string wrappedMessage = string.Empty;
    private int wrappedWidth = -1;

    /// <summary>Which level is loaded. A transfer to any other one is not carried out yet.</summary>
    public int LevelIndex { get; }

    /// <summary>The adventuring party.</summary>
    /// <remarks>
    /// <b>Seeded from the design's pre-generated characters, which is not how a game starts.</b>
    /// The engine builds a party from the "add character" flow or restores one from a savegame;
    /// taking the first few of <c>GLOBAL_STATS::Characters</c> is a stand-in so the trigger
    /// conditions and the roster have something real to read. It is real data — the same records a
    /// savegame carries — placed by a rule the original does not have.
    /// </remarks>
    public Party Party { get; }

    /// <summary>The design's currency.</summary>
    public MoneyRules Money { get; }

    /// <summary>Quest, special-item and key state.</summary>
    public WorldState World { get; }

    /// <summary>
    /// Column offsets from the roster's left edge (<c>displayPartyNames</c>,
    /// <c>UAFWin/Disptext.cpp:1069</c>).
    /// </summary>
    /// <remarks>
    /// Fixed pixel offsets, not a proportion of anything: the header row is drawn at <c>x</c>,
    /// <c>x + 225</c> and <c>x + 300</c>, and the original's own commented-out constants
    /// (<c>displayText(500, …)</c>, <c>displayText(575, …)</c>) are those offsets added to the
    /// default name column of 275.
    /// </remarks>
    private const int ArmorClassColumn = 225;

    /// <inheritdoc cref="ArmorClassColumn"/>
    private const int HitPointColumn = 300;

    /// <summary>The hour of day the clock reads, 0–23.</summary>
    public int Hours => (Minutes / 60) % 24;

    /// <summary>The day number, counting from 1.</summary>
    public int Days => 1 + (Minutes / 1440);

    /// <summary>The event currently on screen, and its text and menu.</summary>
    public EventRunner Runner { get; } = new();

    /// <summary>The fight in progress, or null.</summary>
    /// <remarks>
    /// <para>
    /// Combat replaces the dungeon view entirely rather than drawing over it, so while this is
    /// non-null <see cref="Update"/> routes every key to the session and <see cref="Render"/> draws
    /// the combat map instead of the viewport. That is the same distinction the treasure screen
    /// makes (<c>UpdateSmallSprite</c> against <c>UpdateAdventureScreen</c>), which this document
    /// records under §7 Phase 4 — a full-screen event does not composite.
    /// </para>
    /// <para>
    /// The session owns the fight; this only starts it, feeds it input and clears it. See
    /// <see cref="CombatSession"/> for why it is a separate object.
    /// </para>
    /// </remarks>
    public CombatSession? Combat { get; private set; }

    /// <summary>Whether a fight is on.</summary>
    public bool InCombat => Combat is { IsActive: true };

    /// <summary>
    /// The dice combat rolls with. Replaceable so a test can make a fight deterministic.
    /// </summary>
    /// <remarks>
    /// The reference draws from the engine's shared Mersenne generator (<c>Globals.cpp:4925</c>);
    /// nothing here needs to match its sequence, only its range.
    /// </remarks>
    public Func<int, int> Dice { get; set; } = sides => Random.Shared.Next(1, sides + 1);

    /// <summary>The menu anchor points this design configures.</summary>
    public MenuAnchors Anchors { get; private set; } = MenuAnchors.Default;

    /// <summary>
    /// Resolves the text box and menu anchors from the design's config, once.
    /// </summary>
    /// <remarks>
    /// Both are needed before the first frame — an event can fire on the step that triggers it —
    /// so this cannot wait for <see cref="Render"/>, which is where the box was resolved when
    /// nothing but the message line used it.
    /// </remarks>
    private void EnsurePresentation(BitmapFont font)
    {
        var config = design.Config;

        TextBox ??= ResolveTextBox(config, font);

        if (ReferenceEquals(Anchors, MenuAnchors.Default))
        {
            Anchors = MenuAnchors.FromConfig(key =>
                config.TryGetPoint(key, out int x, out int y, consume: false) ? (x, y) : null);
        }
    }

    /// <summary>Handles one input event.</summary>
    /// <returns>True when the state changed and a redraw is warranted.</returns>
    /// <remarks>
    /// <b>An active event takes every key.</b> The original's task scheduler gives the event at the
    /// top of the queue the input and the movement handler never sees it, so a party standing in a
    /// conversation cannot walk away mid-sentence. Routing movement first would let them.
    /// </remarks>
    public bool Update(InputEvent input)
    {
        if (Combat is not null)
        {
            return UpdateCombat(input);
        }

        if (Runner.IsActive)
        {
            return UpdateEvent(input);
        }

        if (input.Kind != InputEventKind.KeyDown)
        {
            return false;
        }

        switch (input.Key)
        {
            case VirtualKey.Escape:
                Running = false;
                return true;

            case VirtualKey.Left:
                Facing = (Facing)(((int)Facing + 3) & 3);
                Message = $"Turned to face {Facing}.";
                return true;

            case VirtualKey.Right:
                Facing = (Facing)(((int)Facing + 1) & 3);
                Message = $"Turned to face {Facing}.";
                return true;

            case VirtualKey.Up:
                return Step(forward: true);

            case VirtualKey.Down:
                return Step(forward: false);

            default:
                return false;
        }
    }

    /// <summary>
    /// Moves one cell, if the cell being left allows it in that direction.
    /// </summary>
    /// <remarks>
    /// Moving backwards checks the <i>opposite</i> face, not the facing one — the party walks
    /// backwards without turning, so it leaves through the wall behind it. Checking the facing
    /// direction instead would let a party reverse straight through a wall it is staring at.
    /// </remarks>
    private bool Step(bool forward)
    {
        (int dx, int dy) = Facing switch
        {
            Facing.North => (0, -1),
            Facing.East => (1, 0),
            Facing.South => (0, 1),
            _ => (-1, 0),
        };

        if (!forward)
        {
            (dx, dy) = (-dx, -dy);
        }

        var direction = forward ? Facing : (Facing)(((int)Facing + 2) & 3);
        int nextX = X + dx;
        int nextY = Y + dy;

        // With no level loaded there is nothing to collide with, so movement is only bounded.
        if (Map is null)
        {
            nextX = Math.Clamp(nextX, 0, 255);
            nextY = Math.Clamp(nextY, 0, 255);
            if (nextX == X && nextY == Y)
            {
                Message = "You cannot go that way.";
                return true;
            }
        }
        else if (!Map.CanLeave(X, Y, direction))
        {
            // The blockage type is named rather than reduced to "blocked", because a locked door
            // and a wall are the same answer today and will not be once the party has keys.
            var blockage = Map.Blockage(X, Y, direction);
            Message = blockage is BlockageType.Blocked
                ? "A wall blocks your way."
                : $"The way is {blockage}.";
            return true;
        }

        // A level is a torus, not a bounded grid: walking off the east edge arrives at the west
        // (Party.cpp:1735). An earlier revision of this method reported "the map ends here"
        // instead, which is a rule the original does not have -- only walls stop a party.
        if (Map is not null)
        {
            (nextX, nextY) = Map.Wrap(nextX, nextY);
        }

        X = nextX;
        Y = nextY;
        Steps++;

        // One minute per step is this port's placeholder; the original derives it from the
        // party's speed and the zone, which is rules work rather than engine plumbing.
        Minutes++;
        Message = $"Moved {(forward ? "forward" : "back")} to ({X}, {Y}).";

        TriggerEvent();
        return true;
    }

    /// <summary>
    /// Runs whatever event the party has just stepped onto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every type is recognised and named rather than ignored.</b> Silently doing nothing for an
    /// unimplemented event would be indistinguishable from a design with no event there, and the
    /// difference matters constantly while the executor is being built out.
    /// </para>
    /// <para>
    /// A suppressed event still gets its not-happened chain — that is what
    /// <see cref="EventChain"/> is for, and it is the mechanism a design uses for "if the party
    /// does not have the key, say so".
    /// </para>
    /// <para>
    /// Not ported yet: the chain that lets several events share a <i>cell</i> (distinct from the
    /// id-chaining here), and the happened/not-happened flags that <c>PARTY</c> carries and a
    /// savegame persists — the latter is what makes <c>OnceOnly</c> work.
    /// </para>
    /// </remarks>
    private void TriggerEvent()
    {
        var candidate = events?.FirstAt(X, Y);
        if (candidate is null)
        {
            CurrentEvent = null;
            return;
        }

        // EVENT_CONTROL decides whether the event fires at all. Most conditions ask about state
        // this engine does not have yet -- inventory, quests, party composition -- and those come
        // back Unknown rather than false, so a design does not look empty when it is only
        // unevaluated.
        var verdict = EventTrigger.Evaluate(candidate.Base.Control, X, Y, Facing,
                                            party: Party, world: World, hours: Hours);
        var type = (EventTriggerType)candidate.Base.Control.EventTrigger;

        if (verdict == TriggerResult.Suppress)
        {
            CurrentEvent = null;
            Message = $"[{candidate.GetType().Name} suppressed by {type}]";
            FollowChain(EventChain.Next(candidate.Base, happened: false));
            return;
        }

        CurrentEvent = candidate;

        if (verdict == TriggerResult.Unknown)
        {
            Message = $"[{candidate.GetType().Name} needs {type} -- cannot evaluate yet]";
            return;
        }

        StartEvent(candidate);
    }

    /// <summary>
    /// Runs an event: executes it outright if it needs no player input, otherwise puts it on
    /// screen.
    /// </summary>
    /// <remarks>
    /// The split is the original's own — an event that never calls <c>Invalidate</c> and chains
    /// from <c>OnInitialEvent</c> is over before a frame is drawn — but here it also marks the
    /// boundary between what this port can run and what it still only names.
    /// </remarks>
    /// <summary>
    /// Starts a fight from a combat event, if there is a level to fight on.
    /// </summary>
    /// <returns>Whether combat began. False falls through to the ordinary event path.</returns>
    /// <remarks>
    /// The party's combatants are built from its characters here rather than in the session,
    /// because only <see cref="Game"/> knows what the party <i>is</i>. Everything else about the
    /// encounter comes off the event.
    /// </remarks>
    private bool StartCombat(CombatEvent combat)
    {
        if (Map is null || wallSets is null)
        {
            return false;
        }

        // A character's footprint is measured off its combat icon, same as a monster's, so the
        // art has to be resolved before the combatant is built rather than at draw time.
        var partyIcons = new Dictionary<string, (Surface Sheet, CombatantIcon Icon)>();
        var party = new List<Combatant>();

        for (int i = 0; i < Party.Members.Count; i++)
        {
            var member = Party.Members[i];
            var icon = new CombatantIcon(1, 1);

            if (member.Record.Icon is { } pic
                && CombatIcons.Load(pic.FileName, pic.NumFrames,
                                    n => design.Art(n, SurfaceKind.Icon)) is { } loaded)
            {
                icon = loaded.Icon;
                partyIcons[member.Name] = loaded;
            }

            party.Add(new Combatant(i, isFriendly: true, icon, member.Name)
            {
                Kind = CombatantKind.Character,
                HitPoints = member.HitPoints,
                MaxHitPoints = member.MaxHitPoints,
                MaxMovement = 12,
                Initiative = i + 1,
                // Party members are player-run; nothing yet reads a per-character auto flag.
                IsAuto = false,
            });
        }

        if (party.Count == 0)
        {
            // A design can trigger combat before a party exists -- an empty fight is not a fight.
            return false;
        }

        Combat = CombatSession.Begin(combat, Map, wallSets, X, Y, Facing, party,
                                     id => design.Monster(id), Dice,
                                     name => design.Art(name, SurfaceKind.Icon), partyIcons);
        Message = "Combat!";
        return true;
    }

    /// <summary>
    /// Advances the fight, and folds the result back into the event chain when it ends.
    /// </summary>
    /// <remarks>
    /// A fight that is waiting on the player consumes the key; otherwise it steps itself, so a
    /// caller pressing nothing still watches the monsters act. That is this port's stand-in for
    /// the reference's timed task scheduler.
    /// </remarks>
    private bool UpdateCombat(InputEvent input)
    {
        var session = Combat!;

        bool changed = session.AwaitingPlayer
            ? session.Update(input)
            : session.Update();

        Message = session.Message.Length > 0 ? session.Message : Message;

        if (!session.IsActive)
        {
            var outcome = session.Outcome;
            var finished = CurrentEvent;
            Combat = null;
            CurrentEvent = null;
            Message = outcome switch
            {
                CombatOutcome.PartyWon => "The party is victorious!",
                CombatOutcome.PartyLost => "The party has fallen.",
                _ => "The fight breaks off.",
            };

            if (finished is not null)
            {
                FollowChain(EventChain.Next(finished.Base, outcome == CombatOutcome.PartyWon));
            }
        }

        return changed || !session.IsActive;
    }

    private void StartEvent(IGameEvent gameEvent)
    {
        CurrentEvent = gameEvent;

        if (gameEvent is CombatEvent combat && StartCombat(combat))
        {
            return;
        }

        if (ExecuteWithoutInput(gameEvent) is bool ran)
        {
            CurrentEvent = null;
            FollowChain(EventChain.Next(gameEvent.Base, ran));
            return;
        }

        var font = design.Font(design.RequestedFontHeight);
        if (font is null)
        {
            // Nothing can be presented without a font -- a design opened with no rasteriser can
            // still be walked around, so this is a real state rather than a failure.
            Message = $"[{gameEvent.GetType().Name} here -- no font to present it with]";
            return;
        }

        EnsurePresentation(font);
        var step = Runner.Begin(gameEvent, font, TextBox!, Anchors);
        Message = Runner.Unimplemented ?? string.Empty;

        if (step.Kind != EventStepKind.Running)
        {
            Apply(step);
        }
    }

    /// <summary>
    /// Runs the event types that need no player input.
    /// </summary>
    /// <returns>
    /// Whether the event happened, or null when it is not one of these — in which case it has to be
    /// presented. The distinction matters to <see cref="EventChain"/>, which branches on it.
    /// </returns>
    /// <remarks>
    /// <b>Only the ones whose state this engine actually has.</b> <c>PassTime</c> moves a clock
    /// that exists and <c>Teleporter</c> moves a party that exists. <c>GainExperience</c>,
    /// <c>Sounds</c> and <c>FlowControl</c> are left to be named rather than run: there is no party
    /// to award experience to, no audio device wired to the engine, and no task queue for flow
    /// control to steer. Pretending otherwise would make a design look like it worked.
    /// </remarks>
    private bool? ExecuteWithoutInput(IGameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case PassTimeEvent pass:
                int minutes = (pass.Days * 24 * 60) + (pass.Hours * 60) + pass.Minutes;
                if (pass.SetTime != 0)
                {
                    // SetTime means "make it this time", not "add this much".
                    Minutes = minutes;
                }
                else
                {
                    Minutes += minutes;
                }

                Message = pass.PassSilent != 0
                    ? string.Empty
                    : $"Time passes: {pass.Days}d {pass.Hours}h {pass.Minutes}m.";
                return true;

            case TransferEvent transfer:
                return Teleport(transfer);

            // Only the silent form runs without input; the other opens a screen, so it falls
            // through to the presenter and names itself there rather than being consumed here.
            case TreasureEvent { SilentGiveToActiveChar: not 0 } treasure:
                return GiveTreasure(treasure);

            case GainExperienceEvent gain:
                return GainExperience(gain);

            default:
                return null;
        }
    }

    /// <summary>
    /// Hands a treasure's money to the party (<c>GIVE_TREASURE_DATA::OnInitialEvent</c>,
    /// <c>RunEvent.cpp:6541</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only the silent path runs.</b> With <c>SilentGiveToActiveChar</c> set, the original hands
    /// everything to the active character and chains without drawing anything; otherwise it opens
    /// a pick-up-or-leave screen, which is a form this port does not have. The loud path is
    /// reported rather than silently taking the treasure, since a design's pacing depends on the
    /// player seeing it.
    /// </para>
    /// <para>
    /// The money goes to the party's common purse rather than to the active character's, because
    /// characters' purses are still records read off disk rather than live state. That is a
    /// difference in <i>where</i> it lands, not in how much.
    /// </para>
    /// <para>
    /// <b>The experience comes from the item database, not the carried instances</b>
    /// (<c>:6553</c>) — an instance records what it is, not what it is worth — and it goes to the
    /// <i>active character</i> rather than being shared, which is what the original does.
    /// </para>
    /// </remarks>
    private bool GiveTreasure(TreasureEvent treasure)
    {
        var pile = Purse.FromRecord(treasure.Money, Money);
        double before = Party.Pooled.Total();
        Party.Pooled.Transfer(pile);
        double gained = Party.Pooled.Total() - before;

        int experience = 0;
        int unknown = 0;
        foreach (var carried in treasure.Items.Items)
        {
            var record = design.Item(carried.ItemId);
            if (record is null)
            {
                unknown++;
                continue;
            }

            Party.Carried.Add(carried);
            experience += record.Scalars.Experience;
        }

        int awarded = Party.Active?.GiveExperience(experience) ?? 0;
        Message = Describe(gained, treasure.Items.Items.Count - unknown, unknown, awarded);
        return true;
    }

    /// <summary>
    /// Awards experience to some or all of the party (<c>GAIN_EXP_DATA</c>,
    /// <c>RunEvent.cpp:10151</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>who</c> selects between nobody, everybody, the active character, one at random, and a
    /// per-character chance roll. <b>The random case rolls 1..count and indexes count−1</b>, so the
    /// roll is 1-based and the index is not — transcribing it as a plain 0-based roll never picks
    /// the last member.
    /// </para>
    /// <para>
    /// The original re-derives each character's ready-to-train flag afterwards. That needs the
    /// baseclass experience thresholds from <c>baseclass.dat</c>, which has no reader yet, so the
    /// flag stays as the record left it and the roster's blue name is the design's own.
    /// </para>
    /// <para>
    /// This runs without input, where the original waits for Return on a screen. The award is the
    /// same; what is missing is the pause.
    /// </para>
    /// </remarks>
    private bool GainExperience(GainExperienceEvent gain)
    {
        int awarded = 0;

        switch ((PartyAffect)gain.Who)
        {
            case PartyAffect.None:
                break;

            case PartyAffect.EntireParty:
                foreach (var member in Party.Members)
                {
                    awarded += member.GiveExperience(gain.Experience);
                }

                break;

            case PartyAffect.ActiveCharacter:
                awarded += Party.Active?.GiveExperience(gain.Experience) ?? 0;
                break;

            case PartyAffect.OneAtRandom when Party.Count > 0:
                // RollDice(count, 1) is 1..count, indexed at [roll - 1].
                int roll = Random.Shared.Next(1, Party.Count + 1);
                awarded += Party.Members[roll - 1].GiveExperience(gain.Experience);
                break;

            case PartyAffect.ChanceOnEach:
                foreach (var member in Party.Members)
                {
                    if (Random.Shared.Next(1, 101) <= gain.Chance)
                    {
                        awarded += member.GiveExperience(gain.Experience);
                    }
                }

                break;
        }

        Message = awarded > 0
            ? $"The party gains {awarded} experience."
            : "No one gains experience.";

        return true;
    }

    /// <summary>Puts a treasure's outcome into words for the message line.</summary>
    private string Describe(double coins, int items, int unknown, int experience)
    {
        var parts = new List<string>();

        if (coins > 0)
        {
            parts.Add($"{coins:0} {Money[Money.BaseType].Name.ToLowerInvariant()}");
        }

        if (items > 0)
        {
            parts.Add($"{items} item{(items == 1 ? string.Empty : "s")}");
        }

        string gained = parts.Count > 0
            ? $"You gain {string.Join(" and ", parts)}."
            : "You find nothing of value.";

        if (experience > 0)
        {
            gained += $" ({experience} experience, not yet awarded.)";
        }

        if (unknown > 0)
        {
            // Naming this matters: it means the database lookup failed, which in a port is far
            // likelier to be a reader fault than a design one.
            gained += $" ({unknown} item(s) name no record in this design's database.)";
        }

        return gained;
    }

    /// <summary>
    /// Moves the party to a transfer's destination (<c>TRANSFER_EVENT_DATA</c>).
    /// </summary>
    /// <remarks>
    /// <b>Only same-level transfers are carried out.</b> A destination level other than the one
    /// loaded needs the level swapped underneath the game, which this engine does not do yet — so
    /// it is reported rather than silently landing the party at the right coordinates on the wrong
    /// map, which would look like it worked.
    /// </remarks>
    private bool Teleport(TransferEvent transfer)
    {
        var destination = transfer.Destination;

        if (destination.DestLevel != LevelIndex)
        {
            Message = $"[Teleporter to level {destination.DestLevel} "
                      + "-- changing level is not implemented]";
            return false;
        }

        X = destination.DestX;
        Y = destination.DestY;
        Facing = (Facing)(destination.Facing & 3);
        Message = $"You are somewhere else: ({X}, {Y}) facing {Facing}.";
        return true;
    }

    private readonly ZoneData? zones;

    /// <summary>The level's wall sets, which combat needs to build its map.</summary>
    private readonly IReadOnlyList<WallSetSlot>? wallSets;

    /// <summary>
    /// The art a full-screen event shows where the dungeon view was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The treasure screen blits the <b>zone's</b> treasure picture, not the event's
    /// (<c>RunEvent.cpp:6588</c>): <c>currPic = levelData.zoneData.zones[zone].treasurePicture</c>,
    /// with the zone taken from the party's own cell. So two treasures on one level can look
    /// different, and the event carries nothing that says which.
    /// </para>
    /// <para>
    /// Null when the design ships no art for it, which is the ordinary case rather than an error —
    /// the area simply stays empty, as it does when <c>currPic.key</c> is 0.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The combat terrain sheet for the zone the party is standing in.
    /// </summary>
    /// <remarks>
    /// <b>Combat art belongs to the zone, not the design</b> (<c>Dgngame.cpp:1126</c>) — each zone
    /// names an indoor and an outdoor sheet, and which one is used depends on the encounter rather
    /// than on the zone. The indoor one is taken here because outdoor encounters need
    /// <c>GenerateOutdoorCombatMap</c>, which is unported.
    /// </remarks>
    private Surface? CombatArt()
    {
        if (zones is null || Map is null)
        {
            return null;
        }

        int zone = Map.At(X, Y)?.Zone ?? 0;
        if (zone < 0 || zone >= zones.Zones.Count)
        {
            return null;
        }

        string name = zones.Zones[zone].IndoorCombatArt;
        return string.IsNullOrEmpty(name) ? null : design.Art(name);
    }

    private Surface? ScreenArt()
    {
        // CoversRoster means a sheet is over the whole screen, and the zone's picture belongs to
        // the screen underneath it. UpdateViewCharacterScreen blits a picture of its own -- the
        // character's portrait -- which this port does not model, so it draws nothing rather than
        // leaving the treasure showing through the stats.
        if (!Runner.OwnsScreen || Runner.CoversRoster || zones is null || Map is null)
        {
            return null;
        }

        int zone = Map.At(X, Y)?.Zone ?? 0;
        if (zone < 0 || zone >= zones.Zones.Count)
        {
            return null;
        }

        string file = zones.Zones[zone].TreasurePicture.FileName;
        return file.Length > 0 ? design.Art(file) : null;
    }

    /// <summary>Feeds input to the event on screen.</summary>
    private bool UpdateEvent(InputEvent input)
    {
        var currentEvent = Runner.Current;
        var step = Runner.Handle(input);

        if (step.Kind == EventStepKind.Running)
        {
            // A menu entry this port has not built reports itself rather than doing nothing.
            Message = Runner.Unimplemented ?? Message;
            return true;
        }

        // TAKE has to be read before Apply resets the runner. The runner owns no party, so
        // handing the treasure over is the host's job -- which is also why the silent form is
        // consumed in ExecuteWithoutInput and never reaches the runner at all.
        if (Runner.TakeRequested && currentEvent is TreasureEvent treasure)
        {
            GiveTreasure(treasure);
        }

        Apply(step);
        return true;
    }

    /// <summary>Acts on a finished event's outcome.</summary>
    private void Apply(EventStep step)
    {
        CurrentEvent = null;
        Runner.Cancel();

        if (step.Kind == EventStepKind.Chain)
        {
            FollowChain(step.ChainTo);
        }
    }

    /// <summary>
    /// Starts the chained event, if there is one and it exists.
    /// </summary>
    /// <remarks>
    /// A chain naming an event the level does not contain is not an error — the original pushes a
    /// do-nothing event and carries on. Reported, though, because in a port it is far more likely
    /// to mean the reader dropped an event than that the design is wrong.
    /// </remarks>
    private void FollowChain(uint? id)
    {
        if (id is not uint target || events is null)
        {
            return;
        }

        var next = events.ById(target);
        if (next is null)
        {
            Message = $"[chain to event {target}, which this level does not contain]";
            return;
        }

        StartEvent(next);
    }

    /// <summary>Draws the current state and returns the framebuffer.</summary>
    public Surface Render()
    {
        screen.ClipRect = screen.Bounds;
        screen.Fill(0xFF000000);

        var config = design.Config;
        config.Rewind();

        var horizontal = design.Art("border_Horizontal.png");
        var vertical = design.Art("border_Vertical.png");

        if (horizontal is not null)
        {
            Blit(config, horizontal, "HORZ_BAR_LONG", "HORZ_BAR_TOP");
            Blit(config, horizontal, "HORZ_BAR_LONG_2", "HORZ_BAR_MIDDLE");
            Blit(config, horizontal, "HORZ_BAR_LONG_3", "HORZ_BAR_BOTTOM");
        }

        if (vertical is not null)
        {
            Blit(config, vertical, "VERT_BAR_LONG", "VERT_BAR_LEFT");
            Blit(config, vertical, "VERT_BAR_SHORT", "VERT_BAR_MIDDLE");
            Blit(config, vertical, "VERT_BAR_LONG", "VERT_BAR_RIGHT");
        }

        var frame = design.Art("border_Viewport.png");
        if (frame is not null && config.TryGetPoint("VIEWPORT_FRAME", out int fx, out int fy))
        {
            Blitter.BlitOpaque(screen, fx, fy, frame);
        }

        // A full-screen event replaces the dungeon view rather than drawing over it. The treasure
        // screen is UpdateSmallSprite (Screen.cpp:340): ClearAdventureBackground, then the zone's
        // treasure picture where the viewport was, then the roster, menu and text -- updateViewport
        // is never called. Drawing the corridor underneath is what put the item list on top of a
        // wall.
        var backdrop = design.Art("backdrop_IndoorGreyStone.png", SurfaceKind.Background);
        if (config.TryGetRect("VIEWPORT_RECT", out int vx, out int vy, out int vr, out int vb))
        {
            if (Combat is not null)
            {
                // Combat owns the whole viewport, the same way a full-screen event does. The
                // terrain sheet comes off the zone the party is standing in, not the design.
                Combat.Render(screen, CombatArt(), new SurfaceRect(vx, vy, vr, vb));
            }
            else if (Runner.OwnsScreen)
            {
                // The zone's picture takes the viewport's place, drawn through the colour key
                // like every other sprite -- blitView asks for SmallPicDib | SpriteDib.
                if (ScreenArt() is { } art)
                {
                    Blitter.BlitTransparent(screen, vx, vy, art);
                }
            }
            else
            {
                if (backdrop is not null)
                {
                    Blitter.BlitOpaque(screen, vx, vy, backdrop);
                }

                DrawWalls(vx, vy, new SurfaceRect(vx, vy, vr, vb));
            }
        }

        DrawText(config);
        return screen;
    }

    /// <summary>
    /// Draws the corridor's walls into the viewport.
    /// </summary>
    /// <remarks>
    /// Square 0 plus squares 5-14; only 1, 2, 3 and 4 remain. The clip is the viewport
    /// rectangle, because a wall slot's own offsets can place it outside and the original relies on
    /// the viewport being a separate, smaller surface to cut that off.
    /// </remarks>
    private void DrawWalls(int viewportX, int viewportY, SurfaceRect viewport)
    {
        if (resolver is null || wallFormats.Count == 0 || Map is null)
        {
            return;
        }

        var view = Map.View(X, Y, Facing);
        var saved = screen.ClipRect;

        try
        {
            screen.ClipRect = viewport;

            // Far squares first. The original draws back to front and relies on the keyed blits to
            // let nearer walls cover further ones, so the order is load-bearing rather than
            // cosmetic.
            // The two far corner squares, whose slivers sit behind everything else.
            foreach (int square in new[] { 0, 1 })
            {
                string? file = resolver.ArtFor(view, square, Facing, WallLayer.Wall);
                var sheet = file is null ? null : design.Art(file, SurfaceKind.Wall);
                RendererFor(sheet)?.RenderFarSquare(screen, view, resolver, Facing, square,
                                                    viewportX, viewportY,
                                                    f => design.Art(f, SurfaceKind.Wall));
            }

            // Square 2 sits between the far corners and the near squares.
            {
                string? file = resolver.ArtFor(view, 2, Facing, WallLayer.Wall);
                var sheet = file is null ? null : design.Art(file, SurfaceKind.Wall);
                RendererFor(sheet)?.RenderSquare2(screen, view, resolver, Facing,
                                                  viewportX, viewportY,
                                                  f => design.Art(f, SurfaceKind.Wall));
            }

            // Far to near, so a nearer wall's keyed blit covers a further one.
            //
            // The renderer is chosen from *any* face that resolves, not from the front one. An
            // earlier revision picked the sheet from the front face and skipped the whole square
            // when it was empty -- which silently discarded square 9, whose front face is often
            // clear while its left and right walls are the corridor sides the player actually
            // sees. Every pass then re-resolves its own sheet inside RenderSquare, so a square
            // mixing two wall packs still cuts each from its own format.
            foreach (int square in ViewportRenderer.SquarePasses.Keys.OrderBy(s => s))
            {
                var sheet = FirstSheet(view, square);
                RendererFor(sheet)?.RenderSquare(screen, view, resolver, Facing, square,
                                                 viewportX, viewportY,
                                                 f => design.Art(f, SurfaceKind.Wall));
            }
        }
        finally
        {
            screen.ClipRect = saved;
        }
    }

    /// <summary>
    /// The first wall sheet any of a square's passes resolves to, or null when none do.
    /// </summary>
    /// <remarks>
    /// Used only to choose the format. A square draws nothing when every face is clear, but one
    /// clear face must not suppress the others.
    /// </remarks>
    private Surface? FirstSheet(ViewMap view, int square)
    {
        if (resolver is null || !ViewportRenderer.SquarePasses.TryGetValue(square, out var passes))
        {
            return null;
        }

        foreach (var pass in passes)
        {
            var face = pass.Direction switch
            {
                ViewportRenderer.PassDirection.Left => (Facing)(((int)Facing + 3) & 3),
                ViewportRenderer.PassDirection.Right => (Facing)(((int)Facing + 1) & 3),
                _ => Facing,
            };

            string? file = resolver.ArtFor(view, square, face, WallLayer.Wall);
            if (file is not null && design.Art(file, SurfaceKind.Wall) is Surface sheet)
            {
                return sheet;
            }
        }

        return null;
    }

    /// <summary>The renderer whose format matches a sheet's dimensions, or the default.</summary>
    private ViewportRenderer? RendererFor(Surface? sheet)
    {
        if (sheet is null)
        {
            return null;
        }

        var format = WallFormatReader.SelectFor(wallFormats, sheet.Width, sheet.Height);
        return format is null ? null : new ViewportRenderer(format);
    }

    private void Blit(DesignConfig config, Surface art, string sourceKey, string destinationKey)
    {
        if (!config.TryGetRect(sourceKey, out int l, out int t, out int r, out int b) ||
            !config.TryGetPoint(destinationKey, out int x, out int y))
        {
            return;
        }

        // The *_LONG keys are source rectangles into a sheet of stacked strips, not destinations.
        if (new SurfaceRect(l, t, r, b).TryClipTo(art.Bounds, out var source))
        {
            Blitter.BlitOpaque(screen, x, y, art, source);
        }
    }

    private void DrawText(DesignConfig config)
    {
        var font = design.Font(design.RequestedFontHeight);
        if (font is null)
        {
            return;
        }

        // The treasure screen keeps the roster (UpdateSmallSprite calls displayPartyNames); the
        // character sheet does not (UpdateViewCharacterScreen does not). So screen ownership is not
        // one flag but a question per element, and this is the element the two screens disagree on.
        if (!Runner.CoversRoster && config.TryGetInts("PARTYNAMES", out int[] roster, 4))
        {
            DrawRoster(font, roster[2], roster[3]);
        }

        DrawMessageBox(config, font);
    }

    /// <summary>
    /// Draws the party roster and the clock in the config's <c>PARTYNAMES</c> column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transcribed from <c>displayPartyNames</c> (<c>UAFWin/Disptext.cpp:1022</c>): a
    /// <c>NAME</c>/<c>AC</c>/<c>HP</c> header, then one row per member. <b>The line height is the
    /// font's tallest glyph plus two</b>, and <b>the step comes before each row rather than after
    /// it</b>, so the first name sits one full line below the header rather than against it.
    /// </para>
    /// <para>
    /// Colour carries the status: a name is blue when the character is ready to train and green
    /// otherwise; hit points are red at zero or below, yellow when below the maximum, green at
    /// full. That is the whole of the status display — there is no separate condition column.
    /// </para>
    /// <para>
    /// The design name and the position/clock lines below are the port's own diagnostics rather
    /// than anything the original draws. They earn their place while the engine is being built out.
    /// </para>
    /// </remarks>
    private void DrawRoster(BitmapFont font, int x, int y)
    {
        int lineHeight = font.Atlas.MaxCharHeight + 2;

        font.Draw(screen, x, y, design.Name, tint: 0xFFE8C86A);
        y += lineHeight;

        font.Draw(screen, x, y, "NAME", tint: FontPalette.Resolve(FontColor.White));
        font.Draw(screen, x + ArmorClassColumn, y, " AC",
                  tint: FontPalette.Resolve(FontColor.White));
        font.Draw(screen, x + HitPointColumn, y, " HP",
                  tint: FontPalette.Resolve(FontColor.White));

        for (int i = 0; i < Party.Count; i++)
        {
            var member = Party.Members[i];

            // The step comes first, matching the original's `y += LineHeight` at the head of the
            // loop body -- so the header and the first row are never on the same line.
            y += lineHeight;

            uint nameTint = FontPalette.Resolve(
                member.ReadyToTrain ? FontColor.Blue : FontColor.Green);

            var hitPoints = member.HitPoints <= 0 ? FontColor.Red
                          : member.HitPoints < member.MaxHitPoints ? FontColor.Yellow
                          : FontColor.Green;

            // The active character is drawn highlighted, which is the same reverse video the menu
            // uses -- here it marks whose turn it is rather than what a keypress would choose.
            if (i == Party.ActiveCharacter)
            {
                screen.FillRect(
                    new SurfaceRect(x, y, x + font.GetTextWidth(member.Name), y + lineHeight - 2),
                    MenuPalette.Default.HighlightBackground);
                font.Draw(screen, x, y, member.Name, tint: MenuPalette.Default.HighlightInk);
            }
            else
            {
                font.Draw(screen, x, y, member.Name, tint: nameTint);
            }

            font.Draw(screen, x + ArmorClassColumn, y, $"{member.ArmorClass}", tint: nameTint);
            font.Draw(screen, x + HitPointColumn, y, $"{member.HitPoints}",
                      tint: FontPalette.Resolve(hitPoints));
        }

        y += lineHeight + 2;
        font.Draw(screen, x, y, $"({X}, {Y}) facing {Facing}", tint: 0xFFF0E6D2);
        y += lineHeight;

        font.Draw(screen, x, y, $"Day {Days}  {Hours:00}:{Minutes % 60:00}", tint: 0xFF9A9AB0);
        y += lineHeight;

        font.Draw(screen, x, y, $"{Steps} steps", tint: 0xFF9A9AB0);
    }

    /// <summary>
    /// Wraps <see cref="Message"/> into the design's text box and draws the current page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The box comes from the design's own config — <c>TEXTBOX</c> or <c>TEXTBOX_RECT</c>, plus
    /// <c>TextBox_Lines</c> — and is then narrowed against the loaded font, exactly as
    /// <c>GetTextBoxCharWidth</c> does. Wrapping at the raw config width instead overruns by half a
    /// character, which shows only on the occasional line and is the sort of thing that gets
    /// blamed on the font.
    /// </para>
    /// <para>
    /// Re-wrapping is skipped when neither the text nor the width has changed, so a stationary
    /// party does not re-run the scanner every frame.
    /// </para>
    /// </remarks>
    private void DrawMessageBox(DesignConfig config, BitmapFont font)
    {
        EnsurePresentation(font);
        var box = TextBox!;

        // An event on screen owns the text box and the menu -- the message line is what the engine
        // says when nothing else is speaking.
        if (Runner.IsActive)
        {
            Runner.Render(screen);

            // An event this port only names has no text of its own, so the line still says so.
            if (Runner.Unimplemented is null)
            {
                return;
            }
        }

        if (Message.Length == 0)
        {
            return;
        }

        if (!string.Equals(wrappedMessage, Message, StringComparison.Ordinal)
            || wrappedWidth != box.Width)
        {
            TextFormatter.Format(Message, box.Width, font, MessageBox);
            MessageBox.FirstBox();
            wrappedMessage = Message;
            wrappedWidth = box.Width;
        }

        MessageBox.LinesPerBox = box.Lines;
        FormattedTextRenderer.DrawBox(screen, font, MessageBox, box.X, box.Y);
    }

    /// <summary>
    /// Reads the text box out of the design's config, falling back to the engine's own defaults.
    /// </summary>
    /// <remarks>
    /// <c>Screen_Width</c> only matters to the <c>TEXTBOX</c> form, which takes its width as the
    /// screen less the left inset doubled — so a design that sets one and not the other still gets
    /// a box the right shape.
    /// </remarks>
    private TextBoxMetrics ResolveTextBox(DesignConfig config, BitmapFont font)
    {
        (int, int)? textbox = config.TryGetPoint("TEXTBOX", out int tx, out int ty, consume: false)
            ? (tx, ty)
            : null;

        (int, int, int, int)? rect =
            config.TryGetRect("TEXTBOX_RECT", out int l, out int t, out int r, out int b,
                              consume: false)
                ? (l, t, r, b)
                : null;

        int screenWidth = int.TryParse(config.GetString("Screen_Width", consume: false),
                                       out int width) && width > 0
            ? width
            : screen.Width;

        int? lines = config.TryGetValue("TextBox_Lines", out string lineText, consume: false)
                     && int.TryParse(lineText, out int lineCount) && lineCount > 0
            ? lineCount
            : null;

        return TextBoxMetrics.FromConfig(screenWidth, textbox, lines, rect).ForFont(font);
    }
}
