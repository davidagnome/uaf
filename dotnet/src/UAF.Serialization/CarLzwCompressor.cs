namespace UAF.Serialization;

/// <summary>
/// The LZW encoder <see cref="CarLzwDecompressor"/> is the inverse of
/// (<c>CAR::compress</c>, <c>class.cpp:12155</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The last wholly unexplored part of the format.</b> Nothing could write a compressed archive
/// until this existed, which is why byte-identity with a shipped design was out of reach and why
/// Phase 5 could not begin.
/// </para>
/// <para>
/// Same shape as the decoder: 13-bit codes packed into fixed 52-byte blocks, 8190 resets the
/// dictionary and 8191 ends the stream. What the encoder adds is the dictionary itself and the
/// termination rule.
/// </para>
/// <para>
/// <b>The bit packing is an OR into a zeroed buffer, not a write.</b> The reference does an
/// unaligned 32-bit <c>|=</c> at <c>buffer + (index &gt;&gt; 3)</c> and relies on the buffer being
/// zeroed after every flush. A code therefore spills into the following bytes and the next code
/// ORs on top of it. Writing rather than OR-ing would clear the low bits of a code that straddles
/// a byte boundary — which is most of them, since 13 does not divide 8.
/// </para>
/// <para>
/// The reference's OR runs two bytes past the 52-byte block on the final code of each block, the
/// same over-run its decoder has. The buffer here is padded so the spill lands somewhere real; it
/// is always zero and never written out.
/// </para>
/// </remarks>
public sealed class CarLzwCompressor
{
    private const int CodeBits = 13;
    private const int BlockBytes = 52;
    private const int BlockBits = BlockBytes * 8;   // 416, exactly 32 codes
    private const ushort ResetCode = 8190;
    private const ushort EndCode = 8191;

    /// <summary>No pending code. Also the initial value of <c>m_w</c>.</summary>
    private const ushort NoPendingCode = 0xFFFF;

    private readonly Stream _stream;

    // Two spare bytes for the reference's over-run; see the class remarks.
    private readonly byte[] _block = new byte[BlockBytes + 4];
    private int _bufferIndex;

    private readonly CodeTable _codes = new();
    private ushort _pending = NoPendingCode;        // m_w

    public CarLzwCompressor(Stream stream) =>
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    /// <summary>Compresses bytes into the stream.</summary>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        foreach (byte c in bytes)
        {
            WriteByte(c);
        }
    }

    /// <summary>Compresses one byte (<c>CAR::compress</c>'s loop body).</summary>
    /// <remarks>
    /// <b>The full-table check happens before the byte is looked at</b>, and it emits the pending
    /// code <i>before</i> the reset code. Emitting the reset first would leave the decoder with a
    /// cleared table and a code that only made sense against the old one.
    /// </remarks>
    public void WriteByte(byte c)
    {
        if (_codes.IsFull)
        {
            Emit(_pending);
            _pending = NoPendingCode;
            _codes.Clear();
            Emit(ResetCode);
        }

        if (_pending == NoPendingCode)
        {
            // Single characters are in the code list by definition -- 0..255 are never stored.
            _pending = c;
            return;
        }

        uint key = ((uint)_pending << 8) | c;

        if (_codes.Find(key) is { } existing)
        {
            _pending = existing;
            return;
        }

        _codes.Add(key);
        Emit(_pending);
        _pending = c;
    }

    /// <summary>
    /// Ends the stream (<c>CAR::Flush</c>, <c>class.cpp:11626</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pending code goes out, then <b>the rest of the block is filled with end codes</b> and
    /// written — so the output is always a whole number of 52-byte blocks and always ends with at
    /// least one 8191.
    /// </para>
    /// <para>
    /// <b>A stream that was never written still emits one code.</b> The pending value starts at
    /// <c>0xFFFF</c>, and 13 bits of that is 8191 — the end code. So flushing an untouched
    /// compressor writes a block of nothing but terminators rather than an empty file, which is
    /// what the reference does and what its decoder expects to find.
    /// </para>
    /// </remarks>
    public void Flush()
    {
        Emit(_pending);

        do
        {
            EmitBits(EndCode);
        }
        while (_bufferIndex != 0);

        WriteBlock();
    }

    /// <summary>Packs one code and writes the block when it fills.</summary>
    private void Emit(ushort code)
    {
        EmitBits(code);

        if (_bufferIndex == 0)
        {
            WriteBlock();
        }
    }

    /// <summary>The unaligned OR, and the index step.</summary>
    private void EmitBits(ushort code)
    {
        int at = _bufferIndex >> 3;
        int shift = _bufferIndex & 7;
        uint packed = (uint)((code & 0x1FFF) << shift);

        // Only 13 bits are meaningful, but the reference ORs the whole 16-bit value -- for 8191
        // and 8190 the high bits are already clear, and for a real code they are too, since no
        // code exceeds 8189.
        _block[at] |= (byte)packed;
        _block[at + 1] |= (byte)(packed >> 8);
        _block[at + 2] |= (byte)(packed >> 16);

        _bufferIndex = (_bufferIndex + CodeBits) % BlockBits;
    }

    private void WriteBlock()
    {
        _stream.Write(_block, 0, BlockBytes);
        Array.Clear(_block);
    }

    /// <summary>
    /// The dictionary (<c>CAR::CODES</c>, <c>class.h:512</c>).
    /// </summary>
    /// <remarks>
    /// <b>Open addressing with double hashing, and the increment is the key itself.</b> The step
    /// is <c>key % 9973</c> — the same value as the initial bucket — forced to 1 when that is
    /// zero. A table of 9973 buckets for 8190 possible codes, so it never fills.
    /// <para>
    /// Bucket 0 means empty, which is why no code below 256 is ever stored: <c>Clear</c> starts
    /// the counter at 256 and fills the first 256 code slots with <c>0xFFFFFFFF</c>, a value no
    /// 24-bit key can equal.
    /// </para>
    /// </remarks>
    private sealed class CodeTable
    {
        private const int Buckets = 9973;
        private const ushort FirstCode = 256;
        private const ushort FullAt = 8190;

        private readonly ushort[] _hash = new ushort[Buckets];
        private readonly uint[] _keys = new uint[8192];
        private ushort _next = FirstCode;

        public CodeTable() => Clear();

        /// <summary>True once the next code would collide with the reset code.</summary>
        public bool IsFull => _next == FullAt;

        public void Clear()
        {
            Array.Clear(_hash);
            _keys.AsSpan(0, FirstCode).Fill(0xFFFFFFFF);
            _next = FirstCode;
        }

        public ushort? Find(uint key)
        {
            int bucket = (int)(key % Buckets);
            int step = bucket == 0 ? 1 : bucket;

            while (_hash[bucket] != 0)
            {
                if (_keys[_hash[bucket]] == key)
                {
                    return _hash[bucket];
                }

                bucket = (bucket + step) % Buckets;
            }

            return null;
        }

        public ushort Add(uint key)
        {
            int bucket = (int)(key % Buckets);
            int step = bucket == 0 ? 1 : bucket;

            while (_hash[bucket] != 0)
            {
                bucket = (bucket + step) % Buckets;
            }

            _hash[bucket] = _next;
            _keys[_next] = key;
            return _next++;
        }
    }
}
