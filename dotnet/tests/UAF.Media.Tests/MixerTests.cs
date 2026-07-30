namespace UAF.Media.Tests;

/// <summary>
/// Exercises the mixer and the null device — the pair that makes "what would have been heard" an
/// assertion rather than a listening test.
/// </summary>
public class MixerTests
{
    /// <summary>A source that emits a constant value, so mixing arithmetic is checkable by eye.</summary>
    private sealed class ConstantSource(float value, int frames) : IPcmSource
    {
        private int remaining = frames * AudioFormat.Mix.Channels;

        public bool IsFinished => remaining <= 0;

        public int Read(Span<float> destination)
        {
            int take = Math.Min(remaining, destination.Length);
            destination[..take].Fill(value);
            remaining -= take;
            return take;
        }
    }

    [Fact]
    public void VoicesAreSummed()
    {
        var mixer = new SoftwareMixer();
        mixer.Add(new ConstantSource(0.25f, 16), AudioChannel.Effect);
        mixer.Add(new ConstantSource(0.5f, 16), AudioChannel.Effect);

        var buffer = new float[8];
        mixer.Mix(buffer);

        Assert.All(buffer, sample => Assert.Equal(0.75f, sample, 0.0001f));
    }

    /// <summary>
    /// Clipped, not normalised. Scaling the whole buffer to fit one loud frame would make a long
    /// sound's volume depend on its peak, and the original's mixers clipped too.
    /// </summary>
    [Fact]
    public void SummedVoicesAreClippedRatherThanScaled()
    {
        var mixer = new SoftwareMixer();
        for (int i = 0; i < 4; i++)
        {
            mixer.Add(new ConstantSource(0.5f, 16), AudioChannel.Effect);
        }

        var buffer = new float[8];
        mixer.Mix(buffer);

        Assert.All(buffer, sample => Assert.Equal(1f, sample, 0.0001f));
    }

    [Fact]
    public void FinishedVoicesAreDropped()
    {
        var mixer = new SoftwareMixer();
        mixer.Add(new ConstantSource(1f, frames: 2), AudioChannel.Effect);

        Assert.Equal(1, mixer.ActiveVoiceCount);

        mixer.Mix(new float[64]);

        Assert.Equal(0, mixer.ActiveVoiceCount);
    }

    [Fact]
    public void MuteSilencesEverythingWithoutChangingTheVolumes()
    {
        var mixer = new SoftwareMixer { IsMuted = true, MasterVolume = 0.8f };
        mixer.Add(new ConstantSource(1f, 16), AudioChannel.Effect);

        var buffer = new float[8];
        mixer.Mix(buffer);
        Assert.All(buffer, sample => Assert.Equal(0f, sample));

        mixer.IsMuted = false;
        mixer.Mix(buffer);
        Assert.All(buffer, sample => Assert.Equal(0.8f, sample, 0.0001f));
    }

    /// <summary>
    /// Music's on/off switch is separate from its volume, because <c>SetMusicEnable</c> must not
    /// destroy the volume the player chose.
    /// </summary>
    [Fact]
    public void DisablingMusicLeavesEffectsAudible()
    {
        var mixer = new SoftwareMixer { IsMusicEnabled = false, MusicVolume = 0.9f };
        mixer.Add(new ConstantSource(0.5f, 16), AudioChannel.Music);
        mixer.Add(new ConstantSource(0.5f, 16), AudioChannel.Effect);

        var buffer = new float[8];
        mixer.Mix(buffer);

        Assert.All(buffer, sample => Assert.Equal(0.5f, sample, 0.0001f));

        mixer.IsMusicEnabled = true;
        mixer.Mix(buffer);
        Assert.All(buffer, sample => Assert.Equal(0.95f, sample, 0.0001f));
    }

    [Fact]
    public void ChannelsCanBeStoppedIndependently()
    {
        var mixer = new SoftwareMixer();
        mixer.Add(new ConstantSource(1f, 1_000), AudioChannel.Music);
        mixer.Add(new ConstantSource(1f, 1_000), AudioChannel.Background);
        mixer.Add(new ConstantSource(1f, 1_000), AudioChannel.Effect);

        Assert.Equal(1, mixer.RemoveChannel(AudioChannel.Background));

        Assert.True(mixer.IsChannelPlaying(AudioChannel.Music));
        Assert.False(mixer.IsChannelPlaying(AudioChannel.Background));
        Assert.True(mixer.IsChannelPlaying(AudioChannel.Effect));
    }

    [Fact]
    public void MixOverwritesRatherThanAccumulatesAcrossCalls()
    {
        // The device hands back a reused buffer, so a mixer that added to it would ramp to clipping.
        var mixer = new SoftwareMixer();
        mixer.Add(new ConstantSource(0.5f, 1_000), AudioChannel.Effect);

        var buffer = new float[8];
        mixer.Mix(buffer);
        mixer.Mix(buffer);

        Assert.All(buffer, sample => Assert.Equal(0.5f, sample, 0.0001f));
    }

    [Fact]
    public void NullDeviceRendersExactlyWhatIsAskedFor()
    {
        var mixer = new SoftwareMixer();
        mixer.Add(new ConstantSource(0.25f, 1_000), AudioChannel.Effect);

        using var device = new NullAudioDevice();
        device.Start(mixer.Mix);

        var rendered = device.Render(frames: 32);

        Assert.Equal(64, rendered.Length);
        Assert.Equal(32, device.FramesRendered);
        Assert.All(rendered.ToArray(), sample => Assert.Equal(0.25f, sample, 0.0001f));
    }

    [Fact]
    public void StoppedDeviceProducesSilence()
    {
        var mixer = new SoftwareMixer();
        mixer.Add(new ConstantSource(1f, 1_000), AudioChannel.Effect);

        using var device = new NullAudioDevice();
        device.Start(mixer.Mix);
        device.Stop();

        Assert.All(device.Render(8).ToArray(), sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void PcmDataLoopsSeamlessly()
    {
        var data = new PcmData([1f, 1f, 2f, 2f], AudioFormat.Mix);
        var source = data.CreateSource(loop: true);

        var buffer = new float[8];
        Assert.Equal(8, source.Read(buffer));
        Assert.False(source.IsFinished);
        Assert.Equal([1f, 1f, 2f, 2f, 1f, 1f, 2f, 2f], buffer);
    }

    [Fact]
    public void PcmDataWithoutLoopingRunsOut()
    {
        var data = new PcmData([1f, 1f], AudioFormat.Mix);
        var source = data.CreateSource(loop: false);

        var buffer = new float[8];
        Assert.Equal(2, source.Read(buffer));
        Assert.True(source.IsFinished);
    }
}
