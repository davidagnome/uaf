namespace UAF.Import.Frua;

/// <summary>
/// What a monster index in an event resolves to.
/// </summary>
/// <param name="Index">The stored index, 1–127.</param>
/// <param name="Name">
/// The monster's name: the design's own record if it ships one, otherwise the stock FRUA name.
/// </param>
/// <param name="Record">
/// The design's <c>MONST###.DAT</c> record, or null when the index names a stock monster the
/// design does not override.
/// </param>
/// <param name="IsNpc">
/// Whether the design's record is an NPC rather than a monster — race 0 and combat mode 1, say —
/// which the reference sends down a different import path.
/// </param>
public sealed record FruaMonsterReference(
    int Index, string Name, FruaCharacter? Record, bool IsNpc);

/// <summary>
/// A whole DOS FRUA design: its header, levels, monsters and item database, with monster
/// references resolved.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the piece the reference importer does not have.</b> Its own
/// <c>GetMonsterKey</c> is commented out, and with it every assignment that would put a monster
/// into a combat event or an NPC into an add-NPC event — nine of the fourteen
/// <c>NotImplemented</c> markers in <c>UAImport.cpp</c>. A design imported by the reference
/// therefore arrives with quantities but no monsters.
/// </para>
/// <para>
/// <b>This port resolves them, deliberately diverging.</b> The goal is a design that loads
/// correctly rather than one that reproduces a gap, and the mechanism the reference intended is
/// unambiguous from the code that remains: <c>Monster_Keys[index]</c> is filled in by
/// <c>ImportMonsterToUAF</c> from each <c>MONST###.DAT</c>'s own <c>monsterIndex</c>, with
/// <c>MonsterLabels[index]</c> as the fallback. Both tiers are implemented here.
/// </para>
/// </remarks>
public sealed class FruaDesign
{
    private readonly IReadOnlyDictionary<int, FruaCharacter> monsters;

    private FruaDesign(string root, FruaGameData game,
                       IReadOnlyDictionary<int, FruaLevel> levels,
                       IReadOnlyDictionary<int, FruaCharacter> monsters,
                       FruaItemDatabase? items)
    {
        Root = root;
        Game = game;
        Levels = levels;
        this.monsters = monsters;
        Items = items;
    }

    /// <summary>The design directory.</summary>
    public string Root { get; }

    /// <summary>Its <c>game001.dat</c>.</summary>
    public FruaGameData Game { get; }

    /// <summary>Its levels, keyed by their one-based number. Gaps are ordinary.</summary>
    public IReadOnlyDictionary<int, FruaLevel> Levels { get; }

    /// <summary>Its <c>MONST###.DAT</c> records, keyed by index.</summary>
    public IReadOnlyDictionary<int, FruaCharacter> Monsters => monsters;

    /// <summary>The item database, when a UA installation was supplied.</summary>
    public FruaItemDatabase? Items { get; }

    /// <summary>
    /// Opens a design.
    /// </summary>
    /// <param name="uaInstallation">
    /// Optional path to a FRUA installation, whose <c>DISK1</c> holds the stock item database.
    /// Only consulted when the design ships no item files of its own. Without either,
    /// <see cref="Items"/> is null, which is what leaving the dialog's box unticked does in the
    /// reference.
    /// </param>
    public static FruaDesign Open(string designDirectory, string? uaInstallation = null)
    {
        ArgumentNullException.ThrowIfNull(designDirectory);

        // A design that ships its own item files uses them; otherwise it inherits the stock ones
        // from the installation's DISK1. Most designs ship none -- HEIRS.DSN has neither file --
        // and RUNELORD.DSN is the corpus's only example of one that does.
        var items = FruaItemDatabase.Read(designDirectory)
                    ?? (uaInstallation is null
                        ? null
                        : FruaItemDatabase.Read(Path.Combine(uaInstallation, "DISK1")));

        return new FruaDesign(
            designDirectory,
            FruaGameData.ReadFile(designDirectory),
            FruaLevel.ReadAll(designDirectory),
            FruaCharacter.ReadAll(designDirectory),
            items);
    }

    /// <summary>
    /// Resolves a monster index to a name and, where the design ships one, a record.
    /// </summary>
    /// <remarks>
    /// <b>Two tiers, the design's own first</b> — which is the order
    /// <c>GetMonsterKey</c> reads: <c>Monster_Keys[index]</c>, then the stock label. An index of 0,
    /// or one past 127, is not a monster at all and yields null.
    /// </remarks>
    public FruaMonsterReference? Monster(int index)
    {
        if (index <= 0 || index >= FruaMonsterLabels.Count)
        {
            return null;
        }

        if (monsters.TryGetValue(index, out var record))
        {
            return new FruaMonsterReference(index, record.Name, record, !record.IsMonster);
        }

        return FruaMonsterLabels.Name(index) is { } stock
            ? new FruaMonsterReference(index, stock, null, false)
            : null;
    }

    /// <summary>
    /// The name of one of the eight special keys, or empty for an index the design has no key for.
    /// </summary>
    /// <remarks>
    /// An event's trigger names a key by a number and the engine's control block wants a name, so
    /// this is what stands between the two. The bound is the design's own list rather than a fixed
    /// eight, since a design may name fewer.
    /// </remarks>
    public string KeyName(int index) =>
        index >= 0 && index < Game.SpecialKeys.Count ? Game.SpecialKeys[index] : string.Empty;

    /// <summary>The name of one of the twelve special items, or empty.</summary>
    public string SpecialItemName(int index) =>
        index >= 0 && index < Game.SpecialItems.Count ? Game.SpecialItems[index] : string.Empty;

    /// <summary>
    /// Every monster a combat event calls for, with its quantity, resolved.
    /// </summary>
    /// <remarks>
    /// Empty slots — quantity 0, or an index naming nothing — are dropped, so the result is what
    /// the encounter actually fields.
    /// </remarks>
    public IEnumerable<(FruaMonsterReference Monster, int Quantity)> MonstersIn(FruaCombatEvent combat)
    {
        ArgumentNullException.ThrowIfNull(combat);

        foreach (var slot in combat.Monsters)
        {
            if (slot.Quantity > 0 && Monster(slot.MonsterIndex) is { } monster)
            {
                yield return (monster, slot.Quantity);
            }
        }
    }

    /// <summary>The NPC an add-, remove- or says-event names, or null.</summary>
    public FruaMonsterReference? NpcIn(FruaNpcEvent npc)
    {
        ArgumentNullException.ThrowIfNull(npc);

        return Monster(npc.NpcIndex);
    }
}
