using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers a permanent spell effect being written onto an attribute.</summary>
public class PermanentEffectsTests
{
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static Character Member(int hitPoints = 10, int maxHitPoints = 20, int morale = 50)
    {
        var record = new CharacterRecord(
            0, 0, 0, "human", 0, "cleric", 0, 0, 0, "", 0, "Aramil", "",
            0, morale, 0, 0, 0, hitPoints, maxHitPoints, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, [], [], [], 0, 0, 0, null, 0,
            null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, new ItemList([], new ReadyItems([])),
            new SpecabBlock([], [], []), []);

        return new Character(record, MoneyRules.Default);
    }

    private static UAF.Rules.SpellEffect Effect(
        int change, SpellEffectFlags flags = SpellEffectFlags.Target,
        string attribute = PermanentEffects.HitPoints) =>
        new(attribute, change, flags);

    [Fact]
    public void A_permanent_effect_moves_the_real_hit_points()
    {
        // This is the whole point: healing raises the stored value, not an adjustment layered
        // over it -- which is what lets anything reading raw hit points notice.
        var who = Member(hitPoints: 10);

        Assert.True(PermanentEffects.Write(who, PermanentEffects.HitPoints, Effect(5)));

        Assert.Equal(15, who.HitPoints);
        Assert.Empty(who.Effects.Effects);
    }

    [Fact]
    public void A_negative_change_takes_hit_points_away()
    {
        var who = Member(hitPoints: 10);

        PermanentEffects.Write(who, PermanentEffects.HitPoints, Effect(-4));

        Assert.Equal(6, who.HitPoints);
    }

    [Fact]
    public void An_absolute_effect_replaces_rather_than_adds()
    {
        var who = Member(hitPoints: 10);

        PermanentEffects.Write(who, PermanentEffects.HitPoints,
                               Effect(3, SpellEffectFlags.Target | SpellEffectFlags.Absolute));

        Assert.Equal(3, who.HitPoints);
    }

    [Fact]
    public void A_percentage_effect_is_a_percentage_of_what_is_there()
    {
        var who = Member(hitPoints: 10);

        PermanentEffects.Write(who, PermanentEffects.HitPoints,
                               Effect(50, SpellEffectFlags.Target | SpellEffectFlags.Percent));

        Assert.Equal(5, who.HitPoints);
    }

    [Fact]
    public void Hit_points_are_not_clamped_on_the_way_in()
    {
        // The reference writes through SetHitPoints, which is where any bounding would live.
        // AdjustedHitPoints is what caps the number anyone reads.
        var who = Member(hitPoints: 18, maxHitPoints: 20);

        PermanentEffects.Write(who, PermanentEffects.HitPoints, Effect(9));

        Assert.Equal(27, who.HitPoints);
        Assert.Equal(20, who.AdjustedHitPoints);
    }

    [Fact]
    public void The_maximum_and_morale_are_writable_too()
    {
        var who = Member(maxHitPoints: 20, morale: 50);

        PermanentEffects.Write(who, PermanentEffects.MaxHitPoints, Effect(5));
        PermanentEffects.Write(who, PermanentEffects.Morale, Effect(-10));

        Assert.Equal(25, who.MaxHitPoints);
        Assert.Equal(40, who.Morale);
    }

    [Fact]
    public void An_attribute_with_no_field_behind_it_is_not_written()
    {
        // Armour class, THAC0 and magic resistance come off the immutable record, so a permanent
        // effect on one behaves as a virtual trait does and falls through to the effect list.
        var who = Member();

        Assert.False(PermanentEffects.Applies(PermanentEffects.ArmorClass));
        Assert.False(PermanentEffects.Applies(PermanentEffects.Thac0));
        Assert.False(PermanentEffects.Applies(PermanentEffects.MagicResistance));
        Assert.False(PermanentEffects.Write(who, PermanentEffects.ArmorClass, Effect(-2)));
    }

    [Fact]
    public void An_attribute_nobody_recognises_is_not_written()
    {
        var who = Member();

        Assert.False(PermanentEffects.Applies("$CHAR_NONSENSE"));
        Assert.False(PermanentEffects.Write(who, "$CHAR_NONSENSE", Effect(1)));
    }
}
