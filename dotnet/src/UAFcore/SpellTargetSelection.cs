namespace UAFcore;

/// <summary>
/// The targets chosen for one cast, and the rules for what may still be added
/// (<c>SPELL_TARGETING_DATA</c>, <c>Spell.h:340</c>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SpellTargets.Setup"/> works out the limits; this holds the choices made against them.
/// The two are separate in the reference too — the setup happens once when the spell is picked, and
/// the selection runs while the player drives the cursor.
/// </para>
/// <para>
/// <b>Combatants and map squares are alternatives, not both.</b> A cast either selects units or
/// selects one square, decided by <see cref="SpellTargetingSetup.SelectingUnits"/>, and each
/// refuses the other outright.
/// </para>
/// </remarks>
public sealed class SpellTargetSelection(SpellTargeting targeting, SpellTargetingSetup setup,
                                         int combatantCount)
{
    private readonly List<int> targets = [];

    /// <summary>How the spell picks.</summary>
    public SpellTargeting Targeting { get; } = targeting;

    /// <summary>The limits, from <see cref="SpellTargets.Setup"/>.</summary>
    public SpellTargetingSetup Setup { get; } = setup;

    /// <summary>The combatants chosen, in the order they were picked.</summary>
    public IReadOnlyList<int> Targets => targets;

    /// <summary>Hit dice spent so far, for <see cref="SpellTargeting.SelectByHitDice"/>.</summary>
    public double HitDiceUsed { get; private set; }

    /// <summary>The square chosen, or null.</summary>
    public (int X, int Y)? MapTarget { get; private set; }

    /// <summary>
    /// Whether the spell's limits make sense at all (<c>ValidNumTargets</c>, <c>Spell.cpp:7395</c>).
    /// </summary>
    /// <remarks>
    /// The reference treats a false here as a programming error — it <c>die()</c>s and abandons the
    /// cast (<c>RunEvent.cpp:20184</c>). Note the area modes need <b>both</b> a range and a target
    /// count; a design that leaves an area spell's quantity at zero cannot cast it, and
    /// <see cref="SpellTargets.Setup"/> does not fill that in.
    /// </remarks>
    public bool IsValid => Targeting switch
    {
        SpellTargeting.Self or SpellTargeting.SelectedByCount or SpellTargeting.WholeParty
            or SpellTargeting.TouchedTargets => Setup.MaxTargets > 0,
        SpellTargeting.SelectByHitDice => Setup.MaxHitDice > 0,
        _ => Setup.MaxRange > 0 && Setup.MaxTargets > 0,
    };

    /// <summary>
    /// Whether one more combatant may be taken
    /// (<c>STD_CanAddTarget</c>, <c>Spell.cpp:7279</c>).
    /// </summary>
    /// <param name="hitDice">The candidate's hit dice, for the hit-dice budget.</param>
    /// <param name="distance">How far it is from the caster.</param>
    /// <remarks>
    /// <b>Each limit is only enforced when it is set.</b> All three tests are guarded by
    /// <c>&gt; 0</c>, so a zero maximum means <i>no limit</i> rather than none allowed — which is
    /// why <see cref="SpellTargeting.SelectByHitDice"/> can zero <c>MaxTargets</c> and still work.
    /// <para>
    /// <b>A target that exactly reaches the hit-dice budget is allowed</b>; the test refuses only
    /// what would exceed it. <see cref="AllChosen"/> then reports the budget as reached, so the
    /// last pick both lands and ends the selection.
    /// </para>
    /// </remarks>
    public bool CanAdd(double hitDice = 0, int distance = 0)
    {
        if (!Setup.SelectingUnits)
        {
            // A map-targeting cast accepts exactly one square, and only while it has none.
            return MapTarget is null;
        }

        if (Setup.MaxTargets > 0 && targets.Count >= Setup.MaxTargets)
        {
            return false;
        }

        if (Setup.MaxHitDice > 0 && hitDice + HitDiceUsed > Setup.MaxHitDice)
        {
            return false;
        }

        return Setup.MaxRange <= 0 || distance <= Setup.MaxRange;
    }

    /// <summary>
    /// Takes a combatant as a target (<c>STD_AddTarget</c>, <c>Spell.cpp:7503</c>).
    /// </summary>
    /// <returns>Whether it was taken.</returns>
    /// <remarks>
    /// <b>The hit-dice total only accumulates for the hit-dice mode.</b> Every other mode leaves it
    /// at zero however many targets are picked, so the budget cannot leak into a spell that does
    /// not use it.
    /// </remarks>
    public bool Add(int target, double hitDice = 0, int distance = 0)
    {
        if (!Setup.SelectingUnits || !CanAdd(hitDice, distance) || targets.Contains(target))
        {
            return false;
        }

        targets.Add(target);

        if (Targeting == SpellTargeting.SelectByHitDice)
        {
            HitDiceUsed += hitDice;
        }

        return true;
    }

    /// <summary>
    /// Takes a square as the target of an area cast (<c>AddMapTarget</c>, <c>Spell.cpp:7521</c>).
    /// </summary>
    public bool AddMapTarget(int x, int y)
    {
        if (Setup.SelectingUnits || MapTarget is not null || x < 0 || y < 0)
        {
            return false;
        }

        MapTarget = (x, y);
        return true;
    }

    /// <summary>
    /// Whether the selection is finished (<c>AllTargetsChosen</c>, <c>Spell.cpp:7438</c>).
    /// </summary>
    /// <remarks>
    /// <b>Running out of combatants ends the selection as surely as filling the quota.</b> The
    /// count modes test <c>NumTargets() &gt;= GetNumCombatants()</c> as well as against the
    /// maximum, so a spell allowed six targets in a fight with three stops after three rather than
    /// leaving the player pressing EXIT.
    /// </remarks>
    public bool AllChosen => Targeting switch
    {
        SpellTargeting.Self or SpellTargeting.SelectedByCount or SpellTargeting.WholeParty
            or SpellTargeting.TouchedTargets =>
            targets.Count >= Setup.MaxTargets || targets.Count >= combatantCount,

        SpellTargeting.SelectByHitDice =>
            HitDiceLimitReached || targets.Count >= combatantCount,

        _ => MapTarget is not null,
    };

    /// <summary>
    /// Whether the hit-dice budget is spent (<c>HDLimitReached</c>, <c>Spell.cpp:7313</c>).
    /// </summary>
    /// <remarks>
    /// Always false for any other targeting mode, whatever the totals say — the reference checks
    /// the mode first.
    /// </remarks>
    public bool HitDiceLimitReached =>
        Targeting == SpellTargeting.SelectByHitDice
        && Setup.MaxHitDice > 0 && HitDiceUsed >= Setup.MaxHitDice;

    /// <summary>
    /// The menu title, which is how the player is told what is still wanted
    /// (<c>FormatRemainingTargsText</c>, <c>Spell.cpp:7330</c>).
    /// </summary>
    /// <remarks>
    /// <b>The remaining count is clamped to the number of combatants</b>, so a spell allowed six
    /// targets in a fight with three asks for three. The hit-dice form is the only one that shows a
    /// fraction, and it shows what is <i>left</i> rather than what has been spent.
    /// </remarks>
    public string RemainingText()
    {
        switch (Targeting)
        {
            case SpellTargeting.Self:
            case SpellTargeting.SelectedByCount:
            case SpellTargeting.WholeParty:
            case SpellTargeting.TouchedTargets:
            {
                int remaining = Math.Min(Setup.MaxTargets - targets.Count, combatantCount);
                return $"CHOOSE {remaining} TARGETS";
            }

            case SpellTargeting.SelectByHitDice:
                return $"CHOOSE {Setup.MaxHitDice - HitDiceUsed:0.0} HIT DICE";

            case SpellTargeting.AreaCircle:
                return "CHOOSE CENTER OF CIRCLE";

            case SpellTargeting.AreaLinePickStart:
                return "CHOOSE START OF LINE";

            case SpellTargeting.AreaLinePickEnd:
                return "CHOOSE END OF LINE";

            case SpellTargeting.AreaSquare:
                return "CHOOSE CENTER OF SQUARE";

            case SpellTargeting.AreaCone:
                return "CHOOSE START OF CONE";

            default:
                // The reference dies here and then uses this string anyway.
                return "CHOOSE";
        }
    }

    /// <summary>
    /// Whether leaving the menu now should ask to abandon the spell
    /// (the EXIT branch, <c>RunEvent.cpp:20288</c>).
    /// </summary>
    /// <remarks>
    /// <b>Only an empty selection prompts.</b> Fewer targets than the maximum is a perfectly good
    /// cast and EXIT takes it without asking; nothing at all is assumed to be a change of mind.
    /// </remarks>
    public bool ExitWouldAbandon =>
        Setup.SelectingUnits ? targets.Count == 0 : MapTarget is null;
}
