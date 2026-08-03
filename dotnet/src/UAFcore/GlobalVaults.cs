using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The fifteen global vaults (<c>GLOBAL_VAULT_DATA vault[MAX_GLOBAL_VAULTS]</c>,
/// <c>GlobalData.h:922</c>) — what the party has left in storage.
/// </summary>
/// <remarks>
/// <para>
/// <b>A vault is global, not per-level.</b> A <c>VAULT_EVENT_DATA</c> carries only a
/// <c>WhichVault</c> index, so two vault events naming the same number are two doors onto one
/// store — which is how a design gives a party its belongings back in a different town.
/// </para>
/// <para>
/// <b>Fifteen of them, always.</b> The array is fixed and the savegame writes every slot whether
/// or not anything is in it, so an empty vault is a record rather than an absence.
/// </para>
/// </remarks>
public sealed class GlobalVaults
{
    /// <summary><c>MAX_GLOBAL_VAULTS</c> (<c>GlobalData.h:425</c>).</summary>
    public const int Count = 15;

    private readonly List<ItemInstance>[] items;
    private readonly Purse[] money;

    public GlobalVaults(MoneyRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        items = [.. Enumerable.Range(0, Count).Select(_ => new List<ItemInstance>())];
        money = [.. Enumerable.Range(0, Count).Select(_ => new Purse(rules))];
    }

    /// <summary>Whether a vault number names one of the fifteen.</summary>
    public static bool IsValid(int vault) => vault >= 0 && vault < Count;

    /// <summary>What is stored in a vault. An out-of-range number gives an empty list.</summary>
    public IReadOnlyList<ItemInstance> ItemsIn(int vault) =>
        IsValid(vault) ? items[vault] : [];

    /// <summary>A vault's money, or null for an out-of-range number.</summary>
    public Purse? MoneyIn(int vault) => IsValid(vault) ? money[vault] : null;

    /// <summary>Puts an item in.</summary>
    public void Deposit(int vault, ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (IsValid(vault))
        {
            items[vault].Add(item);
        }
    }

    /// <summary>Takes an item out by its position in the vault's list.</summary>
    /// <returns>The item, or null when there is nothing at that index.</returns>
    public ItemInstance? Withdraw(int vault, int index)
    {
        if (!IsValid(vault) || index < 0 || index >= items[vault].Count)
        {
            return null;
        }

        var item = items[vault][index];
        items[vault].RemoveAt(index);
        return item;
    }

    /// <summary>The savegame's shape: all fifteen, in order.</summary>
    public List<Vault> ToRecords() =>
        [.. Enumerable.Range(0, Count).Select(
            v => new Vault(money[v].ToRecord(), new ItemList([.. items[v]], new ReadyItems([]))))];

    /// <summary>Rebuilds from a savegame's records.</summary>
    /// <remarks>
    /// A save with fewer than fifteen leaves the rest empty rather than failing — the count is a
    /// compile-time constant on both sides, so a mismatch means a file this port should still
    /// open.
    /// </remarks>
    public static GlobalVaults FromRecords(IReadOnlyList<Vault> records, MoneyRules rules)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(rules);

        var vaults = new GlobalVaults(rules);
        for (int v = 0; v < Math.Min(records.Count, Count); v++)
        {
            vaults.items[v].AddRange(records[v].Items.Items);
            vaults.money[v] = Purse.FromRecord(records[v].Money, rules);
        }
        return vaults;
    }
}
