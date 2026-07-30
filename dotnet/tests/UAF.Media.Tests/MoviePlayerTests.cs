namespace UAF.Media.Tests;

/// <summary>
/// Exercises movie playback's timing, placement and degradation with a synthetic decoder.
/// </summary>
/// <remarks>
/// No FFmpeg involved on purpose. Everything that can be wrong about playing a cutscene — wrong speed,
/// wrong place, wrong behaviour when the decoder is absent — is decided in <see cref="MoviePlayer"/>,
/// and none of it needs a native library to check. The decoder is a thin adapter behind
/// <see cref="IVideoDecoder"/>.
/// </remarks>
public class MoviePlayerTests
{
    /// <summary>A decoder yielding solid-colour frames at a fixed interval.</summary>
    private sealed class FakeDecoder(int width, int height, int frames, int intervalMs)
        : IVideoDecoder
    {
        private int next;

        public int Width => width;

        public int Height => height;

        public TimeSpan Duration => TimeSpan.FromMilliseconds(frames * intervalMs);

        public bool IsDisposed { get; private set; }

        public bool TryReadFrame(out VideoFrame? frame)
        {
            if (next >= frames)
            {
                frame = null;
                return false;
            }

            var pixels = new uint[width * height];
            Array.Fill(pixels, (uint)(next + 1));
            frame = new VideoFrame(width, height, pixels,
                                   TimeSpan.FromMilliseconds(next * intervalMs));
            next++;
            return true;
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class FakeFactory(Func<IVideoDecoder?> open, bool available = true)
        : IVideoDecoderFactory
    {
        public bool IsAvailable => available;

        public string? UnavailableReason => available ? null : "test factory reports unavailable";

        public IVideoDecoder? Open(string path) => open();
    }

    /// <summary>
    /// The contract the plan requires: a design with movies must still run on a build with no FFmpeg,
    /// degrading to a skipped cutscene rather than a startup failure (section 6.1, "Packaging").
    /// </summary>
    [Fact]
    public void WithNoDecoderInstalledStartFailsQuietly()
    {
        using var player = new MoviePlayer();

        Assert.False(player.IsSupported);
        Assert.False(player.Start("intro.avi"));
        Assert.False(player.IsPlaying);
        Assert.NotNull(player.LastError);

        // And updating a movie that never started must be harmless, because the engine's twelve
        // PlayMovie call sites go on to call UpdateMovie regardless.
        var surface = new Surface(8, 8, SurfaceKind.Buffer);
        Assert.False(player.Update(surface, 0));
        Assert.All(surface.Pixels, pixel => Assert.Equal(0u, pixel));
    }

    [Fact]
    public void AnUnopenableFileIsReportedNotThrown()
    {
        using var player = new MoviePlayer(new FakeFactory(() => null));

        Assert.True(player.IsSupported);
        Assert.False(player.Start("broken.avi"));
        Assert.Contains("broken.avi", player.LastError!, StringComparison.Ordinal);
    }

    [Fact]
    public void FramesAppearOnTheirPresentationSchedule()
    {
        using var player = new MoviePlayer(
            new FakeFactory(() => new FakeDecoder(4, 4, frames: 3, intervalMs: 100)));
        var surface = new Surface(4, 4, SurfaceKind.Buffer);

        Assert.True(player.Start("movie.avi", timestampMs: 1_000));

        // Frame 0 is due immediately; frame 1 is not due until 100 ms have passed.
        Assert.True(player.Update(surface, 1_000));
        Assert.Equal(1u, surface[0, 0] & 0xFF);

        Assert.True(player.Update(surface, 1_050));
        Assert.Equal(1u, surface[0, 0] & 0xFF);

        Assert.True(player.Update(surface, 1_100));
        Assert.Equal(2u, surface[0, 0] & 0xFF);

        Assert.True(player.Update(surface, 1_200));
        Assert.Equal(3u, surface[0, 0] & 0xFF);
    }

    /// <summary>
    /// A slow frame loses frames rather than falling progressively behind, which is what keeps a movie
    /// in step with its own audio.
    /// </summary>
    [Fact]
    public void LateUpdatesDropFramesInsteadOfShowingThemLate()
    {
        using var player = new MoviePlayer(
            new FakeFactory(() => new FakeDecoder(2, 2, frames: 10, intervalMs: 100)));
        var surface = new Surface(2, 2, SurfaceKind.Buffer);

        player.Start("movie.avi");

        // Half a second of movie in one update: the fifth frame shows, the earlier ones are dropped.
        Assert.True(player.Update(surface, 400));

        Assert.Equal(5u, surface[0, 0] & 0xFF);
        Assert.Equal(4, player.FramesDropped);
        Assert.Equal(1, player.FramesPresented);
    }

    [Fact]
    public void PlaybackEndsWhenTheStreamRunsOut()
    {
        var decoder = new FakeDecoder(2, 2, frames: 2, intervalMs: 10);
        using var player = new MoviePlayer(new FakeFactory(() => decoder));
        var surface = new Surface(2, 2, SurfaceKind.Buffer);

        player.Start("movie.avi");
        player.Update(surface, 0);
        player.Update(surface, 10);

        Assert.False(player.Update(surface, 1_000));
        Assert.False(player.IsPlaying);
        Assert.True(decoder.IsDisposed);
    }

    /// <summary>
    /// A frame is letterboxed into its destination rectangle, not stretched: <c>Movie::GetSrcRect</c>
    /// exists precisely so the engine could respect the source's shape.
    /// </summary>
    [Fact]
    public void FramesAreCentredWithTheirAspectRatioPreserved()
    {
        using var player = new MoviePlayer(
            new FakeFactory(() => new FakeDecoder(4, 2, frames: 1, intervalMs: 10)));
        var surface = new Surface(8, 8, SurfaceKind.Buffer);

        player.Start("movie.avi");
        player.Update(surface, 0);

        // A 2:1 frame in an 8x8 area becomes 8x4, centred vertically: rows 2..5 only.
        Assert.Equal(0u, surface[0, 1]);
        Assert.Equal(1u, surface[0, 2] & 0xFF);
        Assert.Equal(1u, surface[7, 5] & 0xFF);
        Assert.Equal(0u, surface[0, 6]);
    }

    [Fact]
    public void DestinationRectangleConfinesTheMovie()
    {
        using var player = new MoviePlayer(
            new FakeFactory(() => new FakeDecoder(2, 2, frames: 1, intervalMs: 10)));
        var surface = new Surface(8, 8, SurfaceKind.Buffer);

        player.Start("movie.avi", 0, new SurfaceRect(2, 2, 6, 6));
        player.Update(surface, 0);

        Assert.Equal(0u, surface[1, 1]);
        Assert.Equal(1u, surface[2, 2] & 0xFF);
        Assert.Equal(1u, surface[5, 5] & 0xFF);
        Assert.Equal(0u, surface[6, 6]);
    }

    [Fact]
    public void StoppingDisposesTheDecoderAndIsIdempotent()
    {
        var decoder = new FakeDecoder(2, 2, 5, 10);
        var player = new MoviePlayer(new FakeFactory(() => decoder));

        player.Start("movie.avi");
        player.Stop();
        player.Stop();
        player.Dispose();

        Assert.True(decoder.IsDisposed);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void StartingASecondMovieReplacesTheFirst()
    {
        var first = new FakeDecoder(2, 2, 5, 10);
        var second = new FakeDecoder(2, 2, 5, 10);
        var queue = new Queue<IVideoDecoder>([first, second]);

        using var player = new MoviePlayer(new FakeFactory(queue.Dequeue));

        player.Start("one.avi");
        player.Start("two.avi");

        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public void AFrameCanBeUsedAsASurfaceForTheOrdinaryBlitter()
    {
        // Movie frames are ARGB8888 like everything else, so nothing in the renderer needs a special
        // path for video.
        var frame = new VideoFrame(2, 1, [0x00112233, 0x00445566], TimeSpan.Zero);
        var destination = new Surface(2, 1, SurfaceKind.Buffer);

        Assert.True(Blitter.BlitOpaque(destination, 0, 0, frame.AsSurface()));

        Assert.Equal(0xFF112233u, destination[0, 0]);
        Assert.Equal(0xFF445566u, destination[1, 0]);
    }

    [Fact]
    public void TheNullFactoryIsNeverAvailableAndOpensNothing()
    {
        Assert.False(NullVideoDecoderFactory.Instance.IsAvailable);
        Assert.NotNull(NullVideoDecoderFactory.Instance.UnavailableReason);
        Assert.Null(NullVideoDecoderFactory.Instance.Open("anything.avi"));
    }
}
