namespace UAF.Rules;

/// <summary>
/// Which ability score, for the bounds table.
/// </summary>
/// <remarks>
/// <b>Not <c>UAFcore.Ability</c>, which is a different list for a different job</b> — that one is
/// the design's wire ordinal for a WHO_TRIES check and has six members. This one adds the strength
/// percentile, which is a score with its own range rather than one of the six a character rolls.
/// </remarks>
public enum AbilityScore
{
    Strength,
    StrengthMod,
    Intelligence,
    Wisdom,
    Dexterity,
    Constitution,
    Charisma,
}

/// <summary>
/// The range an ability score may hold (<c>MIN_STRENGTH</c> … <c>MAX_CHARISMA</c>,
/// <c>Globals.cpp:506</c>) and the clamp that applies it (<c>LimitAb</c>, <c>Char.cpp:13599</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A character carries three versions of every score, and they are not interchangeable.</b>
/// The <i>permanent</i> one is what the record stores. The <i>adjusted</i> one is that plus
/// whatever spell effects are on it, and is <b>unbounded</b> — an effect can drive it anywhere.
/// The <i>limited</i> one is the adjusted one clamped to this table, and is the only form the
/// rules are meant to read. GPDL exposes all three, so a design's script can see the raw sum a
/// clamp would have hidden.
/// </para>
/// <para>
/// <b>Every score shares one range except the strength percentile.</b> Five of the six run 3 to
/// 25; the percentile runs 0 to 100, because it is a percentage rather than a score.
/// </para>
/// </remarks>
public static class AbilityBounds
{
    /// <summary>The lowest a score may be clamped to.</summary>
    public static int Min(AbilityScore ability) =>
        ability == AbilityScore.StrengthMod ? 0 : 3;

    /// <summary>The highest a score may be clamped to.</summary>
    public static int Max(AbilityScore ability) =>
        ability == AbilityScore.StrengthMod ? 100 : 25;

    /// <summary>
    /// Clamps an adjusted score into its range (<c>LimitAb</c>).
    /// </summary>
    /// <remarks>
    /// <b>Floor then ceiling, in that order</b> — <c>min(max(v,min),max)</c>, which is
    /// <see cref="Math.Clamp(int,int,int)"/> for any sane pair and would differ only for a table
    /// whose bounds crossed. Written out rather than clamped so the order stays visible — the same
    /// care a hit-point clamp needs for real, where the maximum really can fall below the floor.
    /// </remarks>
    public static int Limit(int value, AbilityScore ability) =>
        Math.Min(Math.Max(value, Min(ability)), Max(ability));
}
