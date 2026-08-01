namespace UAF.Rules;

/// <summary>
/// What one strength score is worth (<c>determineStrengthProperties</c>,
/// <c>GameRules.cpp:1542</c>).
/// </summary>
/// <param name="MaxMod">
/// The exceptional-strength percentile this row covers, as an exclusive upper bound, or null for a
/// row that does not depend on the percentile.
/// </param>
/// <param name="BendBars">Bend bars / lift gates, as a percentage.</param>
public readonly record struct StrengthRow(int MinScore, int MaxScore, int? MaxMod,
                                          int HitBonus, int DamageBonus,
                                          int OpenDoors, int OpenMagicDoors, int BendBars);

/// <summary>
/// The strength table — hit and damage bonuses, and the door and bar-bending numbers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Transcribed mechanically from the C++ switch, not by hand.</b> It is 24 rows of six numbers
/// and the bands are irregular in both directions; typing it out is the sort of thing that produces
/// one wrong digit and no failing test, which is why <c>DesignVersion</c> and the GPDL opcode
/// tables were generated the same way.
/// </para>
/// <para>
/// <b>Strengths 19–25 are the project's own extension</b>, as they are in
/// <see cref="Encumbrance"/> — the original rules stop at 18/00.
/// </para>
/// </remarks>
public static class Strength
{
    /// <summary>
    /// The table, in the C++ switch's order. The percentile rows are tried in sequence, so the
    /// first whose <see cref="StrengthRow.MaxMod"/> the modifier is under wins.
    /// </summary>
    public static readonly StrengthRow[] Table =
    [
        new(1, 1, null, -5, -4, 1, 0, 0),
        new(2, 2, null, -3, -2, 1, 0, 0),
        new(3, 3, null, -3, -1, 2, 0, 0),
        new(4, 5, null, -2, -1, 3, 0, 0),
        new(6, 7, null, -1, 0, 4, 0, 0),
        new(8, 9, null, 0, 0, 5, 0, 1),
        new(10, 11, null, 0, 0, 6, 0, 2),
        new(12, 13, null, 0, 0, 7, 0, 4),
        new(14, 15, null, 0, 0, 8, 0, 7),
        new(16, 16, null, 0, 1, 9, 0, 10),
        new(17, 17, null, 1, 1, 10, 0, 13),

        // 18 splits by percentile. The first row is the exact-zero case, then four bands, then
        // everything at 100 and above.
        new(18, 18, 0, 1, 2, 11, 0, 16),
        new(18, 18, 51, 1, 3, 12, 0, 20),
        new(18, 18, 76, 2, 3, 13, 0, 25),
        new(18, 18, 91, 2, 4, 14, 0, 30),
        new(18, 18, 100, 2, 5, 15, 3, 35),
        new(18, 18, null, 3, 6, 16, 6, 40),

        new(19, 19, null, 3, 7, 16, 8, 50),
        new(20, 20, null, 3, 8, 17, 10, 60),
        new(21, 21, null, 4, 9, 17, 12, 70),
        new(22, 22, null, 4, 10, 18, 14, 80),
        new(23, 23, null, 5, 11, 18, 16, 90),
        new(24, 24, null, 6, 12, 19, 17, 95),
        new(25, 25, null, 7, 16, 19, 18, 99),
    ];

    /// <summary>What a strength of 0, or anything the table does not cover, is worth.</summary>
    /// <remarks>
    /// The reference logs "Unhandled strength" and leaves the out-parameters untouched, which in
    /// practice means whatever the caller had. Returning an all-zero row is the same thing for a
    /// freshly-zeroed caller and predictable for everyone else.
    /// </remarks>
    public static readonly StrengthRow None = new(0, 0, null, 0, 0, 0, 0, 0);

    /// <summary>The row a score falls in.</summary>
    public static StrengthRow For(int score, int mod = 0)
    {
        foreach (var row in Table)
        {
            if (score < row.MinScore || score > row.MaxScore)
            {
                continue;
            }

            // A row with no percentile matches outright; the 18 rows are tried in order, and the
            // exact-zero case first, exactly as the if/else chain reads.
            if (row.MaxMod is not { } limit)
            {
                return row;
            }

            if (limit == 0 ? mod == 0 : mod < limit)
            {
                return row;
            }
        }

        return None;
    }

    /// <summary>The bonus a strength score adds to a melee damage roll.</summary>
    public static int DamageBonus(int score, int mod = 0) => For(score, mod).DamageBonus;

    /// <summary>The bonus a strength score adds to an attack roll.</summary>
    public static int HitBonus(int score, int mod = 0) => For(score, mod).HitBonus;
}
