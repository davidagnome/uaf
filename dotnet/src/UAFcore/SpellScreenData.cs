using UAF.Rules;

namespace UAFcore;

/// <summary>
/// What the spell screen needs: the spells on offer at each level, the counts that bound them, and
/// what has been taken so far.
/// </summary>
/// <remarks>
/// <b>Index 0 is the totals row and holds no spells</b> — <see cref="SpellAcquisition"/> reads it
/// for the global floor and ceiling and starts its loop at 1, so both lists here reserve the slot.
/// </remarks>
public sealed class SpellScreenData
{
    private readonly List<List<AvailableSpell>> byLevel;

    /// <summary>The word on the first menu entry: SELECT at creation, LEARN when scribing.</summary>
    public string Verb { get; }

    /// <summary>The per-level acquisition state, index 0 being the totals.</summary>
    public IReadOnlyList<SpellLevelState> Levels { get; }

    /// <summary>What was acquired, in the order it was taken.</summary>
    public List<AvailableSpell> Acquired { get; } = [];

    public SpellScreenData(string verb, List<List<AvailableSpell>> byLevel,
                           IReadOnlyList<SpellLevelState> levels)
    {
        ArgumentNullException.ThrowIfNull(byLevel);
        ArgumentNullException.ThrowIfNull(levels);

        Verb = verb;
        this.byLevel = byLevel;
        Levels = levels;
    }

    /// <summary>The spells still on offer at a level.</summary>
    public IReadOnlyList<AvailableSpell> Offered(int level) =>
        level >= 0 && level < byLevel.Count ? byLevel[level] : [];

    /// <summary>The acquisition state for a level.</summary>
    public SpellLevelState State(int level) =>
        Levels[Math.Clamp(level, 0, Levels.Count - 1)];

    /// <summary>
    /// Takes a spell off the level's list, whether or not the attempt succeeded.
    /// </summary>
    /// <remarks>
    /// <b>A failed attempt still consumes the spell.</b> The reference sets
    /// <c>m_spellAvailabilityList[i].learned = success</c> either way and the list is filtered on
    /// having been touched — so a character who fails a roll does not get another go at that
    /// spell, which is what makes <c>Num</c> ("how many he must try") a bound at all.
    /// </remarks>
    public void Taken(int level, AvailableSpell spell, bool acquired)
    {
        ArgumentNullException.ThrowIfNull(spell);

        if (level >= 0 && level < byLevel.Count)
        {
            byLevel[level].Remove(spell);
        }

        if (acquired)
        {
            Acquired.Add(spell);
        }
    }

    /// <summary>The next level with anything left, wrapping.</summary>
    public int NextLevel(int level, int delta)
    {
        int count = Levels.Count;
        if (count <= 1)
        {
            return level;
        }

        for (int i = 0; i < count; i++)
        {
            level += delta;
            if (level < 1)
            {
                level = count - 1;
            }
            else if (level >= count)
            {
                level = 1;
            }

            if (Offered(level).Count > 0)
            {
                return level;
            }
        }

        return level;
    }
}
