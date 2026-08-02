using UAF.Media;

namespace UAFcore;

/// <summary>
/// Draws the combat map (<c>displayCombatWalls</c>, <c>Drawtile.cpp:3085</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every terrain square is one 48×48 tile cut from the zone's combat art sheet at the coordinates
/// the tile table gives (<see cref="CombatTiles"/>). There is no perspective and no slot geometry —
/// unlike the dungeon viewport, this is a flat top-down grid, so the renderer is a double loop and
/// a blit.
/// </para>
/// <para>
/// <b>The art comes from the zone, not the design.</b> Each zone names an indoor and an outdoor
/// combat sheet (<c>ZoneRecord.IndoorCombatArt</c>), and the engine picks between them on whether
/// the encounter is outdoors (<c>Dgngame.cpp:1126</c>).
/// </para>
/// </remarks>
public sealed class CombatRenderer
{
    /// <summary>
    /// Where the map is drawn from, inside the frame
    /// (<c>CombatScreenX</c> / <c>CombatScreenY</c>, <c>Drawtile.h:110</c>).
    /// </summary>
    /// <remarks>
    /// The reference makes these runtime variables because the frame art differs by screen
    /// resolution; the commented-out constants beside them show the values it shipped with.
    /// </remarks>
    public int OriginX { get; set; } = 14;

    /// <inheritdoc cref="OriginX"/>
    public int OriginY { get; set; } = 16;

    /// <summary>The top-left terrain square shown (<c>m_iStartTerrainX/Y</c>).</summary>
    public int ScrollX { get; set; }

    /// <inheritdoc cref="ScrollX"/>
    public int ScrollY { get; set; }

    /// <summary>Screen position of a terrain square (<c>TerrainToScreenCoordX/Y</c>).</summary>
    public (int X, int Y) ToScreen(int terrainX, int terrainY) =>
        (((terrainX - ScrollX) * CombatMap.TileWidth) + OriginX,
         ((terrainY - ScrollY) * CombatMap.TileHeight) + OriginY);

    /// <summary>The terrain square under a screen position, or null when outside the map.</summary>
    public (int X, int Y)? FromScreen(CombatMap map, int screenX, int screenY)
    {
        ArgumentNullException.ThrowIfNull(map);

        int tx = ScrollX + ((screenX - OriginX) / CombatMap.TileWidth);
        int ty = ScrollY + ((screenY - OriginY) / CombatMap.TileHeight);
        return map.Contains(tx, ty) ? (tx, ty) : null;
    }

    /// <summary>
    /// Scrolls so a square is visible, centring when it is not
    /// (<c>EnsureVisible</c>, <c>Drawtile.h:141</c>).
    /// </summary>
    /// <param name="tilesAcross">How many squares fit in the visible area.</param>
    public void EnsureVisible(CombatMap map, int terrainX, int terrainY,
                              int tilesAcross, int tilesDown)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (terrainX < ScrollX || terrainX >= ScrollX + tilesAcross
            || terrainY < ScrollY || terrainY >= ScrollY + tilesDown)
        {
            ScrollX = terrainX - (tilesAcross / 2);
            ScrollY = terrainY - (tilesDown / 2);
        }

        ScrollX = Math.Clamp(ScrollX, 0, Math.Max(0, map.Width - tilesAcross));
        ScrollY = Math.Clamp(ScrollY, 0, Math.Max(0, map.Height - tilesDown));
    }

    /// <summary>
    /// Draws the terrain into <paramref name="screen"/>.
    /// </summary>
    /// <param name="sheet">The zone's combat art.</param>
    /// <param name="area">
    /// Where on the screen the map may draw. The reference sets a clip rectangle for exactly this
    /// and restores it afterwards; squares are drawn whole and clipped by the blitter.
    /// </param>
    /// <remarks>
    /// <b>The reference draws two squares beyond the visible area on every side</b> — its loops run
    /// from <c>start - 2</c> to <c>start + tiles + 2</c> — so a partially-scrolled edge has
    /// something under it rather than a gap. Reproduced, and it is why the clip rectangle matters.
    /// </remarks>
    public void DrawTerrain(Surface screen, CombatMap map, Surface sheet, SurfaceRect area)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sheet);

        int tilesAcross = (area.Width / CombatMap.TileWidth) + 1;
        int tilesDown = (area.Height / CombatMap.TileHeight) + 1;
        var tiles = map.Tiles;

        for (int ty = ScrollY - 2; ty < ScrollY + tilesDown + 2; ty++)
        {
            for (int tx = ScrollX - 2; tx < ScrollX + tilesAcross + 2; tx++)
            {
                if (!map.Contains(tx, ty))
                {
                    continue;
                }

                int cell = map.CellAt(tx, ty);
                if (cell < 0 || cell >= tiles.Length)
                {
                    continue;
                }

                var tile = tiles[cell];
                var source = new SurfaceRect(tile.SourceX, tile.SourceY,
                                             tile.SourceX + CombatMap.TileWidth,
                                             tile.SourceY + CombatMap.TileHeight);
                if (!source.TryClipTo(sheet.Bounds, out var clipped))
                {
                    continue;
                }

                var (sx, sy) = ToScreen(tx, ty);
                BlitClipped(screen, sx, sy, sheet, clipped, area);
            }
        }
    }

    /// <summary>
    /// Draws each combatant's icon over the terrain.
    /// </summary>
    /// <param name="iconFor">
    /// The combatant's icon sheet and the rectangle to cut from it, or null to skip. Icons are
    /// per-monster art the design ships; supplying them is the caller's business.
    /// </param>
    /// <remarks>
    /// <b>The dying are drawn before the living</b>, which is why the grid keeps two occupancy
    /// layers (<c>TERRAIN_CELL</c>'s comment: "dying dude is drawn before regular dude"). A
    /// combatant standing on a corpse hides it rather than the other way round.
    /// </remarks>
    public void DrawCombatants(Surface screen, IEnumerable<Combatant> combatants,
                               Func<Combatant, (Surface Sheet, SurfaceRect Source)?> iconFor,
                               SurfaceRect area)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(iconFor);

        var all = combatants.ToList();

        foreach (var c in all.Where(c => !c.IsOnCombatMap()).Concat(
                          all.Where(c => c.IsOnCombatMap())))
        {
            if (c.X < 0 || c.Y < 0 || iconFor(c) is not { } art)
            {
                continue;
            }

            var (sx, sy) = ToScreen(c.X, c.Y);
            BlitClipped(screen, sx, sy, art.Sheet, art.Source, area);
        }
    }

    /// <summary>
    /// How opaque the cursor is (<c>aval</c>, <c>Combatants.cpp:4732</c>).
    /// </summary>
    /// <remarks>
    /// The reference's own comment beside the blit calls this "50%". It is 100 out of 255, which
    /// is nearer 39% — the comment is wrong and the value is what ships, so the value wins.
    /// </remarks>
    public const int CursorAlpha = 100;

    /// <summary>
    /// Draws the targeting cursor (<c>displayCursor</c>, <c>Combatants.cpp:4733</c>).
    /// </summary>
    /// <param name="cursor">The cursor art, one 48×48 frame.</param>
    /// <param name="over">
    /// The combatant under the cursor, or null. When given, <b>every square of its footprint is
    /// covered</b> rather than just the cursor's own — the reference's <c>coverFullIcon</c> path,
    /// which is how a player can see the whole of a large monster being targeted.
    /// </param>
    /// <remarks>
    /// <para>
    /// Alpha-blended, so the combatant underneath shows through. The reference redraws the
    /// occupant's sprite on top afterwards; here the caller draws combatants after the cursor to
    /// the same effect.
    /// </para>
    /// <para>
    /// <b>The cursor is dropped entirely unless it fits</b> — the reference tests the whole 48×48
    /// against the view's right and bottom edges and draws nothing when it would overhang, rather
    /// than clipping it.
    /// </para>
    /// </remarks>
    public void DrawCursor(Surface screen, Surface cursor, int terrainX, int terrainY,
                           SurfaceRect area, Combatant? over = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(cursor);

        var source = new SurfaceRect(0, 0, CombatMap.TileWidth, CombatMap.TileHeight);

        if (over is not null)
        {
            for (int dy = 0; dy < over.Icon.Height; dy++)
            {
                for (int dx = 0; dx < over.Icon.Width; dx++)
                {
                    Stamp(screen, cursor, source, over.X + dx, over.Y + dy, area);
                }
            }

            return;
        }

        Stamp(screen, cursor, source, terrainX, terrainY, area);
    }

    private void Stamp(Surface screen, Surface cursor, SurfaceRect source,
                       int terrainX, int terrainY, SurfaceRect area)
    {
        var (x, y) = ToScreen(terrainX, terrainY);

        // Whole-or-nothing, as the reference is: no clipping at the edges.
        if (x < area.Left || y < area.Top
            || x + source.Width > area.Right || y + source.Height > area.Bottom)
        {
            return;
        }

        Blitter.BlitTransparentAlpha(screen, x, y, cursor, CursorAlpha, source);
    }

    /// <summary>
    /// Blits a tile, dropping it when it falls entirely outside the map area.
    /// </summary>
    /// <remarks>
    /// Stands in for the reference's <c>SetClipRect</c>: rather than clipping the destination, the
    /// source rectangle is trimmed by however much the tile overhangs. Transparent, because combat
    /// art declares its key the way the rest of this engine's art does — by the top-left pixel.
    /// </remarks>
    private static void BlitClipped(Surface screen, int x, int y, Surface sheet,
                                    SurfaceRect source, SurfaceRect area)
    {
        int left = source.Left + Math.Max(0, area.Left - x);
        int top = source.Top + Math.Max(0, area.Top - y);
        int right = source.Right - Math.Max(0, (x + source.Width) - area.Right);
        int bottom = source.Bottom - Math.Max(0, (y + source.Height) - area.Bottom);

        if (left >= right || top >= bottom)
        {
            return;
        }

        Blitter.BlitTransparent(screen, Math.Max(x, area.Left), Math.Max(y, area.Top),
                                sheet, new SurfaceRect(left, top, right, bottom));
    }
}
