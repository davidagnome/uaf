using UAF.Media;
using UAF.Rules;
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
        BackupRequested = false;
        escapeSelects = null;
        lastAnchors = anchors;
        InventoryRows = null;
        inventoryParent = null;
        InventoryPage = 0;
        InventoryRowIndex = 0;
        LastRefusal = ReadyRefusal.None;
        LastFix = null;
        CastChoices = null;
        castParent = null;
        CastPage = 0;
        CastIndex = 0;
        LastCast = CastRefusal.None;
        Aiming = null;
        AimingSpell = null;
        AimCursor = 0;
        ShopRows = null;
        ShopPage = 0;
        ShopRowIndex = 0;
        LastPurchase = BuyRefusal.None;
        PartyMenuOpen = false;
        partyMenuHall = null;
        LastTraining = null;
        Slots = null;
        SlotMessage = null;
        Roster = null;
        rosterLines = [];
        Confirming = PartyConfirm.None;
        Creating = null;
        CreationOffered = [];
        ArtChoices = null;
        SpellChoices = null;
        spellScreen = null;
        SpellMessage = null;
        SaveMessage = null;
        Typing = null;
        TriesLeft = 0;

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
            WhoTriesEvent trial => BeginPressEnter(trial.Base.Text, anchors),
            JournalEvent journal => BeginPressEnter(journal.Base.Text, anchors),
            TakePartyItemsEvent take => BeginPressEnter(take.Base.Text, anchors),
            AddNpcEvent add => BeginPressEnter(add.Base.Text, anchors),
            RemoveNpcEvent remove => BeginPressEnter(remove.Base.Text, anchors),
            SoundEvent sound => BeginPressEnter(sound.Base.Text, anchors),
            WhoPaysEvent toll => BeginWhoPays(toll, anchors),
            LogicBlockEvent logic => BeginLogicBlock(logic),
            SmallTownEvent town => BeginSmallTown(town, anchors),
            CampEvent camp => BeginCamp(camp, anchors),
            TrainingHallEvent hall => BeginTrainingHall(hall, anchors),
            TavernEvent tavern => BeginTownMenu(TavernMenu, tavern.Base.Text, anchors),
            ShopEvent shop => BeginShop(shop, anchors),
            VaultEvent vault => BeginTownMenu(VaultMenu, vault.Base.Text, anchors),
            TempleEvent temple => BeginTemple(temple, anchors),
            PasswordEvent password => BeginPassword(password, anchors),
            _ => BeginUnsupported(gameEvent),
        };
    }

    // ---- typed text ----------------------------------------------------------------------------

    /// <summary>The line being typed, or null when nothing is asking for one.</summary>
    public TextEntry? Typing { get; private set; }

    /// <summary>How many tries at a password are left.</summary>
    public int TriesLeft { get; private set; }

    /// <summary>
    /// <c>PASSWORD_DATA</c>'s <c>TASK_PasswordGet</c> (<c>RunEvent.cpp:13072</c>).
    /// </summary>
    /// <remarks>
    /// <b>A password screen has no menu.</b> Every other event answers with one; this one takes
    /// raw characters until Return, so the menu is empty and Escape does nothing — there is no
    /// way out but to answer, right or wrong, as many times as the event allows.
    /// </remarks>
    private EventStep BeginPassword(PasswordEvent password, MenuAnchors anchors)
    {
        Typing = new TextEntry(TextEntryRules.Password);

        // A design that asks for zero tries still gets one; nbrTries is compared after the first
        // answer (currTry >= nbrTries), so a zero means "one attempt", not "none".
        TriesLeft = Math.Max(password.NbrTries, 1);

        Menu.Reset();
        Menu.Orientation = MenuOrientation.Horizontal;
        Menu.SetStartCoord(MenuAnchor.DefaultHorizontal, anchors);
        escapeSelects = null;

        ShowText(password.Base.Text);
        return EventStep.Running;
    }

    /// <summary>Feeds a typed character to whatever is asking for text.</summary>
    /// <returns>Whether the key was taken.</returns>
    private bool HandleTyping(InputEvent input)
    {
        if (Typing is null)
        {
            return false;
        }

        // The characters arrive as TextInput and the editing keys as KeyDown -- two kinds for one
        // screen, because the platform decides what a keypress means as text and the engine
        // decides what it means as a command.
        if (input.Kind == InputEventKind.TextInput)
        {
            if (input.Character != '\0')
            {
                Typing.Type(input.Character);
            }
            return true;
        }

        if (input.Kind != InputEventKind.KeyDown)
        {
            return false;
        }

        // Backspace and Left both delete. There is no cursor to move, so Left cannot mean
        // anything else -- see TextEntry.
        if (input.Key is VirtualKey.Backspace or VirtualKey.Left)
        {
            Typing.Backspace();
            return true;
        }

        // Return commits; everything else a password screen simply swallows, since it has no menu
        // to give the key to.
        return input.Key is not VirtualKey.Return;
    }

    /// <summary>
    /// Answers a password (<c>PasswordMatches</c> and the two outcomes around it).
    /// </summary>
    /// <remarks>
    /// <b>A wrong answer that is not the last says so and stays.</b> Only the try that exhausts
    /// <c>nbrTries</c> takes the failure chain, so the screen is a retry loop with a counter and
    /// not a single question.
    /// </remarks>
    private EventStep AnswerPassword(PasswordEvent password)
    {
        if (Typing is null)
        {
            return Complete(happened: true);
        }

        var mode = (PasswordMatch)AttributeInt(password.Base.Attributes,
                                               Password.MatchCriteriaAttribute);
        bool matchCase = AttributeInt(password.Base.Attributes,
                                      Password.MatchCaseAttribute) != 0;

        if (Password.Matches(Typing.Text, password.Password, mode, matchCase))
        {
            Typing = null;
            return Chained(password.SuccessChain);
        }

        TriesLeft--;
        if (TriesLeft > 0)
        {
            Typing.Clear();
            ShowText("That is not the correct answer");
            return EventStep.Running;
        }

        Typing = null;
        return Chained(password.FailChain);
    }

    /// <summary>Follows a chain if the event names one, or finishes.</summary>
    private EventStep Chained(uint chain) =>
        chain != 0 && IsValidEvent?.Invoke(chain) != false
            ? EventStep.To(chain)
            : Complete(happened: true);

    /// <summary>Reads an integer out of an event's attribute list.</summary>
    private static int AttributeInt(IReadOnlyList<AslEntry> attributes, string key) =>
        attributes.FirstOrDefault(a => a.Key == key) is { } entry
        && int.TryParse(entry.Value, out int value)
            ? value
            : 0;

    /// <summary>The tavern's menu (<c>TavernMenu</c>, <c>GameMenu.cpp:743</c>).</summary>
    private static readonly (string Label, int Shortcut)[] TavernMenu =
        [("FIGHT", 0), ("DRINK", 0), ("LISTEN", 0), ("EXIT", 1)];

    /// <summary>The shop's menu (<c>ShopMenu</c>, <c>GameMenu.cpp:652</c>).</summary>
    private static readonly (string Label, int Shortcut)[] ShopMenu =
        [("BUY", 0), ("ITEMS", 0), ("VIEW", 0), ("TAKE", 0), ("POOL", 0), ("SHARE", 0),
         ("APPRAISE", 0), ("EXIT", 1)];

    /// <summary>The vault's menu (<c>VaultMenu</c>, <c>GameMenu.cpp:230</c>).</summary>
    private static readonly (string Label, int Shortcut)[] VaultMenu =
        [("VIEW", 0), ("TAKE", 0), ("POOL", 0), ("SHARE", 0), ("ITEMS", 0), ("EXIT", 1)];

    /// <summary>The temple's menu (<c>TempleMenu</c>, <c>GameMenu.cpp:672</c>).</summary>
    private static readonly (string Label, int Shortcut)[] TempleMenu =
        [("HEAL", 0), ("DONATE", 0), ("VIEW", 0), ("TAKE", 0), ("POOL", 0), ("SHARE", 0),
         ("EXIT", 1)];

    // ---- APPRAISE ------------------------------------------------------------------------------

    /// <summary>Whether the appraise picker is up.</summary>
    public bool AppraiseOpen { get; private set; }

    /// <summary>What has just been valued, or null when the evaluate screen is not up.</summary>
    public (Valuable Kind, string Name, int Value)? Appraising { get; private set; }

    /// <summary>
    /// The design's name for a kind and how many the active character holds; set by the host.
    /// </summary>
    /// <remarks>
    /// <b>Whether the service offers it is not asked here</b> — that is the caller's, because it
    /// differs by service. See <see cref="OpenAppraise"/>.
    /// </remarks>
    public Func<Valuable, (string Name, int Held)>? AppraiseKind { get; set; }

    /// <summary>Takes one piece out of the purse and values it; set by the host.</summary>
    public Func<Valuable, int>? TakeForAppraisal { get; set; }

    /// <summary>Applies the party's decision; set by the host.</summary>
    public Action<Valuable, int, Appraised>? ApplyAppraisal { get; set; }

    private (string Label, int Shortcut)[]? appraiseParent;

    /// <summary>Which kinds the service that opened the picker actually appraises.</summary>
    private (bool Gems, bool Jewels) appraiseOffers;

    /// <summary>
    /// <c>APPRAISE_SELECT_DATA</c> (<c>RunEvent.cpp:26715</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The two entries are renamed to the design's own words</b> for its gem and jewellery
    /// types, so a design calling them "STONES" and "TRINKETS" says so on the bar. The names come
    /// from the design whether the service offers that kind or not — only the enable state differs.
    /// </para>
    /// <para>
    /// <b>Only the shop can refuse a kind.</b> The constructor's two flags default to TRUE
    /// (<c>GameEvent.h:4590</c>) and the temple pushes the screen without them, so a temple
    /// appraises both whatever its design says; the shop passes <c>canApprGems</c> and
    /// <c>canApprJewels</c> and is the only service that can darken an entry outright.
    /// </para>
    /// </remarks>
    private EventStep OpenAppraise((string Label, int Shortcut)[] parent,
                                   bool offerGems = true, bool offerJewels = true)
    {
        appraiseParent = parent;
        appraiseOffers = (offerGems, offerJewels);
        AppraiseOpen = true;

        var gem = AppraiseKind?.Invoke(Valuable.Gem) ?? ("GEMS", 0);
        var jewel = AppraiseKind?.Invoke(Valuable.Jewelry) ?? ("JEWELRY", 0);

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal,
                       (gem.Name, 0), (jewel.Name, 0), ("EXIT", 1));
        escapeSelects = 2;

        Menu.SetItemEnabled(0, Appraisal.CanAppraise(offerGems, gem.Held));
        Menu.SetItemEnabled(1, Appraisal.CanAppraise(offerJewels, jewel.Held));

        ShowText($"YOU HAVE {gem.Held} {gem.Name} {jewel.Held} {jewel.Name} NOT YET APPRAISED");

        return EventStep.Running;
    }

    private EventStep ChooseAppraise()
    {
        if (Menu.ActiveItem is 0 or 1)
        {
            var kind = Menu.ActiveItem == 0 ? Valuable.Gem : Valuable.Jewelry;
            var (name, _) = AppraiseKind?.Invoke(kind) ?? ("", 0);

            // Taken out of the purse first, then valued -- there is no way back to an unappraised
            // piece.
            int value = TakeForAppraisal?.Invoke(kind) ?? 0;
            Appraising = (kind, name, value);

            Menu.Reset();
            SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal,
                           ("SELL", 0), ("KEEP", 0));
            escapeSelects = 1;
            ShowText($"THIS {name} IS VALUED AT {value}");

            return EventStep.Running;
        }

        var parent = appraiseParent;
        AppraiseOpen = false;
        appraiseParent = null;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, parent ?? TempleMenu);
        escapeSelects = (parent?.Length ?? TempleMenu.Length) - 1;

        if (Current is ShopEvent shop && parent == ShopMenu)
        {
            UpdateShopMenu(shop);
        }

        return EventStep.Running;
    }

    private EventStep ChooseAppraised()
    {
        if (Appraising is { } piece)
        {
            ApplyAppraisal?.Invoke(piece.Kind, piece.Value,
                                   Menu.ActiveItem == 0 ? Appraised.Sell : Appraised.Keep);
        }

        Appraising = null;

        // Back to the picker, which re-counts what is left.
        return OpenAppraise(appraiseParent ?? TempleMenu,
                            appraiseOffers.Gems, appraiseOffers.Jewels);
    }

    // ---- CAST ----------------------------------------------------------------------------------

    /// <summary>The caster's castable spells, or null when the cast list is not up.</summary>
    public IReadOnlyList<SpellListEntry>? CastChoices { get; private set; }

    /// <summary>Whether the cast list is the screen on top.</summary>
    public bool CastOpen => CastChoices is not null;

    /// <summary>Which row the cursor is on.</summary>
    public int CastIndex { get; private set; }

    /// <summary>Why the last CAST did nothing, or <see cref="CastRefusal.None"/>.</summary>
    public CastRefusal LastCast { get; private set; }

    /// <summary>The spells the active character can cast here; set by the host.</summary>
    public Func<IReadOnlyList<SpellListEntry>>? CastableSpells { get; set; }

    /// <summary>
    /// Casts the named spell for the active character; set by the host. Returns why it did not.
    /// </summary>
    public Func<string, CastRefusal>? CastSpell { get; set; }

    private (string Label, int Shortcut)[]? castParent;

    /// <summary>
    /// <c>CAST_MENU_DATA</c> (<c>RunEvent.cpp:25832</c>) — the caster's memorised spells.
    /// </summary>
    /// <remarks>
    /// <b>A character who cannot cast never sees it.</b> <c>OnInitialEvent</c> pops the screen
    /// before drawing anything, which lands the player back on the menu underneath — so the entry
    /// looks like it did nothing rather than like it failed.
    /// </remarks>
    private EventStep OpenCast((string Label, int Shortcut)[] parent)
    {
        if (CanCastSpells?.Invoke() == false)
        {
            LastCast = CastRefusal.CannotCast;
            return EventStep.Running;
        }

        castParent = parent;
        CastIndex = 0;
        LastCast = CastRefusal.None;
        CastChoices = CastableSpells?.Invoke() ?? [];

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, NonCombatCast.Menu);
        escapeSelects = NonCombatCast.Menu.Length - 1;

        return EventStep.Running;
    }

    /// <summary>
    /// <c>CAST_MENU_DATA::OnKeypress</c> (<c>RunEvent.cpp:25754</c>).
    /// </summary>
    /// <remarks>
    /// <b>Every refusal is silent.</b> The reference pops the pushed screen with nothing but a
    /// debug line for each of its six guards, so a player who casts a combat-only spell sees the
    /// screen go away and nothing else. <see cref="LastCast"/> records which, for a caller that
    /// wants to say more than the reference does.
    /// </remarks>
    private EventStep ChooseCast()
    {
        switch (Menu.ActiveItem)
        {
            case 0:
            {
                var page = CastPageRows;
                if (CastIndex >= 0 && CastIndex < page.Count)
                {
                    string spellId = page[CastIndex].SpellId;
                    LastCast = CastSpell?.Invoke(spellId) ?? CastRefusal.None;

                    // A spell that names its own targets hands the player the picker instead.
                    if (LastCast == CastRefusal.NeedsTargets)
                    {
                        return OpenAiming(spellId);
                    }

                    // The list is rebuilt because casting spent a copy, which can empty a row.
                    CastChoices = CastableSpells?.Invoke() ?? [];
                    if (CastIndex >= CastPageRows.Count)
                    {
                        CastIndex = Math.Max(CastPageRows.Count - 1, 0);
                    }
                }

                return EventStep.Running;
            }

            case 1:
                TurnCastPage(+1);
                return EventStep.Running;

            case 2:
                TurnCastPage(-1);
                return EventStep.Running;

            default:
            {
                var parent = castParent;
                CastChoices = null;
                castParent = null;

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal,
                               parent ?? MagicMenu);
                escapeSelects = (parent?.Length ?? MagicMenu.Length) - 1;
                return EventStep.Running;
            }
        }
    }

    /// <summary>Which page of the spell list is showing.</summary>
    public int CastPage { get; private set; }

    /// <summary>The rows on the page currently showing.</summary>
    public IReadOnlyList<SpellListEntry> CastPageRows =>
        CastChoices is null ? [] : [.. CastChoices.Skip(CastPage * PageSize).Take(PageSize)];

    private void TurnCastPage(int delta)
    {
        int count = CastChoices?.Count ?? 0;

        if (delta > 0 && (CastPage + 1) * PageSize >= count)
        {
            return;
        }

        CastPage = Math.Max(CastPage + delta, 0);

        if (CastIndex >= CastPageRows.Count)
        {
            CastIndex = Math.Max(CastPageRows.Count - 1, 0);
        }
    }

    /// <summary>The spell list takes the vertical keys; the menu keeps left and right.</summary>
    private bool HandleCastKey(VirtualKey key)
    {
        int onPage = CastPageRows.Count;
        if (onPage <= 0)
        {
            return false;
        }

        switch (key)
        {
            case VirtualKey.Up:
                CastIndex = ((CastIndex - 1) % onPage + onPage) % onPage;
                return true;

            case VirtualKey.Down:
                CastIndex = (CastIndex + 1) % onPage;
                return true;

            case VirtualKey.PageDown:
                TurnCastPage(+1);
                return true;

            case VirtualKey.PageUp:
                TurnCastPage(-1);
                return true;

            default:
                return false;
        }
    }

    // ---- the non-combat target picker -----------------------------------------------------------

    /// <summary>The menu (<c>TargetSelectNonCombatMenu</c>, <c>GameMenu.cpp:197</c>).</summary>
    public static readonly (string Label, int Shortcut)[] AimMenu =
        [("CAST SPELL ON?", 0), ("EXIT", 1)];

    /// <summary>The selection in progress, or null when the picker is not up.</summary>
    public SpellTargetSelection? Aiming { get; private set; }

    /// <summary>The spell the picker is aiming.</summary>
    public string? AimingSpell { get; private set; }

    /// <summary>Which party member the cursor is on (<c>party.activeCharacter</c>).</summary>
    public int AimCursor { get; private set; }

    /// <summary>
    /// The active party member the picker started from, put back when it closes.
    /// </summary>
    /// <remarks>
    /// <b>Choosing targets really does move the active character.</b> The reference walks
    /// <c>party.activeCharacter</c> with the arrow keys and reads <c>GetActiveChar</c> as the
    /// selected target — which is why <c>CAST_NON_COMBAT_SPELL_MENU_DATA</c> saves it into
    /// <c>tempActive</c> before pushing this and restores it on every exit path.
    /// </remarks>
    private int aimReturnsTo;

    /// <summary>Begins a selection; set by the host, which owns the spell and the party.</summary>
    /// <remarks>Returning null means the spell cannot be aimed and the picker never opens.</remarks>
    public Func<string, SpellTargetSelection?>? BeginAiming { get; set; }

    /// <summary>The hit dice of a party member, for the hit-dice budget; set by the host.</summary>
    public Func<int, double>? HitDiceOf { get; set; }

    /// <summary>Casts at the chosen targets; set by the host.</summary>
    public Action<string, IReadOnlyList<int>>? CastAtTargets { get; set; }

    /// <summary>
    /// <c>TARGET_SELECT_NONCOMBAT_EVENT_DATA::OnInitialEvent</c> (<c>RunEvent.cpp:25428</c>).
    /// </summary>
    /// <remarks>
    /// <b>A selection with no valid target count pops before drawing.</b> So a spell whose
    /// targeting the engine cannot make sense of never shows a picker at all.
    /// </remarks>
    private EventStep OpenAiming(string spellId)
    {
        if (BeginAiming?.Invoke(spellId) is not { } selection || !selection.IsValid)
        {
            return EventStep.Running;
        }

        Aiming = selection;
        AimingSpell = spellId;
        aimReturnsTo = AimCursor;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, selection.RemainingText(), MenuOrientation.Horizontal,
                       AimMenu);
        escapeSelects = 1;
        UpdateAimMenu();

        return EventStep.Running;
    }

    /// <summary>
    /// <c>OnUpdateUI</c> (<c>RunEvent.cpp:25489</c>) — SELECT darkens when there is nothing left
    /// to choose.
    /// </summary>
    private void UpdateAimMenu() =>
        Menu.SetItemEnabled(0, Aiming is { IsValid: true, AllChosen: false });

    /// <summary>
    /// <c>OnKeypress</c> (<c>RunEvent.cpp:25449</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The last target chosen closes the picker by itself.</b> There is no confirmation step —
    /// <c>AllTargetsChosen</c> pops immediately, so a one-target spell is aimed with a single
    /// press.
    /// </para>
    /// <para>
    /// <b>EXIT casts at whatever has been chosen so far rather than abandoning.</b> The picker
    /// simply pops, and the screen underneath casts if <c>NumTargets() &gt; 0</c> — so leaving a
    /// three-target spell after one pick casts it at one. The <i>combat</i> picker asks before
    /// abandoning an empty selection; this one never asks at all.
    /// </para>
    /// <para>
    /// <b>The same member cannot be chosen twice</b> — <c>STD_AddTarget</c> refuses a duplicate,
    /// and the reference logs the failure and carries on with the menu still up.
    /// </para>
    /// </remarks>
    private EventStep ChooseAiming()
    {
        if (Aiming is not { } selection)
        {
            return EventStep.Running;
        }

        if (Menu.ActiveItem == 0)
        {
            selection.Add(AimCursor, HitDiceOf?.Invoke(AimCursor) ?? 0);

            if (!selection.AllChosen)
            {
                // Re-titled rather than rebuilt: the reference calls setTitle on the menu it
                // already has, so what is left to choose is on the menu bar and not in the text
                // box.
                Menu.SetTitle(selection.RemainingText());
                UpdateAimMenu();
                return EventStep.Running;
            }
        }

        return CloseAiming(selection);
    }

    private EventStep CloseAiming(SpellTargetSelection selection)
    {
        string? spellId = AimingSpell;

        Aiming = null;
        AimingSpell = null;
        AimCursor = aimReturnsTo;

        if (spellId is not null && selection.Targets.Count > 0)
        {
            CastAtTargets?.Invoke(spellId, selection.Targets);
        }

        // Back to the cast list, which the picker was pushed over.
        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, NonCombatCast.Menu);
        escapeSelects = NonCombatCast.Menu.Length - 1;

        CastChoices = CastableSpells?.Invoke() ?? [];
        if (CastIndex >= CastPageRows.Count)
        {
            CastIndex = Math.Max(CastPageRows.Count - 1, 0);
        }

        return EventStep.Running;
    }

    /// <summary>
    /// The picker moves the party cursor, and the menu keeps left and right
    /// (<c>HMenuVPartyKeyboardAction</c>).
    /// </summary>
    /// <remarks>
    /// <b>The mirror of the party menu's split.</b> There the menu takes the vertical keys and the
    /// party the horizontal ones; here it is the other way round, because the menu is horizontal.
    /// </remarks>
    private bool HandleAimKey(VirtualKey key)
    {
        int size = PartySize?.Invoke() ?? 0;
        if (size <= 0)
        {
            return false;
        }

        switch (key)
        {
            case VirtualKey.Up:
                AimCursor = ((AimCursor - 1) % size + size) % size;
                return true;

            case VirtualKey.Down:
                AimCursor = (AimCursor + 1) % size;
                return true;

            default:
                return false;
        }
    }

    // ---- BUY -----------------------------------------------------------------------------------

    /// <summary>What the shop has on its shelf, priced, or null when BUY is not open.</summary>
    public IReadOnlyList<InventoryRow>? ShopRows { get; private set; }

    /// <summary>Whether the buy screen is the one on top.</summary>
    public bool BuyOpen => ShopRows is not null;

    /// <summary>Which page of the shelf is showing.</summary>
    public int ShopPage { get; private set; }

    /// <summary>Which row of the page the cursor is on (<c>party.activeItem</c>).</summary>
    public int ShopRowIndex { get; private set; }

    /// <summary>The rows on the page currently showing.</summary>
    public IReadOnlyList<InventoryRow> ShopPageRows =>
        ShopRows is null ? [] : [.. ShopRows.Skip(ShopPage * PageSize).Take(PageSize)];

    /// <summary>What the last BUY did, or <see cref="BuyRefusal.None"/>.</summary>
    public BuyRefusal LastPurchase { get; private set; }

    /// <summary>Buys the named item for the active character; set by the host.</summary>
    public Func<string, CostFactor, BuyRefusal>? BuyItem { get; set; }

    /// <summary>Whether the active character could meet a price; set by the host.</summary>
    public Func<int, bool>? CanAfford { get; set; }

    /// <summary>
    /// <c>BUY_SHOP_ITEMS_DATA</c> (<c>RunEvent.cpp:11085</c>) — the shop's shelf.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The shelf is the inventory screen with the columns swapped</b>: COST on, READY off, where
    /// a pack shows the reverse. One list widget, two presentations, which is why this reuses
    /// <see cref="Inventory.Rows"/> rather than building a list of its own.
    /// </para>
    /// <para>
    /// <b>The reference identifies the shop's whole stock on open</b> — it walks <c>itemsAvail</c>
    /// setting <c>identified = TRUE</c> on each entry, "shops disclose full name", writing into the
    /// event rather than a copy. Nothing here needs to: <see cref="Inventory.Rows"/> takes every
    /// name from the database and never consults the flag, so the shelf already shows true names.
    /// The reference's write outlives the visit; this does not reproduce that, and nothing in the
    /// port reads the flag back.
    /// </para>
    /// </remarks>
    private EventStep OpenBuy(ShopEvent shop)
    {
        var factor = Prices.FactorOf(shop.CostFactor);

        ShopPage = 0;
        ShopRowIndex = 0;
        LastPurchase = BuyRefusal.None;
        ShopRows = Inventory.Rows(shop.ItemsAvailable, ItemNames,
                                  id => ItemDatabase?.Invoke(id) is { } record
                                            ? Shopping.Price(record, factor)
                                            : 0);

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, Shopping.Menu);
        escapeSelects = Shopping.Menu.Length - 1;

        PopulateShopForm();
        return EventStep.Running;
    }

    private void PopulateShopForm()
    {
        if (ShopRows is null)
        {
            return;
        }

        var page = ShopPageRows;

        if (ShopRowIndex >= page.Count)
        {
            ShopRowIndex = Math.Max(page.Count - 1, 0);
        }

        Items = new ItemsForm(PageSize);
        if (font is not null)
        {
            Items.Populate(font,
                           [.. page.Select(r => new ItemsFormRow(
                               string.Empty, r.Quantity.ToString(), r.Cost.ToString(), r.Name))],
                           useReady: false, useCost: true);
        }

        if (page.Count > 0)
        {
            Items.Select(ShopRowIndex);
        }

        UpdateBuyMenu();
    }

    /// <summary>
    /// <c>BUY_SHOP_ITEMS_DATA::OnUpdateUI</c> (<c>RunEvent.cpp:11144</c>).
    /// </summary>
    /// <remarks>
    /// <b>BUY darkens on the price of the row the cursor is on, re-tested every frame.</b> So the
    /// entry lights and darkens as the player moves down a shelf they can only half afford, and an
    /// empty shop darkens it outright. An id the design has lost prices at 0, which is affordable —
    /// the reference's <c>pItem == NULL</c> branch sets <c>costOfSelectedItem = 0</c> rather than
    /// refusing, and <see cref="Shopping.Buy"/> is what actually turns it away.
    /// </remarks>
    private void UpdateBuyMenu()
    {
        var page = ShopPageRows;
        int cost = ShopRowIndex >= 0 && ShopRowIndex < page.Count ? page[ShopRowIndex].Cost : 0;
        int count = ShopRows?.Count ?? 0;

        Menu.SetItemEnabled(0, count > 0 && CanAfford?.Invoke(cost) != false);
        Menu.SetItemEnabled(1, AllowShopPage(+1));
        Menu.SetItemEnabled(2, AllowShopPage(-1));
    }

    /// <summary>
    /// <c>allowShopItemNextPage</c> / <c>allowShopItemPrevPage</c> (<c>Disptext.cpp:686</c>).
    /// </summary>
    /// <remarks>
    /// <b>Both answer no for a list that fits on one page</b>, before either end is considered —
    /// so a shop with fewer items than a page darkens NEXT and PREV together.
    /// </remarks>
    private bool AllowShopPage(int delta)
    {
        int count = ShopRows?.Count ?? 0;
        if (count <= PageSize)
        {
            return false;
        }

        int start = ShopPage * PageSize;
        return delta > 0 ? start <= count - PageSize : start >= PageSize;
    }

    /// <summary><c>nextShopPage</c> / <c>prevShopPage</c> (<c>Disptext.cpp:651</c>).</summary>
    /// <remarks>
    /// <b>The page moves, then the cursor is clamped to what is left on it</b> — so paging onto a
    /// short final page pulls the cursor up to its last row rather than leaving it pointing past
    /// the end.
    /// </remarks>
    private void TurnShopPage(int delta)
    {
        int count = ShopRows?.Count ?? 0;

        if (delta > 0 && (ShopPage + 1) * PageSize >= count)
        {
            return;
        }

        ShopPage = Math.Max(ShopPage + delta, 0);
        PopulateShopForm();
    }

    private void MoveShopRow(int delta)
    {
        int onPage = ShopPageRows.Count;
        if (onPage <= 0)
        {
            return;
        }

        ShopRowIndex = ((ShopRowIndex + delta) % onPage + onPage) % onPage;
        Items?.Select(ShopRowIndex);
        UpdateBuyMenu();
    }

    /// <summary>The shelf splits the keys the inventory's way: vertical here, horizontal to the menu.</summary>
    private bool HandleShopKey(VirtualKey key)
    {
        switch (key)
        {
            case VirtualKey.Up:
                MoveShopRow(-1);
                return true;

            case VirtualKey.Down:
                MoveShopRow(+1);
                return true;

            case VirtualKey.PageDown:
                TurnShopPage(+1);
                return true;

            case VirtualKey.PageUp:
                TurnShopPage(-1);
                return true;

            default:
                return false;
        }
    }

    /// <summary><c>BUY_SHOP_ITEMS_DATA::OnKeypress</c> (<c>RunEvent.cpp:11105</c>).</summary>
    /// <remarks>
    /// <b>The shelf does not shrink as things are bought.</b> A shop's stock is a list of what it
    /// offers, not a count of what it has, so the same row can be bought until the party runs out
    /// of money or of carrying capacity.
    /// </remarks>
    private EventStep ChooseBuy(ShopEvent shop)
    {
        switch (Menu.ActiveItem)
        {
            case 0:
            {
                var page = ShopPageRows;
                if (ShopRowIndex < 0 || ShopRowIndex >= page.Count
                    || ShopRows is null || ItemsSold(shop) is not { } stock)
                {
                    return EventStep.Running;
                }

                string itemId = stock[page[ShopRowIndex].Index].ItemId;
                LastPurchase = BuyItem?.Invoke(itemId, Prices.FactorOf(shop.CostFactor))
                               ?? BuyRefusal.None;

                UpdateBuyMenu();
                return EventStep.Running;
            }

            case 1:
                TurnShopPage(+1);
                return EventStep.Running;

            case 2:
                TurnShopPage(-1);
                return EventStep.Running;

            default:
                ShopRows = null;
                Items = null;

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, ShopMenu);
                escapeSelects = ShopMenu.Length - 1;
                UpdateShopMenu(shop);
                return EventStep.Running;
        }
    }

    private static IReadOnlyList<ItemInstance>? ItemsSold(ShopEvent shop) =>
        shop.ItemsAvailable.Items;

    /// <summary>Whether the active character is well enough to shop; set by the host.</summary>
    public Func<bool>? ActiveCharacterOkay { get; set; }

    /// <summary>
    /// <c>SHOP::OnUpdateUI</c> (<c>RunEvent.cpp:11010</c>) — the two entries this port runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>BUY darkens for a character who is not <see cref="CharacterStatus.Okay"/>.</b> Buying is
    /// the active character's, and TAB moves that around the party — so a shop's menu changes as
    /// the player pages past a dead member. <c>buyItem</c> tests it a second time.
    /// </para>
    /// <para>
    /// <b>APPRAISE darkens only when the shop offers neither kind.</b> A shop that appraises gems
    /// but not jewellery keeps the entry lit and darkens the one kind inside.
    /// </para>
    /// <para>
    /// <b>Not ported: TAKE, POOL and SHARE</b>, whose enable states turn on <c>party.moneyPooled</c>
    /// — those three entries do nothing in this port yet, and darkening a name that does nothing
    /// either way would hide which is which.
    /// </para>
    /// </remarks>
    private void UpdateShopMenu(ShopEvent shop)
    {
        Menu.SetItemEnabled(0, ActiveCharacterOkay?.Invoke() != false);
        Menu.SetItemEnabled(6, shop.CanAppraiseGems != 0 || shop.CanAppraiseJewels != 0);
    }

    /// <summary><c>SHOP::OnInitialEvent</c> (<c>RunEvent.cpp:10938</c>).</summary>
    private EventStep BeginShop(ShopEvent shop, MenuAnchors anchors)
    {
        var step = BeginTownMenu(ShopMenu, shop.Base.Text, anchors);
        UpdateShopMenu(shop);
        return step;
    }

    /// <summary><c>SHOP::OnKeypress</c> (<c>RunEvent.cpp:10949</c>).</summary>
    private EventStep ChooseShop(ShopEvent shop)
    {
        if (Appraising is not null)
        {
            return ChooseAppraised();
        }

        if (AppraiseOpen)
        {
            return ChooseAppraise();
        }

        if (BuyOpen)
        {
            return ChooseBuy(shop);
        }

        if (Menu.ActiveItem == 0)
        {
            return OpenBuy(shop);
        }

        if (Menu.ActiveItem == 6)
        {
            // The one service that can refuse a kind outright.
            return OpenAppraise(ShopMenu, shop.CanAppraiseGems != 0, shop.CanAppraiseJewels != 0);
        }

        return ChooseTownItem(ShopMenu, shop.ForceExit);
    }

    /// <summary>The temple's heal menu (<c>TempleHealMenu</c>, <c>GameMenu.cpp:707</c>).</summary>
    public static readonly (string Label, int Shortcut)[] HealMenu =
        [("CAST", 0), ("VIEW", 0), ("FIX", 0), ("TAKE", 0), ("POOL", 0), ("SHARE", 0),
         ("APPR", 0), ("EXIT", 1)];

    public const int HealCast = 0;
    public const int HealFix = 2;
    public const int HealExit = 7;

    // ---- FIX -----------------------------------------------------------------------------------

    /// <summary>Runs FIX for the current environment; set by the host, which owns the party.</summary>
    /// <remarks>
    /// The runner does not own the fix spell book, the party or the spell machinery, so the whole
    /// of <see cref="FixSpells.Run"/> lives on the host side of the callback. What is here is the
    /// two menu entries that reach it and the fact that neither changes the screen.
    /// </remarks>
    public Func<FixEnvironment, IReadOnlyList<FixCast>>? ApplyFix { get; set; }

    /// <summary>The casts the last FIX made, or null if FIX has not been used.</summary>
    public IReadOnlyList<FixCast>? LastFix { get; private set; }

    /// <summary>
    /// <c>party.FixParty(environment)</c> — camp's entry (<c>RunEvent.cpp:9296</c>) and the
    /// temple's (<c>:12878</c>).
    /// </summary>
    /// <remarks>
    /// <b>Neither entry pushes a screen or says anything.</b> Both are a bare call in the middle of
    /// a menu switch, so the healing happens and the player is looking at the same menu they
    /// pressed it on — the only feedback is the party's hit points changing on the status line.
    /// </remarks>
    private EventStep RunFix(FixEnvironment environment)
    {
        LastFix = ApplyFix?.Invoke(environment) ?? [];
        return EventStep.Running;
    }

    /// <summary>Whether the heal sub-menu is up.</summary>
    public bool HealOpen { get; private set; }

    /// <summary>The temple's spells and their prices, or null when that list is not up.</summary>
    public IReadOnlyList<TempleSpell>? TempleCasting { get; private set; }

    /// <summary>Which row the cursor is on.</summary>
    public int TempleSpellIndex { get; private set; }

    /// <summary>What the temple will cast and for how much; set by the host.</summary>
    public Func<IReadOnlyList<TempleSpell>>? TempleSpellsFor { get; set; }

    private EventStep OpenHeal(TempleEvent temple)
    {
        HealOpen = true;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, HealMenu);
        escapeSelects = HealExit;
        ShowText(temple.Base.Text2);

        return EventStep.Running;
    }

    private EventStep ChooseHeal(TempleEvent temple)
    {
        switch (Menu.ActiveItem)
        {
            case HealCast:
                TempleCasting = TempleSpellsFor?.Invoke() ?? [];
                TempleSpellIndex = 0;

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal,
                               ("CAST", 0), ("NEXT", 0), ("PREV", 0), ("EXIT", 1));
                escapeSelects = 3;
                return EventStep.Running;

            case HealFix:
                return RunFix(FixEnvironment.Temple);

            case 6:
                return OpenAppraise(HealMenu);

            case HealExit:
                HealOpen = false;

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, TempleMenu);
                escapeSelects = TempleMenu.Length - 1;
                ShowText(temple.Base.Text2);
                return EventStep.Running;

            default:
                Unimplemented =
                    $"[{HealMenu[Math.Clamp(Menu.ActiveItem, 0, HealMenu.Length - 1)].Label}"
                    + " here -- not implemented]";
                return EventStep.Running;
        }
    }

    /// <summary>
    /// The temple's cast list: CAST, NEXT, PREV, EXIT.
    /// </summary>
    /// <remarks>
    /// <b>The casting itself is not ported.</b> The reference synthesises a max-level
    /// "TempleBishop" (<c>RunEvent.cpp:12408</c>), targets the active character and casts through
    /// the ordinary spell machinery — which is the same layer FIX waits on. What is here is the
    /// list and its prices.
    /// </remarks>
    private EventStep ChooseTempleCast(TempleEvent temple)
    {
        switch (Menu.ActiveItem)
        {
            case 0:
                return OpenCast(HealMenu);

            case 1:
            case 2:
                return EventStep.Running;

            default:
                TempleCasting = null;

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, HealMenu);
                escapeSelects = HealExit;
                ShowText(temple.Base.Text2);
                return EventStep.Running;
        }
    }

    /// <summary>The cast list takes the vertical keys.</summary>
    private bool HandleTempleCastKey(VirtualKey key)
    {
        int count = TempleCasting?.Count ?? 0;
        if (count <= 0)
        {
            return false;
        }

        switch (key)
        {
            case VirtualKey.Up:
                TempleSpellIndex = ((TempleSpellIndex - 1) % count + count) % count;
                return true;

            case VirtualKey.Down:
                TempleSpellIndex = (TempleSpellIndex + 1) % count;
                return true;

            default:
                return false;
        }
    }

    /// <summary>The temple's donate menu (<c>TempleDonateMenu</c>, <c>GameMenu.cpp:690</c>).</summary>
    public static readonly (string Label, int Shortcut)[] DonateMenu =
        [("TAKE", 0), ("POOL", 0), ("SHARE", 0), ("APPR", 0), ("GIVE", 0), ("EXIT", 1)];

    public const int DonateGive = 4;
    public const int DonateExit = 5;

    /// <summary>Whether the donate sub-menu is up.</summary>
    public bool DonateOpen { get; private set; }

    /// <summary>What is being typed into GIVE, or null when that screen is not up.</summary>
    public Donation? Giving { get; private set; }

    /// <summary>The most the party could give; set by the host.</summary>
    public Func<int>? DonationMaximum { get; set; }

    /// <summary>Takes a donation and returns the temple's new running total; set by the host.</summary>
    public Func<int, int>? ApplyDonation { get; set; }

    /// <summary>What this temple has been given so far, across every visit.</summary>
    public int TotalDonated { get; private set; }

    private EventStep OpenDonate(TempleEvent temple)
    {
        DonateOpen = true;
        TotalDonated = TotalDonated == 0 ? temple.TotalDonation : TotalDonated;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, DonateMenu);
        escapeSelects = DonateExit;

        return EventStep.Running;
    }

    private EventStep ChooseDonate(TempleEvent temple)
    {
        switch (Menu.ActiveItem)
        {
            case DonateGive:
                Giving = Donation.None;

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal,
                               ("PRESS ENTER WHEN DONE", 7));
                escapeSelects = 0;
                ShowText("HOW MUCH WILL YOU GIVE?");
                return EventStep.Running;

            case 3:
                return OpenAppraise(DonateMenu);

            case DonateExit:
                DonateOpen = false;

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, TempleMenu);
                escapeSelects = TempleMenu.Length - 1;
                ShowText(temple.Base.Text2);
                return EventStep.Running;

            default:
                Unimplemented =
                    $"[{DonateMenu[Math.Clamp(Menu.ActiveItem, 0, DonateMenu.Length - 1)].Label}"
                    + " here -- not implemented]";
                return EventStep.Running;
        }
    }

    /// <summary>
    /// Return on the GIVE screen: hand the money over and go back to the donate menu.
    /// </summary>
    private EventStep CommitGive()
    {
        if (Giving is { } entry)
        {
            TotalDonated = ApplyDonation?.Invoke(entry.Amount) ?? TotalDonated;
        }

        Giving = null;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, DonateMenu);
        escapeSelects = DonateExit;

        return EventStep.Running;
    }

    /// <summary>The digits and the backspace, which never reach the menu.</summary>
    private bool HandleGiveKey(InputEvent input)
    {
        if (Giving is not { } entry || input.Kind != InputEventKind.KeyDown)
        {
            return false;
        }

        if (input.Key == VirtualKey.Backspace)
        {
            Giving = entry.Backspace();
            return true;
        }

        char typed = input.Character;
        if (typed is >= '0' and <= '9')
        {
            Giving = entry.Type(typed, DonationMaximum?.Invoke() ?? 0);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A town service's outer menu: horizontal, Escape on EXIT, the event's text above it.
    /// </summary>
    /// <remarks>
    /// The four remaining services share this shell exactly; what differs is their entries and
    /// what each entry pushes. See §camp and the training hall in the plan for why the shells are
    /// the cheap half.
    /// </remarks>
    private EventStep BeginTownMenu((string Label, int Shortcut)[] entries, string text,
                                    MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, null, MenuOrientation.Horizontal, entries);

        escapeSelects = entries.Length - 1;      // EXIT is always last
        ShowText(text);
        return EventStep.Running;
    }

    /// <summary>
    /// <c>TEMPLE::OnInitialEvent</c>'s default state (<c>RunEvent.cpp:12473</c>) — the welcome.
    /// </summary>
    /// <remarks>
    /// <b>The temple is the only town service with two screens of its own.</b> It opens on a
    /// press-enter welcome showing the event's <c>Text</c>, and only then shows its menu — which
    /// uses <c>Text2</c>. Two text fields for one event, and nothing but the state distinguishes
    /// which is on screen.
    /// </remarks>
    private EventStep BeginTemple(TempleEvent temple, MenuAnchors anchors)
    {
        templeWelcomed = false;
        SetupFixedMenu(anchors, null, MenuOrientation.Horizontal, ("PRESS ENTER TO CONTINUE", 7));

        escapeSelects = 0;
        ShowText(temple.Base.Text);
        return EventStep.Running;
    }

    /// <summary>Whether the temple has moved past its welcome onto its menu.</summary>
    private bool templeWelcomed;

    /// <summary><c>TEMPLE::OnKeypress</c> (<c>RunEvent.cpp:12588</c>).</summary>
    private EventStep ChooseTemple(TempleEvent temple)
    {
        if (Appraising is not null)
        {
            return ChooseAppraised();
        }

        if (AppraiseOpen)
        {
            return ChooseAppraise();
        }

        if (Giving is not null)
        {
            return CommitGive();
        }

        if (TempleCasting is not null)
        {
            return ChooseTempleCast(temple);
        }

        if (HealOpen)
        {
            return ChooseHeal(temple);
        }

        if (DonateOpen)
        {
            return ChooseDonate(temple);
        }

        if (!templeWelcomed)
        {
            templeWelcomed = true;

            // The reference calls OnInitialEvent again, which calls setMenu -- a replacement, not
            // an addition. SetupFixedMenu only appends, so the reset Begin does has to be repeated.
            Menu.Reset();
            SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, TempleMenu);
            escapeSelects = TempleMenu.Length - 1;
            ShowText(temple.Base.Text2);          // NOT Text -- see BeginTemple
            return EventStep.Running;
        }

        // HEAL and DONATE are the temple's own screens; everything else is the shared town
        // dispatch.
        if (Menu.ActiveItem == 0)
        {
            return OpenHeal(temple);
        }

        if (Menu.ActiveItem == 1 && temple.AllowDonations != 0)
        {
            return OpenDonate(temple);
        }

        if (Menu.ActiveItem == TempleMenu.Length - 1
            && TempleDonations.Triggers(TotalDonated, temple.DonationTrigger)
            && temple.DonationChain != 0
            && IsValidEvent?.Invoke(temple.DonationChain) != false)
        {
            // Only tested on the way out, and only once: the total is reset as it fires.
            TotalDonated = 0;
            BackupRequested = temple.ForceExit != 0;
            Current = null;
            return EventStep.To(temple.DonationChain);
        }

        return ChooseTownItem(TempleMenu, temple.ForceExit);
    }

    /// <summary><c>TAVERN::OnKeypress</c> (<c>RunEvent.cpp:9755</c>).</summary>
    /// <remarks>
    /// <b>FIGHT is the one town entry that chains rather than pushing a screen</b>, and it has a
    /// message of its own when the chain names nothing: "Everyone runs away. There's no one to
    /// fight!" — which is the only place a town service tells the player why nothing happened
    /// instead of silently staying put.
    /// </remarks>
    private EventStep ChooseTavern(TavernEvent tavern)
    {
        if (Menu.ActiveItem == 0)
        {
            if (tavern.FightChain == 0 || IsValidEvent?.Invoke(tavern.FightChain) == false)
            {
                ShowText(NoOneToFight);
                return EventStep.Running;
            }

            Current = null;
            return EventStep.To(tavern.FightChain);
        }

        return ChooseTownItem(TavernMenu, tavern.ForceExit);
    }

    /// <summary>What a tavern says when its brawl chains to nothing (<c>RunEvent.cpp:9782</c>).</summary>
    public const string NoOneToFight = "Everyone runs away. There's no one to fight!";

    /// <summary>
    /// The shared tail of a town menu: VIEW shows the sheet, EXIT chains, everything else is named.
    /// </summary>
    private EventStep ChooseTownItem((string Label, int Shortcut)[] entries, int forceExit)
    {
        int chosen = Menu.ActiveItem;
        string label = chosen >= 0 && chosen < entries.Length ? entries[chosen].Label : "option";

        if (label == "EXIT")
        {
            BackupRequested = forceExit != 0;
            return Complete(happened: true);
        }

        if (label == "ITEMS")
        {
            return OpenInventory(entries);
        }

        if (label == "VIEW" && ActiveCharacterSheet?.Invoke() is { } sheet && font is not null)
        {
            Stats = new CharStatsForm();
            Stats.Populate(font, sheet);
            return EventStep.Running;
        }

        // BUY, ITEMS, APPRAISE, TAKE, POOL, SHARE, DRINK and LISTEN each push a screen this port
        // has not built. HEAL and DONATE are handled by the temple itself.
        Unimplemented = $"[{label} here -- not implemented]";
        return EventStep.Running;
    }

    /// <summary>The anchors the current event was begun with, for a screen that rebuilds its menu.</summary>
    private MenuAnchors lastAnchors = new((0, 0), (0, 0), (0, 0), (0, 0));

    /// <summary>
    /// The active character's carried goods; set by the host, which owns the party.
    /// </summary>
    /// <remarks>
    /// Returning null means the inventory cannot be opened at all — which is what a caller with no
    /// party looks like, and is why ITEMS stays named in that case rather than opening an empty
    /// screen.
    /// </remarks>
    public Func<ItemList?>? ActiveCharacterItems { get; set; }

    /// <summary>Applies a change the inventory screen made; set by the host.</summary>
    public Action<ItemList>? ApplyItemChange { get; set; }

    /// <summary>The rows on the inventory screen, or null when it is not open.</summary>
    public IReadOnlyList<InventoryRow>? InventoryRows { get; private set; }

    /// <summary>Which town menu to rebuild when the inventory closes.</summary>
    private (string Label, int Shortcut)[]? inventoryParent;

    /// <summary>Whether the inventory is the screen on top.</summary>
    public bool InventoryOpen => InventoryRows is not null;

    /// <summary>
    /// Opens the shared inventory over the current service (<c>ITEMS_MENU_DATA</c>).
    /// </summary>
    /// <remarks>
    /// <b>The inventory replaces the service's menu rather than drawing over it</b>, unlike the
    /// character sheet — so closing it has to rebuild what was underneath, which is what
    /// <see cref="inventoryParent"/> remembers. The reference gets this for free by pushing an
    /// event and popping it; this runner presents one event at a time and has to put the parent
    /// back by hand.
    /// </remarks>
    private EventStep OpenInventory((string Label, int Shortcut)[] parent)
    {
        if (ActiveCharacterItems?.Invoke() is not { } carried)
        {
            Unimplemented = "[ITEMS here -- not implemented]";
            return EventStep.Running;
        }

        inventoryParent = parent;
        InventoryPage = 0;
        InventoryRowIndex = 0;
        InventoryRows = Inventory.Rows(carried, ItemNames);

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, Inventory.Menu);
        escapeSelects = (int)InventoryCommand.Exit;

        PopulateInventoryForm();
        return EventStep.Running;
    }

    /// <summary>Which page of the inventory is showing.</summary>
    /// <remarks>
    /// <b>The page lives here rather than in <see cref="ItemsForm"/></b>, which lays out a fixed
    /// number of rows and has no notion of a page. NEXT and PREV therefore re-populate the form
    /// with a slice rather than scrolling it — the same bytes on screen either way, and it avoids
    /// inventing a paging model in a class the treasure screen already shares.
    /// </remarks>
    public int InventoryPage { get; private set; }

    /// <summary>The rows on the page currently showing.</summary>
    public IReadOnlyList<InventoryRow> InventoryPageRows =>
        InventoryRows is null
            ? []
            : [.. InventoryRows.Skip(InventoryPage * PageSize).Take(PageSize)];

    /// <summary>
    /// Which row of the page the cursor is on (<c>party.activeItem</c>).
    /// </summary>
    /// <remarks>
    /// <b>It counts rows on the page, not items in the pack</b> — the reference wraps it modulo
    /// <c>ItemsOnPage</c> and clamps it whenever the page turns, so it never points past the
    /// short final page. <see cref="InventoryPageRows"/> plus this is what a command acts on.
    /// </remarks>
    public int InventoryRowIndex { get; private set; }

    private void PopulateInventoryForm()
    {
        if (InventoryRows is null)
        {
            return;
        }

        var page = InventoryPageRows;

        // The cursor clamps to the shorter final page rather than pointing off the end.
        if (InventoryRowIndex >= page.Count)
        {
            InventoryRowIndex = Math.Max(page.Count - 1, 0);
        }

        Items = new ItemsForm(PageSize);
        if (font is not null)
        {
            // READY on and COST off: this is a pack, not a shop's shelf. A shop turns the price
            // column on, which is the only thing that differs between the two presentations.
            Items.Populate(font,
                           [.. page.Select(r => new ItemsFormRow(
                               r.Ready, r.Quantity.ToString(), string.Empty, r.Name))],
                           useReady: true, useCost: false);
        }

        if (page.Count > 0)
        {
            Items.Select(InventoryRowIndex);
        }
    }

    /// <summary>
    /// Moves the cursor a row (<c>party.nextItem</c> / <c>prevItem</c>, <c>Party.cpp:2986</c>).
    /// </summary>
    /// <remarks>It wraps within the page — never onto the next one, which is what NEXT is for.</remarks>
    private void MoveInventoryRow(int delta)
    {
        int onPage = InventoryPageRows.Count;
        if (onPage <= 0)
        {
            return;
        }

        InventoryRowIndex = ((InventoryRowIndex + delta) % onPage + onPage) % onPage;
        Items?.Select(InventoryRowIndex);
    }

    /// <summary>
    /// Turns the page (<c>nextCharItemsPage</c> / <c>prevCharItemsPage</c>,
    /// <c>Disptext.cpp:577</c>).
    /// </summary>
    /// <remarks>
    /// <b>It stops at the ends rather than wrapping</b> — NEXT on the last page does nothing at
    /// all, which reads as a stuck key but is what the reference does, and matters because the
    /// menu entry and the Page Down key share this and would otherwise disagree.
    /// </remarks>
    private void TurnInventoryPage(int delta)
    {
        int count = InventoryRows?.Count ?? 0;

        if (delta > 0 && (InventoryPage + 1) * PageSize >= count)
        {
            return;
        }

        InventoryPage = Math.Max(InventoryPage + delta, 0);
        PopulateInventoryForm();
    }

    /// <summary>
    /// The inventory's own keys (<c>HMenuVInventoryKeyboardAction</c>, <c>RunEvent.cpp:748</c>).
    /// </summary>
    /// <remarks>
    /// <b>Horizontal menu, vertical inventory</b>: up and down move the item cursor and the page
    /// keys turn the page, while left and right fall through to the menu underneath. This is the
    /// only screen where the arrow keys are split between two things at once.
    /// </remarks>
    private bool HandleInventoryKey(VirtualKey key)
    {
        switch (key)
        {
            case VirtualKey.Up:
                MoveInventoryRow(-1);
                return true;

            case VirtualKey.Down:
                MoveInventoryRow(+1);
                return true;

            case VirtualKey.PageDown:
                TurnInventoryPage(+1);
                return true;

            case VirtualKey.PageUp:
                TurnInventoryPage(-1);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// <c>ITEMS_MENU_DATA::OnKeypress</c> (<c>RunEvent.cpp:7883</c>).
    /// </summary>
    /// <remarks>
    /// Four of the fourteen run; the rest are named. See <see cref="Inventory"/> for which and why.
    /// </remarks>
    private EventStep ChooseInventory()
    {
        var command = (InventoryCommand)Menu.ActiveItem;

        switch (command)
        {
            case InventoryCommand.Exit:
            {
                var parent = inventoryParent;
                InventoryRows = null;
                inventoryParent = null;
                Items = null;

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal,
                               parent ?? Inventory.Menu);
                escapeSelects = (parent?.Length ?? 1) - 1;
                return EventStep.Running;
            }

            case InventoryCommand.Next:
                TurnInventoryPage(+1);
                return EventStep.Running;

            case InventoryCommand.Prev:
                TurnInventoryPage(-1);
                return EventStep.Running;

            case InventoryCommand.Ready:
            {
                if (ActiveCharacterItems?.Invoke() is not { } carried)
                {
                    return EventStep.Running;
                }

                var page = InventoryPageRows;
                int row = InventoryRowIndex;
                if (row < 0 || row >= page.Count)
                {
                    return EventStep.Running;
                }

                var changed = Inventory.ToggleReady(carried, page[row].Index,
                                                    ItemDatabase ?? (_ => null),
                                                    out var refusal);
                LastRefusal = refusal;
                if (refusal is not ReadyRefusal.None)
                {
                    return EventStep.Running;
                }

                ApplyItemChange?.Invoke(changed);

                InventoryRows = Inventory.Rows(changed, ItemNames);
                PopulateInventoryForm();
                return EventStep.Running;
            }

            case InventoryCommand.Examine:
            {
                if (ExamineItem is null || ActiveCharacterItems?.Invoke() is not { } holding)
                {
                    return EventStep.Running;
                }

                var examinePage = InventoryPageRows;
                int examineRow = InventoryRowIndex;
                if (examineRow < 0 || examineRow >= examinePage.Count)
                {
                    return EventStep.Running;
                }

                int which = examinePage[examineRow].Index;
                if (which < 0 || which >= holding.Items.Count)
                {
                    return EventStep.Running;
                }

                // Whatever the item's scripts answered -- "CastSpell" and its siblings. Kept
                // rather than acted on: what each answer means is the spell and use machinery,
                // and nothing here should guess.
                LastExamineResult = ExamineItem(which);

                // The scripts can have changed what the character carries, so the list is rebuilt.
                if (ActiveCharacterItems?.Invoke() is { } afterExamine)
                {
                    InventoryRows = Inventory.Rows(afterExamine, ItemNames);
                    PopulateInventoryForm();
                }

                return EventStep.Running;
            }

            case InventoryCommand.Trade:
            {
                if (ActiveCharacterItems?.Invoke() is not { } toGive || GiveItemTo is null)
                {
                    return EventStep.Running;
                }

                var tradePage = InventoryPageRows;
                int tradeRow = InventoryRowIndex;
                if (tradeRow < 0 || tradeRow >= tradePage.Count)
                {
                    return EventStep.Running;
                }

                int give = tradePage[tradeRow].Index;
                if (give < 0 || give >= toGive.Items.Count)
                {
                    return EventStep.Running;
                }

                // The picker opens on whoever is already active, so at this moment the giver and
                // the taker are the same character -- and staying put is a legitimate answer, which
                // is how the reference lets a player re-arrange one inventory.
                pendingTrade = (GiverIndex?.Invoke() ?? 0, give);

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal,
                               ("SELECT CHAR", 0), ("EXIT", 0));
                escapeSelects = 1;

                return EventStep.Running;
            }

            case InventoryCommand.Sell:
            {
                // Only meaningful over a shop, and only one that buys at all -- a buyback of zero
                // means the reference never even offers, so nothing happens rather than an offer
                // of nothing.
                if (Current is not ShopEvent shop
                    || shop.BuybackPercentage <= 0
                    || ActiveCharacterItems?.Invoke() is not { } forSale)
                {
                    return EventStep.Running;
                }

                var sellPage = InventoryPageRows;
                int sellRow = InventoryRowIndex;
                if (sellRow < 0 || sellRow >= sellPage.Count)
                {
                    return EventStep.Running;
                }

                int at = sellPage[sellRow].Index;
                if (at < 0 || at >= forSale.Items.Count)
                {
                    return EventStep.Running;
                }

                var offered = forSale.Items[at];

                // The same flag DEPOSIT reads: one field governs all four ways out of the party.
                if ((ItemDatabase ?? (_ => null))(offered.ItemId) is not { } priced
                    || !InventoryBundles.CanLeaveTheParty(offered, ItemDatabase!))
                {
                    return EventStep.Running;
                }

                pendingSale = (at, Shopping.SellPrice(offered, priced, shop.BuybackPercentage));

                return AskToSell(pendingSale.Value.Price);
            }

            case InventoryCommand.Deposit:
            {
                // Only meaningful over a vault -- the reference asserts the parent event's type
                // rather than checking it, so a DEPOSIT anywhere else is a programming error there
                // and simply does nothing here.
                if (Current is not VaultEvent vault
                    || Vaults?.Invoke() is not { } vaults
                    || ActiveCharacterItems?.Invoke() is not { } carried)
                {
                    return EventStep.Running;
                }

                var page = InventoryPageRows;
                int row = InventoryRowIndex;
                if (row < 0 || row >= page.Count)
                {
                    return EventStep.Running;
                }

                var stack = new List<ItemInstance>(carried.Items);

                LastDepositRefusal = InventoryBundles.Deposit(
                    stack, page[row].Index, vaults, vault.WhichVault,
                    ItemDatabase ?? (_ => null));

                if (LastDepositRefusal is not InventoryBundles.DepositRefusal.None)
                {
                    return EventStep.Running;
                }

                var afterDeposit = carried with { Items = stack };
                ApplyItemChange?.Invoke(afterDeposit);

                InventoryRows = Inventory.Rows(afterDeposit, ItemNames);
                PopulateInventoryForm();
                return EventStep.Running;
            }

            case InventoryCommand.Halve:
            case InventoryCommand.Join:
            {
                if (ActiveCharacterItems?.Invoke() is not { } carried)
                {
                    return EventStep.Running;
                }

                var page = InventoryPageRows;
                int row = InventoryRowIndex;
                if (row < 0 || row >= page.Count)
                {
                    return EventStep.Running;
                }

                // The list is rebuilt from a mutable copy, because ItemList is a record and both
                // operations change how many entries there are rather than just their contents.
                var bundles = new List<ItemInstance>(carried.Items);
                var database = ItemDatabase ?? (_ => null);

                bool changed = command == InventoryCommand.Halve
                    ? InventoryBundles.Halve(bundles, page[row].Index, database)
                    : InventoryBundles.Join(bundles, page[row].Index, database);

                if (!changed)
                {
                    // Refused silently, as the reference does -- its screen simply redraws.
                    return EventStep.Running;
                }

                var written = carried with { Items = bundles };
                ApplyItemChange?.Invoke(written);

                InventoryRows = Inventory.Rows(written, ItemNames);
                PopulateInventoryForm();
                return EventStep.Running;
            }

            default:
                Unimplemented =
                    $"[{Inventory.Menu[Math.Clamp(Menu.ActiveItem, 0, Inventory.Menu.Length - 1)].Label}" +
                    " here -- not implemented]";
                return EventStep.Running;
        }
    }

    /// <summary>
    /// The vaults a DEPOSIT writes into, when the runner has been given them.
    /// </summary>
    /// <remarks>
    /// <b>A lookup rather than a reference, because the game replaces its vaults on load.</b>
    /// <c>Game.Vaults</c> is rebuilt from the save file, so a runner holding the object it was
    /// given at construction would write into the vaults of the game that was thrown away.
    /// Injected like the item database: the runner draws screens and does not own world state.
    /// </remarks>
    public Func<GlobalVaults?>? Vaults { get; set; }

    /// <summary>
    /// Why the last DEPOSIT was refused, or <see cref="InventoryBundles.DepositRefusal.None"/>.
    /// </summary>
    /// <remarks>
    /// <b>The reference reports only one of these</b> — an item being worn gets a message and every
    /// other refusal simply redraws. Exposed here for the same reason <see cref="LastRefusal"/>
    /// is: nothing should fail silently just because the original had nowhere to say so.
    /// </remarks>
    public InventoryBundles.DepositRefusal LastDepositRefusal { get; private set; }

    /// <summary>
    /// The sale waiting on a yes/no, as a row index and what the shop offered.
    /// </summary>
    /// <remarks>
    /// <b>The row is remembered rather than re-derived on answering.</b> The reference reads the
    /// list index again when the answer comes back (<c>RunEvent.cpp:8886</c>), which is safe there
    /// only because nothing can change the list while the question is up. Holding it is the same
    /// answer without depending on that.
    /// </remarks>
    private (int Row, int Price)? pendingSale;

    /// <summary>What the shop last offered, or null when nothing is being sold.</summary>
    public int? SellOffer => pendingSale?.Price;

    /// <summary>
    /// Puts the shop's offer up as a yes/no over the inventory.
    /// </summary>
    /// <remarks>
    /// The reference's wording, with the design's own name for the default coin
    /// (<c>RunEvent.cpp:8143</c>). The default is NO, as it is for every other confirmation.
    /// </remarks>
    private EventStep AskToSell(int price)
    {
        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, ("YES", 0), ("NO", 0));

        escapeSelects = 1;
        Menu.SetCurrentItem(1);
        ShowText($"I WILL GIVE YOU {price} {SellCurrencyName?.Invoke() ?? "GOLD"} "
                 + "FOR THIS ITEM, SELL IT?");

        return EventStep.Running;
    }

    /// <summary>The design's name for the coin a shop pays in; set by the host.</summary>
    public Func<string>? SellCurrencyName { get; set; }

    /// <summary>Hands the sale's proceeds to the seller; set by the host, which owns the purse.</summary>
    public Action<int>? ApplySale { get; set; }

    /// <summary>
    /// Answers the shop's offer.
    /// </summary>
    /// <remarks>
    /// <b>On YES the money goes to the character, not to the party's pooled purse</b> — the
    /// reference credits <c>dude.money</c> (<c>RunEvent.cpp:8887</c>), so whoever was carrying the
    /// thing keeps what it fetched.
    /// </remarks>
    private EventStep AnswerSale()
    {
        var sale = pendingSale!.Value;
        pendingSale = null;

        if (Menu.ActiveItem == 0 && ActiveCharacterItems?.Invoke() is { } carried
            && sale.Row >= 0 && sale.Row < carried.Items.Count)
        {
            var remaining = new List<ItemInstance>(carried.Items);
            remaining.RemoveAt(sale.Row);

            ApplySale?.Invoke(sale.Price);

            var after = carried with { Items = remaining };
            ApplyItemChange?.Invoke(after);
            InventoryRows = Inventory.Rows(after, ItemNames);
        }

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, Inventory.Menu);
        escapeSelects = (int)InventoryCommand.Exit;

        PopulateInventoryForm();
        return EventStep.Running;
    }

    /// <summary>
    /// The trade waiting on a taker, as the giver's party index and the row being handed over.
    /// </summary>
    /// <remarks>
    /// <b>The giver is remembered and the taker is whoever is active when the answer comes.</b>
    /// That is exactly how the reference works — the picker has no list of its own, it just leaves
    /// TAB switching the party as usual (<c>TRADE_TAKER_SELECT_MENU_DATA::OnKeypress</c>) and reads
    /// <c>party.activeCharacter</c> when RETURN arrives. So the "picker" is the ordinary party
    /// selection with a two-entry menu over it.
    /// </remarks>
    private (int Giver, int Row)? pendingTrade;

    /// <summary>Whether a trade is waiting for its taker to be chosen.</summary>
    public bool TradeOpen => pendingTrade is not null;

    /// <summary>Which party member is active; set by the host, which owns the party.</summary>
    public Func<int>? GiverIndex { get; set; }

    /// <summary>
    /// Hands a row from one party member to whoever is active now; set by the host.
    /// </summary>
    /// <remarks>
    /// The whole move is the host's, because the runner has one character's items and a trade
    /// needs two. It answers why it refused, which the screen keeps for
    /// <see cref="LastTradeRefusal"/>.
    /// </remarks>
    public Func<int, int, InventoryBundles.TradeRefusal>? GiveItemTo { get; set; }

    /// <summary>Why the last TRADE was refused.</summary>
    public InventoryBundles.TradeRefusal LastTradeRefusal { get; private set; }

    /// <summary>
    /// Answers the taker picker.
    /// </summary>
    /// <remarks>
    /// <b>EXIT abandons the trade rather than trading to whoever happens to be showing.</b> The
    /// reference falls through its <c>case 2</c> doing nothing and pops the screen.
    /// </remarks>
    private EventStep AnswerTrade()
    {
        var trade = pendingTrade!.Value;
        pendingTrade = null;

        if (Menu.ActiveItem == 0)
        {
            LastTradeRefusal = GiveItemTo!(trade.Giver, trade.Row);

            if (ActiveCharacterItems?.Invoke() is { } after)
            {
                InventoryRows = Inventory.Rows(after, ItemNames);
            }
        }

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, Inventory.Menu);
        escapeSelects = (int)InventoryCommand.Exit;

        PopulateInventoryForm();
        return EventStep.Running;
    }

    /// <summary>
    /// Runs a carried item's EXAMINE hook; set by the host, which owns the scripts.
    /// </summary>
    /// <remarks>
    /// <b>"EXAMINE" is a default label, not the command's name</b> — a design renames the entry
    /// per item, so this is the machinery behind READ, DRINK, LIGHT and anything else it puts
    /// there. See <see cref="ItemExamine"/>.
    /// </remarks>
    public Func<int, string>? ExamineItem { get; set; }

    /// <summary>
    /// What the last EXAMINE answered — <c>"CastSpell"</c> and its siblings, or empty.
    /// </summary>
    /// <remarks>
    /// Kept rather than acted on: what each answer means is the spell and use machinery, and
    /// nothing on this screen should guess.
    /// </remarks>
    public string LastExamineResult { get; private set; } = string.Empty;

    /// <summary>Why the last READY was refused, or <see cref="ReadyRefusal.None"/>.</summary>
    /// <remarks>
    /// The reference puts the reason in <c>errorText</c> and shows it in a message box the port
    /// does not have yet, so for now the reason is exposed rather than displayed. Nothing is
    /// silently swallowed either way.
    /// </remarks>
    public ReadyRefusal LastRefusal { get; private set; }

    /// <summary>
    /// Set when a town screen closed and the party owes a step backwards
    /// (<c>ForcePartyBackup</c> → <c>TASKMSG_MovePartyBackward</c>).
    /// </summary>
    /// <remarks>
    /// <b>This is what a town service's <c>forceExit</c> field means</b> — not "leave immediately"
    /// as the name suggests, but "step the party off the square on the way out", so walking into a
    /// shop does not leave them standing in its doorway re-triggering it. Four event types spell it
    /// four different ways (<c>ForceExit</c>, <c>ForceBackup</c>, <c>forceExit</c>) behind the one
    /// virtual <c>ForcePartyBackup</c>.
    /// </remarks>
    public bool BackupRequested { get; private set; }

    /// <summary>
    /// <c>ENCAMP_MENU_DATA</c> (<c>RunEvent.cpp:9108</c>), which <c>CAMP_EVENT_DATA</c> pushes
    /// immediately (<c>:11182</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The camp event itself is a wrapper with no screen of its own.</b> It pushes the encamp
    /// menu and, when that returns, backs the party up and chains — so what the player sees is
    /// entirely the inner menu, and the two are collapsed here.
    /// </para>
    /// <para>
    /// <b>Ten of its twelve entries run</b> — SAVE, LOAD, VIEW, MAGIC, REST, ALTER, TALK,
    /// JOURNAL, ZAP and EXIT, and MAGIC's own MEMORIZE and REST run under it. FIX needs the fix
    /// spell book and spell casting; QUIT is the game's own exit.
    /// </para>
    /// </remarks>
    private EventStep BeginCamp(CampEvent camp, MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, null, MenuOrientation.Horizontal, CampMenu);

        escapeSelects = CampExit;
        UpdateCampMenu();
        ShowText(camp.Base.Text);
        return EventStep.Running;
    }

    /// <summary>The encamp menu (<c>EncampMenuData</c>).</summary>
    public static readonly (string Label, int Shortcut)[] CampMenu =
        [("SAVE", 0), ("LOAD", 0), ("VIEW", 0), ("MAGIC", 0), ("REST", 0),
         ("ALTER", 0), ("FIX", 0), ("TALK", 0), ("JOURNAL", 0), ("ZAP", 0),
         ("EXIT", 1), ("QUIT", 0)];

    /// <summary>The EXIT item's index in the encamp menu.</summary>
    public const int CampExit = 10;

    private const int CampSave = 0;
    private const int CampLoad = 1;
    private const int CampMagic = 3;
    private const int CampRest = 4;
    private const int CampFix = 6;
    private const int CampTalk = 7;
    private const int CampJournal = 8;

    /// <summary>
    /// Which encamp entries light up (<c>ENCAMP_MENU_DATA::OnUpdateUI</c>,
    /// <c>RunEvent.cpp:9215</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>TALK needs both a valid event and a label.</b> A character with an event but no
    /// <c>TalkLabel</c> leaves the entry dark, because the label is what the entry is <i>renamed
    /// to</i> — the reference calls <c>changeMenuItem(8, dude.TalkLabel)</c> and then re-derives
    /// the first-letter shortcuts, so an empty label would produce a nameless entry.
    /// </para>
    /// <para>
    /// <b>SAVE is dark inside a global event and LOAD is not</b> — unless the encamp screen is
    /// itself global, or a fight is running, in which case both are. The asymmetry is the
    /// reference's: an event pushed from the world can be saved past, one that <i>is</i> the world
    /// cannot.
    /// </para>
    /// </remarks>
    private void UpdateCampMenu()
    {
        if (Menu.Count < CampMenu.Length)
        {
            return;
        }

        Menu.SetItemEnabled(CampJournal, (PartyJournal?.Invoke().Count ?? 0) > 0);

        var zone = ZoneHere?.Invoke() ?? new ZoneRules(true, true);

        Menu.SetItemEnabled(CampMagic, zone.AllowsMagic);

        // FIX is dark in a no-rest zone whatever pushed this camp; REST is only dark when the
        // camp came from the world rather than from an event. The asymmetry is the reference's
        // (RunEvent.cpp:9204) -- an event that camps the party can rest them somewhere they could
        // not have chosen to.
        Menu.SetItemEnabled(CampFix, zone.AllowsResting);
        Menu.SetItemEnabled(CampRest, zone.AllowsResting || CampPushedByEvent);

        var talk = TalkForActive?.Invoke() ?? default;

        bool canTalk = talk.Event != 0
                       && IsValidEvent?.Invoke(talk.Event) != false
                       && !string.IsNullOrEmpty(talk.Label)
                       && !talk.Silenced;

        Menu.SetItemEnabled(CampTalk, canTalk);

        // The label is not decoration: changeMenuItem(8, dude.TalkLabel) renames the entry and
        // then re-derives the first-letter shortcuts, so a character's own word appears on the
        // menu bar.
        if (canTalk)
        {
            Menu.SetItemText(CampTalk, talk.Label!);
        }
    }



    /// <summary>The ZAP item's text (<c>RunEvent.cpp:9323</c>).</summary>
    /// <remarks>
    /// A debug hook that pushes a text event saying <c>**SHAZAM**</c> and calls <c>ZapCmd()</c>.
    /// The text is on the wire in no design — the event class builds it inline — so it is a
    /// literal here too.
    /// </remarks>
    public const string ZapText = "**SHAZAM**ZapCmd()";

    /// <summary><c>ENCAMP_MENU_DATA::OnKeypress</c> (<c>RunEvent.cpp:9252</c>).</summary>
    private EventStep ChooseCamp(CampEvent camp)
    {
        switch (Menu.ActiveItem)
        {
            case 2 when ActiveCharacterSheet?.Invoke() is { } sheet && font is not null:
                Stats = new CharStatsForm();
                Stats.Populate(font, sheet);
                return EventStep.Running;

            case 7:
                // The active character's TALK event, which is a GLOBAL event rather than a level
                // one -- and a character with none pushes DO_NOTHING, so the screen stays up.
                uint talk = TalkEventOfActive?.Invoke() ?? 0;
                if (talk == 0 || IsValidEvent?.Invoke(talk) == false)
                {
                    return EventStep.Running;
                }
                Current = null;
                return EventStep.To(talk);

            case 9:
                ShowText(ZapText);
                return EventStep.Running;

            case 0:
                return OpenSlots(saving: true, CampMenu);

            case 1:
                return OpenSlots(saving: false, CampMenu);

            case 3:
                return OpenMagic();

            case 4:
                return OpenRest();

            case 5:
                return OpenAlter();

            case CampFix:
                return RunFix(FixEnvironment.Encamp);

            case 8:
                return OpenJournal(CampMenu);

            case CampExit:
                BackupRequested = camp.ForceExit != 0;
                return Complete(happened: true);

            default:
                // QUIT is the one entry left that pushes a screen this port has not built.
                Unimplemented = $"[{LabelOf(Menu.ActiveItem)} here -- not implemented]";
                return EventStep.Running;
        }
    }

    /// <summary>
    /// What the encamp screen's TALK entry offers for the active character.
    /// </summary>
    /// <param name="Event">The character's <c>TalkEvent</c>, or 0 for none.</param>
    /// <param name="Label">
    /// <c>TalkLabel</c> — the word the entry is renamed to. An empty one darkens the entry rather
    /// than leaving it nameless.
    /// </param>
    /// <param name="Silenced">
    /// <c>DisableTalkIfDead</c> against a status that is not <c>Okay</c>. A separate condition
    /// from the two above, applied after them.
    /// </param>
    public readonly record struct TalkOption(uint Event, string? Label, bool Silenced);

    /// <summary>What TALK offers; set by the host, which owns the party.</summary>
    public Func<TalkOption>? TalkForActive { get; set; }

    /// <summary>What the zone under the party permits.</summary>
    public readonly record struct ZoneRules(bool AllowsMagic, bool AllowsResting);

    /// <summary>The zone under the party; set by the host, which owns the level.</summary>
    public Func<ZoneRules>? ZoneHere { get; set; }

    /// <summary>
    /// Whether the encamp screen was pushed by an event rather than opened from the world
    /// (<c>m_pOrigEvent != NULL</c>).
    /// </summary>
    /// <remarks>
    /// <b>Always true in this port, because the other path does not exist yet.</b> The reference
    /// reaches the encamp menu two ways — a <c>CAMP_EVENT</c> on a square, or the player pressing
    /// the camp key while adventuring — and only the first is wired here. It is a property rather
    /// than a constant so the rule it feeds stays visible and correct when the second arrives.
    /// </remarks>
    public bool CampPushedByEvent { get; set; } = true;

    /// <summary>The active character's TALK event id; set by the host, which owns the party.</summary>
    public Func<uint>? TalkEventOfActive { get; set; }

    private string LabelOf(int item) =>
        item >= 0 && item < Menu.Count ? BitmapFont.Decode(Menu.Items[item].Text) : "option";

    /// <summary>
    /// <c>TRAININGHALL::OnInitialEvent</c> (<c>RunEvent.cpp:12040</c>) — the welcome screen.
    /// </summary>
    /// <remarks>
    /// <b>A yes/no, and nothing more.</b> YES pushes the character-picking menu that does the
    /// actual training; NO backs the party up and chains. The training itself is a separate event
    /// class this port has not built, so YES names it.
    /// </remarks>
    private EventStep BeginTrainingHall(TrainingHallEvent hall, MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, null, MenuOrientation.Horizontal, ("YES", 0), ("NO", 0));

        ShowText(hall.Base.Text);
        return EventStep.Running;
    }

    /// <summary><c>TRAININGHALL::OnKeypress</c> (<c>RunEvent.cpp:12065</c>).</summary>
    private EventStep ChooseTrainingHall(TrainingHallEvent hall)
    {
        if (Menu.ActiveItem == 0)
        {
            return OpenPartyMenu(hall);
        }

        BackupRequested = hall.ForceExit != 0;
        return Complete(happened: true);
    }

    /// <summary>
    /// The party menu's twelve entries, in the live table's order (<c>MainMenu</c>,
    /// <c>GameMenu.cpp:570</c>).
    /// </summary>
    /// <remarks>
    /// <b>The order beside it in the source is not the order it runs in.</b> Two twelve-entry
    /// lists sit in the file, one commented "original order" and one "new order"; the commented
    /// one leads with CREATE and DELETE, the live one with ADD and REMOVE. The branch numbers in
    /// <c>OnKeypress</c> happen to agree for the four entries that matter, which is exactly the
    /// kind of coincidence that makes reading the wrong list look fine.
    /// </remarks>
    public static readonly (string Label, int Shortcut)[] PartyMenu =
        [("ADD CHARACTER", 0), ("REMOVE CHARACTER", 0), ("MODIFY CHARACTER", 0),
         ("TRAIN CHARACTER", 0), ("CHANGE CLASS", 0), ("VIEW CHARACTER", 0),
         ("CREATE CHARACTER", 0), ("DELETE CHARACTER", 0), ("LOAD SAVED GAME", 0),
         ("SAVE CURRENT GAME", 0), ("BEGIN ADVENTURING", 0), ("EXIT FROM GAME", 1)];

    /// <summary>The entries, zero-based. The reference's <c>setItemInactive</c> takes them one-based.</summary>
    private const int PartyAdd = 0;
    private const int PartyRemove = 1;
    private const int PartyModify = 2;
    private const int PartyTrain = 3;
    private const int PartyChangeClass = 4;
    private const int PartyView = 5;
    private const int PartyCreate = 6;
    private const int PartyDelete = 7;
    private const int PartyLoad = 8;
    private const int PartySave = 9;
    private const int PartyBegin = 10;
    private const int PartyExit = 11;

    /// <summary>The training hall the party menu was opened from, or null.</summary>
    private TrainingHallEvent? partyMenuHall;

    /// <summary>Whether the party menu is the screen on top.</summary>
    public bool PartyMenuOpen { get; private set; }

    /// <summary>
    /// Opens the party menu over a training hall (<c>PushEvent(new MAIN_MENU_DATA(this))</c>,
    /// <c>RunEvent.cpp:12082</c>).
    /// </summary>
    /// <remarks>
    /// <b>This is the game's own top-level menu, borrowed.</b> The same screen runs at startup
    /// with no parent, and the difference is entirely in what lights up: TRAIN and CHANGE CLASS
    /// are dark unless a training hall pushed it, and BEGIN ADVENTURING pops back to the hall
    /// rather than loading the starting level.
    /// </remarks>
    private EventStep OpenPartyMenu(TrainingHallEvent hall)
    {
        partyMenuHall = hall;
        PartyMenuOpen = true;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Vertical, PartyMenu);
        UpdatePartyMenu();
        escapeSelects = PartyExit;

        return EventStep.Running;
    }

    /// <summary>
    /// Which entries are selectable (<c>MAIN_MENU_DATA::OnUpdateUI</c>, <c>RunEvent.cpp:2477</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Recomputed after every command, not once on open.</b> Training the active character —
    /// or tabbing to a different one — changes whether TRAIN is lit, so the reference calls this
    /// on each pass and so does this.
    /// </para>
    /// <para>
    /// <b>TRAIN's rule is three conditions, not one.</b> Ready to train, able to pay, and holding
    /// a baseclass this particular hall teaches — see <see cref="Training.CanTrain"/>. A character
    /// ready to advance in a baseclass the hall does not teach leaves the entry dark, which is the
    /// only feedback the player gets that they are in the wrong hall.
    /// </para>
    /// </remarks>
    private void UpdatePartyMenu()
    {
        if (!PartyMenuOpen)
        {
            return;
        }

        bool canTrain = partyMenuHall is { } hall && CanTrainHere?.Invoke(hall) == true;

        Menu.SetItemEnabled(PartyTrain, canTrain);

        // CHANGE CLASS is dark outside a training hall whatever the list says (RunEvent.cpp:2522),
        // which is the same gate TRAIN is behind. Inside one it follows
        // CreateChangeClassList != 0 -- and with no scripting layer that list is always empty, so
        // in practice this entry stays dark for every shipped design.
        Menu.SetItemEnabled(PartyChangeClass,
                            partyMenuHall is not null
                            && CanChangeClassHere?.Invoke() == true);

        // The reference darkens SAVE inside a global event or a fight. Reached only from a
        // training hall so far, where neither holds -- but the screen has no other owner yet.
        Menu.SetItemEnabled(PartySave, true);
    }

    /// <summary>Whether the active character can train at this hall; set by the host.</summary>
    /// <remarks>
    /// The runner has no party and no design, so it cannot answer this any more than it can hand
    /// over a treasure. <c>Game</c> wires it to <see cref="Training.CanTrain"/>.
    /// </remarks>
    public Func<TrainingHallEvent, bool>? CanTrainHere { get; set; }

    /// <summary>Trains the active character and returns what happened; set by the host.</summary>
    public Func<TrainingHallEvent, TrainingOutcome>? ApplyTraining { get; set; }

    /// <summary>What the last training session did, for the screen and for tests.</summary>
    public TrainingOutcome? LastTraining { get; private set; }

    /// <summary>The save slots, while the save or load screen is showing.</summary>
    public IReadOnlyList<SaveSlot>? Slots { get; private set; }

    /// <summary>Whether the slot screen showing is the save one rather than the load one.</summary>
    public bool SlotsForSaving { get; private set; }

    /// <summary>Whether a slot screen is on top.</summary>
    public bool SlotsOpen => Slots is not null;

    /// <summary>Lists the save slots; set by the host, which knows where the design lives.</summary>
    public Func<IReadOnlyList<SaveSlot>>? SaveSlotsAvailable { get; set; }

    /// <summary>
    /// Saves into a slot, or explains why not. Set by the host.
    /// </summary>
    /// <remarks>
    /// Returning a message rather than a bool: this is the one screen where the reason a thing
    /// cannot be done is worth more than the fact — see <see cref="SaveGameProjection"/>.
    /// </remarks>
    public Func<int, string?>? SaveToSlot { get; set; }

    /// <summary>Loads from a slot, or explains why not. Set by the host.</summary>
    public Func<int, string?>? LoadFromSlot { get; set; }

    /// <summary>
    /// <c>SAVEGAME_MENU_DATA</c> (<c>RunEvent.cpp:5500</c>) and <c>LOADGAME_MENU_DATA</c>
    /// (<c>:5541</c>) — one screen with two titles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both screens are the same eleven-entry menu.</b> <c>SaveMenuData</c> and
    /// <c>LoadMenuData</c> point at one shared array, so the two cannot drift apart.
    /// </para>
    /// <para>
    /// <b>Only the load screen darkens anything.</b> Saving over an occupied slot is allowed and
    /// unremarked — there is no "are you sure". Loading from an empty one is not, so
    /// <c>LOADGAME_MENU_DATA::OnUpdateUI</c> turns each slot without a file off, and the wording
    /// above the menu changes when none of them has one.
    /// </para>
    /// </remarks>
    private (string Label, int Shortcut)[]? slotParent;

    /// <param name="parent">
    /// The menu to rebuild when the screen closes, or null for the party menu. Camp pushes these
    /// screens too, and closing one there must not drop the player into a menu they never opened.
    /// </param>
    private EventStep OpenSlots(bool saving, (string Label, int Shortcut)[]? parent = null)
    {
        slotParent = parent;

        var slots = SaveSlotsAvailable?.Invoke() ?? SaveSlots.Under(null);

        Slots = slots;
        SlotsForSaving = saving;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, SaveSlots.Menu);

        if (!saving)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                Menu.SetItemEnabled(i, slots[i].Exists);
            }
        }

        escapeSelects = SaveSlots.Exit;

        ShowText(saving
            ? "CHOOSE WHICH SLOT TO SAVE GAME INTO"
            : SaveSlots.Any(slots)
                ? "CHOOSE WHICH SLOT TO LOAD GAME FROM"
                : "THERE ARE NO SAVED GAMES AVAILABLE");

        return EventStep.Running;
    }

    /// <summary>
    /// <c>SAVEGAME_MENU_DATA::OnKeypress</c> (<c>RunEvent.cpp:5514</c>) and its load twin.
    /// </summary>
    /// <remarks>
    /// <b>The screen closes either way.</b> Both pop unconditionally — a failed save leaves
    /// <c>miscError</c> set and returns to the menu just the same, so there is no retry loop and a
    /// player who picks a slot always lands back where they came from.
    /// </remarks>
    private EventStep ChooseSlots()
    {
        int chosen = Menu.ActiveItem;

        if (chosen != SaveSlots.Exit)
        {
            SlotMessage = SlotsForSaving
                ? SaveToSlot?.Invoke(chosen)
                : LoadFromSlot?.Invoke(chosen);
        }

        Slots = null;
        var parent = slotParent;
        slotParent = null;

        Menu.Reset();
        if (parent is null)
        {
            SetupFixedMenu(lastAnchors, null, MenuOrientation.Vertical, PartyMenu);
            UpdatePartyMenu();
            escapeSelects = PartyExit;
        }
        else
        {
            SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, parent);
            escapeSelects = parent.Length - 1;
            UpdateCampMenu();
        }

        if (SlotMessage is { Length: > 0 } message)
        {
            ShowText(message);
        }

        return EventStep.Running;
    }

    /// <summary>Why the last save or load did not happen, or null if it did.</summary>
    public string? SlotMessage { get; private set; }

    // ---- MEMORIZE ------------------------------------------------------------------------------

    /// <summary>The memorise menu (<c>MemorizeMenuData</c>, <c>GameMenu.cpp:880</c>).</summary>
    public static readonly (string Label, int Shortcut)[] MemorizeMenu =
        [("SELECT", 0), ("UNSELECT", 0), ("FORGET", 0), ("NEXT", 0), ("PREV", 0), ("EXIT", 1)];

    public const int MemorizeSelect = 0;
    public const int MemorizeUnselect = 1;
    public const int MemorizeForget = 2;
    public const int MemorizeNext = 3;
    public const int MemorizePrev = 4;
    public const int MemorizeExit = 5;

    /// <summary>The working list, or null when the screen is not up.</summary>
    public MemorizeList? Memorizing { get; private set; }

    /// <summary>Which row the cursor is on, within the page.</summary>
    public int MemorizeIndex { get; private set; }

    /// <summary>Which page of the list is showing.</summary>
    public int MemorizePage { get; private set; }

    /// <summary>Builds the working list for the active character; set by the host.</summary>
    public Func<MemorizeList?>? MemorizeFor { get; set; }

    /// <summary>Writes an edited list back onto the active character; set by the host.</summary>
    public Action<MemorizeList>? ApplyMemorize { get; set; }

    /// <summary>The rows on the page showing.</summary>
    public IReadOnlyList<MemorizeItem> MemorizePageRows =>
        [.. (Memorizing?.Items ?? []).Skip(MemorizePage * PageSize).Take(PageSize)];

    private int MemorizePageCount =>
        Math.Max(1, ((Memorizing?.Items.Count ?? 0) + PageSize - 1) / PageSize);

    /// <summary>
    /// <c>MEMORIZE_MENU_DATA::OnInitialEvent</c> (<c>RunEvent.cpp:25166</c>).
    /// </summary>
    /// <remarks>
    /// <b>A character who cannot cast never sees the screen.</b> <c>OnInitialEvent</c> pops
    /// straight back out — so the refusal is not a message, it is the screen failing to appear.
    /// </remarks>
    private EventStep OpenMemorize()
    {
        if (CanCastSpells?.Invoke() == false || MemorizeFor?.Invoke() is not { } list)
        {
            return EventStep.Running;
        }

        MagicOpen = false;
        Memorizing = list;
        MemorizeIndex = 0;
        MemorizePage = 0;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, MemorizeMenu);
        escapeSelects = MemorizeExit;
        UpdateMemorizeMenu();

        return EventStep.Running;
    }

    /// <summary>
    /// <c>MEMORIZE_MENU_DATA::OnUpdateUI</c> (<c>RunEvent.cpp:25208</c>).
    /// </summary>
    /// <remarks>
    /// <b>An empty list darkens everything but EXIT.</b> The reference returns early after
    /// disabling the other five, so a caster with no castable spells is left with one way out.
    /// </remarks>
    private void UpdateMemorizeMenu()
    {
        if (Memorizing is null || Menu.Count < MemorizeMenu.Length)
        {
            return;
        }

        Menu.SetAllItemsEnabled(true);

        if (Memorizing.Items.Count == 0)
        {
            for (int item = MemorizeSelect; item <= MemorizePrev; item++)
            {
                Menu.SetItemEnabled(item, false);
            }
            return;
        }

        var page = MemorizePageRows;
        var row = MemorizeIndex >= 0 && MemorizeIndex < page.Count ? page[MemorizeIndex] : null;

        Menu.SetItemEnabled(MemorizeSelect, row is not null && MemorizeList.CanSelect(row));
        Menu.SetItemEnabled(MemorizeUnselect, row is not null && MemorizeList.CanUnselect(row));
        Menu.SetItemEnabled(MemorizeForget, row is not null && MemorizeList.CanForget(row));

        bool pages = MemorizePageCount > 1;
        Menu.SetItemEnabled(MemorizeNext, pages);
        Menu.SetItemEnabled(MemorizePrev, pages);
    }

    private EventStep ChooseMemorize()
    {
        if (Memorizing is not { } list)
        {
            return EventStep.Running;
        }

        var page = MemorizePageRows;
        var row = MemorizeIndex >= 0 && MemorizeIndex < page.Count ? page[MemorizeIndex] : null;

        switch (Menu.ActiveItem)
        {
            case MemorizeSelect when row is not null:
                list.Select(row);
                break;

            case MemorizeUnselect when row is not null:
                list.Unselect(row);
                break;

            case MemorizeForget when row is not null:
                MemorizeList.Forget(row);
                break;

            case MemorizeNext:
                MemorizePage = (MemorizePage + 1) % MemorizePageCount;
                MemorizeIndex = 0;
                break;

            case MemorizePrev:
                MemorizePage = (MemorizePage + MemorizePageCount - 1) % MemorizePageCount;
                MemorizeIndex = 0;
                break;

            case MemorizeExit:
                // EXIT is the commit; there is no cancel.
                ApplyMemorize?.Invoke(list);
                return CloseMemorize();
        }

        UpdateMemorizeMenu();
        return EventStep.Running;
    }

    private EventStep CloseMemorize()
    {
        Memorizing = null;

        MagicOpen = true;
        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, MagicMenu);
        escapeSelects = MagicExit;
        UpdateMagicMenu();

        return EventStep.Running;
    }

    /// <summary>The list takes the vertical keys; the menu keeps the horizontal ones.</summary>
    private bool HandleMemorizeKey(VirtualKey key)
    {
        int onPage = MemorizePageRows.Count;
        if (onPage <= 0)
        {
            return false;
        }

        switch (key)
        {
            case VirtualKey.Up:
                MemorizeIndex = ((MemorizeIndex - 1) % onPage + onPage) % onPage;
                UpdateMemorizeMenu();
                return true;

            case VirtualKey.Down:
                MemorizeIndex = (MemorizeIndex + 1) % onPage;
                UpdateMemorizeMenu();
                return true;

            default:
                return false;
        }
    }

    // ---- MAGIC ---------------------------------------------------------------------------------

    /// <summary>The magic menu (<c>MagicMenuData</c>, <c>GameMenu.cpp:791</c>).</summary>
    public static readonly (string Label, int Shortcut)[] MagicMenu =
        [("CAST", 0), ("MEMORIZE", 0), ("SCRIBE", 0), ("DISPLAY", 0), ("REST", 0), ("EXIT", 1)];

    public const int MagicCast = 0;
    public const int MagicMemorize = 1;
    public const int MagicScribe = 2;
    public const int MagicDisplay = 3;
    public const int MagicRest = 4;
    public const int MagicExit = 5;

    /// <summary>Whether the magic hub is the screen on top.</summary>
    public bool MagicOpen { get; private set; }

    /// <summary>Whether the active character may cast; set by the host.</summary>
    public Func<bool>? CanCastSpells { get; set; }

    /// <summary>Whether MEMORIZE should appear for the active character; set by the host.</summary>
    public Func<bool>? CanMemorizeSpells { get; set; }

    /// <summary>
    /// What the SCRIBE entry is called, or null when it should not appear.
    /// </summary>
    /// <remarks>
    /// <b>The entry has no fixed name and no fixed meaning.</b> <c>CAN_SCRIBE_OR_WHATEVER</c> runs
    /// on the character and its class and answers with the menu text and a shortcut index; an
    /// empty answer darkens the entry. The reference's own constant is spelled
    /// <c>SCRIBE_OR_WHATEVER</c>, which says what the designers meant by it — with no script there
    /// is nothing to call it and nothing for it to do.
    /// </remarks>
    public Func<string?>? ScribeLabel { get; set; }

    /// <summary>Whether a fight is running, which darkens three of the six entries.</summary>
    public Func<bool>? InCombat { get; set; }

    /// <summary>
    /// How long the party needs to memorise everything, and who the rest woke; set by the host.
    /// </summary>
    /// <remarks>
    /// <b>Opening the rest screen is not passive.</b> It seeds the duration from
    /// <c>party.CalcRestTime()</c> and then calls <c>BeginResting</c>, which wakes the
    /// unconscious — both in <c>OnInitialEvent</c> (<c>RunEvent.cpp:22778</c>, <c>:22812</c>).
    /// </remarks>
    public Func<int>? BeginRest { get; set; }

    private EventStep OpenMagic()
    {
        MagicOpen = true;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, MagicMenu);
        escapeSelects = MagicExit;
        UpdateMagicMenu();

        return EventStep.Running;
    }

    /// <summary>
    /// <c>MAGIC_MENU_DATA::OnUpdateUI</c> (<c>RunEvent.cpp:26578</c>) — three rules over six
    /// entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A zone that forbids magic darkens four of them</b>, REST among them — and that branch
    /// is unreachable from camp, which darkens its own MAGIC entry on the same flag before this
    /// screen can open. It can only be reached from a magic menu pushed by combat.
    /// </para>
    /// <para>
    /// <b>In combat, MEMORIZE, SCRIBE and REST all go dark</b> and the character predicates are
    /// not consulted at all — the whole <c>else</c> branch that reads them is skipped.
    /// </para>
    /// </remarks>
    private void UpdateMagicMenu()
    {
        if (!MagicOpen || Menu.Count < MagicMenu.Length)
        {
            return;
        }

        Menu.SetAllItemsEnabled(true);

        var zone = ZoneHere?.Invoke() ?? new ZoneRules(true, true);

        if (!zone.AllowsMagic)
        {
            Menu.SetItemEnabled(MagicCast, false);
            Menu.SetItemEnabled(MagicMemorize, false);
            Menu.SetItemEnabled(MagicScribe, false);
            Menu.SetItemEnabled(MagicRest, false);
        }

        if (!zone.AllowsResting)
        {
            Menu.SetItemEnabled(MagicRest, false);
        }

        if (InCombat?.Invoke() == true)
        {
            Menu.SetItemEnabled(MagicMemorize, false);
            Menu.SetItemEnabled(MagicScribe, false);
            Menu.SetItemEnabled(MagicRest, false);
            return;
        }

        if (ScribeLabel?.Invoke() is { Length: > 0 } label)
        {
            Menu.SetItemText(MagicScribe, label);
            Menu.SetItemEnabled(MagicScribe, true);
        }
        else
        {
            Menu.SetItemEnabled(MagicScribe, false);
        }

        if (CanCastSpells?.Invoke() == false)
        {
            Menu.SetItemEnabled(MagicCast, false);
        }

        if (CanMemorizeSpells?.Invoke() == false)
        {
            Menu.SetItemEnabled(MagicMemorize, false);
        }
    }

    private EventStep ChooseMagic()
    {
        switch (Menu.ActiveItem)
        {
            // The hub is only closed once the pushed screen has actually opened. The reference
            // gets this free: MEMORIZE's OnInitialEvent pops itself for a character who cannot
            // cast, which lands back on the magic menu still sitting underneath.
            case MagicMemorize:
                return OpenMemorize();

            case MagicCast:
                return OpenCast(MagicMenu);

            case MagicRest:
                MagicOpen = false;
                return OpenRest(MagicMenu);

            case MagicExit:
                return CloseMagic();

            default:
                Unimplemented =
                    $"[{MagicMenu[Math.Clamp(Menu.ActiveItem, 0, MagicMenu.Length - 1)].Label}"
                    + " here -- not implemented]";
                return EventStep.Running;
        }
    }

    private EventStep CloseMagic()
    {
        MagicOpen = false;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, CampMenu);
        escapeSelects = CampExit;
        UpdateCampMenu();

        return EventStep.Running;
    }

    // ---- REST ----------------------------------------------------------------------------------

    /// <summary>The rest screen's menu (<c>RestMenuData</c>, <c>GameMenu.cpp:773</c>).</summary>
    public static readonly (string Label, int Shortcut)[] RestMenu =
        [("REST", 0), ("DAYS", 0), ("HOURS", 0), ("MINS", 0), ("ADD", 0), ("SUB", 0), ("EXIT", 1)];

    public const int RestBegin = 0;
    public const int RestDays = 1;
    public const int RestHours = 2;
    public const int RestMins = 3;
    public const int RestAdd = 4;
    public const int RestSub = 5;
    public const int RestExit = 6;

    /// <summary>Whether the rest screen is up.</summary>
    public bool RestOpen { get; private set; }

    /// <summary>
    /// Whether the party is actually resting rather than setting a duration.
    /// </summary>
    /// <remarks>
    /// <b>The menu is hidden while this is true</b> (<c>m_showMenu = FALSE</c>) — the player has
    /// nothing to press but the key that stops it.
    /// </remarks>
    public bool RestEngaged { get; private set; }

    /// <summary>
    /// The duration picker — the form that was built rounds ago and had no screen behind it.
    /// </summary>
    public RestTimeForm? RestTime { get; private set; }

    /// <summary>How long is left to rest.</summary>
    public RestDuration RestLeft =>
        RestTime is { } form
            ? new RestDuration((int)form.Days, (int)form.Hours, (int)form.Minutes)
            : default;

    /// <summary>Advances the clock by this many minutes; set by the host.</summary>
    public Action<int>? AdvanceClock { get; set; }

    /// <summary>
    /// Runs everything that depends on the clock, once per cycle
    /// (<c>PARTY::ProcessTimeSensitiveData</c>). Set by the host, which owns the party.
    /// </summary>
    /// <remarks>
    /// <b>Called once with the whole elapsed step, not once per minute.</b> The reference's
    /// minute loop only advances the clock and the rest-event counter; the time-sensitive pass
    /// runs after it, which is what makes the auto-heal at most one point per cycle.
    /// </remarks>
    public Action<int, bool>? ProcessTime { get; set; }

    /// <summary>
    /// Whether a rest event fires this minute, and which. Set by the host, which owns the zones.
    /// </summary>
    /// <remarks>
    /// Called once per game minute, because the reference's counter is incremented per minute and
    /// its check is "has <c>everyMin</c> passed since the last roll".
    /// </remarks>
    public Func<uint>? RestEventThisMinute { get; set; }

    /// <summary>
    /// <c>REST_MENU_DATA::OnInitialEvent</c> (<c>RunEvent.cpp:22769</c>).
    /// </summary>
    private (string Label, int Shortcut)[]? restParent;

    /// <param name="parent">
    /// The menu to rebuild on leaving, or null for camp's. MAGIC pushes this screen too.
    /// </param>
    private EventStep OpenRest((string Label, int Shortcut)[]? parent = null)
    {
        restParent = parent;

        RestOpen = true;
        RestEngaged = false;
        RestTime = new RestTimeForm(lastAnchors.TextBox.X, lastAnchors.TextBox.Y);

        // The screen opens already set to how long the party needs to memorise everything, so a
        // player who just wants their spells back presses REST and nothing else.
        if (BeginRest?.Invoke() is int needed && needed > 0 && font is not null)
        {
            RestTime.SetTime(font, needed / RestDuration.MinutesPerDay,
                             needed / RestDuration.MinutesPerHour % 24,
                             needed % RestDuration.MinutesPerHour);
        }

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, RestMenu);
        escapeSelects = RestExit;
        ShowText("HOW LONG WILL YOU REST?");

        return EventStep.Running;
    }

    private EventStep ChooseRest()
    {
        switch (Menu.ActiveItem)
        {
            case RestBegin:
                // Nothing stops a rest of no time at all; it simply finishes on the first cycle.
                RestEngaged = true;
                return EventStep.Running;

            case RestDays:
                RestTime?.Select(RestField.Days);
                return EventStep.Running;

            case RestHours:
                RestTime?.Select(RestField.Hours);
                return EventStep.Running;

            case RestMins:
                RestTime?.Select(RestField.Minutes);
                return EventStep.Running;

            case RestAdd:
                Adjust(up: true);
                return EventStep.Running;

            case RestSub:
                Adjust(up: false);
                return EventStep.Running;

            default:
                return CloseRest();
        }
    }

    private EventStep CloseRest()
    {
        RestOpen = false;
        RestEngaged = false;
        RestTime = null;

        var parent = restParent;
        restParent = null;

        Menu.Reset();

        if (parent is null)
        {
            SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, CampMenu);
            escapeSelects = CampExit;
            UpdateCampMenu();
        }
        else
        {
            MagicOpen = true;
            SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, parent);
            escapeSelects = parent.Length - 1;
            UpdateMagicMenu();
        }

        return EventStep.Running;
    }

    /// <summary>
    /// The rest screen's arrow keys, which move the duration rather than the menu.
    /// </summary>
    /// <remarks>
    /// <b>The menu's left and right still move the cursor</b>, and moving it onto DAYS, HOURS or
    /// MINS re-activates that field — the reference re-syncs the form to the menu after every
    /// keypress (<c>RunEvent.cpp:22673</c>) so the two can never disagree.
    /// </remarks>
    private bool HandleRestKey(VirtualKey key)
    {
        if (!RestOpen || RestEngaged)
        {
            return false;
        }

        switch (key)
        {
            case VirtualKey.Up:
            case VirtualKey.Add:
                Adjust(up: true);
                return true;

            case VirtualKey.Down:
            case VirtualKey.Subtract:
                Adjust(up: false);
                return true;

            default:
                return false;
        }
    }

    private void Adjust(bool up)
    {
        if (RestTime is not { } form || font is null)
        {
            return;
        }

        if (up)
        {
            form.Increment(font);
        }
        else
        {
            form.Decrement(font);
        }
    }

    /// <summary>Keeps the highlighted field in step with the menu cursor.</summary>
    private void SyncRestField()
    {
        switch (Menu.ActiveItem)
        {
            case RestDays: RestTime?.Select(RestField.Days); break;
            case RestHours: RestTime?.Select(RestField.Hours); break;
            case RestMins: RestTime?.Select(RestField.Minutes); break;
        }
    }

    /// <summary>
    /// One turn of the engine's clock (<c>GameEvent::OnCycle</c>).
    /// </summary>
    /// <returns>Whether anything changed and the screen should be redrawn.</returns>
    /// <remarks>
    /// <para>
    /// <b>This runs whether or not the player pressed anything</b> — it is what lets a rest pass
    /// time on its own. Only the rest screen uses it so far; the reference also drives spell-effect
    /// expiry and drunk points through it, by way of <c>ProcessTimeSensitiveData</c>.
    /// </para>
    /// <para>
    /// <b>Time advances a minute at a time even though the delta is coarse</b>, because the rest
    /// event's counter is per-minute and a rest can be interrupted part-way through a step.
    /// </para>
    /// </remarks>
    public EventStep Cycle()
    {
        if (!RestEngaged)
        {
            return EventStep.Running;
        }

        int delta = RestLeft.MinuteDelta();
        int passed = 0;

        for (int elapsed = 0; elapsed < delta; elapsed++)
        {
            passed++;
            AdvanceClock?.Invoke(1);

            var left = RestLeft.Less(1);
            RestTime?.SetTime(font!, left.Days, left.Hours, left.Minutes);

            if (RestEventThisMinute?.Invoke() is { } fired && fired != 0
                && IsValidEvent?.Invoke(fired) != false)
            {
                // "YOUR REST IS INTERRUPTED!", and the event replaces this screen rather than
                // being pushed over it -- so there is no rest to come back to.
                RestOpen = false;
                RestEngaged = false;
                RestTime = null;
                Current = null;

                // The interrupted rest still counts the minutes it managed.
                ProcessTime?.Invoke(passed, true);
                ShowText(InterruptedText);

                return EventStep.To(fired);
            }
        }

        ProcessTime?.Invoke(passed, true);

        if (RestLeft.Elapsed)
        {
            RestEngaged = false;
            Menu.SetCurrentItem(RestBegin);
        }

        return EventStep.Running;
    }


    /// <summary>What a rest event's interruption says (<c>RunEvent.cpp:22919</c>).</summary>
    public const string InterruptedText = "YOUR REST IS INTERRUPTED!";

    // ---- ALTER ---------------------------------------------------------------------------------

    /// <summary>The ALTER menu (<c>AlterMenuData</c>, <c>GameMenu.cpp:1027</c>).</summary>
    public static readonly (string Label, int Shortcut)[] AlterMenu =
        [("ORDER", 0), ("DROP", 0), ("SPEED", 0), ("ICON", 0), ("PIC", 0),
         ("LEVEL", 0), ("VOLUME", 0), ("MUSIC", 0), ("EXIT", 1)];

    public const int AlterOrder = 0;
    public const int AlterDrop = 1;
    public const int AlterExit = 8;

    /// <summary>Whether the ALTER hub is the screen on top.</summary>
    public bool AlterOpen { get; private set; }

    /// <summary>Whether the party-order screen is up.</summary>
    public bool OrderingParty { get; private set; }

    /// <summary>Moves the active character up or down the marching order; set by the host.</summary>
    public Action<bool>? MoveActive { get; set; }

    /// <summary>How many characters the party has; set by the host.</summary>
    public Func<int>? PartySize { get; set; }

    /// <summary>
    /// <c>ALTER_GAME_MENU_DATA</c> (<c>RunEvent.cpp:22407</c>) — nine entries over one character.
    /// </summary>
    private EventStep OpenAlter()
    {
        AlterOpen = true;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, AlterMenu);
        escapeSelects = AlterExit;
        UpdateAlterMenu();

        return EventStep.Running;
    }

    /// <summary>
    /// <b>ORDER and DROP are dark below two characters</b> (<c>:22423</c>) — there is no order to
    /// alter with one, and dropping the last member would leave no party.
    /// </summary>
    private void UpdateAlterMenu()
    {
        if (!AlterOpen || Menu.Count < AlterMenu.Length)
        {
            return;
        }

        bool several = (PartySize?.Invoke() ?? 0) > 1;

        Menu.SetItemEnabled(AlterOrder, several);
        Menu.SetItemEnabled(AlterDrop, several);
    }

    private EventStep ChooseAlter()
    {
        switch (Menu.ActiveItem)
        {
            case AlterOrder:
                OrderingParty = true;

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, ("EXIT", 1));
                escapeSelects = 0;
                ShowText("USE UP AND DOWN TO CHANGE THE MARCHING ORDER");
                return EventStep.Running;

            case AlterDrop:
                return AskAbout(PartyConfirm.Remove, "REMOVE {0} FROM PARTY?", AlterMenu);

            case AlterExit:
                AlterOpen = false;

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, CampMenu);
                escapeSelects = CampExit;
                UpdateCampMenu();
                return EventStep.Running;

            default:
                Unimplemented =
                    $"[{AlterMenu[Math.Clamp(Menu.ActiveItem, 0, AlterMenu.Length - 1)].Label}"
                    + " here -- not implemented]";
                return EventStep.Running;
        }
    }

    /// <summary>
    /// <c>ALTER_ORDER_MENU_DATA</c> (<c>RunEvent.cpp:22581</c>) — one EXIT entry, and the work is
    /// all in the arrow keys.
    /// </summary>
    private bool HandleOrderKey(VirtualKey key)
    {
        if (!OrderingParty)
        {
            return false;
        }

        switch (key)
        {
            case VirtualKey.Up:
                MoveActive?.Invoke(true);
                return true;

            case VirtualKey.Down:
                MoveActive?.Invoke(false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Return on the order screen means "done", whatever the cursor is on.</summary>
    private EventStep CloseOrder()
    {
        OrderingParty = false;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, AlterMenu);
        escapeSelects = AlterExit;
        UpdateAlterMenu();

        return EventStep.Running;
    }

    // ---- the journal ---------------------------------------------------------------------------

    /// <summary>The journal's wrapped text, or null when the screen is not up.</summary>
    public TextDisplayData? JournalText { get; private set; }

    private (string Label, int Shortcut)[]? journalParent;

    /// <summary>The party's journal; set by the host, which owns the party.</summary>
    public Func<IReadOnlyList<JournalEntry>>? PartyJournal { get; set; }

    /// <summary>
    /// <c>DISPLAY_PARTY_JOURNAL_DATA</c> (<c>RunEvent.cpp:27604</c>).
    /// </summary>
    /// <remarks>
    /// <b>It opens on the <i>last</i> box.</b> <c>OnInitialEvent</c> formats the text and then
    /// calls <c>LastJournalBox()</c> — a player opening the journal wants what just happened, not
    /// the first thing that ever did.
    /// </remarks>
    private EventStep OpenJournal((string Label, int Shortcut)[] parent)
    {
        if (font is null)
        {
            Unimplemented = "[JOURNAL here -- not implemented]";
            return EventStep.Running;
        }

        string text = JournalScreen.Text(PartyJournal?.Invoke() ?? []);

        var data = TextFormatter.Format(text, Box.Width, font);
        data.LinesPerBox = TextDisplayData.JournalLinesPerBox;
        data.LastJournalBox();

        JournalText = data;
        journalParent = parent;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, JournalScreen.Menu);
        escapeSelects = JournalScreen.Exit;
        UpdateJournalMenu();

        return EventStep.Running;
    }

    /// <summary>NEXT and PREV darken at the ends; FIRST, LAST and EXIT never do.</summary>
    private void UpdateJournalMenu()
    {
        if (JournalText is not { } data)
        {
            return;
        }

        Menu.SetItemEnabled(JournalScreen.Next, !data.IsLastJournalBox);
        Menu.SetItemEnabled(JournalScreen.Previous, !data.IsFirstJournalBox);
    }

    private EventStep ChooseJournal()
    {
        if (JournalText is not { } data)
        {
            return EventStep.Running;
        }

        switch (Menu.ActiveItem)
        {
            case JournalScreen.Next:
                data.NextJournalBox();
                break;

            case JournalScreen.Previous:
                data.PrevJournalBox();
                break;

            case JournalScreen.First:
                data.FirstJournalBox();
                break;

            case JournalScreen.Last:
                data.LastJournalBox();
                break;

            default:
            {
                var parent = journalParent;
                JournalText = null;
                journalParent = null;

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal,
                               parent ?? JournalScreen.Menu);
                escapeSelects = (parent?.Length ?? 1) - 1;
                return EventStep.Running;
            }
        }

        UpdateJournalMenu();
        return EventStep.Running;
    }

    // ---- ADD, REMOVE and DELETE ----------------------------------------------------------------

    /// <summary>The roster, while ADD CHARACTER is showing it.</summary>
    public CharacterRoster? Roster { get; private set; }

    /// <summary>Whether the roster is the screen on top.</summary>
    public bool RosterOpen => Roster is not null;

    /// <summary>Which roster entry each menu line stands for.</summary>
    private List<RosterMenuLine> rosterLines = [];

    /// <summary>Which entry the page starts at.</summary>
    private int rosterFirst;

    /// <summary>Builds the roster; set by the host, which knows the design and the save folder.</summary>
    public Func<CharacterRoster>? AvailableCharacters { get; set; }

    /// <summary>Applies the roster's marks; set by the host.</summary>
    public Action<CharacterRoster>? ApplyRoster { get; set; }

    /// <summary>
    /// <c>ADD_CHARACTER_DATA</c> (<c>RunEvent.cpp:3192</c>) — every character available to join.
    /// </summary>
    private EventStep OpenRoster()
    {
        if (AvailableCharacters?.Invoke() is not { } roster)
        {
            Unimplemented = "[ADD CHARACTER here -- not implemented]";
            return EventStep.Running;
        }

        Roster = roster;
        rosterFirst = 0;
        FillRosterMenu();
        return EventStep.Running;
    }

    private void FillRosterMenu()
    {
        if (Roster is null)
        {
            return;
        }

        rosterLines = RosterMenu.Lines(Roster, rosterFirst, PageSize);

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Vertical,
                       [.. rosterLines.Select(l => (l.Label, l.Kind is RosterLine.Exit ? 1 : 0))]);

        escapeSelects = rosterLines.Count - 1;   // EXIT is always last
    }

    /// <summary>
    /// <c>ADD_CHARACTER_DATA::OnKeypress</c> (<c>RunEvent.cpp:3003</c>).
    /// </summary>
    /// <remarks>
    /// <b>Nothing is applied until EXIT.</b> Selecting a name toggles a mark and redraws; leaving
    /// adds every marked character and removes every unmarked one. A player can browse the whole
    /// roster and change their mind, and a mis-click costs nothing.
    /// </remarks>
    private EventStep ChooseRoster()
    {
        if (Roster is null || Menu.ActiveItem < 0 || Menu.ActiveItem >= rosterLines.Count)
        {
            return EventStep.Running;
        }

        var line = rosterLines[Menu.ActiveItem];

        switch (line.Kind)
        {
            case RosterLine.Previous:
                rosterFirst = RosterMenu.PreviousPage(rosterFirst, PageSize);
                FillRosterMenu();
                return EventStep.Running;

            case RosterLine.Next:
                rosterFirst += rosterLines.Count(l => l.Kind is RosterLine.Character);
                FillRosterMenu();
                return EventStep.Running;

            case RosterLine.Character:
                Roster.Toggle(line.Index);
                FillRosterMenu();
                return EventStep.Running;

            default:
            {
                ApplyRoster?.Invoke(Roster);
                Roster = null;
                rosterLines = [];

                Menu.Reset();
                SetupFixedMenu(lastAnchors, null, MenuOrientation.Vertical, PartyMenu);
                UpdatePartyMenu();
                escapeSelects = PartyExit;
                return EventStep.Running;
            }
        }
    }

    // ---- CREATE CHARACTER ----------------------------------------------------------------------

    /// <summary>The character being made, or null when the generator is not running.</summary>
    public CharacterCreation? Creating { get; private set; }

    /// <summary>What is on offer at the current step.</summary>
    public IReadOnlyList<CreationChoice> CreationOffered { get; private set; } = [];

    /// <summary>Which offer the cursor is on.</summary>
    public int CreationIndex { get; private set; }

    /// <summary>Which page of the offers is showing.</summary>
    public int CreationPage { get; private set; }

    /// <summary>Writes the finished character; returns null on success or the reason it did not.</summary>
    public Func<CharacterCreation, string?>? SaveCreatedCharacter { get; set; }

    /// <summary>Why the new character was not saved, or null if it was.</summary>
    public string? SaveMessage { get; private set; }

    /// <summary>What a step may pick; set by the host, which owns the design's tables.</summary>
    public Func<CharacterCreation, IReadOnlyList<CreationChoice>>? CreationChoicesFor { get; set; }

    /// <summary>
    /// The picker's own menu — <c>SelectMenuData</c>, shared by all four
    /// (<c>RunEvent.cpp:3280</c>).
    /// </summary>
    /// <remarks>
    /// <b>EXIT abandons the whole character, not the step.</b> Every picker sets
    /// <c>m_AbortCharCreation</c> and unwinds; none of them steps back one, so a player who picks
    /// the wrong race starts again.
    /// </remarks>
    public static readonly (string Label, int Shortcut)[] CreationMenu =
        [("SELECT", 0), ("NEXT", 0), ("PREV", 0), ("EXIT", 1)];

    private const int CreationSelect = 0;
    private const int CreationNext = 1;
    private const int CreationPrev = 2;
    private const int CreationExit = 3;

    /// <summary>Starts the character generator (<c>CREATE_CHARACTER_DATA</c>).</summary>
    private EventStep BeginCreation()
    {
        if (CreationChoicesFor is null)
        {
            Unimplemented = "[CREATE CHARACTER here -- not implemented]";
            return EventStep.Running;
        }

        Creating = new CharacterCreation();
        return ShowCreationStep();
    }

    /// <summary>
    /// Puts the current step on screen, or ends the generator when it runs out of ported ones.
    /// </summary>
    /// <remarks>
    /// <b>Four of the ten steps run.</b> Race, gender, class and alignment are one screen with
    /// four sources; stats, name, icon, small picture and the two spell screens each need
    /// machinery the port does not have — an ability roller, text entry, an art picker — and the
    /// generator stops and says which rather than producing half a character.
    /// </remarks>
    private EventStep ShowCreationStep()
    {
        if (Creating is null)
        {
            return EventStep.Running;
        }

        // Every step past the name is done typing. Leaving this set makes HandleTyping swallow
        // the movement keys, which is invisible on a screen whose cursor already sits on the only
        // entry that does anything -- and total on one that pages.
        if (Creating.Step is not CreationStep.Name)
        {
            Typing = null;
        }

        // The two art steps share one screen -- see ArtPicker.
        if (!Creating.Aborted && Creating.Step is CreationStep.Icon or CreationStep.SmallPicture)
        {
            return ShowArtScreen();
        }

        // The two spell steps share one screen -- see SpellScreen.
        if (!Creating.Aborted && Creating.Step is CreationStep.Spells)
        {
            return ShowSpellScreen();
        }

        // CHOOSESTATS is a re-roll screen, not the thing that makes the stats: the character was
        // already generated at the alignment step, and item 2 there is "don't re-roll". A host
        // that has not wired one skips the step and keeps the first roll, rather than stranding
        // the wizard one step short of the name.
        if (!Creating.Aborted && Creating.Step is CreationStep.Stats)
        {
            if (StatsForCreation?.Invoke(Creating) is { } screen)
            {
                return ShowStats(screen);
            }

            Creating.SkipStats();
        }

        // The name step asks for typed text rather than offering a list -- the same entry the
        // password screen uses, with the name rules.
        if (!Creating.Aborted && Creating.Step is CreationStep.Name)
        {
            Typing = new TextEntry(TextEntryRules.Name);

            Menu.Reset();
            SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, ("EXIT", 1));
            escapeSelects = 0;
            ShowText("ENTER NEW CHARACTER NAME");

            return EventStep.Running;
        }

        // The last step: keep this character, or throw it away.
        if (!Creating.Aborted && Creating.Step is CreationStep.AskToSave)
        {
            Menu.Reset();
            SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, ("YES", 0), ("NO", 0));
            escapeSelects = 1;
            Menu.SetCurrentItem(1);
            ShowText($"DO YOU WANT TO SAVE {Creating.CharacterName}?");
            return EventStep.Running;
        }

        if (Creating.Aborted || Creating.Step > CreationStep.AskToSave)
        {
            var reached = Creating.Step;
            bool aborted = Creating.Aborted;

            Creating = null;
            CreationOffered = [];

            Menu.Reset();
            SetupFixedMenu(lastAnchors, null, MenuOrientation.Vertical, PartyMenu);
            UpdatePartyMenu();
            escapeSelects = PartyExit;

            if (!aborted)
            {
                Unimplemented = $"[{reached} and the steps after it -- not implemented]";
            }
            return EventStep.Running;
        }

        Typing = null;
        ArtChoices = null;
        CreationOffered = CreationChoicesFor?.Invoke(Creating) ?? [];
        CreationIndex = 0;
        CreationPage = 0;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, CreationMenu);
        escapeSelects = CreationExit;
        ShowText($"SELECT {Creating.Step.ToString().ToUpperInvariant()}");

        return EventStep.Running;
    }

    /// <summary>The offers on the page showing.</summary>
    public IReadOnlyList<CreationChoice> CreationPageOffers =>
        [.. CreationOffered.Skip(CreationPage * PageSize).Take(PageSize)];

    private int CreationPageCount =>
        Math.Max(1, ((CreationOffered.Count + PageSize - 1) / PageSize));

    /// <summary>
    /// <c>RACE_MENU_DATA::OnKeypress</c> and its three twins (<c>RunEvent.cpp:3227</c>).
    /// </summary>
    private EventStep ChooseCreation()
    {
        if (Creating is null)
        {
            return EventStep.Running;
        }

        // The save prompt: YES writes the character, NO throws it away. Either ends the wizard.
        if (Creating.Step is CreationStep.AskToSave)
        {
            if (Menu.ActiveItem == 0)
            {
                SaveMessage = SaveCreatedCharacter?.Invoke(Creating);
            }

            Creating.Finish();
            return ShowCreationStep();
        }

        // The name step has no picker menu -- Return commits whatever has been typed, and an
        // empty name is refused rather than accepted (the reference needs "at least one
        // character", RunEvent.cpp:3686).
        if (Typing is { } typed)
        {
            if (Menu.ActiveItem == 0 && Menu.Count == 1 && typed.Text.Length == 0)
            {
                Creating.Abort();
                return ShowCreationStep();
            }

            if (typed.Text.Length == 0)
            {
                return EventStep.Running;
            }

            Creating.Name(typed.Text);
            return ShowCreationStep();
        }

        switch (Menu.ActiveItem)
        {
            case CreationSelect:
            {
                var page = CreationPageOffers;
                if (CreationIndex < 0 || CreationIndex >= page.Count)
                {
                    return EventStep.Running;
                }

                Creating.Choose(page[CreationIndex].Id);
                return ShowCreationStep();
            }

            case CreationNext:
                CreationPage = (CreationPage + 1) % CreationPageCount;
                CreationIndex = 0;
                return EventStep.Running;

            case CreationPrev:
                CreationPage = (CreationPage + CreationPageCount - 1) % CreationPageCount;
                CreationIndex = 0;
                return EventStep.Running;

            default:
                Creating.Abort();
                return ShowCreationStep();
        }
    }

    /// <summary>
    /// The picker's vertical keys — the same split the inventory has
    /// (<c>HMenuVItemsKeyboardAction</c>).
    /// </summary>
    private bool HandleCreationKey(VirtualKey key)
    {
        int onPage = CreationPageOffers.Count;
        if (onPage <= 0)
        {
            return false;
        }

        switch (key)
        {
            case VirtualKey.Up:
                CreationIndex = ((CreationIndex - 1) % onPage + onPage) % onPage;
                return true;

            case VirtualKey.Down:
                CreationIndex = (CreationIndex + 1) % onPage;
                return true;

            default:
                return false;
        }
    }

    // ---- CHANGE CLASS --------------------------------------------------------------------------

    /// <summary>The classes on offer, or null when the change-class screen is not up.</summary>
    public IReadOnlyList<string>? ClassChoices { get; private set; }

    /// <summary>Which one the cursor is on.</summary>
    public int ClassIndex { get; private set; }

    /// <summary>Lists what the active member could change to; set by the host.</summary>
    public Func<IReadOnlyList<string>>? ClassChangesFor { get; set; }

    /// <summary>Whether the entry lights up — <c>CreateChangeClassList(NULL) != 0</c>.</summary>
    public Func<bool>? CanChangeClassHere { get; set; }

    /// <summary>Applies a chosen class to the active member.</summary>
    public Action<string>? ApplyClassChange { get; set; }

    /// <summary>
    /// <c>CHANGE_CLASS_MENU_DATA</c> (<c>RunEvent.cpp:21949</c>) — the same four-entry picker the
    /// generator's class step uses.
    /// </summary>
    private EventStep OpenClassChange()
    {
        ClassChoices = ClassChangesFor?.Invoke() ?? [];
        ClassIndex = 0;

        if (ClassChoices.Count == 0)
        {
            // The entry should have been dark. Reaching it anyway says so rather than showing an
            // empty list the player cannot leave.
            ClassChoices = null;
            Unimplemented = "[CHANGE CLASS here -- no class qualifies]";
            return EventStep.Running;
        }

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, CreationMenu);
        escapeSelects = CreationExit;
        ShowText("CHOOSE NEW CLASS");

        return EventStep.Running;
    }

    /// <summary>The page of classes showing.</summary>
    public IReadOnlyList<string> ClassPageOffers =>
        [.. (ClassChoices ?? []).Skip(ClassPage * PageSize).Take(PageSize)];

    private int ClassPage;

    private int ClassPageCount =>
        Math.Max(1, ((ClassChoices?.Count ?? 0) + PageSize - 1) / PageSize);

    private EventStep ChooseClassChange()
    {
        if (ClassChoices is null)
        {
            return EventStep.Running;
        }

        switch (Menu.ActiveItem)
        {
            case CreationSelect:
            {
                var page = ClassPageOffers;
                if (ClassIndex < 0 || ClassIndex >= page.Count)
                {
                    return EventStep.Running;
                }

                ApplyClassChange?.Invoke(page[ClassIndex]);
                break;
            }

            case CreationNext:
                ClassPage = (ClassPage + 1) % ClassPageCount;
                ClassIndex = 0;
                return EventStep.Running;

            case CreationPrev:
                ClassPage = (ClassPage + ClassPageCount - 1) % ClassPageCount;
                ClassIndex = 0;
                return EventStep.Running;
        }

        // SELECT and EXIT both leave. The reference replaces the screen with the spell picker on
        // SELECT -- knowSpellsAtCreation is hard-coded TRUE -- which is not wired here, so both
        // drop back to the party menu.
        ClassChoices = null;
        ClassPage = 0;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Vertical, PartyMenu);
        UpdatePartyMenu();
        escapeSelects = PartyExit;
        return EventStep.Running;
    }

    private bool HandleClassChangeKey(VirtualKey key)
    {
        int onPage = ClassPageOffers.Count;
        if (onPage <= 0)
        {
            return false;
        }

        switch (key)
        {
            case VirtualKey.Up:
                ClassIndex = ((ClassIndex - 1) % onPage + onPage) % onPage;
                return true;

            case VirtualKey.Down:
                ClassIndex = (ClassIndex + 1) % onPage;
                return true;

            default:
                return false;
        }
    }

    // ---- the stats screen ----------------------------------------------------------------------

    /// <summary>The stats screen's state, or null when it is not up.</summary>
    public StatsScreen? ChoosingStats { get; private set; }

    /// <summary>
    /// Opens the stats screen over the character being made. Set by the host, which owns the
    /// design's limits and the hit-point seed.
    /// </summary>
    public Func<CharacterCreation, StatsScreen?>? StatsForCreation { get; set; }

    /// <summary>Opens it over the active party member, for MODIFY.</summary>
    public Func<StatsScreen?>? StatsForActiveMember { get; set; }

    /// <summary>
    /// Puts the screen up. Whether it came from the generator or from MODIFY only changes what
    /// happens when it closes.
    /// </summary>
    private EventStep ShowStats(StatsScreen? screen)
    {
        if (screen is null)
        {
            Unimplemented = "[MODIFY CHARACTER here -- not implemented]";
            return EventStep.Running;
        }

        ChoosingStats = screen;
        Typing = null;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, StatsScreen.Menu);
        escapeSelects = StatsScreen.Accept;

        // The title is the one thing AllowModifyStats changes, and it is always true.
        ShowText("<TAB><UP/DOWN> MODIFY CHARACTER STATS");

        return EventStep.Running;
    }

    /// <summary>
    /// Return on the stats screen: keep this character, or roll it again.
    /// </summary>
    /// <remarks>
    /// <b>The generator does not stop here.</b> Accepting moves the wizard on to the name; MODIFY
    /// has no wizard behind it and drops back to the party menu.
    /// </remarks>
    private EventStep ChooseStats()
    {
        if (ChoosingStats is not { } screen)
        {
            return EventStep.Running;
        }

        if (Menu.ActiveItem != StatsScreen.Accept)
        {
            screen.Reroll();
            return EventStep.Running;
        }

        screen.Accepted();
        ChoosingStats = null;

        if (Creating is not null)
        {
            Creating.SkipStats();
            return ShowCreationStep();
        }

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Vertical, PartyMenu);
        UpdatePartyMenu();
        escapeSelects = PartyExit;
        return EventStep.Running;
    }

    /// <summary>
    /// The keys the stats screen takes for itself, before the menu sees them.
    /// </summary>
    /// <remarks>
    /// <b>TAB moves the highlight here and not the party.</b> <c>CHOOSESTATS_MENU_DATA</c> is one
    /// of the few screens whose <c>OnKeypress</c> does not open with <c>TABParty(key)</c>, so the
    /// key means something different on this screen than on every other one.
    /// </remarks>
    private bool HandleStatsKey(VirtualKey key)
    {
        if (ChoosingStats is not { } screen)
        {
            return false;
        }

        switch (key)
        {
            case VirtualKey.Tab:
                screen.Tab();
                return true;

            // Up and down are swallowed whether or not they moved anything -- the reference sets
            // its "handled" bit before consulting the result.
            //
            // KC_PLUS and KC_MINUS are VK_ADD and VK_SUBTRACT (Getinput.cpp:566), the numeric
            // keypad's keys -- not the OEM ones on the number row, which the mapper leaves as
            // KC_NUM. Reading the names rather than the mapping puts the shortcut on a key that
            // does nothing.
            case VirtualKey.Up:
            case VirtualKey.Add:
                screen.Raise();
                return true;

            case VirtualKey.Down:
            case VirtualKey.Subtract:
                screen.Lower();
                return true;

            default:
                return false;
        }
    }

    // ---- the art screens -----------------------------------------------------------------------

    /// <summary>The pictures on offer, or null when no art screen is up.</summary>
    public IReadOnlyList<string>? ArtChoices { get; private set; }

    /// <summary>Which picture is showing.</summary>
    public int ArtIndex { get; private set; }

    /// <summary>Lists the art for a step; set by the host, which knows the design's folders.</summary>
    public Func<CreationStep, IReadOnlyList<string>>? ArtFor { get; set; }

    /// <summary>
    /// <c>GETCHARICON_MENU_DATA</c> and <c>GETCHARSMALLPIC_MENU_DATA</c> — one screen, two
    /// directories (<c>RunEvent.cpp:3362</c>).
    /// </summary>
    private EventStep ShowArtScreen()
    {
        if (Creating is null)
        {
            return EventStep.Running;
        }

        ArtChoices = ArtFor?.Invoke(Creating.Step) ?? [];
        ArtIndex = 0;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, ArtPicker.Menu);

        // One picture darkens both paging entries; SELECT never darkens, so a design with no art
        // still asks the player to press it over an empty screen.
        bool canStep = ArtPicker.CanStep(ArtChoices.Count);
        Menu.SetItemEnabled(ArtPicker.Next, canStep);
        Menu.SetItemEnabled(ArtPicker.Previous, canStep);

        // NEXT is the first entry and the cursor starts there, so darkening it would leave the
        // cursor on an entry that does nothing. With one picture SELECT is all there is.
        if (!canStep)
        {
            Menu.SetCurrentItem(ArtPicker.Select);
        }

        escapeSelects = ArtPicker.Select;
        ShowText(Creating.Step is CreationStep.Icon ? "CHOOSE AN ICON" : "CHOOSE A PORTRAIT");

        return EventStep.Running;
    }

    private EventStep ChooseArt()
    {
        if (Creating is null || ArtChoices is null)
        {
            return EventStep.Running;
        }

        switch (Menu.ActiveItem)
        {
            case ArtPicker.Next:
                ArtIndex = ArtPicker.Step(ArtIndex, ArtChoices.Count, +1);
                return EventStep.Running;

            case ArtPicker.Previous:
                ArtIndex = ArtPicker.Step(ArtIndex, ArtChoices.Count, -1);
                return EventStep.Running;

            default:
            {
                string? picked = ArtIndex >= 0 && ArtIndex < ArtChoices.Count
                    ? ArtChoices[ArtIndex]
                    : null;

                ArtChoices = null;
                Creating.Pick(picked);
                return ShowCreationStep();
            }
        }
    }

    // ---- the spell screens ---------------------------------------------------------------------

    /// <summary>The spells on offer at the level showing, or null when no screen is up.</summary>
    public IReadOnlyList<AvailableSpell>? SpellChoices { get; private set; }

    /// <summary>Which spell level the screen is on.</summary>
    public int SpellLevel { get; private set; }

    /// <summary>Which sweep the acquisition loop is on — 0 fills to Max, 1+ tops up to Min.</summary>
    public int SpellPass { get; private set; }

    /// <summary>The last acquisition message, in the reference's own wording.</summary>
    public string? SpellMessage { get; private set; }

    /// <summary>Supplies the spells and the per-level counts; set by the host.</summary>
    public Func<CharacterCreation, SpellScreenData?>? SpellScreenFor { get; set; }

    /// <summary>Rolls d100 for an acquisition attempt; set by the host.</summary>
    public Func<int, int>? RollPercent { get; set; }

    private SpellScreenData? spellScreen;

    /// <summary>
    /// <c>INITIAL_MU_SPELLS_MENU_DATA</c> and <c>LEARN_SPELLS_MENU</c>
    /// (<c>RunEvent.cpp:23299</c>, <c>:24557</c>).
    /// </summary>
    /// <remarks>
    /// <b>Neither screen has an EXIT.</b> Both menus are three entries — <c>SELECT/NEXT/PREV</c>
    /// and <c>LEARN/NEXT/PREV</c> — so there is no way out but to keep picking until the
    /// acquisition rules say every level is finished. The two differ in the verb and the message
    /// and in nothing else, which is why one screen serves both.
    /// </remarks>
    private EventStep ShowSpellScreen()
    {
        if (Creating is null)
        {
            return EventStep.Running;
        }

        spellScreen = SpellScreenFor?.Invoke(Creating);
        if (spellScreen is null || spellScreen.Levels.Count <= 1)
        {
            // No spells for this character at all; the screen never appears.
            Creating.LearnedSpells();
            return ShowCreationStep();
        }

        SpellPass = 0;
        SpellLevel = 1;
        return FillSpellMenu();
    }

    private EventStep FillSpellMenu()
    {
        if (spellScreen is null)
        {
            return EventStep.Running;
        }

        SpellChoices = spellScreen.Offered(SpellLevel);

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal,
                       (spellScreen.Verb, 0), ("NEXT", 0), ("PREV", 0));

        // No EXIT to map Escape onto.
        escapeSelects = null;
        SpellIndex = 0;

        ShowText(SpellMessage ?? $"SPELL LEVEL {SpellLevel}");
        return EventStep.Running;
    }

    /// <summary>Which row of the spell list the cursor is on.</summary>
    public int SpellIndex { get; private set; }

    private EventStep ChooseSpell()
    {
        if (Creating is null || spellScreen is null || SpellChoices is null)
        {
            return EventStep.Running;
        }

        switch (Menu.ActiveItem)
        {
            case 1:
                SpellLevel = spellScreen.NextLevel(SpellLevel, +1);
                return FillSpellMenu();

            case 2:
                SpellLevel = spellScreen.NextLevel(SpellLevel, -1);
                return FillSpellMenu();

            default:
            {
                if (SpellIndex < 0 || SpellIndex >= SpellChoices.Count)
                {
                    return EventStep.Running;
                }

                var chosen = SpellChoices[SpellIndex];
                var state = spellScreen.State(SpellLevel);

                bool got = UAF.Rules.SpellAcquisition.Acquires(
                    state, chosen.Probability, RollPercent ?? (_ => 100));

                state.Record(got);
                spellScreen.Taken(SpellLevel, chosen, got);

                SpellMessage = got
                    ? $"{Creating.CharacterName} successfully acquired {chosen.Spell.Name}."
                    : $"{Creating.CharacterName} failed to acquire {chosen.Spell.Name}.";

                return AdvanceSpellLoop();
            }
        }
    }

    /// <summary>
    /// Walks the acquisition loop on until a level with something left, or the end.
    /// </summary>
    /// <remarks>
    /// <b>The sweep is a round robin.</b> A later pass leaves the level showing as soon as its
    /// turn comes even when it is the short one, so the loop wraps back to level 1 and increments
    /// the pass rather than sitting still — see §learning spells at creation.
    /// </remarks>
    private EventStep AdvanceSpellLoop()
    {
        if (Creating is null || spellScreen is null)
        {
            return EventStep.Running;
        }

        for (int guard = 0; guard < spellScreen.Levels.Count * 4; guard++)
        {
            var progress = UAF.Rules.SpellAcquisition.Progress(
                spellScreen.Levels, SpellLevel, SpellPass);

            if (progress.HasFlag(UAF.Rules.AcquireProgress.AllLevels))
            {
                SpellChoices = null;
                spellScreen = null;
                Creating.LearnedSpells();
                return ShowCreationStep();
            }

            if (!progress.HasFlag(UAF.Rules.AcquireProgress.ThisLevel))
            {
                return FillSpellMenu();
            }

            SpellLevel++;
            if (SpellLevel >= spellScreen.Levels.Count)
            {
                SpellLevel = 1;
                SpellPass++;
            }
        }

        // The loop cannot make progress; close rather than spin.
        SpellChoices = null;
        spellScreen = null;
        Creating.LearnedSpells();
        return ShowCreationStep();
    }

    /// <summary>Which confirmation the yes/no screen is asking.</summary>
    public enum PartyConfirm
    {
        None = 0,

        /// <summary>Drop the active character from the party.</summary>
        Remove,

        /// <summary>Drop them and delete their saved file.</summary>
        Delete,
    }

    /// <summary>What the yes/no screen on top is asking, or <see cref="PartyConfirm.None"/>.</summary>
    public PartyConfirm Confirming { get; private set; }

    /// <summary>Answers a confirmation; set by the host, which owns the party and the files.</summary>
    public Action<PartyConfirm>? ApplyPartyConfirm { get; set; }

    /// <summary>The active character's name, for the question; set by the host.</summary>
    public Func<string>? ActiveCharacterName { get; set; }

    /// <summary>
    /// Puts a yes/no question over the party menu (<c>ASK_YES_NO_MENU_DATA</c>).
    /// </summary>
    /// <remarks>
    /// <b>The answer comes back through the trade slot.</b> The reference reads
    /// <c>party.tradeItem == 1</c> to mean yes (<c>RunEvent.cpp:2408</c>) — the item-trading
    /// register doubles as the yes/no answer, which is why <c>tradeItem</c> is in the saved
    /// <c>PARTY</c> record at all. Here the question is state on the runner instead.
    /// </remarks>
    private (string Label, int Shortcut)[]? confirmParent;

    /// <param name="parent">
    /// The menu to rebuild on answering, or null for the party menu. ALTER's DROP asks the same
    /// question the party menu's REMOVE does and has to come back to a different screen.
    /// </param>
    private EventStep AskAbout(PartyConfirm what, string question,
                               (string Label, int Shortcut)[]? parent = null)
    {
        Confirming = what;
        confirmParent = parent;

        Menu.Reset();
        SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, ("YES", 0), ("NO", 0));

        escapeSelects = 1;                       // NO
        Menu.SetCurrentItem(1);                  // ...and it starts there
        ShowText(string.Format(question, ActiveCharacterName?.Invoke() ?? "THIS CHARACTER"));

        return EventStep.Running;
    }

    private EventStep AnswerConfirm()
    {
        if (Menu.ActiveItem == 0)
        {
            ApplyPartyConfirm?.Invoke(Confirming);
        }

        Confirming = PartyConfirm.None;

        var parent = confirmParent;
        confirmParent = null;

        Menu.Reset();
        if (parent is null)
        {
            SetupFixedMenu(lastAnchors, null, MenuOrientation.Vertical, PartyMenu);
            UpdatePartyMenu();
            escapeSelects = PartyExit;
        }
        else
        {
            SetupFixedMenu(lastAnchors, null, MenuOrientation.Horizontal, parent);
            escapeSelects = parent.Length - 1;
            UpdateAlterMenu();
        }

        return EventStep.Running;
    }

    /// <summary>
    /// <c>MAIN_MENU_DATA::OnKeypress</c> (<c>RunEvent.cpp:1968</c>).
    /// </summary>
    /// <remarks>
    /// Three of the twelve run. The other nine are character creation, the save and load screens,
    /// and the class change — each a screen of its own rather than a command.
    /// </remarks>
    private EventStep ChoosePartyMenu()
    {
        switch (Menu.ActiveItem)
        {
            case PartyView when ActiveCharacterSheet?.Invoke() is { } sheet && font is not null:
                Stats = new CharStatsForm();
                Stats.Populate(font, sheet);
                return EventStep.Running;

            case PartySave:
                return OpenSlots(saving: true);

            case PartyLoad:
                return OpenSlots(saving: false);

            case PartyAdd:
                return OpenRoster();

            case PartyCreate:
                return BeginCreation();

            // MODIFY is CHOOSESTATS over the active member and nothing else -- no wizard, no
            // questions. Accepting it writes the scores back; re-rolling generates the character
            // again from its own race, gender and class.
            case PartyModify:
                return ShowStats(StatsForActiveMember?.Invoke());

            case PartyChangeClass:
                return OpenClassChange();

            case PartyRemove:
                return AskAbout(PartyConfirm.Remove, "REMOVE {0} FROM PARTY?");

            case PartyDelete:
                return AskAbout(PartyConfirm.Delete,
                                "{0} WILL BE PERMANENTLY REMOVED. CONTINUE?");

            case PartyTrain when partyMenuHall is { } hall:
            {
                LastTraining = ApplyTraining?.Invoke(hall);
                if (LastTraining is { Trained: true, Announcements.Count: > 0 })
                {
                    ShowText(string.Join("  ", LastTraining.Announcements));
                }
                UpdatePartyMenu();
                return EventStep.Running;
            }

            // Both leave. BEGIN pops back to whatever pushed the menu -- and the hall's
            // OnReturnToTopOfQueue immediately backs the party up and chains, so popping back to
            // it and finishing it are the same thing from here. EXIT is the game's own quit,
            // which reached from a hall amounts to the same.
            case PartyBegin:
            case PartyExit:
            {
                bool backup = partyMenuHall?.ForceExit != 0;
                PartyMenuOpen = false;
                partyMenuHall = null;

                BackupRequested = backup;
                return Complete(happened: true);
            }

            default:
                Unimplemented =
                    $"[{PartyMenu[Math.Clamp(Menu.ActiveItem, 0, PartyMenu.Length - 1)].Label}" +
                    " here -- not implemented]";
                return EventStep.Running;
        }
    }

    /// <summary>
    /// <c>SMALL_TOWN_DATA::OnInitialEvent</c> (<c>RunEvent.cpp:10709</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hub the other town services hang off, and the cheapest of them: a horizontal menu of
    /// six destinations and an exit, with the event's own text above it. Nothing about the party
    /// changes here — every entry is a chain.
    /// </para>
    /// <para>
    /// <b>The menu is <i>horizontal</i></b> (<c>menu.setHorzOrient()</c>), which no other event
    /// this runner presents is. The shortcut indices are the table's own and are not first
    /// letters: <c>TRAINING HALL</c> uses index 9, the <c>H</c> of "HALL", because <c>T</c> is
    /// already <c>TEMPLE</c>'s.
    /// </para>
    /// <para>
    /// <b>Escape maps to EXIT</b> (<c>MapKeyCodeToMenuItem(KC_ESCAPE, 7)</c>) rather than
    /// cancelling the event, so a player who backs out of a town still runs its chain.
    /// </para>
    /// </remarks>
    private EventStep BeginSmallTown(SmallTownEvent town, MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, null, MenuOrientation.Horizontal,
                       ("TEMPLE", 0), ("TRAINING HALL", 9), ("SHOP", 0), ("INN", 0),
                       ("PUB", 0), ("VAULT", 0), ("EXIT", 1));

        escapeSelects = SmallTownExit;
        ShowText(town.Base.Text);
        return EventStep.Running;
    }

    /// <summary>The six destinations, in menu order (<c>SmallTownMenu</c>, <c>GameMenu.cpp:211</c>).</summary>
    private static uint DestinationOf(SmallTownEvent town, int item) => item switch
    {
        0 => town.TempleChain,
        1 => town.TrainingHallChain,
        2 => town.ShopChain,
        3 => town.InnChain,
        4 => town.TavernChain,           // the menu says PUB
        5 => town.VaultChain,
        _ => 0,
    };

    /// <summary>
    /// <c>SMALL_TOWN_DATA::OnKeypress</c> (<c>RunEvent.cpp:10721</c>).
    /// </summary>
    /// <remarks>
    /// <b>A destination that names no event does not fall back on the town's own chain.</b> The
    /// reference pushes a <c>DO_NOTHING_EVENT</c>, which returns to the town screen — so choosing
    /// SHOP in a town with no shop leaves the player exactly where they were. Only EXIT chains.
    /// </remarks>
    private EventStep ChooseSmallTown(SmallTownEvent town)
    {
        int chosen = Menu.ActiveItem;

        if (chosen == SmallTownExit)
        {
            return Complete(happened: true);
        }

        uint destination = DestinationOf(town, chosen);
        if (destination == 0 || IsValidEvent?.Invoke(destination) == false)
        {
            // DO_NOTHING_EVENT: the screen stays up.
            return EventStep.Running;
        }

        Current = null;
        return EventStep.To(destination);
    }

    /// <summary>The EXIT item's index in the small-town menu.</summary>
    public const int SmallTownExit = 6;

    /// <summary>
    /// Whether an id names an event this level holds; set by the host, which owns the level.
    /// </summary>
    /// <remarks>
    /// Without it every destination is taken at face value, which is the right default for a
    /// caller that has already checked.
    /// </remarks>
    public Func<uint, bool>? IsValidEvent { get; set; }

    /// <summary>
    /// Runs a logic block; set by the host, which owns the state its terminals read and write.
    /// </summary>
    public Func<LogicBlockEvent, LogicBlockOutcome>? ResolveLogicBlock { get; set; }

    /// <summary>
    /// <c>LOGIC_BLOCK_DATA</c> (<c>ProcessLogicBlock</c>, <c>RunEvent.cpp:14360</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The only event type that draws nothing at all.</b> It has no text, no menu and no
    /// keypress — it evaluates, writes what its actions write, and decides where the run goes
    /// next. So it finishes inside <see cref="Begin"/> and never reaches
    /// <see cref="Handle"/>, which is the same shape as a question whose options are all empty.
    /// </para>
    /// <para>
    /// <b>Its chaining is not the ordinary chain.</b> Under
    /// <see cref="LogicBlockChaining.OnResult"/> the block replaces itself with its own true or
    /// false target and <c>chainEventHappen</c> is never consulted; under
    /// <see cref="LogicBlockChaining.Never"/> the run simply ends. Only
    /// <see cref="LogicBlockChaining.Always"/> defers to <see cref="Complete"/>.
    /// </para>
    /// </remarks>
    private EventStep BeginLogicBlock(LogicBlockEvent logic)
    {
        if (ResolveLogicBlock is null)
        {
            return BeginUnsupported(logic);
        }

        var outcome = ResolveLogicBlock(logic);
        LastLogicBlock = outcome;

        if (outcome.ChainsNormally)
        {
            return Complete(happened: true);
        }

        Current = null;
        return outcome.ChainTo is uint id ? EventStep.To(id) : EventStep.Finished;
    }

    /// <summary>
    /// What the last logic block produced, for a design that asked to record its terminals
    /// (<c>LBF_RECORD_VALUES</c>) and for tests.
    /// </summary>
    public LogicBlockOutcome? LastLogicBlock { get; private set; }

    /// <summary>Seats an NPC; set by the host. See <see cref="EventNpc"/>.</summary>
    public Action<AddNpcEvent>? ApplyAddNpc { get; set; }

    /// <summary>Drops an NPC; set by the host. See <see cref="EventNpc"/>.</summary>
    public Action<RemoveNpcEvent>? ApplyRemoveNpc { get; set; }

    /// <summary>Confiscates goods; set by the host. See <see cref="EventTakeItems"/>.</summary>
    public Action<TakePartyItemsEvent>? ApplyTakeItems { get; set; }

    /// <summary>Adds a journal entry; set by the host. See <see cref="EventJournal"/>.</summary>
    public Action<JournalEvent>? ApplyJournal { get; set; }

    /// <summary>Plays a sound event's queue; set by the host. See <see cref="EventSound"/>.</summary>
    public Action<SoundEvent>? ApplySound { get; set; }

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

    /// <summary>
    /// Cycles the active party member; set by the host.
    /// </summary>
    /// <remarks>
    /// <b>There is no character-selection screen in this format.</b> <c>GameEvent::TABParty</c>
    /// (<c>RunEvent.cpp:792</c>) is the first line of <i>every</i> event's <c>OnKeypress</c>: TAB
    /// advances <c>party.activeCharacter</c>, wrapping at the end, and the event then reads
    /// whoever is active through <c>GetActiveChar</c>. So "who tries" and "who pays" are answered
    /// by the same roster the player has been looking at all along.
    /// </remarks>
    public Action? TabParty { get; set; }

    /// <summary>Resolves a who-tries attempt for the active character; set by the host.</summary>
    public Func<WhoTriesEvent, WhoTriesOutcome>? ResolveWhoTries { get; set; }

    /// <summary>Resolves a toll for the active character; set by the host.</summary>
    /// <remarks>Takes the chosen menu entry, one-based, as <c>menu.currentItem()</c> reports it.</remarks>
    public Func<WhoPaysEvent, int, WhoPaysOutcome>? ResolveWhoPays { get; set; }

    /// <summary>
    /// <c>WHO_PAYS_EVENT_DATA::OnInitialEvent</c> (<c>RunEvent.cpp:10252</c>) — pay or leave.
    /// </summary>
    /// <remarks>
    /// Entry 1 is the only one that pays; the reference's <c>else</c> treats everything else as
    /// EXIT, which runs the failure action <i>without</i> the failure text.
    /// </remarks>
    private EventStep BeginWhoPays(WhoPaysEvent toll, MenuAnchors anchors)
    {
        SetupFixedMenu(anchors, title: null, MenuOrientation.Horizontal, ("YES", 0), ("NO", 0));
        ShowText(toll.Base.Text);
        return EventStep.Running;
    }

    /// <summary>Takes an outcome that may chain, may stop, or may do neither.</summary>
    private EventStep Branch(uint? goTo, bool stop)
    {
        if (goTo is uint target)
        {
            Current = null;
            return EventStep.To(target);
        }

        if (stop)
        {
            Current = null;
            return EventStep.Finished;
        }

        return Complete(happened: true);
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

    /// <summary>Resolves a carried item's id to its database record; set by the host.</summary>
    /// <remarks>
    /// A carried item holds only an id and its own state — how many hands it needs and where it
    /// is worn live in the design's record, so the ready rules cannot be applied without this.
    /// </remarks>
    public Func<string, ItemRecord?>? ItemDatabase { get; set; }

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

    /// <summary>
    /// How many rows a paged list shows at once (<c>Items_Per_Page</c>, <c>Globals.cpp:141</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is design configuration, not a constant.</b> The reference reads
    /// <c>ITEMS_PER_PAGE</c> out of <c>config.txt</c> and falls back to 14
    /// (<c>Globals.cpp:2778</c>). This was a hardcoded 8 for as long as there was a treasure
    /// screen, which is neither the default nor anything a design asked for — every paged list in
    /// the port was six rows short, and the comment beside it named the very constant it was not
    /// using.
    /// </para>
    /// <para>
    /// No shipped design in the corpus sets the token, so in practice this is always 14; the
    /// plumbing exists because the reference's does and because the value reaching the form is
    /// what makes it visible when it is wrong.
    /// </para>
    /// </remarks>
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>What <c>ITEMS_PER_PAGE</c> falls back to when a design does not set it.</summary>
    public const int DefaultPageSize = 14;

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

        Items = new ItemsForm(PageSize);
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

        // ...every OnKeypress but this one. CHOOSESTATS_MENU_DATA (RunEvent.cpp:4049) goes
        // straight to its own handler, so TAB moves the ability highlight and never reaches the
        // party. It has to be tested before the line below, not after it.
        if (ChoosingStats is not null && input.Kind == InputEventKind.KeyDown
            && HandleStatsKey(input.Key))
        {
            return EventStep.Running;
        }

        // The class picker splits its keys the generator's way: the list takes up and down, the
        // SELECT/NEXT/PREV/EXIT menu takes left and right.
        if (ClassChoices is not null && input.Kind == InputEventKind.KeyDown
            && HandleClassChangeKey(input.Key))
        {
            return EventStep.Running;
        }

        // ALTER/ORDER's whole screen is the arrow keys: its only menu entry is EXIT.
        if (OrderingParty && input.Kind == InputEventKind.KeyDown
            && HandleOrderKey(input.Key))
        {
            return EventStep.Running;
        }

        // The rest screen's up and down move the duration; left and right stay with the menu.
        if (RestOpen && input.Kind == InputEventKind.KeyDown && HandleRestKey(input.Key))
        {
            return EventStep.Running;
        }

        // The memorise list takes up and down; the menu keeps left and right.
        if (Memorizing is not null && input.Kind == InputEventKind.KeyDown
            && HandleMemorizeKey(input.Key))
        {
            return EventStep.Running;
        }

        // TABParty is the FIRST line of every OnKeypress (RunEvent.cpp:792) and returns before the
        // menu ever sees the key, so TAB can never also move a selection.
        if (input.Kind == InputEventKind.KeyDown && input.Key == VirtualKey.Tab)
        {
            TabParty?.Invoke();
            return EventStep.Running;
        }

        // menu.MapKeyCodeToMenuItem: a key that selects an item and commits it in one press,
        // rather than moving the selection. Only the town screens use it, and only for Escape.
        if (escapeSelects is int escapeItem
            && input.Kind == InputEventKind.KeyDown && input.Key == VirtualKey.Escape)
        {
            Menu.SetCurrentItem(escapeItem);
            return Commit();
        }

        // The inventory takes the vertical keys before the menu sees them; the horizontal ones
        // fall through, which is the whole shape of HMenuVInventoryKeyboardAction.
        if (InventoryOpen && input.Kind == InputEventKind.KeyDown && HandleInventoryKey(input.Key))
        {
            return EventStep.Running;
        }

        // So does the shop's shelf, which is the same widget with the columns swapped.
        if (BuyOpen && input.Kind == InputEventKind.KeyDown && HandleShopKey(input.Key))
        {
            return EventStep.Running;
        }

        // The target picker sits over the cast list and moves the party cursor with the same two
        // keys the list uses for its rows, so it has to answer first.
        if (Aiming is not null && input.Kind == InputEventKind.KeyDown && HandleAimKey(input.Key))
        {
            return EventStep.Running;
        }

        // And the cast list, which splits them the same way again.
        if (CastOpen && input.Kind == InputEventKind.KeyDown && HandleCastKey(input.Key))
        {
            return EventStep.Running;
        }

        // The GIVE screen takes digits and backspace; Return falls through and commits.
        if (Giving is not null && HandleGiveKey(input))
        {
            return EventStep.Running;
        }

        // The temple's cast list takes up and down; the menu keeps left and right.
        if (TempleCasting is not null && input.Kind == InputEventKind.KeyDown
            && HandleTempleCastKey(input.Key))
        {
            return EventStep.Running;
        }

        // A screen asking for typed text takes every key but Return.
        if (HandleTyping(input))
        {
            return EventStep.Running;
        }

        // The character generator's pickers split the keys the inventory's way round: the list
        // takes up and down, the SELECT/NEXT/PREV/EXIT menu takes left and right.
        if (Creating is not null && input.Kind == InputEventKind.KeyDown
            && HandleCreationKey(input.Key))
        {
            return EventStep.Running;
        }

        // The party menu is the mirror image: VMenuHPartyKeyboardAction gives the menu the
        // vertical keys and the party the horizontal ones (RunEvent.cpp:1973).
        // Every screen the party menu pushes over itself has to be excluded here, because the menu
        // stays "open" underneath them. The stats screen is the one that arrives without a
        // generator behind it, so Creating being null does not cover it.
        if (PartyMenuOpen && !SlotsOpen && !RosterOpen && Creating is null && ChoosingStats is null
            && ClassChoices is null && Confirming is PartyConfirm.None
            && input.Kind == InputEventKind.KeyDown
            && input.Key is VirtualKey.Left or VirtualKey.Right)
        {
            TabParty?.Invoke();
            UpdatePartyMenu();
            return EventStep.Running;
        }

        // Anything that is not a commit goes to the menu, exactly as every OnKeypress does.
        var result = MenuInput.Handle(Menu, input);

        // The rest form re-syncs to the menu after every keypress, so moving the cursor onto
        // DAYS, HOURS or MINS activates that field without a separate press.
        if (RestOpen)
        {
            SyncRestField();
        }
        bool committed = result == MenuInputResult.Accepted
                         || (input.Kind == InputEventKind.KeyDown
                             && input.Key == VirtualKey.Return);

        return committed ? Commit() : EventStep.Running;
    }

    /// <summary>Set by an event whose Escape selects a menu item instead of doing nothing.</summary>
    private int? escapeSelects;

    /// <summary>Acts on the selected menu item, once something has committed it.</summary>
    private EventStep Commit()
    {
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

        // The shop's offer sits OVER the inventory, so it answers before the inventory does -- the
        // same way ALTER's DROP answers before the party menu. This has to come first: the
        // inventory is still open behind the question, so its own dispatch would otherwise take
        // the keypress and the offer would never be answered.
        if (pendingSale is not null)
        {
            return AnswerSale();
        }

        // And so does the taker picker, for the same reason.
        if (pendingTrade is not null)
        {
            return AnswerTrade();
        }

        // The inventory replaces the current event's menu, so it answers before the event does.
        if (InventoryOpen)
        {
            return ChooseInventory();
        }

        // The slot screen sits over the party menu, so it answers first. So do the roster and
        // the two confirmations, which are the party menu's other pushed screens.
        if (SlotsOpen)
        {
            return ChooseSlots();
        }

        if (RosterOpen)
        {
            return ChooseRoster();
        }

        if (ArtChoices is not null)
        {
            return ChooseArt();
        }

        if (SpellChoices is not null)
        {
            return ChooseSpell();
        }

        // Before the generator, because the generator pushed it -- and because MODIFY puts it up
        // with no generator behind it at all.
        if (ChoosingStats is not null)
        {
            return ChooseStats();
        }

        if (ClassChoices is not null)
        {
            return ChooseClassChange();
        }

        if (JournalText is not null)
        {
            return ChooseJournal();
        }

        if (OrderingParty)
        {
            return CloseOrder();
        }

        if (RestOpen)
        {
            return ChooseRest();
        }

        if (Memorizing is not null)
        {
            return ChooseMemorize();
        }

        // The picker sits over the cast list, so it answers before it.
        if (Aiming is not null)
        {
            return ChooseAiming();
        }

        // The cast list sits over both the magic hub and the temple's, so it answers before either.
        if (CastOpen)
        {
            return ChooseCast();
        }

        if (MagicOpen)
        {
            return ChooseMagic();
        }

        if (Creating is not null)
        {
            return ChooseCreation();
        }

        if (Confirming is not PartyConfirm.None)
        {
            return AnswerConfirm();
        }

        // After the confirmation, which ALTER's DROP pushes over it -- a pushed screen answers
        // before the one it is sitting on.
        if (AlterOpen)
        {
            return ChooseAlter();
        }

        // So does the party menu, which the training hall pushes over itself.
        if (PartyMenuOpen)
        {
            return ChoosePartyMenu();
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
            WhoTriesEvent trial => FinishWhoTries(trial),
            JournalEvent journal => Applied(() => ApplyJournal?.Invoke(journal)),
            TakePartyItemsEvent take => Applied(() => ApplyTakeItems?.Invoke(take)),
            AddNpcEvent add => Applied(() => ApplyAddNpc?.Invoke(add)),
            RemoveNpcEvent remove => Applied(() => ApplyRemoveNpc?.Invoke(remove)),
            SoundEvent sound => Applied(() => ApplySound?.Invoke(sound)),
            WhoPaysEvent toll => FinishWhoPays(toll),
            PasswordEvent password => AnswerPassword(password),
            SmallTownEvent town => ChooseSmallTown(town),
            CampEvent camp => ChooseCamp(camp),
            TrainingHallEvent hall => ChooseTrainingHall(hall),
            TavernEvent tavern => ChooseTavern(tavern),
            ShopEvent shop => ChooseShop(shop),
            VaultEvent vault => ChooseTownItem(VaultMenu, vault.ForceBackup),
            TempleEvent temple => ChooseTemple(temple),
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

    /// <summary>Resolves the attempt against whoever is active.</summary>
    /// <remarks>
    /// <b>Neither of these ends a run.</b> Both go through <c>ChainOrQuit</c>
    /// (<c>RunEvent.cpp:931</c>), which falls back on the ordinary chain for a chain id of zero
    /// <i>and</i> for one naming a missing event — unlike <c>QUEST_EVENT_DATA</c>, which stops.
    /// A who-tries whose action is out of range does nothing at all, which is the reference's
    /// defaultless switch and leaves the player on a screen that will not advance.
    /// </remarks>
    private EventStep FinishWhoTries(WhoTriesEvent trial)
    {
        if (ResolveWhoTries?.Invoke(trial) is not { } outcome)
        {
            return Complete(happened: true);
        }

        return outcome.Chains || outcome.GoTo is not null
            ? Branch(outcome.GoTo, stop: false)
            : EventStep.Running;                         // the stuck case, reproduced
    }

    /// <inheritdoc cref="FinishWhoTries"/>
    private EventStep FinishWhoPays(WhoPaysEvent toll)
    {
        if (ResolveWhoPays?.Invoke(toll, Menu.ActiveItem + 1) is not { } outcome)
        {
            return Complete(happened: true);
        }

        return outcome.Stuck ? EventStep.Running : Branch(outcome.GoTo, stop: false);
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
