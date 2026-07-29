using UAF.Common;

namespace UAF.Serialization;

/// <summary>A <c>CHAIN_EVENT</c> — jumps to another event by key.</summary>
public sealed record ChainEvent(GameEventBase Base, uint Chain);

/// <summary>
/// One selectable option on a question event: a label, whether it is shown, what to do after the
/// chained event returns, and the chain target.
/// </summary>
/// <remarks>
/// <c>QLIST_OPTION</c> and <c>QBUTTON_OPTION</c> are declared separately but serialize identically,
/// so one reader covers both.
/// </remarks>
public sealed record QuestionOption(string Label, int Present, int PostChainAction, uint Chain);

/// <summary>A question event: an optional title and a fixed array of options.</summary>
public sealed record QuestionEvent(
    GameEventBase Base, string Title, int NumButtons, IReadOnlyList<QuestionOption> Options);

/// <summary>
/// Event subclasses small enough not to warrant a file each.
/// </summary>
/// <remarks>
/// Each is the shared <see cref="GameEventReader"/> base plus a handful of fields. They are grouped
/// rather than split so the base-then-fields shape stays visible at a glance.
/// </remarks>
public static class SimpleEventReaders
{
    /// <summary>
    /// Reads a <c>CHAIN_EVENT</c> (<c>GameEvent.cpp:10261</c>) — the base plus one <c>DWORD</c>.
    /// </summary>
    /// <remarks>
    /// The smallest subclass with any payload of its own. Note the chain target is an event
    /// <i>key</i>, not an index, so it is meaningful only against the level's event list.
    /// </remarks>
    public static ChainEvent ReadChain(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);
        return new ChainEvent(baseEvent, ar.ReadUInt32());
    }

    /// <summary>Every question event writes exactly this many options (<c>GameEvent.h:50</c>).</summary>
    public const int MaxButtons = 5;

    /// <summary>
    /// Reads a <c>QUESTION_LIST_DATA</c> (<c>GameEvent.cpp:8158</c>) — base, then a
    /// <c>QLIST_DATA</c> with a title.
    /// </summary>
    public static QuestionEvent ReadQuestionList(IArchiveCursor ar, DesignVersion version,
                                                 ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        string title = ArchiveStringConventions.Decode(ar.ReadString());
        int numButtons = ar.ReadInt32();

        return new QuestionEvent(baseEvent, title, numButtons, ReadOptions(ar));
    }

    /// <summary>
    /// Reads a <c>QUESTION_BUTTON_DATA</c> (<c>GameEvent.cpp:8187</c>) — base, then a
    /// <c>QBUTTON_DATA</c>, which is the list form <b>without</b> the title string.
    /// </summary>
    /// <remarks>
    /// The two events look interchangeable — both are just <c>buttons.Serialize(ar)</c> after the
    /// base — but their <c>buttons</c> members are different types, and only the list form writes a
    /// title. Reading one as the other loses or invents a counted string.
    /// </remarks>
    public static QuestionEvent ReadQuestionButton(IArchiveCursor ar, DesignVersion version,
                                                   ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);
        int numButtons = ar.ReadInt32();

        return new QuestionEvent(baseEvent, string.Empty, numButtons, ReadOptions(ar));
    }

    /// <summary>
    /// Reads the fixed option array. Always <see cref="MaxButtons"/> entries regardless of
    /// <c>numListButtons</c>, and read outside the storing/loading branch.
    /// </summary>
    private static List<QuestionOption> ReadOptions(IArchiveCursor ar)
    {
        var options = new List<QuestionOption>(MaxButtons);
        for (int i = 0; i < MaxButtons; i++)
        {
            options.Add(new QuestionOption(
                ArchiveStringConventions.Decode(ar.ReadString()),
                ar.ReadInt32(),
                ar.ReadInt32(),
                ar.ReadUInt32()));
        }
        return options;
    }
}
