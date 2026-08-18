using UAF.Common;

namespace UAF.Serialization;

/// <summary>A character's progress in one baseclass.</summary>
public sealed record BaseclassStats(
    string BaseclassId, int CurrentLevel, int PreviousLevel, int PreDrainLevel, int Experience);

/// <summary>An adjustment to one skill.</summary>
public sealed record SkillAdjustment(string SkillId, string AdjustmentId, int Value, sbyte Type);

/// <summary>An adjustment to spellcasting in one school.</summary>
public sealed record SpellAdjustment(
    string SchoolId, string AdjustmentId, int FirstLevel, int LastLevel, int Percent, int Bonus);

/// <summary>Where the party stands relative to a blockage, and which flags it has cleared.</summary>
public sealed record BlockageData(int Level, int X, int Y, ushort Stats);

/// <summary>The seven ability scores.</summary>
public sealed record AbilityScores(
    int Strength, int StrengthMod, int Intelligence, int Wisdom,
    int Dexterity, int Constitution, int Charisma);

/// <summary>One complete <c>CHARACTER</c> record.</summary>
/// <param name="PreSpellNamesKey">
/// Read only when the opener held a character version, and written unconditionally — so it is kept
/// rather than discarded, for the writer to put back.
/// </param>
public sealed record CharacterRecord(
    int CharacterVersion, int PreSpellNamesKey,
    byte Type, string Race, int Gender, string ClassId, int Alignment,
    int AllowInCombat, int Status, string UndeadType, int CreatureSize,
    string Name, string CharacterId,
    int Thac0, int Morale, int Encumbrance, int MaxEncumbrance, int ArmorClass,
    int HitPoints, int MaxHitPoints, double NumberOfHitDice,
    int Age, int MaxAge, int Birthday, int MaxCureDisease,
    int UnarmedDieSmall, int UnarmedNumberDieSmall, int UnarmedBonus,
    int UnarmedDieLarge, int UnarmedNumberDieLarge,
    byte MaxMovement, int ReadyToTrain, int CanTradeItems,
    AbilityScores Abilities,
    byte OpenDoors, byte OpenMagicDoors, byte BendBarsLiftGates,
    int HitBonus, int DamageBonus, int MagicResistance,
    IReadOnlyList<BaseclassStats> BaseclassStats,
    IReadOnlyList<SkillAdjustment> SkillAdjustments,
    IReadOnlyList<SpellAdjustment> SpellAdjustments,
    int IsPreGenerated, int CanBeSaved, int HasLayedOnHandsToday,
    MoneySack? Money, float NumberOfAttacks,
    PicRecord? Icon, int IconIndex, int OriginalIndex, byte UniquePartyId,
    int DisableTalkIfDead, uint TalkEvent, string TalkLabel,
    uint ExamineEvent, string ExamineLabel,
    SpellBook SpellBook, int DetectingInvisible, int DetectingTraps,
    IReadOnlyList<SpellEffect> SpellEffects, IReadOnlyList<BlockageData> Blockages,
    PicRecord SmallPic, ItemList Items,
    SpecabBlock SpecialAbilities, IReadOnlyList<AslEntry> Attributes);

/// <summary>
/// Reads <c>CHARACTER</c> (<c>Char.cpp:2540</c>) — the largest record in the format.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first field is a discriminator, not a value.</b> An <c>int</c> is read, and if its high
/// bit is set it is a <i>character version</i>; otherwise it is a legacy key and the version is
/// zero (<c>Char.cpp:2691</c>). Only the version case reads the following
/// <c>preSpellNamesKey</c>. Nothing else in the format self-identifies this way.
/// </para>
/// <para>
/// It also carries an unusual density of width traps: <c>nbrHitDice</c> is a <c>double</c>,
/// <c>NbrAttacks</c> a <c>float</c>, and <c>type</c>, <c>maxMovement</c>, <c>uniquePartyID</c>,
/// <c>openDoors</c>, <c>openMagicDoors</c> and <c>BB_LG</c> are all <c>BYTE</c>s among <c>int</c>s.
/// </para>
/// <para>
/// The seven ability scores change width at 0.999702 — <c>BYTE</c> below, <c>int</c> at and above
/// — a 21-byte difference in the middle of the record.
/// </para>
/// </remarks>
public static class CharacterReader
{
    /// <summary>Set in the first field when it holds a character version rather than a key.</summary>
    public const uint CharacterVersionFlag = 0x80000000;

    /// <summary>Retired fields the editor skips below <c>VersionSpellNames</c>.</summary>
    public const int LegacyTrashFields = 13;

    public static CharacterRecord Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role,
                                       PicArchiveVariant pics = PicArchiveVariant.Car)
    {
        ArgumentNullException.ThrowIfNull(ar);

        bool legacyIds = role == ArchiveRole.Editor && version < DesignVersion.SpellNames;

        // The discriminated opener.
        uint first = ar.ReadUInt32();
        int characterVersion = 0;
        int preSpellNamesKey = 0;
        if ((first & CharacterVersionFlag) != 0)
        {
            characterVersion = (int)first;
            if (version.Value >= 0.998917)
            {
                preSpellNamesKey = ar.ReadInt32();
            }
        }

        byte type = ar.ReadByte();                       // BYTE
        string race = ar.ReadString();
        int gender = ar.ReadInt32();
        string classId = ar.ReadString();
        int alignment = ar.ReadInt32();

        int allowInCombat = version >= DesignVersion.V0912 ? ar.ReadInt32() : 0;
        int status = ar.ReadInt32();

        string undeadType;
        if (role == ArchiveRole.Editor && version.Value <= 0.998115)
        {
            // The reference names the index from UndeadTypeText as it loads (Char.cpp:2727), the
            // same as it does for a monster. Keeping the ordinal would put "1" into a modern file
            // where the design means "Skeleton", and no turning table has a category called "1".
            undeadType = MonsterRecordReader.UndeadTypeName(ar.ReadInt32());
        }
        else
        {
            undeadType = ar.ReadString();
        }

        int creatureSize = ar.ReadInt32();
        string name = ReadDas(ar);

        string characterId = string.Empty;
        if (role != ArchiveRole.Editor || version >= DesignVersion.SpellNames)
        {
            characterId = ar.ReadString();
        }

        int thac0 = ar.ReadInt32();
        int morale = ar.ReadInt32();
        int encumbrance = ar.ReadInt32();
        int maxEncumbrance = ar.ReadInt32();
        int armorClass = ar.ReadInt32();
        int hitPoints = ar.ReadInt32();
        int maxHitPoints = ar.ReadInt32();
        double numberOfHitDice = ar.ReadDouble();        // double, not int

        int age = ar.ReadInt32();
        int maxAge = ar.ReadInt32();
        int birthday = version >= DesignVersion.V0830 ? ar.ReadInt32() : 0;

        int maxCureDisease = ar.ReadInt32();
        int unarmedDieSmall = ar.ReadInt32();
        int unarmedNumberDieSmall = ar.ReadInt32();
        int unarmedBonus = ar.ReadInt32();
        int unarmedDieLarge = ar.ReadInt32();
        int unarmedNumberDieLarge = ar.ReadInt32();

        byte maxMovement = ar.ReadByte();                // BYTE
        int readyToTrain = ar.ReadInt32();
        int canTradeItems = version >= DesignVersion.V0695 ? ar.ReadInt32() : 0;

        var abilities = ReadAbilities(ar, version);

        byte openDoors = ar.ReadByte();                  // three BYTEs in a row
        byte openMagicDoors = ar.ReadByte();
        byte bendBarsLiftGates = ar.ReadByte();

        int hitBonus = ar.ReadInt32();
        int damageBonus = ar.ReadInt32();
        int magicResistance = ar.ReadInt32();

        if (legacyIds)
        {
            for (int i = 0; i < LegacyTrashFields; i++) ar.ReadInt32();
        }

        var baseclassStats = ReadBaseclassStats(ar);
        var skillAdjustments = ReadSkillAdjustments(ar);

        var spellAdjustments = new List<SpellAdjustment>();
        if (role != ArchiveRole.Editor || version.Value >= 0.9984)
        {
            spellAdjustments = ReadSpellAdjustments(ar);
        }

        int isPreGenerated = ar.ReadInt32();
        int canBeSaved = version >= DesignVersion.V0698 ? ar.ReadInt32() : 0;
        int hasLayedOnHandsToday = version >= DesignVersion.V0900 ? ar.ReadInt32() : 0;

        MoneySack? money;
        if (version < DesignVersion.V0661)
        {
            // Loose coin counts, folded into a sack after reading. Five types, then gems and
            // jewellery counts, then five more coin types from 0.660.
            for (int i = 0; i < 7; i++) ar.ReadInt32();
            if (version >= DesignVersion.V0660)
            {
                for (int i = 0; i < 5; i++) ar.ReadInt32();
            }
            money = null;
        }
        else
        {
            money = MonsterLeafReaders.ReadMoneySack(ar, version);
        }

        float numberOfAttacks = ar.ReadSingle();         // float, not int

        PicRecord? icon = null;
        if (version < DesignVersion.V0640)
        {
            ReadDas(ar);                                 // legacy icon filename
            ar.ReadInt32();                              // notused
        }
        else
        {
            icon = PicDataReader.Read(ar, version, pics);
        }

        int iconIndex = version >= DesignVersion.V0640 ? ar.ReadInt32() : 0;
        int originalIndex = ar.ReadInt32();
        byte uniquePartyId = ar.ReadByte();              // BYTE

        int disableTalkIfDead = version >= DesignVersion.V0870 ? ar.ReadInt32() : 0;
        uint talkEvent = version >= DesignVersion.V0662 ? ar.ReadUInt32() : 0;
        string talkLabel = version >= DesignVersion.V0710 ? ReadDas(ar) : string.Empty;

        uint examineEvent = 0;
        string examineLabel = string.Empty;
        if (version >= DesignVersion.V0800)
        {
            examineEvent = ar.ReadUInt32();
            examineLabel = ReadDas(ar);
        }

        var spellBook = MoreEventReaders.ReadSpellBook(ar, version, role);

        int detectingInvisible = 0, detectingTraps = 0;
        if (version >= DesignVersion.V06991)
        {
            if (version < DesignVersion.V0850) ar.ReadInt32();   // unused1
            detectingInvisible = ar.ReadInt32();
            detectingTraps = ar.ReadInt32();
        }

        // Both branches are the same on the wire: a count then that many effects.
        var spellEffects = new List<SpellEffect>();
        if (version >= DesignVersion.V0630)
        {
            int count = ar.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                spellEffects.Add(SpellEffectsReader.Read(ar, version));
            }
        }

        // BLOCKAGE_STATUS is a LIST of BlockageDataType, not a single one -- the member is called
        // blockageData but its type is BLOCKAGE_STATUS (Char.h:1398). Reading it as one record
        // consumed 14 bytes where the file had a 4-byte count of zero.
        var blockages = new List<BlockageData>();
        if (version >= DesignVersion.V0696)
        {
            int blockageCount = ar.ReadInt32();
            for (int i = 0; i < blockageCount; i++)
            {
                blockages.Add(new BlockageData(
                    ar.ReadInt32(), ar.ReadInt32(), ar.ReadInt32(),
                    ar.ReadUInt16()));                   // a WORD of 16 flags
            }
        }

        // Outside the storing/loading branch.
        var smallPic = PicDataReader.Read(ar, version, pics);
        var items = MonsterLeafReaders.ReadItemList(ar, version, role);
        var specialAbilities = SpecabReader.Read(ar, version);
        var attributes = AslReader.Read(ar, version, AslMaps.Character);

        return new CharacterRecord(
            characterVersion, preSpellNamesKey,
            type, race, gender, classId, alignment, allowInCombat, status,
            undeadType, creatureSize, name, characterId,
            thac0, morale, encumbrance, maxEncumbrance, armorClass,
            hitPoints, maxHitPoints, numberOfHitDice, age, maxAge, birthday, maxCureDisease,
            unarmedDieSmall, unarmedNumberDieSmall, unarmedBonus,
            unarmedDieLarge, unarmedNumberDieLarge,
            maxMovement, readyToTrain, canTradeItems, abilities,
            openDoors, openMagicDoors, bendBarsLiftGates,
            hitBonus, damageBonus, magicResistance,
            baseclassStats, skillAdjustments, spellAdjustments,
            isPreGenerated, canBeSaved, hasLayedOnHandsToday, money, numberOfAttacks,
            icon, iconIndex, originalIndex, uniquePartyId,
            disableTalkIfDead, talkEvent, talkLabel, examineEvent, examineLabel,
            spellBook, detectingInvisible, detectingTraps, spellEffects, blockages,
            smallPic, items, specialAbilities, attributes);
    }

    /// <summary>
    /// Reads the seven ability scores, whose width changes at 0.999702.
    /// </summary>
    private static AbilityScores ReadAbilities(IArchiveCursor ar, DesignVersion version)
    {
        if (version.Value < 0.999702)
        {
            return new AbilityScores(
                ar.ReadByte(), ar.ReadByte(), ar.ReadByte(), ar.ReadByte(),
                ar.ReadByte(), ar.ReadByte(), ar.ReadByte());
        }

        return new AbilityScores(
            ar.ReadInt32(), ar.ReadInt32(), ar.ReadInt32(), ar.ReadInt32(),
            ar.ReadInt32(), ar.ReadInt32(), ar.ReadInt32());
    }

    /// <summary>
    /// Reads a string-versioned list of <c>BASECLASS_STATS</c> (<c>class.cpp:4801</c>).
    /// </summary>
    /// <remarks>
    /// Each of these three lists opens with its <b>own string version tag</b>, like the tagged
    /// databases — a second self-versioning scheme layered inside a numerically versioned record.
    /// </remarks>
    private static List<BaseclassStats> ReadBaseclassStats(IArchiveCursor ar)
    {
        ar.ReadString();                                 // per-list version tag
        int count = ar.ReadInt32();

        var stats = new List<BaseclassStats>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            stats.Add(new BaseclassStats(
                ar.ReadString(), ar.ReadInt32(), ar.ReadInt32(), ar.ReadInt32(), ar.ReadInt32()));
        }
        return stats;
    }

    private static List<SkillAdjustment> ReadSkillAdjustments(IArchiveCursor ar)
    {
        ar.ReadString();
        int count = ar.ReadInt32();

        var adjustments = new List<SkillAdjustment>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            adjustments.Add(new SkillAdjustment(
                ar.ReadString(), ar.ReadString(), ar.ReadInt32(),
                (sbyte)ar.ReadByte()));                  // type is a char, not an int
        }
        return adjustments;
    }

    private static List<SpellAdjustment> ReadSpellAdjustments(IArchiveCursor ar)
    {
        ar.ReadString();
        int count = ar.ReadInt32();

        var adjustments = new List<SpellAdjustment>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            adjustments.Add(new SpellAdjustment(
                ar.ReadString(), ar.ReadString(),
                ar.ReadInt32(), ar.ReadInt32(), ar.ReadInt32(), ar.ReadInt32()));
        }
        return adjustments;
    }

    /// <summary>Reads a <c>CHAR_LIST</c> (<c>Char.cpp:9531</c>): a count then the characters.</summary>
    public static List<CharacterRecord> ReadList(IArchiveCursor ar, DesignVersion version,
                                                 ArchiveRole role,
                                                 PicArchiveVariant pics = PicArchiveVariant.Car)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var characters = new List<CharacterRecord>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            characters.Add(Read(ar, version, role, pics));
        }
        return characters;
    }

    private static string ReadDas(IArchiveCursor ar) =>
        ArchiveStringConventions.Decode(ar.ReadString());
}
