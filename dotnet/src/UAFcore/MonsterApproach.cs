namespace UAFcore;

/// <summary>
/// Which sides an encounter may approach from (<c>eventDirType</c>, <c>GameEvent.h:75</c>).
/// </summary>
public enum EncounterDirection
{
    Any = 0,
    North = 1,
    South = 2,
    East = 3,
    West = 4,
    NorthSouth = 5,
    NorthEast = 6,
    NorthWest = 7,
    SouthEast = 8,
    SouthWest = 9,
    EastWest = 10,
    NorthSouthEast = 11,
    NorthSouthWest = 12,
    NorthWestEast = 13,
    WestSouthEast = 14,
    InFront = 15,
}

/// <summary>
/// Assigns each monster the side it approaches from
/// (<c>GetNextDirection</c> / <c>getNextMonsterCombatDirection</c>, <c>Combatants.cpp:3124</c>,
/// <c>:2035</c>).
/// </summary>
/// <remarks>
/// <para>
/// The event names a set of permitted sides and the monsters are dealt round-robin across it, so
/// an encounter set to <see cref="EncounterDirection.NorthSouth"/> alternates and one set to
/// <see cref="EncounterDirection.North"/> puts everything on one side. The rotation is stateful:
/// each call advances from the previous direction, which is why this is a cursor rather than a
/// function of the monster's index.
/// </para>
/// <para>
/// <b>The cycles are not all in the same order</b>, and they are transcribed rather than derived —
/// <see cref="EncounterDirection.NorthSouthEast"/> runs N→E→S→N while
/// <see cref="EncounterDirection.NorthWestEast"/> runs W→N→E→W. Generating them from the name
/// would get several wrong.
/// </para>
/// </remarks>
public sealed class MonsterApproach(EncounterDirection allowed, Facing partyFacing)
{
    /// <summary>The cursor. Starts unset, as the original's <c>currDir = Any</c> does.</summary>
    private EncounterDirection current = EncounterDirection.Any;

    /// <summary>The side the party faces, used by <see cref="EncounterDirection.InFront"/>.</summary>
    private EncounterDirection Facing => partyFacing switch
    {
        UAFcore.Facing.North => EncounterDirection.North,
        UAFcore.Facing.East => EncounterDirection.East,
        UAFcore.Facing.South => EncounterDirection.South,
        _ => EncounterDirection.West,
    };

    /// <summary>Advances the cursor and returns the next side, as 0=north, 1=east, 2=south, 3=west.</summary>
    public int Next()
    {
        current = allowed switch
        {
            // Any cycles N→E→S→W, starting from whichever way the party faces.
            EncounterDirection.Any => current switch
            {
                EncounterDirection.North => EncounterDirection.East,
                EncounterDirection.East => EncounterDirection.South,
                EncounterDirection.South => EncounterDirection.West,
                EncounterDirection.West => EncounterDirection.North,
                _ => Facing,
            },

            EncounterDirection.InFront => Facing,

            EncounterDirection.North => EncounterDirection.North,
            EncounterDirection.South => EncounterDirection.South,
            EncounterDirection.East => EncounterDirection.East,
            EncounterDirection.West => EncounterDirection.West,

            EncounterDirection.NorthSouth =>
                current == EncounterDirection.North ? EncounterDirection.South
                                                    : EncounterDirection.North,
            EncounterDirection.NorthEast =>
                current == EncounterDirection.North ? EncounterDirection.East
                                                    : EncounterDirection.North,
            EncounterDirection.NorthWest =>
                current == EncounterDirection.West ? EncounterDirection.North
                                                   : EncounterDirection.West,
            EncounterDirection.SouthEast =>
                current == EncounterDirection.East ? EncounterDirection.South
                                                   : EncounterDirection.East,
            EncounterDirection.SouthWest =>
                current == EncounterDirection.South ? EncounterDirection.West
                                                    : EncounterDirection.South,
            EncounterDirection.EastWest =>
                current == EncounterDirection.East ? EncounterDirection.West
                                                   : EncounterDirection.East,

            EncounterDirection.NorthSouthEast => current switch
            {
                EncounterDirection.North => EncounterDirection.East,
                EncounterDirection.East => EncounterDirection.South,
                _ => EncounterDirection.North,
            },
            EncounterDirection.NorthSouthWest => current switch
            {
                EncounterDirection.South => EncounterDirection.West,
                EncounterDirection.West => EncounterDirection.North,
                _ => EncounterDirection.South,
            },
            EncounterDirection.NorthWestEast => current switch
            {
                EncounterDirection.West => EncounterDirection.North,
                EncounterDirection.North => EncounterDirection.East,
                _ => EncounterDirection.West,
            },
            EncounterDirection.WestSouthEast => current switch
            {
                EncounterDirection.East => EncounterDirection.South,
                EncounterDirection.South => EncounterDirection.West,
                _ => EncounterDirection.East,
            },

            _ => Facing,
        };

        // The engine's own 0=N, 1=E, 2=S, 3=W, which is not the enum's order.
        return current switch
        {
            EncounterDirection.North => 0,
            EncounterDirection.East => 1,
            EncounterDirection.South => 2,
            EncounterDirection.West => 3,
            _ => 0,
        };
    }
}
