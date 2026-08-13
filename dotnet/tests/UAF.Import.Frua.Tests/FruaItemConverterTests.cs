using UAF.Import.Frua;
using UAF.Serialization;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Turning a DOS FRUA item into the engine's <c>ITEM_DATA</c>.
/// </summary>
public class FruaItemConverterTests
{
    /// <summary>
    /// <c>RUNELORD.DSN</c>, not <c>HEIRS.DSN</c>.
    /// </summary>
    /// <remarks>
    /// <b>Most designs ship no item database at all.</b> <c>HEIRS.DSN</c> — the fixture the rest
    /// of the importer tests use — has neither <c>ITEM.DAT</c> nor <c>ITEMS.DAT</c>, because a
    /// design that adds no items of its own inherits the stock ones from the FRUA installation's
    /// <c>DISK1</c>. <c>RUNELORD.DSN</c> ships both files itself, so it is the only fixture in the
    /// corpus that exercises this at all.
    /// </remarks>
    private static string? Runelord()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string design = Path.Combine(dir.FullName, "reference", "RUNELORD.DSN");
        return Directory.Exists(design) ? design : null;
    }

    private static FruaItemDatabase? Database() =>
        Runelord() is { } design ? FruaItemDatabase.Read(design) : null;

    /// <summary>Every item in the design converts, and keeps its name and price.</summary>
    [Fact]
    public void Every_item_converts_and_keeps_its_identity()
    {
        if (Database() is not { } database)
        {
            return;
        }

        Assert.NotEmpty(database.Items);
        int named = 0;

        foreach (var item in database.Items)
        {
            var itemClass = database.Classes[item.ClassIndex];
            var converted = FruaItemConverter.Convert(item, itemClass);

            Assert.Equal(item.Name, converted.Names.IdName);
            Assert.Equal(item.UnidentifiedName, converted.Names.UniqueName);
            Assert.Equal(item.Price, converted.Scalars.Cost);
            Assert.Equal(item.Encumbrance, converted.Scalars.Encumbrance);

            // A bundle of zero would make the item impossible to carry.
            Assert.True(converted.Scalars.BundleQty >= 1);

            if (!string.IsNullOrWhiteSpace(converted.Names.IdName))
            {
                named++;
            }
        }

        // Runelord ships 254 items across 128 classes, and every one converts with a name.
        Assert.Equal(database.Items.Count, named);
        Assert.True(named >= 254, $"only {named} items converted with a name");
    }

    /// <summary>
    /// The readied location is a packed word, never a small ordinal.
    /// </summary>
    /// <remarks>
    /// <b>This is the check that catches a cast.</b> FRUA's slots are 0–10 and the engine's
    /// <c>itemLocationType</c> is 0–10 in the same order, so casting looks right — but the value
    /// that reaches the file is a six-character BASE38 word. Every real location is far larger
    /// than the eleven ordinals, so a cast would show up here immediately.
    /// </remarks>
    [Fact]
    public void The_readied_location_is_a_packed_word()
    {
        if (Database() is not { } database)
        {
            return;
        }

        var known = new[]
        {
            ReadiedLocation.WeaponHand, ReadiedLocation.ShieldHand, ReadiedLocation.BodyArmor,
            ReadiedLocation.Hands, ReadiedLocation.Head, ReadiedLocation.Waist,
            ReadiedLocation.BodyRobe, ReadiedLocation.Back, ReadiedLocation.Feet,
            ReadiedLocation.Fingers, ReadiedLocation.AmmoQuiver, ReadiedLocation.Cannot,
        };

        foreach (var item in database.Items)
        {
            uint location = FruaItemConverter
                .Convert(item, database.Classes[item.ClassIndex]).Combat.LocationReadied;

            Assert.Contains(location, known);
            Assert.True(location > 10, $"location {location} looks like a cast ordinal");
        }
    }

    /// <summary>A weapon always has at least one square of reach.</summary>
    [Fact]
    public void A_weapon_can_always_reach_what_it_stands_next_to()
    {
        if (Database() is not { } database)
        {
            return;
        }

        int weapons = 0;

        foreach (var item in database.Items)
        {
            var itemClass = database.Classes[item.ClassIndex];
            var converted = FruaItemConverter.Convert(item, itemClass);

            if (converted.Tail.WeaponType == FruaItemConverter.NotWeapon)
            {
                continue;
            }

            Assert.True(converted.Tail.RangeMax >= 1,
                        $"{converted.Names.IdName} is a weapon with no reach");
            weapons++;
        }

        Assert.True(weapons > 0, "the design has no weapons");
    }

    /// <summary>
    /// A magic bonus is signed, so a cursed item is a penalty rather than a huge bonus.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(127, 127)]
    [InlineData(255, -1)]
    [InlineData(254, -2)]
    [InlineData(128, -128)]
    public void The_magic_bonus_is_signed(byte stored, int expected) =>
        Assert.Equal(expected, FruaItemConverter.Signed(stored));

    /// <summary>
    /// Nothing is a weapon unless its cutting-or-blunt byte says so.
    /// </summary>
    /// <remarks>
    /// The weapon-type byte is a set of codes, and the reference only consults it at all when
    /// <c>cutting_or_blunt</c> is non-zero — so a shield with a stray code stays a shield.
    /// </remarks>
    [Fact]
    public void The_weapon_type_is_gated_on_cutting_or_blunt()
    {
        // Code 138 is a crossbow, but only when the item cuts or bludgeons.
        var armed = Class(cuttingOrBlunt: 1, weaponType: 138);
        var unarmed = Class(cuttingOrBlunt: 0, weaponType: 138);

        Assert.NotEqual(FruaItemConverter.NotWeapon, FruaItemConverter.WeaponType(armed));
        Assert.Equal(FruaItemConverter.NotWeapon, FruaItemConverter.WeaponType(unarmed));
    }

    /// <summary>The weapon codes map onto the engine's <c>weaponClassType</c>.</summary>
    [Theory]
    [InlineData(138, 1, 6)]     // Crossbow
    [InlineData(26, 1, 7)]      // Throw
    [InlineData(20, 1, 3)]      // HandThrow
    [InlineData(18, 1, 8)]      // Ammo
    [InlineData(15, 1, 5)]      // Bow
    [InlineData(11, 1, 5)]      // Bow, the second code for it
    [InlineData(10, 1, 4)]      // SlingNoAmmo
    [InlineData(4, 1, 2)]       // Code 4 with bit 1: HandCutting
    [InlineData(4, 128, 1)]     // Code 4 with bit 128: HandBlunt
    [InlineData(4, 2, 0)]       // Code 4 with neither: "shouldn't hit this!"
    [InlineData(99, 1, 0)]      // An unknown code is not a weapon
    public void Each_weapon_code_maps_to_its_engine_type(
        byte weaponType, byte cuttingOrBlunt, int expected) =>
        Assert.Equal(expected,
                     FruaItemConverter.WeaponType(Class(cuttingOrBlunt, weaponType)));

    /// <summary>
    /// A quantity is a count of bundles, not of pieces.
    /// </summary>
    /// <remarks>
    /// Twenty arrows is one bundle of twenty. Using the stored count directly would hand out a
    /// twentieth of the ammunition the design intended.
    /// </remarks>
    [Fact]
    public void A_quantity_counts_bundles_not_pieces()
    {
        if (Database() is not { } database)
        {
            return;
        }

        // The first item with a bundle bigger than one is the case worth checking.
        int ordinal = database.Items
            .Select((item, i) => (item, ordinal: i + 1))
            .Where(x => x.item.BundleQuantity > 1)
            .Select(x => x.ordinal)
            .FirstOrDefault();

        if (ordinal == 0)
        {
            return;
        }

        int bundle = database.Items[ordinal - 1].BundleQuantity;

        var one = FruaItemConverter.Instance(ordinal, database, quantity: 1);
        var three = FruaItemConverter.Instance(ordinal, database, quantity: 3);
        var unstated = FruaItemConverter.Instance(ordinal, database);

        Assert.NotNull(one);
        Assert.NotNull(three);
        Assert.NotNull(unstated);

        Assert.Equal(bundle, one.Quantity);
        Assert.Equal(bundle * 3, three.Quantity);

        // No stated quantity leaves the item's own bundle, as AssignItem does before a caller
        // multiplies.
        Assert.Equal(bundle, unstated.Quantity);
    }

    /// <summary>An ordinal outside the one-based range names no item.</summary>
    [Fact]
    public void An_ordinal_outside_the_range_yields_nothing()
    {
        var database = Database();

        // Zero is the empty slot, not the first item.
        Assert.Null(FruaItemConverter.Instance(0, database));
        Assert.Null(FruaItemConverter.Instance(-1, database));
        Assert.Null(FruaItemConverter.Instance(FruaItemConverter.MaxOrdinal + 1, database));

        // Without a database there is nothing to resolve against.
        Assert.Null(FruaItemConverter.Instance(1, null));
    }

    /// <summary>An imported item is carried, not equipped.</summary>
    [Fact]
    public void An_imported_item_is_carried_rather_than_readied()
    {
        if (Database() is not { } database || database.Items.Count == 0)
        {
            return;
        }

        var instance = FruaItemConverter.Instance(1, database);

        Assert.NotNull(instance);
        Assert.Equal(ReadiedLocation.NotReady, instance.ReadyLocation);
    }

    /// <summary>Creatures carry the items their ordinals name.</summary>
    [Fact]
    public void A_creature_carries_its_items()
    {
        if (Runelord() is not { } path)
        {
            return;
        }

        // Runelord keeps its item files in the design directory rather than in a DISK1, so it is
        // its own installation as far as the reader is concerned.
        var design = FruaDesign.Open(path, Path.GetDirectoryName(path));

        if (design.Items is null)
        {
            return;
        }

        int carrying = 0;

        foreach (var creature in design.Monsters.Values)
        {
            var items = creature.IsMonster
                ? FruaCharacterConverter.ToMonster(creature, design.Items).Items?.Items
                : FruaCharacterConverter.ToCharacter(creature, design.Items).Items.Items;

            // Without the database the same creature carries nothing, which is the check that
            // the items really came from it.
            var without = creature.IsMonster
                ? FruaCharacterConverter.ToMonster(creature).Items?.Items
                : FruaCharacterConverter.ToCharacter(creature).Items.Items;

            Assert.Empty(without!);

            if (items is { Count: > 0 })
            {
                carrying++;
                Assert.All(items, i => Assert.True(i.Quantity > 0));
                Assert.All(items, i => Assert.False(string.IsNullOrEmpty(i.ItemId)));
            }
        }

        Assert.True(carrying > 0, "no shipped creature carried a resolvable item");
    }

    private static FruaItemClass Class(byte cuttingOrBlunt, byte weaponType) =>
        new(Slot: 0, TwoHanded: 1,
            VersusLarge: new FruaDamage(1, 6, 0),
            Rate: 1, Protection: 0,
            CuttingOrBlunt: cuttingOrBlunt, Melee: 1,
            VersusSmall: new FruaDamage(1, 6, 0),
            Range: 1, Classes: 0xFF, WeaponType: weaponType);
}
