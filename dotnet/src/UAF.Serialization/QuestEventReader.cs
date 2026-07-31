using UAF.Common;

namespace UAF.Serialization;

/// <summary>A <c>QUEST_EVENT_DATA</c> — advances or resolves a quest stage.</summary>
public sealed record QuestEvent(
    GameEventBase Base, int Operation, int CompleteOnAccept, int FailOnRejection,
    int Quest, ushort Stage, uint AcceptChain, uint RejectChain) : IGameEvent;

/// <summary>
/// Reads <c>QUEST_EVENT_DATA</c> (<c>GameEvent.cpp:9458</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b><c>stage</c> is a <c>WORD</c></b> (<c>GameEvent.h:2424</c>) sitting between an <c>int</c> and
/// two <c>DWORD</c>s, so the record is 26 bytes rather than 28. Its declaration is separated from
/// the other members by a block of inline accessors, which makes the class look like it ends
/// earlier than it does.
/// </para>
/// <para>
/// <c>m_quest</c> is not a plain id: the top four bits encode a <c>QUEST_TYPE</c> (quest, item or
/// key) and the low 28 bits the id (<c>GameEvent.h:2403</c>). Stored raw here; use
/// <see cref="QuestId"/> and <see cref="QuestType"/> to decompose it.
/// </para>
/// </remarks>
public static class QuestEventReader
{
    /// <summary>The id part of a packed <c>m_quest</c> — the low 28 bits.</summary>
    public static int QuestId(int quest) => quest & 0xFFFFFFF;

    /// <summary>
    /// The type part of a packed <c>m_quest</c>. Negative means none; a zero type field means the
    /// default quest flag rather than type 0.
    /// </summary>
    public static int QuestType(int quest)
    {
        if (quest < 0) return -1;
        if ((quest & 0x70000000) == 0) return 0;        // QUEST_FLAG, the default
        return (quest >> 28) & 0x7;
    }

    public static QuestEvent Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int operation = ar.ReadInt32();
        int completeOnAccept = ar.ReadInt32();
        int failOnRejection = ar.ReadInt32();
        int quest = ar.ReadInt32();
        ushort stage = ar.ReadUInt16();                  // WORD, not int
        uint acceptChain = ar.ReadUInt32();
        uint rejectChain = ar.ReadUInt32();

        return new QuestEvent(baseEvent, operation, completeOnAccept, failOnRejection,
                              quest, stage, acceptChain, rejectChain);
    }
}
