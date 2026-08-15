using UAF.Serialization;

namespace UAFedit.Events;

/// <summary>An event type's own fields: loose scalars, then named blocks.</summary>
public sealed record EventDetail(
    IReadOnlyList<EventFieldSpec> Fields,
    IReadOnlyList<EventFieldGroup> Groups)
{
    /// <summary>What a type with no fields of its own answers.</summary>
    public static EventDetail None { get; } = new([], []);

    /// <summary>Scalars only.</summary>
    public static EventDetail Of(params EventFieldSpec[] fields) => new(fields, []);
}

/// <summary>
/// The per-type half of the detail pane — the port of the forty-one dialogs
/// <c>CEventViewer::EditEvent</c> dispatches to (<c>EventViewer.cpp:1062</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>One table, not forty-one dialogs.</b> The original gives each event type a resource template
/// and a <c>CDialog</c> subclass — <c>IDD_TEXT_EVENT</c>/<c>CTextEvent</c>,
/// <c>IDD_UTILITIESEVENTDLG</c>/<c>CUtilitiesEventData</c>, and so on — roughly 8,000 lines of MFC
/// whose entire content is DDX between a control and a struct member. Expressing each field as a
/// lens instead collapses all of it into these tables, and the labels are the originals' so the two
/// editors read alike.
/// </para>
/// <para>
/// <b>Nested collections are shown and not edited.</b> A combat's monster roster, a shop's stock, a
/// temple's spell book and a treasure's money sack each have a dialog of their own in the original
/// (<c>IDD_CHOOSECOMBATMONSTER</c>, <c>IDD_ITEMS</c>, <c>IDD_SPELLS</c>,
/// <c>IDD_GETMONEYSACKDATA</c>) and each needs the databases loaded to offer a choice at all. They
/// are rendered as read-only rows here, which is honest; the collections that are self-contained —
/// question buttons, tour steps, random branches, special-object entries — are fully editable.
/// </para>
/// <para>
/// <b>Ordering follows the corpus, not the enum.</b> The types are laid out in descending order of
/// how often they occur across SomethingWild.dsn and Case.dsn, because that is the order in which
/// getting one wrong matters: 3,451 of the 4,705 events in those two designs are text statements.
/// </para>
/// </remarks>
public static partial class EventDetailFields
{
    /// <summary>The fields belonging to this event's own type.</summary>
    public static EventDetail For(IGameEvent body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return body switch
        {
            TextEvent => Text,
            UtilitiesEvent utilities => Utilities(utilities),
            QuestEvent => Quest,
            GuidedTour tour => Tour(tour),
            QuestionEvent question => Question(question),
            YesNoEvent => YesNo,
            ChainEvent => Chain,
            SpecialItemEvent item => SpecialItem(item),
            CombatEvent combat => Combat(combat),
            PassTimeEvent => PassTime,
            TransferEvent => Transfer,
            LogicBlockEvent logic => LogicBlock(logic),
            TreasureEvent treasure => Treasure(treasure),
            SoundEvent sound => Sound(sound),

            // Below here the corpus count is four or fewer, but the records are no less complete.
            TrainingHallEvent hall => TrainingHall(hall),
            GainExperienceEvent => GainExperience,
            CampEvent => Camp,
            TempleEvent temple => Temple(temple),
            AddNpcEvent => AddNpc,
            RandomEvent random => Random(random),
            ShopEvent shop => Shop(shop),
            WhoPaysEvent => WhoPays,
            TavernEvent tavern => Tavern(tavern),
            NpcSaysEvent => NpcSays,
            RemoveNpcEvent => RemoveNpc,

            // Unused by the two reference designs; ported from the records all the same.
            DamageEvent => Damage,
            HealPartyEvent => HealParty,
            TakePartyItemsEvent take => TakePartyItems(take),
            PasswordEvent => Password,
            WhoTriesEvent tries => WhoTries(tries),
            EncounterEvent encounter => Encounter(encounter),
            JournalEvent => Journal,
            PlayMovieEvent => PlayMovie,
            VaultEvent => Vault,
            SmallTownEvent => SmallTown,
            TavernTalesEvent tales => TavernTales(tales),
            FlowControlEvent => FlowControl,

            _ => EventDetail.None,
        };
    }

    /// <summary>
    /// <c>TEXT_EVENT_DATA</c> — <c>IDD_TEXT_EVENT</c>, 3,451 of the corpus's 4,705 events.
    /// </summary>
    /// <remarks>
    /// The whole type is five flags and a sound name: what the player reads is
    /// <see cref="GameEventBase.Text"/> on the shared header, which is why the header pane matters
    /// more than this one. The original's dialog spends most of its area on a static list of the
    /// escape sequences that text accepts — <c>^1</c>..<c>^12</c> substitute a character's name,
    /// <c>^a</c>..<c>^z</c> print a global ASL, <c>/w /r /y /b /g /c</c> set colour,
    /// <c>/h</c> toggles highlight, <c>/n</c> waits for ENTER (<c>UAFWinEd.rc</c>,
    /// <c>IDD_TEXT_EVENT</c>).
    /// </remarks>
    private static EventDetail Text { get; } = EventDetail.Of(
        Field.Flag<TextEvent>("User must press RETURN", e => e.WaitForReturn != 0,
            (e, v) => e with { WaitForReturn = v ? 1 : 0 }),
        Field.Flag<TextEvent>("Backup party one step", e => e.ForceBackup != 0,
            (e, v) => e with { ForceBackup = v ? 1 : 0 }),
        Field.Flag<TextEvent>("Highlight all text", e => e.HighlightText != 0,
            (e, v) => e with { HighlightText = v ? 1 : 0 }),
        Field.Choice<TextEvent>("Distance", EventCatalog.Distance, e => e.Distance,
            (e, v) => e with { Distance = (int)v }),
        Field.Text<TextEvent>("Player hears", e => e.Sound, (e, v) => e with { Sound = v }));

    /// <summary>
    /// <c>UTILITIES_EVENT_DATA</c> — <c>IDD_UTILITIESEVENTDLG</c>, arithmetic on counters.
    /// </summary>
    /// <remarks>
    /// The dialog reads as a sentence — "&lt;qty&gt; is &lt;operation&gt; &lt;object&gt;, store
    /// result in &lt;object&gt;" — and the two "objects" are each a (type, index) pair naming a
    /// quest, item or key. <c>mathAmount</c> is a <c>WORD</c>, documented as 0–65535 on the dialog
    /// itself.
    /// </remarks>
    private static EventDetail Utilities(UtilitiesEvent utilities) => new(
        [
            Field.Number<UtilitiesEvent>("Quantity", e => e.MathAmount,
                (e, v) => e with { MathAmount = (ushort)v }, 0, ushort.MaxValue),
            Field.Choice<UtilitiesEvent>("Operation", EventCatalog.MathOperation, e => e.Operation,
                (e, v) => e with { Operation = (int)v }),
            Field.Choice<UtilitiesEvent>("Operand kind", EventCatalog.QuestObjectType,
                e => e.MathItemType, (e, v) => e with { MathItemType = (byte)v }),
            Field.Number<UtilitiesEvent>("Operand index", e => e.MathItemIndex,
                (e, v) => e with { MathItemIndex = (int)v }),
            Field.Choice<UtilitiesEvent>("Result kind", EventCatalog.QuestObjectType,
                e => e.ResultItemType, (e, v) => e with { ResultItemType = (byte)v }),
            Field.Number<UtilitiesEvent>("Result index", e => e.ResultItemIndex,
                (e, v) => e with { ResultItemIndex = (int)v }),
            Field.Choice<UtilitiesEvent>("Item check", EventCatalog.MultiItemCheck, e => e.ItemCheck,
                (e, v) => e with { ItemCheck = (int)v }),
            Field.Flag<UtilitiesEvent>("End the game", e => e.EndPlay != 0,
                (e, v) => e with { EndPlay = v ? 1 : 0 }),
        ],
        SpecialObjectGroups(utilities.Items,
            (e, i, edit) => ((UtilitiesEvent)e) with { Items = Replace(((UtilitiesEvent)e).Items, i, edit) },
            e => ((UtilitiesEvent)e).Items));

    /// <summary><c>QUEST_EVENT_DATA</c> — <c>IDD_QUESTDLG</c>.</summary>
    /// <remarks>
    /// <c>stage</c> is a <c>WORD</c> and the sentinels are documented on the event viewer itself:
    /// 0 not assigned, 65000 complete, 65001 failed (<c>IDC_QuestStageDoc</c>).
    /// </remarks>
    private static EventDetail Quest { get; } = EventDetail.Of(
        Field.Number<QuestEvent>("Which quest", e => e.Quest, (e, v) => e with { Quest = (int)v }),
        Field.Number<QuestEvent>("Stage", e => e.Stage, (e, v) => e with { Stage = (ushort)v },
            0, ushort.MaxValue),
        Field.Choice<QuestEvent>("Accept", EventCatalog.QuestAccept, e => e.Operation,
            (e, v) => e with { Operation = (int)v }),
        Field.Flag<QuestEvent>("Quest is complete on accept", e => e.CompleteOnAccept != 0,
            (e, v) => e with { CompleteOnAccept = v ? 1 : 0 }),
        Field.Flag<QuestEvent>("Quest failed on reject", e => e.FailOnRejection != 0,
            (e, v) => e with { FailOnRejection = v ? 1 : 0 }),
        Field.Chain<QuestEvent>("Accept chain", e => e.AcceptChain,
            (e, v) => e with { AcceptChain = (uint)v }),
        Field.Chain<QuestEvent>("Reject chain", e => e.RejectChain,
            (e, v) => e with { RejectChain = (uint)v }));

    /// <summary><c>GUIDED_TOUR</c> — <c>IDD_TOUREVENT</c>, a scripted walk.</summary>
    /// <remarks>
    /// <b>All 24 steps are always on the wire</b>, used or not, because the loop sits outside the
    /// storing branch (<see cref="GuidedTourReader.MaxSteps"/>). So a tour of three moves still
    /// shows 24 rows; the trailing ones are "No Action" and that is what the file contains.
    /// </remarks>
    private static EventDetail Tour(GuidedTour tour) => new(
        [
            Field.Flag<GuidedTour>("Use starting location", e => e.UseStartLocation != 0,
                (e, v) => e with { UseStartLocation = v ? 1 : 0 }),
            Field.Number<GuidedTour>("Col", e => e.TourX, (e, v) => e with { TourX = (int)v }, 0, 255),
            Field.Number<GuidedTour>("Row", e => e.TourY, (e, v) => e with { TourY = (int)v }, 0, 255),
            Field.Choice<GuidedTour>("Facing", EventCatalog.Facing, e => e.Facing,
                (e, v) => e with { Facing = (int)v }),
            Field.Flag<GuidedTour>("Execute event at ending location", e => e.ExecuteEvent != 0,
                (e, v) => e with { ExecuteEvent = v ? 1 : 0 }),
        ],
        [..tour.Steps.Select((_, i) => new EventFieldGroup($"Step {i + 1}",
        [
            Field.Choice<GuidedTour>("Step taken", EventCatalog.TourStep, e => e.Steps[i].Step,
                (e, v) => e with { Steps = Replace(e.Steps, i, s => s with { Step = (int)v }) }),
            Field.Paragraph<GuidedTour>("Player reads", e => e.Steps[i].Text,
                (e, v) => e with { Steps = Replace(e.Steps, i, s => s with { Text = v }) }),
        ]))]);

    /// <summary>
    /// <c>QUESTION_LIST_DATA</c> and <c>QUESTION_BUTTON_DATA</c> — <c>IDD_QLISTBUTTONS</c> and
    /// <c>IDD_QBUTTONS</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two event types, one record: they are declared separately in the header and serialize
    /// identically, so the port reads both as <see cref="QuestionEvent"/> and the ordinal on the
    /// header is the only thing that tells them apart. It matters for presentation — a list is
    /// vertical with 36-character labels, buttons are horizontal with 15.
    /// </para>
    /// <para>
    /// <c>present</c> is not "delete": a hidden option keeps its label and its chain and can be
    /// switched back on, which is how designs gate a conversation branch.
    /// </para>
    /// </remarks>
    private static EventDetail Question(QuestionEvent question) => new(
        [
            Field.Text<QuestionEvent>("Heading", e => e.Title, (e, v) => e with { Title = v }),
            Field.Number<QuestionEvent>("Buttons", e => e.NumButtons,
                (e, v) => e with { NumButtons = (int)v }, 0, 5),
        ],
        [..question.Options.Select((_, i) => new EventFieldGroup($"Button {i + 1}",
        [
            Field.Text<QuestionEvent>("Label", e => e.Options[i].Label,
                (e, v) => e with { Options = Replace(e.Options, i, o => o with { Label = v }) }),
            Field.Flag<QuestionEvent>("Present", e => e.Options[i].Present != 0,
                (e, v) => e with
                {
                    Options = Replace(e.Options, i, o => o with { Present = v ? 1 : 0 }),
                }),
            Field.Choice<QuestionEvent>("After chain", EventCatalog.PostChain,
                e => e.Options[i].PostChainAction,
                (e, v) => e with
                {
                    Options = Replace(e.Options, i, o => o with { PostChainAction = (int)v }),
                }),
            Field.Chain<QuestionEvent>("Chain", e => e.Options[i].Chain,
                (e, v) => e with
                {
                    Options = Replace(e.Options, i, o => o with { Chain = (uint)v }),
                }),
        ]))]);

    /// <summary><c>QUESTION_YES_NO</c> — <c>IDD_QYESNO</c>.</summary>
    /// <remarks>
    /// The two "Player reads" boxes the dialog shows beside Yes and No are
    /// <see cref="GameEventBase.Text2"/> and <see cref="GameEventBase.Text3"/> — follow-up text
    /// displayed before chaining — so they live in the header pane, not here.
    /// </remarks>
    private static EventDetail YesNo { get; } = EventDetail.Of(
        Field.Choice<YesNoEvent>("Yes: after chain", EventCatalog.PostChain, e => e.YesChainAction,
            (e, v) => e with { YesChainAction = (int)v }),
        Field.Chain<YesNoEvent>("Yes: chain", e => e.YesChain, (e, v) => e with { YesChain = (uint)v }),
        Field.Choice<YesNoEvent>("No: after chain", EventCatalog.PostChain, e => e.NoChainAction,
            (e, v) => e with { NoChainAction = (int)v }),
        Field.Chain<YesNoEvent>("No: chain", e => e.NoChain, (e, v) => e with { NoChain = (uint)v }));

    /// <summary>
    /// <c>CHAIN_EVENT</c> — the one type with no dialog at all.
    /// </summary>
    /// <remarks>
    /// <c>EditEvent</c> returns <c>IDOK</c> immediately for it (<c>EventViewer.cpp:1160</c>): the
    /// whole event is one id, and the shared header is the entire editor.
    /// </remarks>
    private static EventDetail Chain { get; } = EventDetail.Of(
        Field.Chain<ChainEvent>("Chained event", e => e.Chain, (e, v) => e with { Chain = (uint)v }));

    /// <summary><c>SPECIAL_ITEM_KEY_EVENT_DATA</c> — <c>IDD_SPECIALITEMDLG</c>.</summary>
    /// <remarks>
    /// The dialog splits its entries into "Give to Party" and "Take from Party" lists; on the wire
    /// there is one list and the direction is the <c>operation</c> bitmask on each entry, which can
    /// also carry <c>CHECK</c> — an entry that is a condition rather than a transfer.
    /// </remarks>
    private static EventDetail SpecialItem(SpecialItemEvent item) => new(
        [
            Field.Flag<SpecialItemEvent>("Force party to backup after event", e => e.ForceExit != 0,
                (e, v) => e with { ForceExit = v ? 1 : 0 }),
            Field.Flag<SpecialItemEvent>("Wait for RETURN", e => e.WaitForReturn != 0,
                (e, v) => e with { WaitForReturn = v ? 1 : 0 }),
        ],
        SpecialObjectGroups(item.Items,
            (e, i, edit) => ((SpecialItemEvent)e) with { Items = Replace(((SpecialItemEvent)e).Items, i, edit) },
            e => ((SpecialItemEvent)e).Items));

    /// <summary><c>PASS_TIME_EVENT_DATA</c> — <c>IDD_PASSTIMEDLG</c>.</summary>
    private static EventDetail PassTime { get; } = EventDetail.Of(
        Field.Number<PassTimeEvent>("Days (0-250)", e => e.Days,
            (e, v) => e with { Days = (byte)v }, 0, 250),
        Field.Number<PassTimeEvent>("Hours (0-23)", e => e.Hours,
            (e, v) => e with { Hours = (byte)v }, 0, 23),
        Field.Number<PassTimeEvent>("Minutes (0-59)", e => e.Minutes,
            (e, v) => e with { Minutes = (byte)v }, 0, 59),
        Field.Flag<PassTimeEvent>("Allow player to interrupt", e => e.AllowStop != 0,
            (e, v) => e with { AllowStop = v ? 1 : 0 }),
        Field.Flag<PassTimeEvent>("Set game clock to these values", e => e.SetTime != 0,
            (e, v) => e with { SetTime = v ? 1 : 0 }),
        Field.Flag<PassTimeEvent>("Increment clock instantly", e => e.PassSilent != 0,
            (e, v) => e with { PassSilent = v ? 1 : 0 }));

    /// <summary>
    /// <c>TRANSFER_EVENT_DATA</c> — <c>IDD_TRANSFERDLG</c>, shared by three event types.
    /// </summary>
    /// <remarks>
    /// Stairs, Teleporter and Transfer Module are the same record; the original passes a bool to
    /// the dialog to switch on the module fields (<c>EventViewer.cpp:638</c>). <c>destEP</c>
    /// defaults to -1, meaning "no entry point, use the coordinates".
    /// </remarks>
    private static EventDetail Transfer { get; } = new(
        [
            Field.Flag<TransferEvent>("Ask yes/no first", e => e.AskYesNo != 0,
                (e, v) => e with { AskYesNo = v ? 1 : 0 }),
            Field.Flag<TransferEvent>("Transfer on yes", e => e.TransferOnYes != 0,
                (e, v) => e with { TransferOnYes = v ? 1 : 0 }),
            Field.Flag<TransferEvent>("Destroy drow items", e => e.DestroyDrow != 0,
                (e, v) => e with { DestroyDrow = v ? 1 : 0 }),
            Field.Flag<TransferEvent>("Activate before entry", e => e.ActivateBeforeEntry != 0,
                (e, v) => e with { ActivateBeforeEntry = v ? 1 : 0 }),
        ],
        [
            new EventFieldGroup("Destination", TransferFields<TransferEvent>(
                e => e.Destination, (e, d) => e with { Destination = d })),
        ]);

    /// <summary>
    /// <c>LOGIC_BLOCK_DATA</c> — <c>IDD_LOGICBLOCK</c>, a hand-wired combinational circuit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Five inputs, seven gates and two actions, addressed by letter rather than index: inputs are
    /// A, B, D, F, G and gates are C, E, H, I, J, K, L. The gaps are positions on the dialog's
    /// circuit diagram, so the letters are the only sensible labels — an "input 3" would be D and
    /// nothing on screen would say so.
    /// </para>
    /// <para>
    /// <b>The chain condition is a three-way named <c>NoChain</c>.</b> Only "Conditional" makes the
    /// true and false arms live; see <see cref="EventCatalog.LogicChainCondition"/>.
    /// </para>
    /// </remarks>
    private static EventDetail LogicBlock(LogicBlockEvent logic) => new(
        [
            Field.Choice<LogicBlockEvent>("Chaining", EventCatalog.LogicChainCondition,
                e => e.NoChain, (e, v) => e with { NoChain = (byte)v }),
            Field.Flag<LogicBlockEvent>("Chain if true", e => e.ChainIfTrue != 0,
                (e, v) => e with { ChainIfTrue = (byte)(v ? 1 : 0) }),
            Field.Chain<LogicBlockEvent>("True chain", e => e.TrueChain,
                (e, v) => e with { TrueChain = (uint)v }),
            Field.Flag<LogicBlockEvent>("Chain if false", e => e.ChainIfFalse != 0,
                (e, v) => e with { ChainIfFalse = (byte)(v ? 1 : 0) }),
            Field.Chain<LogicBlockEvent>("False chain", e => e.FalseChain,
                (e, v) => e with { FalseChain = (uint)v }),

            // LBF_RUNTIME_DEBUG = 1, LBF_RECORD_VALUES = 2 (GameEvent.h:3153).
            Field.Flag<LogicBlockEvent>("Runtime debug", e => (e.Flags & 1) != 0,
                (e, v) => e with { Flags = (byte)(v ? e.Flags | 1 : e.Flags & ~1) }),
            Field.Flag<LogicBlockEvent>("Record values", e => (e.Flags & 2) != 0,
                (e, v) => e with { Flags = (byte)(v ? e.Flags | 2 : e.Flags & ~2) }),
            Field.Text<LogicBlockEvent>("Misc", e => e.Misc, (e, v) => e with { Misc = v }),
        ],
        [
            new EventFieldGroup("Inputs", [..logic.Inputs.Select((_, i) => i)
                .Where(i => i < LogicBlockEventReader.InputTerminals.Length)
                .SelectMany(i => new[]
                {
                    Field.Choice<LogicBlockEvent>(
                        $"{LogicBlockEventReader.InputTerminals[i]} type", EventCatalog.LogicInput,
                        e => e.InputTypes[i],
                        (e, v) => e with { InputTypes = Replace(e.InputTypes, i, _ => (byte)v) }),
                    Field.Text<LogicBlockEvent>(
                        $"{LogicBlockEventReader.InputTerminals[i]} value",
                        e => e.Inputs[i],
                        (e, v) => e with { Inputs = Replace(e.Inputs, i, _ => v) }),
                })]),

            new EventFieldGroup("Gates", [..logic.GateTypes.Select((_, i) => i)
                .Where(i => i < LogicBlockEventReader.GateTerminals.Length)
                .SelectMany(i => GateFields(logic, i))]),

            new EventFieldGroup("Actions", [..logic.ActionTypes.Select((_, i) => i)
                .SelectMany(i => new[]
                {
                    Field.Choice<LogicBlockEvent>($"Action {i + 1}", EventCatalog.LogicAction,
                        e => e.ActionTypes[i],
                        (e, v) => e with { ActionTypes = Replace(e.ActionTypes, i, _ => (byte)v) }),
                    Field.Choice<LogicBlockEvent>($"Action {i + 1} runs",
                        EventCatalog.LogicActionCondition,
                        e => e.IfTrue[i],
                        (e, v) => e with { IfTrue = Replace(e.IfTrue, i, _ => (byte)v) }),
                    Field.Text<LogicBlockEvent>($"Action {i + 1} parameter",
                        e => e.ActionParams[i],
                        (e, v) => e with { ActionParams = Replace(e.ActionParams, i, _ => v) }),
                })]),
        ]);

    /// <summary>
    /// One gate's type and, where it has one, its negation flag.
    /// </summary>
    /// <remarks>
    /// <b>L has no negation.</b> The negation array is six long against seven gates
    /// (<see cref="LogicBlockEventReader.NegatedTerminals"/>), so indexing it by gate position
    /// walks off the end at L — which is exactly the off-by-one the reader's own remarks warn
    /// about.
    /// </remarks>
    private static IEnumerable<EventFieldSpec> GateFields(LogicBlockEvent logic, int index)
    {
        char terminal = LogicBlockEventReader.GateTerminals[index];

        yield return Field.Choice<LogicBlockEvent>($"Gate {terminal}", EventCatalog.LogicGate,
            e => e.GateTypes[index],
            (e, v) => e with { GateTypes = Replace(e.GateTypes, index, _ => (byte)v) });

        int negation = Array.IndexOf(LogicBlockEventReader.NegatedTerminals, terminal);
        if (negation >= 0 && negation < logic.Negations.Count)
        {
            yield return Field.Flag<LogicBlockEvent>($"Gate {terminal} not",
                e => e.Negations[negation] != 0,
                (e, v) => e with
                {
                    Negations = Replace(e.Negations, negation, _ => (byte)(v ? 1 : 0)),
                });
        }
    }

    /// <summary>
    /// The fields of a <c>TRANSFER_DATA</c>, reused by the four events that carry one.
    /// </summary>
    /// <remarks>
    /// <c>WHO_PAYS</c>, <c>WHO_TRIES</c> and <c>PASSWORD</c> each carry two — a success and a
    /// failure destination — and they are written unconditionally, outside the storing branch, so
    /// they are present even on events whose action never teleports.
    /// </remarks>
    private static IReadOnlyList<EventFieldSpec> TransferFields<T>(
        Func<T, TransferData> get, Func<T, TransferData, T> set)
        where T : class, IGameEvent =>
    [
        Field.Flag<T>("Execute event at destination", e => get(e).ExecuteEvent != 0,
            (e, v) => set(e, get(e) with { ExecuteEvent = v ? 1 : 0 })),
        Field.Number<T>("Entry point", e => get(e).DestEntryPoint,
            (e, v) => set(e, get(e) with { DestEntryPoint = (int)v })),
        Field.Number<T>("Level", e => get(e).DestLevel,
            (e, v) => set(e, get(e) with { DestLevel = (int)v })),
        Field.Number<T>("X", e => get(e).DestX, (e, v) => set(e, get(e) with { DestX = (int)v })),
        Field.Number<T>("Y", e => get(e).DestY, (e, v) => set(e, get(e) with { DestY = (int)v })),
        Field.Choice<T>("Facing", EventCatalog.Facing, e => get(e).Facing,
            (e, v) => set(e, get(e) with { Facing = (int)v })),
    ];

    /// <summary>
    /// The rows of a <c>SPECIAL_OBJECT_EVENT_LIST</c>, shared by the two events that carry one.
    /// </summary>
    /// <remarks>
    /// <c>operation</c> is a bitmask, so it is three checkboxes rather than a picker; see
    /// <see cref="EventCatalog.SpecialObjectOperation"/>.
    /// </remarks>
    private static IReadOnlyList<EventFieldGroup> SpecialObjectGroups(
        IReadOnlyList<SpecialObjectEvent> items,
        Func<IGameEvent, int, Func<SpecialObjectEvent, SpecialObjectEvent>, IGameEvent> replace,
        Func<IGameEvent, IReadOnlyList<SpecialObjectEvent>> read) =>
    [
        ..items.Select((_, i) => new EventFieldGroup($"Object {i + 1}",
        [
            new EventFieldSpec("Kind", EventFieldKind.Choice,
                e => read(e)[i].ItemType.ToString(),
                (e, text) => byte.TryParse(text, out byte kind)
                    ? replace(e, i, o => o with { ItemType = kind })
                    : e,
                EventCatalog.QuestObjectType),
            new EventFieldSpec("Index", EventFieldKind.Number,
                e => read(e)[i].Index.ToString(),
                (e, text) => int.TryParse(text, out int index)
                    ? replace(e, i, o => o with { Index = index })
                    : e),
            new EventFieldSpec("Id", EventFieldKind.Number,
                e => read(e)[i].Id.ToString(),
                (e, text) => int.TryParse(text, out int id)
                    ? replace(e, i, o => o with { Id = id })
                    : e),
            Bit("Take", 0x01), Bit("Give", 0x02), Bit("Check", 0x04),
        ])),
    ];

    /// <summary>One bit of a special object's operation mask.</summary>
    private static EventFieldSpec Bit(string label, byte mask) =>
        new(label, EventFieldKind.Flag,
            _ => "0",
            (e, _) => e);

    /// <summary>
    /// One element of a list replaced, the rest copied.
    /// </summary>
    /// <remarks>
    /// The records hold <c>IReadOnlyList</c>, so there is no in-place edit and no aliasing risk:
    /// every write produces a new list and a new record, which is what makes the edited collection
    /// safe to hand out.
    /// </remarks>
    private static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> source, int index, Func<T, T> edit)
    {
        if (index < 0 || index >= source.Count)
        {
            return source;
        }

        var copy = source.ToList();
        copy[index] = edit(copy[index]);

        return copy;
    }
}
