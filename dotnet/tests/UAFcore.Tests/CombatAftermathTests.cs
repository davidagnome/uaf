using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers what a finished fight is worth.</summary>
public class CombatAftermathTests
{
    private static Combatant Monster(int index, CharacterStatus status = CharacterStatus.Dead) =>
        new(index, isFriendly: false, new CombatantIcon(1, 1), $"orc{index}") { Status = status };

    private static Combatant Hero(int index, CharacterStatus status = CharacterStatus.Okay) =>
        new(index, isFriendly: true, new CombatantIcon(1, 1), $"hero{index}") { Status = status };

    private static ItemInstance Carried(string itemId) =>
        new(Key: 1, itemId, LegacyItemId: 0, ReadyLocation: 0, Quantity: 1, Identified: 1,
            Charges: 1, Cursed: 0, Paid: 0);

    private static ItemRecord Item(WeaponClass weapon = WeaponClass.HandCutting,
                                   int canTrade = 0, int experience = 0) =>
        new(new ItemNames(0, string.Empty, "id", "name", string.Empty, string.Empty, string.Empty),
            HitArt: null, MissileArt: null,
            new ItemScalars(string.Empty, experience, 0, 0, 0, 0, 1, 0),
            new ItemCombat(0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0),
            new ItemTail((int)weapon, 0, 0, [], 0, 0, 0, string.Empty, string.Empty, 0, 0, null,
                         0, canTrade, null!, []));

    // ---- the result a design reads -------------------------------------------------------------

    [Fact]
    public void A_win_is_a_win()
    {
        Assert.Equal(CombatResult.Win,
                     CombatAftermath.ResultOf(CombatOutcome.PartyWon, [Hero(0), Monster(1)]));
        Assert.Equal("Win", CombatAftermath.ResultText(CombatResult.Win));
    }

    [Fact]
    public void A_loss_where_somebody_escaped_is_a_flight()
    {
        // Derived after the fact: the reference settles on MonsterWins and only then scans the
        // party for anyone fled. Any member is enough, not all of them.
        var result = CombatAftermath.ResultOf(
            CombatOutcome.PartyLost,
            [Hero(0, CharacterStatus.Dead), Hero(1, CharacterStatus.Fled), Monster(2)]);

        Assert.Equal(CombatResult.Flee, result);
        Assert.Equal("Flee", CombatAftermath.ResultText(result));
    }

    [Fact]
    public void A_loss_with_nobody_fled_is_a_loss()
    {
        var result = CombatAftermath.ResultOf(CombatOutcome.PartyLost,
                                              [Hero(0, CharacterStatus.Dead), Monster(1)]);

        Assert.Equal(CombatResult.Lose, result);
    }

    [Fact]
    public void A_party_that_never_dies_survives_instead_of_losing()
    {
        var result = CombatAftermath.ResultOf(CombatOutcome.PartyLost,
                                              [Hero(0, CharacterStatus.Dead)],
                                              partyNeverDies: true);

        Assert.Equal(CombatResult.LoseButNeverDies, result);
        Assert.Equal("LoseButNeverDies", CombatAftermath.ResultText(result));
    }

    [Fact]
    public void A_fled_party_member_makes_it_a_flight_even_when_the_party_never_dies()
    {
        // The flee check comes first in the reference's ordering.
        var result = CombatAftermath.ResultOf(CombatOutcome.PartyLost,
                                              [Hero(0, CharacterStatus.Fled)],
                                              partyNeverDies: true);

        Assert.Equal(CombatResult.Flee, result);
    }

    // ---- experience ----------------------------------------------------------------------------

    [Fact]
    public void Only_the_dead_are_worth_experience()
    {
        // A monster that fled, was turned, or is merely unconscious is worth nothing.
        var all = new List<Combatant>
        {
            Hero(0),
            Monster(1, CharacterStatus.Dead),
            Monster(2, CharacterStatus.Fled),
            Monster(3, CharacterStatus.Gone),
            Monster(4, CharacterStatus.Unconscious),
        };

        Assert.Equal(15, CombatAftermath.ExperienceFor(all, _ => 15));
    }

    [Fact]
    public void A_fight_won_by_driving_everything_off_pays_nothing()
    {
        var all = new List<Combatant> { Hero(0), Monster(1, CharacterStatus.Fled) };

        Assert.Equal(0, CombatAftermath.ExperienceFor(all, _ => 100));
    }

    [Fact]
    public void Party_members_are_never_worth_experience()
    {
        var all = new List<Combatant> { Hero(0, CharacterStatus.Dead), Monster(1) };

        Assert.Equal(15, CombatAftermath.ExperienceFor(all, _ => 15));
    }

    [Fact]
    public void The_monster_modifier_is_a_percentage_added_on_top()
    {
        var all = new List<Combatant> { Monster(1) };

        Assert.Equal(100, CombatAftermath.ExperienceFor(all, _ => 100));
        Assert.Equal(200, CombatAftermath.ExperienceFor(all, _ => 100, 100));
        Assert.Equal(150, CombatAftermath.ExperienceFor(all, _ => 100, 50));
    }

    [Fact]
    public void The_no_experience_flag_awards_nothing_at_all()
    {
        var all = new List<Combatant> { Monster(1) };

        Assert.Equal(0, CombatAftermath.ExperienceFor(all, _ => 100, 100,
                                                      partyNoExperience: true));
    }

    [Fact]
    public void A_negative_modifier_cannot_take_the_total_below_zero()
    {
        var all = new List<Combatant> { Monster(1) };

        Assert.Equal(0, CombatAftermath.ExperienceFor(all, _ => 100, -200));
    }

    [Fact]
    public void Treasure_items_carry_experience_of_their_own()
    {
        int found = CombatAftermath.ExperienceIn([Carried("sword"), Carried("ring")],
                                                 _ => Item(experience: 25));

        Assert.Equal(50, found);
    }

    // ---- sharing it out ------------------------------------------------------------------------

    private static Character Member(CharacterStatus status = CharacterStatus.Okay)
    {
        var record = new CharacterRecord(
            0, 0, "human", 0, "fighter", 0, 0, 0, "", 0, "hero", "",
            0, 0, 0, 0, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, [new BaseclassStats("fighter", 0, 0, 0, 0)], [], [], 0, 0, 0,
            null, 0, null, 0, 0, 0, 0, 0, "", 0, "",
            new UAF.Serialization.SpellBook(0, []), 0, 0, [], [], null!,
            new ItemList([], new ReadyItems([])), new SpecabBlock([], [], []), []);

        return new Character(record, UAF.Rules.MoneyRules.Default) { Status = status };
    }

    [Fact]
    public void The_whole_remainder_goes_to_the_first_survivor()
    {
        // Not spread: with three survivors and 100 points the first gets 34 and the rest 33.
        var party = new List<Character> { Member(), Member(), Member() };

        Assert.Equal(3, CombatAftermath.Distribute(party, 100));
        Assert.Equal(34, party[0].TotalExperience);
        Assert.Equal(33, party[1].TotalExperience);
        Assert.Equal(33, party[2].TotalExperience);
    }

    [Fact]
    public void Only_the_unharmed_share()
    {
        // The unconscious, the dying and the dead all get nothing, so a costly win pays the
        // survivors more each.
        var party = new List<Character>
        {
            Member(CharacterStatus.Dead),
            Member(),
            Member(CharacterStatus.Unconscious),
        };

        Assert.Equal(1, CombatAftermath.Distribute(party, 90));
        Assert.Equal(0, party[0].TotalExperience);
        Assert.Equal(90, party[1].TotalExperience);
        Assert.Equal(0, party[2].TotalExperience);
    }

    [Fact]
    public void Nothing_is_shared_when_nobody_is_standing()
    {
        var party = new List<Character> { Member(CharacterStatus.Dead) };

        Assert.Equal(0, CombatAftermath.Distribute(party, 100));
    }

    [Fact]
    public void Nothing_is_shared_when_there_is_nothing_to_share()
    {
        var party = new List<Character> { Member() };

        Assert.Equal(0, CombatAftermath.Distribute(party, 0));
        Assert.Equal(0, CombatAftermath.Distribute(party, -5));
    }

    // ---- treasure ------------------------------------------------------------------------------

    [Fact]
    public void Only_the_dead_are_looted()
    {
        var dead = Monster(1);
        dead.Items.Add(Carried("sword"));
        var fled = Monster(2, CharacterStatus.Fled);
        fled.Items.Add(Carried("axe"));

        var (items, _) = CombatAftermath.Loot([Hero(0), dead, fled], _ => Item());

        Assert.Single(items);
        Assert.Equal("sword", items[0].ItemId);
    }

    [Fact]
    public void A_monsters_spell_casting_items_do_not_drop_by_default()
    {
        // A wand a monster used is kept out of the treasure unless the design marks it tradeable.
        var dead = Monster(1);
        dead.Items.Add(Carried("wand"));

        var (kept, _) = CombatAftermath.Loot([dead], _ => Item(WeaponClass.SpellCaster));
        Assert.Empty(kept);

        var (dropped, _) = CombatAftermath.Loot([dead],
                                                _ => Item(WeaponClass.SpellCaster, canTrade: 1));
        Assert.Single(dropped);
    }

    [Fact]
    public void Spell_like_abilities_are_held_back_the_same_way()
    {
        var dead = Monster(1);
        dead.Items.Add(Carried("breath"));

        var (items, _) = CombatAftermath.Loot([dead], _ => Item(WeaponClass.SpellLikeAbility));

        Assert.Empty(items);
    }

    [Fact]
    public void Thrown_weapons_are_picked_up_off_the_map()
    {
        var (items, _) = CombatAftermath.Loot([Hero(0)], _ => Item(),
                                              hurled: [Carried("dagger")]);

        Assert.Single(items);
        Assert.Equal("dagger", items[0].ItemId);
    }

    [Fact]
    public void The_no_treasure_flag_yields_nothing_including_thrown_weapons()
    {
        var dead = Monster(1);
        dead.Items.Add(Carried("sword"));

        var (items, money) = CombatAftermath.Loot([dead], _ => Item(),
                                                  hurled: [Carried("dagger")],
                                                  noMonsterTreasure: true);

        Assert.Empty(items);
        Assert.Empty(money);
    }

    [Fact]
    public void A_fallen_monsters_purse_is_taken_too()
    {
        var dead = Monster(1);
        dead.Money = new MoneySack([10, 0, 0, 0, 0], [], []);

        var (_, money) = CombatAftermath.Loot([dead], _ => Item());

        Assert.Single(money);
    }
}
