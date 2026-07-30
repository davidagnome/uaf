namespace UAF.Media.Tests;

/// <summary>
/// Builds sound files in memory so the audio tests need no fixtures on disk.
/// </summary>
/// <remarks>
/// Synthesised rather than committed because the decoders' interesting cases are format variations —
/// 8-bit unsigned, extra chunks, odd sample rates — and a hand-built file states which variation is
/// under test where a binary blob does not. The one exception is MP3, which cannot be authored
/// without an encoder; that one is a committed fixture.
/// </remarks>
internal static class TestAudio
{
    /// <summary>Writes a RIFF/WAVE file, optionally with a junk chunk between <c>fmt </c> and <c>data</c>.</summary>
    public static byte[] Wave(short[] samples, int sampleRate, int channels,
                              bool includeExtraChunk = false)
    {
        var body = new MemoryStream();
        var writer = new BinaryWriter(body);

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);                       // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);      // average bytes per second
        writer.Write((short)(channels * 2));          // block align
        writer.Write((short)16);                      // bits per sample

        if (includeExtraChunk)
        {
            // Real files carry LIST/INFO metadata here. A decoder that seeks to byte 44 reads it as
            // audio.
            writer.Write("LIST"u8.ToArray());
            writer.Write(4);
            writer.Write("INFO"u8.ToArray());
        }

        writer.Write("data"u8.ToArray());
        writer.Write(samples.Length * 2);
        foreach (short sample in samples)
        {
            writer.Write(sample);
        }

        return Riff(body.ToArray());
    }

    /// <summary>Writes an 8-bit WAVE, whose samples are <b>unsigned</b> with silence at 128.</summary>
    public static byte[] Wave8Bit(byte[] samples, int sampleRate, int channels)
    {
        var body = new MemoryStream();
        var writer = new BinaryWriter(body);

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels);
        writer.Write((short)channels);
        writer.Write((short)8);

        writer.Write("data"u8.ToArray());
        writer.Write(samples.Length);
        writer.Write(samples);

        return Riff(body.ToArray());
    }

    private static byte[] Riff(byte[] body)
    {
        var file = new MemoryStream();
        var writer = new BinaryWriter(file);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(4 + body.Length);
        writer.Write("WAVE"u8.ToArray());
        writer.Write(body);
        return file.ToArray();
    }

    /// <summary>
    /// A one-note format-0 MIDI file: tempo, note on, note off a quarter later, end of track. Half a
    /// second long at 96 ticks per quarter and 500,000 microseconds per quarter.
    /// </summary>
    public static byte[] MidiOneNote()
    {
        byte[] track =
        [
            0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20,   // tempo: 500000 us/quarter
            0x00, 0x90, 0x3C, 0x40,                     // note on, middle C
            0x60, 0x80, 0x3C, 0x40,                     // 96 ticks later, note off
            0x00, 0xFF, 0x2F, 0x00,                     // end of track
        ];

        var file = new MemoryStream();
        var writer = new BinaryWriter(file);

        writer.Write("MThd"u8.ToArray());
        WriteBigEndian(writer, 6);
        WriteBigEndian(writer, (short)0);               // format 0
        WriteBigEndian(writer, (short)1);               // one track
        WriteBigEndian(writer, (short)96);              // ticks per quarter note

        writer.Write("MTrk"u8.ToArray());
        WriteBigEndian(writer, track.Length);
        writer.Write(track);

        return file.ToArray();
    }

    /// <summary>A sine tone as 16-bit samples, for a WAV whose content can be checked.</summary>
    public static short[] Tone(int sampleRate, int frames, double frequency, double amplitude = 0.5)
    {
        var samples = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            double value = Math.Sin(2 * Math.PI * frequency * i / sampleRate) * amplitude;
            samples[i] = (short)(value * short.MaxValue);
        }
        return samples;
    }

    private static void WriteBigEndian(BinaryWriter writer, int value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteBigEndian(BinaryWriter writer, short value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }
}
