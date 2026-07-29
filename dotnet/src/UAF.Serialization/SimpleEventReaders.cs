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

/// <summary>A <c>QUESTION_YES_NO</c> — two fixed branches rather than an option array.</summary>
public sealed record YesNoEvent(
    GameEventBase Base, int YesChainAction, int NoChainAction, uint YesChain, uint NoChain);

/// <summary>A <c>PASS_TIME_EVENT_DATA</c> — advances the clock.</summary>
public sealed record PassTimeEvent(
    GameEventBase Base, byte Days, byte Hours, byte Minutes,
    int AllowStop, int SetTime, int PassSilent);

/// <summary>An <c>ADD_NPC_DATA</c> — joins an NPC to the party.</summary>
public sealed record AddNpcEvent(
    GameEventBase Base, int Operation, string CharacterId, int HitPointMod, int UseOriginal);

/// <summary>Where a transfer sends the party (<c>GameEvent.cpp:4640</c>).</summary>
public sealed record TransferData(
    int ExecuteEvent, int DestEntryPoint, int DestLevel, int DestX, int DestY, int Facing);

/// <summary>
/// A <c>TRANSFER_EVENT_DATA</c> — stairs, a teleporter or a module transfer.
/// </summary>
public sealed record TransferEvent(
    GameEventBase Base, int AskYesNo, int TransferOnYes, int DestroyDrow,
    int ActivateBeforeEntry, TransferData Destination);

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
    /// Reads an <c>ADD_NPC_DATA</c> (<c>GameEvent.cpp:6613</c>).
    /// </summary>
    /// <remarks>
    /// Note the gate on the character reference uses the <c>version</c> parameter while
    /// <c>useOriginal</c> uses the global <c>LoadingVersion</c> — the same mixed-source pattern as
    /// <c>EVENT_CONTROL</c>, four lines apart again.
    /// </remarks>
    public static AddNpcEvent ReadAddNpc(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int operation = ar.ReadInt32();

        string characterId;
        if (role == ArchiveRole.Editor && version < DesignVersion.SpellNames)
        {
            int key = ar.ReadInt32();
            characterId = key <= 0 ? string.Empty : key.ToString();
        }
        else
        {
            characterId = ar.ReadString();
        }

        int hitPointMod = ar.ReadInt32();
        int useOriginal = version >= DesignVersion.V0695 ? ar.ReadInt32() : 0;

        return new AddNpcEvent(baseEvent, operation, characterId, hitPointMod, useOriginal);
    }

    /// <summary>
    /// Reads a <c>TRANSFER_EVENT_DATA</c> (<c>GameEvent.cpp:8734</c>).
    /// </summary>
    /// <remarks>
    /// Serves three event ordinals — <see cref="EventType.Stairs"/>,
    /// <see cref="EventType.Teleporter"/> and <see cref="EventType.TransferModule"/> — which share
    /// this one layout. The destination block is read outside the storing/loading branch.
    /// </remarks>
    public static TransferEvent ReadTransfer(IArchiveCursor ar, DesignVersion version,
                                             ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int askYesNo = ar.ReadInt32();
        int transferOnYes = ar.ReadInt32();
        int destroyDrow = ar.ReadInt32();
        int activateBeforeEntry = ar.ReadInt32();

        var destination = new TransferData(
            ar.ReadInt32(),                              // execEvent
            ar.ReadInt32(),                              // destEP
            ar.ReadInt32(),                              // destLevel
            ar.ReadInt32(),                              // destX
            ar.ReadInt32(),                              // destY
            ar.ReadInt32());                             // m_facing

        return new TransferEvent(baseEvent, askYesNo, transferOnYes, destroyDrow,
                                 activateBeforeEntry, destination);
    }

    /// <summary>
    /// Reads a <c>PASS_TIME_EVENT_DATA</c> (<c>GameEvent.cpp:9309</c>).
    /// </summary>
    /// <remarks>
    /// The duration is three <c>BYTE</c>s and the flags three 4-byte <c>BOOL</c>s, so the record is
    /// 15 bytes at 0.830 and above and 3 below it. Reading the duration as ints would consume the
    /// flags as well.
    /// </remarks>
    public static PassTimeEvent ReadPassTime(IArchiveCursor ar, DesignVersion version,
                                             ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        byte days = ar.ReadByte();
        byte hours = ar.ReadByte();
        byte minutes = ar.ReadByte();

        int allowStop = 0;
        int setTime = 0;
        int passSilent = 0;
        if (version >= DesignVersion.V0830)
        {
            allowStop = ar.ReadInt32();
            setTime = ar.ReadInt32();
            passSilent = ar.ReadInt32();
        }

        return new PassTimeEvent(baseEvent, days, hours, minutes,
                                 allowStop, setTime, passSilent);
    }

    /// <summary>
    /// Reads a <c>QUESTION_YES_NO</c> (<c>GameEvent.cpp:7227</c>).
    /// </summary>
    /// <remarks>
    /// Despite the family resemblance this has no option array at all — just two post-chain
    /// actions and two chain targets, in action/action/chain/chain order rather than interleaved.
    /// </remarks>
    public static YesNoEvent ReadYesNo(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int yesChainAction = ar.ReadInt32();
        int noChainAction = ar.ReadInt32();
        uint yesChain = ar.ReadUInt32();
        uint noChain = ar.ReadUInt32();

        return new YesNoEvent(baseEvent, yesChainAction, noChainAction, yesChain, noChain);
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
