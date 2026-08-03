namespace UAF.Rules;

/// <summary>
/// Rolling a new character's six ability scores (<c>rollSkillDie</c>,
/// <c>GameRules.cpp:1782</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Best of three, always.</b> Every score is rolled three times and the largest kept — which
/// is why new characters are so uniformly good, and why a design that lowers an ability's dice
/// moves the average far less than it looks like it should.
/// </para>
/// <para>
/// <b>Two eras.</b> Below 0.870 the dice are hard-coded 3d6 and the ability database is not
/// consulted at all; from 0.870 each ability carries its own <c>DICEPLUS</c>. The old path is
/// still live, so a design old enough gets 3d6 whatever its <c>ability.dat</c> says.
/// </para>
/// </remarks>
public static class AbilityRoll
{
    /// <summary><c>SKILL_DIE</c> — the sides of the pre-0.870 die.</summary>
    public const int SkillDie = 6;

    /// <summary>How many dice the pre-0.870 roll uses.</summary>
    public const int SkillDiceCount = 3;

    /// <summary>How many times each score is rolled before the best is kept.</summary>
    public const int Attempts = 3;

    /// <summary>
    /// Rolls one score the pre-0.870 way: three tries at 3d6, best kept.
    /// </summary>
    /// <param name="roll">Rolls <c>count</c> dice of <c>sides</c> and totals them.</param>
    public static int Legacy(Func<int, int, int> roll)
    {
        ArgumentNullException.ThrowIfNull(roll);

        int best = 0;
        for (int i = 0; i < Attempts; i++)
        {
            best = Math.Max(best, roll(SkillDiceCount, SkillDie));
        }
        return best;
    }

    /// <summary>
    /// Rolls one score from an ability's own dice, best of three.
    /// </summary>
    /// <param name="rollAbility">
    /// Rolls the ability's <c>DICEPLUS</c> once. Returning null stands for the reference's
    /// <c>RollAbility</c> answering false — which leaves that attempt at <b>zero</b> rather than
    /// skipping it, so an ability whose dice never roll produces a score of 0 and not a refusal.
    /// </param>
    public static int Modern(Func<int?> rollAbility)
    {
        ArgumentNullException.ThrowIfNull(rollAbility);

        int best = 0;
        for (int i = 0; i < Attempts; i++)
        {
            best = Math.Max(best, rollAbility() ?? 0);
        }
        return best;
    }

    /// <summary>
    /// Whether a score qualifies for exceptional strength (<c>rollStats</c>,
    /// <c>Char.cpp:4559</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Exactly 18, not 18 or more.</b> A character whose adjusted strength is 19 gets no
    /// percentile at all — the test is equality, so racial or magical strength above 18 skips the
    /// bonus rather than maximising it.
    /// </para>
    /// <para>
    /// <b>And it is the <i>class</i> that supplies the dice</b>, not the rules. The reference's
    /// commented-out predecessor restricted the bonus to fighters, rangers and paladins; the live
    /// code rolls whatever <c>strengthBonusDice</c> the class carries, so a class with none gets
    /// zero and the restriction is data rather than code.
    /// </para>
    /// </remarks>
    public const int ExceptionalStrength = 18;

    /// <inheritdoc cref="ExceptionalStrength"/>
    public static bool QualifiesForStrengthBonus(int adjustedStrength) =>
        adjustedStrength == ExceptionalStrength;

    /// <summary>
    /// Raises a score to a class's minimum, if it has one
    /// (<c>CheckNewCharClassScores</c>, <c>GameRules.cpp:1820</c>).
    /// </summary>
    /// <remarks>
    /// <b>Applied after the roll, and it only ever raises.</b> A character who rolls below what
    /// their class demands is given the minimum rather than re-rolled or refused — so picking a
    /// class with high requirements is, in this engine, a way to guarantee good scores.
    /// </remarks>
    public static int AtLeast(int rolled, int minimum) => Math.Max(rolled, minimum);
}
