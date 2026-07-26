namespace UAF.Serialization.Tests;

/// <summary>
/// Mechanics tests for <see cref="CarLzwDecompressor"/> driven by hand-built code streams.
/// </summary>
/// <remarks>
/// <para>
/// These do <b>not</b> prove compatibility with the C++ encoder — that needs a tier-3 fixture,
/// which no design in the repository provides (see docs/PORTING-PLAN.md section 3.2). What they
/// do prove is that the bit-packing, block boundaries, reset/end handling and dictionary growth
/// behave as the algorithm at <c>class.cpp:12215</c> specifies. Without them the decoder is
/// entirely untested and a transposed shift or an off-by-one in the 416-bit wrap would be
/// invisible.
/// </para>
/// <para>
/// Codes are 13 bits packed little-endian-by-bit-offset into fixed 52-byte blocks: bit
/// <c>i*13</c> of the block holds code <c>i</c>, exactly 32 codes per block with no remainder.
/// </para>
/// </remarks>
public class CarLzwDecompressorTests
{
    private const int CodeBits = 13;
    private const int BlockBytes = 52;
    private const int CodesPerBlock = 32;   // 52 * 8 / 13

    /// <summary>Packs codes into 52-byte blocks the way <c>CAR::compress</c> lays them out.</summary>
    private static byte[] PackBlocks(params ushort[] codes)
    {
        int blocks = (codes.Length + CodesPerBlock - 1) / CodesPerBlock;
        byte[] output = new byte[blocks * BlockBytes];
        for (int i = 0; i < codes.Length; i++)
        {
            int blockIndex = i / CodesPerBlock;
            int bitOffset = (i % CodesPerBlock) * CodeBits;
            int baseByte = blockIndex * BlockBytes + (bitOffset >> 3);
            int shift = bitOffset & 7;
            uint value = (uint)(codes[i] & 0x1FFF) << shift;
            // A 13-bit field at any shift 0..7 spans at most 3 bytes.
            output[baseByte] |= (byte)(value & 0xFF);
            output[baseByte + 1] |= (byte)((value >> 8) & 0xFF);
            if ((shift + CodeBits) > 16)
            {
                output[baseByte + 2] |= (byte)((value >> 16) & 0xFF);
            }
        }
        return output;
    }

    private static CarLzwDecompressor Decoder(params ushort[] codes) =>
        new(new MemoryStream(PackBlocks(codes)));

    [Fact]
    public void Literal_codes_decode_to_themselves()
    {
        // Codes below 256 are literals. The first code after a start/reset is emitted directly.
        var decoder = Decoder(0x41, 0x42, 0x43, 0x44);
        byte[] output = decoder.ReadBytes(4);
        Assert.Equal(new byte[] { 0x41, 0x42, 0x43, 0x44 }, output);
    }

    [Fact]
    public void Bit_packing_survives_every_shift_within_a_block()
    {
        // 32 codes exercise all eight bit-offsets (13*i mod 8 cycles through 0..7), so a wrong
        // shift or a missing third byte in the widening read shows up here.
        ushort[] codes = new ushort[CodesPerBlock];
        for (int i = 0; i < codes.Length; i++)
        {
            codes[i] = (ushort)(i + 1);   // stay under 256 so every code is a literal
        }

        byte[] output = Decoder(codes).ReadBytes(codes.Length);

        Assert.Equal(codes.Select(c => (byte)c).ToArray(), output);
    }

    [Fact]
    public void Decoding_continues_across_a_block_boundary()
    {
        // 40 codes spans two 52-byte blocks; the decoder must refill at exactly code 32, when
        // its bit index wraps to 0.
        ushort[] codes = Enumerable.Range(1, 40).Select(i => (ushort)i).ToArray();
        byte[] output = Decoder(codes).ReadBytes(40);
        Assert.Equal(codes.Select(c => (byte)c).ToArray(), output);
    }

    [Fact]
    public void End_code_8191_stops_decoding_and_leaves_the_rest_untouched()
    {
        // The C++ returns early on 8191 rather than reporting an error, so the caller gets a
        // short read. Asking for more than the stream provides must not throw.
        var decoder = Decoder(0x41, 0x42, 8191, 0x43);
        byte[] output = decoder.ReadBytes(10);

        Assert.Equal(new byte[] { 0x41, 0x42 }, output);
        Assert.True(decoder.Ended);
    }

    [Fact]
    public void Reset_code_8190_clears_the_dictionary_and_is_not_emitted()
    {
        // 8190 resets and is consumed without producing output; the code after it is then treated
        // as the first code again (emitted literally).
        var decoder = Decoder(0x41, 8190, 0x42, 0x43);
        byte[] output = decoder.ReadBytes(3);
        Assert.Equal(new byte[] { 0x41, 0x42, 0x43 }, output);
    }

    [Fact]
    public void Dictionary_entry_256_expands_to_the_first_two_literals()
    {
        // After literals A,B the encoder has defined 256 = "AB". Referencing 256 must expand to
        // both bytes in order -- this exercises the prefix/postfix walk and the reversed stack
        // drain, which is where a naive port gets the byte order backwards.
        var decoder = Decoder(0x41, 0x42, 256);
        byte[] output = decoder.ReadBytes(4);
        Assert.Equal(new byte[] { 0x41, 0x42, 0x41, 0x42 }, output);
    }

    [Fact]
    public void KwKwK_case_resolves_a_code_that_is_not_yet_in_the_dictionary()
    {
        // The classic LZW edge case: a code equal to the next entry to be assigned, referenced
        // before it exists. Expected output derived by hand-tracing class.cpp:12215, not by
        // running this implementation:
        //
        //   'A'  -> first code, emitted literally.            OC=A
        //   'B'  -> literal.  defines 256="AB" (prefix=A, postfix=B).  OC=B   => "B"
        //   256  -> in dictionary; expands via prefix/postfix to "AB".
        //           defines 257 (prefix=B, postfix=A).        OC=256  => "AB"
        //   258  -> NOT yet defined (numCode==258), so the KwKwK branch applies:
        //           push C ('A'), then expand OC(256)="AB"  => stack "A","B","A" -> "ABA"
        //           defines 258.                             OC=258  => "ABA"
        //
        // Concatenated: A + B + AB + ABA == "ABABABA", seven bytes.
        var decoder = Decoder(0x41, 0x42, 256, 258);
        byte[] output = decoder.ReadBytes(7);

        Assert.Equal("ABABABA", System.Text.Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void Short_input_stops_cleanly_rather_than_throwing()
    {
        // `if (Read(...) != sizeof(m_buffer)) return;` -- a truncated final block ends decoding
        // silently. Truncated files must not crash the reader.
        var decoder = new CarLzwDecompressor(new MemoryStream(new byte[10]));
        byte[] output = decoder.ReadBytes(16);
        Assert.Empty(output);
    }
}
