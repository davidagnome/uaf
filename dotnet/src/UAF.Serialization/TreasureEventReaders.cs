using UAF.Common;

namespace UAF.Serialization;

/// <summary>A treasure event — money and items handed to the party.</summary>
public sealed record TreasureEvent(
    GameEventBase Base, MoneySack Money, ItemList Items, int SilentGiveToActiveChar);

/// <summary>
/// Reads the two treasure events, <c>GIVE_TREASURE_DATA</c> (<c>GameEvent.cpp:7768</c>) and
/// <c>COMBAT_TREASURE</c> (<c>:7813</c>).
/// </summary>
/// <remarks>
/// They look like the same event and are not. <c>COMBAT_TREASURE</c> is money then items and
/// nothing else; <c>GIVE_TREASURE_DATA</c> adds a flag between them and carries a legacy
/// coin-count form below 0.740. Both reuse <c>MONEY_SACK</c> and <c>ITEM_LIST</c>, already ported
/// for monsters.
/// </remarks>
public static class TreasureEventReaders
{
    /// <summary>
    /// Reads a <c>GIVE_TREASURE_DATA</c>.
    /// </summary>
    /// <remarks>
    /// Below 0.740 the money is three loose counts rather than a <c>MONEY_SACK</c>, and the coin
    /// type itself only appears from 0.670 — so there are three distinct shapes across the version
    /// range. The <c>items</c> list is read outside the storing/loading branch.
    /// </remarks>
    public static TreasureEvent ReadGiveTreasure(IArchiveCursor ar, DesignVersion version,
                                                 ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        MoneySack money;
        if (version < DesignVersion.V0740)
        {
            // The legacy form: an optional coin type then plat/gem/jewel counts, which the
            // reference folds into a MONEY_SACK after reading.
            if (version >= DesignVersion.V0670)
            {
                ar.ReadInt32();                          // coinType
            }
            ar.ReadInt32();                              // platinum
            ar.ReadInt32();                              // gems
            ar.ReadInt32();                              // jewels
            money = new MoneySack([], [], []);
        }
        else
        {
            money = MonsterLeafReaders.ReadMoneySack(ar, version);
        }

        int silentGive = version >= DesignVersion.V0890 ? ar.ReadInt32() : 0;

        var items = MonsterLeafReaders.ReadItemList(ar, version, role);

        return new TreasureEvent(baseEvent, money, items, silentGive);
    }

    /// <summary>
    /// Reads a <c>COMBAT_TREASURE</c> — money then items, with no flag and no legacy form.
    /// </summary>
    public static TreasureEvent ReadCombatTreasure(IArchiveCursor ar, DesignVersion version,
                                                   ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);
        var money = MonsterLeafReaders.ReadMoneySack(ar, version);
        var items = MonsterLeafReaders.ReadItemList(ar, version, role);

        return new TreasureEvent(baseEvent, money, items, 0);
    }
}
