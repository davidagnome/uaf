namespace UAFcore;

/// <summary>
/// The experience table read in both directions
/// (<c>$DAT_Baseclass_Level</c> and <c>$DAT_Baseclass_Experience</c>, <c>GPDLexec.cpp:4081</c>).
/// </summary>
/// <remarks>
/// <b>One table, two questions.</b> A level's entry is the experience it takes to reach it — so
/// the experience for a level is that entry read straight out, and the level for an experience is
/// how many entries it has passed. A rule rather than host plumbing, which is why it is here.
/// </remarks>
public static class BaseclassTable
{
    /// <param name="wantExperience">
    /// True to ask "what does level <paramref name="value"/> cost", false to ask "what level does
    /// <paramref name="value"/> experience reach".
    /// </param>
    public static int Read(IReadOnlyList<uint> experienceLevels, int value, bool wantExperience)
    {
        ArgumentNullException.ThrowIfNull(experienceLevels);

        return wantExperience
            ? CostOfLevel(experienceLevels, value)
            : LevelReached(experienceLevels, value);
    }

    /// <summary>
    /// What a level costs.
    /// </summary>
    /// <remarks>
    /// <b>Levels are one-based and the table is not.</b> Level 1 is entry zero, so a level outside
    /// the table — including level 0 and a negative one — answers nothing rather than reading off
    /// the end.
    /// </remarks>
    public static int CostOfLevel(IReadOnlyList<uint> experienceLevels, int level)
    {
        ArgumentNullException.ThrowIfNull(experienceLevels);

        return level >= 1 && level <= experienceLevels.Count
            ? (int)experienceLevels[level - 1]
            : 0;
    }

    /// <summary>
    /// What level an amount of experience reaches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Counted forwards and stopping at the first entry not yet paid for</b>, so a table whose
    /// entries are not ascending is read only as far as its first fall. That is the reference's
    /// own shape and a design with a mis-sorted table gets the truncated answer.
    /// </para>
    /// <para>
    /// <b>Exactly meeting an entry reaches that level</b> — the test is <c>&gt;=</c>, so the
    /// experience a level costs is enough to be it rather than one short.
    /// </para>
    /// </remarks>
    public static int LevelReached(IReadOnlyList<uint> experienceLevels, int experience)
    {
        ArgumentNullException.ThrowIfNull(experienceLevels);

        int reached = 0;
        for (int i = 0; i < experienceLevels.Count && experience >= experienceLevels[i]; i++)
        {
            reached = i + 1;
        }

        return reached;
    }
}
