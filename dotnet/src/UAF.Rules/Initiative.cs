namespace UAF.Rules;

/// <summary>Who was caught off guard when a fight started (<c>eventSurpriseType</c>).</summary>
public enum Surprise
{
    /// <summary>Nobody: everyone rolls.</summary>
    Neither = 0,

    /// <summary>The party was surprised, so the monsters go first.</summary>
    Party,

    /// <summary>The monsters were surprised, so the party goes first.</summary>
    Monsters,
}

/// <summary>
/// Turn order within a combat round (<c>COMBATANT::RollInitiative</c>,
/// <c>UAFWin/Combatant.cpp</c>, and <c>COMBAT_DATA::DetermineCombatInitiative</c>,
/// <c>Combatants.cpp:1488</c>).
/// </summary>
/// <remarks>
/// <b>Lower acts earlier.</b> The order is an ascending sort, so 9 moves before 18 — the number is
/// a position in the round rather than a score to beat.
/// </remarks>
public static class Initiative
{
    /// <summary>The earliest slot in a round (<c>INITIATIVE_FirstDefault</c>).</summary>
    public const int First = 9;

    /// <summary>The latest (<c>INITIATIVE_LastDefault</c>).</summary>
    public const int Last = 18;

    /// <summary>
    /// One combatant's initiative.
    /// </summary>
    /// <param name="roll">
    /// A d10, used only when nobody is surprised. The reference rolls
    /// <c>RollDice(10, 1, First - 1)</c>, so the die is 1–10 and the result lands in 9–18.
    /// </param>
    /// <remarks>
    /// <b>Surprise does not modify the roll — it replaces it.</b> A surprised side is assigned the
    /// last slot outright and the other side the first, so the die is never consulted. Treating
    /// surprise as a bonus would leave the outcome uncertain when the reference makes it certain.
    /// </remarks>
    public static int Roll(Surprise surprise, bool isPartySide, int roll = 1)
    {
        int initiative = surprise switch
        {
            Surprise.Party => isPartySide ? Last : First,
            Surprise.Monsters => isPartySide ? First : Last,
            _ => roll + First - 1,
        };

        return Math.Clamp(initiative, First, Last);
    }

    /// <summary>
    /// Orders combatants for a round.
    /// </summary>
    /// <returns>Indices into <paramref name="initiatives"/>, in the order they act.</returns>
    /// <remarks>
    /// <b>The sort must be stable, and .NET's <c>List.Sort</c> is not.</b> The reference bubble
    /// sorts with a strict <c>&gt;</c> comparison (<c>Combatants.cpp:1514</c>), which leaves equal
    /// initiatives in their original order — and ties are common on a ten-sided range with a whole
    /// party rolling. An unstable sort would shuffle them differently, which changes who strikes
    /// first and is invisible until a save game diverges.
    /// </remarks>
    public static int[] Order(IReadOnlyList<int> initiatives)
    {
        ArgumentNullException.ThrowIfNull(initiatives);

        // OrderBy is documented stable; Array.Sort and List.Sort are explicitly not.
        return [.. Enumerable.Range(0, initiatives.Count).OrderBy(i => initiatives[i])];
    }
}
