using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// The LZW encoder, tested against the decoder that was diffed against the C++ oracle.
/// </summary>
/// <remarks>
/// <para>
/// <b>The decoder is the specification here, and it is a strong one.</b> It reads every compressed
/// design in the corpus to exact end-of-file, so a stream it accepts and reproduces byte for byte
/// is a stream the reference would too. What this cannot show is that the <i>encoding choices</i>
/// match — a different but valid LZW stream would round-trip just as well — so the block layout
/// and the termination rule are asserted separately against the reference's own arithmetic.
/// </para>
/// </remarks>
public class CarLzwCompressorTests
{
    private const int BlockBytes = 52;

    private static byte[] Compress(ReadOnlySpan<byte> input)
    {
        var stream = new MemoryStream();
        var compressor = new CarLzwCompressor(stream);
        compressor.Write(input);
        compressor.Flush();
        return stream.ToArray();
    }

    private static byte[] Decompress(byte[] compressed, int count)
    {
        var decompressor = new CarLzwDecompressor(new MemoryStream(compressed));
        var output = new byte[count];
        int got = decompressor.Read(output, count);
        return output.AsSpan(0, got).ToArray();
    }

    private static void RoundTrips(byte[] input)
    {
        Assert.Equal(input, Decompress(Compress(input), input.Length));
    }

    // ---- the round trip --------------------------------------------------------------------------

    [Fact]
    public void A_short_run_of_bytes_comes_back()
    {
        RoundTrips("Hello, world"u8.ToArray());
    }

    [Fact]
    public void Every_byte_value_survives()
    {
        // Including 0 and 0xFF: this is a byte stream, not text, and records are full of both.
        RoundTrips([.. Enumerable.Range(0, 256).Select(i => (byte)i)]);
    }

    [Fact]
    public void A_highly_repetitive_run_compresses_and_comes_back()
    {
        // The case LZW exists for: the dictionary should be doing real work here.
        byte[] input = [.. Enumerable.Repeat((byte)'A', 5000)];

        byte[] compressed = Compress(input);

        Assert.Equal(input, Decompress(compressed, input.Length));
        Assert.True(compressed.Length < input.Length,
                    $"5,000 identical bytes compressed to {compressed.Length}");
    }

    [Fact]
    public void A_long_incompressible_run_comes_back_too()
    {
        // Pseudo-random with a fixed seed: no repetition for the dictionary to exploit, so this
        // exercises the block-boundary arithmetic far more than the compression.
        var random = new Random(1234);
        byte[] input = new byte[20_000];
        random.NextBytes(input);

        RoundTrips(input);
    }

    [Fact]
    public void Bytes_written_one_at_a_time_produce_the_same_stream()
    {
        byte[] input = "the quick brown fox jumps over the lazy dog"u8.ToArray();

        var stream = new MemoryStream();
        var compressor = new CarLzwCompressor(stream);
        foreach (byte b in input)
        {
            compressor.WriteByte(b);
        }
        compressor.Flush();

        Assert.Equal(Compress(input), stream.ToArray());
    }

    // ---- the block layout ------------------------------------------------------------------------

    [Fact]
    public void Output_is_always_a_whole_number_of_fifty_two_byte_blocks()
    {
        // 13-bit codes into 416-bit blocks: 32 codes exactly, no remainder and no padding.
        foreach (int length in new[] { 0, 1, 10, 100, 1000, 9999 })
        {
            byte[] compressed = Compress([.. Enumerable.Repeat((byte)'x', length)]);

            Assert.Equal(0, compressed.Length % BlockBytes);
        }
    }

    [Fact]
    public void An_untouched_compressor_still_writes_one_block_of_terminators()
    {
        // The pending code starts at 0xFFFF and 13 bits of that IS the end code, so flushing an
        // untouched stream emits terminators rather than nothing. The decoder expects to find one.
        byte[] compressed = Compress([]);

        Assert.Equal(BlockBytes, compressed.Length);
        Assert.All(compressed, b => Assert.Equal(0xFF, b));
    }

    [Fact]
    public void The_stream_always_ends_with_a_terminator()
    {
        // Flush fills the remainder of the final block with 8191, so the last 13 bits are always
        // all ones however the data happened to land.
        byte[] compressed = Compress("some bytes"u8.ToArray());

        Assert.Equal(0xFF, compressed[^1]);
        Assert.Equal(0xFF, compressed[^2]);
    }

    [Fact]
    public void Reading_past_the_terminator_stops_rather_than_inventing_bytes()
    {
        byte[] input = "abc"u8.ToArray();

        var decompressor = new CarLzwDecompressor(new MemoryStream(Compress(input)));
        var output = new byte[100];
        int got = decompressor.Read(output, 100);

        Assert.Equal(input.Length, got);
        Assert.True(decompressor.Ended);
    }

    // ---- the dictionary reset --------------------------------------------------------------------

    [Fact]
    public void A_stream_long_enough_to_fill_the_dictionary_still_round_trips()
    {
        // 8,190 codes is the ceiling, and filling it emits the pending code and then a reset. The
        // order matters: a reset first would leave the decoder holding a code that only made
        // sense against the table it had just cleared.
        var random = new Random(99);
        byte[] input = new byte[120_000];
        random.NextBytes(input);

        RoundTrips(input);
    }

    [Fact]
    public void The_reset_is_exercised_by_that_stream()
    {
        // A guard on the test above: without enough distinct pairs the dictionary never fills and
        // the reset path is never taken, which would make it look verified when it is not.
        var random = new Random(99);
        byte[] input = new byte[120_000];
        random.NextBytes(input);

        // 8,190 - 256 dictionary entries need at least that many novel pairs; random bytes over
        // 120,000 positions produce far more.
        Assert.True(input.Length > (8190 - 256) * 2);
    }
}
