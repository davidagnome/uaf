namespace UAF.Media.Tests;

/// <summary>
/// Drives the animation state machine through the cases the art records actually express:
/// <c>NumFrames</c>, <c>TimeDelay</c>, <c>MaxLoops</c> and <c>RestartFrame</c>.
/// </summary>
/// <remarks>
/// Every test here supplies its own timestamps. That is the whole reason this is testable: the
/// original drove the same logic from a Windows timer in the editor and from <c>virtualGameTime</c> in
/// the engine, and neither could be stepped.
/// </remarks>
public class AnimatedSpriteTests
{
    [Fact]
    public void AnimationBelowTheMinimumDelayNeverAdvances()
    {
        // PIC_DATA::AnimateNextFrame bails out under 30ms. This is how the data says "still picture".
        var sprite = new AnimatedSprite(frameCount: 4, timeDelayMs: 29,
                                        flags: AnimationFlags.Loop);

        Assert.False(sprite.AnimateNextFrame(10_000));
        Assert.Equal(0, sprite.Frame);
    }

    [Fact]
    public void FrameAdvancesOnlyOnceTheDelayHasElapsed()
    {
        var sprite = new AnimatedSprite(frameCount: 4, timeDelayMs: 100,
                                        flags: AnimationFlags.Loop);
        sprite.SetFirstFrame(1_000);

        Assert.False(sprite.AnimateNextFrame(1_099));
        Assert.Equal(0, sprite.Frame);

        Assert.True(sprite.AnimateNextFrame(1_100));
        Assert.Equal(1, sprite.Frame);
    }

    [Fact]
    public void WithoutTheLoopFlagTheAnimationStopsOnTheLastFrame()
    {
        var sprite = new AnimatedSprite(frameCount: 3, timeDelayMs: 100);
        sprite.SetFirstFrame(0);

        Assert.True(sprite.AnimateNextFrame(100));
        Assert.Equal(1, sprite.Frame);
        Assert.True(sprite.AnimateNextFrame(200));
        Assert.Equal(2, sprite.Frame);

        Assert.False(sprite.AnimateNextFrame(300));
        Assert.Equal(2, sprite.Frame);
        Assert.True(sprite.IsFinished);
    }

    /// <summary>
    /// <c>RestartFrame</c> is 1-based. <c>Graphics::SetNextFrame</c> subtracts one from it, so a value
    /// of 2 loops back to frame index 1 — treating it as 0-based restarts one frame too early on every
    /// pass.
    /// </summary>
    [Fact]
    public void LoopReturnsToRestartFrameMinusOne()
    {
        var sprite = new AnimatedSprite(frameCount: 4, timeDelayMs: 100, restartFrame: 2,
                                        flags: AnimationFlags.Loop);
        sprite.SetFirstFrame(0);

        sprite.AnimateNextFrame(100);
        sprite.AnimateNextFrame(200);
        sprite.AnimateNextFrame(300);
        Assert.Equal(3, sprite.Frame);

        Assert.True(sprite.AnimateNextFrame(400));
        Assert.Equal(1, sprite.Frame);
    }

    [Fact]
    public void RestartFrameOfZeroLoopsToTheStart()
    {
        var sprite = new AnimatedSprite(frameCount: 2, timeDelayMs: 100, restartFrame: 0,
                                        flags: AnimationFlags.Loop);
        sprite.SetFirstFrame(0);

        sprite.AnimateNextFrame(100);
        Assert.Equal(1, sprite.Frame);

        sprite.AnimateNextFrame(200);
        Assert.Equal(0, sprite.Frame);
    }

    [Fact]
    public void MaxLoopCounterStopsAfterTheGivenNumberOfPasses()
    {
        var sprite = new AnimatedSprite(frameCount: 2, timeDelayMs: 100,
                                        flags: AnimationFlags.Loop | AnimationFlags.MaxLoopCounter,
                                        maxLoops: 2);
        sprite.SetFirstFrame(0);

        long now = 0;
        int advances = 0;
        while (!sprite.IsFinished && advances < 20)
        {
            now += 100;
            if (sprite.AnimateNextFrame(now))
            {
                advances++;
            }
        }

        Assert.True(sprite.IsFinished);
        Assert.Equal(2u, sprite.LoopCounter);
    }

    [Fact]
    public void KeypressBeforeLoopHoldsOnTheLastFrame()
    {
        var sprite = new AnimatedSprite(
            frameCount: 2, timeDelayMs: 100,
            flags: AnimationFlags.Loop | AnimationFlags.KeypressBeforeLoop);
        sprite.SetFirstFrame(0);

        Assert.True(sprite.AnimateNextFrame(100));
        Assert.Equal(1, sprite.Frame);

        // On the last frame it reports "still going" but does not move, however long passes.
        Assert.True(sprite.AnimateNextFrame(200));
        Assert.True(sprite.IsAwaitingKeypress);
        Assert.Equal(1, sprite.Frame);

        Assert.True(sprite.AnimateNextFrame(10_000));
        Assert.Equal(1, sprite.Frame);

        sprite.AcknowledgeKeypress();
        Assert.False(sprite.IsAwaitingKeypress);
        Assert.Equal(0, sprite.Frame);
    }

    /// <summary>
    /// Non-sequenced styles index their frames — one per facing, one per target — so a timer must not
    /// walk through them. <c>PIC_DATA::AnimateNextFrame</c> and <c>SetPicTimeDelay</c> both refuse.
    /// </summary>
    [Theory]
    [InlineData(AnimationStyle.Directional)]
    [InlineData(AnimationStyle.Radius)]
    [InlineData(AnimationStyle.EachTarget)]
    public void NonSequencedStylesDoNotAnimateOnATimer(AnimationStyle style)
    {
        var sprite = new AnimatedSprite(frameCount: 8, timeDelayMs: 100,
                                        flags: AnimationFlags.Loop, style: style);
        sprite.SetFirstFrame(0);

        Assert.False(sprite.AnimateNextFrame(5_000));
        Assert.Equal(0, sprite.Frame);

        // But the engine can still select a frame directly, which is the point of these styles.
        Assert.True(sprite.SetFrame(5));
        Assert.Equal(5, sprite.Frame);
    }

    [Fact]
    public void SetFrameRejectsOutOfRangeRequests()
    {
        // Graphics::SetFrame silently ignores them rather than clamping.
        var sprite = new AnimatedSprite(frameCount: 3);

        Assert.False(sprite.SetFrame(-1));
        Assert.False(sprite.SetFrame(3));
        Assert.Equal(0, sprite.Frame);
    }

    [Fact]
    public void ForcedNextFrameWrapsWithoutATimer()
    {
        // The engine calls SetNextFrame directly when posing combat icons.
        var sprite = new AnimatedSprite(frameCount: 2, restartFrame: 0);

        sprite.SetNextFrame();
        Assert.Equal(1, sprite.Frame);
        sprite.SetNextFrame();
        Assert.Equal(0, sprite.Frame);
    }

    [Fact]
    public void SetFirstFrameResetsTheLoopCounter()
    {
        var sprite = new AnimatedSprite(frameCount: 2, timeDelayMs: 100,
                                        flags: AnimationFlags.Loop | AnimationFlags.MaxLoopCounter,
                                        maxLoops: 1);
        sprite.SetFirstFrame(0);
        sprite.AnimateNextFrame(100);
        sprite.AnimateNextFrame(200);
        Assert.True(sprite.IsFinished);

        sprite.SetFirstFrame(1_000);

        Assert.False(sprite.IsFinished);
        Assert.Equal(0u, sprite.LoopCounter);
        Assert.Equal(0, sprite.Frame);
    }

    [Fact]
    public void FlagValuesStillMatchTheOriginals()
    {
        // PIC_DATA::flags is read straight out of a design file, so these numbers are format.
        Assert.Equal(1u, (uint)AnimationFlags.KeypressBeforeLoop);
        Assert.Equal(2u, (uint)AnimationFlags.MaxLoopCounter);
        Assert.Equal(4u, (uint)AnimationFlags.Loop);
        Assert.Equal(8u, (uint)AnimationFlags.LoopForever);
    }
}

/// <summary>Covers the frame-number-to-source-rectangle mapping.</summary>
public class SpriteSheetTests
{
    [Fact]
    public void FramesAreLaidOutRowMajorAcrossTheSheet()
    {
        // CDXSprite::Draw: TilesInWidth = surfaceWidth / frameWidth, then frame % / frame / it.
        var sheet = new SpriteSheet(new Surface(64, 32), frameWidth: 32, frameHeight: 16,
                                    frameCount: 4);

        Assert.Equal(2, sheet.FramesPerRow);
        Assert.Equal(new SurfaceRect(0, 0, 32, 16), sheet.FrameRect(0));
        Assert.Equal(new SurfaceRect(32, 0, 64, 16), sheet.FrameRect(1));
        Assert.Equal(new SurfaceRect(0, 16, 32, 32), sheet.FrameRect(2));
        Assert.Equal(new SurfaceRect(32, 16, 64, 32), sheet.FrameRect(3));
    }

    [Fact]
    public void ASingleColumnSheetIsAVerticalStrip()
    {
        var sheet = new SpriteSheet(new Surface(16, 48), frameWidth: 16, frameHeight: 16,
                                    frameCount: 3);

        Assert.Equal(1, sheet.FramesPerRow);
        Assert.Equal(new SurfaceRect(0, 32, 16, 48), sheet.FrameRect(2));
    }

    [Fact]
    public void FrameNumberIsBoundsChecked()
    {
        var sheet = new SpriteSheet(new Surface(32, 16), 16, 16, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.FrameRect(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.FrameRect(-1));
    }

    [Fact]
    public void AnimatedSpriteDrivesASheetThroughTheBlitter()
    {
        // The integration that matters: the state machine picks a frame, the sheet turns it into a
        // rectangle, the blitter draws it. Each frame is a solid colour so the result is checkable.
        var sheetSurface = new Surface(4, 1, SurfaceKind.Sprite);
        for (int x = 0; x < 4; x++)
        {
            sheetSurface[x, 0] = (uint)(x + 1);
        }

        var sheet = new SpriteSheet(sheetSurface, frameWidth: 1, frameHeight: 1, frameCount: 4);
        var sprite = new AnimatedSprite(frameCount: 4, timeDelayMs: 50,
                                        flags: AnimationFlags.Loop);
        var destination = new Surface(1, 1, SurfaceKind.Buffer);

        sprite.SetFirstFrame(0);
        var drawn = new List<uint>();

        for (long now = 0; now <= 200; now += 50)
        {
            sprite.AnimateNextFrame(now);
            Blitter.Blit(destination, 0, 0, sheet.Surface, sheet.FrameRect(sprite.Frame));
            drawn.Add(destination[0, 0] & 0xFF);
        }

        Assert.Equal([1u, 2u, 3u, 4u, 1u], drawn);
    }
}
