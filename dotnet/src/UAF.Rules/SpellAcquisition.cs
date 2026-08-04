namespace UAF.Rules;

/// <summary>
/// How many spells a character may take at one spell level (<c>MNMC</c>,
/// <c>GameEvent.h:4333</c>).
/// </summary>
/// <param name="Min">The floor later passes work back up to.</param>
/// <param name="Num">How many must be <i>attempted</i> before moving to the next level.</param>
/// <param name="Max">The ceiling at this level.</param>
/// <param name="Certain">How many are acquired with no roll at all.</param>
/// <remarks>
/// <b>Four counts, and only two of them are limits.</b> <c>Certain</c> is a free allowance and
/// <c>Num</c> is an obligation to try — a character who fails every roll still leaves the level,
/// because attempts are what <c>Num</c> counts and not successes.
/// </remarks>
public readonly record struct SpellCounts(int Min, int Num, int Max, int Certain);

/// <summary>What has happened so far at one spell level (<c>AcquireState</c>).</summary>
public sealed class SpellLevelState(SpellCounts counts, int available)
{
    public SpellCounts Counts { get; } = counts;

    /// <summary>How many spells the level offers at all.</summary>
    public int Available { get; } = available;

    public int Acquired { get; private set; }

    public int Attempted { get; private set; }

    /// <summary>Records one attempt and whether it worked.</summary>
    public void Record(bool acquired)
    {
        Attempted++;
        if (acquired)
        {
            Acquired++;
        }
    }
}

/// <summary>Why the acquisition loop stopped.</summary>
[Flags]
public enum AcquireProgress
{
    None = 0,

    /// <summary>Nothing left to take anywhere; the screen closes.</summary>
    AllLevels = 0x0001,

    /// <summary>Nothing left at the level showing; move to the next.</summary>
    ThisLevel = 0x0002,
}

/// <summary>
/// Learning spells at character creation (<c>INITIAL_MU_SPELLS_MENU_DATA</c>,
/// <c>RunEvent.cpp:23143</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every new character sees this screen, whatever their class.</b> The reference computes
/// <c>PickMUSpells</c> from the class, comments the line out, and assigns
/// <c>knowSpellsAtCreation = TRUE</c> unconditionally — so the <c>else</c> branch that skips
/// straight to the save prompt is dead, and a fighter is offered a spell list.
/// </para>
/// <para>
/// <b>It is a two-pass acquisition, and the passes have different goals.</b> The reference states
/// it in its own words above <c>AreWeDone</c>: pass 0 fills every level up to its <c>Max</c>, and
/// passes 1 onwards go back around bringing any level that fell short up to its <c>Min</c>. So
/// <c>Min</c> is not a floor checked at the end — it is the target of a second sweep.
/// </para>
/// </remarks>
public static class SpellAcquisition
{
    /// <summary>
    /// Whether one attempt succeeds (<c>RunEvent.cpp:23351</c>).
    /// </summary>
    /// <remarks>
    /// <b>The first <c>Certain</c> at a level are free; everything after rolls.</b> The test is
    /// <c>numAcquired &lt; certain</c>, so it counts <i>successes</i> and not attempts — a
    /// character who fails a roll still has their free allowance intact.
    /// </remarks>
    public static bool Acquires(SpellLevelState level, int probability, Func<int, int> roll)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(roll);

        if (level.Acquired < level.Counts.Certain)
        {
            return true;
        }

        return roll(100) <= probability;
    }

    /// <summary>
    /// Whether the level showing, or the whole screen, is finished
    /// (<c>AreWeDone</c>, <c>RunEvent.cpp:23143</c>).
    /// </summary>
    /// <param name="levels">
    /// One per spell level, <b>from index 1</b> — index 0 is not a level but the totals, carrying
    /// the global <c>Min</c> and <c>Max</c> across every level at once.
    /// </param>
    /// <param name="current">Which level is showing.</param>
    /// <param name="pass">0 for the first sweep, 1 or more for the top-up sweeps.</param>
    /// <remarks>
    /// <b>Index 0 holds the totals, not a spell level.</b> The loop starts at 1 and
    /// <c>m_acquireStates[0].mnmc</c> is read for the global ceiling and floor — so a reader that
    /// treats the array as levels 0..n counts the totals as a level and finishes early.
    /// </remarks>
    public static AcquireProgress Progress(IReadOnlyList<SpellLevelState> levels, int current,
                                           int pass)
    {
        ArgumentNullException.ThrowIfNull(levels);

        if (levels.Count == 0)
        {
            return Finished(AcquireProgress.None);
        }

        int totalAcquired = 0;
        bool oneLevelNotMax = false;
        bool oneLevelNotMin = false;
        bool thisLevelNotMax = false;

        for (int i = 1; i < levels.Count; i++)
        {
            var level = levels[i];
            totalAcquired += level.Acquired;

            // A level with nothing on offer is neither short of its minimum nor of its maximum:
            // it is simply out of the reckoning.
            if (level.Available <= 0)
            {
                continue;
            }

            if (level.Acquired < level.Counts.Max)
            {
                oneLevelNotMax = true;
                if (i == current)
                {
                    thisLevelNotMax = true;
                }
            }

            if (level.Acquired < level.Counts.Min)
            {
                oneLevelNotMin = true;
            }
        }

        var totals = levels[0].Counts;
        var result = AcquireProgress.ThisLevel;

        if (pass == 0)
        {
            if (!oneLevelNotMax)
            {
                return Finished(result);        // every level is full
            }

            if (totalAcquired >= totals.Max)
            {
                return Finished(result);        // the global ceiling
            }

            if (thisLevelNotMax)
            {
                result &= ~AcquireProgress.ThisLevel;
            }

            return result;
        }

        // Passes 1 onwards: bring the short levels up to Min.
        if (oneLevelNotMin)
        {
            // Some level is still short. The only thing that ends the screen here is the global
            // ceiling -- and note that the level showing is NOT held open even when it has room,
            // which is where this branch differs from pass 0. A later pass moves on as soon as
            // this level has its minimum, leaving its spare capacity unused.
            return totalAcquired >= totals.Max
                ? Finished(result)
                : result;
        }

        // Every level is at its minimum, so only the global floor can keep this going.
        if (totalAcquired >= totals.Min)
        {
            return Finished(result);
        }

        if (!oneLevelNotMax)
        {
            return Finished(result);        // nowhere left to put another spell
        }

        return thisLevelNotMax ? result & ~AcquireProgress.ThisLevel : result;
    }

    /// <summary>
    /// Finishing everything finishes the level showing too.
    /// </summary>
    /// <remarks>
    /// The reference does this as a postcondition — <c>if (result &amp; FinishedAllLevels) result
    /// |= FinishedThisLevel;</c> — so the two flags can never disagree, and a caller that checked
    /// only <c>ThisLevel</c> still advances off the last one.
    /// </remarks>
    private static AcquireProgress Finished(AcquireProgress result) =>
        result | AcquireProgress.AllLevels | AcquireProgress.ThisLevel;
}
