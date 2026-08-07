namespace UAF.Scripting.Tests;

/// <summary>
/// The annular-sector coverage walk: the shape it draws, what stops it, and the inner radius that
/// does nothing.
/// </summary>
/// <remarks>
/// <b>Several of these assert a whole picture rather than a property.</b> The walk is a Bresenham
/// sweep round an arc with per-octant tangent epsilons, and the shapes it produces are not quite
/// symmetric — the row through the centre is a square wider on one side, and the poles get a
/// one-cell spur. Those are the reference's, they are what a shipped design was balanced against,
/// and a property test ("roughly a disc") would let any of them drift.
/// </remarks>
public class AnnularCoverageTests
{
    private const int Extent = 21;

    private const int Centre = 10;

    /// <summary>Open ground unless the test walls or occupies a square.</summary>
    private sealed class Ground : IAuraWorld
    {
        public int MapWidth => Extent;

        public int MapHeight => Extent;

        public int CombatantCount => Combatants.Count;

        public List<(int X, int Y, AuraFacing Facing)> Combatants { get; } = [];

        public (int Width, int Height) Footprint { get; init; } = (1, 1);

        public HashSet<(int X, int Y)> Walls { get; } = [];

        public HashSet<(int X, int Y)> Occupants { get; } = [];

        public (int X, int Y, AuraFacing Facing) Combatant(int index) =>
            index >= 0 && index < Combatants.Count
                ? Combatants[index] : (-1, -1, AuraFacing.North);

        public (int Width, int Height) CombatantFootprint(int index) => Footprint;

        public AuraObstacle Obstacle(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Extent || y >= Extent) { return AuraObstacle.OffMap; }
            if (Walls.Contains((x, y))) { return AuraObstacle.Wall; }
            return Occupants.Contains((x, y)) ? AuraObstacle.Occupied : AuraObstacle.None;
        }

        public void RunAuraScript(Aura aura, string scriptName, int combatantIndex)
        {
        }
    }

    /// <summary>Builds a placed aura and runs the coverage walk over it.</summary>
    private static Aura Covered(Ground ground, int minRadius, int maxRadius,
                                int startAngle, int sectorSize,
                                AuraWavelength wavelength = AuraWavelength.Visible,
                                AuraAttachment attachment = AuraAttachment.Xy,
                                AuraFacing facing = AuraFacing.North)
    {
        var aura = new AuraStore(Extent * Extent).Create("x", "", "", "", "");

        // Written straight to the committed buffer: the coverage walk reads Current, and this test
        // is about the walk rather than about the placement check that fills it in.
        aura.Current.Shape = AuraShape.AnnularSector;
        aura.Current.Attachment = attachment;
        aura.Current.X = Centre;
        aura.Current.Y = Centre;
        aura.Current.Size1 = minRadius;
        aura.Current.Size2 = maxRadius;
        aura.Current.Size3 = startAngle;
        aura.Current.Size4 = sectorSize;
        aura.Current.Wavelength = wavelength;
        aura.Facing = facing;

        AnnularCoverage.Determine(aura, ground);
        return aura;
    }

    /// <summary><c>*</c> covered, <c>#</c> wall, <c>O</c> the centre, <c>.</c> nothing.</summary>
    private static string Picture(Aura aura, Ground ground)
    {
        var rows = new System.Text.StringBuilder();

        for (int y = 0; y < Extent; y++)
        {
            for (int x = 0; x < Extent; x++)
            {
                rows.Append(ground.Walls.Contains((x, y)) ? '#'
                            : x == Centre && y == Centre ? 'O'
                            : (aura.Cells[(y * Extent) + x] & 1) != 0 ? '*' : '.');
            }

            rows.Append('\n');
        }

        return rows.ToString();
    }

    private static string Rows(params string[] rows) => string.Join("\n", rows) + "\n";

    private static string Blank(int count) => string.Join("\n", Enumerable.Repeat(new string('.', Extent), count));

    [Fact]
    public void A_full_circle_is_a_disc_with_a_spur_at_each_pole()
    {
        var ground = new Ground();

        Assert.Equal(Rows(
            Blank(6),
            "..........*..........",
            "........*****........",
            ".......*******.......",
            ".......*******.......",
            "......****O****......",
            ".......*******.......",
            ".......*******.......",
            "........*****........",
            "..........*..........",
            Blank(6)),
            Picture(Covered(ground, 0, 4, 0, 360), ground));
    }

    [Fact]
    public void The_inner_radius_does_nothing_at_all()
    {
        var ground = new Ground();

        // minD2 is computed in both walkers and never read again. There is no hole, and there
        // never was one -- so a design asking for a ring got a disc.
        Assert.Equal(Picture(Covered(ground, 0, 4, 0, 360), ground),
                     Picture(Covered(ground, 3, 4, 0, 360), ground));
    }

    [Fact]
    public void A_quarter_sector_starts_at_east_and_runs_counter_clockwise()
    {
        var ground = new Ground();

        // 0 degrees is east and angles increase counter-clockwise, which on a screen whose y grows
        // downwards means the wedge opens upwards.
        Assert.Equal(Rows(
            Blank(6),
            "..........*..........",
            "........*****........",
            "........******.......",
            ".........*****.......",
            "..........O****......",
            Blank(10)),
            Picture(Covered(ground, 0, 4, 0, 90), ground));
    }

    [Fact]
    public void A_wall_stops_a_visible_aura()
    {
        var ground = new Ground();
        for (int y = 8; y <= 12; y++) { ground.Walls.Add((12, y)); }

        Assert.Equal(Rows(
            Blank(6),
            "..........*..........",
            "........****.........",
            ".......*****#........",
            ".......*****#........",
            "......****O*#........",
            ".......*****#........",
            ".......*****#........",
            "........****.........",
            "..........*..........",
            Blank(6)),
            Picture(Covered(ground, 0, 4, 0, 360), ground));
    }

    [Fact]
    public void Nothing_stops_a_neutrino_aura_not_even_a_wall()
    {
        var ground = new Ground();
        for (int y = 8; y <= 12; y++) { ground.Walls.Add((12, y)); }

        Assert.Equal(Rows(
            Blank(6),
            "..........*..........",
            "........*****........",
            ".......*****#*.......",
            ".......*****#*.......",
            "......****O*#**......",
            ".......*****#*.......",
            ".......*****#*.......",
            "........*****........",
            "..........*..........",
            Blank(6)),
            Picture(Covered(ground, 0, 4, 0, 360, AuraWavelength.Neutrino), ground));
    }

    [Fact]
    public void A_combatant_casts_a_shadow_in_a_visible_aura_and_none_in_an_xray_one()
    {
        static Ground WithSomebodyStanding()
        {
            var ground = new Ground();
            for (int y = 8; y <= 12; y++) { ground.Occupants.Add((12, y)); }
            return ground;
        }

        var visible = WithSomebodyStanding();
        var xray = WithSomebodyStanding();

        string behindVisible = Picture(Covered(visible, 0, 4, 0, 360), visible);
        string behindXray = Picture(Covered(xray, 0, 4, 0, 360, AuraWavelength.Xray), xray);

        // Only the visible walk tests for an occupied square; the X-ray one looks for walls alone.
        Assert.NotEqual(behindVisible, behindXray);
        Assert.Contains("......****O*", behindVisible, StringComparison.Ordinal);
        Assert.Contains("......****O****", behindXray, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unattached_aura_covers_nothing_however_large_its_radius()
    {
        var ground = new Ground();

        // LocateAuraCenters adds nothing for AURA_ATTACH_NONE, so the walk runs zero times.
        var aura = Covered(ground, 0, 8, 0, 360, attachment: AuraAttachment.None);

        Assert.DoesNotContain('*', Picture(aura, ground));
    }

    [Theory]
    [InlineData(-1, 4, 0, 90)]        // negative inner radius
    [InlineData(4, 2, 0, 90)]         // outer smaller than inner
    [InlineData(0, 4, -1, 90)]        // negative start angle
    [InlineData(0, 4, 0, 0)]          // empty sector
    public void Bad_sizes_leave_the_previous_mask_standing_rather_than_clearing_it(
        int minRadius, int maxRadius, int startAngle, int sectorSize)
    {
        var ground = new Ground();
        var aura = Covered(ground, 0, 4, 0, 360);
        string before = Picture(aura, ground);

        aura.Current.Size1 = minRadius;
        aura.Current.Size2 = maxRadius;
        aura.Current.Size3 = startAngle;
        aura.Current.Size4 = sectorSize;
        AnnularCoverage.Determine(aura, ground);

        // Every guard returns before the memset, so an invalid size freezes the aura where it was
        // -- it does not turn it off. The same trap as AURA_SHAPE_GLOBAL, from the other side.
        Assert.Equal(before, Picture(aura, ground));
    }

    // ---- where the aura radiates from -----------------------------------------------------------

    [Fact]
    public void A_combatant_attached_aura_radiates_from_every_cell_of_its_perimeter()
    {
        var ground = new Ground { Footprint = (3, 3) };
        ground.Combatants.Add((4, 4, AuraFacing.North));

        var aura = new AuraStore(Extent * Extent).Create("x", "", "", "", "");
        aura.Current.Attachment = AuraAttachment.Combatant;
        aura.Current.CombatantIndex = 0;

        // Eight of the nine cells of a 3x3 -- the outline, not the middle, and not just the square
        // the combatant is nominally on.
        Assert.Equal(
            [(4, 4), (4, 5), (4, 6), (5, 4), (5, 6), (6, 4), (6, 5), (6, 6)],
            AnnularCoverage.Centers(aura, ground).OrderBy(c => c.X).ThenBy(c => c.Y).ToList());
    }

    [Fact]
    public void A_one_by_one_combatant_gives_exactly_one_centre()
    {
        var ground = new Ground();
        ground.Combatants.Add((7, 3, AuraFacing.North));

        var aura = new AuraStore(Extent * Extent).Create("x", "", "", "", "");
        aura.Current.Attachment = AuraAttachment.CombatantFacing;
        aura.Current.CombatantIndex = 0;

        Assert.Equal([(7, 3)], AnnularCoverage.Centers(aura, ground).ToList());
    }

    [Fact]
    public void An_out_of_range_combatant_index_gives_no_centres_rather_than_the_origin()
    {
        var ground = new Ground();

        var aura = new AuraStore(Extent * Extent).Create("x", "", "", "", "");
        aura.Current.Attachment = AuraAttachment.Combatant;
        aura.Current.CombatantIndex = 5;        // there are none at all

        // The reference assigns a (0,0) point on this path and then returns without adding it, so
        // the assignment is dead and the aura covers nothing. It does not cover the corner.
        Assert.Empty(AnnularCoverage.Centers(aura, ground));
    }

    // ---- facing --------------------------------------------------------------------------------

    [Fact]
    public void A_facing_attached_wedge_turns_with_the_combatant()
    {
        var ground = new Ground();
        ground.Combatants.Add((Centre, Centre, AuraFacing.North));

        string East = Wedge(AuraFacing.East);
        string North = Wedge(AuraFacing.North);
        string South = Wedge(AuraFacing.South);
        string West = Wedge(AuraFacing.West);

        // Four different quarters of the map, and east is the unrotated one.
        Assert.Equal(4, new HashSet<string>([East, North, South, West], StringComparer.Ordinal).Count);
        Assert.Equal(Wedge(AuraFacing.East, startAngle: 0), Wedge(AuraFacing.North, startAngle: 270));

        string Wedge(AuraFacing facing, int startAngle = 0)
        {
            var aura = new AuraStore(Extent * Extent).Create("x", "", "", "", "");
            aura.Current.Shape = AuraShape.AnnularSector;
            aura.Current.Attachment = AuraAttachment.CombatantFacing;
            aura.Current.CombatantIndex = 0;
            aura.Current.Size2 = 5;
            aura.Current.Size3 = startAngle;
            aura.Current.Size4 = 90;
            aura.Facing = facing;

            AnnularCoverage.Determine(aura, ground);
            return Picture(aura, ground);
        }
    }

    // ---- the edges of the map -------------------------------------------------------------------

    [Fact]
    public void An_aura_at_the_corner_of_the_map_does_not_write_off_the_end_of_its_mask()
    {
        var ground = new Ground();
        var aura = new AuraStore(Extent * Extent).Create("x", "", "", "", "");
        aura.Current.Shape = AuraShape.AnnularSector;
        aura.Current.Attachment = AuraAttachment.Xy;
        aura.Current.X = 0;
        aura.Current.Y = 0;
        aura.Current.Size2 = 8;
        aura.Current.Size4 = 360;

        // Only a wall stops a ray, and an off-map square is not a wall -- so the reference marks
        // cells outside the map and writes past the end of the array. This is the port's guard.
        AnnularCoverage.Determine(aura, ground);

        Assert.NotEqual(0, aura.Cells[0] | aura.Cells[1]);
    }
}
