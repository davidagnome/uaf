using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers <see cref="SpellEffects"/>.</summary>
public class SpellEffectsTests
{
    private const string Ac = "$CHAR_AC";
    private const string Thac0 = "$CHAR_THAC0";

    [Fact]
    public void No_flag_means_the_change_is_a_delta()
    {
        Assert.Equal(7, SpellEffects.Apply(10, new SpellEffect(Ac, -3)));
        Assert.Equal(13, SpellEffects.Apply(10, new SpellEffect(Ac, 3)));
    }

    [Fact]
    public void A_percentage_effect_replaces_the_value_rather_than_scaling_it_up()
    {
        // 50% of 10 is 5 -- not 15. The result is a percentage OF the original, which reads like
        // a bonus and is a replacement.
        Assert.Equal(5, SpellEffects.Apply(10, new SpellEffect(Ac, 50, SpellEffectFlags.Percent)));
        Assert.Equal(20, SpellEffects.Apply(10, new SpellEffect(Ac, 200, SpellEffectFlags.Percent)));
    }

    [Fact]
    public void An_absolute_effect_discards_the_value_entirely()
    {
        Assert.Equal(2, SpellEffects.Apply(10, new SpellEffect(Ac, 2, SpellEffectFlags.Absolute)));
        Assert.Equal(2, SpellEffects.Apply(-99, new SpellEffect(Ac, 2, SpellEffectFlags.Absolute)));
    }

    [Fact]
    public void A_negated_effect_leaves_the_value_untouched()
    {
        // EFFECT_NONE is what a successful saving throw sets. It is checked before the others, so
        // a negated percentage is a no-op rather than a multiply by zero.
        var negated = new SpellEffect(Ac, 50, SpellEffectFlags.None | SpellEffectFlags.Percent);
        Assert.Equal(10, SpellEffects.Apply(10, negated));
    }

    [Fact]
    public void Effects_apply_in_order_and_a_replacement_wipes_out_what_came_before()
    {
        // This is the property that makes ordering part of the answer: the -5 is simply lost.
        var effects = new[]
        {
            new SpellEffect(Ac, -5),
            new SpellEffect(Ac, 3, SpellEffectFlags.Absolute),
        };

        Assert.Equal(3, SpellEffects.ApplyAll(10, Ac, effects));

        // Reversed, both count.
        Assert.Equal(-2, SpellEffects.ApplyAll(10, Ac, effects.Reverse()));
    }

    [Fact]
    public void Only_effects_on_the_named_attribute_apply()
    {
        var effects = new[]
        {
            new SpellEffect(Ac, -4),
            new SpellEffect(Thac0, -2),
        };

        Assert.Equal(6, SpellEffects.ApplyAll(10, Ac, effects));
        Assert.Equal(18, SpellEffects.ApplyAll(20, Thac0, effects));
        Assert.Equal(5, SpellEffects.ApplyAll(5, "$CHAR_HITPOINTS", effects));
    }

    [Fact]
    public void Attribute_names_are_matched_exactly()
    {
        var effects = new[] { new SpellEffect("$CHAR_AC", -4) };
        Assert.Equal(10, SpellEffects.ApplyAll(10, "$char_ac", effects));
    }

    [Fact]
    public void An_empty_list_leaves_the_value_alone()
    {
        Assert.Equal(10, SpellEffects.ApplyAll(10, Ac, []));
    }

    [Fact]
    public void The_accessors_clamp_after_applying()
    {
        // GetAdjAC holds the result inside MIN_AC and MAX_AC, so an absolute effect cannot push an
        // attribute outside its own legal range.
        var huge = new[] { new SpellEffect(Ac, 9999, SpellEffectFlags.Absolute) };
        var tiny = new[] { new SpellEffect(Ac, -9999, SpellEffectFlags.Absolute) };

        Assert.Equal(ArmorClass.Worst,
                     SpellEffects.ApplyAll(10, Ac, huge, ArmorClass.Best, ArmorClass.Worst));
        Assert.Equal(ArmorClass.Best,
                     SpellEffects.ApplyAll(10, Ac, tiny, ArmorClass.Best, ArmorClass.Worst));
    }

    [Fact]
    public void A_percentage_of_a_negative_value_keeps_its_sign()
    {
        // Armour classes go negative, and half of -8 is -4 rather than 4.
        Assert.Equal(-4, SpellEffects.Apply(-8, new SpellEffect(Ac, 50, SpellEffectFlags.Percent)));
    }
}
