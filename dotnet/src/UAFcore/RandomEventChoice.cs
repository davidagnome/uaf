using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Picks which branch a <c>RANDOM_EVENT_DATA</c> takes
/// (<c>RANDOM_EVENT_DATA::OnKeypress</c>, <c>RunEvent.cpp:12550</c>).
/// </summary>
/// <remarks>
/// <para>
/// Separated from the presentation because the selection is the whole event — the screen is one
/// line of text and a Return — and because it is the part worth pinning with a fixed die.
/// </para>
/// <para>
/// <b>Two filters, and both matter.</b> A branch counts only if its chance is above zero
/// <i>and</i> its target is an event the level actually contains. A design that deletes an event
/// without clearing the branch that named it would otherwise have that branch's share of the
/// probability silently vanish into a dead end; here the weight is removed from the total instead,
/// so the surviving branches keep their relative odds.
/// </para>
/// </remarks>
public static class RandomEventChoice
{
    /// <summary>
    /// The chosen event id, or null when the event has nothing to pick and should just chain.
    /// </summary>
    /// <param name="isValidEvent">Whether an id names an event this level holds.</param>
    /// <param name="dice">
    /// A single roll of an <i>n</i>-sided die, 1..n — <see cref="Game.Dice"/>, so a test can pin it.
    /// </param>
    /// <remarks>
    /// <para>
    /// The roll is <c>RollDice(total, 1, 0)</c>, so it lands in 1..total, and the walk takes the
    /// first branch whose running total <b>reaches or passes</b> it. With chances of 30 and 70 a
    /// roll of 30 takes the first branch and 31 the second — the boundary belongs to the earlier
    /// one.
    /// </para>
    /// <para>
    /// <b>The chances need not sum to 100.</b> The total is whatever they add up to and the die is
    /// sized to match, so a design using 1/2/3 gets sixths. Normalising to a percentage would
    /// change the outcome of every such design.
    /// </para>
    /// </remarks>
    public static uint? Pick(RandomEvent random, Func<uint, bool> isValidEvent, Func<int, int> dice)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(isValidEvent);
        ArgumentNullException.ThrowIfNull(dice);

        var eligible = new List<RandomBranch>(random.Branches.Count);
        int total = 0;

        foreach (var branch in random.Branches)
        {
            if (branch.Chance > 0 && isValidEvent(branch.Chain))
            {
                eligible.Add(branch);
                total += branch.Chance;
            }
        }

        if (eligible.Count == 0 || total == 0)
        {
            return null;
        }

        int roll = dice(total);
        int running = 0;

        foreach (var branch in eligible)
        {
            running += branch.Chance;
            if (running >= roll)
            {
                return branch.Chain;
            }
        }

        // Unreachable for a roll inside 1..total, and the reference has the same fall-through:
        // it chains rather than picking, which is what returning null means here.
        return null;
    }
}
