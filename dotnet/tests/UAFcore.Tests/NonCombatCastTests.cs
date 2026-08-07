using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the cast list, the six silent refusals, and who a cast lands on.</summary>
public class NonCombatCastTests
{
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static DicePlus Dice(string text) =>
        new("DP2", text, string.Empty, 0, 0, 0, 0, 0, 1, []);

    private static SpellRecord Spell(
        string name = "cure",
        SpellTargeting targeting = SpellTargeting.WholeParty,
        int restrictions = NonCombatCast.InCamp,
        params string[] parameters) =>
        new(0, name, string.Empty, string.Empty, [],
            Level: 1, CastingTime: 0, CastingTimeType: 0,
            CanTargetFriend: 1, CanTargetEnemy: 0, IsCumulative: 1, Restrictions: restrictions,
            CanBeDispelled: 1, CanMemorize: 1, AllowScribe: 0, AutoScribe: 0,
            Lingers: 0, LingerOnceOnly: 0,
            SaveVersus: 0, SaveResult: 0, Targeting: (int)targeting,
            DurationRate: (int)SpellDurationRate.Permanent, CastCost: 0, CastPriority: 0,
            Parameters: [.. parameters.Select(Dice)], Effects: [], CastArt: null, Art: [],
            Sounds: [], CastMessage: string.Empty, Scripts: [], EffectDuration: null,
            SpecialAbilities: null!, Attributes: []);

    private static Character Member(string name = "Aramil", byte type = 0)
    {
        var record = new CharacterRecord(
            0, 0, type, "human", 0, "cleric", 0, 0, 0, "", 0, name, name,
            0, 0, 0, 0, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, [], [], [], 0, 0, 0, null, 0,
            null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, new ItemList([], new ReadyItems([])),
            new SpecabBlock([], [], []), []);

        return new Character(record, MoneyRules.Default);
    }

    /// <summary>Rolls a DICEPLUS by reading its literal text, which is all these fixtures use.</summary>
    private static int Roll(DicePlus dice) => int.TryParse(dice.Text, out int n) ? n : 0;

    // ---- the list ----------------------------------------------------------------------------

    private static SpellList Book(params (string Id, int Memorized)[] spells)
    {
        var book = new SpellList();
        foreach (var (id, memorized) in spells)
        {
            book.Add(id, 1, memorized);
        }
        return book;
    }

    [Fact]
    public void Only_memorised_spells_are_offered()
    {
        var book = Book(("cure", 1), ("bless", 0));

        var offered = NonCombatCast.Castable(book, CastingEnvironment.Camp,
                                             _ => Spell());

        Assert.Equal(["cure"], offered.Select(e => e.SpellId));
    }

    [Fact]
    public void A_spell_the_design_lost_is_skipped()
    {
        var book = Book(("ghost", 3));

        Assert.Empty(NonCombatCast.Castable(book, CastingEnvironment.Camp, _ => null));
    }

    [Fact]
    public void The_restriction_flags_are_permissions_not_prohibitions()
    {
        // A spell with neither flag set appears nowhere at all.
        var neither = Spell(restrictions: 0);

        Assert.False(NonCombatCast.Allows(neither, CastingEnvironment.Camp));
        Assert.False(NonCombatCast.Allows(neither, CastingEnvironment.Combat));
        Assert.False(NonCombatCast.Allows(neither, CastingEnvironment.Adventure));
    }

    [Fact]
    public void Adventure_is_filtered_by_the_camp_flag()
    {
        // Three environments, two flags: a design cannot let a spell be cast while camping but
        // not while walking around.
        var camp = Spell(restrictions: NonCombatCast.InCamp);

        Assert.True(NonCombatCast.Allows(camp, CastingEnvironment.Camp));
        Assert.True(NonCombatCast.Allows(camp, CastingEnvironment.Adventure));
        Assert.False(NonCombatCast.Allows(camp, CastingEnvironment.Combat));
    }

    [Fact]
    public void A_combat_only_spell_is_not_offered_in_camp()
    {
        var book = Book(("fireball", 1));

        Assert.Empty(NonCombatCast.Castable(book, CastingEnvironment.Camp,
                                            _ => Spell(restrictions: NonCombatCast.InCombat)));
    }

    // ---- the refusals ------------------------------------------------------------------------

    [Fact]
    public void A_monster_cannot_cast()
    {
        var monster = Member("Ogre", type: (byte)CombatantKind.Monster);

        Assert.Equal(CastRefusal.CannotCast,
                     NonCombatCast.Plan(monster, [monster], Spell(), memorized: true).Refusal);
    }

    [Fact]
    public void An_unmemorised_spell_does_nothing_at_all()
    {
        var who = Member();

        Assert.Equal(CastRefusal.NotMemorized,
                     NonCombatCast.Plan(who, [who], Spell(), memorized: false).Refusal);
    }

    [Fact]
    public void A_lost_spell_id_refuses()
    {
        var who = Member();

        Assert.Equal(CastRefusal.UnknownSpell,
                     NonCombatCast.Plan(who, [who], null, memorized: true).Refusal);
    }

    [Fact]
    public void A_combat_only_spell_refuses()
    {
        var who = Member();

        Assert.Equal(CastRefusal.CombatOnly,
                     NonCombatCast.Plan(who, [who], Spell(restrictions: NonCombatCast.InCombat),
                                        memorized: true).Refusal);
    }

    // ---- who it lands on ---------------------------------------------------------------------

    [Fact]
    public void Self_targets_the_caster_alone()
    {
        var who = Member("Caster");
        var other = Member("Other");

        var plan = NonCombatCast.Plan(who, [who, other],
                                      Spell(targeting: SpellTargeting.Self), memorized: true);

        Assert.False(plan.NeedsSelection);
        Assert.Equal([who], plan.Targets);
    }

    [Fact]
    public void Whole_party_takes_everyone()
    {
        var who = Member("Caster");
        var other = Member("Other");

        var plan = NonCombatCast.Plan(who, [who, other],
                                      Spell(targeting: SpellTargeting.WholeParty),
                                      memorized: true);

        Assert.Equal([who, other], plan.Targets);
    }

    [Theory]
    [InlineData(SpellTargeting.AreaCircle)]
    [InlineData(SpellTargeting.AreaSquare)]
    [InlineData(SpellTargeting.AreaCone)]
    [InlineData(SpellTargeting.AreaLinePickStart)]
    [InlineData(SpellTargeting.AreaLinePickEnd)]
    public void Every_area_shape_becomes_the_whole_party_out_of_combat(SpellTargeting targeting)
    {
        // There is no map to centre it on, so a fireball cast in camp hits everyone the party has.
        var who = Member("Caster");
        var other = Member("Other");

        var plan = NonCombatCast.Plan(who, [who, other], Spell(targeting: targeting),
                                      memorized: true);

        Assert.False(plan.NeedsSelection);
        Assert.Equal([who, other], plan.Targets);
    }

    [Theory]
    [InlineData(SpellTargeting.SelectedByCount)]
    [InlineData(SpellTargeting.TouchedTargets)]
    [InlineData(SpellTargeting.SelectByHitDice)]
    public void The_three_picking_modes_ask_the_player(SpellTargeting targeting)
    {
        var who = Member();

        var plan = NonCombatCast.Plan(who, [who], Spell(targeting: targeting), memorized: true);

        Assert.True(plan.NeedsSelection);
        Assert.Empty(plan.Targets);
        Assert.Equal(CastRefusal.None, plan.Refusal);
    }

    [Fact]
    public void An_empty_party_leaves_a_whole_party_spell_with_nobody()
    {
        var who = Member();

        var plan = NonCombatCast.Plan(who, [], Spell(targeting: SpellTargeting.WholeParty),
                                      memorized: true);

        Assert.Equal(CastRefusal.NoTargets, plan.Refusal);
    }

    // ---- the parameter aliasing --------------------------------------------------------------

    [Fact]
    public void A_target_count_comes_from_P1_only_for_the_modes_that_have_one()
    {
        // Parameters are [duration, P1, P2, P3, ...].
        var counted = Spell(targeting: SpellTargeting.SelectedByCount, parameters: ["0", "3", "5", "7"]);

        Assert.Equal(3, SpellParameters.Quantity(counted, Roll));

        var party = Spell(targeting: SpellTargeting.WholeParty, parameters: ["0", "3", "5", "7"]);

        Assert.Equal(SpellParameters.Infinity, SpellParameters.Quantity(party, Roll));
    }

    [Fact]
    public void The_range_is_P3_not_P2()
    {
        // The header still says "//Was TargetRange" on P2, and it is not true any more.
        var ranged = Spell(targeting: SpellTargeting.SelectedByCount,
                           parameters: ["0", "3", "5", "7"]);

        Assert.Equal(7, SpellParameters.Range(ranged, Roll));
    }

    [Fact]
    public void Touch_reaches_one_square_from_a_constant()
    {
        // A designer cannot give touch a reach: the accessor returns "0d0+1" rather than a field.
        var touch = Spell(targeting: SpellTargeting.TouchedTargets,
                          parameters: ["0", "3", "5", "99"]);

        Assert.Equal(1, SpellParameters.Range(touch, Roll));
    }

    [Fact]
    public void Self_and_whole_party_reach_everywhere()
    {
        Assert.Equal(SpellParameters.Infinity,
                     SpellParameters.Range(Spell(targeting: SpellTargeting.Self,
                                                 parameters: ["0", "1", "2", "3"]), Roll));
        Assert.Equal(SpellParameters.Infinity,
                     SpellParameters.Range(Spell(targeting: SpellTargeting.WholeParty,
                                                 parameters: ["0", "1", "2", "3"]), Roll));
    }

    [Fact]
    public void A_circle_takes_its_width_and_height_from_the_same_field()
    {
        // Which is what makes it a circle rather than an ellipse -- and why P1 is free to be its
        // target count.
        var circle = Spell(targeting: SpellTargeting.AreaCircle,
                           parameters: ["0", "3", "5", "7"]);

        Assert.Equal(5, SpellParameters.Width(circle, Roll));
        Assert.Equal(5, SpellParameters.Height(circle, Roll));
        Assert.Equal(3, SpellParameters.Quantity(circle, Roll));
    }

    [Fact]
    public void A_square_takes_its_width_from_P1_where_the_circle_takes_a_count()
    {
        var square = Spell(targeting: SpellTargeting.AreaSquare,
                           parameters: ["0", "3", "5", "7"]);

        Assert.Equal(3, SpellParameters.Width(square, Roll));
        Assert.Equal(5, SpellParameters.Height(square, Roll));
        Assert.Equal(SpellParameters.Infinity, SpellParameters.Quantity(square, Roll));
    }

    [Fact]
    public void An_old_design_missing_the_later_fields_reads_them_as_zero()
    {
        // P3, P4 and P5 only arrived at version 0.999432.
        var old = Spell(targeting: SpellTargeting.SelectedByCount, parameters: ["0", "3", "5"]);

        Assert.Equal(0, SpellParameters.Range(old, Roll));
        Assert.Equal(3, SpellParameters.Quantity(old, Roll));
    }
}
