using UAF.Common;

namespace UAF.Serialization;

/// <summary>One complete <c>MONSTER_DATA</c> record.</summary>
public sealed record MonsterRecord(
    int PreSpellNameKey, string Name, PicRecord? Icon, string LegacyIconFile,
    string HitSound, string MissSound, string MoveSound, string DeathSound,
    int Intelligence, int ArmorClass, int Movement, float HitDice, int UseHitDice,
    int HitDiceBonus, int Thac0,
    IReadOnlyList<AttackDetails> Attacks,
    int MagicResistance, int Size, string ClassId, int Morale, int ExperienceValue,
    uint FormType, uint PenaltyType, uint ImmunityType, uint MiscOptionsType,
    string UndeadType,
    SpecabBlock SpecialAbilities, IReadOnlyList<AslEntry> Attributes,
    ItemList? Items, MoneySack? Money);

/// <summary>
/// Reads <c>MONSTER_DATA</c> as written through <c>CAR</c> (<c>Monster.cpp:629</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The record does not end at its ASL.</b> Unlike <c>ITEM_DATA</c>, two more structures follow:
/// <c>myItems</c> at &gt; 0.693 and <c>money</c> at ≥ 0.906 (<c>Monster.cpp:851</c>). They sit
/// after <c>mon_asl</c>, so a reader modelled on the item record stops three structures early.
/// </para>
/// <para>
/// <c>Hit_Dice</c> is a <b><c>float</c></b> among <c>long</c>s (<c>Monster.h:410</c>). It is the
/// same four bytes either way, so misreading it does not desynchronise — it silently yields a
/// nonsense number, which is worse. A monster with 2 hit dice reads as 1,073,741,824 as an int.
/// </para>
/// </remarks>
public static class MonsterRecordReader
{
    public static MonsterRecord Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int preSpellNameKey = -1;
        if (version < DesignVersion.SpellNames || version >= DesignVersion.SaveIDs)
        {
            preSpellNameKey = ar.ReadInt32();
        }

        string name = ReadDas(ar);

        // Older designs stored just an icon filename where later ones store a whole PIC_DATA.
        PicRecord? icon = null;
        string legacyIconFile = string.Empty;
        if (version < DesignVersion.V0640)
        {
            legacyIconFile = ReadDas(ar);
        }
        else
        {
            icon = PicDataReader.Read(ar, version, PicArchiveVariant.Car);
        }

        string hitSound = ReadDas(ar);
        string missSound = ReadDas(ar);

        string moveSound = string.Empty;
        string deathSound = string.Empty;
        if (version >= DesignVersion.V0575)
        {
            moveSound = ReadDas(ar);
            deathSound = ReadDas(ar);
        }

        int intelligence = ar.ReadInt32();
        int armorClass = ar.ReadInt32();
        int movement = ar.ReadInt32();

        // float, not long -- four bytes either way, so this never desynchronises. It just gives
        // the wrong number, which no alignment check will catch.
        float hitDice = ar.ReadSingle();

        int useHitDice = version >= DesignVersion.V0906 ? ar.ReadInt32() : 0;
        int hitDiceBonus = ar.ReadInt32();
        int thac0 = ar.ReadInt32();

        var attacks = new List<AttackDetails>();
        if (version < DesignVersion.V0750)
        {
            // Four packed scalars describing a single uniform attack, later replaced by a list.
            int nbrAttacks = ar.ReadInt32();
            int dmgDiceForAttack = ar.ReadInt32();
            int dmgDiceBonus = ar.ReadInt32();
            int nbrDmgDice = ar.ReadInt32();

            // Three of the four are floored, and the floors differ: one attack, ten sides, one
            // die. A zero in the file means "unset", not "none" (Monster.cpp:746).
            if (nbrAttacks <= 0) nbrAttacks = 1;
            if (dmgDiceForAttack <= 0) dmgDiceForAttack = 10;
            if (nbrDmgDice <= 0) nbrDmgDice = 1;

            for (int i = 0; i < nbrAttacks; i++)
            {
                attacks.Add(new AttackDetails(dmgDiceForAttack, nbrDmgDice, dmgDiceBonus,
                                              DefaultAttackMessage, string.Empty, 0, 0, 0));
            }
        }
        else
        {
            attacks = MonsterLeafReaders.ReadAttackData(ar, version, role);
        }

        // Applied at EVERY version, not just the legacy branch: a monster that reaches here with
        // no attacks is given one (Monster.cpp:764). Live in the corpus -- one of dc-default's 171
        // monsters has an empty list on disk, and the reference fights it with 1d6 where a literal
        // reader leaves it unable to attack at all.
        if (attacks.Count == 0)
        {
            attacks.Add(new AttackDetails(6, 1, 0, DefaultAttackMessage, string.Empty, 0, 0, 0));
        }

        int magicResistance = ar.ReadInt32();
        int size = ar.ReadInt32();

        // Below VersionSpellNames the editor assigns "Fighter" and reads NOTHING (Monster.cpp:781).
        string classId = "Fighter";
        if (!(role == ArchiveRole.Editor && version < DesignVersion.SpellNames))
        {
            classId = ar.ReadString();
        }

        int morale = ar.ReadInt32();
        int experienceValue = ar.ReadInt32();

        if (role == ArchiveRole.Editor && version < DesignVersion.SpellNames)
        {
            ar.ReadByte();                       // retired ItemMask -- a BYTE, not an int
        }

        uint formType = ar.ReadUInt32();
        uint penaltyType = ar.ReadUInt32();
        uint immunityType = ar.ReadUInt32();
        uint miscOptionsType = ar.ReadUInt32();

        string undeadType = string.Empty;
        if (version >= DesignVersion.V0750)
        {
            // A numeric index into UndeadTypeText, replaced by the name itself at 0.998115.
            if (version.Value <= 0.998115)
            {
                undeadType = UndeadTypeName(ar.ReadInt32());
            }
            else
            {
                undeadType = ar.ReadString();
            }
        }

        var specialAbilities = SpecabReader.Read(ar, version);
        var attributes = AslReader.Read(ar, version, AslMaps.MonsterData);

        // Both AFTER the attribute list -- see the class remarks.
        ItemList? items = version > DesignVersion.V0693
            ? MonsterLeafReaders.ReadItemList(ar, version, role)
            : null;

        MoneySack? money = version >= DesignVersion.V0906
            ? MonsterLeafReaders.ReadMoneySack(ar, version)
            : null;

        return new MonsterRecord(
            preSpellNameKey, name, icon, legacyIconFile,
            hitSound, missSound, moveSound, deathSound,
            intelligence, armorClass, movement, hitDice, useHitDice, hitDiceBonus, thac0,
            attacks, magicResistance, size, classId, morale, experienceValue,
            formType, penaltyType, immunityType, miscOptionsType, undeadType,
            specialAbilities, attributes, items, money);
    }

    /// <summary>
    /// Reads a whole <c>monsters.dat</c> payload (<c>MONSTER_DATA_TYPE::Serialize</c>,
    /// <c>Monster.cpp:1023</c>): a count then the records, with no trailing list.
    /// </summary>
    public static List<MonsterRecord> ReadDatabase(IArchiveCursor ar, DesignVersion version,
                                                   ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var monsters = new List<MonsterRecord>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            monsters.Add(Read(ar, version, role));
        }
        return monsters;
    }

    public static List<MonsterRecord> ReadDatabase(MfcArchiveReader ar, DesignVersion version,
                                                   ArchiveRole role) =>
        ReadDatabase(ArchiveCursor.For(ar), version, role);

    public static List<MonsterRecord> ReadDatabase(CarArchiveReader ar, DesignVersion version,
                                                   ArchiveRole role) =>
        ReadDatabase(ArchiveCursor.For(ar), version, role);

    /// <summary>
    /// What the reference names an attack it had to invent (<c>Monster.cpp:756</c>).
    /// </summary>
    /// <remarks>
    /// It reads as a verb because the combat log renders it as <c>"&lt;name&gt; attacks"</c>.
    /// </remarks>
    public const string DefaultAttackMessage = "attacks";

    /// <summary>
    /// The turning categories, indexed as the <c>undeadType</c> enum
    /// (<c>Externs.h:899</c>) — <c>UndeadTypeText</c>, <c>Globtext.cpp:667</c>.
    /// </summary>
    /// <remarks>
    /// Only ever used to name an index read from a design at or below 0.998115. Above that the
    /// name is in the file, and this table is not consulted — which is why designs may contain
    /// categories that are not in it at all.
    /// </remarks>
    public static readonly IReadOnlyList<string> UndeadTypeNames =
    [
        "Not Undead", "Skeleton", "Zombie", "Ghoul", "Shadow", "Wight", "Ghast",
        "Wraith", "Mummy", "Spectre", "Vampire", "Ghost", "Lich", "Special",
    ];

    /// <summary>
    /// Names a legacy undead-type index, or gives empty for one that names nothing.
    /// </summary>
    /// <remarks>
    /// <b>Index 0 is empty, not <c>"Not Undead"</c>.</b> The reference clears the string first and
    /// only fills it for <c>0 &lt; index &lt; 14</c> (<c>Monster.cpp:812</c>), so the zero row of
    /// its own table is unreachable — and it must stay unreachable, because "is this monster
    /// undead at all?" is asked everywhere as "is the string non-empty".
    /// </remarks>
    public static string UndeadTypeName(int index) =>
        index is > 0 and < 14 ? UndeadTypeNames[index] : string.Empty;

    private static string ReadDas(IArchiveCursor ar) =>
        ArchiveStringConventions.Decode(ar.ReadString());
}
