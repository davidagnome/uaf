using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Which build's serialization behaviour to reproduce.
/// </summary>
/// <remarks>
/// The two builds differ in which <b>version range</b> they support, not in how they read a file
/// both accept. `ITEM_DATA::Serialize(CAR&amp;)` gates `HitArt`/`MissileArt` behind
/// <c>#ifdef UAFEDITOR</c> (<c>Items.cpp:2784</c>): the editor skips them at or below 0.998100,
/// the engine reads them unconditionally. That looks like a divergence, but the engine
/// <b>refuses</b> any design below 0.998101 (<c>Level.cpp:3365</c>) — so every version it accepts
/// is above the gate and both builds read the art.
/// <para>
/// An audit of all 59 inline conditional blocks that touch the archive found every one to be
/// editor-only, gated on <c>version &lt; VersionSpellNames</c>: legacy-conversion reads the engine
/// never reaches. So the role distinguishes <see cref="Editor"/> (legacy-capable, 0.500 → 5.29)
/// from <see cref="Engine"/> (modern-only, ≥ 0.998101).
/// </para>
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

/// <summary>
/// Everything from <c>Wpn_Type</c> to the end of the record, including the two structures that
/// terminate it.
/// </summary>
public sealed record ItemTail(
    int WeaponType, int UsageFlags, int LegacyUsableByClass,
    IReadOnlyList<string> UsableByBaseclass, int RangeMax, uint UseEvent, uint ExamineEvent,
    string ExamineLabel, string AttackMessage, int RechargeRate, int IsNonLethal,
    PicRecord? HitArt, int CanBeHalvedJoined, int CanBeTradeDropSoldDep,
    SpecabBlock SpecialAbilities, IReadOnlyList<AslEntry> Attributes);

/// <summary>One complete <c>ITEM_DATA</c> record.</summary>
public sealed record ItemRecord(
    ItemNames Names, PicRecord? HitArt, PicRecord? MissileArt,
    ItemScalars Scalars, ItemCombat Combat, ItemTail Tail);

/// <summary>
/// The whole of <c>items.dat</c>: every record, plus the ammo-type list that follows them.
/// </summary>
/// <remarks>
/// The trailing list is easy to overlook — it sits after the record loop rather than inside it
/// (<c>Items.cpp:3091</c>), so a reader that stops at the last record looks complete and leaves
/// the file's final bytes unconsumed.
/// </remarks>
public sealed record ItemDatabase(
    IReadOnlyList<ItemRecord> Items, IReadOnlyList<string> AmmoTypes);

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

    /// <summary>
    /// The six-character word each ordinal maps to, blank-padded (<c>Items.h:110-120</c>).
    /// </summary>
    private static readonly string[] LegacyWords =
    [
        "WEAPON", "SHIELD", "ARMOR ", "HANDS ", "HEAD  ",
        "WAIST ", "ROBE  ", "CLOAK ", "FEET  ", "FINGER", "QUIVER",
    ];

    /// <summary>True when the stored value is a legacy ordinal needing conversion.</summary>
    public static bool IsLegacyOrdinal(uint stored) => stored < (uint)LegacyOrder.Length;

    /// <summary>
    /// Packs a six-character word into the base-38 <c>DWORD</c> the field actually holds
    /// (<c>Items.h:105-106</c>).
    /// </summary>
    /// <remarks>
    /// Letters encode as <c>'A' → 12 … 'Z' → 37</c> and a space as 1, then the six digits are
    /// folded most-significant first. Nothing about the resulting number is human-readable, which
    /// is why comparing it against the oracle is worth doing rather than eyeballing it.
    /// </remarks>
    public static uint Base38(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length != 6)
        {
            throw new ArgumentException("A base-38 name is exactly six characters.", nameof(word));
        }

        uint value = 0;
        foreach (char c in word)
        {
            // `blank` is defined as 'A'-11, so it lands on 1 after the +12 shift.
            uint digit = c == ' ' ? 1u : (uint)(c - 'A' + 12);
            value = (value * 38) + digit;
        }
        return value;
    }

    /// <summary>
    /// Applies the loading branch's conversion (<c>Items.cpp:2820</c>): a small ordinal becomes
    /// its base-38 name, anything else passes through unchanged.
    /// </summary>
    public static uint Convert(uint stored) =>
        IsLegacyOrdinal(stored) ? Base38(LegacyWords[stored]) : stored;
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
    public static ItemNames ReadNames(IArchiveCursor ar, DesignVersion version)
    {
        // Items.cpp:2753 -- read outside the [SpellNames, SaveIDs) window, defaulted inside it.
        int preSpellNameKey = -1;
        if (version < DesignVersion.SpellNames || version >= DesignVersion.SaveIDs)
        {
            preSpellNameKey = ar.ReadInt32();
        }

        // Items.cpp:2761. spellID is a STRING, not an int: SPELL_ID derives from CString
        // (Externs.h:1324), so `ar >> spellID` takes the string path. The name reads like an
        // identifier and it sits among integers, which is why this was mis-modelled twice before
        // the oracle settled it -- both wrong versions still produced printable output.
        // 0.999647 is a bare literal in the C++ with no named constant -- one of several gate
        // values that exist only as inline numbers, which is why DesignVersion.All must never be
        // treated as the set of valid versions.
        if (version.Value >= 0.999647)
        {
            ar.ReadString();
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
    public static ItemScalars ReadScalars(IArchiveCursor ar, DesignVersion version)
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
    public static ItemCombat ReadCombat(IArchiveCursor ar)
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

    // Convenience overloads for the uncompressed reader. Note this does NOT mean the plain
    // CArchive field order -- these follow Items.cpp:2677 (the CAR overload) throughout, because
    // an archive with compressType 0 or 1 still runs the CAR code path, just without LZW.
    public static ItemNames ReadNames(MfcArchiveReader ar, DesignVersion version) =>
        ReadNames(ArchiveCursor.For(ar), version);

    public static ItemScalars ReadScalars(MfcArchiveReader ar, DesignVersion version) =>
        ReadScalars(ArchiveCursor.For(ar), version);

    public static ItemCombat ReadCombat(MfcArchiveReader ar) =>
        ReadCombat(ArchiveCursor.For(ar));

    /// <summary>
    /// True when this role and version take the pre-<c>VersionSpellNames</c> conversion branches
    /// — a single <c>Usable_by_Class</c> bitmask instead of a baseclass-name array, and three
    /// extra spell fields after <c>RangeMax</c>.
    /// </summary>
    /// <remarks>
    /// Both branches are <c>#ifdef UAFEDITOR</c>, so the engine always takes the modern path. It
    /// would anyway: it refuses designs below 0.998101, and the gate is exactly that version.
    /// </remarks>
    public static bool UsesLegacyUsability(ArchiveRole role, DesignVersion version) =>
        role == ArchiveRole.Editor && version < DesignVersion.SpellNames;

    /// <summary>
    /// Reads everything from <c>Wpn_Type</c> to the end of the record, including the special
    /// abilities and attribute list that terminate it (<c>Items.cpp:2857-2944</c>).
    /// </summary>
    public static ItemTail ReadTail(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int weaponType = ar.ReadInt32();
        int usageFlags = ar.ReadInt32();

        int legacyUsableByClass = 0;
        var usableByBaseclass = new List<string>();
        if (UsesLegacyUsability(role, version))
        {
            // A bitmask of the seven original classes, kept for later conversion.
            legacyUsableByClass = ar.ReadInt32();
        }
        else
        {
            int count = ar.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                // BASECLASS_ID derives from CString (Externs.h:1222), so this is a STRING -- the
                // same trap as SPELL_ID, and just as invisible if read as an int.
                usableByBaseclass.Add(ar.ReadString());
            }
        }

        int rangeMax = ar.ReadInt32();

        if (UsesLegacyUsability(role, version))
        {
            ar.ReadInt32();          // preVersionSpellNames_gsID -- int (Items.h:832)
            ar.ReadInt32();          // junk
            ar.ReadInt32();          // junk
        }

        uint useEvent = version >= DesignVersion.V0662 ? ar.ReadUInt32() : 0;

        uint examineEvent = 0;
        string examineLabel = string.Empty;
        if (version >= DesignVersion.V0800)
        {
            examineEvent = ar.ReadUInt32();
            examineLabel = ArchiveStringConventions.Decode(ar.ReadString());
        }

        // Items.cpp:2872 defaults this rather than leaving it empty.
        string attackMessage = "attacks";
        if (version >= DesignVersion.V0860)
        {
            attackMessage = ArchiveStringConventions.Decode(ar.ReadString());
        }

        int rechargeRate = 0;
        int isNonLethal = 0;
        PicRecord? hitArt = null;
        if (version >= DesignVersion.V0690)
        {
            rechargeRate = ar.ReadInt32();
            isNonLethal = ar.ReadInt32();

            // HitArt a SECOND time. The earlier art block (gated on ReadsHitAndMissileArt) reads
            // HitArt and MissileArt together; this one re-reads HitArt alone. Not a typo in the
            // reference -- both are on the wire, and skipping either desynchronises the record.
            hitArt = PicDataReader.Read(ar, version, PicArchiveVariant.Car);
        }

        // Both default to TRUE when absent (Items.cpp:2884, :2889), not to zero.
        int canBeHalvedJoined = version >= DesignVersion.V0881 ? ar.ReadInt32() : 1;
        int canBeTradeDropSoldDep = version >= DesignVersion.V0904 ? ar.ReadInt32() : 1;

        // The record tail proper, in this order (Items.cpp:2939-2944).
        var specialAbilities = SpecabReader.Read(ar, version);
        var attributes = AslReader.Read(ar, version, AslMaps.ItemData);

        return new ItemTail(weaponType, usageFlags, legacyUsableByClass, usableByBaseclass,
                            rangeMax, useEvent, examineEvent, examineLabel, attackMessage,
                            rechargeRate, isNonLethal, hitArt, canBeHalvedJoined,
                            canBeTradeDropSoldDep, specialAbilities, attributes);
    }

    /// <summary>Reads one complete <c>ITEM_DATA</c> record.</summary>
    public static ItemRecord ReadRecord(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var names = ReadNames(ar, version);

        PicRecord? hitArt = null;
        PicRecord? missileArt = null;
        if (ReadsHitAndMissileArt(role, version))
        {
            hitArt = PicDataReader.Read(ar, version, PicArchiveVariant.Car);
            missileArt = PicDataReader.Read(ar, version, PicArchiveVariant.Car);
        }

        var scalars = ReadScalars(ar, version);
        var combat = ReadCombat(ar);
        var tail = ReadTail(ar, version, role);

        return new ItemRecord(names, hitArt, missileArt, scalars, combat, tail);
    }

    /// <summary>
    /// Reads a whole <c>items.dat</c> payload: the record count, the records, then the ammo-type
    /// list (<c>ITEM_DATA_TYPE::Serialize</c>, <c>Items.cpp:3078</c>).
    /// </summary>
    /// <remarks>
    /// Call this rather than looping over <see cref="ReadRecord(IArchiveCursor, DesignVersion,
    /// ArchiveRole)"/>: the trailing ammo-type list is part of the payload, and leaving it unread
    /// means the stream does not land on EOF — the one cheap check that every field width in
    /// every record was right.
    /// </remarks>
    public static ItemDatabase ReadDatabase(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var items = new List<ItemRecord>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            items.Add(ReadRecord(ar, version, role));
        }

        var ammoTypes = new List<string>();
        if (version >= DesignVersion.V0690)
        {
            int ammoCount = ar.ReadInt32();
            for (int i = 0; i < ammoCount; i++)
            {
                ammoTypes.Add(ArchiveStringConventions.Decode(ar.ReadString()));
            }
        }

        return new ItemDatabase(items, ammoTypes);
    }

    public static ItemDatabase ReadDatabase(MfcArchiveReader ar, DesignVersion version, ArchiveRole role) =>
        ReadDatabase(ArchiveCursor.For(ar), version, role);

    public static ItemDatabase ReadDatabase(CarArchiveReader ar, DesignVersion version, ArchiveRole role) =>
        ReadDatabase(ArchiveCursor.For(ar), version, role);

    public static ItemRecord ReadRecord(MfcArchiveReader ar, DesignVersion version, ArchiveRole role) =>
        ReadRecord(ArchiveCursor.For(ar), version, role);

    public static ItemRecord ReadRecord(CarArchiveReader ar, DesignVersion version, ArchiveRole role) =>
        ReadRecord(ArchiveCursor.For(ar), version, role);
}
