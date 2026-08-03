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
        SaveDirectory = Path.Combine(design.Root, "Saves");

        // A carried item names its m_uniqueName; the fuller m_idName is what a player should see,
        // so the treasure list cannot be built without the item database.
        Runner.ItemNames = id => design.Item(id)?.Names.IdName;

        // READY needs the record itself: how many hands the thing takes, and which slot it goes in.
        Runner.ItemDatabase = design.Item;

        // VIEW shows whoever is active. Built here rather than in the runner, which has no party.
        Runner.ActiveCharacterSheet = () => Party?.Active is { } active
            ? CharacterSheetBuilder.Build(active, design.Baseclasses, design.Item)
            : null;

        // A random event's branch needs the level's event list and the dice, neither of which the
        // runner has. Dice is read through the property so a test that replaces it still counts.
        Runner.ChooseRandomBranch = random =>
            RandomEventChoice.Pick(random, id => events?.ById(id) is not null, sides => Dice(sides));


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

        // Special items and keys are global rather than carried, so they live on World -- which
        // is why this is wired after it exists rather than beside the other runner callbacks.
        Runner.ApplySpecialItems = special => SpecialItems.Apply(special, World);

        // Quests share that store, and a quest event can set an item's or a key's stage instead.
        Runner.ResolveQuest = (quest, accepted) =>
            Quests.Resolve(quest, accepted, World, id => events?.ById(id) is not null);

        // A logic block reads and writes more of the game than any other event -- attributes in
        // four scopes, the quest table, the party roster -- so it gets an adapter rather than a
        // callback each. The quest map is here because a block names a quest where the rest of the
        // engine uses its id.
        LogicBlockHost = new GameLogicBlockHost(
            this,
            design.Globals.Quests
                .GroupBy(q => q.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal));

        Runner.ResolveLogicBlock = block =>
            LogicBlockRun.Run(block, LogicBlockHost, id => events?.ById(id) is not null);

        Globals.Load(design.Globals.Attributes);

        Money = design.Globals.Money is { } currency
            ? MoneyRules.FromDesign(currency)
            : MoneyRules.Default;
        Party = new Party { Pooled = new Purse(Money) };

        // A stand-in party -- see the remarks on the property.
        foreach (var member in design.Globals.Characters.Take(6))
        {
            Party.Add(new Character(member, Money));
        }

        // Damage and healing act on the party through PartyAffect, so neither needs the player to
        // choose anyone -- unlike WhoTries and WhoPays, which do and are not wired yet.
        Runner.ApplyDamage = damage =>
        {
            var hurt = EventDamage.Apply(damage, Party, sides => Dice(sides));
            Message = hurt.TotalDamage > 0
                ? $"The party takes {hurt.TotalDamage} damage."
                : "The party is unharmed.";
        };

        // TAB cycles the active party member, which is how the format answers "who tries" and
        // "who pays" -- there is no selection screen; see EventRunner.TabParty.
        Runner.TabParty = () =>
        {
            if (Party.Count > 0)
            {
                Party.ActiveCharacter = (Party.ActiveCharacter + 1) % Party.Count;
            }
        };

        // Saves live beside the design (rte.SaveDir is m_designDir + "Saves\"), so the slot
        // screens read the same folder the reference writes.
        Runner.SaveSlotsAvailable = () => SaveSlots.Under(SaveDirectory);

        // Reading and writing a .pty are both finished; turning a game in progress back into one
        // is not. See SaveGameProjection for exactly what is missing and why this refuses rather
        // than writing a file that has forgotten half the game.
        Runner.SaveToSlot = _ =>
            SaveGameProjection.CanSave(this, out string reason) ? null : reason;

        Runner.LoadFromSlot = _ =>
            "This port cannot load a saved game yet: it has nowhere to put the visited squares, " +
            "trigger flags and journal a save carries.";

        // The party menu's TRAIN entry is dark unless all three conditions hold, and it is
        // recomputed on every pass because TAB can change who is standing at the counter.
        TrainingRules RulesFor(Character who) =>
            new(id => design.Baseclasses?.GetValueOrDefault(id),
                baseclass => design.LevelCap(who, baseclass));

        Runner.CanTrainHere = hall =>
            Party.Active is { } who && Training.CanTrain(hall, who, RulesFor(who));

        Runner.ApplyTraining = hall =>
            Party.Active is { } who
                ? Training.Train(hall, who, RulesFor(who),
                                 (count, sides) => DiceExpression.Roll(count, sides, Dice))
                : TrainingOutcome.Refused(TrainingRefusal.NotReady);

        Runner.ResolveWhoTries = trial =>
        {
            var attempt = Party.Active is { } who
                ? EventWhoTries.Attempt(trial, who, sides => Dice(sides))
                : default;

            // The design gets to take a success away, but never to grant one -- the hook runs
            // only when the checks already passed.
            bool succeeded = attempt.Succeeded
                             && !WhoTriesVeto.Vetoes(trial, attempt.Succeeded, Scripts, ScriptHost);

            Message = succeeded ? "Success." : "It does not work.";
            return EventWhoTries.Resolve(trial, succeeded, id => events?.ById(id) is not null);
        };

        Runner.ResolveWhoPays = (toll, chose) =>
        {
            if (Party.Active is not { } payer)
            {
                return default;
            }

            var outcome = EventWhoPays.Resolve(toll, chose, payer, Party,
                                               id => events?.ById(id) is not null);
            Message = outcome.Result switch
            {
                WhoPaysResult.Paid => $"{payer.Name} pays.",
                WhoPaysResult.CannotPay => $"{payer.Name} cannot pay.",
                _ => string.Empty,
            };

            return outcome;
        };

        // Both NPC events gate on the record's kind, so a design character left at the
        // player-character type is invisible to them.
        Runner.ApplyAddNpc = add =>
        {
            var seated = EventNpc.Add(add, Party, FindDesignCharacter, Money,
                                      EventNpc.MaxPartyMembers(design.Globals.MaxPartyMaxPcs));

            Message = seated.Result switch
            {
                AddNpcResult.Joined => $"{seated.Joined!.Name} joins the party.",
                AddNpcResult.PartyFull => "The party is already full.",
                _ => string.Empty,
            };
        };

        Runner.ApplyRemoveNpc = remove =>
        {
            var left = EventNpc.Remove(remove, Party);
            Message = left.Removed ? $"{left.Member!.Name} leaves the party." : string.Empty;
        };

        // There is no vault in this port, so MoneyForVault is reported and discarded -- the goods
        // are still taken either way, which is what the party notices.
        Runner.ApplyTakeItems = take =>
        {
            var taken = EventTakeItems.Apply(take, Party, sides => Dice(sides));
            int goods = taken.Items.Count + taken.Gems.Count + taken.Jewelry.Count;

            Message = goods > 0 || taken.Money > 0
                ? "The party is relieved of their goods."
                : "The party has nothing worth taking.";
        };

        // The journal accumulates on the party, not on World -- the reference serializes it inside
        // PARTY::Serialize, above the quest and special-item records.
        //
        // The `^` token expander (FormattedText.cpp:823) is NOT ported, so identity is passed and
        // a design using ^D or ^a-^z gets the raw token. That is a real difference, not a stub.
        Runner.ApplyJournal = journal =>
        {
            var added = EventJournal.Apply(journal, design.Globals.Journal, Party, text => text);
            Message = added.Added ? "The party records what they have learned." : string.Empty;
        };

        // No IAudioBackend is wired to the engine yet, so the queue is computed and discarded --
        // which still lets the event chain instead of naming itself. An adapter onto
        // StopQueue/QueueSound/PlayQueue is what turns this audible.
        Runner.ApplySound = sound => EventSound.Play(sound, audio: null);

        Runner.ApplyHeal = heal =>
        {
            var healed = EventHeal.Apply(heal, Party, sides => Dice(sides));
            Message = healed.HitPointsRestored > 0 || healed.CursesLifted > 0
                ? $"The party is restored."
                : "Nothing happens.";
        };

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

    /// <summary>Where saved games live — <c>Saves</c> beside the design (<c>rte.SaveDir</c>).</summary>
    public string SaveDirectory { get; }

    /// <summary>The design's currency.</summary>
    public MoneyRules Money { get; }

    /// <summary>Quest, special-item and key state.</summary>
    public WorldState World { get; }

    private GlobalScripts? scripts;

    /// <summary>The design's global scripts, compiled on demand.</summary>
    public GlobalScripts Scripts => scripts ??= new GlobalScripts(design.SpecialAbilities);

    private GameScriptHost? scriptHost;

    /// <summary>The host those scripts talk to.</summary>
    /// <remarks>
    /// One per game rather than per call, because the hook-parameter block lives on it and a
    /// caller leaves arguments there for the script to read.
    /// </remarks>
    public GameScriptHost ScriptHost => scriptHost ??= new GameScriptHost(this);

    /// <summary>
    /// Treasure staged by <c>COMBAT_TREASURE</c> events for the end of the fight
    /// (<c>globalData.combatTreasure</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A <c>COMBAT_TREASURE</c> is not a <c>GIVE_TREASURE_DATA</c>, despite reading into the
    /// same record.</b> It presents nothing at all — its <c>OnInitialEvent</c> resets the menu and
    /// then <i>appends</i> its items and money to this pile (<c>RunEvent.cpp:9591</c>) — where a
    /// give-treasure event opens the pickup screen. Treating the two alike, which this port did
    /// until the difference was noticed, pops a treasure screen in the middle of setting up a
    /// fight.
    /// </para>
    /// <para>
    /// <b>Nothing consumes this yet.</b> The reference hands the pile to the combat results screen,
    /// which adds each item's <c>Experience</c> into the party's share and then clears it
    /// (<c>RunEvent.cpp:19688</c> and <c>:19842</c>). That screen is not ported, so the pile
    /// accumulates and is only observable here — a missing reader, not a defect in what is staged.
    /// </para>
    /// </remarks>
    public List<ItemInstance> StagedCombatTreasure { get; } = [];

    /// <summary>Money staged alongside <see cref="StagedCombatTreasure"/>.</summary>
    public Purse StagedCombatMoney { get; } = new(MoneyRules.Default);

    /// <summary>
    /// The design's global attributes, which its scripts read and write
    /// (<c>globalData.global_asl</c>).
    /// </summary>
    /// <remarks>
    /// Seeded from the design and then written by the engine — the combat results screen puts its
    /// verdict here under <see cref="AttributeList.CombatResultKey"/>, which is how a design
    /// branches on how a fight went.
    /// </remarks>
    public AttributeList Globals { get; } = new();

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
    private bool Step(bool forward, bool triggerEvents = true)
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

        // A guided tour walks the party through squares without setting off what is on them --
        // the reference calls movePartyForward(0) rather than the ordinary move, and only fires
        // an event at the destination if the tour asks for one.
        if (triggerEvents)
        {
            TriggerEvent();
        }

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
    /// <summary>
    /// The game as a logic block's terminals and actions see it — see
    /// <see cref="GameLogicBlockHost"/>.
    /// </summary>
    public GameLogicBlockHost? LogicBlockHost { get; private set; }

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
                                     name => design.Art(name, SurfaceKind.Icon), partyIcons,
                                     id => design.Spell(id));
        Combat.ItemInfo = id => design.Item(id);
        Combat.MonsterInfo = id => design.Monster(id);
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
            var spoils = SettleCombat(session, finished as CombatEvent);
            Combat = null;
            CurrentEvent = null;

            Message = spoils.Result switch
            {
                CombatResult.Win => spoils.Experience > 0
                    ? $"The party is victorious, and receives {spoils.Experience} "
                      + "experience points."
                    : "The party is victorious!",
                CombatResult.Flee => "The party has run away.",
                CombatResult.LoseButNeverDies => "The party has survived.",
                _ => "The party has fallen.",
            };

            uint? chain = finished is null
                ? null
                : EventChain.Next(finished.Base, outcome == CombatOutcome.PartyWon);

            // The fallen's possessions go through the ordinary treasure screen, which is what the
            // reference builds a GIVE_TREASURE_DATA for and pushes ahead of the chain.
            if (CombatAftermath.TreasureScreen(spoils, finished?.Base) is { } pile)
            {
                pendingChain = chain;
                StartEvent(pile);

                if (Runner.IsActive || pendingChain is null)
                {
                    // Either the screen is up and will release the chain when it closes, or it
                    // finished at once and Apply has already followed it.
                    return changed || !session.IsActive;
                }

                // StartEvent declined it -- with no font nothing can be presented -- so nothing
                // will ever release the chain. Follow it here instead.
                pendingChain = null;
            }

            FollowChain(chain);
        }

        return changed || !session.IsActive;
    }

    /// <summary>
    /// What a finished fight is worth (<c>COMBAT_RESULTS_MENU_DATA::OnInitialEvent</c>,
    /// <c>RunEvent.cpp:19669</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order matters and is the reference's: clear the lingering spells, total the experience
    /// (monsters first, then the treasure's own items), share it out, then gather the treasure.
    /// An item's experience is counted from the <i>treasure</i> and added <i>before</i> the
    /// distribution, so finding a magic sword pays for the finding.
    /// </para>
    /// <para>
    /// <b>Fled party members are restored to <c>Okay</c> on the way out</b>, on both a win and a
    /// flight (<c>RunEvent.cpp:19787</c>) — otherwise a character who ran once would stay fled for
    /// the rest of the game. They are restored <i>after</i> the experience is shared, so a
    /// character who fled does not share in the fight they left.
    /// </para>
    /// </remarks>
    private CombatSpoils SettleCombat(CombatSession session, CombatEvent? combat)
    {
        session.Lingering.Clear();

        var result = CombatAftermath.ResultOf(session.Outcome, session.Combatants,
                                              partyNeverDies: combat?.PartyNeverDies != 0);

        // What a design's scripts branch on. Written with ASLF_MODIFIED, as the results screen
        // does, because this is a change during play rather than a first insertion.
        Globals.Insert(AttributeList.CombatResultKey, CombatAftermath.ResultText(result),
                       AttributeFlags.Modified);

        if (result != CombatResult.Win)
        {
            RestoreFled();
            return new CombatSpoils(result, 0, [], []);
        }

        var (items, money) = CombatAftermath.Loot(
            session.Combatants, id => design.Item(id),
            noMonsterTreasure: combat?.NoMonsterTreasure != 0);

        int experience = CombatAftermath.ExperienceFor(
            session.Combatants, ExperienceWorth,
            partyNoExperience: combat?.PartyNoExperience != 0);

        if (combat?.PartyNoExperience == 0)
        {
            experience += CombatAftermath.ExperienceIn(items, id => design.Item(id));
        }

        CombatAftermath.Distribute(Party.Members, experience);
        RestoreFled();

        return new CombatSpoils(result, experience, items, money);
    }

    /// <summary>
    /// What one fallen combatant is worth (<c>getCharExpWorth</c>).
    /// </summary>
    /// <remarks>
    /// Read off the monster database by name, as the encounter builder placed it. A combatant with
    /// no monster record — a party member, or a name the design does not hold — is worth nothing.
    /// </remarks>
    private int ExperienceWorth(Combatant combatant) =>
        design.Monster(combatant.Name)?.ExperienceValue ?? 0;

    /// <summary>
    /// The chain to follow once a fight's treasure screen is done with.
    /// </summary>
    /// <remarks>
    /// The reference pushes the treasure event ahead of the combat event's own exit, so the chain
    /// is followed after the screen rather than instead of it (<c>RunEvent.cpp:19660</c>). Holding
    /// the destination is this port's equivalent of that push.
    /// </remarks>
    private uint? pendingChain;

    /// <summary>
    /// Walks a <c>GUIDED_TOUR</c> (<c>RunEvent.cpp:14568</c> and <c>:14652</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The tour runs to its end in one go rather than a step at a time.</b> The reference drives
    /// it from a timer — <c>TASKTIMER_GuidedTour</c> — so the player watches the party walk and a
    /// <c>Pause</c> step holds the caption on screen. This port has no task scheduler to hang that
    /// timer on (§4.4), so the walk is applied at once and only the last caption survives. Where
    /// the party ends up, which way it faces and whether the destination's event fires are all
    /// correct; the animation is not there.
    /// </para>
    /// <para>
    /// <b>An out-of-range starting square abandons the event outright.</b> No steps run, no chain
    /// is followed — the reference pops the event. Returning "did not happen" instead would send a
    /// design down its not-happened branch, which is a route it never wrote.
    /// </para>
    /// </remarks>
    private bool? RunGuidedTour(GuidedTour tour)
    {
        if (tour.UseStartLocation != 0)
        {
            int width = Map?.Width ?? 0;
            int height = Map?.Height ?? 0;

            if (!GuidedTourPath.StartIsValid(tour, width, height))
            {
                CurrentEvent = null;
                Message = $"[guided tour starts at ({tour.TourX}, {tour.TourY}), " +
                          "which is off this level]";
                return null;
            }

            X = tour.TourX;
            Y = tour.TourY;
            Facing = (Facing)(tour.Facing & 3);
        }

        string caption = string.Empty;

        foreach (var step in GuidedTourPath.Steps(tour))
        {
            switch ((TourMove)step.Step)
            {
                case TourMove.Forward:
                    Step(forward: true, triggerEvents: false);
                    break;

                case TourMove.Left:
                    Facing = (Facing)(((int)Facing + 3) & 3);
                    break;

                case TourMove.Right:
                    Facing = (Facing)(((int)Facing + 1) & 3);
                    break;

                case TourMove.Pause:
                    break;
            }

            if (step.Text.Length > 0)
            {
                caption = step.Text;
            }
        }

        Message = caption;

        // TASKMSG_ExecuteEvent: the tour can hand over to whatever sits where it stopped.
        if (tour.ExecuteEvent != 0)
        {
            TriggerEvent();
        }

        return true;
    }

    /// <summary>
    /// A character from the design's own list, by id.
    /// </summary>
    /// <remarks>
    /// Matched on <c>CharacterId</c> rather than <c>Name</c>, because that is what
    /// <c>PARTY::isNPCinParty</c> compares — and case-sensitively, since <c>CString::operator==</c>
    /// is <c>strcmp</c>. Both NPC events additionally require the record's kind to be
    /// <see cref="EventNpc.NpcType"/>, which this does not check; <see cref="EventNpc.HaveNpc"/>
    /// does.
    /// </remarks>
    private CharacterRecord? FindDesignCharacter(string characterId) =>
        design.Globals.Characters.FirstOrDefault(
            c => string.Equals(c.CharacterId, characterId, StringComparison.Ordinal));

    /// <summary>
    /// Adds a <c>COMBAT_TREASURE</c>'s contents to the pile for the end of the fight
    /// (<c>COMBAT_TREASURE::OnInitialEvent</c>, <c>RunEvent.cpp:9591</c>).
    /// </summary>
    /// <remarks>
    /// <b>Appends rather than replaces</b>, so several such events before one fight accumulate —
    /// which is how a design gives a multi-stage encounter one shared reward. Nothing clears the
    /// pile here, because the screen that would is not ported.
    /// </remarks>
    private void StageCombatTreasure(TreasureEvent staged)
    {
        StagedCombatTreasure.AddRange(staged.Items.Items);

        for (int i = 0; i < staged.Money.Coins.Count; i++)
        {
            if (staged.Money.Coins[i] != 0)
            {
                StagedCombatMoney.Add((ItemClass)(i + 1), staged.Money.Coins[i]);
            }
        }

        foreach (var gem in staged.Money.Gems)
        {
            StagedCombatMoney.AddGem(gem);
        }

        foreach (var piece in staged.Money.Jewelry)
        {
            StagedCombatMoney.AddJewelry(piece);
        }

        Message = string.Empty;
    }

    /// <summary>Puts anyone who ran back on their feet, as the results screen does.</summary>
    private void RestoreFled()
    {
        foreach (var member in Party.Members.Where(m => m.Status == CharacterStatus.Fled))
        {
            member.Status = CharacterStatus.Okay;
        }
    }

    /// <summary>
    /// Runs an event, as stepping onto one or chaining to one does
    /// (<c>PushEvent</c>, <c>RunEvent.cpp</c> throughout).
    /// </summary>
    /// <remarks>
    /// Public because events are not only reached by walking: a chain reaches them, and a design's
    /// scripts will too. It is also the only way to drive a fight without walking a party to it —
    /// most combat events sit at (−1, −1), meaning they are chained to rather than stepped on.
    /// </remarks>
    public void StartEvent(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        CurrentEvent = gameEvent;

        if (gameEvent is CombatEvent combat && StartCombat(combat))
        {
            return;
        }

        // CHAIN_EVENT replaces itself with its target rather than chaining to it
        // (RunEvent.cpp:10974). The difference is real: its own chainEventHappen is never
        // consulted, and a target the level does not contain ends the run outright -- the
        // reference pops the event rather than falling through to anything.
        if (gameEvent is ChainEvent chain)
        {
            CurrentEvent = null;
            FollowChain(chain.Chain);
            return;
        }

        // FLOW_CONTROL_EVENT_DATA modifies a global and branches on it, with no presentation at
        // all. Like CHAIN_EVENT it can replace itself, so it cannot go through
        // ExecuteWithoutInput -- that path always ends by following the ordinary chain.
        // A guided tour can abandon itself outright, which ExecuteWithoutInput cannot express --
        // its bool answer always ends in a chain, and "did not happen" is a route the design
        // never wrote.
        if (gameEvent is GuidedTour tour)
        {
            CurrentEvent = null;
            if (RunGuidedTour(tour) is bool walked)
            {
                FollowChain(EventChain.Next(tour.Base, walked));
            }

            return;
        }

        if (gameEvent is FlowControlEvent flow)
        {
            CurrentEvent = null;
            var outcome = FlowControl.Run(flow, Globals, id => events?.ById(id) is not null);

            if (outcome.Stop)
            {
                Message = $"[flow control to event {flow.DestinationId}, " +
                          "which this level does not contain]";
                return;
            }

            FollowChain(outcome.GoTo ?? EventChain.Next(flow.Base, happened: true));
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

            // COMBAT_TREASURE reads into the same record as GIVE_TREASURE_DATA and behaves
            // nothing like it: no screen, no pickup -- it stages for the end of the fight. This
            // case must come first, because a combat treasure has SilentGiveToActiveChar of 0 and
            // would otherwise fall through to the presenter.
            case TreasureEvent staged
                when staged.Base.EventType == (int)EventType.CombatTreasure:
                StageCombatTreasure(staged);
                return true;

            // Only the silent form runs without input; the other opens a screen, so it falls
            // through to the presenter and names itself there rather than being consumed here.
            case TreasureEvent { SilentGiveToActiveChar: not 0 } treasure:
                return GiveTreasure(treasure);

            case GainExperienceEvent gain:
                return GainExperience(gain);

            // UTILITIES_EVENT_DATA presents nothing: OnInitialEvent clears the menu and OnIdle
            // does the arithmetic and chains. endPlay pushes EXIT_DATA, which is how a design
            // ends the game -- there is no other route to it.
            case UtilitiesEvent utilities:
                var outcome = Utilities.Run(utilities, World);
                if (outcome.EndsPlay)
                {
                    Running = false;
                    Message = "[the design ends play here]";
                }

                return true;

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
    /// <summary><c>destEP</c> for a destination resolved by a global script.</summary>
    /// <remarks>
    /// <c>HandleTransfer</c> (<c>RunEvent.cpp:975</c>) formats <c>/level+1/x/y</c> into a name and
    /// asks <c>RunGlobalScript("TeleporterDestinations", …)</c> for the real one, so the three
    /// stored fields are <b>arguments, not coordinates</b>.
    /// </remarks>
    public const int ScriptedDestination = -3;

    /// <summary>
    /// Carries out a transfer (<c>GameEvent::HandleTransfer</c>, <c>RunEvent.cpp:959</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>destEP</c> has three meanings and only one of them is "use destX and destY".</b> At
    /// zero or above it is an <i>index into the destination level's entry-point table</i> and the
    /// stored coordinates are ignored entirely (<c>Party.cpp:3495</c>); at
    /// <see cref="ScriptedDestination"/> the fields are script arguments; only otherwise are they
    /// the square to arrive on. This port read them literally in all three cases, which sent a
    /// design using entry points to whatever those fields happened to hold — silently, and to the
    /// wrong square.
    /// </para>
    /// <para>
    /// <b>Stairs, teleporters and module transfers are the same operation.</b>
    /// <c>TRANSFER_EVENT_DATA</c>'s runner never branches on which of the three it is; the
    /// destination decides, and the only fork is whether <c>destLevel</c> is the level already
    /// loaded.
    /// </para>
    /// </remarks>
    private bool Teleport(TransferEvent transfer)
    {
        var destination = transfer.Destination;

        if (destination.DestEntryPoint == ScriptedDestination)
        {
            // The three fields are arguments to a script named after them. A design that does not
            // author one gets nothing -- the reference logs and then transfers to whatever the
            // fields held, which is a square it never named; refusing is the safer half of that.
            if (TeleporterDestinations.Resolve(destination, Scripts, ScriptHost) is not { } resolved)
            {
                Message = "[no TeleporterDestinations script for "
                          + TeleporterDestinations.ScriptName(destination.DestLevel,
                                                              destination.DestX, destination.DestY)
                          + "]";
                return false;
            }

            destination = resolved;
        }

        if (destination.DestLevel != LevelIndex)
        {
            Message = $"[Teleporter to level {destination.DestLevel} "
                      + "-- changing level is not implemented]";
            return false;
        }

        int x = destination.DestX;
        int y = destination.DestY;

        if (destination.DestEntryPoint >= 0)
        {
            if (EntryPoint(destination.DestEntryPoint) is not { } arrival)
            {
                Message = $"[teleporter names entry point {destination.DestEntryPoint}, "
                          + "which this level does not define]";
                return false;
            }

            (x, y) = (arrival.X, arrival.Y);
        }

        X = x;
        Y = y;
        Facing = (Facing)(destination.Facing & 3);
        Message = $"You are somewhere else: ({X}, {Y}) facing {Facing}.";
        return true;
    }

    /// <summary>An entry point on the current level, or null when there is no such slot.</summary>
    /// <remarks>
    /// The reference reads these from the level it has just loaded. Only same-level transfers get
    /// this far, so the current level's table is the right one.
    /// </remarks>
    private EntryPoint? EntryPoint(int index)
    {
        if (design.Globals.Levels?.Levels.TryGetValue((uint)LevelIndex, out var stats) != true)
        {
            return null;
        }

        return index >= 0 && index < stats!.EntryPoints.Count ? stats.EntryPoints[index] : null;
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

    /// <summary>
    /// The combat cursor's art, named by <c>DEF_COMBAT_CURSOR</c> in the design's config.
    /// </summary>
    /// <remarks>
    /// The reference keeps a hard-coded default (<c>cu_DefCC.png</c>, <c>PicSlot.cpp:104</c>) that
    /// config overrides; every reference design sets the token, so the fallback is unused here.
    /// </remarks>
    private Surface? CursorArt()
    {
        design.Config.Rewind();
        return design.Config.TryGetValue("DEF_COMBAT_CURSOR", out string name)
               && !string.IsNullOrEmpty(name)
            ? design.Art(name)
            : null;
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
    /// <remarks>
    /// <b>A fight's treasure screen borrows the combat event's chain, not its own.</b> The
    /// synthesised event carries the combat event's base, so its chain fields point wherever the
    /// combat event pointed — following them here would run the same destination twice. The
    /// destination held back when the screen was raised wins instead.
    /// </remarks>
    private void Apply(EventStep step)
    {
        CurrentEvent = null;
        Runner.Cancel();

        if (pendingChain is { } afterCombat)
        {
            pendingChain = null;
            FollowChain(afterCombat);
            return;
        }

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
    /// <summary>
    /// How many events one step onto a cell may run before the chain is assumed to be a loop.
    /// </summary>
    /// <remarks>
    /// <b>Not a rule from the reference</b>, which has no limit: it pushes and pops an event stack
    /// and a design that chains an event to itself simply hangs. Chains of chains are ordinary —
    /// <c>Case.dsn</c> alone holds 165 <c>CHAIN_EVENT</c>s — so a cycle is a mistake a design can
    /// easily make, and a hang gives the author nothing to go on. The cap is far above any real
    /// chain and reports where it stopped.
    /// </remarks>
    public const int MaxChainDepth = 256;

    private int chainDepth;

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

        if (chainDepth >= MaxChainDepth)
        {
            Message = $"[event chain exceeded {MaxChainDepth} steps at event {target} -- " +
                      "the design most likely chains an event back to itself]";
            return;
        }

        chainDepth++;
        try
        {
            StartEvent(next);
        }
        finally
        {
            chainDepth--;
        }
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
                Combat.Render(screen, CombatArt(), new SurfaceRect(vx, vy, vr, vb),
                              cursorArt: CursorArt());
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
