using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes the leaf structures a <c>MONSTER_DATA</c> record depends on — the inverses of
/// <see cref="MonsterLeafReaders"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>None of these storing branches has a version gate.</b> Every one of the five is a flat
/// <c>if (ar.IsStoring()) { … }</c> that writes the modern shape; the version tests all sit in the
/// loading half. So there is no version parameter here, and none of the legacy shapes the readers
/// understand can be produced — see <see cref="MonsterRecordWriter.CanWrite"/>, which refuses a
/// record still carrying one rather than writing a plausible file with the content dropped.
/// </para>
/// </remarks>
public static class MonsterLeafWriters
{
    /// <summary>Writes an <c>ITEM</c> (<c>Items.cpp:825</c>) — an instance, not a database record.</summary>
    /// <remarks>
    /// <para>
    /// <c>cursed</c> is a <b><c>BYTE</c></b> between two <c>int</c>s. Writing four bytes there
    /// shifts <c>paid</c> and everything after it.
    /// </para>
    /// <para>
    /// <b><c>readyLocation</c> goes out exactly as it came in.</b> The reference's own reader maps
    /// the ordinals 0‥16 onto the base-38 packed constants as it loads
    /// (<c>itemReadiedLocation::Synonym</c>, <c>Items.cpp:728</c>) and then stores the mapped
    /// value, so a reference load-and-save silently upgrades an old slot. This port reads the raw
    /// <c>DWORD</c>, which makes writing it back byte-exact; the conversion is available separately
    /// through <see cref="ReadiedLocation"/> for a caller that wants it.
    /// </para>
    /// </remarks>
    public static void WriteItem(MfcArchiveWriter ar, ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(item);

        ar.WriteInt32(item.Key);
        ar.WriteString(item.ItemId);             // verbatim: an ITEM_ID, not a DAS string
        ar.WriteUInt32(item.ReadyLocation);
        ar.WriteInt32(item.Quantity);
        ar.WriteInt32(item.Identified);
        ar.WriteInt32(item.Charges);
        ar.WriteByte(item.Cursed);               // BYTE, not int
        ar.WriteInt32(item.Paid);
    }

    /// <summary>Writes a <c>READY_ITEMS</c>: twelve slot indices, in declaration order.</summary>
    /// <remarks>
    /// The order is <see cref="ReadyItems.SlotNames"/> and it is positional — nothing in the stream
    /// labels a slot, so a permuted list produces a file that reads back with the armour on the
    /// wrong limb and no error anywhere.
    /// </remarks>
    public static void WriteReadyItems(MfcArchiveWriter ar, ReadyItems ready)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(ready);

        if (ready.Slots.Count != MonsterLeafReaders.ReadySlotCount)
        {
            throw new ArgumentException(
                $"READY_ITEMS is a fixed {MonsterLeafReaders.ReadySlotCount} slots, not " +
                $"{ready.Slots.Count}. The count is compile-time in the reference, so it is never " +
                "written and a short list would silently truncate the record.", nameof(ready));
        }

        foreach (int slot in ready.Slots)
        {
            ar.WriteInt32(slot);
        }
    }

    /// <summary>
    /// Writes an <c>ITEM_LIST</c> (<c>Items.cpp:1679</c>): a count, the items, then the equipment
    /// slots.
    /// </summary>
    /// <remarks>
    /// The trailing <c>READY_ITEMS</c> is outside the loop. A writer that stops after the items
    /// leaves the reader 48 bytes short and it consumes the start of the money sack instead.
    /// </remarks>
    public static void WriteItemList(MfcArchiveWriter ar, ItemList list)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(list);

        ar.WriteInt32(list.Items.Count);
        foreach (var item in list.Items)
        {
            WriteItem(ar, item);
        }

        WriteReadyItems(ar, list.Ready);
    }

    /// <summary>Writes a <c>GEM_TYPE</c>: an id and a value.</summary>
    public static void WriteGem(MfcArchiveWriter ar, GemType gem)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(gem);

        ar.WriteInt32(gem.Id);
        ar.WriteInt32(gem.Value);
    }

    /// <summary>
    /// Writes a <c>MONEY_SACK</c> (<c>Money.cpp:1722</c>): ten coin slots, then gems and jewellery.
    /// </summary>
    /// <remarks>
    /// The pre-0.911 gem encoding the reader refuses is a <i>loading</i> branch, so there is
    /// nothing to refuse here — the storing half writes plain <c>int</c> counts at every version.
    /// </remarks>
    public static void WriteMoneySack(MfcArchiveWriter ar, MoneySack money)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(money);

        if (money.Coins.Count != MonsterLeafReaders.MaxCoinTypes)
        {
            throw new ArgumentException(
                $"MONEY_SACK is a fixed {MonsterLeafReaders.MaxCoinTypes} coin slots, not " +
                $"{money.Coins.Count}. Like READY_ITEMS the count is compile-time and never " +
                "written, so a short list truncates the record without any sign of it.",
                nameof(money));
        }

        foreach (int coin in money.Coins)
        {
            ar.WriteInt32(coin);
        }

        WriteGemList(ar, money.Gems);
        WriteGemList(ar, money.Jewelry);
    }

    private static void WriteGemList(MfcArchiveWriter ar, IReadOnlyList<GemType> gems)
    {
        ar.WriteInt32(gems.Count);
        foreach (var gem in gems)
        {
            WriteGem(ar, gem);
        }
    }

    /// <summary>Writes one <c>ATTACK_DETAILS</c> (<c>Monster.cpp:105</c>).</summary>
    /// <remarks>
    /// <b>The spell fields are written whatever the version</b>, where the reader only reads them
    /// from 0.904. An attack read from an older design therefore gains an empty spell id and two
    /// zeroes — which is what the reference writes too, since those members are simply whatever
    /// the load left in them.
    /// </remarks>
    public static void WriteAttackDetails(MfcArchiveWriter ar, AttackDetails attack)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(attack);

        ar.WriteInt32(attack.Sides);
        ar.WriteInt32(attack.Nbr);
        ar.WriteInt32(attack.Bonus);
        ar.WriteString(ArchiveStringConventions.Encode(attack.AttackMessage));
        ar.WriteString(attack.SpellId);          // verbatim: a SPELL_ID, not a DAS string
        ar.WriteInt32(attack.SpellClass);
        ar.WriteInt32(attack.SpellLevel);
    }

    /// <summary>Writes an <c>ATTACK_DATA</c> (<c>Monster.h:267</c>): a count then the attacks.</summary>
    public static void WriteAttackData(MfcArchiveWriter ar, IReadOnlyList<AttackDetails> attacks)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(attacks);

        ar.WriteInt32(attacks.Count);
        foreach (var attack in attacks)
        {
            WriteAttackDetails(ar, attack);
        }
    }
}
