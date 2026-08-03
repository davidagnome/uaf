using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Copying a design's journal entry into the party's journal (<c>JOURNAL_EVENT</c>).
/// </summary>
/// <remarks>
/// <b>Every expectation below is transcribed, not observed.</b> <c>JournalEvent</c> appears zero
/// times across the six level-bearing corpus designs, and the design journal table it indexes is
/// empty in all five that load. So there is no design anywhere that could disagree with these, and
/// they are only as good as the reading of <c>RunEvent.cpp:27540</c> and <c>Shared/Party.h:99</c>
/// that produced them.
/// </remarks>
public class EventJournalTests
{
    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static JournalEvent Event(int entry) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.JournalEvent, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            entry);

    /// <summary>
    /// A design's journal table as the editor builds it: keys from 1 upwards in authoring order,
    /// with <c>origentry</c> left at the -1 <c>JOURNAL_ENTRY::Clear</c> gives it.
    /// </summary>
    private static List<JournalEntry> Design(params string[] texts)
    {
        var table = new List<JournalEntry>();
        for (int i = 0; i < texts.Length; i++)
        {
            table.Add(new JournalEntry(i + 1, -1, texts[i]));
        }
        return table;
    }

    /// <summary>No <c>^</c> expander exists in this port; the tests supply their own.</summary>
    private static string Verbatim(string text) => text;

    private static JournalOutcome Apply(int entry, IReadOnlyList<JournalEntry> design, Party party,
                                        Func<string, string>? expand = null) =>
        EventJournal.Apply(Event(entry), design, party, expand ?? Verbatim);

    // ---- collecting an entry ---------------------------------------------------------------------

    [Fact]
    public void The_designs_text_is_what_lands_in_the_party_journal()
    {
        // The event carries a key and no words of its own; the text comes from the design's table.
        var party = new Party();

        var outcome = Apply(2, Design("first", "second"), party);

        Assert.True(outcome.Added);
        Assert.Equal("second", Assert.Single(party.Journal).Text);
    }

    [Fact]
    public void The_design_key_becomes_the_original_and_the_party_issues_a_new_one()
    {
        var party = new Party();

        var outcome = Apply(2, Design("first", "second"), party);

        var collected = Assert.Single(party.Journal);
        Assert.Equal(2, collected.OriginalEntry);         // ":27555 -- save original journal key"
        Assert.Equal(1, collected.Entry);                 // JOURNAL_DATA::Add overwrote it
        Assert.Equal(1, outcome.Key);
    }

    [Fact]
    public void The_design_entrys_own_original_key_is_discarded()
    {
        // The field is read off disk for the design table too, and :27555 overwrites it outright.
        var party = new Party();
        var design = new List<JournalEntry> { new(1, 99, "text") };

        Apply(1, design, party);

        Assert.Equal(1, Assert.Single(party.Journal).OriginalEntry);
    }

    [Fact]
    public void Keys_begin_at_one_and_count_up()
    {
        // GetNextKey issues 1 for the first entry and tail+1 after that. Never 0.
        var party = new Party();
        var design = Design("a", "b", "c");

        Assert.Equal(1, Apply(1, design, party).Key);
        Assert.Equal(2, Apply(3, design, party).Key);
        Assert.Equal(3, Apply(2, design, party).Key);

        Assert.Equal([1, 2, 3], party.Journal.Select(e => e.Entry));
        Assert.Equal([1, 3, 2], party.Journal.Select(e => e.OriginalEntry));
    }

    [Fact]
    public void Entries_come_back_in_the_order_they_were_collected()
    {
        // Each new key is one past the tail's, so the key-ordered queue only ever appends.
        var party = new Party();
        var design = Design("a", "b", "c");

        Apply(3, design, party);
        Apply(1, design, party);

        Assert.Equal(["c", "a"], party.Journal.Select(e => e.Text));
    }

    // ---- the three ways to add nothing -----------------------------------------------------------

    [Fact]
    public void A_negative_index_adds_nothing()
    {
        var party = new Party();

        var outcome = Apply(-1, Design("first"), party);

        Assert.False(outcome.Added);
        Assert.Equal(-1, outcome.Key);
        Assert.Empty(party.Journal);
    }

    [Fact]
    public void Index_zero_is_not_caught_by_the_negative_guard_and_fails_the_lookup_instead()
    {
        // Same outcome by a different path, and only because keys start at 1. A port that treated 0
        // as "no entry" would be right by accident.
        var party = new Party();

        Assert.Null(EventJournal.Lookup(Design("first"), 0));
        Assert.False(Apply(0, Design("first"), party).Added);
        Assert.Empty(party.Journal);
    }

    [Fact]
    public void An_index_the_design_does_not_hold_adds_nothing()
    {
        // An event left pointing at a deleted journal entry is completely silent.
        var party = new Party();

        Assert.False(Apply(7, Design("first", "second"), party).Added);
        Assert.Empty(party.Journal);
    }

    [Fact]
    public void An_empty_design_table_adds_nothing_for_any_index()
    {
        // Which is every corpus design: all five that load carry a zero-length journal table.
        var party = new Party();

        Assert.False(Apply(1, [], party).Added);
        Assert.Empty(party.Journal);
    }

    // ---- the reference's own bug -----------------------------------------------------------------

    [Fact]
    public void The_same_entry_can_be_collected_twice()
    {
        // JOURNAL_DATA::HaveGlobalJournalEntryAlready (Shared/Party.h:154) exists to answer exactly
        // this question and is called from nowhere in the tree. OnInitialEvent does not consult it,
        // so an event that is not once-only appends the same text again every time it fires.
        // Reproduced deliberately: de-duplicating would silently change any design that repeats an
        // event on purpose.
        var party = new Party();
        var design = Design("you found a letter");

        Apply(1, design, party);
        Apply(1, design, party);

        Assert.Equal(2, party.Journal.Count);
        Assert.Equal(["you found a letter", "you found a letter"],
                     party.Journal.Select(e => e.Text));
        Assert.Equal([1, 1], party.Journal.Select(e => e.OriginalEntry));
        Assert.Equal([1, 2], party.Journal.Select(e => e.Entry));
    }

    // ---- the ^ token gap -------------------------------------------------------------------------

    [Fact]
    public void The_expander_sees_the_designs_text_and_its_result_is_what_is_stored()
    {
        // PreProcessText (UAFWin/FormattedText.cpp:823) is not ported, so the substitution arrives
        // as a caller-supplied function rather than being approximated here.
        var party = new Party();
        string? seen = null;

        Apply(1, Design("day ^D"), party, text => { seen = text; return "day 12"; });

        Assert.Equal("day ^D", seen);
        Assert.Equal("day 12", Assert.Single(party.Journal).Text);
    }

    [Fact]
    public void Expansion_happens_once_at_collection_time_and_is_never_redone()
    {
        // The reference stores PreProcessText's result (:27560), not the template. So ^D freezes the
        // day the entry was collected rather than the day it is read back.
        var party = new Party();
        var design = Design("day ^D");

        Apply(1, design, party, _ => "day 1");
        Apply(1, design, party, _ => "day 40");

        Assert.Equal(["day 1", "day 40"], party.Journal.Select(e => e.Text));
    }

    [Fact]
    public void An_identity_expander_is_a_real_behaviour_difference_not_a_no_op()
    {
        // Naming the gap rather than papering over it: with no expander the token survives verbatim
        // into the saved text, which is not what the reference stores.
        var party = new Party();

        Apply(1, Design("greetings ^1"), party);

        Assert.Equal("greetings ^1", Assert.Single(party.Journal).Text);
    }

    // ---- the lookup ------------------------------------------------------------------------------

    [Fact]
    public void Lookup_matches_on_the_key_and_not_on_the_position()
    {
        // The design table's keys survive editor deletions, so the third entry is not key 3.
        var design = new List<JournalEntry> { new(1, -1, "a"), new(4, -1, "b"), new(9, -1, "c") };

        Assert.Equal("b", EventJournal.Lookup(design, 4)?.Text);
        Assert.Equal("c", EventJournal.Lookup(design, 9)?.Text);
        Assert.Null(EventJournal.Lookup(design, 2));
        Assert.Null(EventJournal.Lookup(design, 3));
    }

    [Fact]
    public void A_gap_in_the_design_keys_is_a_miss_and_the_event_is_silent()
    {
        var party = new Party();
        var design = new List<JournalEntry> { new(1, -1, "a"), new(9, -1, "c") };

        Assert.False(Apply(5, design, party).Added);
        Assert.True(Apply(9, design, party).Added);
        Assert.Equal("c", Assert.Single(party.Journal).Text);
    }

    // ---- the store -------------------------------------------------------------------------------

    [Fact]
    public void The_store_ignores_the_key_it_is_handed()
    {
        // JOURNAL_DATA::Add assigns GetNextKey() over data.entry before inserting.
        var party = new Party();

        Assert.Equal(1, party.AddJournalEntry(new JournalEntry(500, 7, "text")));
        Assert.Equal(1, Assert.Single(party.Journal).Entry);
        Assert.Equal(7, party.Journal[0].OriginalEntry);
    }

    [Fact]
    public void A_fresh_party_has_an_empty_journal()
    {
        // The reference gates its journal menu item on exactly this (RunEvent.cpp:9232).
        Assert.Empty(new Party().Journal);
    }
}
