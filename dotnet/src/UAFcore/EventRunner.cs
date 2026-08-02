using UAF.Media;
using UAF.Serialization;

namespace UAFcore;

/// <summary>What an event did with the last input.</summary>
public enum EventStepKind
{
    /// <summary>Still on screen, waiting for the player.</summary>
    Running,

    /// <summary>Finished, and another event follows.</summary>
    Chain,

    /// <summary>Finished, with nothing after it.</summary>
    Finished,
}

/// <summary>The result of feeding an event one input.</summary>
public readonly record struct EventStep(EventStepKind Kind, uint ChainTo = 0)
{
    public static readonly EventStep Running = new(EventStepKind.Running);

    public static readonly EventStep Finished = new(EventStepKind.Finished);

    public static EventStep To(uint id) =>
        id > 0 ? new EventStep(EventStepKind.Chain, id) : Finished;
}

/// <summary>
/// Presents one event and takes the player's answer.
/// </summary>
/// <remarks>
/// <para>
/// Each event type in the original is a class with <c>OnInitialEvent</c>, <c>OnKeypress</c> and
/// <c>OnDraw</c>, sharing the one global <c>menu</c> and the one global <c>textData</c>. This is
/// the same shape with the state made explicit: <see cref="Begin"/> is <c>OnInitialEvent</c>,
/// <see cref="Handle"/> is <c>OnKeypress</c>, <see cref="Render"/> is <c>OnDraw</c>.
/// </para>
/// <para>
/// <b>Scope: the five types that needed only text and a menu.</b> Everything else is recognised and
/// named rather than run — the same rule the engine's event dispatch already follows, because
/// silently doing nothing is indistinguishable from an empty cell and that difference is the whole
/// signal while the executor is being built out.
/// </para>
/// <para>
/// <b>Return is the only key that commits.</b> Every one of these types tests
/// <c>key != KC_RETURN</c> first and routes everything else to the menu, so arrows move the
/// selection and letters pick an entry outright — the shortcut path synthesises a Return of its
/// own, which is why <see cref="MenuInputResult.Accepted"/> and a real Return are handled together.
/// </para>
/// </remarks>
public sealed class EventRunner
{
    /// <summary><c>MAX_BUTTONS</c> (<c>Shared/GameEvent.h:50</c>) — for both list and button forms.</summary>
    public const int MaxButtons = 5;

    private BitmapFont? font;

    /// <summary>The event being presented, or null.</summary>
    public IGameEvent? Current { get; private set; }

    /// <summary>The event's text, wrapped to the box.</summary>
    public TextDisplayData Text { get; } = new();

    /// <summary>The event's menu.</summary>
    public Menu Menu { get; } = new();

    /// <summary>Where the menu sits, resolved from the event type's anchor.</summary>
    public TextBoxMetrics Box { get; private set; } = TextBoxMetrics.Default;

    /// <summary>A line describing an event this port presents but does not run.</summary>
    public string? Unimplemented { get; private set; }

    /// <summary>Whether an event is on screen.</summary>
    public bool IsActive => Current is not null;

    /// <summary>
    /// Whether the event on screen replaces the dungeon view rather than drawing over it.
    /// </summary>
    /// <remarks>
    /// The distinction is in the reference's screen routines. A text or question event runs under
    /// <c>UpdateAdventureScreen</c>, which calls <c>updateViewport</c>; the treasure screen runs
    /// under <c>UpdateSmallSprite</c> (<c>Screen.cpp:340</c>), which clears the adventure
    /// background and blits the zone's treasure picture where the viewport was, never touching the
    /// viewport itself. Every form-bearing screen to come — character stats, spells, camp — is in
    /// the second group.
    /// </remarks>
    public bool OwnsScreen => Current is TreasureEvent;

    /// <summary>
    /// Whether what is on screen replaces the party roster as well as the dungeon view.
    /// </summary>
    /// <remarks>
    /// <b>Screen ownership is not one flag.</b> The treasure screen keeps the roster —
    /// <c>UpdateSmallSprite</c> calls <c>displayPartyNames</c> — while the character sheet drops it,
    /// since <c>UpdateViewCharacterScreen</c> (<c>Screen.cpp:620</c>) draws only the frame, the
    /// picture, the menu and the stats. The sheet is also the thing that occupies that corner of
    /// the screen, so leaving the roster under it produces two columns of text on top of each other.
    /// </remarks>
    public bool CoversRoster => Stats is not null;

    /// <summary>
    /// Starts presenting <paramref name="gameEvent"/> (<c>OnInitialEvent</c>).
    /// </summary>
    /// <returns>
    /// <see cref="EventStepKind.Running"/> when the event is waiting for input. A type with nothing
    /// to ask — or a question whose options are all empty — finishes here without ever drawing,
    /// which is what the original's <c>if (count == 0) ChainHappened();</c> does.
    /// </returns>
    public EventStep Begin(IGameEvent gameEvent, BitmapFont font, TextBoxMetrics box,
                           MenuAnchors anchors)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(anchors);

        Current = gameEvent;
        this.font = font;
        Box = box;
        Unimplemented = null;

        Menu.Reset();
        Text.Clear();
        Items = null;
        Stats = null;
        TakeRequested = false;

        return gameEvent switch
        {
            TextEvent text => BeginTextStatement(text, anchors),
            YesNoEvent yesNo => BeginYesNo(yesNo, anchors),
            NpcSaysEvent npc => BeginNpcSays(npc, anchors),
            QuestionEvent question => BeginQuestion(question, anchors),
            TreasureEvent treasure => BeginTreasure(treasure, anchors),
            RandomEvent random => BeginRandom(random, anchors),
            SpecialItemEvent special => BeginSpecialItem(special, anchors),
            QuestEvent quest => BeginQuest(quest, anchors),
            DamageEvent damage => BeginPressEnter(damage.Base.Text, anchors),
            HealPartyEvent heal => BeginPressEnter(heal.Base.Text, anchors),
            _ => BeginUnsupported(gameEvent),
        };
    }

    /// <summary>Applies a damage event to the party; set by the host.</summary>
    /// <remarks>
    /// The runner owns no party, the same reason it cannot hand over a treasure. See
    /// <see cref="EventDamage"/>.
    /// </remarks>
    public Action<DamageEvent>? ApplyDamage { get; set; }

    /// <summary>Applies a heal event to the party; set by the host. See <see cref="EventHeal"/>.</summary>
    public Action<HealPartyEvent>? ApplyHeal { get; set; }

    /// <summary>
    /// Text and a Return, for the events whose whole presentation is that.
    /// </summary>
    /// <remarks>
    /// <c>GIVE_DAMAGE_DATA</c> and <c>HEAL_PARTY_DATA</c> both set this menu in
    /// <c>OnInitialEvent</c> (<c>RunEvent.cpp:9989</c> and <c>:10216</c>) and do all their work in
    /// <c>OnKeypress</c> — so the party is neither hurt nor healed until the player commits, and a
    /// run abandoned before that leaves them untouched.
    /// </remarks>
    private EventStep BeginPressEnter(string text, MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, title: null, MenuOrientation.Horizontal,
                       ("PRESS ENTER TO CONTINUE", 7));
        ShowText(text);
        return EventStep.Running;
    }

    /// <summary>Applies a quest event's outcome; set by the host.</summary>
    /// <remarks>
    /// Quest, special-item and key state all live on <see cref="WorldState"/>, which the runner
    /// does not have. Takes whether the party accepted and returns where to go. See
    /// <see cref="Quests"/>.
    /// </remarks>
    public Func<QuestEvent, bool, QuestOutcome>? ResolveQuest { get; set; }

    /// <summary>
    /// <c>QUEST_EVENT_DATA::OnInitialEvent</c> (<c>RunEvent.cpp:12300</c>).
    /// </summary>
    /// <remarks>
    /// The two automatic operations present text and a Return; everything else asks Yes or No —
    /// including <c>QA_Impossible</c>, which asks and then refuses whatever the answer.
    /// </remarks>
    private EventStep BeginQuest(QuestEvent quest, MenuAnchors anchors)
    {
        if (Quests.AsksTheQuestion(quest.Operation))
        {
            SetupFixedMenu(anchors, title: null, MenuOrientation.Horizontal, ("YES", 0), ("NO", 0));
        }
        else
        {
            SetupFixedMenu(anchors, title: null, MenuOrientation.Horizontal,
                           ("PRESS ENTER TO CONTINUE", 7));
        }

        ShowText(quest.Base.Text);
        return EventStep.Running;
    }

    /// <summary>Applies the quest and takes its branch.</summary>
    private EventStep FinishQuest(QuestEvent quest)
    {
        // The menu reports Yes as entry 0 here and the reference counts from 1, so the host is
        // handed the reference's numbering rather than this menu's.
        bool accepted = Quests.IsAccepted(quest.Operation, Menu.ActiveItem + 1);

        var outcome = ResolveQuest?.Invoke(quest, accepted)
                      ?? new QuestOutcome(accepted, null, Stop: false);

        if (outcome.GoTo is uint target)
        {
            Current = null;
            return EventStep.To(target);
        }

        if (outcome.Stop)
        {
            Current = null;
            return EventStep.Finished;
        }

        return Complete(happened: true);
    }

    /// <summary>Applies a special-item event's give/take list; set by the host.</summary>
    /// <remarks>
    /// Special items and keys are global rather than carried, so they live on
    /// <see cref="WorldState"/> — which the runner does not have. See <see cref="SpecialItems"/>.
    /// </remarks>
    public Action<SpecialItemEvent>? ApplySpecialItems { get; set; }

    /// <summary>
    /// <c>SPECIAL_ITEM_KEY_EVENT_DATA::OnInitialEvent</c> (<c>RunEvent.cpp:12783</c>) — text and
    /// a Return, with the giving and taking done on the way out.
    /// </summary>
    private EventStep BeginSpecialItem(SpecialItemEvent special, MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, title: null, MenuOrientation.Horizontal,
                       ("PRESS ENTER TO CONTINUE", 7));
        ShowText(special.Base.Text);
        return EventStep.Running;
    }

    /// <summary>
    /// Picks a random event's branch; set by the host.
    /// </summary>
    /// <remarks>
    /// The choice needs the level's event list — a branch pointing at a deleted event does not
    /// count — and the dice the rest of the engine rolls with. The runner has neither, the same
    /// reason it cannot hand over a treasure. See <see cref="RandomEventChoice"/>.
    /// </remarks>
    public Func<RandomEvent, uint?>? ChooseRandomBranch { get; set; }

    /// <summary>
    /// <c>RANDOM_EVENT_DATA::OnInitialEvent</c> (<c>RunEvent.cpp:12536</c>) — text and a Return.
    /// </summary>
    /// <remarks>
    /// The event presents like a text statement and then, on Return, becomes whichever event it
    /// rolled. Nothing on screen says which way it went, which is the point.
    /// </remarks>
    private EventStep BeginRandom(RandomEvent random, MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, title: null, MenuOrientation.Horizontal,
                       ("PRESS ENTER TO CONTINUE", 7));
        ShowText(random.Base.Text);
        return EventStep.Running;
    }

    /// <summary>Rolls the branch and replaces this event with it.</summary>
    /// <remarks>
    /// A roll that finds nothing to take falls back on the event's own chain — the reference's
    /// <c>ChainHappened()</c> — rather than ending the run.
    /// </remarks>
    private EventStep ChooseRandom(RandomEvent random)
    {
        if (ChooseRandomBranch?.Invoke(random) is uint chosen && chosen > 0)
        {
            Current = null;
            return EventStep.To(chosen);
        }

        return Complete(happened: true);
    }

    /// <summary>Wraps an event's text into the box, as <c>FormatDisplayText</c> does.</summary>
    private void ShowText(string body)
    {
        string decoded = ArchiveStringConventions.Decode(body ?? string.Empty);
        if (decoded.Length == 0)
        {
            return;
        }

        TextFormatter.Format(decoded, Box.Width, font!, Text);
        Text.LinesPerBox = Box.Lines;
        Text.FirstBox();
    }

    /// <summary>
    /// <c>TEXT_EVENT_DATA::OnInitialEvent</c> (<c>RunEvent.cpp:6057</c>) — text and a two-entry bar.
    /// </summary>
    /// <remarks>
    /// The <c>**SHAZAM</c> prefix that hands the text to the GPDL interpreter
    /// (<c>RunEvent.cpp:6063</c>) is not handled: the scripting VM exists but is not wired to the
    /// engine, so such an event is presented as ordinary text rather than executed.
    /// </remarks>
    private EventStep BeginTextStatement(TextEvent text, MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, title: null, MenuOrientation.Horizontal,
                       ("EXIT", 1), ("PRESS ENTER TO CONTINUE", 7));
        ShowText(text.Base.Text);
        return EventStep.Running;
    }

    /// <summary>Resolves a carried item's id to its display name; set by the host.</summary>
    /// <remarks>
    /// An item's id is its <c>m_uniqueName</c> while the fuller <c>m_idName</c> is what a player
    /// should see, so the list cannot be built from the event alone.
    /// </remarks>
    public Func<string, string?>? ItemNames { get; set; }

    /// <summary>The treasure list, while a treasure event is on screen.</summary>
    public ItemsForm? Items { get; private set; }

    /// <summary>Builds the active character's sheet; set by the host.</summary>
    /// <remarks>
    /// The runner has no party, so it cannot know whose sheet VIEW should show any more than it
    /// can hand over a treasure.
    /// </remarks>
    public Func<CharacterSheet?>? ActiveCharacterSheet { get; set; }

    /// <summary>The character sheet, while VIEW is showing it.</summary>
    public CharStatsForm? Stats { get; private set; }

    /// <summary>How many rows the treasure list shows at once (<c>Items_Per_Page</c>).</summary>
    public const int TreasurePageSize = 8;

    /// <summary>
    /// The six entries, zero-based. The reference's <c>setItemInactive</c> takes these one-based,
    /// so its 2/3/4/5 are TAKE/POOL/SHARE/DETECT.
    /// </summary>
    private const int TreasureView = 0;
    private const int TreasureTake = 1;
    private const int TreasurePool = 2;
    private const int TreasureShare = 3;
    private const int TreasureDetect = 4;
    private const int TreasureExit = 5;

    /// <summary>
    /// <c>GIVE_TREASURE_DATA::OnInitialEvent</c>'s non-silent path (<c>RunEvent.cpp:6572</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The silent form never reaches here — it hands everything to the active character without
    /// drawing anything, and <c>Game</c> consumes it before the runner is asked.
    /// </para>
    /// <para>
    /// <b>Not ported from this screen:</b> the treasure picture, which comes from the zone rather
    /// than the event, and the four menu entries that open screens of their own. Those name
    /// themselves when chosen rather than doing nothing, so a design that relies on them says so.
    /// </para>
    /// </remarks>
    private EventStep BeginTreasure(TreasureEvent treasure, MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, title: null, MenuOrientation.Horizontal,
                       ("VIEW", 0), ("TAKE", 0), ("POOL", 0), ("SHARE", 0), ("DETECT", 0),
                       ("EXIT", 1));

        // The event's own text if it has any, and the engine's line if it does not.
        string message = ArchiveStringConventions.Decode(treasure.Base.Text ?? string.Empty);
        ShowText(message.Length > 0 ? message : "You Have Found Treasure!");

        Items = new ItemsForm(TreasurePageSize);
        var rows = new List<ItemsFormRow>();
        foreach (var carried in treasure.Items.Items)
        {
            string name = ItemNames?.Invoke(carried.ItemId) ?? carried.ItemId;
            rows.Add(new ItemsFormRow(string.Empty, carried.Quantity.ToString(), string.Empty,
                                      name));
        }

        if (font is not null)
        {
            // No READY or COST column: this is a pile on the floor, not an inventory or a shop.
            Items.Populate(font, rows, useReady: false, useCost: false);
        }

        // GIVE_TREASURE_DATA::OnUpdateUI (RunEvent.cpp:6694) greys entries out rather than hiding
        // them. TAKE goes when there is nothing to take; POOL and SHARE are mutually exclusive on
        // whether the money is pooled; DETECT needs a caster and a zone that allows magic, neither
        // of which this port models yet, so it is always off rather than offered and broken.
        bool pooled = false;
        Menu.SetItemEnabled(TreasureTake, rows.Count > 0);
        Menu.SetItemEnabled(TreasurePool, !pooled);
        Menu.SetItemEnabled(TreasureShare, pooled);
        Menu.SetItemEnabled(TreasureDetect, false);

        return EventStep.Running;
    }

    /// <summary>
    /// The treasure menu (<c>GIVE_TREASURE_DATA::OnKeypress</c>, <c>RunEvent.cpp:6612</c>).
    /// </summary>
    private EventStep ChooseTreasure(TreasureEvent treasure)
    {
        int chosen = Menu.ActiveItem;

        switch (chosen)
        {
            case TreasureTake:
                TakeRequested = true;
                return Complete(happened: true);

            case TreasureExit:
                return Complete(happened: true);

            case TreasureView when ActiveCharacterSheet?.Invoke() is { } sheet && font is not null:
                // VIEW_CHARACTER_DATA is pushed as its own event in the reference; here the sheet
                // is drawn over the treasure screen and the next commit dismisses it, which is the
                // same flow from the player's side without an event stack to build first.
                Stats = new CharStatsForm();
                Stats.Populate(font, sheet);
                return EventStep.Running;

            default:
                // POOL, SHARE and DETECT each push a screen this port has not built.
                string label = chosen >= 0 && chosen < Menu.Count
                    ? BitmapFont.Decode(Menu.Items[chosen].Text)
                    : "treasure option";
                Unimplemented = $"[{label} here -- not implemented]";
                return EventStep.Running;
        }
    }

    /// <summary>
    /// Set when the player chose TAKE, for the host to act on — the runner owns no party.
    /// </summary>
    public bool TakeRequested { get; private set; }

    /// <summary>
    /// <c>QUESTION_YES_NO::OnInitialEvent</c> (<c>RunEvent.cpp:6210</c>).
    /// </summary>
    /// <remarks>
    /// The follow-up text the original shows on Yes or No before chaining
    /// (<c>GetEventText2</c>, <c>:6232</c>) is skipped: it is a second presentation state, and the
    /// branch that has it and the branch that does not chain to the same place. What is lost is a
    /// page of prose, not a destination.
    /// </remarks>
    private EventStep BeginYesNo(YesNoEvent yesNo, MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, "CHOOSE: ", MenuOrientation.Horizontal, ("YES", 0), ("NO", 0));
        ShowText(yesNo.Base.Text);
        return EventStep.Running;
    }

    /// <summary><c>NPC_SAYS_DATA::OnInitialEvent</c> (<c>RunEvent.cpp:10893</c>).</summary>
    private EventStep BeginNpcSays(NpcSaysEvent npc, MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, title: null, MenuOrientation.Horizontal,
                       ("PRESS ENTER TO CONTINUE", 7));
        ShowText(npc.Base.Text);
        return EventStep.Running;
    }

    /// <summary>
    /// The two question forms (<c>RunEvent.cpp:13152</c> and <c>:13293</c>), which differ only in
    /// where they sit and how far apart their entries are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every one of the five slots becomes a menu entry, empty or not.</b> An empty label is
    /// added as <c>" "</c> and disabled, so entry <i>n</i> is always option <i>n</i> — which is
    /// what lets the original index straight into <c>buttons[UserResult-1]</c>. Adding only the
    /// non-empty ones would silently pick the wrong option whenever a design leaves a gap.
    /// </para>
    /// <para>
    /// An option that is present in the record but flagged not-<c>Present</c> is disabled too
    /// (<c>OnUpdateUI</c>, <c>:13240</c>), so it keeps its slot without being selectable.
    /// </para>
    /// </remarks>
    private EventStep BeginQuestion(QuestionEvent question, MenuAnchors anchors)
    {
        bool list = question.Base.EventType == (int)EventType.QuestionList;

        Menu.Orientation = list ? MenuOrientation.Vertical : MenuOrientation.Horizontal;
        Menu.ItemSeparation = list ? 2 : 7;
        Menu.SetStartCoord(list ? MenuAnchor.DefaultTextBox : MenuAnchor.DefaultHorizontal,
                           anchors);

        if (list && question.Title.Length > 0)
        {
            Menu.SetTitle(ArchiveStringConventions.Decode(question.Title));
        }

        int offered = 0;
        for (int i = 0; i < MaxButtons; i++)
        {
            var option = i < question.Options.Count ? question.Options[i] : null;
            string label = ArchiveStringConventions.Decode(option?.Label ?? string.Empty);

            if (label.Length == 0)
            {
                Menu.AddItem(" ");
                Menu.SetItemEnabled(i, false);
                continue;
            }

            Menu.AddItem(label);
            if (option!.Present == 0)
            {
                Menu.SetItemEnabled(i, false);
            }
            else
            {
                offered++;
            }
        }

        Menu.SetFirstLetterShortcuts();
        ShowText(question.Base.Text);

        // A question with no options at all is not a question -- it chains straight through.
        return offered == 0 ? Complete(happened: true) : EventStep.Running;
    }

    /// <summary>Names an event this port parses but does not run.</summary>
    private EventStep BeginUnsupported(IGameEvent gameEvent)
    {
        Unimplemented = $"[{(EventType)gameEvent.Base.EventType} here -- not implemented]";
        return EventStep.Running;
    }

    /// <summary>Builds one of the fixed menus from <c>GameMenu.cpp</c>'s tables.</summary>
    /// <remarks>
    /// The shortcut indices are the tables' own and are not first letters: <c>EXIT</c> uses index
    /// 1 for the <c>X</c>, and <c>PRESS ENTER TO CONTINUE</c> uses index 7 for the <c>N</c> of
    /// "ENTER". They are picked to be mnemonic and non-colliding, so first-lettering them instead
    /// would give <c>E</c> twice and suppress both.
    /// </remarks>
    private void SetupFixedMenu(MenuAnchors anchors, string? title, MenuOrientation orientation,
                                params (string Label, int Shortcut)[] entries)
    {
        Menu.Orientation = orientation;
        Menu.SetStartCoord(MenuAnchor.DefaultHorizontal, anchors);
        Menu.SetTitle(title);

        foreach (var (label, shortcut) in entries)
        {
            Menu.AddItem(label, shortcut);
        }
    }

    /// <summary>
    /// Feeds one input to the current event (<c>OnKeypress</c>).
    /// </summary>
    public EventStep Handle(InputEvent input)
    {
        if (Current is null)
        {
            return EventStep.Finished;
        }

        // Anything that is not a commit goes to the menu, exactly as every OnKeypress does.
        var result = MenuInput.Handle(Menu, input);
        bool committed = result == MenuInputResult.Accepted
                         || (input.Kind == InputEventKind.KeyDown
                             && input.Key == VirtualKey.Return);

        if (!committed)
        {
            return EventStep.Running;
        }

        // Long text pages before the event finishes -- Return advances the box first.
        if (!Text.IsLastBox())
        {
            Text.NextBox();
            return EventStep.Running;
        }

        // The sheet is a page over the treasure screen: the next commit puts it away rather than
        // choosing anything, so a player pressing Return twice lands back on the menu.
        if (Stats is not null)
        {
            Stats = null;
            return EventStep.Running;
        }

        return Current switch
        {
            QuestionEvent question => ChooseOption(question),
            YesNoEvent yesNo => ChooseYesNo(yesNo),
            TreasureEvent treasure => ChooseTreasure(treasure),
            RandomEvent random => ChooseRandom(random),
            SpecialItemEvent special => FinishSpecialItem(special),
            QuestEvent quest => FinishQuest(quest),
            DamageEvent damage => Applied(() => ApplyDamage?.Invoke(damage)),
            HealPartyEvent heal => Applied(() => ApplyHeal?.Invoke(heal)),
            _ => Complete(happened: true),
        };
    }

    /// <summary>
    /// Takes the selected option's chain target (<c>buttons[UserResult-1].chain</c>).
    /// </summary>
    /// <remarks>
    /// The menu index <i>is</i> the option index because every slot got an entry. An option whose
    /// chain is 0 or names no event falls back to the event's own chain rather than erroring —
    /// the original pushes a do-nothing event in that case (<c>RunEvent.cpp:13223</c>).
    /// </remarks>
    private EventStep ChooseOption(QuestionEvent question)
    {
        int index = Menu.ActiveItem;
        if (index >= 0 && index < question.Options.Count && question.Options[index].Chain > 0)
        {
            uint chain = question.Options[index].Chain;
            Current = null;
            return EventStep.To(chain);
        }

        return Complete(happened: true);
    }

    /// <summary>Yes is entry 0 and No is entry 1, in the table's order.</summary>
    private EventStep ChooseYesNo(YesNoEvent yesNo)
    {
        uint chain = Menu.ActiveItem == 0 ? yesNo.YesChain : yesNo.NoChain;
        if (chain > 0)
        {
            Current = null;
            return EventStep.To(chain);
        }

        return Complete(happened: true);
    }

    /// <summary>
    /// Gives and takes on the way out, then chains.
    /// </summary>
    /// <remarks>
    /// <b>The list is applied on Return, not on arrival.</b> A player who never presses Return —
    /// because the run was abandoned — never receives the item, which is the reference's behaviour
    /// and matters for a design that gates progress on one.
    /// <para>
    /// <c>ForceExit</c> is <b>not</b> ported: the reference posts <c>TASKMSG_MovePartyBackward</c>
    /// to step the party off the square, and this port has no task queue to post to. A design
    /// relying on it leaves the party standing on the event instead.
    /// </para>
    /// </remarks>
    private EventStep FinishSpecialItem(SpecialItemEvent special)
    {
        ApplySpecialItems?.Invoke(special);
        return Complete(happened: true);
    }

    /// <summary>Runs the host's effect on the way out, then chains.</summary>
    private EventStep Applied(Action effect)
    {
        effect();
        return Complete(happened: true);
    }

    /// <summary>Ends the event and applies its own chaining rule.</summary>
    public EventStep Complete(bool happened)
    {
        var finished = Current;
        Current = null;

        if (finished is null)
        {
            return EventStep.Finished;
        }

        uint? next = EventChain.Next(finished.Base, happened);
        return next is uint id ? EventStep.To(id) : EventStep.Finished;
    }

    /// <summary>Clears the presentation without running the chain.</summary>
    public void Cancel()
    {
        Current = null;
        Unimplemented = null;
        Text.Clear();
        Menu.Reset();
    }

    /// <summary>Draws the event's text and menu (<c>OnDraw</c>).</summary>
    public void Render(Surface destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (Current is null || font is null)
        {
            return;
        }

        // The sheet covers the list and the text box both. The reference gets this by pushing
        // VIEW_CHARACTER_DATA as its own event, which brings its own empty text with it; drawing
        // the sheet in place is the same thing from the player's side, so the text underneath has
        // to go with it.
        if (Stats is not null)
        {
            Stats.Display(destination, font);
        }
        else
        {
            FormattedTextRenderer.DrawBox(destination, font, Text, Box.X, Box.Y);
            Items?.Display(destination, font);
        }
        MenuRenderer.Draw(destination, Menu, font);
    }
}
