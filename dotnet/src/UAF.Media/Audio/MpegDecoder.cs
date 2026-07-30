using NLayer;

namespace UAF.Media;

/// <summary>
/// Decodes the MPEG audio designs use for music (<c>.mp3</c>, and the <c>.mp2</c>/<c>.mp1</c> the
/// original also accepted).
/// </summary>
/// <remarks>
/// <para>
/// MP3 support in the original came from BASS, which is proprietary and whose licence conflicts
/// with this project's GPL v2 (docs/PORTING-PLAN.md section 2.4). SDL3 decodes only WAV, so
/// something had to fill the gap; NLayer is the NAudio project's pure-C# decoder, MIT-licensed and
/// with no native component, which keeps this decodable on a CI runner with no audio device.
/// </para>
/// <para>
/// Decoded whole, like every other source here — see <see cref="PcmData"/> for why.
/// </para>
/// </remarks>
public static class MpegDecoder
{
    public static PcmData Decode(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var stream = File.OpenRead(path);
        return Decode(stream);
    }

    public static PcmData Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var file = new MpegFile(stream);

        var source = new AudioFormat(file.SampleRate, Math.Clamp(file.Channels, 1, 2));
        var decoded = new List<float>();
        var buffer = new float[source.Channels * 4096];

        while (true)
        {
            int read = file.ReadSamples(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            decoded.AddRange(buffer.AsSpan(0, read));
        }

        return PcmResampler.ToMixFormat([.. decoded], source);
    }
}
