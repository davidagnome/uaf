using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers spell effects applied to a character outside combat.</summary>
public class CharacterEffectsTests
{
    private static Character Hero(int hitPoints = 10, int maxHitPoints = 12, int armorClass = 5)
    {
        // Named where it matters: the record has seventeen leading scalars and getting armour
        // class or hit points into the wrong slot produces a character that looks fine and reads
        // zero.
        var record = new CharacterRecord(
            CharacterVersion: 0, Type: 0, Race: "human", Gender: 0, ClassId: "fighter",
            Alignment: 0, AllowInCombat: 0, Status: 0, UndeadType: "", CreatureSize: 0,
            Name: "hero", CharacterId: "hero-1",
            Thac0: 20, Morale: 50, Encumbrance: 0, MaxEncumbrance: 0, ArmorClass: armorClass,
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
}
