namespace UAF.Media;

/// <summary>
/// Plays a movie into the shared framebuffer — the replacement for <c>Movie</c>
/// (<c>Shared/Movie.cpp</c>) and <c>Graphics::PlayMovie</c>/<c>UpdateMovie</c>/<c>StopMovie</c>.
/// </summary>
/// <remarks>
/// <para>
/// All the behaviour is here and none of the decoding: timing, scaling into the destination
/// rectangle, and the skip-and-carry-on path live in managed code over an
/// <see cref="IVideoDecoderFactory"/>, so they are testable with a synthetic decoder and no FFmpeg.
/// That split is the point — a movie that plays at the wrong speed or in the wrong place is a bug in
/// this file, and finding it should not need a native library or a display.
/// </para>
/// <para>
/// <b>A movie that cannot play is not an error.</b> <see cref="Start"/> returns false and the engine
/// carries on, matching the plan's requirement that a design run on a build with no FFmpeg (section
/// 6.1). The original's <c>PlayMovie</c> also returned FALSE on failure and its callers already
/// continue.
/// </para>
/// <para>
/// Time is supplied by the caller, as with <see cref="AnimatedSprite"/>: the engine advances it from
/// its own clock and a test advances it by hand, so a whole cutscene can be stepped through
/// deterministically.
/// </para>
/// </remarks>
public sealed class MoviePlayer(IVideoDecoderFactory? factory = null) : IDisposable
{
    private readonly IVideoDecoderFactory factory = factory ?? NullVideoDecoderFactory.Instance;
    private IVideoDecoder? decoder;
    private VideoFrame? pending;
    private VideoFrame? shown;
    private long startedAtMs;

    /// <summary>Whether movies can play at all in this build — the factory's availability.</summary>
    public bool IsSupported => factory.IsAvailable;

    /// <summary>Why movies cannot play, when <see cref="IsSupported"/> is false.</summary>
    public string? UnsupportedReason => factory.UnavailableReason;

    public bool IsPlaying { get; private set; }

    /// <summary>Where the movie is drawn. Null means the whole destination surface.</summary>
    public SurfaceRect? DestinationRect { get; private set; }

    /// <summary>Frames actually drawn, for tests and for diagnosing a stalled cutscene.</summary>
    public int FramesPresented { get; private set; }

    /// <summary>Frames decoded but never drawn because playback ran behind.</summary>
    public int FramesDropped { get; private set; }

    /// <summary>Why the last <see cref="Start"/> failed, or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Opens a movie and begins playback at <paramref name="timestampMs"/>.
    /// </summary>
    /// <returns>False when the movie cannot be played, in which case the caller should skip it.</returns>
    public bool Start(string path, long timestampMs = 0, SurfaceRect? destination = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        Stop();
        LastError = null;

        if (!factory.IsAvailable)
        {
            LastError = factory.UnavailableReason ?? "video playback is unavailable";
            return false;
        }

        decoder = factory.Open(path);
        if (decoder is null)
        {
            LastError = $"could not open movie: {path}";
            return false;
        }

        DestinationRect = destination;
        startedAtMs = timestampMs;
        IsPlaying = true;
        FramesPresented = 0;
        FramesDropped = 0;
        shown = null;
        pending = null;
        return true;
    }

    /// <summary>
    /// Draws whichever frame is due at <paramref name="timestampMs"/>, and reports whether the movie
    /// is still running — <c>Graphics::UpdateMovie</c>.
    /// </summary>
    /// <remarks>
    /// Frames whose presentation time has already passed are dropped rather than shown late, so a
    /// slow machine loses frames instead of falling progressively behind the movie's audio. The one
    /// frame held back in <see cref="pending"/> is what lets this decide without decoding twice.
    /// </remarks>
    public bool Update(Surface destination, long timestampMs)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!IsPlaying || decoder is null)
        {
            return false;
        }

        var elapsed = TimeSpan.FromMilliseconds(timestampMs - startedAtMs);
        bool advancedThisUpdate = false;
        bool endOfStream = false;

        while (true)
        {
            if (pending is null)
            {
                // A decoder that reports success with no frame is treated as spent, rather than
                // trusted into a null dereference on the frame's timestamp.
                if (decoder.TryReadFrame(out var next) && next is not null)
                {
                    pending = next;
                }
                else
                {
                    endOfStream = true;
                    break;
                }
            }

            if (pending.Timestamp > elapsed)
            {
                break;
            }

            // A second advance inside one update means the frame selected a moment ago was already
            // overdue and is being discarded unseen. Only that counts as a drop -- the first advance
            // of an update is the normal case.
            if (advancedThisUpdate)
            {
                FramesDropped++;
            }

            shown = pending;
            pending = null;
            advancedThisUpdate = true;
        }

        // The last frame is drawn on the update that consumes it, and playback ends on the next one.
        // Stopping the moment the stream runs dry would drop the final frame of every movie.
        if (endOfStream && !advancedThisUpdate)
        {
            Stop();
            return false;
        }

        if (shown is null)
        {
            return true;
        }

        Draw(destination, shown);
        FramesPresented++;
        return true;
    }

    /// <summary><c>Graphics::StopMovie</c>.</summary>
    public void Stop()
    {
        decoder?.Dispose();
        decoder = null;
        IsPlaying = false;
        pending = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Blits a frame into the destination rectangle, scaled with nearest-neighbour and centred with
    /// its aspect ratio preserved.
    /// </summary>
    /// <remarks>
    /// Nearest-neighbour on purpose. The movies are 320x240-ish AVIs from the late nineties being
    /// shown on a viewport that is usually a whole multiple of that, and a smoothing filter would
    /// blur art the rest of the renderer keeps pixel-exact. Letterboxing rather than stretching for
    /// the same reason: <c>Movie::GetSrcRect</c> exists precisely so the engine could respect the
    /// source's shape.
    /// </remarks>
    private void Draw(Surface destination, VideoFrame frame)
    {
        var area = DestinationRect ?? destination.Bounds;
        if (!area.TryClipTo(destination.ClipRect, out var target))
        {
            return;
        }

        int scaledWidth = Math.Min(target.Width, target.Height * frame.Width / frame.Height);
        int scaledHeight = Math.Min(target.Height, target.Width * frame.Height / frame.Width);
        if (scaledWidth <= 0 || scaledHeight <= 0)
        {
            return;
        }

        int offsetX = target.Left + ((target.Width - scaledWidth) / 2);
        int offsetY = target.Top + ((target.Height - scaledHeight) / 2);

        for (int y = 0; y < scaledHeight; y++)
        {
            int sourceY = y * frame.Height / scaledHeight;
            var row = destination.Row(offsetY + y);

            for (int x = 0; x < scaledWidth; x++)
            {
                int sourceX = x * frame.Width / scaledWidth;
                row[offsetX + x] = frame.Argb[(sourceY * frame.Width) + sourceX] | 0xFF000000;
            }
        }
    }
}
