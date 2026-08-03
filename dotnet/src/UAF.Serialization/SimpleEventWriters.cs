namespace UAF.Serialization;

/// <summary>
/// Writes the event subclasses small enough not to warrant a file each — the inverses of
/// <see cref="SimpleEventReaders"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every one is <see cref="GameEventWriter.Write"/> followed by a handful of fields, so the refusal
/// that matters — a legacy numeric id in the control block — is checked once, in the base.
/// </para>
/// <para>
/// <b>Two of these carry fixed-size arrays with no count</b>, which is the shape this family gets
/// wrong most easily: a question event always writes five options whatever <c>numListButtons</c>
/// says, and a random event always writes thirteen branches out of a fourteen-slot array. Sizing
/// either from the record's own list rather than from the constant produces a stream that reads
/// back plausibly and desynchronises at the next event.
/// </para>
/// </remarks>
public static class SimpleEventWriters
{
    /// <summary>Writes a <c>CHAIN_EVENT</c> (<c>GameEvent.cpp:10261</c>).</summary>
    public static void WriteChain(IArchiveWriteCursor ar, ChainEvent chain)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(chain);

        GameEventWriter.Write(ar, chain.Base);
        ar.WriteUInt32(chain.Chain);
    }

    /// <summary>
    /// Writes a <c>QUESTION_LIST_DATA</c> (<c>GameEvent.cpp:8158</c>) — the base, then a
    /// <c>QLIST_DATA</c>: a title, a button count, and five options.
    /// </summary>
    public static void WriteQuestionList(IArchiveWriteCursor ar, QuestionEvent question)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(question);

        GameEventWriter.Write(ar, question.Base);

        GameEventWriter.WriteDas(ar, question.Title);
        ar.WriteInt32(question.NumButtons);
        WriteOptions(ar, question.Options);
    }

    /// <summary>
    /// Writes a <c>QUESTION_BUTTON_DATA</c> (<c>GameEvent.cpp:8187</c>) — the list form
    /// <b>without</b> the title.
    /// </summary>
    /// <remarks>
    /// The two events look interchangeable: both are <c>buttons.Serialize(ar)</c> after the base.
    /// Only their <c>buttons</c> members differ in type, and only the list form has a title — so
    /// writing one as the other invents or loses a counted string, and
    /// <see cref="QuestionEvent.Title"/> is simply not written here.
    /// </remarks>
    public static void WriteQuestionButton(IArchiveWriteCursor ar, QuestionEvent question)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(question);

        GameEventWriter.Write(ar, question.Base);

        ar.WriteInt32(question.NumButtons);
        WriteOptions(ar, question.Options);
    }

    /// <summary>Writes a <c>QUESTION_YES_NO</c> (<c>GameEvent.cpp:7227</c>).</summary>
    /// <remarks>
    /// No option array at all — two post-chain actions then two chain targets, in
    /// action/action/chain/chain order rather than interleaved.
    /// </remarks>
    public static void WriteYesNo(IArchiveWriteCursor ar, YesNoEvent yesNo)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(yesNo);

        GameEventWriter.Write(ar, yesNo.Base);

        ar.WriteInt32(yesNo.YesChainAction);
        ar.WriteInt32(yesNo.NoChainAction);
        ar.WriteUInt32(yesNo.YesChain);
        ar.WriteUInt32(yesNo.NoChain);
    }

    /// <summary>Writes a <c>PASS_TIME_EVENT_DATA</c> (<c>GameEvent.cpp:9309</c>).</summary>
    /// <remarks>
    /// Three <c>BYTE</c>s then three 4-byte <c>BOOL</c>s — fifteen bytes, not twenty-four. Writing
    /// the duration as <c>int</c>s would put nine bytes where the flags belong.
    /// </remarks>
    public static void WritePassTime(IArchiveWriteCursor ar, PassTimeEvent passTime)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(passTime);

        GameEventWriter.Write(ar, passTime.Base);

        ar.WriteByte(passTime.Days);
        ar.WriteByte(passTime.Hours);
        ar.WriteByte(passTime.Minutes);
        ar.WriteInt32(passTime.AllowStop);
        ar.WriteInt32(passTime.SetTime);
        ar.WriteInt32(passTime.PassSilent);
    }

    /// <summary>Writes a <c>RANDOM_EVENT_DATA</c> (<c>GameEvent.cpp:10048</c>).</summary>
    /// <remarks>
    /// <b>Thirteen branches, not fourteen.</b> The reference loops
    /// <c>for (i = 1; i &lt; MAX_RANDOM_EVENTS; i++)</c> indexing <c>[i - 1]</c>, so the array's
    /// fourteenth slot is never on the wire. Each branch is a <c>DWORD</c> and a <b><c>BYTE</c></b>
    /// — five bytes, so the block is 65.
    /// </remarks>
    public static void WriteRandom(IArchiveWriteCursor ar, RandomEvent random)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(random);

        if (random.Branches.Count != SimpleEventReaders.SerializedRandomBranches)
        {
            throw new ArgumentException(
                $"a RANDOM_EVENT_DATA writes exactly " +
                $"{SimpleEventReaders.SerializedRandomBranches} branches, not " +
                $"{random.Branches.Count}. The count is compile-time in the reference and never " +
                "written, so a short list silently truncates the event.", nameof(random));
        }

        GameEventWriter.Write(ar, random.Base);

        foreach (var branch in random.Branches)
        {
            ar.WriteUInt32(branch.Chain);
            ar.WriteByte(branch.Chance);             // BYTE, not int
        }
    }

    /// <summary>Writes an <c>ADD_NPC_DATA</c> (<c>GameEvent.cpp:6613</c>).</summary>
    /// <remarks>
    /// The character reference has the same pre-0.998101 numeric-key form the control block's ids
    /// do, and under exactly the same condition — so
    /// <see cref="GameEventWriter.CanWrite"/> on the base already refuses it, and there is no
    /// second check to make here.
    /// </remarks>
    public static void WriteAddNpc(IArchiveWriteCursor ar, AddNpcEvent npc)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(npc);

        GameEventWriter.Write(ar, npc.Base);

        ar.WriteInt32(npc.Operation);
        ar.WriteString(npc.CharacterId);             // verbatim: a CHARACTER_ID
        ar.WriteInt32(npc.HitPointMod);
        ar.WriteInt32(npc.UseOriginal);
    }

    /// <summary>Writes a <c>TRANSFER_EVENT_DATA</c> (<c>GameEvent.cpp:8734</c>).</summary>
    /// <remarks>
    /// Serves three event ordinals — stairs, teleporter and module transfer — which share this one
    /// layout. The destination block sits <b>outside</b> the storing branch, so it is written at
    /// every version and by both halves.
    /// </remarks>
    public static void WriteTransfer(IArchiveWriteCursor ar, TransferEvent transfer)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(transfer);

        GameEventWriter.Write(ar, transfer.Base);

        ar.WriteInt32(transfer.AskYesNo);
        ar.WriteInt32(transfer.TransferOnYes);
        ar.WriteInt32(transfer.DestroyDrow);
        ar.WriteInt32(transfer.ActivateBeforeEntry);

        WriteTransferData(ar, transfer.Destination);
    }

    /// <summary>Writes a <c>TRANSFER_DATA</c> (<c>GameEvent.cpp:4640</c>): six ints.</summary>
    public static void WriteTransferData(IArchiveWriteCursor ar, TransferData destination)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(destination);

        ar.WriteInt32(destination.ExecuteEvent);
        ar.WriteInt32(destination.DestEntryPoint);
        ar.WriteInt32(destination.DestLevel);
        ar.WriteInt32(destination.DestX);
        ar.WriteInt32(destination.DestY);
        ar.WriteInt32(destination.Facing);
    }

    /// <summary>
    /// Writes the fixed option array — always <see cref="SimpleEventReaders.MaxButtons"/> entries,
    /// whatever <c>numListButtons</c> says, and outside the storing branch.
    /// </summary>
    private static void WriteOptions(IArchiveWriteCursor ar, IReadOnlyList<QuestionOption> options)
    {
        if (options.Count != SimpleEventReaders.MaxButtons)
        {
            throw new ArgumentException(
                $"a question event writes exactly {SimpleEventReaders.MaxButtons} options, not " +
                $"{options.Count}. Unused slots carry the blank sentinel rather than being " +
                "omitted, so a short list truncates the event.", nameof(options));
        }

        foreach (var option in options)
        {
            GameEventWriter.WriteDas(ar, option.Label);
            ar.WriteInt32(option.Present);
            ar.WriteInt32(option.PostChainAction);
            ar.WriteUInt32(option.Chain);
        }
    }
}
