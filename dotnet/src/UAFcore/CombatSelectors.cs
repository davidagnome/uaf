namespace UAFcore;

/// <summary>
/// The combatant a script's selector picks
/// (<c>GetNearestTo</c> and its neighbours, <c>Combatants.cpp:7500</c>–<c>:7700</c>).
/// </summary>
/// <remarks>
/// Rules rather than host plumbing, which is why they are here and not in
/// <see cref="GameScriptHost"/>: GPDL is the only caller today, but what "nearest" and "most
/// damaged" mean belongs with the fight.
/// </remarks>
public static class CombatSelectors
{
    /// <summary>
    /// The nearest combatant to another (<c>GetNearestTo</c>, <c>Combatants.cpp:7621</c>).
    /// </summary>
    /// <remarks>
    /// <b>It never excludes the combatant itself, so it always answers the one it was asked
    /// about.</b> The loop has no <c>i != self</c> guard and the distance from anyone to themselves
    /// is zero, which no other candidate can beat — the comparison is strictly <c>&lt;</c>. The
    /// function is useless as written and is transcribed that way, because a design's script was
    /// written against what it does rather than what it is called.
    /// </remarks>
    public static Combatant? Nearest(IReadOnlyList<Combatant> combatants, Combatant from)
    {
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(from);

        return Closest(combatants, from, enemiesOnly: false);
    }

    /// <summary>
    /// The nearest enemy (<c>GetNearestEnemyTo</c>, <c>Combatants.cpp:7671</c>).
    /// </summary>
    /// <remarks>
    /// <b>"Enemy" means <i>not friendly</i> in absolute terms, not "the other side from you".</b>
    /// The filter is <c>!GetIsFriendly()</c> with no reference to the asker, so a monster asking
    /// for its nearest enemy is handed the nearest <i>monster</i> — and by the rule above, itself.
    /// A party member gets the answer the name promises; nobody else does.
    /// </remarks>
    public static Combatant? NearestEnemy(IReadOnlyList<Combatant> combatants, Combatant from)
    {
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(from);

        return Closest(combatants, from, enemiesOnly: true);
    }

    private static Combatant? Closest(IReadOnlyList<Combatant> combatants, Combatant from,
                                      bool enemiesOnly)
    {
        Combatant? best = null;
        int nearest = int.MaxValue;

        foreach (var candidate in combatants)
        {
            if (enemiesOnly && candidate.IsFriendly)
            {
                continue;
            }

            int distance = MonsterAiScript.DistanceBetween(from, candidate);
            if (distance < nearest)
            {
                nearest = distance;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// The combatant on one side with the lowest or highest hit points
    /// (<c>GetMostDamagedEnemy</c> and its three neighbours, <c>Combatants.cpp:7500</c>).
    /// </summary>
    /// <param name="friendly">Which side to look at.</param>
    /// <param name="lowest">
    /// True for the "most damaged" pair, false for the "least damaged" pair.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>"Most damaged" means lowest hit points, not most damage taken.</b> The comparison is on
    /// <c>GetAdjHitPoints</c> alone, with no reference to the maximum — so a goblin at full health
    /// with four hit points is "more damaged" than a fighter on 60 of 100. The name is misleading
    /// and the rule is not.
    /// </para>
    /// <para>
    /// <b>The first of a tie wins</b>, in both directions: the comparisons are strict, so a later
    /// combatant with the same hit points never displaces an earlier one.
    /// </para>
    /// </remarks>
    public static Combatant? ByHitPoints(IReadOnlyList<Combatant> combatants, bool friendly,
                                         bool lowest)
    {
        ArgumentNullException.ThrowIfNull(combatants);

        Combatant? best = null;
        int extreme = lowest ? int.MaxValue : -1;

        foreach (var candidate in combatants)
        {
            if (candidate.IsFriendly != friendly)
            {
                continue;
            }

            int hitPoints = candidate.HitPoints;
            if (lowest ? hitPoints < extreme : hitPoints > extreme)
            {
                extreme = hitPoints;
                best = candidate;
            }
        }

        return best;
    }
}
