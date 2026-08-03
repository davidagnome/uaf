namespace UAF.Serialization;

/// <summary>
/// Writes the leaf structures a <c>CHARACTER</c> record depends on that nothing else had needed —
/// the spellbook, the blockage list and the three tagged adjustment lists.
/// </summary>
/// <remarks>
/// <para>
/// <b>None of these storing branches has a version gate</b>, as everywhere else on the write side.
/// <c>spellLimitsType</c> is the extreme case: its storing branch is one live statement and five
/// commented-out ones (<c>GameRules.cpp:3611</c>), against a loading branch with a whole
/// pre-0.780 <c>BYTE</c> matrix in it.
/// </para>
/// <para>
/// The spellbook is not character-specific — <c>TEMPLE</c>, <c>SHOP</c> and the global
/// <c>fixSpellBook</c> all carry one — so <see cref="WriteSpellBook"/> is here only because a
/// character is the first thing that can be written.
/// </para>
/// </remarks>
public static class CharacterLeafWriters
{
    /// <summary>
    /// Writes a <c>spellBookType</c> (<c>Spell.cpp:2325</c>): the casting limits, then the list.
    /// </summary>
    /// <remarks>
    /// <b>The whole of <c>spellLimits</c> is one <c>int</c>.</b> Everything else the structure
    /// once wrote — the per-baseclass prime scores and the limits array — is commented out, so the
    /// modern form on the wire is <c>UseLimits</c> and nothing more.
    /// </remarks>
    public static void WriteSpellBook(IArchiveWriteCursor ar, SpellBook book)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(book);

        ar.WriteInt32(book.UseLimits);

        ar.WriteInt32(book.Spells.Count);
        foreach (var spell in book.Spells)
        {
            // Verbatim: a SPELL_ID, which is a string above VersionSpellNames and a numeric key
            // below it. Only the string form can be written -- see CharacterRecordWriter.
            ar.WriteString(spell.SpellId);
            ar.WriteInt32(spell.Memorized);
            ar.WriteInt32(spell.Level);
            ar.WriteInt32(spell.Selected);
        }
    }

    /// <summary>
    /// Writes a <c>BLOCKAGE_STATUS</c> (<c>Char.cpp:464</c>): a count then the entries.
    /// </summary>
    /// <remarks>
    /// <b>It is a list, not a record.</b> The member is called <c>blockageData</c> and reads like a
    /// single one, but its type is <c>BLOCKAGE_STATUS</c> (<c>Char.h:1398</c>) — the mistake that
    /// cost the reader 14 bytes against a 4-byte count of zero. <c>Stats.StatsFull</c> is a
    /// <c>WORD</c> of sixteen cleared-flag bits, not an <c>int</c>.
    /// </remarks>
    public static void WriteBlockages(IArchiveWriteCursor ar, IReadOnlyList<BlockageData> blockages)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(blockages);

        ar.WriteInt32(blockages.Count);
        foreach (var blockage in blockages)
        {
            ar.WriteInt32(blockage.Level);
            ar.WriteInt32(blockage.X);
            ar.WriteInt32(blockage.Y);
            ar.WriteUInt16(blockage.Stats);          // WORD
        }
    }

    /// <summary>The version tag a <c>BASECLASS_STATS</c> list opens with (<c>Char.cpp:2624</c>).</summary>
    public const string BaseclassStatsTag = "BS0";

    /// <summary>
    /// The version tag <b>both</b> adjustment lists open with (<c>Char.cpp:2636</c>, <c>:2648</c>).
    /// </summary>
    /// <remarks>
    /// The skill and spell lists share the literal — the reference declares a local called
    /// <c>SAVersion</c> in each block and gives both the same value. So the tag does not identify
    /// which list follows, only how its rows are laid out; position does the rest.
    /// </remarks>
    public const string AdjustmentTag = "SA0";

    /// <summary>Writes the tagged <c>BASECLASS_STATS</c> list (<c>class.cpp:4801</c>).</summary>
    /// <remarks>
    /// <b>Each of these three lists carries its own string version tag</b>, like the tagged
    /// databases — a second self-versioning scheme layered inside a numerically versioned record.
    /// Only the tag the reference writes today can be produced, which is the general rule again.
    /// </remarks>
    public static void WriteBaseclassStats(IArchiveWriteCursor ar,
                                           IReadOnlyList<BaseclassStats> stats)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(stats);

        ar.WriteString(BaseclassStatsTag);
        ar.WriteInt32(stats.Count);
        foreach (var row in stats)
        {
            ar.WriteString(row.BaseclassId);         // verbatim: a BASECLASS_ID
            ar.WriteInt32(row.CurrentLevel);
            ar.WriteInt32(row.PreviousLevel);
            ar.WriteInt32(row.PreDrainLevel);
            ar.WriteInt32(row.Experience);
        }
    }

    /// <summary>Writes the tagged skill-adjustment list.</summary>
    /// <remarks><c>type</c> is a <c>char</c> — one byte, not an <c>int</c>.</remarks>
    public static void WriteSkillAdjustments(IArchiveWriteCursor ar,
                                             IReadOnlyList<SkillAdjustment> adjustments)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(adjustments);

        ar.WriteString(AdjustmentTag);
        ar.WriteInt32(adjustments.Count);
        foreach (var row in adjustments)
        {
            ar.WriteString(row.SkillId);
            ar.WriteString(row.AdjustmentId);
            ar.WriteInt32(row.Value);
            ar.WriteByte((byte)row.Type);            // char, not int
        }
    }

    /// <summary>Writes the tagged spell-adjustment list.</summary>
    public static void WriteSpellAdjustments(IArchiveWriteCursor ar,
                                             IReadOnlyList<SpellAdjustment> adjustments)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(adjustments);

        ar.WriteString(AdjustmentTag);
        ar.WriteInt32(adjustments.Count);
        foreach (var row in adjustments)
        {
            ar.WriteString(row.SchoolId);
            ar.WriteString(row.AdjustmentId);
            ar.WriteInt32(row.FirstLevel);
            ar.WriteInt32(row.LastLevel);
            ar.WriteInt32(row.Percent);
            ar.WriteInt32(row.Bonus);
        }
    }
}
