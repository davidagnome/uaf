using UAF.Common;

namespace UAF.Serialization;

/// <summary>One carried item instance — a reference into the item database, plus its state.</summary>
public sealed record ItemInstance(
    int Key, string ItemId, int LegacyItemId, uint ReadyLocation,
    int Quantity, int Identified, int Charges, byte Cursed, int Paid);

/// <summary>Which item occupies each equipment slot. Twelve slots, all <c>int</c>.</summary>
public sealed record ReadyItems(IReadOnlyList<int> Slots)
{
    /// <summary>
    /// Twelve empty slots — what a list nobody has equipped from looks like.
    /// </summary>
    /// <remarks>
    /// <b>Not an empty list.</b> The count is compile-time in the reference and never written, so
    /// a shorter one truncates the record; <c>WriteReadyItems</c> refuses rather than letting that
    /// happen, which is how a projection built with <c>[]</c> gets caught at the writer instead of
    /// in a file nobody can read back.
    /// </remarks>
    public static ReadyItems Empty { get; } =
        new(new int[MonsterLeafReaders.ReadySlotCount]);

    public static readonly string[] SlotNames =
    [
        "WeaponHand", "ShieldHand", "MissileAmmo", "Armor", "Gauntlets", "Helmet",
        "Belt", "Robe", "Cloak", "Boots", "Ring1", "Ring2",
    ];
}

/// <summary>An item list plus the equipment slots that index into it.</summary>
public sealed record ItemList(IReadOnlyList<ItemInstance> Items, ReadyItems Ready);

/// <summary>A gem or piece of jewellery: a database id and a value.</summary>
public sealed record GemType(int Id, int Value);

/// <summary>Coins, gems and jewellery.</summary>
public sealed record MoneySack(
    IReadOnlyList<int> Coins, IReadOnlyList<GemType> Gems, IReadOnlyList<GemType> Jewelry);

/// <summary>One of a monster's attacks: damage dice, a message, and an optional spell.</summary>
public sealed record AttackDetails(
    int Sides, int Nbr, int Bonus, string AttackMessage,
    string SpellId, int LegacySpellId, int SpellClass, int SpellLevel);

/// <summary>
/// The leaf structures a <c>MONSTER_DATA</c> record depends on.
/// </summary>
/// <remarks>
/// Kept together because they are only reachable through monsters, and each is too small to
/// justify a file. All follow the <c>CAR</c> loading branches.
/// </remarks>
public static class MonsterLeafReaders
{
    /// <summary>Coin slots in a <c>MONEY_SACK</c> — a fixed compile-time count, not design data.</summary>
    public const int MaxCoinTypes = 10;

    /// <summary>Equipment slots in a <c>READY_ITEMS</c>.</summary>
    public const int ReadySlotCount = 12;

    /// <summary>
    /// Reads an <c>ITEM</c> (<c>Items.cpp:825</c>) — an instance, not a database record.
    /// </summary>
    /// <remarks>
    /// <c>cursed</c> is a <b><c>BYTE</c></b> (<c>Items.h:325</c>) sitting between <c>int</c>
    /// neighbours; reading it as four bytes shifts <c>paid</c> and everything after.
    /// <c>readyLocation</c> is a <c>DWORD</c> holding a base-38 packed name — see
    /// <see cref="ReadiedLocation"/>.
    /// </remarks>
    public static ItemInstance ReadItem(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int key = ar.ReadInt32();

        // Older designs stored a numeric id that the editor resolves against the item database;
        // newer ones store the name directly. ITEM_ID derives from CString (Externs.h:1378).
        string itemId = string.Empty;
        int legacyItemId = 0;
        if (role == ArchiveRole.Editor && version < DesignVersion.SpellNames)
        {
            legacyItemId = ar.ReadInt32();
        }
        else
        {
            itemId = ar.ReadString();
        }

        uint readyLocation = ar.ReadUInt32();

        return new ItemInstance(
            key, itemId, legacyItemId, readyLocation,
            Quantity: ar.ReadInt32(),
            Identified: ar.ReadInt32(),
            Charges: ar.ReadInt32(),
            Cursed: ar.ReadByte(),               // BYTE, not int
            Paid: ar.ReadInt32());
    }

    /// <summary>Reads a <c>READY_ITEMS</c> (<c>Items.cpp</c>): twelve slot indices.</summary>
    public static ReadyItems ReadReadyItems(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var slots = new List<int>(ReadySlotCount);
        for (int i = 0; i < ReadySlotCount; i++)
        {
            slots.Add(ar.ReadInt32());
        }
        return new ReadyItems(slots);
    }

    /// <summary>
    /// Reads an <c>ITEM_LIST</c> (<c>Items.cpp:1679</c>): a count, the items, then the equipment
    /// slots.
    /// </summary>
    /// <remarks>
    /// The trailing <c>READY_ITEMS</c> is outside the loop, so a reader that stops after the items
    /// leaves 48 bytes unconsumed — the same shape as the ammo list after <c>items.dat</c>'s
    /// records.
    /// </remarks>
    public static ItemList ReadItemList(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var items = new List<ItemInstance>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            items.Add(ReadItem(ar, version, role));
        }

        return new ItemList(items, ReadReadyItems(ar));
    }

    /// <summary>Reads a <c>GEM_TYPE</c> (<c>Money.cpp</c>): an id and a value.</summary>
    public static GemType ReadGem(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);
        return new GemType(ar.ReadInt32(), ar.ReadInt32());
    }

    /// <summary>
    /// Reads a <c>MONEY_SACK</c> (<c>Money.cpp:1722</c>): ten coin slots, then gems and jewellery.
    /// </summary>
    /// <remarks>
    /// Below 0.911 the gem counts are read with <c>ar.ar.ReadCount()</c> — note the double
    /// <c>ar</c>: it reaches past the <c>CAR</c> to the underlying <c>CArchive</c>, so the count
    /// comes from the <b>uncompressed</b> stream even in a compressed archive. That is almost
    /// certainly a defect in the reference, and it is not ported: no fixture is old enough to
    /// reach it.
    /// </remarks>
    public static MoneySack ReadMoneySack(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var coins = new List<int>(MaxCoinTypes);
        for (int i = 0; i < MaxCoinTypes; i++)
        {
            coins.Add(ar.ReadInt32());
        }

        if (version < DesignVersion.V0911)
        {
            throw new NotSupportedException(
                $"MONEY_SACK below 0.911 (this is {version}) reads its gem counts straight from " +
                "the underlying CArchive, bypassing the CAR (Money.cpp:1760). Not ported: no " +
                "fixture reaches it.");
        }

        var gems = ReadGemList(ar);
        var jewelry = ReadGemList(ar);
        return new MoneySack(coins, gems, jewelry);
    }

    private static List<GemType> ReadGemList(IArchiveCursor ar)
    {
        int count = ar.ReadInt32();
        var gems = new List<GemType>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            gems.Add(ReadGem(ar));
        }
        return gems;
    }

    /// <summary>Reads one <c>ATTACK_DETAILS</c> (<c>Monster.cpp:105</c>).</summary>
    /// <remarks>
    /// The spell fields arrived at 0.904; below that the attack is dice and a message only.
    /// <c>spellID</c> is a <c>SPELL_ID</c> — a string, not a key.
    /// </remarks>
    public static AttackDetails ReadAttackDetails(IArchiveCursor ar, DesignVersion version,
                                                  ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int sides = ar.ReadInt32();
        int nbr = ar.ReadInt32();
        int bonus = ar.ReadInt32();
        string attackMessage = ArchiveStringConventions.Decode(ar.ReadString());

        string spellId = string.Empty;
        int legacySpellId = 0;
        int spellClass = 0;
        int spellLevel = 0;
        if (version >= DesignVersion.V0904)
        {
            if (role == ArchiveRole.Editor && version < DesignVersion.SpellNames)
            {
                // -1 is the reference's "no spell" sentinel (Monster.h:146), and 0 is what the
                // modern shape leaves the field at when an attack has none. Normalising the two
                // means "no spell" has one representation: the modern format has no such field at
                // all, so a round trip through it would otherwise turn every -1 into a 0 and look
                // like 58 lost values.
                legacySpellId = Math.Max(ar.ReadInt32(), 0);
            }
            else
            {
                spellId = ar.ReadString();
            }
            spellClass = ar.ReadInt32();
            spellLevel = ar.ReadInt32();
        }

        return new AttackDetails(sides, nbr, bonus, attackMessage,
                                 spellId, legacySpellId, spellClass, spellLevel);
    }

    /// <summary>Reads an <c>ATTACK_DATA</c> (<c>Monster.h:268</c>): a count then the attacks.</summary>
    public static List<AttackDetails> ReadAttackData(IArchiveCursor ar, DesignVersion version,
                                                     ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var attacks = new List<AttackDetails>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            attacks.Add(ReadAttackDetails(ar, version, role));
        }
        return attacks;
    }
}
