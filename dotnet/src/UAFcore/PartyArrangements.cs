namespace UAFcore;

/// <summary>
/// The party formation tables and the offset lookup over them
/// (<c>Combatants.cpp:2303</c>, and the index arithmetic at <c>:2168</c>).
/// </summary>
/// <remarks>
/// <para>
/// The tables themselves are generated — see <c>dotnet/tools/gen-party-arrangements.py</c>. Each
/// party member gets two characters giving its offset from the party origin, so a formation is
/// authored rather than computed: facing north, a party of three stands abreast at
/// <c>bB AB BB</c> = (−1,+1), (0,+1), (+1,+1).
/// </para>
/// <para>
/// <b>These are the <i>preferred</i> positions, not the final ones.</b> Each is the starting point
/// of a spiral search (<see cref="CombatPlacement"/>), so a formation that would put somebody
/// inside a wall degrades to the nearest free square rather than failing.
/// </para>
/// <para>
/// A design can replace the whole table at runtime: the <c>PartyArrangement</c> global script hook
/// returns a string that is used instead when it is exactly the same length
/// (<c>Combatants.cpp:2489</c>). That needs the GPDL VM running global scripts and is not wired up;
/// <see cref="For"/> takes the table as an argument so a hook result can be passed straight in when
/// it is.
/// </para>
/// </remarks>
public static partial class PartyArrangements
{
    /// <summary>
    /// Decodes one offset character (<c>Decode</c>, <c>Combatants.cpp:2016</c>).
    /// </summary>
    /// <remarks>
    /// Upper case counts up from zero and lower case counts <i>down</i> from zero: <c>A</c> and
    /// <c>a</c> are both 0, <c>B</c> is +1, <c>b</c> is −1. Note the asymmetry — this is not a
    /// sign bit, and reading <c>a</c> as −1 shifts every negative offset by one.
    /// </remarks>
    public static int Decode(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => 'a' - c,
        _ => 0,
    };

    /// <summary>
    /// The preferred offset from the party origin for one member of a party.
    /// </summary>
    /// <param name="table">
    /// <see cref="Indoor"/> or <see cref="Outdoor"/>, or a same-length replacement from the
    /// <c>PartyArrangement</c> hook.
    /// </param>
    /// <param name="facing">The party's facing, which selects one of four direction blocks.</param>
    /// <param name="partySize">How many are in the formation, 1..<see cref="MaxPartyMembers"/>.</param>
    /// <param name="index">The member's place in the marching order, from zero.</param>
    /// <remarks>
    /// The original indexes the table with no bounds check at all, so a party larger than the
    /// tables describe reads off the end of the array. This throws instead — the callers are all
    /// in-process and a silent misplacement is worse than a stack trace.
    /// </remarks>
    public static (int Dx, int Dy) For(string table, Facing facing, int partySize, int index)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfLessThan(partySize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(partySize, MaxPartyMembers);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, partySize);

        // Facing is already north=0, east=1, south=2, west=3, which is the block order.
        // (partySize - 1) * partySize is the running total of the shorter runs before this one:
        // 2 + 4 + ... + 2(n-1).
        int at = ((int)facing * DirectionBlock)
                 + ((partySize - 1) * partySize)
                 + (2 * index);

        if (at + 1 >= table.Length)
        {
            throw new ArgumentException(
                $"arrangement table is {table.Length} characters; " +
                $"{facing}/{partySize}/{index} needs {at + 2}", nameof(table));
        }

        return (Decode(table[at]), Decode(table[at + 1]));
    }
}
