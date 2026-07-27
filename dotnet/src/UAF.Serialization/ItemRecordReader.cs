using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Which build's serialization behaviour to reproduce.
/// </summary>
/// <remarks>
/// The reference implementation's format <b>forks by build</b>. `ITEM_DATA::Serialize(CAR&amp;)`
/// gates `HitArt`/`MissileArt` behind <c>#ifdef UAFEDITOR</c> (<c>Items.cpp:2784</c>): the editor
/// skips them at or below 0.998100, the engine always reads them. The two builds therefore
/// consume different byte counts from the same file. Since UAFcore and UAFedit share this
/// library, the role must be explicit rather than assumed.
/// <para>
/// Note the C++ oracle can only ever validate <see cref="Editor"/>, because the dumper is built
/// into UAFWinEd.
/// </para>
/// </remarks>
public enum ArchiveRole
{
    Engine,
    Editor,
}

/// <summary>The leading, name-bearing portion of an <c>ITEM_DATA</c> record.</summary>
public sealed record ItemNames(int PreSpellNameKey, string UniqueName, string IdName,
                               string HitSound, string MissSound, string LaunchSound);

/// <summary>
/// The scalar block following the art, read as <c>long</c>/<c>BOOL</c> — all 4 bytes on Win32.
/// </summary>
public sealed record ItemScalars(string AmmoType, int Experience, int Cost, int Encumbrance,
                                 int AttackBonus, int Cursed, int BundleQty, int NumCharges);

/// <summary>Combat block: readied location, hands, damage dice, rate of fire, protection.</summary>
public sealed record ItemCombat(uint LocationReadied, int HandsToUse,
                                int DmgDiceSm, int NbrDiceSm, int DmgBonusSm,
                                int DmgDiceLg, int NbrDiceLg, int DmgBonusLg,
                                double RofPerRound, int ProtectionBase, int ProtectionBonus);

/// <summary>
/// Base-38 readied-location names that <c>Items.cpp:2820</c> maps old numeric codes onto.
/// </summary>
/// <remarks>
/// The stored <c>DWORD</c> is <b>not</b> the value the member ends up holding: legacy designs
/// wrote small ordinals (0 = weapon hand, 1 = shield hand, …) which the loading branch rewrites
/// into base-38 encoded names. A reader that keeps the raw ordinal disagrees with the reference
/// on every old design, with no error to indicate it.
/// </remarks>
public static class ReadiedLocation
{
    /// <summary>Ordinals 0..10 as written by pre-conversion designs, in switch order.</summary>
    public static readonly string[] LegacyOrder =
    [
        "WeaponHand", "ShieldHand", "BodyArmor", "Hands", "Head",
        "Waist", "BodyRobe", "Back", "Feet", "Fingers", "AmmoQuiver",
    ];

    /// <summary>True when the stored value is a legacy ordinal needing conversion.</summary>
    public static bool IsLegacyOrdinal(uint stored) => stored < (uint)LegacyOrder.Length;
}

/// <summary>
/// Reads <c>ITEM_DATA</c> records as written through <c>CAR</c> (<c>Items.cpp:2749</c>).
/// </summary>
/// <remarks>
/// <b>This is not interchangeable with the <c>CArchive</c> overload</b> (<c>Items.cpp:2341</c>).
/// The two have different version gates — most visibly <c>preSpellNameKey</c>, which the
/// <c>CArchive</c> path reads only below 0.576 while the <c>CAR</c> path reads it across the
/// whole range outside <c>[VersionSpellNames, VersionSaveIDs)</c>. Using the wrong order
/// desynchronises the first record and every string after it comes back as garbage.
/// </remarks>
public static class ItemRecordReader
{
    /// <summary>
    /// Reads the leading fields of one record, up to and including <c>LaunchSound</c>.
    /// </summary>
    /// <remarks>
    /// Stops there deliberately: what follows is <c>HitArt</c>/<c>MissileArt</c>, whose presence
    /// depends on <see cref="ArchiveRole"/>, and then ~40 further version-gated fields. The names
    /// alone are enough to prove stream alignment, which is what this is for.
    /// </remarks>
    public static ItemNames ReadNames(MfcArchiveReader ar, DesignVersion version)
    {
        // Items.cpp:2753 -- read outside the [SpellNames, SaveIDs) window, defaulted inside it.
        int preSpellNameKey = -1;
        if (version < DesignVersion.SpellNames || version >= DesignVersion.SaveIDs)
        {
            preSpellNameKey = ar.ReadInt32();
        }

        // Items.cpp:2761 -- only on designs newer than any currently available fixture.
        if (version.Value >= 0.999647)
        {
            ar.ReadInt32();   // spellID
        }

        string uniqueName = ArchiveStringConventions.Decode(ar.ReadString());
        string idName = ArchiveStringConventions.Decode(ar.ReadString());
        string hitSound = ArchiveStringConventions.Decode(ar.ReadString());
        string missSound = ArchiveStringConventions.Decode(ar.ReadString());

        string launchSound = string.Empty;
        if (version >= DesignVersion.V05691)
        {
            launchSound = ArchiveStringConventions.Decode(ar.ReadString());
        }

        return new ItemNames(preSpellNameKey, uniqueName, idName, hitSound, missSound, launchSound);
    }

    /// <summary>
    /// True when this role and version read <c>HitArt</c>/<c>MissileArt</c> at this point in the
    /// record. <c>Items.cpp:2784</c>.
    /// </summary>
    public static bool ReadsHitAndMissileArt(ArchiveRole role, DesignVersion version) =>
        role == ArchiveRole.Engine || version > DesignVersion.SpellIDs;

    /// <summary>
    /// Reads the scalar block that follows the art (<c>Items.cpp:2804</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call only after <see cref="ReadsHitAndMissileArt"/> has been honoured — when it returns
    /// true, two <c>PIC_DATA</c> records sit between the sounds and this block.
    /// </para>
    /// <para>
    /// <c>Experience</c> through <c>Num_Charges</c> are declared <c>long</c> and <c>Cursed</c> is
    /// <c>BOOL</c> (<c>Items.h:120-126</c>) — all 4 bytes on Win32. <c>Cursed</c> is kept as
    /// <c>int</c> rather than <c>bool</c>: the <c>AutoDarkenAmount</c> precedent shows this
    /// codebase stores non-boolean values in <c>BOOL</c> fields, and narrowing would be lossy.
    /// </para>
    /// </remarks>
    public static ItemScalars ReadScalars(MfcArchiveReader ar, DesignVersion version)
    {
        string ammoType = string.Empty;
        if (version >= DesignVersion.V0690)
        {
            ammoType = ArchiveStringConventions.Decode(ar.ReadString());
        }
        // Items.cpp:2807 normalises "None" to empty after reading. Reproduced so the value
        // matches what the oracle reports rather than what the bytes literally say.
        if (string.Equals(ammoType, "None", StringComparison.OrdinalIgnoreCase))
        {
            ammoType = string.Empty;
        }

        return new ItemScalars(
            ammoType,
            Experience: ar.ReadInt32(),
            Cost: ar.ReadInt32(),
            Encumbrance: ar.ReadInt32(),
            AttackBonus: ar.ReadInt32(),
            Cursed: ar.ReadInt32(),
            BundleQty: ar.ReadInt32(),
            NumCharges: ar.ReadInt32());
    }

    /// <summary>
    /// Reads the combat block that follows the scalars (<c>Items.cpp:2817</c>).
    /// </summary>
    /// <remarks>
    /// <c>ROF_Per_Round</c> is a <c>double</c> in the middle of <c>long</c>s — 8 bytes where the
    /// neighbours are 4. <c>Location_Readied</c> is returned <b>raw</b>; the legacy-ordinal
    /// conversion is exposed via <see cref="ReadiedLocation"/> rather than applied here, so the
    /// caller can compare either form against the oracle.
    /// </remarks>
    public static ItemCombat ReadCombat(MfcArchiveReader ar)
    {
        uint locationReadied = ar.ReadUInt32();
        return new ItemCombat(
            locationReadied,
            HandsToUse: ar.ReadInt32(),
            DmgDiceSm: ar.ReadInt32(),
            NbrDiceSm: ar.ReadInt32(),
            DmgBonusSm: ar.ReadInt32(),
            DmgDiceLg: ar.ReadInt32(),
            NbrDiceLg: ar.ReadInt32(),
            DmgBonusLg: ar.ReadInt32(),
            RofPerRound: ar.ReadDouble(),      // 8 bytes among 4-byte neighbours
            ProtectionBase: ar.ReadInt32(),
            ProtectionBonus: ar.ReadInt32());
    }
}
