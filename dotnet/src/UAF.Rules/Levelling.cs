namespace UAF.Rules;

/// <summary>
/// Character advancement: turning experience into a level, and deciding who may train.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>BASE_CLASS_DATA::GetLevel</c> (<c>class.cpp:6449</c>),
/// <c>CHARACTER::GetAllowedLevel</c> and <c>CHARACTER::IsReadyToTrain</c> (<c>Char.cpp</c>).
/// </para>
/// <para>
/// This is per-<b>baseclass</b>, not per-class. A multiclass character advances each of its
/// baseclasses independently off that baseclass's own experience and its own thresholds, which is
/// why nothing here takes a class.
/// </para>
/// </remarks>
public static class Levelling
{
    /// <summary>
    /// The reference's "no such skill" sentinel (<c>NoSkill</c>, <c>class.h:1051</c>) — not a level.
    /// </summary>
    public const int NoLevelCap = unchecked((int)0x80000000);

    /// <summary>The skill a design defines to cap a baseclass's level (<c>GlobalData.cpp:93</c>).</summary>
    public const string MaxLevelSkill = "MaxLevel$SYS$";

    /// <summary>
    /// The level a given experience total earns in a baseclass.
    /// </summary>
    /// <param name="thresholds">
    /// The baseclass's cumulative experience table, in order — <c>BaseclassRecord.ExperienceLevels</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// A forward scan returning the first index whose threshold the experience does <i>not</i>
    /// reach, and the table's length when it reaches them all. Because designs write a leading
    /// <c>0</c>, a character with no experience is level 1 rather than level 0.
    /// </para>
    /// <para>
    /// <b>The table is the design's, not AD&amp;D's.</b> A design may publish any thresholds it
    /// likes and there is no hard-coded fallback, so this must never be second-guessed against a
    /// rulebook — it is why the thresholds had to be read out of <c>baseclass.dat</c> before
    /// levelling could exist at all.
    /// </para>
    /// </remarks>
    public static int GetLevel(IReadOnlyList<uint> thresholds, uint experience)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        for (int i = 0; i < thresholds.Count; i++)
        {
            if (experience < thresholds[i])
            {
                return i;
            }
        }
        return thresholds.Count;
    }

    /// <summary>
    /// The highest level a character is currently entitled to in one baseclass, or 0 when it is
    /// entitled to none.
    /// </summary>
    /// <param name="previousLevel">
    /// The baseclass's level-drain marker. Anything above zero means drained.
    /// </param>
    /// <param name="levelCap">
    /// A design-imposed ceiling, or <see cref="NoLevelCap"/> when the design sets none.
    /// </param>
    /// <remarks>
    /// <b>A drained baseclass is entitled to nothing at all</b> — <c>Char.cpp</c> returns 0 rather
    /// than the level the experience would otherwise buy, and <c>IncCurExperience</c> refuses to
    /// add to it (<c>class.cpp:4828</c>). The character's <i>other</i> baseclasses are unaffected,
    /// so this has to be asked per baseclass.
    /// </remarks>
    public static int GetAllowedLevel(IReadOnlyList<uint> thresholds, uint experience,
                                      int previousLevel, int levelCap = NoLevelCap)
    {
        if (previousLevel > 0)
        {
            return 0;
        }

        int allowed = GetLevel(thresholds, experience);
        if (levelCap != NoLevelCap && allowed > levelCap)
        {
            allowed = levelCap;
        }
        return allowed;
    }

    /// <summary>
    /// Whether a character may train in one baseclass: its experience entitles it to more than it
    /// has.
    /// </summary>
    public static bool IsReadyToTrain(IReadOnlyList<uint> thresholds, uint experience,
                                      int currentLevel, int previousLevel,
                                      int levelCap = NoLevelCap) =>
        GetAllowedLevel(thresholds, experience, previousLevel, levelCap) > currentLevel;

    /// <summary>
    /// Applies a training session, returning the new level.
    /// </summary>
    /// <param name="maxLevelGain">
    /// How many levels one session may grant. The engine passes this from the training hall.
    /// </param>
    /// <remarks>
    /// <c>getNewCharLevel</c> (<c>Char.cpp:5366</c>) advances one level at a time up to the
    /// entitlement, then clamps to <paramref name="maxLevelGain"/> levels above where it started.
    /// The cap is applied to the target before the walk, so a character eligible for four levels
    /// with a gain of one arrives at exactly one.
    /// </remarks>
    public static int Train(IReadOnlyList<uint> thresholds, uint experience, int currentLevel,
                            int previousLevel, int maxLevelGain, int levelCap = NoLevelCap)
    {
        int allowed = GetAllowedLevel(thresholds, experience, previousLevel, levelCap);
        int limit = currentLevel + maxLevelGain;
        return allowed > limit ? limit : Math.Max(allowed, currentLevel);
    }

    /// <summary>
    /// The experience a character is allowed to keep when its level is capped
    /// (<c>Char.cpp:5503</c>).
    /// </summary>
    /// <returns>
    /// The capped total, or <paramref name="experience"/> unchanged when nothing is forfeited.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The reference deliberately destroys experience here</b> — the comment names Manikus and
    /// says to "steal experience points so that this fellow does not qualify for any level higher
    /// than limitLevel". Without it a character held at a level would bank arbitrary experience and
    /// jump several levels the moment the cap lifted.
    /// </para>
    /// <para>
    /// It applies only when the limit is inside the table, and only to an undrained baseclass — a
    /// drained one keeps its experience, since it is already earning nothing.
    /// </para>
    /// </remarks>
    public static uint CapExperience(IReadOnlyList<uint> thresholds, uint experience,
                                     int limitLevel, int previousLevel)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        if (previousLevel > 0 || limitLevel < 0 || limitLevel >= thresholds.Count)
        {
            return experience;
        }

        uint maximum = thresholds[limitLevel] - 1;
        return experience > maximum ? maximum : experience;
    }

    /// <summary>
    /// The level cap a baseclass's own skills impose, or <see cref="NoLevelCap"/> for none.
    /// </summary>
    /// <param name="skills">The baseclass record's skill list.</param>
    /// <remarks>
    /// <b>Partial: this reads the baseclass side only.</b> The reference resolves
    /// <c>MaxLevel$SYS$</c> through a <c>SKILL_COMPUTATION</c> that also consults the character's
    /// <b>race</b> (<c>Char.cpp:GetLevelCap</c>), and <c>races.dat</c> has no reader yet. A design
    /// capping a level by race therefore goes unenforced here — no fixture in the corpus does, but
    /// that is not the same as it being safe.
    /// </remarks>
    public static int GetBaseclassLevelCap(IEnumerable<(string SkillId, int Value)> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        foreach (var (skillId, value) in skills)
        {
            if (string.Equals(skillId, MaxLevelSkill, StringComparison.Ordinal))
            {
                return value;
            }
        }
        return NoLevelCap;
    }
}
