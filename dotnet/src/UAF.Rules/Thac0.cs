namespace UAF.Rules;

/// <summary>One baseclass's standing, as the combat rules need it.</summary>
/// <param name="Thac0Table">
/// The baseclass's <c>THAC0</c> blob — <c>HIGHEST_CHARACTER_LEVEL</c> bytes, indexed by level − 1.
/// </param>
public readonly record struct BaseclassStanding(int CurrentLevel, int PreviousLevel,
                                                IReadOnlyList<byte> Thac0Table);

/// <summary>
/// "To hit armour class 0" — the attack number a character rolls against
/// (<c>CHARACTER::getCharTHAC0</c>, <c>Char.cpp:6023</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>There are two definitions of <c>getCharTHAC0</c> in the source and only one is compiled.</b>
/// They sit either side of <c>#ifdef OldDualClass20180126</c>, which is defined nowhere in the
/// tree, so the <c>#else</c> half at <c>Char.cpp:6023</c> is live and the other is dead — the same
/// trap as <c>ProjectVersion.h</c>. This ports the live one.
/// </para>
/// <para>
/// <b>Lower is better, so the best baseclass wins.</b> The walk starts at 20 — the unskilled
/// value — and keeps the <i>minimum</i> across the character's baseclasses, which is why a
/// fighter/mage attacks as a fighter.
/// </para>
/// </remarks>
public static class Thac0
{
    /// <summary>What a character with no usable baseclass rolls against.</summary>
    public const int Unskilled = 20;

    /// <summary><c>HIGHEST_CHARACTER_LEVEL</c> — the table's length, and the level clamp.</summary>
    public const int HighestLevel = 40;

    /// <summary>
    /// The character's THAC0 across all its baseclasses.
    /// </summary>
    /// <remarks>
    /// <b>A drained baseclass still counts, through its <i>previous</i> level.</b> The level used is
    /// <c>currentLevel</c> when it is positive and <c>previousLevel</c> otherwise, so a character
    /// drained to zero in a baseclass keeps the attack number it had — it does not fall back to
    /// unskilled. Whether that baseclass counts at all is <see cref="CanUse"/>'s question.
    /// </remarks>
    public static int ForCharacter(IEnumerable<BaseclassStanding> baseclasses)
    {
        ArgumentNullException.ThrowIfNull(baseclasses);

        var all = baseclasses as IReadOnlyList<BaseclassStanding> ?? [.. baseclasses];
        int best = Unskilled;

        foreach (var standing in all)
        {
            if (!CanUse(standing, all))
            {
                continue;
            }

            int level = standing.CurrentLevel > 0 ? standing.CurrentLevel : standing.PreviousLevel;
            if (level < 1)
            {
                continue;
            }

            if (level > HighestLevel)
            {
                level = HighestLevel;
            }

            if (standing.Thac0Table.Count < level)
            {
                continue;   // a truncated table contributes nothing rather than throwing
            }

            int thac0 = standing.Thac0Table[level - 1];
            if (thac0 < best)
            {
                best = thac0;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether a baseclass counts toward the character's combat numbers
    /// (<c>CHARACTER::CanUseBaseclass</c>, <c>Char.cpp:7427</c>).
    /// </summary>
    /// <remarks>
    /// <b>This is the dual-class rule.</b> A current baseclass always counts. A <i>previous</i> one
    /// — the half a dual-classed character abandoned — counts only once some current baseclass has
    /// climbed strictly past the level it was left at. Until then the character fights as its new
    /// class, however good the old one was, which is the whole point of the restriction.
    /// </remarks>
    public static bool CanUse(BaseclassStanding standing,
                              IReadOnlyList<BaseclassStanding> allBaseclasses)
    {
        ArgumentNullException.ThrowIfNull(allBaseclasses);

        if (standing.CurrentLevel > 0)
        {
            return true;
        }

        if (standing.PreviousLevel <= 0)
        {
            return false;
        }

        foreach (var other in allBaseclasses)
        {
            // Only an undrained baseclass can release a previous one.
            if (other.PreviousLevel > 0)
            {
                continue;
            }

            if (other.CurrentLevel > standing.PreviousLevel)
            {
                return true;
            }
        }

        return false;
    }
}
