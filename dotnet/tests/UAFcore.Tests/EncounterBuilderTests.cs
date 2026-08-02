using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers building an encounter's combatants from a combat event
/// (<c>AddMonstersToCombatants</c>, <c>Combatants.cpp:660</c>).
/// </summary>
public class EncounterBuilderTests
{
    private static MonsterEvent Entry(string id, int quantity = 1, int useQty = 0,
                                      int sides = 0, int qty = 0, int bonus = 0,
                                      int friendly = 0) =>
        new(quantity, Type: 3, id, CharacterId: string.Empty, friendly, MoraleAdjustment: 0,
            sides, qty, bonus, useQty, Money: null, Items: new ItemList([], new ReadyItems([])));

    private static CombatEvent Event(IReadOnlyList<MonsterEvent> monsters, int random = 0) =>
        new(Base: null!, string.Empty, string.Empty, string.Empty,
            Distance: 2, Direction: 0, Surprise: 0, AutoApproach: 0,
            Outdoors: 0, NoMonsterTreasure: 0, PartyNeverDies: 0, NoMagic: 0,
            MonsterMorale: 50, Terrain: 0, RandomMonster: random, PartyNoExperience: 0,
            BackgroundSounds: null!, monsters);

    private static MonsterRecord Monster(string name, int movement = 9, int attacks = 1,
                                         string undead = "") =>
        new(0, name, null, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            Intelligence: 8, ArmorClass: 6, movement, HitDice: 1, UseHitDice: 1, HitDiceBonus: 0,
            Thac0: 19,
            Attacks: [.. Enumerable.Repeat(
                new AttackDetails(6, 1, 0, string.Empty, string.Empty, 0, 0, 0), attacks)],
            MagicResistance: 0, Size: 1, ClassId: string.Empty, Morale: 50, ExperienceValue: 15,
            FormType: 0, PenaltyType: 0, ImmunityType: 0, MiscOptionsType: 0, UndeadType: undead,
            SpecialAbilities: null!, Attributes: [], Items: null, Money: null);

    private static List<Combatant> Party(int size) =>
        [.. Enumerable.Range(0, size).Select(i =>
            new Combatant(i, isFriendly: true, new CombatantIcon(1, 1), $"hero{i}"))];

    /// <summary>A roller that always returns the same face, as the reference's would sum.</summary>
    private static Func<int, int, int, int> Fixed(int face) =>
        (sides, times, bonus) => sides <= 0 || times <= 0 ? bonus : (face * times) + bonus;

    [Fact]
    public void The_party_goes_in_first_and_keeps_its_order()
    {
        // Combatant indices are grid occupancy values and placement takes the party from the front
        // of the list, so this is not merely tidy.
        var all = EncounterBuilder.Build(Event([Entry("orc", quantity: 2)]), Party(4),
                                         Fixed(1), _ => Monster("Orc"));

        Assert.Equal(6, all.Count);
        Assert.All(all.Take(4), c => Assert.True(c.IsFriendly));
        Assert.All(all.Skip(4), c => Assert.False(c.IsFriendly));
        Assert.Equal([0, 1, 2, 3, 4, 5], all.Select(c => c.Index));
    }

    [Fact]
    public void A_literal_quantity_is_used_as_written()
    {
        var all = EncounterBuilder.Build(Event([Entry("orc", quantity: 3)]), Party(1),
                                         (_, _, _) => throw new InvalidOperationException("rolled"),
                                         _ => Monster("Orc"));

        Assert.Equal(3, all.Count(c => !c.IsFriendly));
    }

    [Fact]
    public void A_dice_quantity_is_rolled()
    {
        // useQty non-zero means roll: 2d6 with a fixed face of 4 gives 8.
        var all = EncounterBuilder.Build(
            Event([Entry("orc", useQty: 1, sides: 6, qty: 2, bonus: 0)]), Party(1),
            Fixed(4), _ => Monster("Orc"));

        Assert.Equal(8, all.Count(c => !c.IsFriendly));
    }

    [Fact]
    public void A_quantity_of_zero_still_produces_one_monster()
    {
        var all = EncounterBuilder.Build(Event([Entry("orc", quantity: 0)]), Party(1),
                                         Fixed(1), _ => Monster("Orc"));

        Assert.Equal(1, all.Count(c => !c.IsFriendly));
    }

    [Fact]
    public void The_quantity_modifier_scales_the_group_and_truncates()
    {
        // +50% on a roll of 3 gives 4, not 4.5.
        var all = EncounterBuilder.Build(Event([Entry("orc", quantity: 3)]), Party(1),
                                         Fixed(1), _ => Monster("Orc"),
                                         quantityModPercent: 50);

        Assert.Equal(4, all.Count(c => !c.IsFriendly));
    }

    [Fact]
    public void Every_entry_appears_when_the_encounter_is_not_random()
    {
        var all = EncounterBuilder.Build(
            Event([Entry("orc", quantity: 2), Entry("goblin", quantity: 3)]), Party(1),
            Fixed(1), id => Monster(id));

        Assert.Equal(2, all.Count(c => c.Name == "orc"));
        Assert.Equal(3, all.Count(c => c.Name == "goblin"));
    }

    [Fact]
    public void A_random_encounter_picks_one_entry_but_still_rolls_its_quantity()
    {
        // "Random" means which kind shows up, not how many.
        var all = EncounterBuilder.Build(
            Event([Entry("orc", quantity: 2), Entry("goblin", quantity: 3)], random: 1),
            Party(1), Fixed(2), id => Monster(id));      // roll of 2 picks the second entry

        Assert.Equal(0, all.Count(c => c.Name == "orc"));
        Assert.Equal(3, all.Count(c => c.Name == "goblin"));
    }

    [Fact]
    public void A_random_pick_outside_the_list_is_clamped()
    {
        var all = EncounterBuilder.Build(
            Event([Entry("orc", quantity: 1)], random: 1), Party(1),
            Fixed(99), id => Monster(id));

        Assert.Equal(1, all.Count(c => c.Name == "orc"));
    }

    [Fact]
    public void An_event_with_no_monsters_yields_the_party_alone()
    {
        var all = EncounterBuilder.Build(Event([]), Party(3),
                                         (_, _, _) => throw new InvalidOperationException("rolled"),
                                         _ => throw new InvalidOperationException("looked up"));

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void A_monster_the_design_does_not_define_is_skipped()
    {
        var all = EncounterBuilder.Build(
            Event([Entry("orc", quantity: 2), Entry("ghost", quantity: 2)]), Party(1),
            Fixed(1), id => id == "orc" ? Monster("Orc") : null);

        Assert.Equal(2, all.Count(c => !c.IsFriendly));
    }

    [Fact]
    public void The_encounter_never_exceeds_the_combatant_limit()
    {
        var all = EncounterBuilder.Build(Event([Entry("orc", quantity: 500)]), Party(6),
                                         Fixed(1), _ => Monster("Orc"));

        Assert.Equal(EncounterBuilder.MaxCombatants, all.Count);
    }

    [Fact]
    public void Monsters_come_out_computer_run_with_their_records_numbers()
    {
        var all = EncounterBuilder.Build(Event([Entry("wight", quantity: 1)]), Party(1),
                                         Fixed(1),
                                         _ => Monster("Wight", movement: 12, attacks: 3,
                                                      undead: "wight"));

        var monster = all.Single(c => !c.IsFriendly);
        Assert.True(monster.IsAuto);
        Assert.Equal(CombatantKind.Monster, monster.Kind);
        Assert.Equal(12, monster.MaxMovement);
        Assert.Equal(3, monster.TotalAttacks);
        Assert.True(monster.IsUndead);
    }

    [Fact]
    public void A_friendly_monster_fights_on_the_partys_side()
    {
        var all = EncounterBuilder.Build(Event([Entry("hound", quantity: 1, friendly: 1)]),
                                         Party(1), Fixed(1), _ => Monster("Hound"));

        Assert.Equal(2, all.Count(c => c.IsFriendly));
    }

    [Fact]
    public void An_encounter_built_from_an_event_can_be_placed_and_fought()
    {
        // End to end: event -> combatants -> map -> everybody placed on passable ground.
        string? root = ReferenceDesign();
        if (root is null)
        {
            return;
        }

        using var design = LoadedDesign.Open(root);
        var level = design.Level(0);
        var levelMap = design.Map(0);
        if (level is null || levelMap is null)
        {
            return;
        }

        var all = EncounterBuilder.Build(
            Event([Entry("orc", quantity: 4)]), Party(4), Fixed(1), _ => Monster("Orc"));

        var setup = CombatSetup.Begin(levelMap, level.WallSets, 5, 5, Facing.North, all);

        Assert.Equal(8, setup.Positions.Count);
        Assert.All(setup.Positions.Where(p => p.IsPlaced),
                   p => Assert.True(setup.Map.IsPassable(p.X, p.Y)));
        Assert.All(setup.Positions.Take(4), p => Assert.True(p.IsPlaced));
    }

    private static string? ReferenceDesign()
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

        string design = Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");
        return Directory.Exists(design) ? design : null;
    }
}
