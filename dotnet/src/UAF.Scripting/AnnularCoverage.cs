namespace UAF.Scripting;

/// <summary>
/// The one aura shape that computes anything: a sector of an annulus, traced out to
/// <c>size2</c> squares and stopped by whatever the light runs into
/// (<c>DetermineAnnularCoverage</c>, <c>Combatants.cpp:8182</c>, with <c>LocateAuraCenters</c> at
/// <c>:7823</c> and the two octant walkers at <c>:7871</c> and <c>:8025</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The four sizes are <c>minRadius</c>, <c>maxRadius</c>, <c>startAngle</c> and
/// <c>sectorSize</c></b> — so <c>$AURA_Size(2, 8, 90, 45)</c> is "from radius 2 to 8, a 45°
/// wedge starting at 90°". Angles are degrees counter-clockwise from east.
/// </para>
/// <para>
/// <b>Except that the inner radius does nothing at all.</b> Both walkers compute
/// <c>minD2 = minRadius*minRadius</c> and then never read it. Every "annular sector" the engine
/// has ever drawn is a solid wedge from the centre out to <c>maxRadius</c>; there is no hole.
/// The parameter is validated (<c>if (minRadius &lt; 0) return;</c>) and passed down through two
/// call layers to be discarded. Transcribed, because a design was balanced against the shape the
/// engine actually drew.
/// </para>
/// <para>
/// <b>Bad sizes leave the previous mask standing.</b> The four guards all <c>return</c> before the
/// <c>memset</c> at <c>:8225</c>, so a negative radius or a zero sector does not clear the aura —
/// it freezes it. The same trap as <see cref="AuraShape.Global"/>, from a different direction.
/// </para>
/// <para>
/// <b>The walk is per-ray, from the centre outwards, and it marks two cells per step.</b> For each
/// perimeter cell of the max-radius circle that falls inside the wedge, a Bresenham line is run
/// from the centre towards it; each step marks the cell it is on and the next one across, and the
/// ray stops at a wall — and, if the aura is <see cref="AuraWavelength.Visible"/>, at an occupied
/// square too. So combatants cast aura shadows and X-ray auras do not.
/// </para>
/// </remarks>
public static class AnnularCoverage
{
    private const double DegreesToRadians = 3.1415926535 / 180.0;

    /// <summary>
    /// Fills in <see cref="Aura.Cells"/> for an <see cref="AuraShape.AnnularSector"/> aura.
    /// </summary>
    public static void Determine(Aura aura, IAuraWorld world)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(world);

        int minRadius = aura.Current.Size1;
        if (minRadius < 0) { return; }
        int maxRadius = aura.Current.Size2;
        if (maxRadius < minRadius) { return; }
        int startAngle = aura.Current.Size3;
        if (startAngle < 0) { return; }
        int sectorSize = aura.Current.Size4;
        if (sectorSize <= 0) { return; }

        if (aura.Current.Attachment == AuraAttachment.CombatantFacing)
        {
            // Degrees counter-clockwise from east, so the wedge turns with the combatant.
            startAngle += aura.Facing switch
            {
                AuraFacing.North => 90,
                AuraFacing.East => 0,
                AuraFacing.South => 270,
                AuraFacing.West => 180,
                AuraFacing.NorthEast => 45,
                AuraFacing.SouthEast => 315,
                AuraFacing.SouthWest => 225,
                AuraFacing.NorthWest => 135,
                _ => 0,
            };
            startAngle %= 360;
        }

        // Only now, after every guard has had its chance to return without clearing anything.
        Array.Clear(aura.Cells);

        startAngle %= 360;
        if (sectorSize > 360) { sectorSize = 360; }

        // Nudge off the octant boundaries. On a multiple of 45 the tangent is exactly 0 or exactly
        // 1, and the >= / > tests at the edges of the walkers then either double-count a ray or
        // drop one; backing the start off by a degree and growing the sector to compensate keeps
        // both ends strictly inside an octant.
        if (startAngle % 45 == 0)
        {
            startAngle += 359;
            sectorSize += 1;
            startAngle %= 360;
        }

        if ((startAngle + sectorSize) % 45 == 0)
        {
            sectorSize += 1;
        }

        foreach (var (x0, y0) in Centers(aura, world))
        {
            // Probably not yet placed on the combat map.
            if (x0 < 0 || y0 < 0) { continue; }

            int octant = startAngle / 45;
            int remainingAngle = sectorSize;
            int skipAngle = startAngle - (45 * octant);

            while (remainingAngle > 0)
            {
                Octant(aura, world, octant, minRadius, maxRadius, skipAngle, remainingAngle,
                       x0, y0);

                remainingAngle = remainingAngle - 45 + skipAngle;
                skipAngle = 0;
                octant = (octant + 1) & 7;
            }
        }
    }

    /// <summary>
    /// Where the aura radiates from (<c>LocateAuraCenters</c>, <c>Combatants.cpp:7823</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An unattached aura has no centres and therefore covers nothing.</b>
    /// <see cref="AuraAttachment.None"/> falls through the switch adding nothing to the list, so
    /// the whole walk below runs zero times. An aura that never called <c>$AURA_Attach</c> is
    /// invisible however large its radius.
    /// </para>
    /// <para>
    /// <b>A combatant-attached aura radiates from every cell of its perimeter.</b> Not its centre
    /// and not its whole footprint — the <c>if</c> keeps only cells on the outline, so a 1×1
    /// combatant gives one centre and a 3×3 gives eight. Each is walked independently and the
    /// results are unioned, which is what lets a big monster's aura reach round a corner its
    /// centre cannot see.
    /// </para>
    /// <para>
    /// <b>An out-of-range combatant index yields no centres.</b> The reference assigns a (0,0)
    /// point and then <c>return</c>s without adding it — so the assignment is dead and the aura
    /// covers nothing, rather than covering the top-left corner. It guards only the upper bound;
    /// a negative index runs off the front of the array there and is refused here.
    /// </para>
    /// </remarks>
    public static IEnumerable<(int X, int Y)> Centers(Aura aura, IAuraWorld world)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(world);

        int index = aura.Current.CombatantIndex;

        switch (aura.Current.Attachment)
        {
            case AuraAttachment.Combatant:
            case AuraAttachment.CombatantFacing:
                if (index < 0 || index >= world.CombatantCount)
                {
                    yield break;
                }

                var (x, y, _) = world.Combatant(index);
                var (width, height) = world.CombatantFootprint(index);

                for (int w = 0; w < width; w++)
                {
                    for (int h = 0; h < height; h++)
                    {
                        if (w == 0 || w == width - 1 || h == 0 || h == height - 1)
                        {
                            yield return (x + w, y + h);
                        }
                    }
                }

                break;

            case AuraAttachment.Xy:
                yield return (aura.Current.X, aura.Current.Y);
                break;

            case AuraAttachment.None:
            default:
                break;
        }
    }

    /// <summary>
    /// One of the eight 45° wedges, each with its own tangent bounds and its own walk axis.
    /// </summary>
    /// <remarks>
    /// <b>The epsilons are not uniform and are transcribed as written.</b> Four octants floor their
    /// minimum tangent at <c>0</c>, <c>0.0</c>, <c>0.000000</c> and <c>0.000001</c> respectively,
    /// and four cap their maximum at <c>1.0</c> where the others use <c>0.999999</c>. The
    /// differences decide which ray lands on a shared octant boundary, so rounding them to one
    /// value would move cells.
    /// </remarks>
    private static void Octant(Aura aura, IAuraWorld world, int octant,
                               int minRadius, int maxRadius, int skipAngle, int remainingAngle,
                               int x0, int y0)
    {
        double minTangent;
        double maxTangent;

        switch (octant)
        {
            case 0:
                minTangent = skipAngle > 0 ? Tan(skipAngle) : 0;
                maxTangent = remainingAngle < 45 ? Tan(remainingAngle) : 1.0;
                WalkX(aura, world, minRadius, maxRadius, minTangent, maxTangent, x0, y0, 1, -1);
                break;

            case 1:
                maxTangent = skipAngle > 0 ? Tan(45 - skipAngle) : 0.999999;
                minTangent = skipAngle + remainingAngle < 45
                    ? Tan(45 - skipAngle - remainingAngle) : 0.0;
                WalkY(aura, world, minRadius, maxRadius, minTangent, maxTangent, x0, y0, 1, -1);
                break;

            case 2:
                minTangent = skipAngle > 0 ? Tan(skipAngle) : 0.000001;
                maxTangent = skipAngle + remainingAngle < 45
                    ? Tan(45 - skipAngle - remainingAngle) : 0.999999;
                WalkY(aura, world, minRadius, maxRadius, minTangent, maxTangent, x0, y0, -1, -1);
                break;

            case 3:
                maxTangent = skipAngle > 0 ? Tan(45 - skipAngle) : 1.0;
                minTangent = skipAngle + remainingAngle < 45
                    ? Tan(45 - skipAngle - remainingAngle) : 0.000001;
                WalkX(aura, world, minRadius, maxRadius, minTangent, maxTangent, x0, y0, -1, -1);
                break;

            case 4:
                minTangent = skipAngle > 0 ? Tan(skipAngle) : 0;
                maxTangent = remainingAngle < 45 ? Tan(remainingAngle) : 1.0;
                WalkX(aura, world, minRadius, maxRadius, minTangent, maxTangent, x0, y0, -1, 1);
                break;

            case 5:
                maxTangent = skipAngle > 0 ? Tan(45 - skipAngle) : 0.999999;
                minTangent = skipAngle + remainingAngle < 45
                    ? Tan(45 - skipAngle - remainingAngle) : 0.000000;
                WalkY(aura, world, minRadius, maxRadius, minTangent, maxTangent, x0, y0, -1, 1);
                break;

            case 6:
                minTangent = skipAngle > 0 ? Tan(skipAngle) : 0.000001;
                maxTangent = skipAngle + remainingAngle < 45
                    ? Tan(45 - skipAngle - remainingAngle) : 0.999999;
                WalkY(aura, world, minRadius, maxRadius, minTangent, maxTangent, x0, y0, 1, 1);
                break;

            default:
                maxTangent = skipAngle > 0 ? Tan(45 - skipAngle) : 1.0;
                minTangent = skipAngle + remainingAngle < 45
                    ? Tan(45 - skipAngle - remainingAngle) : 0.000001;
                WalkX(aura, world, minRadius, maxRadius, minTangent, maxTangent, x0, y0, 1, 1);
                break;
        }
    }

    private static double Tan(int degrees) => Math.Tan(degrees * DegreesToRadians);

    /// <summary>
    /// The four octants whose rays are stepped along X (<c>AnnularOctantX</c>,
    /// <c>Combatants.cpp:7871</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>y</c> is never reset between values of <c>x</c>.</b> The outer loop counts <c>x</c>
    /// down from <c>maxRadius</c> while the inner one counts <c>y</c> up, and neither resets the
    /// other — so this is one continuous sweep around the arc, not a scan per column. Resetting
    /// <c>y</c> to 0 each time, which is what the shape of the loops suggests, would walk the whole
    /// quarter-disc instead of its rim.
    /// </para>
    /// <para>
    /// <b>Each step can advance the cross-axis twice.</b> The Bresenham accumulator is tested once
    /// inside the wavelength branch and again after it, so a steep ray moves two rows in one
    /// column. That is what pairs the two marked cells per step rather than leaving gaps.
    /// </para>
    /// <para>
    /// <b>Off-map squares are marked, not skipped, in the reference.</b> Only
    /// <see cref="AuraObstacle.Wall"/> stops a ray, and <see cref="AuraObstacle.OffMap"/> is not a
    /// wall — so the mark that follows indexes the cell array with a coordinate outside the map and
    /// writes past the end of it. Guarded here; a C# array would throw where the original quietly
    /// corrupted the heap.
    /// </para>
    /// </remarks>
    private static void WalkX(Aura aura, IAuraWorld world, int minRadius, int maxRadius,
                              double minTangent, double maxTangent, int x0, int y0,
                              int dirX, int dirY)
    {
        // minRadius is squared into a local the reference never reads again. Kept as a statement
        // of what the parameter is for, and as the reason an annulus has no hole.
        _ = minRadius * minRadius;
        int maxD2 = maxRadius * maxRadius;

        int x = maxRadius;
        int y = 0;
        int dsy = 0;

        while (x > 0)
        {
            int dxy2 = (x * x) + (y * y);
            int msy = 2 * x;

            while (dxy2 <= maxD2)
            {
                double tanyx = Math.Abs((double)y / x);
                if (tanyx > maxTangent) { break; }

                if (tanyx >= minTangent)
                {
                    int sy = dsy;
                    int dx = 0;
                    int dy = 0;
                    bool stop = false;
                    int steps = Math.Abs(x);

                    for (int i = 0; i < steps; i++)
                    {
                        var ot1 = world.Obstacle(x0 + dx, y0 + dy);

                        switch (aura.Current.Wavelength)
                        {
                            case AuraWavelength.Visible:
                                if (ot1 == AuraObstacle.Wall) { stop = true; break; }

                                Mark(aura, world, x0 + dx, y0 + dy);

                                // A combatant standing on the centre does not shadow their own
                                // aura -- the dx != 0 guard -- but one further out does.
                                if (ot1 == AuraObstacle.Occupied && dx != 0) { stop = true; break; }

                                if (sy >= msy) { sy -= msy; dy += dirY; }

                                if (world.Obstacle(x0 + dx + dirX, y0 + dy) is var ot2
                                    && ot2 == AuraObstacle.Wall)
                                {
                                    stop = true;
                                    break;
                                }

                                Mark(aura, world, x0 + dx + dirX, y0 + dy);

                                // And no dx guard on this one, unlike the cell before it.
                                if (ot2 == AuraObstacle.Occupied) { stop = true; }
                                break;

                            case AuraWavelength.Xray:
                                if (ot1 == AuraObstacle.Wall) { stop = true; break; }

                                Mark(aura, world, x0 + dx, y0 + dy);

                                if (sy >= msy) { sy -= msy; dy += dirY; }

                                if (world.Obstacle(x0 + dx + dirX, y0 + dy) == AuraObstacle.Wall)
                                {
                                    // Stops, but does NOT break out of the switch first, so this
                                    // arm always reaches the end. Same effect, different shape
                                    // from the visible one above.
                                    stop = true;
                                }
                                else
                                {
                                    Mark(aura, world, x0 + dx + dirX, y0 + dy);
                                }
                                break;

                            default:
                                // Neutrino: nothing stops it, not even a wall.
                                Mark(aura, world, x0 + dx, y0 + dy);
                                if (sy >= msy) { sy -= msy; dy += dirY; }
                                Mark(aura, world, x0 + dx + dirX, y0 + dy);
                                break;
                        }

                        if (stop) { break; }

                        sy += dsy;
                        if (sy >= msy) { sy -= msy; dy += dirY; }
                        dx += dirX;
                    }
                }

                dxy2 += dsy + 1;
                y++;
                dsy = 2 * Math.Abs(y);
            }

            x--;
        }
    }

    /// <summary>
    /// The four octants whose rays are stepped along Y (<c>AnnularOctantY</c>,
    /// <c>Combatants.cpp:8025</c>) — <see cref="WalkX"/> with the axes exchanged.
    /// </summary>
    /// <remarks>
    /// <b>It is a true mirror, down to the asymmetries.</b> The occupied-square guard is
    /// <c>dy != 0</c> here where the other reads <c>dx != 0</c>, and the second marked cell is the
    /// next <i>row</i> rather than the next column. Everything else, including the double
    /// cross-axis step and the X-ray arm's missing <c>break</c>, is identical.
    /// </remarks>
    private static void WalkY(Aura aura, IAuraWorld world, int minRadius, int maxRadius,
                              double minTangent, double maxTangent, int x0, int y0,
                              int dirX, int dirY)
    {
        _ = minRadius * minRadius;
        int maxD2 = maxRadius * maxRadius;

        int y = maxRadius;
        int x = 0;
        int dsx = 0;

        while (y > 0)
        {
            int dyx2 = (y * y) + (x * x);
            int msx = 2 * y;

            while (dyx2 <= maxD2)
            {
                double tanxy = Math.Abs((double)x / y);
                if (tanxy > maxTangent) { break; }

                if (tanxy >= minTangent)
                {
                    int sx = dsx;
                    int dx = 0;
                    int dy = 0;
                    bool stop = false;
                    int steps = Math.Abs(y);

                    for (int i = 0; i < steps; i++)
                    {
                        var ot1 = world.Obstacle(x0 + dx, y0 + dy);

                        switch (aura.Current.Wavelength)
                        {
                            case AuraWavelength.Visible:
                                if (ot1 == AuraObstacle.Wall) { stop = true; break; }

                                Mark(aura, world, x0 + dx, y0 + dy);

                                if (ot1 == AuraObstacle.Occupied && dy != 0) { stop = true; break; }

                                if (sx >= msx) { sx -= msx; dx += dirX; }

                                if (world.Obstacle(x0 + dx, y0 + dy + dirY) is var ot2
                                    && ot2 == AuraObstacle.Wall)
                                {
                                    stop = true;
                                    break;
                                }

                                Mark(aura, world, x0 + dx, y0 + dy + dirY);

                                if (ot2 == AuraObstacle.Occupied) { stop = true; }
                                break;

                            case AuraWavelength.Xray:
                                if (ot1 == AuraObstacle.Wall) { stop = true; break; }

                                Mark(aura, world, x0 + dx, y0 + dy);

                                if (sx >= msx) { sx -= msx; dx += dirX; }

                                if (world.Obstacle(x0 + dx, y0 + dy + dirY) == AuraObstacle.Wall)
                                {
                                    stop = true;
                                }
                                else
                                {
                                    Mark(aura, world, x0 + dx, y0 + dy + dirY);
                                }
                                break;

                            default:
                                Mark(aura, world, x0 + dx, y0 + dy);
                                if (sx >= msx) { sx -= msx; dx += dirX; }
                                Mark(aura, world, x0 + dx, y0 + dy + dirY);
                                break;
                        }

                        if (stop) { break; }

                        sx += dsx;
                        if (sx >= msx) { sx -= msx; dx += dirX; }
                        dy += dirY;
                    }
                }

                dyx2 += dsx + 1;
                x++;
                dsx = 2 * Math.Abs(x);
            }

            y--;
        }
    }

    /// <summary>
    /// Sets bit 0 of a square's mask byte, if the square is on the map.
    /// </summary>
    /// <remarks>
    /// <b>The bounds test is the port's, not the reference's.</b> See <see cref="WalkX"/>: an
    /// off-map square is not a wall, so the original writes outside the cell array. Dropping the
    /// mark is the only defensible reading — the cell it wanted does not exist.
    /// </remarks>
    private static void Mark(Aura aura, IAuraWorld world, int x, int y)
    {
        if (x < 0 || y < 0 || x >= world.MapWidth || y >= world.MapHeight)
        {
            return;
        }

        int index = (y * world.MapWidth) + x;

        if (index < aura.Cells.Length)
        {
            aura.Cells[index] |= 1;
        }
    }
}
