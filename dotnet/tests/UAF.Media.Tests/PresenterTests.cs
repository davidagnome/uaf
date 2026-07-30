namespace UAF.Media.Tests;

/// <summary>
/// Covers the headless presenter, which is the mechanism behind the plan's golden-framebuffer strategy
/// (section 8, item 3).
/// </summary>
public class PresenterTests
{
    [Fact]
    public void PresentedFramesAreCopiedNotAliased()
    {
        // The engine presents the same back buffer every frame and then draws over it. A presenter
        // holding the reference would record every frame as identical to the last -- a recorded trace
        // that passes for the wrong reason.
        using var presenter = new HeadlessPresenter(4, 4);
        var buffer = new Surface(4, 4, SurfaceKind.Buffer);

        buffer.Fill(0x00111111);
        presenter.Present(buffer);
        ulong first = presenter.FrameHashes[0];

        buffer.Fill(0x00222222);
        presenter.Present(buffer);

        Assert.Equal(2, presenter.PresentCount);
        Assert.NotEqual(first, presenter.FrameHashes[1]);
        Assert.Equal(first, presenter.FrameHashes[0]);
    }

    [Fact]
    public void LastFrameHoldsWhatWasPresented()
    {
        using var presenter = new HeadlessPresenter(2, 2);
        var buffer = new Surface(2, 2, SurfaceKind.Buffer);
        buffer.Fill(0x00ABCDEF);

        presenter.Present(buffer);

        Assert.All(presenter.LastFrame.Pixels, pixel => Assert.Equal(0xFFABCDEFu, pixel));
    }

    [Fact]
    public void ASmallerSurfaceIsPlacedAtTheOrigin()
    {
        using var presenter = new HeadlessPresenter(4, 4);
        var buffer = new Surface(2, 2, SurfaceKind.Buffer);
        buffer.Fill(0x00FFFFFF);

        presenter.Present(buffer);

        Assert.Equal(0xFFFFFFFFu, presenter.LastFrame[0, 0]);
        Assert.Equal(0xFFFFFFFFu, presenter.LastFrame[1, 1]);
        Assert.Equal(0xFF000000u, presenter.LastFrame[3, 3]);
    }

    /// <summary>
    /// The whole point of hashing frames: an identical drawing sequence produces an identical hash, so
    /// a rendering change is detectable without a reference image.
    /// </summary>
    [Fact]
    public void IdenticalDrawingSequencesHashIdentically()
    {
        static ulong Draw()
        {
            using var presenter = new HeadlessPresenter(8, 8);
            var buffer = new Surface(8, 8, SurfaceKind.Buffer);
            var sprite = new Surface(4, 4, SurfaceKind.Sprite) { ColorKey = 0x00FF00FF };
            sprite.Fill(0x00FF00FF);
            sprite[1, 1] = 0x00112233;
            sprite[2, 2] = 0x00445566;

            buffer.Fill(0x00202020);
            Blitter.Blit(buffer, 2, 2, sprite);
            Blitter.Darken(buffer, new SurfaceRect(0, 0, 4, 4), 128);
            presenter.Present(buffer);

            return presenter.FrameHashes[0];
        }

        Assert.Equal(Draw(), Draw());
    }

    [Fact]
    public void RecordedInputReplaysInOrder()
    {
        var source = new RecordedInputSource(
            InputEvent.KeyDown(VirtualKey.NumPad7, timestampMs: 10),
            InputEvent.MouseDown(4, 5, MouseButtons.Left, timestampMs: 20),
            InputEvent.Quit(timestampMs: 30));

        source.Pump();

        Assert.True(source.TryPoll(out var first));
        Assert.Equal(VirtualKey.NumPad7, first.Key);

        Assert.True(source.TryPoll(out var second));
        Assert.Equal(MouseButtons.Left, second.Button);
        Assert.Equal(4, second.X);

        Assert.True(source.TryPoll(out var third));
        Assert.Equal(InputEventKind.Quit, third.Kind);

        Assert.False(source.TryPoll(out _));
    }

    [Fact]
    public void VirtualKeyValuesMatchTheWindowsCodesTheEngineSwitchesOn()
    {
        // CInput::LookupKeyCode switches on raw VK values, so keeping the numbering makes the Phase 4
        // translation a cast rather than a 60-case table.
        Assert.Equal(0x0D, (int)VirtualKey.Return);
        Assert.Equal(0x1B, (int)VirtualKey.Escape);
        Assert.Equal(0x20, (int)VirtualKey.Space);
        Assert.Equal(0x67, (int)VirtualKey.NumPad7);
        Assert.Equal(0x24, (int)VirtualKey.Home);
        Assert.Equal(0x41, (int)VirtualKey.A);
        Assert.Equal(0x30, (int)VirtualKey.D0);
        Assert.Equal(0x7B, (int)VirtualKey.F12);
    }

    [Fact]
    public void ModifierBitsMatchTheEnginesFlags()
    {
        // UAFWin/Getinput.h:65 -- SHIFT_KEY 1, ALT_KEY 2, CTRL_KEY 4.
        Assert.Equal(1, (int)KeyModifiers.Shift);
        Assert.Equal(2, (int)KeyModifiers.Alt);
        Assert.Equal(4, (int)KeyModifiers.Control);
    }
}
