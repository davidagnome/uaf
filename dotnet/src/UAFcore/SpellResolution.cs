using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>Why a spell did nothing to a target, or that it landed.</summary>
public enum SpellOutcome
{
    /// <summary>The effects were applied.</summary>
    Applied,

    /// <summary>The target already carries this spell and it does not stack.</summary>
    AlreadyAffected,

    /// <summary>A script said the attack does not succeed.</summary>
    Refused,

    /// <summary>The target saved and the spell negates on a save.</summary>
    Saved,

    /// <summary>It landed, but nothing in it changed anything.</summary>
    NoEffect,
}

/// <summary>What one target got.</summary>
/// <param name="Target">The combatant's index.</param>
/// <param name="Saved">Whether a saving throw was made. False when none was rolled.</param>
/// <param name="Effects">How many effects were actually added.</param>
public readonly record struct SpellHit(int Target, SpellOutcome Outcome, bool Saved, int Effects);

/// <summary>
/// Applying a spell to one target (<c>CHARACTER::InvokeSpellOnTarget</c>, <c>Char.cpp:15987</c>).
/// </summary>
/// <remarks>
/// <para>
/// The last piece of casting, and the one everything else was for: the clock decides <i>when</i>
/// (<see cref="PendingSpellList"/>), targeting decides <i>who</i> (<see cref="SpellTargets"/> and
/// <see cref="SpellArea"/>), and this decides <i>what happens to them</i>.
/// </para>
/// <para>
/// <b>Five script hooks sit in this function and none of them are ported.</b> In order:
/// <c>DOES_SPELL_ATTACK_SUCCEED</c> (tried against the spell, then the target's race, then its
/// monster record, then its character record — first non-empty answer wins, and <c>'N'</c> means
/// no), the spell's begin script, each effect's activation script, its modification script, and
/// <c>INVOKE_SPELL_ON_TARGET</c>. All are optional in the reference and all default to "carry on",
/// so a spell with no scripts — which is most of them — resolves identically here. The two that
/// can refuse are exposed as predicates so the caller can supply them once GPDL is wired in.
/// </para>
/// </remarks>
public static class SpellResolution
{
    /// <summary>
    /// Resolves a spell against one target.
    /// </summary>
    /// <param name="elapsedMinutes">The clock, for working out when the effects stop.</param>
    /// <param name="activeSpellKey">
    /// The entry every effect of this cast is parented to. <b>One per cast, not per target</b> —
    /// the reference allocates it before the target loop so a spell that hit four combatants
    /// expires from all four together.
    /// </param>
    /// <param name="saveScore">The number the target must roll on its save.</param>
    /// <param name="attackSucceeds">
    /// Stands in for the <c>DOES_SPELL_ATTACK_SUCCEED</c> chain. Defaults to succeeding.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The order of the first two tests matters.</b> The non-cumulative check comes before
    /// everything — before the scripts, before the save — so a second casting of a spell the target
    /// already carries is not merely wasted, it never even rolls. And it is the <i>spell</i> that
    /// is checked, by source, not the individual attributes: <c>SpellEffectList.Add</c> applies its
    /// own per-attribute cumulative rule afterwards, and the two are independent.
    /// </para>
    /// <para>
    /// <b>A spell whose <c>Save_Result</c> is <c>NoSave</c> never rolls a save at all.</b> The
    /// reference guards the call, so no d20 is spent and no save-succeeded script runs — which is
    /// not the same as rolling and ignoring the answer. Two thirds of the spells in every shipped
    /// design are <c>NoSave</c>.
    /// </para>
    /// <para>
    /// <b>Only effects flagged <c>EFFECT_TARGET</c> are applied to the target.</b> The others
    /// describe the caster or the map and are skipped here, silently, as the reference skips them.
    /// </para>
    /// </remarks>
    public static SpellHit Invoke(Combatant caster, Combatant target, SpellRecord spell,
                                  Func<int, int> dice, double elapsedMinutes = 0,
                                  int activeSpellKey = -1, int saveScore = 20,
                                  int casterLevel = 1,
                                  Func<Combatant, Combatant, bool>? attackSucceeds = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(dice);

        // 1. Already carrying this spell, and it does not stack.
        if (spell.IsCumulative == 0
            && target.Effects.Effects.Any(e => e.SourceSpell == spell.Name))
        {
            return new SpellHit(target.Index, SpellOutcome.AlreadyAffected, false, 0);
        }

        // 2. The DOES_SPELL_ATTACK_SUCCEED chain.
        if (attackSucceeds is not null && !attackSucceeds(caster, target))
        {
            return new SpellHit(target.Index, SpellOutcome.Refused, false, 0);
        }

        // 3. The saving throw -- rolled only when the spell declares one.
        bool saved = false;
        var save = new SaveOutcome(false, false, 1.0);

        if ((SaveResult)spell.SaveResult != SaveResult.NoSave)
        {
            save = SavingThrow.Resolve((SaveResult)spell.SaveResult, saveScore, dice(20),
                                       magicResistance: target.MagicResistance,
                                       resistanceRoll: dice(100),
                                       casterThac0: caster.Thac0,
                                       targetArmorClass: target.ArmorClass);
            saved = save.Saved;

            if (save.NoEffect)
            {
                return new SpellHit(target.Index, SpellOutcome.Saved, true, 0);
            }
        }

        // 4. Roll and add each effect aimed at the target.
        int applied = 0;
        double? stopTime = StopTimeFor(spell, elapsedMinutes, dice, casterLevel);

        foreach (var effect in spell.Effects)
        {
            var flags = (SpellEffectFlags)effect.Flags;
            if ((flags & SpellEffectFlags.Target) == 0)
            {
                continue;
            }

            if (DiceExpression.Evaluate(effect.ChangeData?.Text ?? string.Empty, dice,
                                        name => Level(name, casterLevel)) is not { } change)
            {
                // An expression the engine cannot compile contributes nothing -- see
                // DiceExpression. The effect is still skipped rather than added with a zero.
                continue;
            }

            var runtime = new UAF.Rules.SpellEffect(effect.IndexKey, change, flags);
            if (target.Effects.Add(new ActiveSpellEffect(runtime, stopTime,
                                                         SourceSpell: spell.Name,
                                                         Parent: activeSpellKey)))
            {
                applied++;
            }
        }

        return new SpellHit(target.Index,
                            applied > 0 ? SpellOutcome.Applied : SpellOutcome.NoEffect,
                            saved, applied);
    }

    /// <summary>
    /// When this spell's effects stop, or null for one that never expires.
    /// </summary>
    /// <remarks>
    /// The duration is itself a dice expression (<c>EffectDuration</c>), so it is rolled once per
    /// cast rather than read as a constant. A duration that will not evaluate is treated as
    /// permanent, which is what the reference's zero-length compile leaves behind.
    /// </remarks>
    private static double? StopTimeFor(SpellRecord spell, double elapsedMinutes,
                                       Func<int, int> dice, int casterLevel)
    {
        var rate = (SpellDurationRate)spell.DurationRate;
        if (rate == SpellDurationRate.Permanent)
        {
            return null;
        }

        int? duration = DiceExpression.Evaluate(spell.EffectDuration?.Text ?? string.Empty, dice,
                                                name => Level(name, casterLevel));
        return duration is null
            ? null
            : SpellDuration.StopTimeFor(rate, duration.Value, elapsedMinutes);
    }

    /// <summary>
    /// Resolves the one identifier a dice expression uses in practice.
    /// </summary>
    /// <remarks>
    /// The reference routes every name through <c>GENERIC_REFERENCE::LookupReferenceData</c>, which
    /// reaches the level, race, class and gender tables. Only <c>level</c> appears in any shipped
    /// spell expression; anything else falls through to zero, as it does there.
    /// </remarks>
    private static int? Level(string name, int casterLevel) =>
        name.Equals("level", StringComparison.OrdinalIgnoreCase) ? casterLevel : null;

    /// <summary>
    /// Resolves a spell against every target of a cast.
    /// </summary>
    /// <returns>One <see cref="SpellHit"/> per target, in the order the targets were given.</returns>
    /// <remarks>
    /// <b>One active-spell entry for the whole cast.</b> The reference allocates the key before the
    /// loop and passes the same one to every target, so all of them expire together — the alternative
    /// would let half a fireball wear off before the other half.
    /// </remarks>
    public static List<SpellHit> InvokeAll(Combatant caster, IEnumerable<Combatant> targets,
                                           SpellRecord spell, Func<int, int> dice,
                                           double elapsedMinutes = 0, int activeSpellKey = -1,
                                           Func<Combatant, int> saveScoreOf = null!,
                                           int casterLevel = 1)
    {
        ArgumentNullException.ThrowIfNull(targets);

        return [.. targets.Select(t => Invoke(caster, t, spell, dice, elapsedMinutes,
                                              activeSpellKey,
                                              saveScoreOf?.Invoke(t) ?? 20, casterLevel))];
    }
}
