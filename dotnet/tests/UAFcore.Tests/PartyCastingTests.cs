using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers casting a spell outside combat, where every target is a party member.</summary>
public class PartyCastingTests
{
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static DicePlus Expression(string text) =>
        new("DP2", text, string.Empty, 0, 0, 0, 0, 0, 1, []);

    private static UAF.Serialization.SpellEffect Effect(
        string attribute, string change,
        SpellEffectFlags flags = SpellEffectFlags.Target) =>
        new(attribute, (uint)flags, 0, string.Empty, 0, 0, [], 0, 0, Expression(change));

    private static SpellRecord Spell(
        string name = "cure",
        SpellDurationRate duration = SpellDurationRate.Permanent,
        SaveResult save = SaveResult.NoSave,
        int cumulative = 1,
        params UAF.Serialization.SpellEffect[] effects) =>
        new(0, name, string.Empty, string.Empty, [],
            Level: 1, CastingTime: 0, CastingTimeType: 0,
            CanTargetFriend: 1, CanTargetEnemy: 0, IsCumulative: cumulative, Restrictions: 0,
            CanBeDispelled: 1, CanMemorize: 1, AllowScribe: 0, AutoScribe: 0,
            Lingers: 0, LingerOnceOnly: 0,
            SaveVersus: (int)SaveVersus.Spell, SaveResult: (int)save, Targeting: 1,
            DurationRate: (int)duration, CastCost: 0, CastPriority: 0,
            Parameters: [], Effects: effects.Length > 0
                ? effects
                : [Effect("$CHAR_HITPOINTS", "2")],
            CastArt: null, Art: [], Sounds: [],
            CastMessage: string.Empty, Scripts: [], EffectDuration: Expression("3"),
            SpecialAbilities: null!, Attributes: []);

    private static Character Member(string name, params (string Baseclass, int Level)[] classes)
    {
        var stats = classes.Select(c => new BaseclassStats(c.Baseclass, c.Level, 0, 0, 0)).ToList();

        var record = new CharacterRecord(
            0, 0, 0, "human", 0, "cleric", 0, 0, 0, "", 0, name, name,
            0, 0, 0, 0, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, stats, [], [], 0, 0, 0, null, 0,
            null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, new ItemList([], new ReadyItems([])),
            new SpecabBlock([], [], []), []);

        return new Character(record, MoneyRules.Default);
    }

    private static Character Knowing(Character who, string spellId, int memorized = 1)
    {
        who.Book.Add(spellId, 1, memorized);
        return who;
    }

    /// <summary>A roller with a fixed face, so a cast is deterministic.</summary>
    private static Func<int, int> Face(int face) => _ => face;

    private static CastResult Cast(Character caster, IReadOnlyList<Character> party,
                                   IReadOnlyList<Character> targets, SpellRecord? spell,
                                   Func<int>? keys = null, bool freeOfCharge = false,
                                   int castingLevel = -1) =>
        PartyCasting.Cast(caster, party, targets, spell, Face(3),
                          keys ?? (() => 7), freeOfCharge: freeOfCharge,
                          castingLevel: castingLevel);

    // ---- what gets spent ----------------------------------------------------------------------

    [Fact]
    public void Casting_spends_a_memorised_copy()
    {
        var cleric = Knowing(Member("Cleric"), "cure", memorized: 2);
        var hurt = Member("Hurt");

        var result = Cast(cleric, [cleric, hurt], [hurt], Spell());

        Assert.True(result.Cast);
        Assert.Equal(1, cleric.Book.Find("cure")!.Memorized);
    }

    [Fact]
    public void A_spell_the_design_lost_is_not_cast_and_costs_nothing()
    {
        var cleric = Knowing(Member("Cleric"), "cure", memorized: 2);

        var result = Cast(cleric, [cleric], [cleric], null);

        Assert.False(result.Cast);
        Assert.Empty(result.Hits);
        Assert.Equal(2, cleric.Book.Find("cure")!.Memorized);
    }

    [Fact]
    public void A_free_cast_spends_nothing()
    {
        // LayOrCureOrWhatever -- laying on hands, and the temple's bishop.
        var bishop = Knowing(Member("Bishop"), "cure", memorized: 2);
        var hurt = Member("Hurt");

        Cast(bishop, [hurt], [hurt], Spell(), freeOfCharge: true);

        Assert.Equal(2, bishop.Book.Find("cure")!.Memorized);
    }

    [Fact]
    public void The_copy_is_spent_even_when_the_spell_reaches_nobody()
    {
        var cleric = Knowing(Member("Cleric"), "cure", memorized: 2);
        var stranger = Member("Stranger");

        // The only target is not in the party, so nothing is resolved -- but the copy is gone.
        var result = Cast(cleric, [cleric], [stranger], Spell());

        Assert.True(result.Cast);
        Assert.Empty(result.Hits);
        Assert.Equal(1, cleric.Book.Find("cure")!.Memorized);
    }

    // ---- who it reaches -----------------------------------------------------------------------

    [Fact]
    public void A_target_outside_the_party_is_skipped_not_refused()
    {
        var cleric = Knowing(Member("Cleric"), "cure");
        var inside = Member("Inside");
        var outside = Member("Outside");

        var result = Cast(cleric, [cleric, inside], [outside, inside], Spell());

        Assert.Single(result.Hits);
        Assert.Equal(1, result.Hits[0].Target);       // the party slot, not the target's position
    }

    [Fact]
    public void The_hit_carries_the_party_slot()
    {
        var a = Knowing(Member("A"), "cure");
        var b = Member("B");
        var c = Member("C");

        var result = Cast(a, [a, b, c], [c, a], Spell());

        Assert.Equal([2, 0], result.Hits.Select(h => h.Target));
    }

    [Fact]
    public void A_caster_can_be_their_own_target()
    {
        var cleric = Knowing(Member("Cleric"), "cure");

        var result = Cast(cleric, [cleric], [cleric], Spell());

        Assert.Equal(SpellOutcome.Applied, Assert.Single(result.Hits).Outcome);
        Assert.Equal(12, cleric.HitPoints);
    }

    [Fact]
    public void Every_target_gets_the_effects()
    {
        var cleric = Knowing(Member("Cleric"), "cure");
        var a = Member("A");
        var b = Member("B");

        var result = Cast(cleric, [cleric, a, b], [a, b], Spell());

        Assert.Equal(2, result.Hits.Count);
        Assert.Equal(12, a.HitPoints);
        Assert.Equal(12, b.HitPoints);
    }

    // ---- the active-spell key -----------------------------------------------------------------

    [Fact]
    public void One_key_serves_the_whole_cast()
    {
        // Allocated before the target loop, so a spell that reached three people expires from all
        // three together rather than each on its own clock.
        int issued = 0;

        var cleric = Knowing(Member("Cleric"), "cure");
        var a = Member("A");
        var b = Member("B");

        Cast(cleric, [cleric, a, b], [a, b], Spell(duration: SpellDurationRate.InRounds),
             keys: () => { issued++; return 7; });

        Assert.Equal(1, issued);
        Assert.Equal(7, a.Effects.Effects[0].Parent);
        Assert.Equal(7, b.Effects.Effects[0].Parent);
    }

    [Fact]
    public void A_permanent_spell_takes_no_key_at_all()
    {
        int issued = 0;

        var cleric = Knowing(Member("Cleric"), "cure");
        var hurt = Member("Hurt");

        Cast(cleric, [cleric, hurt], [hurt], Spell(duration: SpellDurationRate.Permanent),
             keys: () => { issued++; return 7; });

        Assert.Equal(0, issued);

        // And nothing is stored either -- a permanent effect is written onto the attribute, so
        // there is no list entry for a key to parent.
        Assert.Empty(hurt.Effects.Effects);
        Assert.Equal(12, hurt.HitPoints);
    }

    [Fact]
    public void A_cast_that_reaches_nobody_still_takes_its_key()
    {
        // The key is allocated before the loop, so it is spent whether or not a target is found.
        int issued = 0;

        var cleric = Knowing(Member("Cleric"), "cure");

        Cast(cleric, [cleric], [Member("Stranger")],
             Spell(duration: SpellDurationRate.InRounds),
             keys: () => { issued++; return 7; });

        Assert.Equal(1, issued);
    }

    // ---- the casting level --------------------------------------------------------------------

    [Fact]
    public void A_level_scaled_effect_uses_the_casters_own_level()
    {
        var cleric = Knowing(Member("Cleric", ("cleric", 5)), "cure");
        var hurt = Member("Hurt");

        Cast(cleric, [cleric, hurt], [hurt], Spell(effects: Effect("$CHAR_HITPOINTS", "level")));

        Assert.Equal(15, hurt.HitPoints);       // 10 + the caster's five levels
    }

    [Fact]
    public void The_highest_baseclass_is_the_casting_level_not_the_total()
    {
        var dual = Member("Dual", ("cleric", 3), ("magicuser", 7));

        Assert.Equal(7, PartyCasting.CasterLevel(dual));
    }

    [Fact]
    public void A_character_with_no_baseclasses_casts_at_one()
    {
        Assert.Equal(1, PartyCasting.CasterLevel(Member("Nobody")));
    }

    [Fact]
    public void An_explicit_casting_level_overrides_the_casters_own()
    {
        // m_spellCastingLevel -- what lets an item or a temple cast above its holder's level.
        var cleric = Knowing(Member("Cleric", ("cleric", 2)), "cure");
        var hurt = Member("Hurt");

        Cast(cleric, [cleric, hurt], [hurt],
             Spell(effects: Effect("$CHAR_HITPOINTS", "level")), castingLevel: 20);

        Assert.Equal(30, hurt.HitPoints);
    }

    [Fact]
    public void Minus_one_means_no_override()
    {
        var cleric = Knowing(Member("Cleric", ("cleric", 4)), "cure");
        var hurt = Member("Hurt");

        Cast(cleric, [cleric, hurt], [hurt],
             Spell(effects: Effect("$CHAR_HITPOINTS", "level")), castingLevel: -1);

        Assert.Equal(14, hurt.HitPoints);
    }

    // ---- what the shared resolution still does -------------------------------------------------

    [Fact]
    public void A_non_cumulative_spell_does_not_land_twice()
    {
        // The check that already lived in SpellResolution, reached from the non-combat path.
        var cleric = Knowing(Member("Cleric"), "cure", memorized: 2);
        var hurt = Member("Hurt");
        var spell = Spell(duration: SpellDurationRate.InRounds, cumulative: 0);

        Cast(cleric, [cleric, hurt], [hurt], spell);
        var second = Cast(cleric, [cleric, hurt], [hurt], spell);

        Assert.Equal(SpellOutcome.AlreadyAffected, Assert.Single(second.Hits).Outcome);
        Assert.Single(hurt.Effects.Effects);
        Assert.Equal(0, cleric.Book.Find("cure")!.Memorized);   // and both copies are gone
    }

    [Fact]
    public void A_permanent_spell_can_be_cast_again_however_non_cumulative_it_is()
    {
        // The non-cumulative check looks for an existing effect FROM THIS SPELL, and a permanent
        // spell leaves none -- so the flag simply does not bite on one. The reference has the same
        // hole, and it is what lets a cure be cast over and over.
        var cleric = Knowing(Member("Cleric"), "cure", memorized: 2);
        var hurt = Member("Hurt");
        var spell = Spell(duration: SpellDurationRate.Permanent, cumulative: 0);

        Cast(cleric, [cleric, hurt], [hurt], spell);
        var second = Cast(cleric, [cleric, hurt], [hurt], spell);

        Assert.Equal(SpellOutcome.Applied, Assert.Single(second.Hits).Outcome);
        Assert.Equal(14, hurt.HitPoints);
    }
}
