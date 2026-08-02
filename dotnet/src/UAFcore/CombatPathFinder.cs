namespace UAFcore;

/// <summary>
/// A route across the combat grid, as a list of squares to step through.
/// </summary>
/// <remarks>
/// The starting square is <b>not</b> included: the reference walks parents back from the
/// destination and stops when it reaches the source (<c>path.cpp:806</c>), so the first entry is
/// the first square to move into.
/// </remarks>
public sealed record CombatPath(IReadOnlyList<(int X, int Y)> Steps)
{
    public int StepCount => Steps.Count;

    /// <summary>The square the mover ends on, or null for an empty path.</summary>
    public (int X, int Y)? Destination => Steps.Count > 0 ? Steps[^1] : null;
}

/// <summary>
/// The combat pathfinder (<c>CPathFinder::GeneratePath</c>, <c>path.cpp:566</c>).
/// </summary>
/// <remarks>
/// <para>
/// A cost-ordered best-first search over the eight neighbours of each square, terminating as soon
/// as a square inside the destination rectangle is reached. It is <b>not</b> the A* implementation
/// that sits above it in <c>path.cpp</c> — that one is guarded by <c>#ifdef OLDPATH</c>, and
/// <c>OLDPATH</c> is commented out at <c>path.h:97</c> and defined nowhere, so the whole
/// <c>_asNode</c> / open-list / closed-list machinery at <c>path.cpp:88–441</c> is dead. Its
/// comment says the old one "took cpu time proportional to the fourth power of the distance".
/// </para>
/// <para>
/// Diagonal steps are legal and cost more: <c>GetCost</c> is <c>5 · d² + 5</c> over the squared
/// Euclidean distance (<c>path.cpp:47</c>), so an orthogonal step is 10 and a diagonal 15. That
/// ratio is 1.5 rather than √2 ≈ 1.414, which makes diagonals slightly less attractive than true
/// geometry would — deliberate or not, it is what decides the shape of every walk.
/// </para>
/// </remarks>
public sealed class CombatPathFinder
{
    /// <summary>Neighbour offsets, orthogonal first (<c>dX</c>/<c>dY</c>, <c>path.cpp:505</c>).</summary>
    /// <remarks>
    /// The order matters: equal-cost ties are broken by which neighbour was queued first, so
    /// reordering this changes which of several shortest routes a combatant walks. The file keeps
    /// the previous ordering in a comment marked "Old method compatibility"; this is the live one.
    /// </remarks>
    private static ReadOnlySpan<int> StepX => [0, -1, 1, 0, -1, 1, -1, 1];

    /// <inheritdoc cref="StepX"/>
    private static ReadOnlySpan<int> StepY => [-1, 0, 0, 1, -1, -1, 1, 1];

    private const int Unvisited = -1;
    private const int Invalid = -2;

    private readonly CombatMap map;

    public CombatPathFinder(CombatMap map) =>
        this.map = map ?? throw new ArgumentNullException(nameof(map));

    /// <summary>The footprint being moved. 1×1 unless a large monster is walking.</summary>
    public int PathWidth { get; set; } = 1;

    /// <inheritdoc cref="PathWidth"/>
    public int PathHeight { get; set; } = 1;

    /// <summary>Whether other combatants block the route (<c>OccupantsBlock</c>).</summary>
    public bool OccupantsBlock { get; set; } = true;

    /// <summary>A combatant to treat as absent — normally the one doing the moving.</summary>
    public int IgnoreCombatant { get; set; } = CombatMap.NoDude;

    /// <summary>
    /// Finds a route to a single square.
    /// </summary>
    public CombatPath? To(int startX, int startY, int destX, int destY,
                          bool moveOriginPoint = false) =>
        To(startX, startY, destX, destY, destX, destY, moveOriginPoint);

    /// <summary>
    /// Finds a route into a destination rectangle
    /// (<c>PATH_MANAGER::GetPath</c>, <c>path.cpp:870</c>).
    /// </summary>
    /// <param name="moveOriginPoint">
    /// When true the mover's <i>origin square</i> must land inside the rectangle; when false it is
    /// enough for any part of its footprint to overlap. Attacking a large monster uses the second.
    /// </param>
    /// <returns>
    /// The route, or null when there is none — <b>including when the mover is already there</b>.
    /// The reference returns the same −1 for both cases (<c>path.cpp:919</c>), so a caller cannot
    /// distinguish "no route" from "no move needed"; <see cref="IsAlreadyWithin"/> exists so a
    /// caller that cares can ask first.
    /// </returns>
    public CombatPath? To(int startX, int startY,
                          int destLeft, int destTop, int destRight, int destBottom,
                          bool moveOriginPoint = false)
    {
        if (IsAlreadyWithin(startX, startY, destLeft, destTop, destRight, destBottom,
                            moveOriginPoint))
        {
            return null;
        }

        return Search(startX, startY, destLeft, destTop, destRight, destBottom, moveOriginPoint);
    }

    /// <summary>
    /// Whether the mover already overlaps the destination, in which case the reference does not
    /// search at all.
    /// </summary>
    public bool IsAlreadyWithin(int startX, int startY,
                                int destLeft, int destTop, int destRight, int destBottom,
                                bool moveOriginPoint = false)
    {
        if (moveOriginPoint)
        {
            return startX >= destLeft && startY >= destTop
                   && startX <= destRight && startY <= destBottom;
        }

        return startX + PathWidth - 1 >= destLeft
               && startY + PathHeight - 1 >= destTop
               && startX <= destRight && startY <= destBottom;
    }

    /// <summary>
    /// Whether a square can be occupied by the moving footprint
    /// (<c>GetValid</c>, <c>path.cpp:62</c>).
    /// </summary>
    private bool IsValid(int x, int y) =>
        map.Contains(x, y)
        && map.Obstacle(x, y, PathWidth, PathHeight, OccupantsBlock, IgnoreCombatant)
           == ObstacleType.None;

    /// <summary>Step cost (<c>GetCost</c>, <c>path.cpp:47</c>): 10 orthogonal, 15 diagonal.</summary>
    private static int Cost(int fromX, int fromY, int toX, int toY)
    {
        int dx = fromX - toX;
        int dy = fromY - toY;
        return (5 * ((dx * dx) + (dy * dy))) + 5;
    }

    private CombatPath? Search(int startX, int startY,
                               int destLeft, int destTop, int destRight, int destBottom,
                               bool moveOriginPoint)
    {
        // Which squares count as arrival. With moveOriginPoint the rectangle collapses to its
        // top-left corner -- note the reference uses destLeft/destTop for both, ignoring
        // destRight/destBottom entirely in that mode (path.cpp:607).
        int arriveLeft, arriveRight, arriveTop, arriveBottom;
        if (moveOriginPoint)
        {
            arriveLeft = arriveRight = destLeft;
            arriveTop = arriveBottom = destTop;
        }
        else
        {
            arriveLeft = destLeft - PathWidth + 1;
            arriveRight = destRight;
            arriveTop = destTop - PathHeight + 1;
            arriveBottom = destBottom;
        }

        int rows = map.Height;
        int total = map.Width * rows;

        // queueIndex doubles as the visited set: -1 not yet tested, -2 tested and impassable,
        // >= 0 the node's position in the queue. The reference clears it a column at a time as
        // the frontier widens, to keep a short search from paying for a big array; that is a
        // memory optimisation with no effect on the result, so this clears it once.
        var queueIndex = new int[total];
        Array.Fill(queueIndex, Unvisited);

        var nodeId = new int[total];
        var parentId = new int[total];
        var nodeCost = new int[total];

        int sourceId = (startX * rows) + startY;
        nodeId[0] = sourceId;
        parentId[0] = -1;
        nodeCost[0] = 0;
        queueIndex[sourceId] = 0;

        int inQueue = 1;
        int examined = 0;

        while (inQueue > examined)
        {
            int parentIdValue = nodeId[examined];
            int parentX = parentIdValue / rows;
            int parentY = parentIdValue % rows;

            for (int i = 0; i < 8; i++)
            {
                int childX = parentX + StepX[i];
                int childY = parentY + StepY[i];

                if (childX < 0 || childX >= map.Width || childY < 0 || childY >= rows)
                {
                    continue;
                }

                int childId = (rows * childX) + childY;

                if (queueIndex[childId] == Invalid)
                {
                    continue;
                }

                if (queueIndex[childId] == Unvisited && !IsValid(childX, childY))
                {
                    queueIndex[childId] = Invalid;
                    continue;
                }

                int childCost = nodeCost[examined] + Cost(parentX, parentY, childX, childY);
                int at = queueIndex[childId];

                if (at >= 0)
                {
                    // Already queued. The reference only improves a node that has not been
                    // examined yet; reaching an examined one more cheaply is impossible here and
                    // it puts up an error box if it happens.
                    if (childCost < nodeCost[at] && at >= examined)
                    {
                        parentId[at] = parentIdValue;
                        nodeCost[at] = childCost;
                        CostSort(nodeId, parentId, nodeCost, queueIndex, at);
                    }
                    else if (childCost == nodeCost[at] && at >= examined && (at & 1) != 0)
                    {
                        // "An attempt to get more random-looking walks" -- on odd queue slots an
                        // equal-cost rival takes over as parent. Deterministic despite the intent.
                        parentId[at] = parentIdValue;
                    }

                    continue;
                }

                queueIndex[childId] = inQueue;
                nodeId[inQueue] = childId;
                parentId[inQueue] = parentIdValue;
                nodeCost[inQueue] = childCost;
                inQueue++;

                // Arrival is tested on the node just *added*, not on the node being examined, so
                // the search stops one expansion earlier than a textbook Dijkstra would.
                if (childX >= arriveLeft && childX <= arriveRight
                    && childY >= arriveTop && childY <= arriveBottom)
                {
                    return Rebuild(nodeId, parentId, queueIndex, inQueue - 1, sourceId, rows);
                }

                CostSort(nodeId, parentId, nodeCost, queueIndex, inQueue - 1);
            }

            examined++;
        }

        return null;
    }

    private static CombatPath Rebuild(int[] nodeId, int[] parentId, int[] queueIndex,
                                      int from, int sourceId, int rows)
    {
        var steps = new List<(int X, int Y)>();
        for (int id = nodeId[from]; id != sourceId; id = parentId[queueIndex[id]])
        {
            steps.Add((id / rows, id % rows));
        }

        steps.Reverse();        // built destination-first, as AddHead does
        return new CombatPath(steps);
    }

    /// <summary>
    /// Moves a freshly-costed node toward the front of the queue
    /// (<c>CostSort</c>, <c>path.cpp:526</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a general sort: it walks one node forward past any run of equal-or-greater cost,
    /// swapping it with the <i>first</i> node of each such run rather than shuffling the whole
    /// block. The queue therefore stays grouped by cost without ever being fully ordered.
    /// </para>
    /// <para>
    /// The <c>i &amp; 1</c> test is the second of the two "more random-looking walks" rules — on
    /// odd indices a node jumps ahead of its equals, on even ones it stays put. It is deterministic,
    /// and it decides which of several equal-length routes gets walked, so it is not optional.
    /// </para>
    /// </remarks>
    private static void CostSort(int[] nodeId, int[] parentId, int[] nodeCost, int[] queueIndex,
                                 int i)
    {
        while (i > 0)
        {
            int iCost = nodeCost[i];
            int jCost = nodeCost[i - 1];
            if (jCost < iCost)
            {
                break;
            }

            if (jCost > iCost || (i & 1) != 0)
            {
                int j = i - 1;
                while (j > 0 && nodeCost[j - 1] == jCost)
                {
                    j--;
                }

                (nodeId[i], nodeId[j]) = (nodeId[j], nodeId[i]);
                (parentId[i], parentId[j]) = (parentId[j], parentId[i]);
                nodeCost[i] = jCost;
                nodeCost[j] = iCost;

                queueIndex[nodeId[i]] = i;
                queueIndex[nodeId[j]] = j;
                i = j;
            }

            if (iCost == jCost)
            {
                break;
            }
        }
    }
}
