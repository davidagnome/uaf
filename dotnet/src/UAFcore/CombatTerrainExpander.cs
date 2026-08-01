namespace UAFcore;

/// <summary>
/// A wall square's shape, from how many of its four neighbours are also walls
/// (the <c>*_WALL</c> defines at <c>Drawtile.cpp:126</c>).
/// </summary>
/// <remarks>
/// The names describe the drawn tile, not the neighbours: a square whose only wall neighbour is to
/// the north is the <i>bottom</i> terminator of a vertical run, because the run comes down from
/// above and stops there.
/// </remarks>
public enum TerrainWallType
{
    None = 0,
    VerticalTopTerminator = 1,
    Vertical = 2,
    VerticalBottomTerminator = 3,
    HorizontalLeftTerminator = 4,
    Horizontal = 5,
    HorizontalRightTerminator = 6,
    UpperLeftCorner = 7,
    TopT = 8,
    UpperRightCorner = 9,
    LowerLeftCorner = 10,
    BottomT = 11,
    LowerRightCorner = 12,
    Intersection = 13,
    LeftT = 14,
    RightT = 15,
}

/// <summary>
/// Pass 3 — samples the junction grid into the finished combat map
/// (<c>ConvertTempMapToCombatTerrain</c>, <c>Drawtile.cpp:1981</c>).
/// </summary>
/// <remarks>
/// <para>
/// Each junction type expands to between one and five terrain tiles, placed relative to the target
/// square. The tile numbers are indices into <see cref="CombatTiles.Dungeon"/> and are not
/// derivable from anything — they are which frame of the terrain art sheet to draw — so this is a
/// transcription and is meant to read like one.
/// </para>
/// <para>
/// <b>Several cases inspect neighbours in the junction grid, not the output.</b>
/// <see cref="TerrainWallType.Vertical"/> and <see cref="TerrainWallType.Horizontal"/> bail out
/// entirely when the junction up-and-left of them is a corner or a T, because that neighbour has
/// already drawn this square as part of its own expansion. Dropping those guards double-draws and
/// thickens every wall.
/// </para>
/// <para>
/// The wilderness junction types (<c>DBL_HIGH_*</c>, <c>SGL_HIGH_*</c>, 100–134) are not handled:
/// only <c>GenerateOutdoorCombatMap</c> produces them and it is not ported. They fall through to
/// the default, which is what the original does with an unrecognised type.
/// </para>
/// </remarks>
public static class CombatTerrainExpander
{
    /// <summary>
    /// Expands a window of the junction grid, centred on <paramref name="startCol"/>,
    /// <paramref name="startRow"/>, into <paramref name="combat"/>.
    /// </summary>
    public static void Expand(CombatMap combat, TerrainWallType[] junctions, int junctionStride,
                              int startCol, int startRow)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(junctions);

        TerrainWallType At(int x, int y)
        {
            int index = (y * junctionStride) + x;
            return index >= 0 && index < junctions.Length && x >= 0 && x < junctionStride
                ? junctions[index]
                : TerrainWallType.None;
        }

        int sy = startRow - (combat.Height / 2);
        for (int ty = 0; ty < combat.Height; ty++, sy++)
        {
            int sx = startCol - (combat.Width / 2);
            for (int tx = 0; tx < combat.Width; tx++, sx++)
            {
                switch (At(sx, sy))
                {
                    case TerrainWallType.None:
                        break;

                    case TerrainWallType.VerticalTopTerminator:
                        combat.SetTile(tx + 1, ty, 1);
                        combat.SetTile(tx + 1, ty + 1, 2);
                        combat.SetTile(tx + 1, ty + 2, 5);
                        break;

                    case TerrainWallType.Vertical:
                        // Already drawn by the neighbour up-and-left.
                        if (At(sx - 1, sy - 1) is TerrainWallType.LowerRightCorner
                                                or TerrainWallType.BottomT
                                                or TerrainWallType.RightT
                                                or TerrainWallType.VerticalTopTerminator)
                        {
                            break;
                        }

                        if (combat.IsEmpty(tx, ty - 1))
                        {
                            combat.SetTile(tx, ty - 1, 14);
                        }
                        else if (At(sx - 1, sy - 1) == TerrainWallType.Intersection)
                        {
                            combat.SetTile(tx, ty - 1, 3);
                            combat.SetTile(tx, ty, 4);
                            combat.SetTile(tx, ty + 1, 5);
                            break;
                        }
                        else if (At(sx, sy - 2) is TerrainWallType.HorizontalRightTerminator
                                                 or TerrainWallType.UpperLeftCorner)
                        {
                            combat.SetTile(tx, ty - 1, 22);
                        }
                        else
                        {
                            combat.SetTile(tx, ty - 1, 7);
                        }

                        combat.SetTile(tx, ty, 4);
                        combat.SetTile(tx, ty + 1, 5);
                        break;

                    case TerrainWallType.VerticalBottomTerminator:
                        combat.SetTile(tx, ty - 1, combat.IsEmpty(tx, ty - 1) ? 14 : 7);
                        combat.SetTile(tx, ty, 8);
                        combat.SetTile(tx, ty + 1, 9);
                        break;

                    case TerrainWallType.Intersection:
                        combat.SetTile(tx, ty - 1, combat.IsEmpty(tx, ty - 1) ? 14 : 7);
                        combat.SetTile(tx, ty, 10);
                        combat.SetTile(tx, ty + 1, 15);
                        break;

                    case TerrainWallType.HorizontalLeftTerminator:
                        combat.SetTile(tx, ty, 16);
                        combat.SetTile(tx, ty + 1, 17);
                        break;

                    case TerrainWallType.Horizontal:
                        if (At(sx - 1, sy) is TerrainWallType.LowerRightCorner
                                            or TerrainWallType.BottomT
                                            or TerrainWallType.RightT)
                        {
                            break;
                        }

                        combat.SetTile(tx, ty, 6);
                        combat.SetTile(tx, ty + 1, 11);
                        break;

                    case TerrainWallType.HorizontalRightTerminator:
                        combat.SetTile(tx, ty, 18);
                        combat.SetTile(tx, ty + 1, 24);
                        break;

                    case TerrainWallType.UpperLeftCorner:
                        combat.SetTile(tx, ty - 1, combat.IsEmpty(tx, ty - 1) ? 14 : 7);
                        combat.SetTile(tx, ty, 20);
                        combat.SetTile(tx, ty + 1, 24);
                        break;

                    case TerrainWallType.TopT:
                        combat.SetTile(tx, ty - 1, combat.IsEmpty(tx, ty - 1) ? 14 : 7);
                        combat.SetTile(tx, ty, 6);
                        combat.SetTile(tx, ty + 1, 11);
                        break;

                    case TerrainWallType.UpperRightCorner:
                        combat.SetTile(tx, ty - 1, combat.IsEmpty(tx, ty - 1) ? 14 : 7);
                        combat.SetTile(tx, ty, 12);
                        combat.SetTile(tx, ty + 1, 13);
                        break;

                    case TerrainWallType.LeftT:
                        combat.SetTile(tx, ty - 1, combat.IsEmpty(tx, ty - 1) ? 14 : 7);
                        combat.SetTile(tx, ty, 10);
                        combat.SetTile(tx, ty + 1, 15);
                        break;

                    case TerrainWallType.RightT:
                        // Unconditional 14 here, where its siblings test IsEmpty first. The
                        // conditional is present but commented out in the original
                        // (Drawtile.cpp:2132); this follows the live code.
                        combat.SetTile(tx, ty - 1, 14);
                        combat.SetTile(tx, ty, 4);
                        combat.SetTile(tx, ty + 1, 5);
                        combat.SetTile(tx + 1, ty, 3);
                        combat.SetTile(tx + 1, ty + 1, 4);
                        combat.SetTile(tx + 1, ty + 2, 5);
                        break;

                    case TerrainWallType.LowerLeftCorner:
                        combat.SetTile(tx, ty, 10);
                        combat.SetTile(tx, ty + 1, 15);
                        break;

                    case TerrainWallType.BottomT:
                        combat.SetTile(tx, ty, 10);
                        combat.SetTile(tx, ty + 1, 15);
                        combat.SetTile(tx + 1, ty, 3);
                        combat.SetTile(tx + 1, ty + 1, 4);
                        combat.SetTile(tx + 1, ty + 2, 5);
                        break;

                    case TerrainWallType.LowerRightCorner:
                        combat.SetTile(tx + 1, ty, 19);
                        combat.SetTile(tx + 1, ty + 1, 2);
                        combat.SetTile(tx + 1, ty + 2, 5);
                        break;

                    default:
                        break;
                }
            }
        }
    }
}
