using UAF.Serialization;
using UAFcore;

namespace UAFedit.Events;

/// <summary>
/// The editor for <see cref="GameEventBase"/> and its <see cref="EventControl"/> — the part every
/// one of the 42 readable event types has.
/// </summary>
/// <remarks>
/// <para>
/// <b>In the original this is not a dialog.</b> The right-hand column of <c>IDD_EVENTVIEWER</c>
/// (<c>UAFWinEd.rc:2170</c>) <i>is</i> the header editor, applying to whichever tree node is
/// selected, and the per-type dialogs open on top of it. So the header is genuinely the main
/// surface of the event editor and the type-specific pane is the secondary one — which is why it
/// gets the whole file.
/// </para>
/// <para>
/// <b>Half these fields are irrelevant at any given moment.</b> <c>SetControlStates</c>
/// (<c>EventViewer.cpp:2967</c>) hides everything and then reveals only what the selected
/// <c>eventTrigger</c> reads: Item for <c>PartyHaveItem</c>, Quest for the four quest conditions,
/// Party X/Y for <c>PartyAtXY</c>, and so on. <see cref="RelevantTo"/> is that mapping extracted as
/// a pure predicate. The original also <i>clears</i> the irrelevant fields as a side effect of
/// redrawing, which silently discards a designer's earlier setting; that half is deliberately not
/// reproduced.
/// </para>
/// </remarks>
public static class EventHeaderFields
{
    /// <summary>The identity and placement block — mostly not editable.</summary>
    public static IReadOnlyList<EventFieldSpec> Identity { get; } =
    [
        Field.Info("Type", body => EventCatalog.Name(EventRecords.TypeOf(body))),

        // The id is the chain currency. Changing it would orphan every event pointing here, and
        // the original never offers it either: ids come from GameEventList's allocator.
        Field.Info("Id", body => body.Base.Id.ToString()),

        // Cells hold no event index -- an AreaMapCell carries only an EventExists flag and the
        // coordinates live here (EventLookup). So moving an event is exactly writing these two,
        // with the caveat that the source cell's flag is the map editor's to clear.
        Field.Number<IGameEvent>("Map X", e => e.Base.X,
            (e, v) => WithHeader(e, h => h with { X = (int)v }), 0, 255),
        Field.Number<IGameEvent>("Map Y", e => e.Base.Y,
            (e, v) => WithHeader(e, h => h with { Y = (int)v }), 0, 255),

        Field.Info("Picture", body => Picture(body.Base.Pic)),
        Field.Info("Picture 2", body => Picture(body.Base.Pic2)),
    ];

    /// <summary>When the event fires, and what happens afterwards.</summary>
    public static IReadOnlyList<EventFieldSpec> Control { get; } =
    [
        Field.Choice<IGameEvent>("Event Trigger", EventCatalog.EventTrigger,
            e => e.Base.Control.EventTrigger,
            (e, v) => WithControl(e, c => c with { EventTrigger = (int)v })),

        Field.Flag<IGameEvent>("Once Only", e => e.Base.Control.OnceOnly != 0,
            (e, v) => WithControl(e, c => c with { OnceOnly = v ? 1 : 0 })),

        Field.Choice<IGameEvent>("Chain Trigger", EventCatalog.ChainTrigger,
            e => e.Base.Control.ChainTrigger,
            (e, v) => WithControl(e, c => c with { ChainTrigger = (int)v })),

        Field.Chain<IGameEvent>("Chain on happen", e => e.Base.ChainEventHappen,
            (e, v) => WithHeader(e, h => h with { ChainEventHappen = (int)v })),
        Field.Chain<IGameEvent>("Chain on not happen", e => e.Base.ChainEventNotHappen,
            (e, v) => WithHeader(e, h => h with { ChainEventNotHappen = (int)v })),
    ];

    /// <summary>
    /// The trigger's operands: the fields only some <c>eventTrigger</c> values read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>Quest Stage</c> and <c>Party X</c> are the same stored field.</b> The original's DDX
    /// map exchanges <c>m_questStage</c> and <c>m_PartyX</c> with each other in both directions
    /// (<c>EventViewer.cpp:906</c> and <c>:954</c>), so <c>control.partyX</c> holds an x coordinate
    /// under <see cref="EventTriggerType.PartyAtXy"/> and a quest stage number under
    /// <see cref="EventTriggerType.QuestStageEqual"/>. There is one field and two meanings; showing
    /// it once under both names is the only honest rendering, and the documented stage values are
    /// 0 = not assigned, 65000 = complete, 65001 = failed (<c>IDC_QuestStageDoc</c>).
    /// </para>
    /// <para>
    /// <b>Gender, Special Item and Special Key are not on the record at all.</b> They are moved
    /// into <c>eventcontrol_asl</c> under the keys <c>Gen</c>, <c>SpIt</c> and <c>SpKy</c> before
    /// writing and pulled back after reading (<c>PreSerialize</c>, <c>GameEvent.cpp:1318</c>), so
    /// they appear in the attribute list below rather than as fields here. Editing them means
    /// editing an ASL entry, which is why the attribute pane is not an afterthought.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<EventFieldSpec> Trigger { get; } =
    [
        Field.Number<IGameEvent>("Chance %", e => e.Base.Control.Chance,
            (e, v) => WithControl(e, c => c with { Chance = (int)v }), 0, 100),

        Field.Choice<IGameEvent>("Facing", EventCatalog.Direction, e => e.Base.Control.Facing,
            (e, v) => WithControl(e, c => c with { Facing = (int)v })),

        Field.Text<IGameEvent>("Item", e => e.Base.Control.ItemId,
            (e, v) => WithControl(e, c => c with { ItemId = v })),

        Field.Number<IGameEvent>("Quest", e => e.Base.Control.Quest,
            (e, v) => WithControl(e, c => c with { Quest = (int)v })),

        Field.Number<IGameEvent>("Party X / Quest Stage", e => e.Base.Control.PartyX,
            (e, v) => WithControl(e, c => c with { PartyX = (int)v }), 0, 65001),
        Field.Number<IGameEvent>("Party Y", e => e.Base.Control.PartyY,
            (e, v) => WithControl(e, c => c with { PartyY = (int)v }), 0, 65535),

        Field.Text<IGameEvent>("Race", e => e.Base.Control.RaceId,
            (e, v) => WithControl(e, c => c with { RaceId = v })),

        // One string, two destinations: the trigger decides whether it is read as a classID or a
        // baseclassID, and the original retitles the label to match (EventViewer.cpp:1916).
        Field.Text<IGameEvent>("Class / Baseclass", e => e.Base.Control.ClassOrBaseclassId,
            (e, v) => WithControl(e, c => c with { ClassOrBaseclassId = v })),

        Field.Text<IGameEvent>("NPC", e => e.Base.Control.CharacterId,
            (e, v) => WithControl(e, c => c with { CharacterId = v })),

        Field.Text<IGameEvent>("Memorized spell", e => e.Base.Control.MemorizedSpellId,
            (e, v) => WithControl(e, c => c with { MemorizedSpellId = v })),
        Field.Number<IGameEvent>("Spell class", e => e.Base.Control.MemorizedSpellClass,
            (e, v) => WithControl(e, c => c with { MemorizedSpellClass = (uint)v }), 0, uint.MaxValue),
        Field.Number<IGameEvent>("Spell level", e => e.Base.Control.MemorizedSpellLevel,
            (e, v) => WithControl(e, c => c with { MemorizedSpellLevel = (uint)v }), 0, uint.MaxValue),

        Field.Paragraph<IGameEvent>("GPDL trigger", e => e.Base.Control.GpdlData,
            (e, v) => WithControl(e, c => c with { GpdlData = v })),
    ];

    /// <summary>
    /// The three display strings.
    /// </summary>
    /// <remarks>
    /// <b>This is where an event's text lives, not on its subclass.</b> Even
    /// <c>TEXT_EVENT_DATA</c>, whose entire purpose is showing a paragraph, keeps it here — its own
    /// record is five flags and a sound. So these three are the most-edited fields in the whole
    /// editor: Case.dsn has 3,146 text statements and every one of them is a string in
    /// <see cref="GameEventBase.Text"/>.
    /// </remarks>
    public static IReadOnlyList<EventFieldSpec> Texts { get; } =
    [
        Field.Paragraph<IGameEvent>("Text", e => e.Base.Text,
            (e, v) => WithHeader(e, h => h with { Text = v })),
        Field.Paragraph<IGameEvent>("Text 2", e => e.Base.Text2,
            (e, v) => WithHeader(e, h => h with { Text2 = v })),
        Field.Paragraph<IGameEvent>("Text 3", e => e.Base.Text3,
            (e, v) => WithHeader(e, h => h with { Text3 = v })),
    ];

    /// <summary>
    /// Which trigger operands the selected <c>eventTrigger</c> actually reads.
    /// </summary>
    /// <remarks>
    /// Extracted from <c>SetControlStates</c> (<c>EventViewer.cpp:2967</c>) as a predicate over the
    /// label, so the pane can grey out the rest without the original's habit of clearing them.
    /// Anything not listed — the two spell fields under a non-spell trigger, for instance — is
    /// still shown, just marked irrelevant.
    /// </remarks>
    public static bool RelevantTo(EventTriggerType trigger, string label) => label switch
    {
        "Chance %" => trigger == EventTriggerType.RandomChance,

        "Facing" => trigger is EventTriggerType.FacingDirection
                             or EventTriggerType.FacingDirectionAnyTime,

        "Item" => trigger is EventTriggerType.PartyHaveItem or EventTriggerType.PartyNotHaveItem,

        "Quest" => trigger is EventTriggerType.QuestComplete or EventTriggerType.QuestFailed
                            or EventTriggerType.QuestInProgress or EventTriggerType.QuestPresent
                            or EventTriggerType.QuestNotPresent
                            or EventTriggerType.QuestStageEqual
                            or EventTriggerType.QuestStageNotEqual,

        // The one field with two owners -- see the remarks on Trigger.
        "Party X / Quest Stage" => trigger is EventTriggerType.PartyAtXy
                                            or EventTriggerType.QuestStageEqual
                                            or EventTriggerType.QuestStageNotEqual,

        "Party Y" => trigger == EventTriggerType.PartyAtXy,

        "Race" => trigger is EventTriggerType.RaceInParty or EventTriggerType.RaceNotInParty,

        "Class / Baseclass" => trigger is EventTriggerType.ClassInParty
                                        or EventTriggerType.ClassNotInParty
                                        or EventTriggerType.BaseclassInParty
                                        or EventTriggerType.BaseclassNotInParty,

        "NPC" => trigger is EventTriggerType.NpcInParty or EventTriggerType.NpcNotInParty,

        "Memorized spell" or "Spell class" or "Spell level" =>
            trigger == EventTriggerType.SpellMemorized,

        "GPDL trigger" => trigger == EventTriggerType.ExecuteGpdl,

        _ => true,
    };

    /// <summary>
    /// A picture slot rendered for display.
    /// </summary>
    /// <remarks>
    /// Two <c>PIC_DATA</c> sit between the control block and the event's own fields on every event
    /// (<c>GameEventReader</c>). Choosing art is a browser dialog of its own in the original
    /// (<c>IDD_PICSELECTDLG</c>), so this reports what is there and leaves picking to that.
    /// </remarks>
    private static string Picture(PicRecord? pic) =>
        pic is null || string.IsNullOrWhiteSpace(pic.FileName) ? "none" : pic.FileName;

    private static IGameEvent WithHeader(IGameEvent body, Func<GameEventBase, GameEventBase> edit) =>
        EventRecords.WithBase(body, edit(body.Base));

    private static IGameEvent WithControl(IGameEvent body, Func<EventControl, EventControl> edit) =>
        EventRecords.WithBase(body, body.Base with { Control = edit(body.Base.Control) });
}
