using System.Text;

namespace UAF.Media;

/// <summary>
/// Reads the <c>.wav</c> files designs ship as sound effects.
/// </summary>
/// <remarks>
/// <para>
/// Bespoke rather than a library, and small enough to be worth it: the format the designs actually
/// contain is RIFF/WAVE with an uncompressed PCM <c>data</c> chunk, 8 or 16 bits, mono or stereo,
/// 11025 to 44100 Hz. That is what DirectSound accepted in 1999 and what the editor's sound
/// browser produced, so it is the whole corpus. IEEE-float WAVs are accepted too because modern
/// editors emit them and a design author may have re-saved a sound.
/// </para>
/// <para>
/// Chunks are walked rather than assumed to be at fixed offsets: real files carry <c>LIST</c> and
/// <c>fact</c> chunks between <c>fmt </c> and <c>data</c>, and a decoder that seeks to byte 44
/// reads metadata as audio.
/// </para>
/// </remarks>
public static class WaveDecoder
{
    private const ushort FormatPcm = 1;
    private const ushort FormatIeeeFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    public static PcmData Decode(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var stream = File.OpenRead(path);
        return Decode(stream);
    }

    public static PcmData Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        if (ReadFourCc(reader) != "RIFF")
        {
            throw new InvalidDataException("not a RIFF file");
        }

        reader.ReadUInt32();                     // RIFF chunk size, unused: chunks are walked

        if (ReadFourCc(reader) != "WAVE")
        {
            throw new InvalidDataException("RIFF file is not WAVE");
        }

        ushort formatTag = 0;
        int channels = 0;
        int sampleRate = 0;
        int bitsPerSample = 0;
        byte[]? data = null;

        while (stream.Position + 8 <= stream.Length)
        {
            string id = ReadFourCc(reader);
            uint size = reader.ReadUInt32();

            // Chunks are word-aligned, so an odd size is followed by a pad byte. The header itself is
            // always eight bytes, which is why this loop advances even on a zero-sized chunk.
            long next = stream.Position + size + (size % 2);

            switch (id)
            {
                case "fmt ":
                    formatTag = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadUInt32();                 // average bytes per second
                    reader.ReadUInt16();                 // block align
                    bitsPerSample = reader.ReadUInt16();
                    break;

                case "data":
                    data = reader.ReadBytes((int)size);
                    break;
            }

            stream.Position = Math.Min(next, stream.Length);
        }

        if (data is null || channels is < 1 or > 2 || sampleRate <= 0)
        {
            throw new InvalidDataException("WAVE file has no usable fmt/data pair");
        }

        if (formatTag is not (FormatPcm or FormatIeeeFloat or FormatExtensible))
        {
            throw new NotSupportedException(
                $"WAVE format tag {formatTag} is compressed; only PCM and IEEE float are read");
        }

        float[] samples = formatTag == FormatIeeeFloat
            ? DecodeFloat(data, bitsPerSample)
            : DecodeInteger(data, bitsPerSample);

        return PcmResampler.ToMixFormat(samples, new AudioFormat(sampleRate, channels));
    }

    private static float[] DecodeInteger(byte[] data, int bitsPerSample)
    {
        switch (bitsPerSample)
        {
            case 8:
            {
                // 8-bit WAV is UNSIGNED, unlike every wider size, which is signed. Reading it as
                // signed produces audible clipping rather than silence, so it is easy to ship.
                var samples = new float[data.Length];
                for (int i = 0; i < data.Length; i++)
                {
                    samples[i] = (data[i] - 128) / 128f;
                }
                return samples;
            }

            case 16:
            {
                var samples = new float[data.Length / 2];
                for (int i = 0; i < samples.Length; i++)
                {
                    short value = (short)(data[i * 2] | (data[(i * 2) + 1] << 8));
                    samples[i] = value / 32768f;
                }
                return samples;
            }

            case 24:
            {
                var samples = new float[data.Length / 3];
                for (int i = 0; i < samples.Length; i++)
                {
                    int value = (data[i * 3] << 8) | (data[(i * 3) + 1] << 16) |
                                (data[(i * 3) + 2] << 24);
                    samples[i] = (value >> 8) / 8388608f;
                }
                return samples;
            }

            case 32:
            {
                var samples = new float[data.Length / 4];
                for (int i = 0; i < samples.Length; i++)
                {
                    int value = BitConverter.ToInt32(data, i * 4);
                    samples[i] = value / 2147483648f;
                }
                return samples;
            }

            default:
                throw new NotSupportedException($"{bitsPerSample}-bit PCM is not read");
        }
    }

    private static float[] DecodeFloat(byte[] data, int bitsPerSample)
    {
        if (bitsPerSample != 32)
        {
            throw new NotSupportedException($"{bitsPerSample}-bit float PCM is not read");
        }

        var samples = new float[data.Length / 4];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToSingle(data, i * 4);
        }
        return samples;
    }

    private static string ReadFourCc(BinaryReader reader) =>
        Encoding.ASCII.GetString(reader.ReadBytes(4));
}
