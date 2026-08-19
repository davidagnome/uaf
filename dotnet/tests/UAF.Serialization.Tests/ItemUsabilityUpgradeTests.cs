using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Converting a pre-0.998101 item's <c>Usable_by_Class</c> bitmask into a baseclass list.
/// </summary>
/// <remarks>
/// <b>The point of the conversion is that a legacy design becomes writable.</b> Without it
/// <see cref="ItemRecordWriter"/> refuses every item still carrying a mask, so a design below
/// 0.998101 — the editor's own template among them — could be read and never saved.
/// </remarks>
public class ItemUsabilityUpgradeTests
{
    private const int MagicUser = 1;
    private const int Cleric = 2;
    private const int Thief = 4;
    private const int Fighter = 8;
    private const int Paladin = 16;
    private const int Ranger = 32;
    private const int Druid = 64;

    private static readonly DicePlus NoDice =
        new(string.Empty, string.Empty, string.Empty, 0, 0, 0, 0, 0, 0, []);

    private static ClassRecord Class(int preSpellNameKey, params string[] baseclasses) =>
        new("CL5", preSpellNameKey, $"class{preSpellNameKey}", baseclasses,
            new SpecabBlock([], [], []), [], NoDice,
            new ItemList([], new ReadyItems([])), string.Empty);

    /// <summary>Each bit names the class it stands for.</summary>
    /// <remarks>
    /// <b>The bit order and the class-key order are different</b> — the fighter is bit 8 and key 0,
    /// the magic user bit 1 and key 4 — so a table that derived one from the other would be wrong
    /// for five of the seven.
    /// </remarks>
    [Theory]
    [InlineData(Fighter, "fighter")]
    [InlineData(MagicUser, "magicUser")]
    [InlineData(Cleric, "cleric")]
    [InlineData(Thief, "thief")]
    [InlineData(Paladin, "paladin")]
    [InlineData(Ranger, "ranger")]
    [InlineData(Druid, "druid")]
    public void Each_bit_names_its_class(int mask, string expected) =>
        Assert.Equal([expected], ItemUsabilityUpgrade.BaseclassesFor(mask, []));

    /// <summary>No bits is no baseclasses — an item nobody can use.</summary>
    [Fact]
    public void An_empty_mask_names_nobody() =>
        Assert.Empty(ItemUsabilityUpgrade.BaseclassesFor(0, []));

    /// <summary>
    /// Every bit gives all seven, in the order the reference adds them.
    /// </summary>
    /// <remarks>
    /// Not the bit order: the reference calls <c>AddUsableBaseclass</c> fighter first and magic
    /// user second, where the bits run magic user, cleric, thief, fighter.
    /// </remarks>
    [Fact]
    public void Every_bit_gives_all_seven_in_the_references_order()
    {
        int all = Fighter | MagicUser | Cleric | Thief | Paladin | Ranger | Druid;

        Assert.Equal(
            ["fighter", "magicUser", "cleric", "thief", "paladin", "ranger", "druid"],
            ItemUsabilityUpgrade.BaseclassesFor(all, []));
    }

    /// <summary>A design's own name for a class beats the built-in one.</summary>
    /// <remarks>
    /// The whole reason the conversion needs <c>classes.dat</c>. A design that renamed its fighter
    /// wants its own baseclass named, not <c>"fighter"</c>.
    /// </remarks>
    [Fact]
    public void A_designs_own_class_name_wins()
    {
        // Key 0 is the fighter; this design calls its fighter baseclass something else.
        ClassRecord[] classes = [Class(0, "swordsman", "brawler")];

        // The class's FIRST baseclass, not its name and not its second.
        Assert.Equal(["swordsman"], ItemUsabilityUpgrade.BaseclassesFor(Fighter, classes));

        // A class the design does not define falls back on the built-in name.
        Assert.Equal(["druid"], ItemUsabilityUpgrade.BaseclassesFor(Druid, classes));
    }

    /// <summary>
    /// Two classes renamed onto the same baseclass contribute one entry.
    /// </summary>
    /// <remarks>
    /// The reference checks "already there" against the list being built, so the duplicate is
    /// dropped rather than added twice — and the survivor keeps the earlier position.
    /// </remarks>
    [Fact]
    public void A_duplicate_after_renaming_collapses()
    {
        ClassRecord[] classes = [Class(0, "hero"), Class(4, "hero")];

        Assert.Equal(["hero"], ItemUsabilityUpgrade.BaseclassesFor(Fighter | MagicUser, classes));
    }

    /// <summary>A class with no baseclasses at all falls back rather than yielding nothing.</summary>
    [Fact]
    public void A_class_with_no_baseclasses_falls_back()
    {
        ClassRecord[] classes = [Class(0)];

        Assert.Equal(["fighter"], ItemUsabilityUpgrade.BaseclassesFor(Fighter, classes));
    }

    /// <summary>An upgraded record drops the mask, which is what lets a writer take it.</summary>
    [Fact]
    public void Upgrading_clears_the_mask()
    {
        var item = Item(Fighter | Thief);

        Assert.True(ItemUsabilityUpgrade.NeedsUpgrade(item));

        var upgraded = ItemUsabilityUpgrade.Upgrade(item, []);

        Assert.False(ItemUsabilityUpgrade.NeedsUpgrade(upgraded));
        Assert.Equal(0, upgraded.Tail.LegacyUsableByClass);
        Assert.Equal(["fighter", "thief"], upgraded.Tail.UsableByBaseclass);

        // And the writer will now take it, which is the point.
        Assert.True(ItemRecordWriter.CanWrite(upgraded, out string reason), reason);
    }

    /// <summary>A record with no mask is returned untouched, the same instance.</summary>
    [Fact]
    public void A_modern_record_is_left_alone()
    {
        var item = Item(0);

        Assert.Same(item, ItemUsabilityUpgrade.Upgrade(item, []));
    }

    /// <summary>
    /// A record carrying both keeps the list it already has.
    /// </summary>
    /// <remarks>
    /// The two are alternatives on the wire, so a record with both has been through this once
    /// already and its list is the converted one — recomputing it would undo any later edit.
    /// </remarks>
    [Fact]
    public void A_record_with_both_keeps_its_list()
    {
        var item = Item(Fighter) with
        {
            Tail = Item(Fighter).Tail with { UsableByBaseclass = ["swordsman"] },
        };

        Assert.Equal(["swordsman"], ItemUsabilityUpgrade.Upgrade(item, []).Tail.UsableByBaseclass);
    }

    private static ItemRecord Item(int mask) =>
        new(new ItemNames(0, string.Empty, "thing", "Thing", string.Empty, string.Empty,
                          string.Empty),
            null, null,
            new ItemScalars(string.Empty, 0, 0, 0, 0, 0, 0, 0),
            new ItemCombat(0, 0, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
            new ItemTail(0, 0, mask, [], 0, 0, 0, string.Empty, string.Empty, 0, 0, null, 0, 0,
                         new SpecabBlock([], [], []), []));
}
