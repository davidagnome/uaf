using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>
/// Covers spell durations and the stacking rules
/// (<c>Char.cpp:16397</c>, <c>Spell.cpp:967</c>, <c>Char.cpp:11989</c>).
/// </summary>
public class SpellDurationTests
{
    // ---- converting a duration into a stop time --------------------------------------------

    [Fact]
    public void One_round_is_one_minute()
    {
        // The bridge between the round clock and the duration layer: StartNewRound advances the
        // clock by one minute, so a three-round spell survives three rounds.
        Assert.Equal(103, SpellDuration.StopTimeFor(SpellDurationRate.InRounds, 3, 100));
    }

    [Theory]
    [InlineData(SpellDurationRate.InHours, 2, 220)]      // 2 * 60 + 100
    [InlineData(SpellDurationRate.InDays, 1, 1540)]      // 24 * 60 + 100
    public void Hours_and_days_convert_to_minutes(SpellDurationRate rate, double duration,
                                                  double expected)
    {
        Assert.Equal(expected, SpellDuration.StopTimeFor(rate, duration, 100));
    }

    [Theory]
    [InlineData(SpellDurationRate.InRounds)]
    [InlineData(SpellDurationRate.InHours)]
    [InlineData(SpellDurationRate.InDays)]
    public void Every_timed_rate_lasts_at_least_a_minute(SpellDurationRate rate)
    {
        // The floor is applied after the unit conversion, so half an hour and zero rounds both
        // come out as one minute.
        Assert.Equal(101, SpellDuration.StopTimeFor(rate, 0, 100));
        Assert.Equal(101, SpellDuration.StopTimeFor(rate, -5, 100));
    }

    [Theory]
    [InlineData(SpellDurationRate.ByDamageTaken)]
    [InlineData(SpellDurationRate.ByNumberOfAttacks)]
    public void The_two_counted_rates_store_a_raw_count_not_a_time(SpellDurationRate rate)
    {
        // They are declared, authored, and then unreachable: IsReadyToExpire hits its error path
        // for both. The count is stored where a time belongs.
        Assert.Equal(7, SpellDuration.StopTimeFor(rate, 7, 100));
    }

    [Fact]
    public void Permanent_has_no_case_in_the_reference_at_all()
    {
        // The switch falls to `default: die()` and leaves the stop time at zero -- which then
        // means "expire immediately". A permanent effect in the reference lasts no time at all.
        Assert.Null(SpellDuration.StopTimeFor(SpellDurationRate.Permanent, 99, 100));
        Assert.True(SpellDuration.PermanentExpiresImmediately);
        Assert.True(SpellDuration.IsReadyToExpire(
            SpellDuration.StopTimeFor(SpellDurationRate.Permanent, 99, 100), 100));
    }

    // ---- expiry ----------------------------------------------------------------------------

    [Fact]
    public void A_stop_time_of_zero_expires_immediately()
    {
        // The first test in the function, ahead of everything else: "no duration" and "already
        // over" are the same state.
        Assert.True(SpellDuration.IsReadyToExpire(0, 0));
        Assert.True(SpellDuration.IsReadyToExpire(0, 500));
    }

    [Fact]
    public void A_spell_effect_survives_the_minute_it_stops_on()
    {
        Assert.False(SpellDuration.IsReadyToExpire(103, 102));
        Assert.False(SpellDuration.IsReadyToExpire(103, 103));   // strictly greater
        Assert.True(SpellDuration.IsReadyToExpire(103, 104));
    }

    [Fact]
    public void A_script_effect_expires_one_minute_sooner_than_a_spell_one()
    {
        // The two paths disagree by one: script uses >=, spell uses >. Transcribed, because both
        // are reachable and neither is obviously the intended one.
        Assert.True(SpellDuration.IsReadyToExpire(103, 103, fromScript: true));
        Assert.False(SpellDuration.IsReadyToExpire(103, 103, fromScript: false));
    }

    // ---- stacking --------------------------------------------------------------------------

    private static ActiveSpellEffect Effect(string attribute, double change,
                                            SpellEffectFlags flags = SpellEffectFlags.Delta,
                                            double? stopTime = 100) =>
        new(new SpellEffect(attribute, change, flags), stopTime);

    [Fact]
    public void A_negated_effect_never_lands()
    {
        // The None flag means a saving throw negated it. The check exists because effects were
        // landing despite a successful save.
        var list = new SpellEffectList();

        Assert.False(list.Add(Effect("$CHAR_AC", -2, SpellEffectFlags.None)));
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void A_non_cumulative_effect_refuses_to_stack_and_the_incumbent_wins()
    {
        // Two castings of the same buff do not add up -- and it is the SECOND that is dropped,
        // not the first that is replaced.
        var list = new SpellEffectList();

        Assert.True(list.Add(Effect("$CHAR_AC", -2)));
        Assert.False(list.Add(Effect("$CHAR_AC", -5)));

        Assert.Equal(1, list.Count);
        Assert.Equal(-2, list.Effects[0].Effect.Change);
    }

    [Fact]
    public void A_cumulative_effect_stacks()
    {
        var list = new SpellEffectList();

        Assert.True(list.Add(Effect("$CHAR_AC", -2, SpellEffectFlags.Cumulative)));
        Assert.True(list.Add(Effect("$CHAR_AC", -3, SpellEffectFlags.Cumulative)));

        Assert.Equal(2, list.Count);
        Assert.Equal(5, list.Apply(10, "$CHAR_AC"));
    }

    [Fact]
    public void A_different_attribute_never_collides()
    {
        var list = new SpellEffectList();

        Assert.True(list.Add(Effect("$CHAR_AC", -2)));
        Assert.True(list.Add(Effect("$CHAR_THAC0", -1)));
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Remove_all_clears_the_attribute_and_leaves_nothing_behind()
    {
        // The reference's branch ends in `return TRUE` without reaching the add: the flag is an
        // instruction to strip the attribute, not an effect to carry. It reports success even
        // though the list is now shorter than before.
        var list = new SpellEffectList();
        list.Add(Effect("$CHAR_AC", -2, SpellEffectFlags.Cumulative));
        list.Add(Effect("$CHAR_AC", -3, SpellEffectFlags.Cumulative));

        Assert.True(list.Add(Effect("$CHAR_AC", -1,
                                    SpellEffectFlags.Cumulative | SpellEffectFlags.RemoveAll)));

        Assert.Empty(list.Effects);
    }

    [Fact]
    public void Remove_all_leaves_other_attributes_alone()
    {
        var list = new SpellEffectList();
        list.Add(Effect("$CHAR_AC", -2, SpellEffectFlags.Cumulative));
        list.Add(Effect("$CHAR_THAC0", -3, SpellEffectFlags.Cumulative));

        list.Add(Effect("$CHAR_AC", -1,
                        SpellEffectFlags.Cumulative | SpellEffectFlags.RemoveAll));

        Assert.Equal("$CHAR_THAC0", Assert.Single(list.Effects).Attribute);
    }

    [Fact]
    public void Remove_all_cannot_touch_an_intrinsic_character_ability()
    {
        // They are part of what the character IS, not something done to it.
        var list = new SpellEffectList();
        list.Add(Effect("$CHAR_AC", -4, SpellEffectFlags.CharacterSpecialAbility));

        list.Add(Effect("$CHAR_AC", -1,
                        SpellEffectFlags.Cumulative | SpellEffectFlags.RemoveAll));

        Assert.Contains(list.Effects, e => e.IsIntrinsic);
        Assert.Single(list.Effects);
    }

    [Fact]
    public void Remove_all_and_non_cumulative_together_still_refuse_to_stack()
    {
        // Rule 2 runs before rule 3, so a non-cumulative remove-all never gets to clear anything.
        var list = new SpellEffectList();
        list.Add(Effect("$CHAR_AC", -2));

        Assert.False(list.Add(Effect("$CHAR_AC", -1, SpellEffectFlags.RemoveAll)));
        Assert.Equal(-2, Assert.Single(list.Effects).Effect.Change);
    }

    // ---- expiry against the clock ----------------------------------------------------------

    [Fact]
    public void Expired_effects_are_dropped_and_returned()
    {
        var list = new SpellEffectList();
        list.Add(Effect("$CHAR_AC", -2, SpellEffectFlags.Cumulative, stopTime: 102));
        list.Add(Effect("$CHAR_THAC0", -1, SpellEffectFlags.Cumulative, stopTime: 110));

        Assert.Empty(list.Expire(101));
        Assert.Equal(2, list.Count);

        var gone = list.Expire(103);
        Assert.Equal("$CHAR_AC", Assert.Single(gone).Attribute);
        Assert.Equal(1, list.Count);
    }

    [Fact]
    public void A_three_round_spell_survives_exactly_three_rounds()
    {
        // The whole point of the unit bridge, end to end: cast at minute 100 for three rounds,
        // then tick a minute per round.
        var list = new SpellEffectList();
        double elapsed = 100;
        double? stop = SpellDuration.StopTimeFor(SpellDurationRate.InRounds, 3, elapsed);
        list.Add(new ActiveSpellEffect(new SpellEffect("$CHAR_AC", -2), stop));

        for (int round = 1; round <= 3; round++)
        {
            elapsed += 1;
            Assert.Empty(list.Expire(elapsed));
            Assert.Equal(1, list.Count);
        }

        elapsed += 1;
        Assert.Single(list.Expire(elapsed));
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void Clearing_removes_everything_as_the_end_of_a_fight_does()
    {
        var list = new SpellEffectList();
        list.Add(Effect("$CHAR_AC", -2, SpellEffectFlags.Cumulative));
        list.Add(Effect("$CHAR_THAC0", -1, SpellEffectFlags.Cumulative));

        list.Clear();
        Assert.Equal(0, list.Count);
    }
}
