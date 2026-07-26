namespace UAF.Serialization;

/// <summary>
/// The LZW layer that <c>CAR</c> wraps around <c>CArchive</c> (<c>class.cpp:12215</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> a standard LZW stream and cannot be swapped for a library implementation.
/// Its specifics, all load-bearing:
/// </para>
/// <list type="bullet">
///   <item>Codes are <b>13 bits</b>, packed little-endian-ish into fixed <b>52-byte blocks</b>
///     (416 bits = exactly 32 codes per block, no remainder and no padding).</item>
///   <item>Code <b>8190</b> resets the dictionary; code <b>8191</b> terminates the stream.</item>
///   <item>The dictionary starts at 256 and is <b>never cleared implicitly</b> — only on 8190.</item>
///   <item>Expansion is stack-based and the stack is drained in reverse, one output byte per
///     call iteration, so decoding state persists across reads.</item>
/// </list>
/// <para>
/// The C++ extracts each code with an unaligned 4-byte read at <c>buffer + (bufferIndex &gt;&gt; 3)</c>,
/// which for the final code of a block reads two bytes past the 52-byte buffer. That is undefined
/// behaviour that happens to work because adjacent members follow it in memory. Here the block is
/// zero-padded instead: the last code starts at bit 403, needs bits 403..415, and those live
/// entirely within bytes 50-51, so the padding is never actually consumed and the output is
/// identical.
/// </para>
/// </remarks>
public sealed class CarLzwDecompressor
{
    private const int CodeBits = 13;
    private const int BlockBytes = 52;
    private const int BlockBits = BlockBytes * 8;   // 416
    private const ushort ResetCode = 8190;
    private const ushort EndCode = 8191;
    private const ushort NoPreviousCode = 0xFFFF;
    private const int MaxCodes = 8192;
    private const int StackCapacity = 1000;

    private readonly Stream _stream;

    // Bit-packed input block. Two extra bytes so the widening read below can never
    // walk off the end; see the remarks about the C++ over-read.
    private readonly byte[] _block = new byte[BlockBytes + 2];
    private int _bufferIndex;

    private readonly ushort[] _prefix = new ushort[MaxCodes];
    private readonly byte[] _postfix = new byte[MaxCodes];
    private readonly byte[] _stack = new byte[StackCapacity];
    private int _stackLength;

    private ushort _previousCode = NoPreviousCode;   // m_OC
    private byte _lastByte;                          // m_C
    private int _nextCode = 256;                     // m_numCode
    private bool _ended;

    public CarLzwDecompressor(Stream stream) =>
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    /// <summary>
    /// True once code 8191 was read. The C++ returns early on that code, leaving the remainder
    /// of the caller's buffer untouched rather than reporting an error.
    /// </summary>
    public bool Ended => _ended;

    private bool FillBlock()
    {
        Array.Clear(_block);
        int read = 0;
        while (read < BlockBytes)
        {
            int n = _stream.Read(_block, read, BlockBytes - read);
            if (n == 0)
            {
                // Matches `if (Read(...) != sizeof(m_buffer)) return;` -- a short read stops
                // decoding silently rather than throwing.
                return false;
            }
            read += n;
        }
        return true;
    }

    private ushort NextCode()
    {
        int byteOffset = _bufferIndex >> 3;
        int bitOffset = _bufferIndex & 7;
        uint window = (uint)(_block[byteOffset]
                             | (_block[byteOffset + 1] << 8)
                             | (_block[byteOffset + 2] << 16)
                             | (_block[byteOffset + 3] << 24));
        ushort code = (ushort)((window >> bitOffset) & 0x1FFF);
        _bufferIndex = (_bufferIndex + CodeBits) % BlockBits;
        return code;
    }

    /// <summary>
    /// Decompresses exactly <paramref name="count"/> bytes into <paramref name="destination"/>,
    /// continuing from wherever the previous call stopped.
    /// </summary>
    /// <returns>The number of bytes actually produced; less than <paramref name="count"/> only
    /// at end-of-stream.</returns>
    public int Read(Span<byte> destination, int count)
    {
        int produced = 0;
        for (; produced < count; produced++)
        {
            if (_stackLength != 0)
            {
                destination[produced] = _stack[--_stackLength];
                continue;
            }

            if (_ended)
            {
                return produced;
            }

            if (_bufferIndex == 0 && !FillBlock())
            {
                return produced;
            }

            ushort code = NextCode();

            while (code == ResetCode)
            {
                _previousCode = NoPreviousCode;
                _nextCode = 256;
                if (_bufferIndex == 0 && !FillBlock())
                {
                    return produced;
                }
                code = NextCode();
            }

            if (_previousCode == NoPreviousCode)
            {
                // First code after start or reset is emitted literally.
                _previousCode = code;
                _lastByte = (byte)code;
                destination[produced] = (byte)code;
                continue;
            }

            if (code > 255)
            {
                int expand;
                if (code >= _nextCode)
                {
                    if (code == EndCode)
                    {
                        _ended = true;
                        return produced;
                    }
                    // KwKwK case: the code is not yet in the dictionary, so it expands to
                    // previous + previous's first byte.
                    _stack[_stackLength++] = _lastByte;
                    expand = _previousCode;
                }
                else
                {
                    expand = code;
                }

                while (expand > 255)
                {
                    _stack[_stackLength++] = _postfix[expand];
                    expand = _prefix[expand];
                }
                _stack[_stackLength++] = (byte)expand;
                _lastByte = _stack[_stackLength - 1];
            }
            else
            {
                _lastByte = (byte)code;
                _stack[_stackLength++] = _lastByte;
            }

            // The dictionary grows unconditionally, with no guard against passing 8190.
            // Reproduced as-is: a guard here would diverge from the reference decoder.
            _prefix[_nextCode] = _previousCode;
            _postfix[_nextCode] = _lastByte;
            _nextCode++;

            _previousCode = code;
            destination[produced] = _stack[--_stackLength];
        }

        return produced;
    }

    /// <summary>Decompresses <paramref name="count"/> bytes into a new array.</summary>
    public byte[] ReadBytes(int count)
    {
        byte[] buffer = new byte[count];
        int n = Read(buffer, count);
        return n == count ? buffer : buffer[..n];
    }
}
