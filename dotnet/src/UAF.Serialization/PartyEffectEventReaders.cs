using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// A <c>GIVE_DAMAGE_DATA</c> — a trap or hazard that attacks the party directly.
/// </summary>
/// <param name="EventSave">A <c>spellSaveEffectType</c>: what a successful save does.</param>
/// <param name="SpellSave">A <c>spellSaveVersusType</c>: which saving-throw column applies.</param>
/// <param name="Who">An <c>eventPartyAffectType</c>: which characters it reaches.</param>
/// <param name="Distance">An <c>eventDistType</c>.</param>
public sealed record DamageEvent(
    GameEventBase Base, int NbrAttacks, int ChancePerAttack, int DmgDice, int DmgDiceQty,
    int DmgBonus, int SaveBonus, int AttackThac0,
    int EventSave, int SpellSave, int Who, int Distance) : IGameEvent;

/// <summary>
/// A <c>HEAL_PARTY_DATA</c> — restores hit points, levels drained, or removes curses.
/// </summary>
/// <param name="Chance">Percentage chance, a <c>BYTE</c>.</param>
/// <param name="LiteralOrPercent">0 means <see cref="HowMuchHp"/> is literal, 1 a percentage.</param>
public sealed record HealPartyEvent(
    GameEventBase Base, int HealHitPoints, int HealDrain, int HealCurse, byte Chance,
    int Who, int HowMuchHp, byte LiteralOrPercent) : IGameEvent;

/// <summary>
/// A <c>TAKE_PARTY_ITEMS_DATA</c> — confiscates money and items, optionally into a vault.
/// </summary>
/// <param name="TakeItems">A bitmask of which item classes to take, a <c>BYTE</c>.</param>
/// <param name="TakeAffects">A <c>takeItemsAffectsType</c>: whom it takes from.</param>
/// <param name="WhichVault">Which vault receives them, a <c>BYTE</c>.</param>
/// <param name="Items">Specific items named by the design, rather than a class of them.</param>
public sealed record TakePartyItemsEvent(
    GameEventBase Base, int StoreItems, int MustHitReturn, byte TakeItems, int TakeAffects,
    int ItemSelectFlags, int PlatinumSelectFlags, int GemsSelectFlags, int JewelrySelectFlags,
    int Platinum, int Gems, int Jewelry, int ItemPercent, int MoneyType, byte WhichVault,
    ItemList Items) : IGameEvent;

/// <summary>
/// The three events that act on the party's health and possessions.
/// </summary>
/// <remarks>
/// Grouped because they share a hazard: <b>each mixes 4-byte <c>BOOL</c>s with 1-byte
/// <c>BYTE</c>s</b>, and the declarations interleave them, so reading down the class in
/// declaration order gets the widths wrong. The <c>Serialize</c> order is the one to follow, and
/// the widths come from <c>GameEvent.h</c>.
/// </remarks>
public static class PartyEffectEventReaders
{
    /// <summary>
    /// Reads a <c>GIVE_DAMAGE_DATA</c> (<c>GameEvent.cpp:7914</c>) — eleven <c>int</c>s, no gates.
    /// </summary>
    /// <remarks>
    /// The four enum-typed members are written through an <c>int temp</c>, so they are four bytes
    /// each like the rest. This is the rare event record with no version fork anywhere in it.
    /// </remarks>
    public static DamageEvent ReadDamage(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        return new DamageEvent(
            baseEvent,
            NbrAttacks: ar.ReadInt32(),
            ChancePerAttack: ar.ReadInt32(),
            DmgDice: ar.ReadInt32(),
            DmgDiceQty: ar.ReadInt32(),
            DmgBonus: ar.ReadInt32(),
            SaveBonus: ar.ReadInt32(),
            AttackThac0: ar.ReadInt32(),
            EventSave: ar.ReadInt32(),
            SpellSave: ar.ReadInt32(),
            Who: ar.ReadInt32(),
            Distance: ar.ReadInt32());
    }

    /// <summary>
    /// Reads a <c>HEAL_PARTY_DATA</c> (<c>GameEvent.cpp:13789</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>chance</c> is a <c>BYTE</c> among <c>BOOL</c>s</b> (<c>GameEvent.h</c>), so the fixed
    /// part is 13 bytes rather than 16.
    /// </para>
    /// <para>
    /// <c>HowMuchHP</c> and <c>LiteralOrPercent</c> arrive at 0.882 — and note the second is a
    /// <c>BYTE</c> too, so the gated part is five bytes, not eight.
    /// </para>
    /// <para>
    /// <b>Below the gate the reference runs on 100/1, not on zero</b> — <c>Clear</c> sets
    /// <c>HowMuchHP=100; LiteralOrPercent=1;</c> (<c>GameEvent.cpp:13726-13727</c>), which is
    /// "add 100% of maximum", the old unconditional full heal. This reader writes 0/0 because it
    /// has nothing to read: the zero pair is this port's stand-in for "absent", not the
    /// reference's default, so <b>a consumer must map it back to 100/1</b> rather than read it as
    /// "add nothing". <c>UAFcore.EventHeal.Adjustment</c> (<c>UAFcore/EventHeal.cs</c>) is where
    /// that happens — a plain reference, not a <c>cref</c>, since <c>UAFcore</c> depends on this
    /// assembly and not the reverse. The cost is a collision the format cannot resolve: a design
    /// at 0.882 or above can author "add 0 to current", and once read the two are identical.
    /// </para>
    /// </remarks>
    public static HealPartyEvent ReadHealParty(IArchiveCursor ar, DesignVersion version,
                                               ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int healHitPoints = ar.ReadInt32();
        int healDrain = ar.ReadInt32();
        int healCurse = ar.ReadInt32();
        byte chance = ar.ReadByte();                     // BYTE, not BOOL
        int who = ar.ReadInt32();

        int howMuchHp = 0;
        byte literalOrPercent = 0;
        if (version >= DesignVersion.V0882)
        {
            howMuchHp = ar.ReadInt32();
            literalOrPercent = ar.ReadByte();            // BYTE again
        }

        return new HealPartyEvent(baseEvent, healHitPoints, healDrain, healCurse, chance,
                                  who, howMuchHp, literalOrPercent);
    }

    /// <summary>
    /// Reads a <c>TAKE_PARTY_ITEMS_DATA</c> (<c>GameEvent.cpp:8327</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two <c>BYTE</c>s, and they are not adjacent.</b> <c>takeItems</c> sits third, between two
    /// <c>BOOL</c>s, and <c>WhichVault</c> is last and gated at 0.910. Reading either as an
    /// <c>int</c> shifts everything after it — and what follows is an <c>ITEM_LIST</c>, whose count
    /// would then be read from the middle of a flag.
    /// </para>
    /// <para>
    /// The <c>ITEM_LIST</c> is outside the storing/loading branch, so it is present at every
    /// version even though <c>WhichVault</c> just above it is not.
    /// </para>
    /// </remarks>
    public static TakePartyItemsEvent ReadTakePartyItems(IArchiveCursor ar, DesignVersion version,
                                                         ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int storeItems = ar.ReadInt32();
        int mustHitReturn = ar.ReadInt32();
        byte takeItems = ar.ReadByte();                  // BYTE between two BOOLs
        int takeAffects = ar.ReadInt32();
        int itemSelectFlags = ar.ReadInt32();
        int platinumSelectFlags = ar.ReadInt32();
        int gemsSelectFlags = ar.ReadInt32();
        int jewelrySelectFlags = ar.ReadInt32();
        int platinum = ar.ReadInt32();
        int gems = ar.ReadInt32();
        int jewelry = ar.ReadInt32();
        int itemPercent = ar.ReadInt32();
        int moneyType = ar.ReadInt32();

        byte whichVault = version >= DesignVersion.V0910 ? ar.ReadByte() : (byte)0;

        var items = MonsterLeafReaders.ReadItemList(ar, version, role);

        return new TakePartyItemsEvent(
            baseEvent, storeItems, mustHitReturn, takeItems, takeAffects,
            itemSelectFlags, platinumSelectFlags, gemsSelectFlags, jewelrySelectFlags,
            platinum, gems, jewelry, itemPercent, moneyType, whichVault, items);
    }
}
