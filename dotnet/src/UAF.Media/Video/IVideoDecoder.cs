namespace UAF.Media;

/// <summary>One decoded video frame, in the framebuffer's own pixel layout.</summary>
/// <remarks>
/// ARGB8888 so a frame blits into the shared framebuffer with the ordinary blitter and needs no
/// special path in the renderer — which is also what makes movie playback testable by hashing
/// frames (docs/PORTING-PLAN.md section 6.1, "Integration").
/// </remarks>
public sealed record VideoFrame(int Width, int Height, uint[] Argb, TimeSpan Timestamp)
{
    /// <summary>Wraps the frame as a surface so it can be blitted like any other art.</summary>
    public Surface AsSurface() => Surface.FromPixels(Width, Height, Argb, SurfaceKind.Buffer);
}

/// <summary>A video stream being read frame by frame.</summary>
public interface IVideoDecoder : IDisposable
{
    int Width { get; }

    int Height { get; }

    /// <summary>Total length, or <see cref="TimeSpan.Zero"/> when the container does not say.</summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Reads the next frame, or returns false at the end of the stream.
    /// </summary>
    bool TryReadFrame(out VideoFrame? frame);
}

/// <summary>
/// Opens video files, and says up front whether it can.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsAvailable"/> exists because movie support is <b>optional at runtime</b>, which the
/// plan requires (section 6.1, "Packaging"): FFmpeg is the heaviest native dependency in the project,
/// and a design with no movies must run on a build with no FFmpeg present, degrading to a skipped
/// cutscene rather than a startup failure. Every caller checks it instead of catching an exception
/// from a missing native library.
/// </para>
/// <para>
/// The decision on what implements this is FFmpeg/libav, because of what has to be decoded: the
/// movies in existing designs are AVI files encoded with whatever Video for Windows codec their
/// author had in about 1998 — Cinepak, Indeo 3/4/5, Microsoft Video 1 — and no managed-only decoder
/// covers that set.
/// </para>
/// </remarks>
public interface IVideoDecoderFactory
{
    /// <summary>Whether the decoder's dependencies are actually present on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>A short description of why not, when <see cref="IsAvailable"/> is false.</summary>
    string? UnavailableReason { get; }

    /// <summary>Opens a file, or returns null if it cannot be decoded.</summary>
    IVideoDecoder? Open(string path);
}

/// <summary>
/// The factory used when no video decoder is installed: never available, opens nothing.
/// </summary>
/// <remarks>
/// The default, so that a build of the game with no video support behaves correctly by construction
/// rather than by remembering to handle a null factory at each of <c>PlayMovie</c>'s twelve call
/// sites.
/// </remarks>
public sealed class NullVideoDecoderFactory : IVideoDecoderFactory
{
    public static readonly NullVideoDecoderFactory Instance = new();

    public bool IsAvailable => false;

    public string? UnavailableReason => "no video decoder is installed in this build";

    public IVideoDecoder? Open(string path) => null;
}
