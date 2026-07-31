using UAF.Serialization;

namespace UAFcore;

/// <summary>Which of a wall set's three pictures a slot wants.</summary>
public enum WallLayer
{
    Wall,
    Door,
    Overlay,
}

/// <summary>
/// Resolves a viewport slot to the wall-set entry whose art should be drawn there.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>getWallSurface</c> / <c>getDoorSurface</c> / <c>getOverlaySurface</c>
/// (<c>Shared/Viewport.cpp:1110-1240</c>), which differ only in which picture they pull off the
/// resolved wall set. Every square-rendering routine calls these before it can draw anything, so
/// this is the layer between the geometry and the blitter.
/// </para>
/// <para>
/// <b>Wall index 0 means "no wall", not "wall set 0".</b> The original returns -1 for it
/// (<c>Viewport.cpp:1150</c>) and every caller then draws nothing. Treating index 0 as a valid
/// entry would paper the whole level in whatever art happened to sit in the first slot.
/// </para>
/// <para>
/// <b>An out-of-range index is clamped, not rejected.</b> Anything at or above
/// <c>MAX_WALLSETS</c> (192, <c>Externs.h:863</c>) is logged as "Bogus wall slot num" and reset to
/// 0 — so it degrades to "no wall" rather than failing the frame. Reproduced, because designs in
/// the wild evidently trip it often enough to warrant the engine's own de-duplicated warning.
/// </para>
/// </remarks>
public sealed class WallResolver(Map map, IReadOnlyList<WallSetSlot> wallSets)
{
    /// <summary><c>MAX_WALLSETS</c> (<c>Externs.h:863</c>).</summary>
    public const int MaxWallSets = 192;

    /// <summary>The index that means nothing is drawn.</summary>
    public const int NoWall = 0;

    private readonly Map map = map ?? throw new ArgumentNullException(nameof(map));

    private readonly IReadOnlyList<WallSetSlot> wallSets =
        wallSets ?? throw new ArgumentNullException(nameof(wallSets));

    /// <summary>Indices that were out of range, for diagnostics rather than control flow.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// The wall-set index a slot resolves to, or <see cref="NoWall"/> when nothing is drawn.
    /// </summary>
    /// <remarks>
    /// A slot whose cell lies outside the map resolves to nothing. That is only reachable for
    /// slots 13 and 14, which <see cref="ViewMap"/> leaves unwrapped precisely so this question
    /// has an answer — the occlusion tests depend on it.
    /// </remarks>
    public int IndexAt(ViewMap view, int slot, Facing facing)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (slot < 0 || slot >= ViewMap.TotalSlots)
        {
            return NoWall;
        }

        var (x, y) = view[slot];
        var cell = map.At(x, y);
        if (cell is null)
        {
            return NoWall;
        }

        int index = (int)facing < cell.Walls.Length ? cell.Walls[(int)facing] : NoWall;

        // The 5.x per-cell override tables (WALL_OVERRIDE_INDEX 6, DOOR_OVERRIDE_INDEX 7 --
        // GlobalData.h:479) would be consulted here and would win. They are not ported: they live
        // in the LEVEL_STATS cell-content tables that UAF.Serialization refuses at
        // _CELL_CONTENTS_VERSION. A design using them will draw its unoverridden walls, which is
        // wrong but bounded, and no design read so far reaches that version.

        if (index >= MaxWallSets)
        {
            Warnings.Add($"wall index {index} at ({x},{y}) facing {facing} exceeds {MaxWallSets}");
            return NoWall;
        }

        return index;
    }

    /// <summary>
    /// The art filename for a slot, or null when nothing is drawn there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The index addresses the table directly.</b> An earlier revision subtracted one, reasoning
    /// that since 0 is the "none" sentinel the real entries must start at 1 — plausible, and wrong.
    /// The C++ does <c>WallSets[wallSlot]</c> with no adjustment, guarded only by an early return
    /// for 0 (<c>Viewport.cpp:1150</c>), because the table <i>is</i> the full 192-entry
    /// <c>MAX_WALLSETS</c> array with slot 0 present and unused. A design's level really does carry
    /// all 192, most of them blank.
    /// </para>
    /// <para>
    /// The off-by-one survived its first test because entries 1, 2 and 3 of the design used for it
    /// all name the same file — so every reference still resolved to art that exists. It took
    /// printing the table to see it.
    /// </para>
    /// </remarks>
    public string? ArtFor(ViewMap view, int slot, Facing facing, WallLayer layer)
    {
        int index = IndexAt(view, slot, facing);
        if (index == NoWall)
        {
            return null;
        }

        if (index >= wallSets.Count)
        {
            Warnings.Add($"wall index {index} has no wall set (design declares {wallSets.Count})");
            return null;
        }

        var set = wallSets[index];
        string file = layer switch
        {
            WallLayer.Door => set.DoorFile,
            WallLayer.Overlay => set.OverlayFile,
            _ => set.WallFile,
        };

        return string.IsNullOrWhiteSpace(file) ? null : file;
    }

    /// <summary>
    /// Whether this cell's wall set wants its door drawn before its overlay.
    /// </summary>
    /// <remarks>
    /// <c>RenderDoorBeforeOverlay</c> (<c>Viewport.cpp:1293</c>). Note it does <b>not</b> guard
    /// index 0 the way the surface lookups do — it reads <c>WallSets[0].doorFirst</c> quite
    /// happily. Harmless, since slot 0 is the unused entry and its flag is clear, but reproduced
    /// rather than tidied: a design that put something in slot 0 would behave the same way here as
    /// in the original.
    /// </remarks>
    public bool DoorFirst(ViewMap view, int slot, Facing facing)
    {
        int index = IndexAt(view, slot, facing);
        return index < wallSets.Count && wallSets[index].DoorFirst != 0;
    }

    /// <summary>Whether a slot has a wall at all — the occlusion tests' question.</summary>
    public bool HasWall(ViewMap view, int slot, Facing facing) =>
        IndexAt(view, slot, facing) != NoWall;

    /// <summary>
    /// Whether the cell a slot names exists on the map.
    /// </summary>
    /// <remarks>
    /// <c>validCoords</c> (<c>Viewport.cpp:1117</c>). Only ever false for slots 13 and 14, and the
    /// square routines combine it with <see cref="HasWall"/> as "there is a wall there, or there is
    /// no there there" — both of which mean the far edge does not need drawing through.
    /// </remarks>
    public bool CellExists(ViewMap view, int slot)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (slot < 0 || slot >= ViewMap.TotalSlots)
        {
            return false;
        }

        var (x, y) = view[slot];
        return map.Contains(x, y);
    }
}
