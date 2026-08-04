using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers a character's casting ability — what <c>CanKnowSpell</c> consults, and therefore what
/// decides which spells a new character is offered.
/// </summary>
public class SpellAbilityTests
{
    private const int Levels = SpellAbility.MaxSpellLevel;

    private const int Primes = 25;

    /// <summary>A by-prime table where score <paramref name="at"/> and up give <paramref name="value"/>.</summary>
    private static byte[] ByPrime(int at, int value)
    {
        var table = new byte[Primes];
        for (int score = at; score <= Primes; score++)
        {
            table[score - 1] = (byte)value;
        }
        return table;
    }

    /// <summary>Spell limits: level <paramref name="baseclassLevel"/> knows <paramref name="counts"/>.</summary>
    private static byte[] Limits(int baseclassLevel, params int[] counts)
    {
        var blob = new byte[40 * Levels];
        for (int i = 0; i < counts.Length && i < Levels; i++)
        {
            blob[((baseclassLevel - 1) * Levels) + i] = (byte)counts[i];
        }
        return blob;
    }

    private static CastingInfo Casting(string school, string prime, byte[] limits,
                                       int maxLevel = 9, int maxSpells = 20) =>
        new(school, prime, limits, ByPrime(1, maxLevel), ByPrime(1, maxSpells));

    private static BaseclassRecord Baseclass(string name, CastingInfo casting,
                                             string bonusAbility = "Wisdom",
                                             byte[]? bonusSpells = null) =>
        new(name, 0, name, [], [], [], 0, [], bonusAbility, bonusSpells ?? [],
            [casting], new SpecabBlock([], [], []), [], [], [], [], [], [], []);

    private static Dictionary<string, SchoolAbility> Fold(
        BaseclassRecord baseclass, int level, Func<string, int>? scores = null)
    {
        var abilities = new Dictionary<string, SchoolAbility>(StringComparer.Ordinal);
        SpellAbility.Fold(abilities, baseclass, level, scores ?? (_ => 12));
        return abilities;
    }

    // ---- the tables ----------------------------------------------------------------------------

    [Fact]
    public void The_school_comes_from_the_baseclasses_casting_info()
    {
        var abilities = Fold(Baseclass("mage", Casting("magic", "Intelligence", Limits(1, 2))), 1);

        Assert.True(abilities.ContainsKey("magic"));
        Assert.Equal(2, abilities["magic"].Base[0]);
    }

    [Fact]
    public void The_spell_limits_are_read_for_the_characters_own_level()
    {
        // The blob is level-major and one-based: row level-1 holds the nine counts.
        var casting = Casting("magic", "Intelligence", Limits(3, 4, 2));

        Assert.Equal(0, Fold(Baseclass("mage", casting), 1)["magic"].Base[0]);
        Assert.Equal(4, Fold(Baseclass("mage", casting), 3)["magic"].Base[0]);
        Assert.Equal(2, Fold(Baseclass("mage", casting), 3)["magic"].Base[1]);
    }

    [Fact]
    public void A_level_outside_the_table_is_clamped_rather_than_read_off_the_end()
    {
        var casting = Casting("magic", "Intelligence", Limits(1, 5));

        Assert.Equal(5, Fold(Baseclass("mage", casting), 0)["magic"].Base[0]);
        Assert.Equal(0, Fold(Baseclass("mage", casting), 999)["magic"].Base[0]);
    }

    [Fact]
    public void The_maximum_spell_level_and_count_come_from_the_prime_score()
    {
        var casting = new CastingInfo("magic", "Intelligence", Limits(1, 1),
                                      ByPrime(at: 15, value: 4), ByPrime(at: 15, value: 9));

        var low = Fold(Baseclass("mage", casting), 1, _ => 10)["magic"];
        var high = Fold(Baseclass("mage", casting), 1, _ => 18)["magic"];

        Assert.Equal(0, low.MaxSpellLevel);
        Assert.Equal(4, high.MaxSpellLevel);
        Assert.Equal(9, high.MaxSpells);
    }

    // ---- what the 2017 change did --------------------------------------------------------------

    [Fact]
    public void With_two_baseclasses_in_one_school_the_last_folded_wins_the_level()
    {
        // The line above it is the commented-out "if (maxSpellLevel > ...)", replaced by a bare
        // assignment -- so this is NOT the better of the two, it is the later of the two.
        var abilities = new Dictionary<string, SchoolAbility>(StringComparer.Ordinal);

        var strong = Baseclass("mage", new CastingInfo("magic", "Intelligence", Limits(1, 1),
                                                       ByPrime(1, 6), ByPrime(1, 9)));
        var weak = Baseclass("bard", new CastingInfo("magic", "Intelligence", Limits(1, 1),
                                                     ByPrime(1, 2), ByPrime(1, 3)));

        SpellAbility.Fold(abilities, strong, 1, _ => 15);
        SpellAbility.Fold(abilities, weak, 1, _ => 15);

        Assert.Equal(2, abilities["magic"].MaxSpellLevel);      // the weaker one, because it is last

        // The spell *count*, by contrast, does take the maximum -- the two are inconsistent.
        Assert.Equal(9, abilities["magic"].MaxSpells);
    }

    [Fact]
    public void The_base_count_updates_on_a_tie_and_hands_over_the_contributing_level()
    {
        // The test is >=, not >, so an equal count still takes the slot -- and the contributing
        // level is what tells a level-up how many new spells to grant.
        var abilities = new Dictionary<string, SchoolAbility>(StringComparer.Ordinal);

        SpellAbility.Fold(abilities, Baseclass("a", Casting("magic", "Int", Limits(1, 2))), 1,
                          _ => 15);
        SpellAbility.Fold(abilities, Baseclass("b", Casting("magic", "Int", Limits(5, 2))), 5,
                          _ => 15);

        Assert.Equal(2, abilities["magic"].Base[0]);
        Assert.Equal(5, abilities["magic"].ContributingLevel[0]);
    }

    // ---- bonus spells --------------------------------------------------------------------------

    [Fact]
    public void Bonus_spells_are_triples_of_threshold_bonus_and_level()
    {
        var baseclass = Baseclass("cleric", Casting("clerical", "Wisdom", Limits(1, 1)),
                                  bonusSpells: [13, 1, 1, 14, 2, 2]);

        var ability = Fold(baseclass, 1, _ => 14)["clerical"];

        Assert.Equal(1, ability.Bonus[0]);
        Assert.Equal(2, ability.Bonus[1]);
    }

    [Fact]
    public void A_score_below_the_threshold_grants_nothing()
    {
        var baseclass = Baseclass("cleric", Casting("clerical", "Wisdom", Limits(1, 1)),
                                  bonusSpells: [15, 3, 1]);

        Assert.Equal(0, Fold(baseclass, 1, _ => 14)["clerical"].Bonus[0]);
    }

    [Fact]
    public void Bonuses_accumulate_rather_than_replace()
    {
        // Every qualifying triple adds; two at the same level stack.
        var baseclass = Baseclass("cleric", Casting("clerical", "Wisdom", Limits(1, 1)),
                                  bonusSpells: [10, 1, 1, 12, 2, 1]);

        Assert.Equal(3, Fold(baseclass, 1, _ => 18)["clerical"].Bonus[0]);
    }

    [Fact]
    public void A_bonus_above_the_schools_maximum_level_is_skipped_not_clamped()
    {
        var casting = Casting("clerical", "Wisdom", Limits(1, 1), maxLevel: 2);
        var baseclass = Baseclass("cleric", casting, bonusSpells: [10, 5, 7]);

        var ability = Fold(baseclass, 1, _ => 18)["clerical"];

        Assert.All(ability.Bonus, b => Assert.Equal(0, b));
    }

    // ---- what CanKnowSpell asks ----------------------------------------------------------------

    [Fact]
    public void A_school_the_character_has_no_ability_in_is_a_refusal()
    {
        var abilities = Fold(Baseclass("mage", Casting("magic", "Int", Limits(1, 1))), 1);

        Assert.False(SpellAbility.CanKnow(abilities, "clerical", 1));
    }

    [Fact]
    public void A_spell_at_or_below_the_maximum_is_knowable()
    {
        var casting = Casting("magic", "Int", Limits(1, 1), maxLevel: 3);
        var abilities = Fold(Baseclass("mage", casting), 1);

        Assert.True(SpellAbility.CanKnow(abilities, "magic", 3));
        Assert.False(SpellAbility.CanKnow(abilities, "magic", 4));
    }
}
