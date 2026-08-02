namespace UAFcore;

/// <summary>
/// The spells a character may still learn, kept as one packed string in its attribute store
/// (<c>AddKnowableSpell</c> and friends, <c>Char.cpp:1145</c>).
/// </summary>
/// <remarks>
/// <para>
/// The only live use of the per-character attribute list in the whole engine — everything else it
/// holds is design data nothing reads back. The list is stored under the key
/// <see cref="Key"/> as a bare concatenation of <c>?name</c> entries, so
/// <c>?magic missile?sleep</c> is two spells.
/// </para>
/// <para>
/// <b>The packing is where the traps are.</b> A concatenation with a leading delimiter and no
/// terminator means the last entry is shaped differently from the rest, and membership is tested
/// by substring rather than by splitting.
/// </para>
/// </remarks>
public static class KnowableSpells
{
    /// <summary>The attribute key, dollar-wrapped as the engine's own names are.</summary>
    public const string Key = "$KnowableSpells$";

    /// <summary>The delimiter, which prefixes every entry rather than separating them.</summary>
    public const char Delimiter = '?';

    /// <summary>
    /// Adds a spell to the list (<c>AddKnowableSpell</c>).
    /// </summary>
    /// <param name="alreadyKnown">
    /// Whether the character already has the spell in its book, in which case there is nothing to
    /// learn and the call does nothing.
    /// </param>
    /// <returns>Whether the list changed.</returns>
    /// <remarks>
    /// <b>Membership is a substring test, not an entry test</b> — <c>list.Find("?" + name)</c> —
    /// so a spell whose entry is a prefix of another's silently fails to be added. With
    /// <c>?Fireball</c> already in the list, adding <c>Fire</c> finds <c>?Fire</c> inside it and
    /// refuses. Reproduced: it is the storage format's own consequence, and a design's spell names
    /// were chosen against it.
    /// </remarks>
    public static bool Add(AttributeList attributes, string spellName, bool alreadyKnown = false)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (alreadyKnown)
        {
            return false;
        }

        string entry = Delimiter + spellName;
        string list = attributes.Find(Key) ?? string.Empty;

        if (list.Length == 0)
        {
            list = entry;
        }
        else if (list.Contains(entry, StringComparison.Ordinal))
        {
            return false;
        }
        else
        {
            list += entry;
        }

        attributes.Insert(Key, list, AttributeFlags.Modified);
        return true;
    }

    /// <summary>
    /// Removes a spell from the list (<c>DelKnowableSpell</c>).
    /// </summary>
    /// <returns>Whether the list changed.</returns>
    /// <remarks>
    /// <b>Two branches, and the first exists only because of the packing.</b> The last entry has
    /// nothing after it, so it is matched as a <i>suffix</i> of the whole string; every other entry
    /// is matched as <c>?name?</c> — with the following delimiter — and the removal deliberately
    /// leaves that trailing delimiter behind, because it belongs to the entry after it.
    /// <para>
    /// A name that is not there leaves the list alone, and a list shorter than the entry is
    /// rejected before either branch runs.
    /// </para>
    /// </remarks>
    public static bool Remove(AttributeList attributes, string spellName)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        string list = attributes.Find(Key) ?? string.Empty;
        string entry = Delimiter + spellName;

        if (list.Length < entry.Length)
        {
            return false;
        }

        bool removed = false;

        if (list.EndsWith(entry, StringComparison.Ordinal))
        {
            list = list[..^entry.Length];
            removed = true;
        }
        else
        {
            string bounded = entry + Delimiter;
            int at = list.IndexOf(bounded, StringComparison.Ordinal);
            if (at >= 0)
            {
                // Keep the trailing delimiter: it introduces the entry that follows.
                list = list[..at] + list[(at + bounded.Length - 1)..];
                removed = true;
            }
        }

        if (removed)
        {
            attributes.Insert(Key, list, AttributeFlags.Modified);
        }

        return removed;
    }

    /// <summary>Empties the list (<c>ClrKnowableSpell</c>).</summary>
    /// <remarks>
    /// <b>The reference deletes the attribute and then returns <c>false</c> unconditionally</b>,
    /// where its two siblings return whether anything changed. Nothing reads the result, so the
    /// inconsistency is invisible; this returns whether there was a list to clear, which is what
    /// the name promises.
    /// </remarks>
    public static bool Clear(AttributeList attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return attributes.Remove(Key) is not null;
    }

    /// <summary>The spell names in the list, unpacked.</summary>
    /// <remarks>
    /// Not a function the reference has — it never unpacks the list, only searches it as a string.
    /// Provided because everything reading the list in this port wants the names.
    /// </remarks>
    public static IEnumerable<string> All(AttributeList attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return (attributes.Find(Key) ?? string.Empty)
            .Split(Delimiter, StringSplitOptions.RemoveEmptyEntries);
    }
}
