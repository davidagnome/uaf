using UAF.Data;
using UAF.Media;

namespace UAFcore;

/// <summary>Which way the party faces. Masked with <c>&amp;3</c> by the original.</summary>
/// <remarks>
/// Four cardinal directions, not eight. The C++ never names an enum for this, but
/// <c>GlobalData.cpp:2359</c> rejects <c>facing &gt; 3</c> and <c>Viewport.cpp:3680</c> computes
/// the right-hand direction as <c>(facing + 1) &amp; 3</c>, which fixes both the range and the
/// rotation order.
/// </remarks>
public enum Facing
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
}

/// <summary>
/// The engine's state machine and renderer, with no knowledge of SDL, windows or timers.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is driven by <see cref="Update"/> and observed through <see cref="Render"/>, so
/// the whole engine can be exercised from a test with a recorded input source and a headless
/// presenter. That is the point of the split: the C++ engine's equivalent logic is entangled with
/// a live DirectX device, which is why it has no automated tests at all.
/// </para>
/// <para>
/// <b>Scope.</b> This walks a party around a level and draws the screen. It does not run events,
/// combat, or the 3D wall projection — the viewport shows the level's backdrop rather than a
/// rendered corridor. Those are Phase 4 proper; this is the beachhead that makes the rest
/// testable.
/// </para>
/// </remarks>
public sealed class Game
{
    private readonly LoadedDesign design;
    private readonly Surface screen;

    public Game(LoadedDesign design, int width = 640, int height = 480, int levelIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(design);

        this.design = design;
        screen = new Surface(width, height);

        // The full level gives the wall sets, which sit after the event list; the map-only read is
        // the fallback for a level whose events cannot all be decoded, since movement needs the
        // grid and nothing else.
        var level = design.Level(levelIndex);
        Map = level is not null
            ? new Map(level.Width, level.Height, level.Cells)
            : design.Map(levelIndex);

        if (level is not null && Map is not null)
        {
            resolver = new WallResolver(Map, level.WallSets);
            wallFormats = WallFormatReader.ReadAll(design.Config);
        }

        // The engine's own defaults, from GLOBAL_STATS. A design says where a new party starts.
        X = design.Globals.StartX;
        Y = design.Globals.StartY;
        Facing = (Facing)(design.Globals.StartFacing & 3);
        Minutes = design.Globals.StartTime;
    }

    private readonly WallResolver? resolver;
    private readonly IReadOnlyList<WallFormat> wallFormats = [];

    /// <summary>The current level's grid, or null when it could not be read.</summary>
    public Map? Map { get; }

    /// <summary>Resolves viewport slots to wall art, when the level's wall sets were readable.</summary>
    public WallResolver? Walls => resolver;

    public int X { get; private set; }

    public int Y { get; private set; }

    public Facing Facing { get; private set; }

    /// <summary>Game time in minutes, which <c>GLOBAL_STATS::startTime</c> seeds.</summary>
    public int Minutes { get; private set; }

    public int Steps { get; private set; }

    public bool Running { get; private set; } = true;

    /// <summary>The last message drawn in the text box.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>Handles one input event.</summary>
    /// <returns>True when the state changed and a redraw is warranted.</returns>
    public bool Update(InputEvent input)
    {
        if (input.Kind != InputEventKind.KeyDown)
        {
            return false;
        }

        switch (input.Key)
        {
            case VirtualKey.Escape:
                Running = false;
                return true;

            case VirtualKey.Left:
                Facing = (Facing)(((int)Facing + 3) & 3);
                Message = $"Turned to face {Facing}.";
                return true;

            case VirtualKey.Right:
                Facing = (Facing)(((int)Facing + 1) & 3);
                Message = $"Turned to face {Facing}.";
                return true;

            case VirtualKey.Up:
                return Step(forward: true);

            case VirtualKey.Down:
                return Step(forward: false);

            default:
                return false;
        }
    }

    /// <summary>
    /// Moves one cell, if the cell being left allows it in that direction.
    /// </summary>
    /// <remarks>
    /// Moving backwards checks the <i>opposite</i> face, not the facing one — the party walks
    /// backwards without turning, so it leaves through the wall behind it. Checking the facing
    /// direction instead would let a party reverse straight through a wall it is staring at.
    /// </remarks>
    private bool Step(bool forward)
    {
        (int dx, int dy) = Facing switch
        {
            Facing.North => (0, -1),
            Facing.East => (1, 0),
            Facing.South => (0, 1),
            _ => (-1, 0),
        };

        if (!forward)
        {
            (dx, dy) = (-dx, -dy);
        }

        var direction = forward ? Facing : (Facing)(((int)Facing + 2) & 3);
        int nextX = X + dx;
        int nextY = Y + dy;

        // With no level loaded there is nothing to collide with, so movement is only bounded.
        if (Map is null)
        {
            nextX = Math.Clamp(nextX, 0, 255);
            nextY = Math.Clamp(nextY, 0, 255);
            if (nextX == X && nextY == Y)
            {
                Message = "You cannot go that way.";
                return true;
            }
        }
        else if (!Map.CanLeave(X, Y, direction))
        {
            // The blockage type is named rather than reduced to "blocked", because a locked door
            // and a wall are the same answer today and will not be once the party has keys.
            var blockage = Map.Blockage(X, Y, direction);
            Message = blockage is BlockageType.Blocked
                ? "A wall blocks your way."
                : $"The way is {blockage}.";
            return true;
        }

        // A level is a torus, not a bounded grid: walking off the east edge arrives at the west
        // (Party.cpp:1735). An earlier revision of this method reported "the map ends here"
        // instead, which is a rule the original does not have -- only walls stop a party.
        if (Map is not null)
        {
            (nextX, nextY) = Map.Wrap(nextX, nextY);
        }

        X = nextX;
        Y = nextY;
        Steps++;

        // One minute per step is this port's placeholder; the original derives it from the
        // party's speed and the zone, which is rules work rather than engine plumbing.
        Minutes++;
        Message = $"Moved {(forward ? "forward" : "back")} to ({X}, {Y}).";
        return true;
    }

    /// <summary>Draws the current state and returns the framebuffer.</summary>
    public Surface Render()
    {
        screen.ClipRect = screen.Bounds;
        screen.Fill(0xFF000000);

        var config = design.Config;
        config.Rewind();

        var horizontal = design.Art("border_Horizontal.png");
        var vertical = design.Art("border_Vertical.png");

        if (horizontal is not null)
        {
            Blit(config, horizontal, "HORZ_BAR_LONG", "HORZ_BAR_TOP");
            Blit(config, horizontal, "HORZ_BAR_LONG_2", "HORZ_BAR_MIDDLE");
            Blit(config, horizontal, "HORZ_BAR_LONG_3", "HORZ_BAR_BOTTOM");
        }

        if (vertical is not null)
        {
            Blit(config, vertical, "VERT_BAR_LONG", "VERT_BAR_LEFT");
            Blit(config, vertical, "VERT_BAR_SHORT", "VERT_BAR_MIDDLE");
            Blit(config, vertical, "VERT_BAR_LONG", "VERT_BAR_RIGHT");
        }

        var frame = design.Art("border_Viewport.png");
        if (frame is not null && config.TryGetPoint("VIEWPORT_FRAME", out int fx, out int fy))
        {
            Blitter.BlitOpaque(screen, fx, fy, frame);
        }

        var backdrop = design.Art("backdrop_IndoorGreyStone.png", SurfaceKind.Background);
        if (config.TryGetRect("VIEWPORT_RECT", out int vx, out int vy, out int vr, out int vb))
        {
            if (backdrop is not null)
            {
                Blitter.BlitOpaque(screen, vx, vy, backdrop);
            }

            DrawWalls(vx, vy, new SurfaceRect(vx, vy, vr, vb));
        }

        DrawText(config);
        return screen;
    }

    /// <summary>
    /// Draws the corridor's walls into the viewport.
    /// </summary>
    /// <remarks>
    /// Square 0 and the four simple side squares so far; nine remain. The clip is the viewport
    /// rectangle, because a wall slot's own offsets can place it outside and the original relies on
    /// the viewport being a separate, smaller surface to cut that off.
    /// </remarks>
    private void DrawWalls(int viewportX, int viewportY, SurfaceRect viewport)
    {
        if (resolver is null || wallFormats.Count == 0 || Map is null)
        {
            return;
        }

        var view = Map.View(X, Y, Facing);
        var saved = screen.ClipRect;

        try
        {
            screen.ClipRect = viewport;

            // Far squares first. The original draws back to front and relies on the keyed blits to
            // let nearer walls cover further ones, so the order is load-bearing rather than
            // cosmetic.
            string? far = resolver.ArtFor(view, 0, Facing, WallLayer.Wall);
            if (far is not null && design.Art(far, SurfaceKind.Wall) is Surface farSheet)
            {
                // The format is chosen per sheet, by its dimensions -- a design can mix wall packs
                // with different layouts, and picking one format for the whole view would cut most
                // of them from the wrong rectangles.
                RendererFor(farSheet)?.RenderSquare0(screen, farSheet, view, resolver, Facing,
                                                      viewportX, viewportY);
            }

            // Far to near, so a nearer wall's keyed blit covers a further one.
            //
            // The renderer is chosen from *any* face that resolves, not from the front one. An
            // earlier revision picked the sheet from the front face and skipped the whole square
            // when it was empty -- which silently discarded square 9, whose front face is often
            // clear while its left and right walls are the corridor sides the player actually
            // sees. Every pass then re-resolves its own sheet inside RenderSquare, so a square
            // mixing two wall packs still cuts each from its own format.
            foreach (int square in ViewportRenderer.SquarePasses.Keys.OrderBy(s => s))
            {
                var sheet = FirstSheet(view, square);
                RendererFor(sheet)?.RenderSquare(screen, view, resolver, Facing, square,
                                                 viewportX, viewportY,
                                                 f => design.Art(f, SurfaceKind.Wall));
            }
        }
        finally
        {
            screen.ClipRect = saved;
        }
    }

    /// <summary>
    /// The first wall sheet any of a square's passes resolves to, or null when none do.
    /// </summary>
    /// <remarks>
    /// Used only to choose the format. A square draws nothing when every face is clear, but one
    /// clear face must not suppress the others.
    /// </remarks>
    private Surface? FirstSheet(ViewMap view, int square)
    {
        if (resolver is null || !ViewportRenderer.SquarePasses.TryGetValue(square, out var passes))
        {
            return null;
        }

        foreach (var pass in passes)
        {
            var face = pass.Direction switch
            {
                ViewportRenderer.PassDirection.Left => (Facing)(((int)Facing + 3) & 3),
                ViewportRenderer.PassDirection.Right => (Facing)(((int)Facing + 1) & 3),
                _ => Facing,
            };

            string? file = resolver.ArtFor(view, square, face, WallLayer.Wall);
            if (file is not null && design.Art(file, SurfaceKind.Wall) is Surface sheet)
            {
                return sheet;
            }
        }

        return null;
    }

    /// <summary>The renderer whose format matches a sheet's dimensions, or the default.</summary>
    private ViewportRenderer? RendererFor(Surface? sheet)
    {
        if (sheet is null)
        {
            return null;
        }

        var format = WallFormatReader.SelectFor(wallFormats, sheet.Width, sheet.Height);
        return format is null ? null : new ViewportRenderer(format);
    }

    private void Blit(DesignConfig config, Surface art, string sourceKey, string destinationKey)
    {
        if (!config.TryGetRect(sourceKey, out int l, out int t, out int r, out int b) ||
            !config.TryGetPoint(destinationKey, out int x, out int y))
        {
            return;
        }

        // The *_LONG keys are source rectangles into a sheet of stacked strips, not destinations.
        if (new SurfaceRect(l, t, r, b).TryClipTo(art.Bounds, out var source))
        {
            Blitter.BlitOpaque(screen, x, y, art, source);
        }
    }

    private void DrawText(DesignConfig config)
    {
        var font = design.Font(design.RequestedFontHeight);
        if (font is null)
        {
            return;
        }

        if (config.TryGetInts("PARTYNAMES", out int[] roster, 4))
        {
            int y = roster[3];
            font.Draw(screen, roster[2], y, design.Name, tint: 0xFFE8C86A);
            y += font.Atlas.MaxCharHeight + 2;

            font.Draw(screen, roster[2], y,
                      $"({X}, {Y}) facing {Facing}", tint: 0xFFF0E6D2);
            y += font.Atlas.MaxCharHeight;

            font.Draw(screen, roster[2], y,
                      $"Day {1 + (Minutes / 1440)}  {(Minutes / 60) % 24:00}:{Minutes % 60:00}",
                      tint: 0xFF9A9AB0);
            y += font.Atlas.MaxCharHeight;

            font.Draw(screen, roster[2], y, $"{Steps} steps", tint: 0xFF9A9AB0);
        }

        if (config.TryGetPoint("TEXTBOX", out int tx, out int ty) && Message.Length > 0)
        {
            font.Draw(screen, tx, ty, Message, tint: 0xFF60C060);
        }
    }
}
