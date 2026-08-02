using UAF.Media;

namespace UAFcore;

/// <summary>
/// Combat icon sheets: how big a combatant is, and which part of its sheet to draw
/// (<c>determineIconSize</c> and <c>LoadCombatIcon</c>, <c>Combatant.cpp:8579</c>, <c>:8492</c>).
/// </summary>
/// <remarks>
/// <para>
/// A combat icon sheet holds <b>poses laid out left to right</b>, each one the combatant's full
/// footprint wide. There are two poses per icon — ready and attacking — so a sheet 96 pixels wide
/// with two frames is a one-square monster, and one 768 wide is eight squares across.
/// </para>
/// <para>
/// <b>The footprint comes from the art, not from the monster record.</b> Nothing in
/// <c>MONSTER_DATA</c> says how many squares a monster occupies; the engine measures the loaded
/// sprite. That is why <see cref="EncounterBuilder"/> cannot size a monster on its own.
/// </para>
/// </remarks>
public static class CombatIcons
{
    /// <summary>Poses per icon — one ready, one attacking.</summary>
    /// <remarks>
    /// The reference divides by this and then by <c>NumFrames / 2</c>, so a sheet's frame count is
    /// always a multiple of two. Its comment says as much: "2 frame minimum (one of each pose,
    /// rdy/attack), frame count is multiple of 2".
    /// </remarks>
    public const int PosesPerIcon = 2;

    /// <summary>
    /// The squares a combatant occupies, measured off its sheet
    /// (<c>determineIconSize</c>, <c>Combatant.cpp:8579</c>).
    /// </summary>
    /// <param name="frames">The icon record's frame count.</param>
    /// <remarks>
    /// <para>
    /// Width is <c>(sheetWidth / 48) / 2 / (frames / 2)</c> and height is
    /// <c>sheetHeight / 48</c>, each flooring at one. <b>The two divisions are separate integer
    /// steps and collapsing them changes the answer</b> for sheets that are not exact multiples —
    /// <c>(w/2)/(n/2)</c> is not <c>w/n</c> once truncation is involved.
    /// </para>
    /// <para>
    /// There is <b>no upper clamp</b>. A comment in <c>Drawtile.cpp</c> says icons are at most 4×4,
    /// but nothing enforces it and real art exceeds it — `SomethingWild`'s Red Dragon measures
    /// 8×4 from a 768×192 sheet.
    /// </para>
    /// </remarks>
    public static CombatantIcon SizeOf(Surface sheet, int frames)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        int width = sheet.Width / CombatMap.TileWidth / PosesPerIcon;
        width /= Math.Max(1, frames / PosesPerIcon);

        int height = sheet.Height / CombatMap.TileHeight;

        return new CombatantIcon(Math.Max(1, width), Math.Max(1, height));
    }

    /// <summary>
    /// The rectangle a pose occupies on the sheet.
    /// </summary>
    /// <param name="iconIndex">
    /// Which frame pair to use, from one. Characters pick their own; monsters use the first.
    /// </param>
    /// <param name="attacking">The second pose of the pair.</param>
    /// <remarks>
    /// <b>The index resets to one when it would run off the sheet</b> — the reference checks
    /// <c>offset + width·48 &gt;= imageWidth − width</c> and rewinds (<c>:8615</c>). Note the
    /// right-hand side subtracts <c>width</c>, a square count, from a pixel count; it is off by
    /// almost a whole tile, and reproducing it keeps the same frames reachable.
    /// </remarks>
    public static SurfaceRect PoseRect(Surface sheet, CombatantIcon icon, int iconIndex = 1,
                                       bool attacking = false)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        int poseWidth = icon.Width * CombatMap.TileWidth;
        int offset = (Math.Max(1, iconIndex) - 1) * poseWidth * PosesPerIcon;

        if (offset + poseWidth >= sheet.Width - icon.Width)
        {
            offset = 0;
        }

        int left = offset + (attacking ? poseWidth : 0);
        return new SurfaceRect(left, 0, left + poseWidth,
                               icon.Height * CombatMap.TileHeight);
    }

    /// <summary>
    /// Loads a combatant's icon and measures it, or null when the design ships no usable art.
    /// </summary>
    /// <param name="fileName">The icon record's file name.</param>
    /// <param name="frames">The icon record's frame count.</param>
    /// <param name="art">Resolves a file name to a surface.</param>
    /// <remarks>
    /// The reference falls back to a default monster icon when the named one will not load, and
    /// records a <c>MissingMonsterCombatIcons</c> error when even that fails. There is no bundled
    /// default here, so a missing sheet yields null and the caller keeps a one-square footprint —
    /// which is the safe direction, since too small never refuses a placement that should have
    /// succeeded.
    /// </remarks>
    public static (Surface Sheet, CombatantIcon Icon)? Load(string fileName, int frames,
                                                            Func<string, Surface?> art)
    {
        ArgumentNullException.ThrowIfNull(art);

        if (string.IsNullOrEmpty(fileName) || art(fileName) is not { } sheet)
        {
            return null;
        }

        return (sheet, SizeOf(sheet, frames));
    }
}
