using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// A hit-point bonus applied at each prime (<c>HIT_DICE_LEVEL_BONUS</c>, <c>class.h:1513</c>).
/// </summary>
/// <remarks>
/// <b>The wire order is <c>baseclassID</c> then <c>ability</c></b> (<c>class.cpp:7507</c>) — the
/// struct declares <c>ability</c> first. Both are strings, so transposing them silently swaps two
/// plausible identifiers, exactly as the hit-dice field order does in
/// <see cref="BaseclassRecordReader"/>.
/// </remarks>
public sealed record HitDiceLevelBonus(string BaseclassId, string Ability, byte[] BonusValues);

/// <summary>A <c>CLASS_DATA</c> record (<c>classes.dat</c>).</summary>
/// <param name="Baseclasses">
/// What levelling needs: the baseclasses a character of this class advances in. The experience
/// split divides an award by this count (<c>Char.cpp:5798</c>).
/// </param>
public sealed record ClassRecord(string Tag, int PreSpellNameKey, string Name,
                                 IReadOnlyList<string> Baseclasses,
                                 SpecabBlock SpecialAbilities,
                                 IReadOnlyList<HitDiceLevelBonus> HitDiceLevelBonuses,
                                 DicePlus StrengthBonusDice,
                                 ItemList StartingEquipment,
                                 string HitDiceBaseclassId);

/// <summary>
/// Reads <c>classes.dat</c>'s records (<c>CLASS_DATA::Serialize</c>, <c>class.cpp:7936</c>,
/// loading branch).
/// </summary>
/// <remarks>
/// <para>
/// <b>Only <c>CL5</c> is read.</b> The reference accepts <c>Bc0</c> and <c>CL1</c>–<c>CL5</c>, but
/// the older shapes reach editor-only conversion branches and no available design carries them.
/// Anything else is refused rather than guessed at.
/// </para>
/// <para>
/// <b>This record needs the design version passed in, and <see cref="BaseclassRecordReader"/> does
/// not.</b> Both embed a <c>Specab</c> block, but <c>BASE_CLASS_DATA</c> hard-codes 0.930
/// (<c>class.cpp:6146</c>) while <c>CLASS_DATA</c> uses <c>globalData.version</c> — the real design
/// version — for both its special abilities and its starting equipment. So the same file read
/// against the wrong design version takes the wrong <c>Specab</c> branch, and <c>game.dat</c> must
/// be read first. The two sibling databases genuinely disagree here; it is not an oversight in one
/// of them.
/// </para>
/// <para>
/// <b>The starting-equipment list is not self-contained.</b> The reference resolves every entry
/// against <c>itemData</c> and <i>discards</i> any item the design does not define
/// (<c>Items.cpp:1700</c>, "Undefined item named %s"). This reader keeps them: dropping records
/// while parsing would make the reader's output depend on load order, and the caller can resolve.
/// </para>
/// </remarks>
public static class ClassRecordReader
{
    /// <summary>The only record version this reads.</summary>
    public const string SupportedTag = "CL5";

    /// <summary><c>HIGHEST_CHARACTER_PRIME</c> — the bonus table's length, one byte per entry.</summary>
    public const int BonusValueCount = 25;

    /// <summary>Reads one <c>CLASS_DATA</c> record.</summary>
    /// <param name="version">
    /// The design version from <c>game.dat</c>. Load-bearing — see this class's remarks.
    /// </param>
    /// <exception cref="InvalidDataException">The record is not <see cref="SupportedTag"/>.</exception>
    public static ClassRecord Read(IArchiveCursor ar, DesignVersion version,
                                   ArchiveRole role = ArchiveRole.Editor)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string tag = ar.ReadString();
        if (tag != SupportedTag)
        {
            throw new InvalidDataException(
                $"class record '{tag}' is not {SupportedTag}; only that shape is ported. The "
                + "reference also accepts Bc0 and CL1-CL4, whose conversion paths are editor-only.");
        }

        // CL4 and above only; below that the editor reads it from a different place entirely.
        int preSpellNameKey = ar.ReadInt32();

        string name = ArchiveStringConventions.Decode(ar.ReadString());

        // ReadCount here, but a bare int for the hit-dice bonuses below -- one record, both framings,
        // as in BASE_CLASS_DATA.
        uint baseclassCount = ar.ReadCount();
        var baseclasses = new List<string>((int)baseclassCount);
        for (uint i = 0; i < baseclassCount; i++)
        {
            // BASECLASS_ID::Serialize is a plain string read (class.cpp:7007).
            baseclasses.Add(ArchiveStringConventions.Decode(ar.ReadString()));
        }

        var specabs = SpecabReader.Read(ar, version);

        int bonusCount = ar.ReadInt32();
        var bonuses = new List<HitDiceLevelBonus>(Math.Max(bonusCount, 0));
        for (int i = 0; i < bonusCount; i++)
        {
            string baseclassId = ArchiveStringConventions.Decode(ar.ReadString());
            string ability = ArchiveStringConventions.Decode(ar.ReadString());
            bonuses.Add(new HitDiceLevelBonus(baseclassId, ability, ar.ReadBytes(BonusValueCount)));
        }

        var strengthBonus = DicePlusReader.Read(ar);
        var equipment = MonsterLeafReaders.ReadItemList(ar, version, role);
        string hitDiceBaseclassId = ArchiveStringConventions.Decode(ar.ReadString());

        return new ClassRecord(tag, preSpellNameKey, name, baseclasses, specabs, bonuses,
                               strengthBonus, equipment, hitDiceBaseclassId);
    }

    /// <summary>Reads every record of an already-opened <c>classes.dat</c> body.</summary>
    public static List<ClassRecord> ReadAll(IArchiveCursor ar, uint count, DesignVersion version,
                                            ArchiveRole role = ArchiveRole.Editor)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var records = new List<ClassRecord>((int)count);
        for (uint i = 0; i < count; i++)
        {
            records.Add(Read(ar, version, role));
        }
        return records;
    }
}
