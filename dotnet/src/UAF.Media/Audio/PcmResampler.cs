namespace UAF.Media;

/// <summary>
/// Brings decoded audio to <see cref="AudioFormat.Mix"/> so the mixer never has to convert.
/// </summary>
/// <remarks>
/// Linear interpolation, deliberately. The source material is 11 kHz 8-bit game sound effects from
/// the late nineties; a windowed-sinc resampler would cost more than the assets are worth and would
/// make the output depend on the filter's exact taps, which makes a byte-comparing test brittle for
/// no fidelity gain. The original resampled in DirectSound/BASS with something equally simple.
/// </remarks>
public static class PcmResampler
{
    public static PcmData ToMixFormat(float[] samples, AudioFormat source)
        => ToFormat(samples, source, AudioFormat.Mix);

    public static PcmData ToFormat(float[] samples, AudioFormat source, AudioFormat target)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(source.Channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(source.SampleRate);

        if (source == target)
        {
            return new PcmData(samples, target);
        }

        long sourceFrames = samples.Length / source.Channels;
        if (sourceFrames == 0)
        {
            return new PcmData([], target);
        }

        long targetFrames = Math.Max(1,
            (long)Math.Round(sourceFrames * (double)target.SampleRate / source.SampleRate));

        var output = new float[targetFrames * target.Channels];
        double step = (double)(sourceFrames - 1) / Math.Max(1, targetFrames - 1);

        for (long frame = 0; frame < targetFrames; frame++)
        {
            double position = frame * step;
            long left = (long)position;
            long right = Math.Min(left + 1, sourceFrames - 1);
            float blend = (float)(position - left);

            for (int channel = 0; channel < target.Channels; channel++)
            {
                // Mono to stereo duplicates; stereo to mono takes the left channel rather than
                // averaging, because the engine only ever plays mono effects and averaging would
                // quietly halve the level of anything that is out of phase.
                int sourceChannel = Math.Min(channel, source.Channels - 1);
                float a = samples[(left * source.Channels) + sourceChannel];
                float b = samples[(right * source.Channels) + sourceChannel];
                output[(frame * target.Channels) + channel] = a + ((b - a) * blend);
            }
        }

        return new PcmData(output, target);
    }
}
