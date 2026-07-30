namespace UAF.Media.Tests;

/// <summary>
/// Decodes the three formats designs actually contain — <c>.wav</c> effects, <c>.mp3</c> music, and
/// <c>.mid</c> music — with no audio device involved.
/// </summary>
public class AudioDecoderTests
{
    [Fact]
    public void SixteenBitMonoIsBroughtToTheMixFormat()
    {
        byte[] file = TestAudio.Wave(TestAudio.Tone(22050, 2205, 440), 22050, 1);

        var pcm = WaveDecoder.Decode(new MemoryStream(file));

        Assert.Equal(AudioFormat.Mix, pcm.Format);
        // 2205 frames at 22050 Hz is 100 ms, which is 4410 frames at 44100.
        Assert.Equal(4410, pcm.FrameCount);
        Assert.Equal(100, pcm.Duration.TotalMilliseconds, 1);
    }

    [Fact]
    public void MonoIsDuplicatedAcrossBothOutputChannels()
    {
        byte[] file = TestAudio.Wave([16384, 16384, 16384, 16384], 44100, 1);

        var pcm = WaveDecoder.Decode(new MemoryStream(file));

        for (int frame = 0; frame < pcm.FrameCount; frame++)
        {
            Assert.Equal(pcm.Samples[frame * 2], pcm.Samples[(frame * 2) + 1]);
        }
    }

    /// <summary>
    /// 8-bit WAV samples are unsigned with silence at 128, unlike every wider width. Reading them as
    /// signed produces loud clipping rather than silence, so the bug ships audibly.
    /// </summary>
    [Fact]
    public void EightBitSamplesAreTreatedAsUnsigned()
    {
        byte[] file = TestAudio.Wave8Bit([128, 128, 128, 128], 44100, 1);

        var pcm = WaveDecoder.Decode(new MemoryStream(file));

        Assert.All(pcm.Samples, sample => Assert.Equal(0f, sample, 0.001f));
    }

    [Fact]
    public void EightBitFullScaleReachesTheRailsAndNoFurther()
    {
        byte[] file = TestAudio.Wave8Bit([255, 0], 44100, 1);

        var pcm = WaveDecoder.Decode(new MemoryStream(file));

        Assert.True(pcm.Samples[0] is > 0.98f and <= 1.0f);
        Assert.Equal(-1f, pcm.Samples[^1], 0.01f);
    }

    /// <summary>
    /// Chunks between <c>fmt </c> and <c>data</c> are normal in real files. A decoder that assumes the
    /// data starts at byte 44 plays the metadata as a burst of noise.
    /// </summary>
    [Fact]
    public void ChunksBetweenFormatAndDataAreSkipped()
    {
        short[] samples = [1000, 2000, 3000, 4000];
        byte[] plain = TestAudio.Wave(samples, 44100, 1);
        byte[] withList = TestAudio.Wave(samples, 44100, 1, includeExtraChunk: true);

        var a = WaveDecoder.Decode(new MemoryStream(plain));
        var b = WaveDecoder.Decode(new MemoryStream(withList));

        Assert.Equal(a.Samples, b.Samples);
    }

    [Fact]
    public void ANonRiffFileIsRejectedWithADiagnosis()
    {
        var garbage = new MemoryStream(new byte[64]);

        var error = Assert.Throws<InvalidDataException>(() => WaveDecoder.Decode(garbage));
        Assert.Contains("RIFF", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompressedWaveIsRefusedRatherThanPlayedAsNoise()
    {
        // Format tag 0x11 is IMA ADPCM. The original got these from BASS; refusing loudly is better
        // than decoding the compressed bytes as PCM.
        var body = new MemoryStream();
        var writer = new BinaryWriter(body);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(28);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)0x11);
        writer.Write((short)1);
        writer.Write(22050);
        writer.Write(11025);
        writer.Write((short)256);
        writer.Write((short)4);
        writer.Write("data"u8.ToArray());
        writer.Write(4);
        writer.Write(new byte[4]);

        Assert.Throws<NotSupportedException>(
            () => WaveDecoder.Decode(new MemoryStream(body.ToArray())));
    }

    /// <summary>
    /// The committed fixture is a quarter-second 440 Hz tone generated with the ffmpeg CLI, not
    /// content from <c>reference/</c>. MP3 support exists because the original got it from BASS, which
    /// this project cannot use.
    /// </summary>
    [Fact]
    public void Mp3IsDecodedToAudibleAudioAtTheMixFormat()
    {
        string path = TestPaths.Asset("tone-440hz.mp3");

        var pcm = MpegDecoder.Decode(path);

        Assert.Equal(AudioFormat.Mix, pcm.Format);
        Assert.InRange(pcm.Duration.TotalMilliseconds, 200, 320);

        float peak = 0f;
        foreach (float sample in pcm.Samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }
        Assert.True(peak > 0.1f, $"decoded MP3 is effectively silent (peak {peak})");
    }

    [Fact]
    public void MidiLengthIsReadableWithoutASoundFont()
    {
        // Parsing and synthesis are separate in MeltySynth, which is what lets a build with no
        // SoundFont still validate a design's music files.
        using var stream = new MemoryStream(TestAudio.MidiOneNote());

        Assert.True(MidiSynth.TryReadDuration(stream, out var duration));
        Assert.Equal(500, duration.TotalMilliseconds, 1);
    }

    [Fact]
    public void ACorruptMidiFileIsReportedRatherThanThrown()
    {
        using var stream = new MemoryStream("not a midi file at all"u8.ToArray());

        Assert.False(MidiSynth.TryReadDuration(stream, out _));
    }

    [Fact]
    public void MissingSoundFontIsReportedNotThrown()
    {
        Assert.False(MidiSynth.TryCreate("/nonexistent/general.sf2", out var synth, out string? why));
        Assert.Null(synth);
        Assert.Contains("not found", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptySoundFontPathIsReportedNotThrown()
    {
        Assert.False(MidiSynth.TryCreate("", out _, out string? why));
        Assert.NotNull(why);
    }

    [Theory]
    [InlineData("hit.wav", AudioFileKind.Wave)]
    [InlineData("BATTLE.MID", AudioFileKind.Midi)]
    [InlineData("theme.mp3", AudioFileKind.Mpeg)]
    [InlineData("theme.mp2", AudioFileKind.Mpeg)]
    [InlineData("theme.mp1", AudioFileKind.Mpeg)]
    [InlineData("readme.txt", AudioFileKind.Unknown)]
    // The original tests with strstr, not an extension compare (Shared/SoundMgr.cpp:2907), so a
    // trailing suffix does not change the verdict. Reproduced, because designs in the wild hit it.
    [InlineData("battle.mid.bak", AudioFileKind.Midi)]
    [InlineData("sounds.wav/quiet.dat", AudioFileKind.Wave)]
    public void FileKindMatchesTheOriginalsRule(string name, AudioFileKind expected)
        => Assert.Equal(expected, AudioFileKinds.Detect(name));
}
