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

    private readonly List<CharacterRecord> members = [];

    public IReadOnlyList<CharacterRecord> Members => members;

    public int Count => members.Count;

    /// <summary>Whose turn it is to act, as an index into <see cref="Members"/>.</summary>
    public int ActiveCharacter { get; set; }

    /// <summary>Coins the party holds in common, rather than in a character's own purse.</summary>
    public int MoneyPooled { get; set; }

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

    public void Add(CharacterRecord member)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (members.Count < MaxMembers)
        {
            members.Add(member);
        }
    }

    public void Clear()
    {
        members.Clear();
        ActiveCharacter = 0;
    }

    /// <summary>The character whose turn it is, or null when the party is empty.</summary>
    public CharacterRecord? Active =>
        (uint)ActiveCharacter < (uint)members.Count ? members[ActiveCharacter] : null;

    /// <summary>Whether any member carries an item with this id (<c>PartyHasItem</c>).</summary>
    /// <remarks>
    /// Matches on the item's identifying name. The reference compares an <c>ITEM_ID</c>, which is a
    /// string in the same way <c>SPELL_ID</c> is — the mistake that cost this port a whole record's
    /// alignment when it was first read as an integer.
    /// </remarks>
    public bool HasItem(string itemId) =>
        !string.IsNullOrEmpty(itemId)
        && members.Any(m => m.Items.Items.Any(
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
        && members.Any(m => m.BaseclassStats.Any(
            b => string.Equals(b.BaseclassId, baseclassId, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Whether any member is of this sex (<c>PartyHasGender</c>).</summary>
    public bool HasGender(Gender gender) => members.Any(m => (Gender)m.Gender == gender);

    private bool Any(Func<CharacterRecord, string> field, string value) =>
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
}
