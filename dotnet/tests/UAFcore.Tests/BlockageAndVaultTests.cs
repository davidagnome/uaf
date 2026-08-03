using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the last two pieces of live state a savegame carries: what the party has got past, and
/// what it has left in storage.
/// </summary>
public class BlockageAndVaultTests
{
    // ---- blockages -----------------------------------------------------------------------------

    [Fact]
    public void Everything_is_blocked_until_something_is_cleared()
    {
        // The list records clearances, not blockages: an empty one is a dungeon where nothing has
        // been opened, and "not found" means still secret.
        var cleared = new BlockageClearances();

        Assert.True(cleared.IsBlocked(0, 5, 5, Facing.North, Clearable.Secret));
        Assert.Equal(BlockageClearances.AllBlocked, cleared.FlagsAt(0, 5, 5));
        Assert.Equal(0, cleared.Count);
    }

    [Fact]
    public void Clearing_one_way_leaves_the_other_three_alone()
    {
        var cleared = new BlockageClearances();
        cleared.Clear(0, 5, 5, Facing.North, Clearable.Secret);

        Assert.False(cleared.IsBlocked(0, 5, 5, Facing.North, Clearable.Secret));
        Assert.True(cleared.IsBlocked(0, 5, 5, Facing.South, Clearable.Secret));
        Assert.True(cleared.IsBlocked(0, 5, 5, Facing.East, Clearable.Secret));
        Assert.True(cleared.IsBlocked(0, 5, 5, Facing.West, Clearable.Secret));
    }

    [Fact]
    public void Clearing_one_kind_leaves_the_others_on_the_same_wall()
    {
        var cleared = new BlockageClearances();
        cleared.Clear(0, 5, 5, Facing.North, Clearable.Locked);

        Assert.False(cleared.IsBlocked(0, 5, 5, Facing.North, Clearable.Locked));
        Assert.True(cleared.IsBlocked(0, 5, 5, Facing.North, Clearable.Secret));
        Assert.True(cleared.IsBlocked(0, 5, 5, Facing.North, Clearable.Spelled));
    }

    [Fact]
    public void The_bit_groups_are_not_in_facing_order()
    {
        // Bit groups run North, South, East, West; facings run North, East, South, West. Indexing
        // the flags by the facing value swaps east and south -- a secret door found to the east
        // would open one to the south and stay shut.
        Assert.Equal(0, BlockageClearances.GroupOf(Facing.North));
        Assert.Equal(1, BlockageClearances.GroupOf(Facing.South));
        Assert.Equal(2, BlockageClearances.GroupOf(Facing.East));
        Assert.Equal(3, BlockageClearances.GroupOf(Facing.West));

        Assert.NotEqual((int)Facing.East, BlockageClearances.GroupOf(Facing.East));
        Assert.NotEqual((int)Facing.South, BlockageClearances.GroupOf(Facing.South));
    }

    [Fact]
    public void Clearing_east_does_not_clear_south()
    {
        var cleared = new BlockageClearances();
        cleared.Clear(0, 5, 5, Facing.East, Clearable.Secret);

        Assert.False(cleared.IsBlocked(0, 5, 5, Facing.East, Clearable.Secret));
        Assert.True(cleared.IsBlocked(0, 5, 5, Facing.South, Clearable.Secret));
    }

    [Fact]
    public void Clearances_are_per_cell_and_per_level()
    {
        var cleared = new BlockageClearances();
        cleared.Clear(0, 5, 5, Facing.North, Clearable.Secret);

        Assert.True(cleared.IsBlocked(0, 6, 5, Facing.North, Clearable.Secret));
        Assert.True(cleared.IsBlocked(1, 5, 5, Facing.North, Clearable.Secret));
    }

    [Fact]
    public void A_cleared_cell_survives_a_round_trip_through_the_savegame_shape()
    {
        var before = new BlockageClearances();
        before.Clear(2, 10, 20, Facing.West, Clearable.Spelled);
        before.Clear(2, 10, 20, Facing.North, Clearable.Locked);
        before.Clear(0, 1, 1, Facing.East, Clearable.Secret);

        var after = BlockageClearances.FromRecords(before.ToRecords());

        Assert.False(after.IsBlocked(2, 10, 20, Facing.West, Clearable.Spelled));
        Assert.False(after.IsBlocked(2, 10, 20, Facing.North, Clearable.Locked));
        Assert.True(after.IsBlocked(2, 10, 20, Facing.North, Clearable.Secret));
        Assert.False(after.IsBlocked(0, 1, 1, Facing.East, Clearable.Secret));
        Assert.Equal(2, after.Count);
    }

    [Fact]
    public void One_record_covers_one_cell_however_many_ways_are_cleared()
    {
        var cleared = new BlockageClearances();
        cleared.Clear(0, 5, 5, Facing.North, Clearable.Secret);
        cleared.Clear(0, 5, 5, Facing.South, Clearable.Locked);

        var records = cleared.ToRecords();

        Assert.Single(records);
        Assert.Equal(5, records[0].X);
        Assert.Equal(5, records[0].Y);
    }

    // ---- vaults --------------------------------------------------------------------------------

    private static ItemInstance Item(string id) =>
        new(0, id, 0, Inventory.NotReady, 1, 1, 0, 0, 0);

    [Fact]
    public void There_are_fifteen_vaults()
    {
        Assert.Equal(15, GlobalVaults.Count);
        Assert.True(GlobalVaults.IsValid(0));
        Assert.True(GlobalVaults.IsValid(14));
        Assert.False(GlobalVaults.IsValid(15));
        Assert.False(GlobalVaults.IsValid(-1));
    }

    [Fact]
    public void A_vault_starts_empty()
    {
        var vaults = new GlobalVaults(MoneyRules.Default);

        Assert.Empty(vaults.ItemsIn(3));
        Assert.True(vaults.MoneyIn(3)!.IsEmpty);
    }

    [Fact]
    public void What_goes_in_comes_out()
    {
        var vaults = new GlobalVaults(MoneyRules.Default);
        vaults.Deposit(3, Item("Long Sword"));

        Assert.Single(vaults.ItemsIn(3));

        var taken = vaults.Withdraw(3, 0);

        Assert.Equal("Long Sword", taken!.ItemId);
        Assert.Empty(vaults.ItemsIn(3));
    }

    [Fact]
    public void Two_doors_onto_the_same_number_are_one_store()
    {
        // A vault event carries only a WhichVault index -- which is how a design gives a party its
        // belongings back in a different town.
        var vaults = new GlobalVaults(MoneyRules.Default);
        vaults.Deposit(3, Item("Long Sword"));

        Assert.Single(vaults.ItemsIn(3));
        Assert.Empty(vaults.ItemsIn(4));
    }

    [Fact]
    public void An_out_of_range_vault_swallows_nothing()
    {
        var vaults = new GlobalVaults(MoneyRules.Default);
        vaults.Deposit(99, Item("Long Sword"));

        Assert.Empty(vaults.ItemsIn(99));
        Assert.Null(vaults.MoneyIn(99));
        Assert.Null(vaults.Withdraw(99, 0));
    }

    [Fact]
    public void Withdrawing_what_is_not_there_gives_nothing()
    {
        var vaults = new GlobalVaults(MoneyRules.Default);

        Assert.Null(vaults.Withdraw(0, 0));
        Assert.Null(vaults.Withdraw(0, -1));
    }

    [Fact]
    public void Every_vault_is_written_whether_or_not_anything_is_in_it()
    {
        // The array is fixed and the savegame writes all fifteen, so an empty vault is a record
        // rather than an absence.
        var vaults = new GlobalVaults(MoneyRules.Default);
        vaults.Deposit(7, Item("Long Sword"));

        var records = vaults.ToRecords();

        Assert.Equal(GlobalVaults.Count, records.Count);
        Assert.Single(records[7].Items.Items);
        Assert.Empty(records[0].Items.Items);
    }

    [Fact]
    public void A_purse_writes_all_ten_coin_slots()
    {
        // MONEY_SACK blits a fixed array, so an inactive denomination still occupies its slot and
        // a writer that emitted only the active ones would shift everything after it.
        var purse = new Purse(MoneyRules.Default);
        purse.Add(MoneyRules.Default.BaseType, 50);

        var sack = purse.ToRecord();

        Assert.Equal(MoneyRules.MaxCoinTypes, sack.Coins.Count);
        Assert.Equal(50, sack.Coins[MoneyRules.IndexOf(MoneyRules.Default.BaseType)]);
    }

    [Fact]
    public void The_vaults_survive_a_round_trip_through_the_savegame_shape()
    {
        var before = new GlobalVaults(MoneyRules.Default);
        before.Deposit(0, Item("Long Sword"));
        before.Deposit(14, Item("Shield"));
        before.MoneyIn(0)!.Add(MoneyRules.Default.BaseType, 250);

        var after = GlobalVaults.FromRecords(before.ToRecords(), MoneyRules.Default);

        Assert.Equal("Long Sword", after.ItemsIn(0)[0].ItemId);
        Assert.Equal("Shield", after.ItemsIn(14)[0].ItemId);
        Assert.Equal(250, after.MoneyIn(0)![MoneyRules.Default.BaseType]);
        Assert.Empty(after.ItemsIn(7));
    }

    [Fact]
    public void A_save_with_fewer_vaults_leaves_the_rest_empty()
    {
        var records = new List<Vault>
        {
            new(new Purse(MoneyRules.Default).ToRecord(),
                new ItemList([Item("Long Sword")], new ReadyItems([]))),
        };

        var vaults = GlobalVaults.FromRecords(records, MoneyRules.Default);

        Assert.Single(vaults.ItemsIn(0));
        Assert.Empty(vaults.ItemsIn(1));
    }

    // ---- what this unblocks ---------------------------------------------------------------------

    [Fact]
    public void Nothing_is_left_on_the_list_of_what_a_save_cannot_carry()
    {
        // All five are tracked. What remains is the projection itself, which is a different job
        // from keeping the state.
        Assert.Empty(SaveGameProjection.Untracked);
    }
}
