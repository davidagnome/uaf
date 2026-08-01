namespace UAFcore;

/// <summary>
/// How far off an encounter starts (<c>eventDistType</c>, <c>GameEvent.h:63</c>).
/// </summary>
/// <remarks>
/// The last three are internal to <c>TEXT_EVENT</c>'s auto-approach and are not authored;
/// <c>NUM_DIST_TYPES</c> is 4, so only the first four are ever stored in an event.
/// </remarks>
public enum EncounterDistance
{
    UpClose = 0,
    Nearby = 1,
    FarAway = 2,
    AutoFarAway = 3,
    AutoUpClose = 4,
    AutoNearby = 5,
}

/// <summary>Where one monster ended up, and which side it approached from.</summary>
/// <remarks><c>MonsterPlacement</c>, <c>Combatants.h:70</c>.</remarks>
public sealed class MonsterSlot
{
    /// <summary>0=north, 1=east, 2=south, 3=west, or −1 for unassigned.</summary>
    public int DirectionFromParty { get; set; } = -1;

    public int PlaceX { get; set; } = -1;

    public int PlaceY { get; set; } = -1;

    public bool IsPlaced => PlaceX >= 0;
}

/// <summary>
/// The mutable state a monster-placement turtle program runs against
/// (<c>MonsterArrangement</c>, <c>Combatants.h:92</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The turtle works in a sheared frame, not in map squares.</b> Its position is party-relative,
/// and <see cref="MoveTurtleY"/> shifts <see cref="TurtleX"/> by the same delta so that
/// <c>x − y</c> is held constant (<c>Combatants.cpp:2743</c>). That is deliberate: the combat map
/// is the dungeon rotated 45°, so the axis across a corridor is <c>x − y</c>, and the
/// <see cref="LimitMinX"/>/<see cref="LimitMaxX"/> tests compare against <c>x − y</c> rather than
/// <c>x</c> (<c>Combatants.cpp:2698</c>). Treating these as ordinary map coordinates puts monsters
/// on the diagonal.
/// </para>
/// <para>
/// One arrangement is run per approach direction, and the four directions redefine what
/// forward/back/left/right mean via <see cref="Dx"/> and <see cref="Dy"/>.
/// </para>
/// </remarks>
public sealed class MonsterArrangement
{
    /// <summary>Turtle position, party-relative and sheared. See the class remarks.</summary>
    public int TurtleX { get; set; }

    /// <summary>Turtle position, party-relative and sheared. See the class remarks.</summary>
    public int TurtleY { get; set; }

    /// <summary>The footprint the turtle tests with; <c>S</c> sets it from the current monster.</summary>
    public int TurtleWidth { get; set; } = 1;

    /// <summary>The footprint the turtle tests with.</summary>
    public int TurtleHeight { get; set; } = 1;

    /// <summary>The party's square on the combat map — the turtle's origin.</summary>
    public int PartyX { get; set; }

    /// <summary>The party's square on the combat map — the turtle's origin.</summary>
    public int PartyY { get; set; }

    /// <summary>Which side this run is placing monsters on. 0=north, 1=east, 2=south, 3=west.</summary>
    public int CurrentDirection { get; set; }

    /// <summary>Whether <c>V</c> has switched on the line-of-sight requirement.</summary>
    public bool RequireLineOfSight { get; set; }

    /// <summary>
    /// Minimum <i>squared</i> distance from every party member, or negative for no requirement.
    /// </summary>
    public int MinDistanceSquared { get; set; } = -1;

    /// <summary>Placement limits. The X pair bounds <c>x − y</c>, not <c>x</c>.</summary>
    public int LimitMinX { get; set; } = int.MinValue;

    /// <inheritdoc cref="LimitMinX"/>
    public int LimitMaxX { get; set; } = int.MaxValue;

    /// <inheritdoc cref="LimitMinX"/>
    public int LimitMinY { get; set; } = int.MinValue;

    /// <inheritdoc cref="LimitMinX"/>
    public int LimitMaxY { get; set; } = int.MaxValue;

    /// <summary>Bounding box of the placed party, party-relative.</summary>
    public int PartyMinX { get; set; } = int.MaxValue;

    /// <inheritdoc cref="PartyMinX"/>
    public int PartyMaxX { get; set; } = int.MinValue;

    /// <inheritdoc cref="PartyMinX"/>
    public int PartyMinY { get; set; } = int.MaxValue;

    /// <inheritdoc cref="PartyMinX"/>
    public int PartyMaxY { get; set; } = int.MinValue;

    /// <summary>Party member positions, party-relative.</summary>
    public List<(int X, int Y)> PartyPositions { get; } = [];

    /// <summary>One slot per combatant, indexed as <c>m_aCombatants</c> is.</summary>
    public MonsterSlot[] Slots { get; private set; } = [];

    /// <summary>The monster being placed, or −1.</summary>
    public int CurrentMonster { get; set; } = -1;

    /// <summary>How many monsters approach from each direction.</summary>
    public int[] CountByDirection { get; } = new int[4];

    /// <summary>Two-deep turtle position stack for <c>u</c> and <c>o</c>.</summary>
    internal int[] TurtleStack { get; } = new int[4];

    /// <summary>Column offset for <c>F</c>, <c>R</c>, <c>L</c>, <c>B</c>, indexed by letter − 'A'.</summary>
    public int[] Dx { get; } = new int['R' - 'A' + 1];

    /// <inheritdoc cref="Dx"/>
    public int[] Dy { get; } = new int['R' - 'A' + 1];

    /// <summary>Sizes the per-combatant slots (<c>Activate</c>, <c>Combatants.h:125</c>).</summary>
    public void Activate(int combatantCount)
    {
        Slots = new MonsterSlot[combatantCount];
        for (int i = 0; i < combatantCount; i++)
        {
            Slots[i] = new MonsterSlot();
        }

        Array.Clear(CountByDirection);
        PartyPositions.Clear();
        PartyMinX = PartyMinY = int.MaxValue;
        PartyMaxX = PartyMaxY = int.MinValue;
    }

    /// <summary>
    /// Resets the per-direction state before a turtle program runs
    /// (<c>Combatants.cpp:2562</c>), including the forward/right/left/back tables.
    /// </summary>
    public void BeginDirection(int direction)
    {
        CurrentDirection = direction;
        CurrentMonster = -1;
        TurtleWidth = 1;
        TurtleHeight = 1;
        RequireLineOfSight = false;
        MinDistanceSquared = -1;
        LimitMinX = LimitMinY = int.MinValue;
        LimitMaxX = LimitMaxY = int.MaxValue;
        TurtleX = 0;
        TurtleY = 0;
        Array.Clear(TurtleStack);

        // Transcribed from the switch at Combatants.cpp:2573. Note these are sheared-frame steps,
        // so "forward" for a northern approach is (-1,-1) rather than (0,-1).
        (int fx, int fy, int rx, int ry, int lx, int ly, int bx, int by) t = direction switch
        {
            0 => (-1, -1, 1, 0, -1, 0, 1, 1),      // headed north
            1 => (1, 0, 1, 1, -1, -1, -1, 0),      // headed east
            2 => (1, 1, -1, 0, 1, 0, -1, -1),      // headed south
            _ => (-1, 0, -1, -1, 1, 1, 1, 0),      // headed west
        };

        Dx['F' - 'A'] = t.fx; Dy['F' - 'A'] = t.fy;
        Dx['R' - 'A'] = t.rx; Dy['R' - 'A'] = t.ry;
        Dx['L' - 'A'] = t.lx; Dy['L' - 'A'] = t.ly;
        Dx['B' - 'A'] = t.bx; Dy['B' - 'A'] = t.by;
    }

    /// <summary>Sets the turtle's column directly (<c>MoveTurtleX</c>).</summary>
    public void MoveTurtleX(int x) => TurtleX = x;

    /// <summary>
    /// Sets the turtle's row, carrying the column with it (<c>MoveTurtleY</c>,
    /// <c>Combatants.cpp:2743</c>).
    /// </summary>
    /// <remarks>
    /// <b>This moves diagonally, and that is not a bug.</b> It holds <c>x − y</c> constant, which
    /// is the coordinate the placement limits are expressed in. See the class remarks.
    /// </remarks>
    public void MoveTurtleY(int y)
    {
        TurtleX += y - TurtleY;
        TurtleY = y;
    }
}
