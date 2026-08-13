using UAF.Common;
using UAF.Serialization;

namespace UAF.Import.Frua;

/// <summary>
/// Turns a DOS FRUA item into the engine's <c>ITEM_DATA</c>, and an item ordinal into an instance.
/// </summary>
/// <remarks>
/// <para>
/// <b>FRUA splits an item across two files and the engine does not.</b> <c>item.dat</c> holds the
/// per-item record — names, price, charges — and <c>items.dat</c> the shared <i>class</i> record
/// with the damage dice, slot and weapon behaviour. Each item names a class, so several items are
/// the same kind of thing at different prices. The engine's record is one flat thing, so
/// <see cref="Convert"/> takes both.
/// </para>
/// <para>
/// <b>An item instance is thin.</b> <c>AssignItem</c> (<c>UAImport.cpp:902</c>) copies only the
/// charges and bundle quantity out of the database record and leaves everything else to the
/// reference by id — so <see cref="Instance"/> needs the converted record, not just the ordinal.
/// </para>
/// </remarks>
public static class FruaItemConverter
{
    /// <summary>
    /// The highest item ordinal an event or creature can name (<c>MAX_IMPORT_ITEMS</c>).
    /// </summary>
    /// <remarks>
    /// <b>Ordinals are one-based and zero means "no item".</b> <c>AssignItem</c> rejects both 0 and
    /// anything above this before it indexes, which is why the empty slots in a treasure or a
    /// creature's inventory are zeroes rather than absences.
    /// </remarks>
    public const int MaxOrdinal = 255;

    /// <summary>Converts one item, given the class record it names.</summary>
    /// <param name="item">The <c>item.dat</c> record.</param>
    /// <param name="itemClass">The <c>items.dat</c> class record it points at.</param>
    public static ItemRecord Convert(FruaItem item, FruaItemClass itemClass)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(itemClass);

        int weaponType = WeaponType(itemClass);
        bool isWeapon = weaponType != NotWeapon;
        int attackBonus = isWeapon ? Signed(item.MagicBonus) : 0;

        return new ItemRecord(
            Names: new ItemNames(
                PreSpellNameKey: -1,
                SpellId: string.Empty,

                // Not a mix-up: the reference's UniqueName is the one that HIDES the words the
                // party has not identified, and its IdName spells the item out in full. The
                // reader's two properties are named the other way round, after what they do.
                UniqueName: item.UnidentifiedName,
                IdName: item.Name,

                // The reference points these at the editor's default sounds, which are template
                // paths rather than anything FRUA supplies.
                HitSound: string.Empty,
                MissSound: string.Empty,
                LaunchSound: string.Empty),

            HitArt: null,
            MissileArt: null,

            Scalars: new ItemScalars(
                AmmoType: AmmoType(weaponType, item.Name),
                Experience: 0,
                Cost: item.Price,
                Encumbrance: item.Encumbrance,
                AttackBonus: attackBonus,
                Cursed: item.Cursed,

                // A bundle of zero would make the item impossible to carry.
                BundleQty: Math.Max((int)item.BundleQuantity, 1),
                NumCharges: item.Charges),

            Combat: new ItemCombat(
                LocationReadied: Location(itemClass.Slot),
                HandsToUse: Hands(itemClass),
                DmgDiceSm: itemClass.VersusSmall.Sides,
                NbrDiceSm: itemClass.VersusSmall.Dice,
                DmgDiceLg: itemClass.VersusLarge.Sides,
                NbrDiceLg: itemClass.VersusLarge.Dice,

                // A weapon's magic bonus adds to damage as well as to the attack; a
                // non-weapon's is armour class instead, and the class's own bonus stands.
                DmgBonusSm: isWeapon ? attackBonus : itemClass.VersusSmall.Bonus,
                DmgBonusLg: isWeapon ? attackBonus : itemClass.VersusLarge.Bonus,

                RofPerRound: RateOfFire(itemClass.Rate),
                ProtectionBase: Protection(itemClass.Protection),

                // Armour improves armour class by lowering it, so a magic bonus is negated.
                ProtectionBonus: isWeapon ? 0 : -Signed(item.MagicBonus)),

            Tail: new ItemTail(
                WeaponType: weaponType,
                UsageFlags: 0,
                LegacyUsableByClass: 0,
                UsableByBaseclass: [],
                RangeMax: Range(itemClass, weaponType),
                UseEvent: 0,
                ExamineEvent: 0,
                ExamineLabel: string.Empty,
                AttackMessage: string.Empty,
                RechargeRate: 0,
                IsNonLethal: 0,
                HitArt: null,
                CanBeHalvedJoined: 0,
                CanBeTradeDropSoldDep: 1,
                SpecialAbilities: new SpecabBlock([], [], []),
                Attributes: []));
    }

    /// <summary>
    /// One carried instance of an item, by its one-based ordinal.
    /// </summary>
    /// <param name="ordinal">The stored ordinal; 0 and anything past <see cref="MaxOrdinal"/> are
    /// not items and yield null.</param>
    /// <param name="database">The design's item database.</param>
    /// <param name="quantity">
    /// How many bundles the source names. Zero leaves the item's own bundle quantity, which is
    /// what <c>AssignItem</c> sets before a caller multiplies.
    /// </param>
    /// <param name="identified">Whether the party already knows what it is.</param>
    /// <remarks>
    /// <b>The quantity is bundles, not pieces.</b> Both creature and treasure paths multiply the
    /// stored count by the item's bundle size — twenty arrows is one bundle of twenty, not twenty
    /// bundles — so a converter that used the count directly would give out a twentieth of the
    /// ammunition a design intended.
    /// </remarks>
    public static ItemInstance? Instance(int ordinal, FruaItemDatabase? database,
                                         int quantity = 0, bool identified = false)
    {
        if (ordinal < 1 || ordinal > MaxOrdinal || database is null
            || ordinal > database.Items.Count)
        {
            return null;
        }

        var item = database.Items[ordinal - 1];
        int bundle = Math.Max((int)item.BundleQuantity, 1);

        return new ItemInstance(
            Key: ordinal,

            // The engine identifies an item by name; the ordinal is the design's own numbering.
            ItemId: item.Name,
            LegacyItemId: ordinal,

            // An imported item is carried rather than equipped. NotReady is a packed BASE38
            // word, not zero and not a small ordinal -- see ReadiedLocation.
            ReadyLocation: ReadiedLocation.NotReady,
            Quantity: quantity > 0 ? quantity * bundle : bundle,
            Identified: identified ? 1 : 0,
            Charges: item.Charges,
            Cursed: item.Cursed,
            Paid: 0);
    }

    /// <summary><c>NotWeapon</c> (<c>Items.h:54</c>).</summary>
    public const int NotWeapon = 0;

    /// <summary>
    /// The engine's <c>weaponClassType</c> for a FRUA class record.
    /// </summary>
    /// <remarks>
    /// <b>The weapon-type byte is a set of codes, not an ordinal</b>, and it only means anything
    /// when <c>cutting_or_blunt</c> is non-zero — otherwise the item is not a weapon whatever the
    /// code says. Code 4 splits further on that same byte: bit 1 cutting, bit 128 blunt, and
    /// neither is a case the reference expects to reach ("shouldn't hit this!").
    /// </remarks>
    public static int WeaponType(FruaItemClass itemClass)
    {
        ArgumentNullException.ThrowIfNull(itemClass);

        if (itemClass.CuttingOrBlunt == 0)
        {
            return NotWeapon;
        }

        return itemClass.WeaponType switch
        {
            138 => 6,                                    // Crossbow
            26 => 7,                                     // Throw
            20 => 3,                                     // HandThrow
            18 => 8,                                     // Ammo
            15 or 11 => 5,                               // Bow
            10 => 4,                                     // SlingNoAmmo
            4 => (itemClass.CuttingOrBlunt & 1) != 0 ? 2 // HandCutting
               : (itemClass.CuttingOrBlunt & 128) != 0 ? 1 // HandBlunt
               : NotWeapon,
            _ => NotWeapon,
        };
    }

    /// <summary>
    /// Where a FRUA slot number is worn.
    /// </summary>
    /// <remarks>
    /// <b>The engine stores a readied location as a packed BASE38 word, not as an ordinal.</b>
    /// FRUA's slots 0–10 are consecutive and line up one for one with the reference's
    /// <c>itemLocationType</c> (<c>Items.h:81</c>), so it is tempting to cast — but the value that
    /// reaches the file is <see cref="ReadiedLocation.WeaponHand"/> and its siblings, six
    /// characters encoded into a <c>DWORD</c>. A cast ordinal would name no slot at all.
    /// </remarks>
    private static uint Location(byte slot) => slot switch
    {
        0 => ReadiedLocation.WeaponHand,
        1 => ReadiedLocation.ShieldHand,
        2 => ReadiedLocation.BodyArmor,
        3 => ReadiedLocation.Hands,
        4 => ReadiedLocation.Head,
        5 => ReadiedLocation.Waist,
        6 => ReadiedLocation.BodyRobe,
        7 => ReadiedLocation.Back,
        8 => ReadiedLocation.Feet,
        9 => ReadiedLocation.Fingers,
        10 => ReadiedLocation.AmmoQuiver,

        // Not a slot the reference's switch covers, so the item cannot be worn.
        _ => ReadiedLocation.Cannot,
    };

    /// <summary>
    /// How many hands an item occupies when readied.
    /// </summary>
    /// <remarks>
    /// <b>Only the two hands and the quiver take hands; everything else takes none</b> — a helmet
    /// with a stored two-handed flag would otherwise make the wearer unable to hold a sword. Bows
    /// and crossbows are then forced to one hand regardless of what the class says.
    /// </remarks>
    private static int Hands(FruaItemClass itemClass)
    {
        uint location = Location(itemClass.Slot);
        bool takesHands = location == ReadiedLocation.WeaponHand
                          || location == ReadiedLocation.ShieldHand
                          || location == ReadiedLocation.AmmoQuiver;

        if (!takesHands)
        {
            return 0;
        }

        int weapon = WeaponType(itemClass);
        return weapon is 5 or 6 ? 1 : itemClass.TwoHanded;
    }

    /// <summary>
    /// Shots per round, which the stored rate expresses in halves above two.
    /// </summary>
    /// <remarks>
    /// A rate of 3 is one-and-a-half shots per round, not three — but a rate of 1 or 2 is itself.
    /// The discontinuity is the reference's, and it is why this is not a plain division.
    /// </remarks>
    private static double RateOfFire(byte rate) => rate <= 2 ? rate : rate / 2.0;

    /// <summary>
    /// Armour class from the stored protection byte.
    /// </summary>
    /// <remarks>
    /// <b>Two encodings in one byte, and both produce a negative number.</b> Above 170 the value
    /// is <c>188 - stored</c> subtracted from ten; above 117 it is simply <c>stored - 128</c>
    /// negated; below that the item gives no protection at all. Armour class improves downward, so
    /// better armour is a larger negative.
    /// </remarks>
    private static int Protection(byte protection)
    {
        if (protection > 170)
        {
            int prot = 188 - protection;
            return prot > 0 ? -(10 - prot) : 0;
        }

        // Below 128 would be a shield at -2, for example.
        return protection > 117 ? -(protection - 128) : 0;
    }

    /// <summary>
    /// Reach, which a weapon always has at least one square of.
    /// </summary>
    /// <remarks>
    /// A weapon whose class stores zero range would be unable to attack anything, including what
    /// it is standing next to.
    /// </remarks>
    private static int Range(FruaItemClass itemClass, int weaponType) =>
        weaponType == NotWeapon ? itemClass.Range : Math.Max(1, (int)itemClass.Range);

    /// <summary>
    /// The ammunition a launcher uses, or the kind a piece of ammunition is.
    /// </summary>
    /// <remarks>
    /// <b>Ammunition is told apart by its name.</b> FRUA has one ammunition weapon type and no
    /// field saying what fires it, so the reference looks for "Bolt" in the item's name and calls
    /// everything else a bow's. Crossbows and bows name their own.
    /// </remarks>
    private static string AmmoType(int weaponType, string name) => weaponType switch
    {
        8 => name.Contains("Bolt", StringComparison.OrdinalIgnoreCase) ? "CrossBow" : "Bow",
        5 => "Bow",
        6 => "CrossBow",
        _ => string.Empty,
    };

    /// <summary>
    /// A stored magic bonus, which is signed despite being a byte.
    /// </summary>
    /// <remarks>
    /// <b>Cursed items carry a negative bonus as a wrapped byte.</b> The reference tests
    /// <c>&gt; 127</c> and subtracts from 256, so a stored 255 is −1 — read unsigned it would be a
    /// +255 sword.
    /// </remarks>
    public static int Signed(byte magicBonus) =>
        magicBonus > 127 ? -(256 - magicBonus) : magicBonus;
}
