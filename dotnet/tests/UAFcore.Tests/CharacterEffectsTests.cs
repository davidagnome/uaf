using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers spell effects applied to a character outside combat.</summary>
public class CharacterEffectsTests
{
    private static Character Hero(int hitPoints = 10, int maxHitPoints = 12, int armorClass = 5,
                                  int thac0 = 18)
    {
        // Named where it matters: the record has seventeen leading scalars and getting armour
        // class or hit points into the wrong slot produces a character that looks fine and reads
        // zero.
        var record = new CharacterRecord(
            CharacterVersion: 0, PreSpellNamesKey: 0, Type: 0, Race: "human", Gender: 0, ClassId: "fighter",
            Alignment: 0, AllowInCombat: 0, Status: 0, UndeadType: "", CreatureSize: 0,
            Name: "hero", CharacterId: "hero-1",
            Thac0: thac0, Morale: 50, Encumbrance: 0, MaxEncumbrance: 0, ArmorClass: armorClass,
            HitPoints: hitPoints, MaxHitPoints: maxHitPoints, NumberOfHitDice: 1,
            Age: 0, MaxAge: 0, Birthday: 0, MaxCureDisease: 0,
            UnarmedDieSmall: 0, UnarmedNumberDieSmall: 0, UnarmedBonus: 0,
            UnarmedDieLarge: 0, UnarmedNumberDieLarge: 0,
            MaxMovement: 12, ReadyToTrain: 0, CanTradeItems: 0,
            Abilities: new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            OpenDoors: 0, OpenMagicDoors: 0, BendBarsLiftGates: 0,
            HitBonus: 0, DamageBonus: 0, MagicResistance: 0,
            BaseclassStats: [new BaseclassStats("fighter", 0, 0, 0, 0)],
            SkillAdjustments: [], SpellAdjustments: [],
            IsPreGenerated: 0, CanBeSaved: 0, HasLayedOnHandsToday: 0,
            Money: null, NumberOfAttacks: 1,
            Icon: null, IconIndex: 0, OriginalIndex: 0, UniquePartyId: 0,
            DisableTalkIfDead: 0, TalkEvent: 0, TalkLabel: "",
            ExamineEvent: 0, ExamineLabel: "",
            SpellBook: new UAF.Serialization.SpellBook(0, []),
            DetectingInvisible: 0, DetectingTraps: 0,
            SpellEffects: [], Blockages: [], SmallPic: null!,
            Items: new ItemList([], new ReadyItems([])),
            SpecialAbilities: new SpecabBlock([], [], []), Attributes: []);

        return new Character(record, MoneyRules.Default);
    }

    private static ActiveSpellEffect Effect(string attribute, double change,
                                            SpellEffectFlags flags = SpellEffectFlags.Delta) =>
        new(new UAF.Rules.SpellEffect(attribute, change, flags), StopTime: null);

    // ---- armour class --------------------------------------------------------------------------

    [Fact]
    public void With_no_effects_the_adjusted_value_is_the_base_one()
    {
        var hero = Hero(armorClass: 5);

        Assert.Equal(5, hero.AdjustedArmorClass);
        Assert.Equal(hero.ArmorClass, hero.AdjustedArmorClass);
    }

    [Fact]
    public void A_blessing_improves_armour_class_by_lowering_it()
    {
        // Armour class counts down, so a helpful effect is negative.
        var hero = Hero(armorClass: 5);
        hero.Effects.Add(Effect("$CHAR_AC", -2));

        Assert.Equal(3, hero.AdjustedArmorClass);
    }

    [Fact]
    public void The_armour_class_bounds_run_the_opposite_way_from_their_names()
    {
        // MAX_AC is 10 -- the worst -- and MIN_AC is -500. A clamp written as "at least MAX"
        // inverts the rule.
        Assert.True(Character.BestArmorClass < Character.WorstArmorClass);

        var terrible = Hero(armorClass: 5);
        terrible.Effects.Add(Effect("$CHAR_AC", 99));
        Assert.Equal(Character.WorstArmorClass, terrible.AdjustedArmorClass);

        var superb = Hero(armorClass: 5);
        superb.Effects.Add(Effect("$CHAR_AC", -9999));
        Assert.Equal(Character.BestArmorClass, superb.AdjustedArmorClass);
    }

    // ---- hit points ----------------------------------------------------------------------------

    [Fact]
    public void An_effect_can_add_hit_points_up_to_the_characters_own_maximum()
    {
        var hero = Hero(hitPoints: 10, maxHitPoints: 12);
        hero.Effects.Add(Effect("$CHAR_HITPOINTS", 1));
        Assert.Equal(11, hero.AdjustedHitPoints);

        var healed = Hero(hitPoints: 10, maxHitPoints: 12);
        healed.Effects.Add(Effect("$CHAR_HITPOINTS", 50));
        Assert.Equal(12, healed.AdjustedHitPoints);
    }

    [Fact]
    public void An_effect_cannot_drain_a_character_past_dead()
    {
        // Ten below zero is where a character is finally dead rather than dying.
        var hero = Hero(hitPoints: 10);
        hero.Effects.Add(Effect("$CHAR_HITPOINTS", -500));

        Assert.Equal(Character.DeadAt, hero.AdjustedHitPoints);
    }

    [Fact]
    public void A_maximum_below_the_floor_wins_rather_than_raising_to_it()
    {
        // `val = max(-10,val); val = min(val, GetMaxHitPoints())` floors and only then ceilings, so
        // the maximum has the last word. Math.Clamp(value, DeadAt, MaxHitPoints) throws instead,
        // its bounds being crossed -- degenerate data, but it would be a throw where the reference
        // has a value, and this is read on the path that decides whether a character is dead.
        var hero = Hero(hitPoints: 0, maxHitPoints: -20);

        Assert.Equal(-20, hero.AdjustedHitPoints);

        // The floor still runs first; it is simply overridden.
        var drained = Hero(hitPoints: 0, maxHitPoints: -20);
        drained.Effects.Add(Effect("$CHAR_HITPOINTS", -500));

        Assert.Equal(-20, drained.AdjustedHitPoints);
    }

    [Fact]
    public void An_effect_on_another_attribute_leaves_this_one_alone()
    {
        var hero = Hero(hitPoints: 10, armorClass: 5);
        hero.Effects.Add(Effect("$CHAR_THAC0", -4));

        Assert.Equal(10, hero.AdjustedHitPoints);
        Assert.Equal(5, hero.AdjustedArmorClass);
    }

    [Fact]
    public void An_absolute_effect_replaces_rather_than_adjusts()
    {
        var hero = Hero(armorClass: 5);
        hero.Effects.Add(Effect("$CHAR_AC", 2, SpellEffectFlags.Absolute));

        Assert.Equal(2, hero.AdjustedArmorClass);
    }

    // ---- to-hit --------------------------------------------------------------------------------

    [Fact]
    public void The_thac0_bounds_run_the_same_way_round_as_armour_class()
    {
        // Two lines apart in the same header, and the same trap: THAC0 counts down, so MAX is the
        // worst.
        Assert.True(Character.BestThac0 < Character.WorstThac0);
        Assert.Equal(20, Character.WorstThac0);
    }

    [Fact]
    public void Bonuses_are_subtracted_because_a_lower_thac0_is_better()
    {
        var hero = Hero(thac0: 18);

        Assert.Equal(18, hero.AdjustedThac0());
        Assert.Equal(15, hero.AdjustedThac0(hitBonus: 3));
        Assert.Equal(13, hero.AdjustedThac0(hitBonus: 3, weaponAttackBonus: 2));
    }

    [Fact]
    public void Spell_effects_apply_on_top_of_the_bonuses()
    {
        var hero = Hero(thac0: 18);
        hero.Effects.Add(Effect("$CHAR_THAC0", -2));

        Assert.Equal(13, hero.AdjustedThac0(hitBonus: 3));
    }

    [Fact]
    public void The_result_is_clamped_at_both_ends()
    {
        var superb = Hero(thac0: 18);
        Assert.Equal(Character.BestThac0, superb.AdjustedThac0(hitBonus: 9999));

        var hopeless = Hero(thac0: 18);
        hopeless.Effects.Add(Effect("$CHAR_THAC0", 500));
        Assert.Equal(Character.WorstThac0, hopeless.AdjustedThac0());
    }

    [Fact]
    public void The_base_thac0_is_the_records_own()
    {
        Assert.Equal(16, Hero(thac0: 16).Thac0);
    }
}
