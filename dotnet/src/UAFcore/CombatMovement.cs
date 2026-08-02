namespace UAFcore;

/// <summary>
/// One of the eight directions a combatant can step (<c>PATH_DIR</c>, <c>path.h:27</c>).
/// </summary>
/// <remarks>
/// <see cref="None"/> is the reference's <c>PathBAD</c> = −1, returned when the source and
/// destination are the same square.
/// </remarks>
public enum PathDirection
{
    North = 0,
    NorthEast = 1,
    East = 2,
    SouthEast = 3,
    South = 4,
    SouthWest = 5,
    West = 6,
    NorthWest = 7,
    None = -1,
}

/// <summary>What happened when a combatant tried to step.</summary>
public enum MoveOutcome
{
    /// <summary>Nothing happened — no movement left, blocked, or already there.</summary>
    None,

    /// <summary>The combatant moved.</summary>
    Moved,

    /// <summary>Somebody was in the way and got attacked instead.</summary>
    Attacked,

    /// <summary>The combatant stepped off the map and has left the fight.</summary>
    Fled,
}

/// <summary>
/// Moving a combatant across the grid
/// (<c>MoveCombatant</c> and <c>TakeNextStep</c>, <c>Combatant.cpp:9293</c>, <c>:4026</c>).
/// </summary>
public static class CombatMovement
{
    /// <summary>
    /// The direction from one square to another (<c>GetDir</c>, <c>path.h:34</c>).
    /// </summary>
    public static PathDirection DirectionTo(int fromX, int fromY, int toX, int toY)
    {
        if (fromX > toX)
        {
            return fromY > toY ? PathDirection.NorthWest
                 : fromY < toY ? PathDirection.SouthWest
                 : PathDirection.West;
        }

        if (fromX < toX)
        {
            return fromY > toY ? PathDirection.NorthEast
                 : fromY < toY ? PathDirection.SouthEast
                 : PathDirection.East;
        }

        return fromY > toY ? PathDirection.North
             : fromY < toY ? PathDirection.South
             : PathDirection.None;
    }

    /// <summary>
    /// The nominal cost of a step (<c>GetDist</c>, <c>path.h:56</c>): 1 orthogonal, 2 diagonal.
    /// </summary>
    /// <remarks>
    /// Nominal because <see cref="Step"/> halves every second diagonal — see there.
    /// </remarks>
    public static int StepCost(PathDirection direction) => direction switch
    {
        PathDirection.North or PathDirection.East
            or PathDirection.South or PathDirection.West => 1,
        _ => 2,
    };

    /// <summary>
    /// Turns the icon to face a step (<c>FaceDirection</c>, <c>Combatant.cpp:8335</c>).
    /// </summary>
    /// <remarks>
    /// <b>Facing only ever becomes east or west.</b> The icon is a sprite that flips horizontally,
    /// so a north or south step leaves the facing <i>unchanged</i> — the reference's
    /// <c>default:</c> arm says "if north/south attacker, no need to change facing". The full
    /// eight-way direction is kept in <see cref="Combatant.MoveDirection"/> instead.
    /// </remarks>
    public static void Face(Combatant combatant, PathDirection direction)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        combatant.MoveDirection = direction;

        switch (direction)
        {
            case PathDirection.NorthWest or PathDirection.West or PathDirection.SouthWest:
                combatant.Facing = Facing.West;
                break;
            case PathDirection.NorthEast or PathDirection.East or PathDirection.SouthEast:
                combatant.Facing = Facing.East;
                break;
            default:
                break;      // north and south leave the facing alone
        }
    }

    /// <summary>
    /// Takes one step of a path, moving or attacking as the square allows
    /// (<c>MoveCombatant</c>, <c>Combatant.cpp:9293</c>).
    /// </summary>
    /// <param name="allowZeroMoveAttack">
    /// Whether an attack may be made into an occupied square even with no movement left. The round
    /// passes this so a combatant that has run out of movement can still swing at what it has
    /// walked up to.
    /// </param>
    /// <param name="canAttack">
    /// Whether the blocking combatant may be attacked. Supplied by the caller because
    /// <see cref="Targeting.CanAttack"/> needs weapons, which the combatant does not carry.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Every second diagonal is free.</b> A diagonal nominally costs 2, but
    /// <c>m_iNumDiagonalMoves</c> is incremented first and the cost drops to 1 whenever the count
    /// lands on an even number (<c>:9316</c>) — so diagonals cost 2, 1, 2, 1… and average 1.5.
    /// That is the same 1.5 the pathfinder charges (15 against 10), so the two agree, and it is the
    /// AD&amp;D "diagonals cost one and a half" rule made integral.
    /// </para>
    /// <para>
    /// <b>Stepping off the map is fleeing, not a failed move.</b> The reference's <c>else</c> arm
    /// sets status to fled, bumps the side's flee counter and ends the turn (<c>:9440</c>). A
    /// caller that treats an off-map destination as an error loses the only way out of a fight.
    /// </para>
    /// <para>
    /// <b>The occupancy test and the obstacle test disagree on purpose.</b> The blocking combatant
    /// is looked up first, and the wall test that follows passes <c>CheckOccupants = FALSE</c>
    /// (<c>:9362</c>) — otherwise the combatant just found would block its own attack.
    /// </para>
    /// </remarks>
    public static MoveOutcome Step(Combatant combatant, CombatMap map, int newX, int newY,
                                   bool allowZeroMoveAttack = false,
                                   Func<Combatant, int, bool>? canAttack = null)
    {
        ArgumentNullException.ThrowIfNull(combatant);
        ArgumentNullException.ThrowIfNull(map);

        if (newX == combatant.X && newY == combatant.Y)
        {
            return MoveOutcome.None;
        }

        var direction = DirectionTo(combatant.X, combatant.Y, newX, newY);
        int cost = StepCost(direction);

        // The counter moves whether or not the step is taken -- it is incremented before any of
        // the tests below, so a refused diagonal still shifts which of the next ones is free.
        if (cost == 2)
        {
            combatant.DiagonalMoves++;
            if ((combatant.DiagonalMoves & 1) == 0)
            {
                cost = 1;
            }
        }

        Face(combatant, direction);

        bool onMap = map.Fits(newX, newY, combatant.Icon.Width, combatant.Icon.Height);
        int blocker = onMap
            ? map.OccupantAt(newX, newY, combatant.Icon.Width, combatant.Icon.Height,
                             ignoreCombatant: combatant.Index)
            : CombatMap.NoDude;

        bool affordable = combatant.Movement < combatant.MaxMovement - (cost - 1);
        if (!((blocker != CombatMap.NoDude && allowZeroMoveAttack) || affordable))
        {
            return MoveOutcome.None;
        }

        if (!onMap)
        {
            combatant.Status = CharacterStatus.Fled;
            combatant.TurnIsDone = true;
            return MoveOutcome.Fled;
        }

        if (blocker != CombatMap.NoDude)
        {
            if (canAttack?.Invoke(combatant, blocker) == true)
            {
                combatant.Target = blocker;
                combatant.State = CombatantState.Attacking;
                return MoveOutcome.Attacked;
            }

            return MoveOutcome.None;
        }

        // CheckOccupants is false here on purpose -- see the remarks.
        if (map.Obstacle(newX, newY, combatant.Icon.Width, combatant.Icon.Height,
                         checkOccupants: false) != ObstacleType.None)
        {
            return MoveOutcome.None;
        }

        combatant.Movement += cost;
        map.Remove(combatant.X, combatant.Y, combatant.Icon.Width, combatant.Icon.Height);
        map.Place(newX, newY, combatant.Index, combatant.Icon.Width, combatant.Icon.Height);
        combatant.X = newX;
        combatant.Y = newY;
        combatant.DidMove = true;

        return MoveOutcome.Moved;
    }

    /// <summary>
    /// Walks the next square of a path (<c>TakeNextStep</c>, <c>Combatant.cpp:4026</c>).
    /// </summary>
    /// <param name="path">
    /// The remaining route. The consumed step is removed, so the same list can be walked down to
    /// empty across successive calls.
    /// </param>
    /// <returns>What the step did; <see cref="MoveOutcome.None"/> when the path is spent.</returns>
    /// <remarks>
    /// The reference clears the path whenever a step fails or the route runs out, so a combatant
    /// that cannot continue does not keep retrying the same square. That is the caller's job here,
    /// signalled by anything other than <see cref="MoveOutcome.Moved"/>.
    /// </remarks>
    public static MoveOutcome TakeNextStep(Combatant combatant, CombatMap map,
                                           List<(int X, int Y)> path,
                                           bool allowZeroMoveAttack = false,
                                           Func<Combatant, int, bool>? canAttack = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Count == 0)
        {
            return MoveOutcome.None;
        }

        var (x, y) = path[0];
        path.RemoveAt(0);

        return Step(combatant, map, x, y, allowZeroMoveAttack, canAttack);
    }
}
