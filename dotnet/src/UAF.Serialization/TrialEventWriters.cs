namespace UAF.Serialization;

/// <summary>
/// Writers for the five event types the FRUA importer produces that had none.
/// </summary>
/// <remarks>
/// <b>All five are types no shipped <i>native</i> design contains</b>, which is why they were left
/// unported: there was no corpus to build a writer against and nothing to check one with. A FRUA
/// design supplies them, so each is written here as the exact mirror of its reader, and the
/// importer's round-trip test is the check the native corpus could not provide.
/// </remarks>
public static class TrialEventWriters
{
    /// <summary>Writes a <c>SMALL_TOWN_DATA</c> (<c>GameEvent.cpp:10138</c>).</summary>
    /// <remarks>
    /// <b><c>Unused</c> is on the wire.</b> It is a <c>long</c> the reference reads and writes and
    /// never looks at; omitting it would shorten the record by four bytes and desynchronise
    /// everything after it.
    /// </remarks>
    public static void WriteSmallTown(IArchiveWriteCursor ar, SmallTownEvent town)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(town);

        GameEventWriter.Write(ar, town.Base);

        ar.WriteInt32(town.Unused);
        ar.WriteUInt32(town.TempleChain);
        ar.WriteUInt32(town.TrainingHallChain);
        ar.WriteUInt32(town.ShopChain);
        ar.WriteUInt32(town.InnChain);
        ar.WriteUInt32(town.TavernChain);
        ar.WriteUInt32(town.VaultChain);
    }

    /// <summary>Writes a <c>TAVERN_TALES</c>.</summary>
    /// <remarks>
    /// <b>Each tale's text is written verbatim, not through the DAS convention</b>, matching what
    /// the reader consumes. Each tale also carries its own attribute list, and the event carries
    /// one more after the loop — two different ASL maps, in that order.
    /// </remarks>
    public static void WriteTavernTales(IArchiveWriteCursor ar, TavernTalesEvent tales)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(tales);

        GameEventWriter.Write(ar, tales.Base);

        ar.WriteUInt32(tales.Flags);
        ar.WriteInt32(tales.Tales.Count);

        foreach (var tale in tales.Tales)
        {
            ar.WriteString(tale.Text);                   // verbatim, no DAS
            ar.WriteUInt32(tale.Flags);
            AslWriter.Write(ar, GameEventWriter.WrittenVersion, AslMaps.Tale, tale.Attributes);
        }

        AslWriter.Write(ar, GameEventWriter.WrittenVersion, AslMaps.TavernTale, tales.Attributes);
    }

    /// <summary>Writes an <c>ENCOUNTER_DATA</c> (<c>GameEvent.cpp:7313</c>).</summary>
    /// <remarks>
    /// <b>All five options are written whatever <c>NumButtons</c> says.</b> The array is
    /// compile-time in the reference and the count is a separate field, so writing only the
    /// counted ones would drop the rest of the record.
    /// </remarks>
    public static void WriteEncounter(IArchiveWriteCursor ar, EncounterEvent encounter)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(encounter);

        GameEventWriter.Write(ar, encounter.Base);

        ar.WriteInt32(encounter.Distance);
        ar.WriteInt32(encounter.MonsterSpeed);
        ar.WriteInt32(encounter.ZeroRangeResult);
        ar.WriteUInt32(encounter.CombatChain);
        ar.WriteUInt32(encounter.TalkChain);
        ar.WriteUInt32(encounter.EscapeChain);
        ar.WriteInt32(encounter.NumButtons);

        for (int i = 0; i < EncounterEventReader.MaxButtons; i++)
        {
            var option = i < encounter.Options.Count ? encounter.Options[i] : EmptyOption;

            GameEventWriter.WriteDas(ar, option.Label);
            ar.WriteInt32(option.Present);
            ar.WriteInt32(option.AllowedUpClose);
            ar.WriteInt32(option.OptionResult);
            ar.WriteUInt32(option.Chain);

            // Gated at 0.890 on read; always written, since a file is only written modern.
            ar.WriteInt32(option.OnlyUpClose);
        }
    }

    private static EncounterOption EmptyOption { get; } = new(string.Empty, 0, 0, 0, 0, 0);

    /// <summary>Writes a <c>PASSWORD_DATA</c>.</summary>
    /// <remarks>
    /// <b>The two chains come before the two actions, not after.</b> The reader's order is tries,
    /// success chain, fail chain, success action, fail action — pairing chain with chain rather
    /// than each action with its own target, which is the order the record's field names suggest.
    /// </remarks>
    public static void WritePassword(IArchiveWriteCursor ar, PasswordEvent password)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(password);

        GameEventWriter.Write(ar, password.Base);

        ar.WriteInt32(password.NbrTries);
        ar.WriteUInt32(password.SuccessChain);
        ar.WriteUInt32(password.FailChain);
        ar.WriteInt32(password.SuccessAction);
        ar.WriteInt32(password.FailAction);

        GameEventWriter.WriteDas(ar, password.Password);

        SimpleEventWriters.WriteTransferData(ar, password.SuccessTransfer);
        SimpleEventWriters.WriteTransferData(ar, password.FailTransfer);
    }

    /// <summary>Writes a <c>WHO_TRIES_EVENT_DATA</c>.</summary>
    /// <remarks>
    /// <b>The two check arrays are fixed-length and the strength bonus is a <c>BYTE</c> among
    /// them.</b> Six abilities then eight thief skills, all <c>int</c>, then a single byte — so a
    /// list shorter than the reader expects has to be padded rather than truncating the record.
    /// </remarks>
    public static void WriteWhoTries(IArchiveWriteCursor ar, WhoTriesEvent tries)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(tries);

        GameEventWriter.Write(ar, tries.Base);

        ar.WriteInt32(tries.AlwaysSucceeds);
        ar.WriteInt32(tries.AlwaysFails);

        WriteChecks(ar, tries.AbilityChecks, TrialEventReaders.AbilityNames.Count);
        WriteChecks(ar, tries.ThiefSkillChecks, TrialEventReaders.ThiefSkillNames.Count);

        ar.WriteByte(tries.StrengthBonus);
        ar.WriteInt32(tries.CompareToDie);
        ar.WriteInt32(tries.CompareDie);
        ar.WriteInt32(tries.NbrTries);
        ar.WriteUInt32(tries.SuccessChain);
        ar.WriteInt32(tries.SuccessAction);
        ar.WriteInt32(tries.FailAction);
        ar.WriteUInt32(tries.FailChain);

        SimpleEventWriters.WriteTransferData(ar, tries.SuccessTransfer);
        SimpleEventWriters.WriteTransferData(ar, tries.FailTransfer);
    }

    /// <summary>
    /// One fixed-length check array, as a flag per check rather than a list of indices.
    /// </summary>
    /// <remarks>
    /// <b>The wire format is one <c>int</c> per check, not a list of the checks that apply.</b>
    /// A converter that holds the selected indices — as the FRUA importer does, because FRUA
    /// selects exactly one — has to expand them back into the full array, or every later field
    /// lands short.
    /// </remarks>
    private static void WriteChecks(IArchiveWriteCursor ar, IReadOnlyList<int> selected, int count)
    {
        for (int i = 0; i < count; i++)
        {
            ar.WriteInt32(Selected(selected, i, count) ? 1 : 0);
        }
    }

    /// <summary>
    /// Whether check <paramref name="i"/> is on.
    /// </summary>
    /// <remarks>
    /// A list the same length as the array is already the flags; a shorter one names indices.
    /// Both shapes reach here — the reader produces the first and the FRUA importer the second —
    /// and telling them apart by length is what lets one writer serve both.
    /// </remarks>
    private static bool Selected(IReadOnlyList<int> selected, int i, int count) =>
        selected.Count == count ? selected[i] != 0 : selected.Contains(i);
}
