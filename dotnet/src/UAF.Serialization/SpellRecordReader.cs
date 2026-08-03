using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// One of a spell's GPDL scripts as it sits on the wire: the source, and the compiled form beside
/// it.
/// </summary>
/// <remarks>
/// <b>The binary is kept rather than discarded.</b> The reference empties every one of them as it
/// loads (<c>Spell.cpp:4230</c> and around it) to force a recompile, so a file the reference wrote
/// carries empty binaries throughout — but the field is on the wire either way and a writer has to
/// put something back. <see cref="DicePlus.Binary"/> is kept for the same reason.
/// </remarks>
public sealed record SpellScript(string Source, string Binary)
{
    /// <summary>An unused slot: what the reference's default-constructed member writes as.</summary>
    public static SpellScript Empty { get; } = new(string.Empty, string.Empty);
}

/// <summary>
/// The seven script slots, in the order they sit on the wire — which is <b>not</b> version order.
/// </summary>
/// <remarks>
/// See <see cref="SpellRecordReader"/>: the <c>&gt;= 2.6</c> group is written before the
/// <c>&gt;= 1.0303</c> group, so a design between the two carries the last three and not the middle
/// two. <see cref="SpellRecord.Scripts"/> is always all seven regardless, with the slots that
/// version did not have left <see cref="SpellScript.Empty"/> — otherwise a slot's meaning would
/// depend on the version the record was read at, and the writer could not tell which two to skip.
/// </remarks>
public enum SpellScriptSlot
{
    Begin,
    End,
    Initiation,
    Termination,
    SavingThrow,
    SavingThrowSucceeded,
    SavingThrowFailed,
}

/// <summary>One complete <c>SPELL_DATA</c> record.</summary>
/// <param name="Scripts">
/// Always seven entries, indexed by <see cref="SpellScriptSlot"/>.
/// </param>
public sealed record SpellRecord(
    int PreSpellNameKey, string Name, string CastSound, string SchoolId,
    IReadOnlyList<string> AllowedBaseclasses,
    int Level, int CastingTime, int CastingTimeType,
    int CanTargetFriend, int CanTargetEnemy, int IsCumulative, int Restrictions,
    int CanBeDispelled, int CanMemorize, int AllowScribe, int AutoScribe,
    int Lingers, int LingerOnceOnly,
    int SaveVersus, int SaveResult, int Targeting, int DurationRate,
    int CastCost, int CastPriority,
    IReadOnlyList<DicePlus> Parameters, IReadOnlyList<SpellEffect> Effects,
    PicRecord? CastArt, IReadOnlyList<PicRecord> Art,
    IReadOnlyList<string> Sounds, string CastMessage, IReadOnlyList<SpellScript> Scripts,
    DicePlus? EffectDuration,
    SpecabBlock SpecialAbilities, IReadOnlyList<AslEntry> Attributes);

/// <summary>
/// Reads <c>SPELL_DATA</c> as written through <c>CAR</c> (<c>Spell.cpp:3743</c>).
/// </summary>
/// <remarks>
/// <para>
/// The largest record type in the format — noticeably bigger than <c>ITEM_DATA</c>. Most of the
/// bulk is a legacy block of ~35 scalars (<c>Target_</c>, <c>Duration_</c>, <c>Range_</c>,
/// <c>Attack_</c>, <c>Damage_</c>, <c>Protection_</c>, <c>Heal_</c>, five each) that exists only at
/// or below 0.6992, where <see cref="DicePlus"/> expressions replaced them.
/// </para>
/// <para>
/// <b>Wire order follows source order, not version order.</b> In the script block the
/// <c>&gt;= 2.6</c> group is written <i>before</i> the <c>&gt;= 1.0303</c> group
/// (<c>Spell.cpp:4232</c>, <c>:4241</c>). A design at 2.53 therefore reads the second group and not
/// the first, and a reader that sorts these gates by version gets both the order and the count
/// wrong for everything in between.
/// </para>
/// <para>
/// <c>schoolID</c> is a <c>SCHOOL_ID</c>, which derives from <c>CString</c>
/// (<c>Externs.h:1350</c>) — another string that reads like a numeric field.
/// </para>
/// </remarks>
public static class SpellRecordReader
{
    /// <summary>Below this, spell parameters are packed scalars rather than dice expressions.</summary>
    public static DesignVersion DiceParameterGate => DesignVersion.V0670;

    /// <summary>How many <see cref="SpellScriptSlot"/>s a record carries.</summary>
    public const int SpellScriptCount = 7;

    public static SpellRecord Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int preSpellNameKey = -1;
        if (version < DesignVersion.SpellNames || version >= DesignVersion.SaveIDs)
        {
            preSpellNameKey = ar.ReadInt32();
        }

        string name = ReadDas(ar);
        string castSound = ReadDas(ar);

        // Legacy designs stored bitmasks; modern ones store names.
        bool legacyClassMasks = role == ArchiveRole.Editor && version < DesignVersion.SpellNames;

        string schoolId;
        ushort schoolMask = 0;
        if (legacyClassMasks)
        {
            schoolMask = ar.ReadUInt16();                 // WORD, not int
            schoolId = (schoolMask & ClassFlags.MagicUser) != 0 ? "Magic User" : "Cleric";
        }
        else
        {
            schoolId = ar.ReadString();
        }

        var allowedBaseclasses = new List<string>();
        if (version >= DesignVersion.V0910)
        {
            allowedBaseclasses = legacyClassMasks
                ? ReadLegacyCastMask(ar, version, schoolMask)
                : BaseclassListReader.Read(ar);
        }

        int level = ar.ReadInt32();
        int castingTime = ar.ReadInt32();
        int castingTimeType = version >= DesignVersion.V0662 ? ar.ReadInt32() : 0;

        int canTargetFriend = ar.ReadInt32();

        // A bare literal gate with no named constant.
        int canTargetEnemy = version.Value > 0.999725 ? ar.ReadInt32() : 1;

        int isCumulative = version >= DesignVersion.V06991 ? ar.ReadInt32() : 0;
        int restrictions = ar.ReadInt32();

        int canBeDispelled = version >= DesignVersion.V0909 ? ar.ReadInt32() : 0;
        int canMemorize = version >= DesignVersion.V0670 ? ar.ReadInt32() : 0;
        int allowScribe = version >= DesignVersion.V0692 ? ar.ReadInt32() : 0;
        int autoScribe = version >= DesignVersion.V0910 ? ar.ReadInt32() : 0;

        int lingers = 0;
        int lingerOnceOnly = 0;
        if (version >= DesignVersion.V0906)
        {
            lingers = ar.ReadInt32();
            lingerOnceOnly = ar.ReadInt32();
        }

        int saveVersus = ar.ReadInt32();
        int saveResult = ar.ReadInt32();
        int targeting = ar.ReadInt32();

        // The retired packed-scalar blocks. Five fields per group, superseded by DICEPLUS.
        if (version <= DesignVersion.V06992)
        {
            ReadRetiredScalars(ar, 5);                   // Target_*
        }

        int durationRate = ar.ReadInt32();

        if (version <= DesignVersion.V06992)
        {
            // Duration_, Range_, Attack_, Damage_, Protection_, Heal_ -- five each.
            ReadRetiredScalars(ar, 30);
        }

        int castCost = ar.ReadInt32();
        int castPriority = ar.ReadInt32();

        var parameters = new List<DicePlus>();
        var effects = new List<SpellEffect>();
        if (version >= DiceParameterGate)
        {
            parameters.Add(DicePlusReader.Read(ar));     // Duration
            parameters.Add(DicePlusReader.Read(ar));     // P1 (was NumTargets)
            parameters.Add(DicePlusReader.Read(ar));     // P2 (was TargetRange)
            if (version.Value >= 0.999432)
            {
                parameters.Add(DicePlusReader.Read(ar)); // P3
                parameters.Add(DicePlusReader.Read(ar)); // P4
                parameters.Add(DicePlusReader.Read(ar)); // P5
            }

            int effectCount = ar.ReadInt32();
            for (int i = 0; i < effectCount; i++)
            {
                effects.Add(SpellEffectsReader.Read(ar, version));
            }
        }
        else
        {
            throw new NotSupportedException(
                $"Spell version {version} predates {DiceParameterGate} and uses the packed " +
                "parameter layout (Spell.cpp:4113). Not ported: no fixture exercises it.");
        }

        PicRecord? castArt = version >= DesignVersion.V0840
            ? PicDataReader.Read(ar, version, PicArchiveVariant.Car)
            : null;

        // Four more, always present.
        var art = new List<PicRecord>();
        for (int i = 0; i < 4; i++)
        {
            art.Add(PicDataReader.Read(ar, version, PicArchiveVariant.Car));
        }

        var sounds = new List<string>();
        if (version >= DesignVersion.V0840)
        {
            for (int i = 0; i < 4; i++)
            {
                sounds.Add(ReadDas(ar));                 // Missile, Coverage, Hit, Linger
            }
        }

        string castMessage = version >= DesignVersion.V0841 ? ReadDas(ar) : string.Empty;

        // Always seven slots. A version that has fewer leaves the rest empty rather than shortening
        // the list, so an index always means the same script -- see SpellScriptSlot.
        var scripts = new SpellScript[SpellScriptCount];
        Array.Fill(scripts, SpellScript.Empty);
        if (version >= DesignVersion.V0904)
        {
            scripts[(int)SpellScriptSlot.Begin] = ReadScriptPair(ar);
            scripts[(int)SpellScriptSlot.End] = ReadScriptPair(ar);

            // NOTE the order: 2.6 is tested first even though it is the HIGHER version, so this
            // group precedes the 1.0303 group on the wire.
            if (version.Value >= 2.6)
            {
                scripts[(int)SpellScriptSlot.Initiation] = ReadScriptPair(ar);
                scripts[(int)SpellScriptSlot.Termination] = ReadScriptPair(ar);
            }
            if (version.Value >= 1.0303)
            {
                scripts[(int)SpellScriptSlot.SavingThrow] = ReadScriptPair(ar);
                scripts[(int)SpellScriptSlot.SavingThrowSucceeded] = ReadScriptPair(ar);
                scripts[(int)SpellScriptSlot.SavingThrowFailed] = ReadScriptPair(ar);
            }
        }

        DicePlus? effectDuration = version >= DesignVersion.V0906
            ? DicePlusReader.Read(ar)
            : null;

        var specialAbilities = SpecabReader.Read(ar, version);
        var attributes = AslReader.Read(ar, version, AslMaps.SpellData);

        return new SpellRecord(
            preSpellNameKey, name, castSound, schoolId, allowedBaseclasses,
            level, castingTime, castingTimeType, canTargetFriend, canTargetEnemy,
            isCumulative, restrictions, canBeDispelled, canMemorize, allowScribe, autoScribe,
            lingers, lingerOnceOnly, saveVersus, saveResult, targeting, durationRate,
            castCost, castPriority, parameters, effects, castArt, art,
            sounds, castMessage, scripts, effectDuration, specialAbilities, attributes);
    }

    /// <summary>
    /// Reads the pre-<c>VersionSpellNames</c> <c>castMask</c> and expands it into baseclass names
    /// (<c>Spell.cpp:3922</c>).
    /// </summary>
    /// <remarks>
    /// A single <c>WORD</c> on the wire, so the cost of getting this wrong is two bytes — but it is
    /// the branch the oracle's own fixture takes, since DefaultDesign is 0.915: above the 0.910
    /// gate that introduced the field, and below the 0.998101 that replaced it with a name list.
    /// <para>
    /// Below 0.930 the stored mask is not trusted: magic-user spells are forced to magic-user only,
    /// and for anything else the magic-user bit is stripped, falling back to cleric if that
    /// emptied the mask. The comment in the reference is blunt about why — the flag "is certainly
    /// not going to be correct for older designs".
    /// </para>
    /// </remarks>
    private static List<string> ReadLegacyCastMask(IArchiveCursor ar, DesignVersion version,
                                                   ushort schoolMask)
    {
        int castMask = ar.ReadUInt16();

        if (version < DesignVersion.V0930)
        {
            if (schoolMask == ClassFlags.MagicUser)
            {
                castMask = ClassFlags.MagicUser;
            }
            else
            {
                // Magic-user and cleric are mutually exclusive.
                castMask &= ~ClassFlags.MagicUser;
                if (castMask == 0)
                {
                    castMask = ClassFlags.Cleric;
                }
            }
        }

        // Order matters: it is the order the reference inserts them.
        var names = new List<string>();
        foreach ((int flag, string name) in ClassFlags.InSerializedOrder)
        {
            if ((castMask & flag) != 0)
            {
                names.Add(name);
            }
        }
        return names;
    }

    /// <summary>
    /// Reads a source/binary script pair. The reference clears the binary after loading; this keeps
    /// it — see <see cref="SpellScript"/>.
    /// </summary>
    private static SpellScript ReadScriptPair(IArchiveCursor ar) =>
        new(ReadDas(ar), ReadDas(ar));

    private static void ReadRetiredScalars(IArchiveCursor ar, int count)
    {
        for (int i = 0; i < count; i++)
        {
            ar.ReadInt32();
        }
    }

    /// <summary>
    /// Reads a whole <c>spells.dat</c> payload (<c>SPELL_DATA_TYPE::Serialize</c>,
    /// <c>Spell.cpp:6910</c>): a count then the records. No trailing list, unlike
    /// <c>items.dat</c>.
    /// </summary>
    public static List<SpellRecord> ReadDatabase(IArchiveCursor ar, DesignVersion version,
                                                 ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var spells = new List<SpellRecord>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            spells.Add(Read(ar, version, role));
        }
        return spells;
    }

    public static List<SpellRecord> ReadDatabase(MfcArchiveReader ar, DesignVersion version,
                                                 ArchiveRole role) =>
        ReadDatabase(ArchiveCursor.For(ar), version, role);

    public static List<SpellRecord> ReadDatabase(CarArchiveReader ar, DesignVersion version,
                                                 ArchiveRole role) =>
        ReadDatabase(ArchiveCursor.For(ar), version, role);

    private static string ReadDas(IArchiveCursor ar) =>
        ArchiveStringConventions.Decode(ar.ReadString());
}

/// <summary>
/// The seven original class bits, and the baseclass names legacy masks expand into
/// (<c>GameRules.cpp:2220</c>, <c>Spell.cpp:3951</c>).
/// </summary>
public static class ClassFlags
{
    public const int MagicUser = 1;
    public const int Cleric = 2;
    public const int Thief = 4;
    public const int Fighter = 8;
    public const int Paladin = 16;
    public const int Ranger = 32;
    public const int Druid = 64;

    /// <summary>Flag/name pairs in the order the reference inserts them.</summary>
    public static readonly (int Flag, string Name)[] InSerializedOrder =
    [
        (MagicUser, "magicUser"), (Cleric, "cleric"), (Thief, "thief"), (Fighter, "fighter"),
        (Paladin, "paladin"), (Ranger, "ranger"), (Druid, "druid"),
    ];
}
