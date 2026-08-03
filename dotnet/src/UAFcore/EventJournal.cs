using UAF.Serialization;

namespace UAFcore;

/// <summary>What a journal event did.</summary>
/// <param name="Added">Whether an entry reached the party's journal.</param>
/// <param name="Key">
/// The key the party's journal gave the new entry, or -1 when nothing was added — the same -1
/// <c>JOURNAL_DATA::Add</c> returns (<c>Shared/Party.h:99</c>).
/// </param>
public readonly record struct JournalOutcome(bool Added, int Key);

/// <summary>
/// Runs a <c>JOURNAL_EVENT</c> (<c>JOURNAL_EVENT::OnInitialEvent</c>,
/// <c>UAFWin/RunEvent.cpp:27540</c>): copies one of the design's journal entries into the party's
/// own journal, then chains.
/// </summary>
/// <remarks>
/// <para>
/// <b>The event carries an index, not text.</b> <c>journal_entry</c>
/// (<c>Shared/GameEvent.h:2971</c>) is a key into the design's authored journal table,
/// <c>globalData.journalData</c> (<c>Shared/GlobalData.h:927</c>) — a <c>JOURNAL_DATA</c>
/// serialized as part of <c>GLOBAL_STATS</c> and surfaced here as
/// <see cref="GlobalStatsPrefix.Journal"/>. The editor authors it in <c>JournalDataDlg.cpp</c> and
/// hands the event a key from that same list (<c>JournalAddEventDlg.cpp:181</c>). So a journal
/// event contributes no words of its own; it names one of the design's.
/// </para>
/// <para>
/// <b>The accumulated journal is party state, not world state.</b> Both tables are the same
/// <c>JOURNAL_DATA</c> type, but the collected one is <c>PARTY::journal</c>
/// (<c>Shared/Party.h:643</c>) and is written inside <c>PARTY::Serialize</c>
/// (<c>Shared/Party.cpp:926</c>), between the pooled money sack and <c>party_asl</c>. Quests,
/// special items and keys — <see cref="WorldState"/>'s contents — are written <i>after</i> PARTY
/// instead (<c>UAFWin/Dgngame.cpp:188</c>), which is the same split
/// <see cref="SaveGame"/> already reads. That is why the store lives on <see cref="Party"/> and not
/// on <see cref="WorldState"/>: putting it there would place it in the savegame's global record
/// rather than its party one, the mirror image of the mistake <see cref="WorldState"/>'s own
/// remarks warn against.
/// </para>
/// <para>
/// <b>Nothing below was observed; all of it was transcribed.</b> <c>JournalEvent</c> appears zero
/// times across the six level-bearing corpus designs (see
/// <c>EventTypesAbsentFromCorpusTests</c>), and the design journal table it indexes is
/// <i>empty</i> in every one of the five that load — Case, Ambassador's Letter, SomethingWild,
/// dc-default and ci-tier3 all carry a zero count. So there is no design anywhere in the corpus
/// that could exercise a single line of this, which is the reason the reference's awkward parts
/// are reproduced exactly rather than tidied.
/// </para>
/// <para>
/// <b>Entries go in and, in this port, nothing reads them back yet.</b> In the reference they are
/// live: the game menu greys out its journal item when the count is zero
/// (<c>UAFWin/RunEvent.cpp:9232</c>) and <c>DISPLAY_PARTY_JOURNAL_DATA::OnInitialEvent</c>
/// (<c>:27604</c>) renders the whole list through <c>FormatJournalText</c>
/// (<c>UAFWin/FormattedText.cpp:1201</c>), newest box first. That screen is not ported — the
/// journal-sized text box survives only as <c>TextDisplayData.LinesPerBox</c> — so today this is a
/// write-only store. That is a missing reader, not a defect in what is written.
/// </para>
/// <para>
/// <b><c>JOURNAL_EVENT::EventShouldTrigger</c> does not matter.</b> The override
/// (<c>Shared/GameEvent.cpp:10209</c>) is <c>if (!GameEvent::EventShouldTrigger()) return FALSE;
/// return TRUE;</c> — the base result, unchanged. Virtual dispatch reaches the same decision it
/// would with no override at all, so the port needs nothing for it and a journal event triggers on
/// the ordinary rules.
/// </para>
/// </remarks>
public static class EventJournal
{
    /// <summary>The result when the event adds nothing.</summary>
    public static JournalOutcome NotAdded => new(false, -1);

    /// <summary>
    /// Finds the design's journal entry with this key (<c>JOURNAL_DATA::Get</c>,
    /// <c>Shared/Party.h:132</c>).
    /// </summary>
    /// <remarks>
    /// The reference searches an ascending key-ordered queue and gives up early once it passes the
    /// key it wants (<c>OrderedQueue::FindKeyPos</c>, <c>Shared/SharedQueue.h:954</c>); this scans
    /// the whole list. The two agree because the table is written out head-to-tail from that same
    /// ordered queue, so it arrives from disk already sorted — on a list that was somehow <i>not</i>
    /// sorted this would find a late entry the reference would step over.
    /// </remarks>
    public static JournalEntry? Lookup(IReadOnlyList<JournalEntry> designJournal, int entry)
    {
        ArgumentNullException.ThrowIfNull(designJournal);

        foreach (var candidate in designJournal)
        {
            if (candidate.Entry == entry)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Copies the named design entry into the party's journal.
    /// </summary>
    /// <param name="expandTokens">
    /// Substitutes the <c>^</c> tokens in the entry's text. <b>Required, because the port has no
    /// equivalent to substitute with</b> — see the remarks.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>There are three ways to add nothing and they are indistinguishable to the design.</b> A
    /// negative index returns early (<c>RunEvent.cpp:27542</c>); a key the table does not hold
    /// returns early (<c>:27549</c>); and a journal already holding
    /// <see cref="Party.MaxJournalEntries"/> entries silently refuses the insert
    /// (<c>Shared/Party.h:102</c>). All three then chain exactly as a successful add does, so an
    /// event pointing at a deleted journal entry is completely silent rather than an error.
    /// </para>
    /// <para>
    /// <b>Index 0 is not caught by the negative guard.</b> It falls through to the lookup and fails
    /// there instead, because <c>JOURNAL_DATA::GetNextKey</c> (<c>Shared/Party.h:61</c>) issues 1
    /// for the first entry and never 0. The two paths reach the same outcome only because the key
    /// space starts at 1; a port that special-cased 0 as "no entry" would be right by accident.
    /// </para>
    /// <para>
    /// <b>An entry can be journalled twice.</b> <c>JOURNAL_DATA::HaveGlobalJournalEntryAlready</c>
    /// (<c>Shared/Party.h:154</c>) exists to answer exactly the question "has this design entry
    /// already been collected", matching on <c>origentry</c> — and it is called from nowhere in the
    /// entire tree. <c>OnInitialEvent</c> does not consult it, so a journal event that is not marked
    /// once-only appends the same text again every time the party walks over it. Reproduced: the
    /// duplicate is what the reference does, and de-duplicating here would silently change any
    /// design that repeats an event deliberately.
    /// </para>
    /// <para>
    /// <b>The design entry's own <c>origentry</c> is discarded.</b> <c>:27555</c> assigns
    /// <c>data.origentry = data.entry</c> before inserting, so the collected copy remembers the
    /// design key it came from and whatever the table held in that field is overwritten — even
    /// though the field is read back off disk for the design table as well as the party's.
    /// </para>
    /// <para>
    /// <b>The key handed to the store is ignored.</b> <c>JOURNAL_DATA::Add</c> overwrites
    /// <c>entry</c> with a fresh party-local key before inserting, so the collected entry's key
    /// numbers the party's own collection order and bears no relation to the design key — which now
    /// lives in <c>OriginalEntry</c>. The reference mutates the caller's local struct doing it; here
    /// the record is copied instead, which is invisible because that local goes out of scope
    /// immediately.
    /// </para>
    /// <para>
    /// <b>The <c>^</c> tokens are expanded once, at collection time, and the expander is missing.</b>
    /// The reference runs the text through <c>PreProcessText</c>
    /// (<c>UAFWin/FormattedText.cpp:823</c>) and stores the <i>result</i> (<c>:27560</c>), so
    /// <c>^D</c> freezes the day the entry was collected rather than the day it is read, and
    /// <c>StripInvalidChars</c> has already replaced byte 0x80 with a space in what is saved.
    /// <c>PreProcessText</c> is not ported — nothing in this port expands <c>^</c> at all — so the
    /// substitution arrives as a caller-supplied function rather than being approximated here.
    /// Passing an identity function is a real behaviour difference for any design that uses a token,
    /// not a no-op. Two things to know before porting it: the live code looks <c>^a</c>–<c>^z</c> up
    /// in <c>globalData.global_asl</c> (<c>:950</c>) while the function's own header comment says
    /// temp ASL (<c>:815</c>) and its dead length-counting pass used <c>temp_asl</c> (<c>:879</c>) —
    /// the live global lookup is what actually happens; and <c>^10</c>–<c>^12</c> are parsed as
    /// two-digit party slots (<c>:915</c>) while <c>^13</c> onwards is not, falling back to the
    /// single-digit branch and leaving the second digit as literal text.
    /// </para>
    /// </remarks>
    public static JournalOutcome Apply(JournalEvent journal,
                                       IReadOnlyList<JournalEntry> designJournal,
                                       Party party,
                                       Func<string, string> expandTokens)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(designJournal);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(expandTokens);

        if (journal.Entry < 0)
        {
            return NotAdded;
        }

        var source = Lookup(designJournal, journal.Entry);
        if (source is null)
        {
            return NotAdded;
        }

        var collected = source with
        {
            OriginalEntry = source.Entry,                // ":27555 -- save original journal key"
            Text = expandTokens(source.Text),
        };

        int key = party.AddJournalEntry(collected);
        return new JournalOutcome(key >= 0, key);
    }
}
