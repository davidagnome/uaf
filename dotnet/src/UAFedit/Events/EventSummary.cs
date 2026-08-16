using UAF.Serialization;

namespace UAFedit.Events;

/// <summary>
/// One line describing what an event actually does, for the list.
/// </summary>
/// <remarks>
/// <para>
/// The original's tree node is <c>"&lt;TypeName&gt; (&lt;id&gt;): &lt;x&gt;,&lt;y&gt;"</c>
/// (<c>GetEventIdDescription</c>, <c>Shared/Globals.cpp:1445</c>, plus the coordinates appended in
/// <c>PopulateTreeFromRootEvent</c>, <c>EventViewer.cpp:2621</c>) — type, id, position, and nothing
/// about the event's content. In a design where 3,146 of 4,244 events are text statements, that
/// makes every node read the same, and the original's own answer is a Search dialog
/// (<c>IDD_SEARCHPARAMS</c>) that scans <c>text</c>, <c>text2</c> and <c>text3</c> precisely
/// because the tree cannot show them.
/// </para>
/// <para>
/// So this adds the content: the event's text where it has any, and otherwise the one field that
/// distinguishes it from its neighbours. Type, id and position stay as their own columns.
/// </para>
/// </remarks>
public static class EventSummary
{
    /// <summary>How much of a text an event's row shows before eliding.</summary>
    private const int MaxLength = 110;

    /// <summary>A one-line description of what the event does.</summary>
    public static string For(IGameEvent body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string text = Clean(body.Base.Text);

        return text.Length > 0 ? Elide(text) : Elide(Specific(body));
    }

    /// <summary>
    /// The distinguishing field for an event with no display text.
    /// </summary>
    /// <remarks>
    /// Only types that carry something worth a glance are listed. Everything else falls through to
    /// the empty string, and the row shows type, id and position — which is exactly what the
    /// original shows for all of them.
    /// </remarks>
    private static string Specific(IGameEvent body) => body switch
    {
        ChainEvent chain => $"→ {chain.Chain}",

        CombatEvent combat => Roster(combat),

        TransferEvent transfer =>
            $"→ level {transfer.Destination.DestLevel} "
            + $"at {transfer.Destination.DestX},{transfer.Destination.DestY}",

        QuestionEvent question when question.Title.Length > 0 => Clean(question.Title),

        QuestionEvent question =>
            string.Join(" / ", question.Options.Where(o => o.Present != 0 && o.Label.Length > 0)
                                               .Select(o => Clean(o.Label))),

        SoundEvent sound => string.Join(", ", sound.Sounds.Where(s => s.Length > 0)),

        QuestEvent quest => $"quest {quest.Quest} → stage {quest.Stage}",

        JournalEvent journal => $"journal entry {journal.Entry}",

        PlayMovieEvent movie => movie.FileName,

        NpcSaysEvent npc => npc.CharacterId,

        AddNpcEvent npc => npc.CharacterId,

        RemoveNpcEvent npc => npc.CharacterId,

        GainExperienceEvent experience => $"{experience.Experience} xp",

        PassTimeEvent time => $"{time.Days}d {time.Hours}h {time.Minutes}m",

        // The one line that says what a logic block is for: its first input's value.
        LogicBlockEvent logic => logic.Inputs.FirstOrDefault(i => i.Length > 0) ?? string.Empty,

        FlowControlEvent flow => flow.DestinationMarker.Length > 0
            ? $"→ {flow.DestinationMarker}"
            : flow.EntryMarker,

        _ => string.Empty,
    };

    /// <summary>A combat's monsters, which is the only thing that distinguishes one from another.</summary>
    private static string Roster(CombatEvent combat)
    {
        var named = combat.Monsters
            .Where(m => m.MonsterId.Length > 0 || m.CharacterId.Length > 0)
            .Select(m => m.MonsterId.Length > 0 ? m.MonsterId : m.CharacterId)
            .ToList();

        return named.Count > 0 ? string.Join(", ", named) : string.Empty;
    }

    /// <summary>
    /// A display text flattened to one line.
    /// </summary>
    /// <remarks>
    /// Event text carries the engine's own escapes — <c>^1</c> for a character name, <c>/r</c> for
    /// red, <c>/n</c> to wait for ENTER (<c>IDD_TEXT_EVENT</c>'s legend). They are left in rather
    /// than stripped: a summary that silently dropped them would hide the difference between two
    /// events whose only difference is one.
    /// </remarks>
    private static string Clean(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Elide(string text) =>
        text.Length <= MaxLength ? text : string.Concat(text.AsSpan(0, MaxLength - 1), "…");
}
