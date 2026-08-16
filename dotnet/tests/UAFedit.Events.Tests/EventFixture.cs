using UAF.Common;
using UAF.Serialization;

namespace UAFedit.Events.Tests;

/// <summary>
/// Hand-built events and levels, for the behaviour that must hold with or without a corpus.
/// </summary>
/// <remarks>
/// The reference designs are gitignored, so every corpus test can silently do nothing. Editing,
/// dirtiness and the chain graph are all testable without one, and they are the parts most likely
/// to break, so they are tested here on data this file owns.
/// </remarks>
public static class EventFixture
{
    /// <summary>An empty <c>PIC_DATA</c> — every event carries two.</summary>
    public static PicRecord NoPicture { get; } =
        new(0, string.Empty, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>A control block with nothing set: trigger Always, chain trigger Always.</summary>
    public static EventControl Control(int eventTrigger = 0, int chainTrigger = 0) =>
        new(0, 0, 0, chainTrigger, eventTrigger,
            string.Empty, -1, 100, 0, string.Empty, string.Empty, string.Empty,
            [], string.Empty, 0, 0, 0, string.Empty, 0, 0);

    /// <summary>A shared header for an event of a given type and id.</summary>
    public static GameEventBase Header(EventType type, uint id, string text = "",
                                       int chainHappen = 0, int chainNotHappen = 0,
                                       int chainTrigger = 0, int eventTrigger = 0,
                                       int x = 0, int y = 0) =>
        new(Control(eventTrigger, chainTrigger), NoPicture, NoPicture,
            (int)type, id, x, y, chainHappen, chainNotHappen, text, string.Empty, string.Empty, []);

    /// <summary>A text statement, the type that is 73% of the reference corpus.</summary>
    public static TextEvent Text(uint id, string text, int chainHappen = 0,
                                 int chainNotHappen = 0, int chainTrigger = 0,
                                 int eventTrigger = 0) =>
        new(Header(EventType.TextStatement, id, text, chainHappen, chainNotHappen, chainTrigger,
                   eventTrigger),
            WaitForReturn: 1, ForceBackup: 0, HighlightText: 0, Distance: 0, Sound: string.Empty);

    /// <summary>A question list, whose options are where its chains live.</summary>
    public static QuestionEvent Question(uint id, string title,
                                         params (string Label, uint Chain)[] options) =>
        new(Header(EventType.QuestionList, id, title),
            title, options.Length,
            [..options.Select(o => new QuestionOption(o.Label, 1, 0, o.Chain))]);

    /// <summary>A yes/no question with two fixed branches.</summary>
    public static YesNoEvent YesNo(uint id, uint yes, uint no) =>
        new(Header(EventType.QuestionYesNo, id), 0, 0, yes, no);

    /// <summary>
    /// A level around a set of events.
    /// </summary>
    /// <remarks>
    /// Everything but the events is empty. The editor reads <c>Entries</c> for the wire order and
    /// <c>Events</c> for the baseline, so both are filled from the same list.
    /// </remarks>
    public static LevelFile Level(params IGameEvent[] events) =>
        new(DesignVersion.Engine, 10, 10, [], 0,
            events.Length, events,
            [..events.Select(e => new LevelEventEntry((EventType)e.Base.EventType, e))],
            new ZoneData([], string.Empty), [], [], [], [], []);
}
