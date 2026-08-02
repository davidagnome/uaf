using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// What an attribute's flags mean (<c>ASLF_*</c>, <c>ASL.h:144</c>).
/// </summary>
/// <remarks>
/// Two of the four are documented as "info only. Not used" in the header, and are transcribed for
/// completeness rather than because anything reads them.
/// </remarks>
[Flags]
public enum AttributeFlags
{
    None = 0,

    /// <summary>
    /// Cannot be deleted, changed or saved, and cannot be created during play.
    /// </summary>
    /// <remarks>
    /// <b>The load-bearing one.</b> It decides what a save game holds and what survives a restore
    /// — see <see cref="AttributeList.Saveable"/> and <see cref="AttributeList.CommitRestore"/>.
    /// The header notes it implies <see cref="Design"/>.
    /// </remarks>
    ReadOnly = 1,

    /// <summary>
    /// Changed during play. <b>The first insertion during play does not set it</b> — the header
    /// says so explicitly, and it is the caller's job either way.
    /// </summary>
    Modified = 2,

    /// <summary>Created by the editor. "Info only. Not used."</summary>
    Design = 4,

    /// <summary>A system attribute. "Info only. Not used."</summary>
    System = 8,
}

/// <summary>
/// A named store of strings that a design's scripts read and write
/// (<c>A_ASLENTRY_L</c>, <c>ASL.h:95</c>).
/// </summary>
/// <remarks>
/// <para>
/// The engine keeps several of these — one global, one per character, one per event, one per item —
/// and they are how a design records state that outlives a single script. The combat results screen
/// writes <c>"Combat Result"</c> into the global one, which is how a design branches on whether the
/// party won (see <see cref="CombatAftermath"/>).
/// </para>
/// <para>
/// A B-tree in the reference, ordered by key; a dictionary here, with the order exposed sorted so
/// enumeration is stable. Nothing depends on the tree's shape.
/// </para>
/// </remarks>
public sealed class AttributeList
{
    private readonly Dictionary<string, AslEntry> entries = new(StringComparer.Ordinal);

    /// <summary>Every attribute, ordered by key as the reference's tree is.</summary>
    public IEnumerable<AslEntry> Entries =>
        entries.Values.OrderBy(e => e.Key, StringComparer.Ordinal);

    public int Count => entries.Count;

    /// <summary>
    /// Adds or replaces an attribute (<c>Insert</c>, <c>ASL.cpp:1285</c>).
    /// </summary>
    /// <returns>
    /// <b>Whether the key was already there</b> — true for a replacement, false for a new entry.
    /// The reference returns <c>TRUE</c> for the overwrite case, which reads backwards from
    /// "did it work"; kept, because callers that test it are testing for a pre-existing value.
    /// </returns>
    /// <remarks>
    /// <b>An existing entry's flags are replaced too</b>, not merged — so inserting over a
    /// read-only attribute with no flags makes it writable. The reference does not guard that, and
    /// neither does this.
    /// </remarks>
    public bool Insert(string key, string value, AttributeFlags flags = AttributeFlags.None)
    {
        ArgumentNullException.ThrowIfNull(key);

        bool existed = entries.ContainsKey(key);
        entries[key] = new AslEntry(key, (byte)flags, value);
        return existed;
    }

    /// <summary>The value stored under a key, or null.</summary>
    public string? Find(string key) =>
        entries.TryGetValue(key, out var entry) ? entry.Value : null;

    /// <summary>The whole entry, or null.</summary>
    public AslEntry? Entry(string key) =>
        entries.TryGetValue(key, out var entry) ? entry : null;

    /// <summary>
    /// Removes an attribute (<c>Delete</c>, <c>ASL.cpp:1547</c>).
    /// </summary>
    /// <returns>The value it held, or null when there was none.</returns>
    /// <remarks>
    /// <b>Read-only is not enforced here.</b> The flag's own comment says such an attribute "can't
    /// be deleted", but <c>Delete</c> takes a key and removes whatever it finds — the protection
    /// lives in the callers and in the save path, not in the container.
    /// </remarks>
    public string? Remove(string key)
    {
        if (!entries.Remove(key, out var entry))
        {
            return null;
        }

        return entry.Value;
    }

    public void Clear() => entries.Clear();

    /// <summary>
    /// What a save game holds (<c>Serialize</c>, <c>ASL.cpp:1489</c>).
    /// </summary>
    /// <remarks>
    /// <b>Everything except read-only.</b> A read-only attribute comes from the design and is
    /// reloaded with it, so storing it would only let a stale copy override the design later.
    /// </remarks>
    public IEnumerable<AslEntry> Saveable =>
        Entries.Where(e => ((AttributeFlags)e.Flags & AttributeFlags.ReadOnly) == 0);

    /// <summary>
    /// Replaces the saveable half from a restored list (<c>CommitRestore</c>,
    /// <c>ASL.cpp:1516</c>).
    /// </summary>
    /// <remarks>
    /// <b>Drop every non-read-only entry first, then take the source's non-read-only entries.</b>
    /// Both halves of that matter: the discard is what stops a key the save game no longer has
    /// from lingering, and the filter on the way in is what stops a save game overriding the
    /// design's read-only values.
    /// </remarks>
    public void CommitRestore(IEnumerable<AslEntry> restored)
    {
        ArgumentNullException.ThrowIfNull(restored);

        foreach (var stale in Saveable.ToList())
        {
            entries.Remove(stale.Key);
        }

        foreach (var entry in restored)
        {
            if (((AttributeFlags)entry.Flags & AttributeFlags.ReadOnly) == 0)
            {
                entries[entry.Key] = entry;
            }
        }
    }

    /// <summary>Loads a design's attributes, as read off the wire.</summary>
    public void Load(IEnumerable<AslEntry> design)
    {
        ArgumentNullException.ThrowIfNull(design);

        foreach (var entry in design)
        {
            entries[entry.Key] = entry;
        }
    }

    /// <summary>
    /// The key the combat results screen writes the verdict into
    /// (<c>RunEvent.cpp:19742</c>).
    /// </summary>
    /// <remarks>
    /// Spelled with a space, and a design tests it by that exact name. See
    /// <see cref="CombatAftermath.ResultText"/> for the four values it takes.
    /// </remarks>
    public const string CombatResultKey = "Combat Result";
}
