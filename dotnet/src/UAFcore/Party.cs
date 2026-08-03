using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>A character's sex (<c>genderType</c>, <c>Shared/GameRules.h:104</c>).</summary>
/// <remarks>
/// Three values, not two. <c>Bishop</c> is the original's own name for the third and is a real
/// selectable value, so a two-valued model would drop it.
/// </remarks>
public enum Gender
{
    Male = 0,
    Female = 1,
    Bishop = 2,
}

/// <summary>
/// The adventuring party, as the engine holds it while a design runs (<c>PARTY</c>,
/// <c>Shared/Party.h</c>).
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="UAF.Serialization.PartyState"/>, which is the record a savegame
/// carries. This is the live thing the engine asks questions of; that is a snapshot of it.
/// </para>
/// <para>
/// <b>Scope: what the trigger conditions and the roster need.</b> The roster, the flags an event
/// can ask about, and the pooled money. Inventory is present only as far as
/// <see cref="HasItem"/> needs — each character's own <c>ItemList</c>, already parsed — because
/// giving and taking items is a rules question rather than an engine one.
/// </para>
/// </remarks>
public sealed class Party
{
    /// <summary>
    /// <c>MAX_PARTY_MEMBERS</c> (<c>Externs.h:929</c>) — the number of slots a savegame writes.
    /// </summary>
    /// <remarks>
    /// Twelve, not the six a party fields at once. The savegame reader had to get this right for a
    /// different reason: it writes twelve records regardless of how many are occupied.
    /// </remarks>
    public const int MaxMembers = SaveGameReader.MaxPartyMembers;

    private readonly List<Character> members = [];

    public IReadOnlyList<Character> Members => members;

    /// <summary>
    /// The party's own attribute store (<c>party_asl</c>), which a design's scripts read and write
    /// through <c>$SET_PARTY_ASL</c> and its neighbours.
    /// </summary>
    /// <remarks>
    /// Starts empty: unlike the global store, nothing in a design seeds it — it exists purely for
    /// state a script puts there during play.
    /// </remarks>
    public AttributeList Attributes { get; } = new();

    public int Count => members.Count;

    /// <summary>Whose turn it is to act, as an index into <see cref="Members"/>.</summary>
    public int ActiveCharacter { get; set; }

    /// <summary>Coins the party holds in common, rather than in a character's own purse.</summary>
    /// <remarks>
    /// The original's <c>PARTY::moneyPooled</c> is a flag saying whether money is pooled, not an
    /// amount — the coins themselves live in <see cref="Pooled"/>. Kept as the flag it is.
    /// </remarks>
    public int MoneyPooled { get; set; }

    /// <summary>The party's common purse.</summary>
    /// <remarks>
    /// Where treasure lands when it is not given to one character. Built against the design's own
    /// currency, so a design with its own denominations is respected.
    /// </remarks>
    public Purse Pooled { get; init; } = new(MoneyRules.Default);

    /// <summary>Items the party has picked up during play.</summary>
    /// <remarks>
    /// <b>A party-level list, where the original gives items to a character.</b>
    /// <c>GIVE_TREASURE_DATA</c> calls <c>dude.myItems.AddItem</c> on the active character, but a
    /// character's inventory here is still the record read off disk rather than live state. Held
    /// separately from a character's own inventory, and <see cref="HasItem"/> searches both --
    /// so a trigger asking whether the party holds something finds a treasure pickup too.
    /// </remarks>
    public List<ItemInstance> Carried { get; } = [];

    /// <summary>Whether the party is searching as it moves (<c>PartyIsSearching</c>).</summary>
    public bool Searching { get; set; }

    /// <summary>
    /// A separate flag the searching condition also accepts.
    /// </summary>
    /// <remarks>
    /// <c>PartySearching</c> is <c>party.PartyIsSearching(); shouldTrigger |= party.looking;</c>
    /// (<c>GameEvent.cpp:888</c>) — an OR. <b><c>PartyNotSearching</c> does not mirror it</b>: it
    /// is the plain negation of <c>PartyIsSearching()</c> and ignores <c>looking</c> entirely. So a
    /// party that is looking but not searching satisfies <i>both</i> conditions at once.
    /// </remarks>
    public bool Looking { get; set; }

    public bool DetectingTraps { get; set; }

    public bool DetectingInvisible { get; set; }

    public void Add(Character member)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (members.Count < MaxMembers)
        {
            members.Add(member);
        }
    }

    /// <summary>
    /// Drops a member (<c>PARTY::removeCharacter</c>).
    /// </summary>
    /// <remarks>
    /// <b>The active character is pulled back if it would be left past the end.</b> Removing the
    /// last member of a party whose active index pointed at it would otherwise leave
    /// <see cref="Active"/> reading off the end — and the index is what TAB cycles and what every
    /// "who tries"/"who pays" event reads.
    /// </remarks>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= members.Count)
        {
            return;
        }

        members.RemoveAt(index);

        if (ActiveCharacter >= members.Count)
        {
            ActiveCharacter = Math.Max(members.Count - 1, 0);
        }
    }

    public void Clear()
    {
        members.Clear();
        ActiveCharacter = 0;
    }

    /// <summary>The character whose turn it is, or null when the party is empty.</summary>
    public Character? Active =>
        (uint)ActiveCharacter < (uint)members.Count ? members[ActiveCharacter] : null;

    /// <summary>Whether any member carries an item with this id (<c>PartyHasItem</c>).</summary>
    /// <remarks>
    /// Matches on the item's identifying name. The reference compares an <c>ITEM_ID</c>, which is a
    /// string in the same way <c>SPELL_ID</c> is — the mistake that cost this port a whole record's
    /// alignment when it was first read as an integer.
    /// </remarks>
    public bool HasItem(string itemId) =>
        !string.IsNullOrEmpty(itemId)
        && (members.Any(m => m.Items.Any(
                i => string.Equals(i.ItemId, itemId, StringComparison.OrdinalIgnoreCase)))
            || Carried.Any(
                i => string.Equals(i.ItemId, itemId, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Whether any member has this class (<c>PartyHasClass</c>).</summary>
    public bool HasClass(string classId) => Any(m => m.ClassId, classId);

    /// <summary>Whether any member has this race (<c>PartyHasRace</c>).</summary>
    public bool HasRace(string raceId) => Any(m => m.Race, raceId);

    /// <summary>Whether any member is this named character (<c>PartyHasNPC</c>).</summary>
    public bool HasCharacter(string characterId) => Any(m => m.CharacterId, characterId);

    /// <summary>Whether any member has this baseclass (<c>PartyHasBaseclass</c>).</summary>
    /// <remarks>
    /// A character's baseclasses come from its <c>BaseclassStats</c> list rather than a single
    /// field, because a multiclass character has several — which is why this is not the same
    /// question as <see cref="HasClass"/>.
    /// </remarks>
    public bool HasBaseclass(string baseclassId) =>
        !string.IsNullOrEmpty(baseclassId)
        && members.Any(m => m.Baseclass(baseclassId) is not null);

    /// <summary>Whether any member is of this sex (<c>PartyHasGender</c>).</summary>
    public bool HasGender(Gender gender) => members.Any(m => m.Gender == gender);

    private bool Any(Func<Character, string> field, string value) =>
        !string.IsNullOrEmpty(value)
        && members.Any(m => string.Equals(field(m), value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the clock reads daytime (<c>PartyInDaytime</c>, <c>Party.cpp:1161</c>).
    /// </summary>
    /// <param name="hours">The hour of day, 0–23.</param>
    /// <remarks>
    /// <b>Inclusive at both ends</b> — <c>hours &gt;= 6 &amp;&amp; hours &lt;= 18</c> — so the hour
    /// beginning at 18:00 is still day and the daylight span is thirteen hours, not twelve. An
    /// exclusive upper bound is the natural guess and makes 18:30 night.
    /// </remarks>
    public static bool InDaytime(int hours) => hours is >= 6 and <= 18;

    // ---- the journal (PARTY::journal, Shared/Party.h:643) ---------------------------------------

    /// <summary>
    /// <c>MAX_JOURNAL_ENTRIES</c> (<c>Shared/Party.h:32</c>) — the ceiling
    /// <c>JOURNAL_DATA::Add</c> refuses to cross.
    /// </summary>
    /// <remarks>
    /// 0x00FFFFFF, which is a cap on the <i>count</i> and not on the key. It exists so the key
    /// space stays strictly larger than the population, which is what lets <c>GetNextKey</c>'s wrap
    /// branch (<c>Shared/Party.h:68</c>) assume a gap it can reuse. That branch needs a key at
    /// <c>INT_MAX</c> to run and so is unreachable; the cap itself is transcribed because it is the
    /// third of the three ways a journal event adds nothing.
    /// </remarks>
    public const int MaxJournalEntries = 0x00FFFFFF;

    private readonly List<JournalEntry> journal = [];

    /// <summary>
    /// The journal entries the party has collected, oldest first (<c>PARTY::journal</c>,
    /// <c>Shared/Party.h:643</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Party state, not world state</b>, on the reference's own evidence: this is written inside
    /// <c>PARTY::Serialize</c> (<c>Shared/Party.cpp:926</c>), between the pooled money sack and
    /// <c>party_asl</c>, while quests, special items and keys are written after PARTY
    /// (<c>UAFWin/Dgngame.cpp:188</c>) and live on <see cref="WorldState"/>. Filled by
    /// <see cref="EventJournal"/>, which is the only thing in the engine that adds to it.
    /// </para>
    /// <para>
    /// Each entry's <c>Entry</c> is this party's own collection key and its <c>OriginalEntry</c> is
    /// the design key it was copied from — the reverse of the design's own table, where the two
    /// agree.
    /// </para>
    /// <para>
    /// Distinct from the design's authored journal text, which is
    /// <see cref="UAF.Serialization.GlobalStatsPrefix.Journal"/> and is read-only.
    /// </para>
    /// </remarks>
    public IReadOnlyList<JournalEntry> Journal => journal;

    /// <summary>
    /// Collects one entry, giving it the next party-local key (<c>JOURNAL_DATA::Add</c>,
    /// <c>Shared/Party.h:99</c>).
    /// </summary>
    /// <returns>The key assigned, or -1 when the journal is full and nothing was added.</returns>
    /// <remarks>
    /// <b>The caller's key is overwritten, not honoured</b> — <c>Add</c> assigns
    /// <c>GetNextKey()</c> before inserting, so passing a design key here does not preserve it. Keys
    /// begin at 1, never 0.
    /// <para>
    /// The reference holds these in a key-ordered queue and inserts by key
    /// (<c>OrderedQueue::Insert</c>, <c>Shared/SharedQueue.h:990</c>), but the new key is always one
    /// past the tail's, so every insert lands at the end and a plain append is the same list. That
    /// equivalence depends on this being the only way in: anything that later loads a saved journal
    /// must add it in ascending key order, or the next key stops being one past the highest.
    /// </para>
    /// </remarks>
    public int AddJournalEntry(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (journal.Count >= MaxJournalEntries)
        {
            return -1;
        }

        int key = NextJournalKey();
        journal.Add(entry with { Entry = key });
        return key;
    }

    /// <summary><c>JOURNAL_DATA::GetNextKey</c> (<c>Shared/Party.h:61</c>).</summary>
    private int NextJournalKey() => journal.Count == 0 ? 1 : journal[^1].Entry + 1;
}
