using UAF.Import.Frua;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// A whole design, with the monster and NPC references the reference importer drops.
/// </summary>
public class FruaDesignTests
{
    private static string? Corpus(params string[] parts)
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

        string path = Path.Combine([dir.FullName, "reference", .. parts]);
        return Directory.Exists(path) ? path : null;
    }

    private static string? Heirs() =>
        Corpus("Unlimited Adventures -ENG", "DESIGNS", "UA", "HEIRS.DSN");

    [Fact]
    public void The_stock_labels_came_across_whole()
    {
        Assert.Equal(128, FruaMonsterLabels.Count);
        Assert.Equal("Kobold", FruaMonsterLabels.Name(1));
        Assert.Equal("Goblin", FruaMonsterLabels.Name(2));
        Assert.Equal("Orc", FruaMonsterLabels.Name(3));

        // Slot 0 holds "none" in the table, but the reference can never look it up.
        Assert.Null(FruaMonsterLabels.Name(0));
        Assert.Null(FruaMonsterLabels.Name(128));
    }

    [Fact]
    public void A_design_opens_with_its_levels_and_monsters()
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        var design = FruaDesign.Open(path);

        Assert.Equal("Heirs to skull crag", design.Game.DesignName);
        Assert.Equal(26, design.Levels.Count);
        Assert.Equal(4, design.Monsters.Count);
        Assert.Null(design.Items);   // no UA installation supplied
    }

    /// <summary>
    /// An index the design ships a record for resolves to that record.
    /// </summary>
    /// <remarks>
    /// This is the join the reference cannot make: its <c>GetMonsterKey</c> is commented out, so
    /// nothing ever connects an event's index to a <c>MONST###.DAT</c>.
    /// </remarks>
    [Fact]
    public void A_designs_own_monster_wins_over_the_stock_label()
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        var design = FruaDesign.Open(path);
        var m = design.Monster(101);

        Assert.NotNull(m);
        Assert.Equal("Khulzond", m.Name);
        Assert.NotNull(m.Record);
        Assert.Equal(14, m.Record.Level);
        Assert.False(m.IsNpc);
    }

    /// <summary>An index the design does not override falls back to the stock name.</summary>
    [Fact]
    public void An_index_without_a_record_falls_back_to_the_stock_name()
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        var design = FruaDesign.Open(path);
        var m = design.Monster(1);

        Assert.NotNull(m);
        Assert.Equal("Kobold", m.Name);
        Assert.Null(m.Record);
    }

    /// <summary>A MONST record that is an NPC is reported as one.</summary>
    [Fact]
    public void An_npc_record_is_flagged_as_such()
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        var design = FruaDesign.Open(path);
        var m = design.Monster(109);

        Assert.NotNull(m);
        Assert.Equal("xelez-dar", m.Name);
        Assert.True(m.IsNpc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(128)]
    [InlineData(500)]
    public void An_index_outside_the_range_is_not_a_monster(int index)
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        Assert.Null(FruaDesign.Open(path).Monster(index));
    }

    /// <summary>
    /// Real combat events field real monsters — the thing the reference importer loses.
    /// </summary>
    [Fact]
    public void Shipped_combats_resolve_to_named_monsters()
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        var design = FruaDesign.Open(path);
        int encounters = 0;
        int fielded = 0;
        var named = new HashSet<string>();

        foreach (var (_, level) in design.Levels)
        {
            foreach (var e in level.Events.Where(e => e.Type is FruaEventType.Combat
                                                            or FruaEventType.PickOneCombat))
            {
                var combat = FruaCombatEvent.Read(e);
                encounters++;

                foreach (var (monster, quantity) in design.MonstersIn(combat))
                {
                    Assert.InRange(quantity, 1, 31);
                    Assert.False(string.IsNullOrWhiteSpace(monster.Name));
                    named.Add(monster.Name);
                    fielded++;
                }
            }
        }

        Assert.True(encounters > 100, $"only {encounters} combats");
        Assert.True(fielded > 50, $"only {fielded} monster slots resolved");
        Assert.True(named.Count > 5,
                    $"only {named.Count} distinct monsters across the whole design");
    }

    /// <summary>Add- and remove-NPC events resolve to a named NPC or monster.</summary>
    [Fact]
    public void Shipped_npc_events_resolve()
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        var design = FruaDesign.Open(path);
        int resolved = 0;

        foreach (var (_, level) in design.Levels)
        {
            foreach (var e in level.Events)
            {
                var npc = e.Type switch
                {
                    FruaEventType.AddNpc or FruaEventType.RemoveNpc => FruaNpcEvent.Read(e),
                    FruaEventType.NpcSays => FruaNpcEvent.ReadSays(e),
                    _ => null,
                };

                if (npc is not null && design.NpcIn(npc) is { } who)
                {
                    Assert.False(string.IsNullOrWhiteSpace(who.Name));
                    resolved++;
                }
            }
        }

        Assert.True(resolved > 0, "no NPC event resolved to anybody");
    }

    /// <summary>With a UA installation, the item database loads too.</summary>
    [Fact]
    public void A_design_opened_with_a_ua_installation_has_items()
    {
        if (Heirs() is not { } path
            || Corpus("Unlimited Adventures -ENG", "GAME", "UA") is not { } ua)
        {
            return;
        }

        var design = FruaDesign.Open(path, ua);

        Assert.NotNull(design.Items);
        Assert.Equal(254, design.Items.Items.Count);
        Assert.Equal("Battle Axe", design.Items.Items[1].Name);
    }
}
