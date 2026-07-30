namespace UAF.Media;

/// <summary>
/// Puts a finished framebuffer on screen. The only interface in the layer that a platform has to
/// implement to make the game visible.
/// </summary>
/// <remarks>
/// <para>
/// The framebuffer is the contract between drawing and presentation (docs/PORTING-PLAN.md
/// section 6). Everything above this interface is platform-agnostic managed code; below it are
/// SDL3 for the game and, later, an Avalonia <c>WriteableBitmap</c> for the editor. Splitting here
/// is what stops the project growing two blitters, because the editor draws game art too.
/// </para>
/// <para>
/// <see cref="Present"/> takes the surface as an argument rather than owning one so that the
/// engine can present different buffers — the original flips between a front and back buffer and
/// blits the mouse cursor into a saved region around them.
/// </para>
/// </remarks>
public interface IPresenter : IDisposable
{
    /// <summary>Framebuffer size in pixels. A surface of another size is scaled to fit.</summary>
    int Width { get; }

    int Height { get; }

    /// <summary>Uploads and shows <paramref name="surface"/>.</summary>
    void Present(Surface surface);
}

/// <summary>
/// A presenter that keeps the last frame in memory instead of showing it.
/// </summary>
/// <remarks>
/// This is what makes the golden-framebuffer strategy (plan section 8, item 3) possible: a test
/// runs the real drawing code, presents, and hashes the result, with no display, no GPU and no
/// SDL. It is also the presenter the headless engine tests will use in Phase 4.
/// </remarks>
public sealed class HeadlessPresenter : IPresenter
{
    private readonly List<ulong> hashes = [];

    public HeadlessPresenter(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        LastFrame = new Surface(width, height, SurfaceKind.Buffer);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>How many times <see cref="Present"/> has been called.</summary>
    public int PresentCount { get; private set; }

    /// <summary>A copy of the most recently presented frame.</summary>
    public Surface LastFrame { get; }

    /// <summary>The hash of every frame presented, in order — a cheap frame-by-frame trace.</summary>
    public IReadOnlyList<ulong> FrameHashes => hashes;

    public void Present(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        // Copy rather than keep a reference: the engine presents the same back buffer every frame
        // and then draws over it, so holding the reference would make every recorded hash equal to
        // the last one -- a test that passes for the wrong reason.
        if (surface.Width == Width && surface.Height == Height)
        {
            surface.Pixels.CopyTo(LastFrame.Pixels, 0);
        }
        else
        {
            LastFrame.Fill(0);
            Blitter.BlitOpaque(LastFrame, 0, 0, surface);
        }

        PresentCount++;
        hashes.Add(LastFrame.ContentHash());
    }

    public void Dispose()
    {
    }
}
