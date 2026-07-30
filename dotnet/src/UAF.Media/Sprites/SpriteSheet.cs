namespace UAF.Media;

/// <summary>
/// A surface holding animation frames in a grid, and the arithmetic that turns a frame number into
/// a source rectangle.
/// </summary>
/// <remarks>
/// <para>
/// Frames are laid out row-major with as many per row as the surface is wide, which is
/// <c>CDXSprite::Draw</c>'s rule: <c>TilesInWidth = surfaceWidth / frameWidth</c>, then
/// <c>srcX = (frame % TilesInWidth) * frameWidth</c> and
/// <c>srcY = (frame / TilesInWidth) * frameHeight</c> (<c>src/cdx/cdxsprite.cpp:256</c>).
/// </para>
/// <para>
/// Nothing in the art file records the layout — <c>PIC_DATA</c> carries only
/// <c>FrameWidth</c>/<c>FrameHeight</c>/<c>NumFrames</c> — so the grid is inferred from the
/// image's width. A sheet whose width is not a whole multiple of the frame width therefore has
/// unreachable pixels in its last column, exactly as in the original.
/// </para>
/// </remarks>
public sealed class SpriteSheet
{
    public SpriteSheet(Surface surface, int frameWidth, int frameHeight, int frameCount)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);

        Surface = surface;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        FrameCount = frameCount;
        FramesPerRow = Math.Max(1, surface.Width / frameWidth);
    }

    public Surface Surface { get; }

    public int FrameWidth { get; }

    public int FrameHeight { get; }

    public int FrameCount { get; }

    /// <summary>How many frames fit across the sheet — the divisor in the frame-to-rect mapping.</summary>
    public int FramesPerRow { get; }

    /// <summary>The source rectangle of one frame, numbered from 0.</summary>
    public SurfaceRect FrameRect(int frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame, FrameCount);

        int x = (frame % FramesPerRow) * FrameWidth;
        int y = (frame / FramesPerRow) * FrameHeight;
        return SurfaceRect.FromBounds(x, y, FrameWidth, FrameHeight);
    }
}
