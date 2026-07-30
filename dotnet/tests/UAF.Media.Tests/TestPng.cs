using System.Buffers.Binary;
using System.IO.Compression;

namespace UAF.Media.Tests;

/// <summary>
/// Authors PNG files in-test, so the decoder can be driven down paths no shipped design uses.
/// </summary>
/// <remarks>
/// The reference corpus is 1286 truecolour files, 14 truecolour+alpha and 12 palette — nothing
/// greyscale, nothing 16-bit, nothing under 8 bits per sample, and only whichever row filters the
/// designers' tools happened to emit. Those branches exist because libpng handled them, so they
/// need coverage from somewhere, and authoring the bytes is the same approach the audio tests take
/// for WAV and MIDI.
/// </remarks>
internal static class TestPng
{
    private static readonly byte[] Signature =
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Builds a PNG from already-packed rows — <paramref name="rows"/> holds one entry per image
    /// row, each already in the sample layout the colour type and bit depth imply.
    /// </summary>
    /// <param name="filter">
    /// The row filter to encode with. Every filter is applied here in its forward direction, so a
    /// round trip through the decoder proves the reverse.
    /// </param>
    public static byte[] Build(int width, int height, int bitDepth, int colorType,
                               IReadOnlyList<byte[]> rows, byte[]? palette = null,
                               int filter = 0, uint? gamma = null, bool interlaced = false)
    {
        var file = new MemoryStream();
        file.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = (byte)bitDepth;
        ihdr[9] = (byte)colorType;
        ihdr[10] = 0;                                   // deflate
        ihdr[11] = 0;                                   // adaptive filtering
        ihdr[12] = (byte)(interlaced ? 1 : 0);
        WriteChunk(file, "IHDR", ihdr);

        if (gamma is not null)
        {
            Span<byte> gama = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(gama, gamma.Value);
            WriteChunk(file, "gAMA", gama);
        }

        if (palette is not null)
        {
            WriteChunk(file, "PLTE", palette);
        }

        int unit = Math.Max(1, FilterUnit(bitDepth, colorType));
        var scanlines = new MemoryStream();
        var prior = new byte[rows[0].Length];
        foreach (byte[] row in rows)
        {
            scanlines.WriteByte((byte)filter);
            scanlines.Write(Filter(filter, row, prior, unit));
            prior = row;
        }

        var deflated = new MemoryStream();
        using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(scanlines.ToArray());
        }

        WriteChunk(file, "IDAT", deflated.ToArray());
        WriteChunk(file, "IEND", []);
        return file.ToArray();
    }

    /// <summary>A solid-colour truecolour image, the simplest thing that decodes.</summary>
    public static byte[] Solid(int width, int height, byte r, byte g, byte b, int filter = 0,
                               uint? gamma = null)
    {
        var rows = new List<byte[]>();
        for (int y = 0; y < height; y++)
        {
            var row = new byte[width * 3];
            for (int x = 0; x < width; x++)
            {
                row[x * 3] = r;
                row[(x * 3) + 1] = g;
                row[(x * 3) + 2] = b;
            }
            rows.Add(row);
        }
        return Build(width, height, 8, 2, rows, filter: filter, gamma: gamma);
    }

    private static int FilterUnit(int bitDepth, int colorType)
    {
        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 1 };
        return channels * bitDepth / 8;
    }

    /// <summary>Applies a row filter in the forward direction.</summary>
    private static byte[] Filter(int filter, byte[] row, byte[] prior, int unit)
    {
        var output = new byte[row.Length];
        for (int i = 0; i < row.Length; i++)
        {
            byte left = i >= unit ? row[i - unit] : (byte)0;
            byte above = prior[i];
            byte upperLeft = i >= unit ? prior[i - unit] : (byte)0;

            output[i] = filter switch
            {
                0 => row[i],
                1 => (byte)(row[i] - left),
                2 => (byte)(row[i] - above),
                3 => (byte)(row[i] - ((left + above) >> 1)),
                4 => (byte)(row[i] - Paeth(left, above, upperLeft)),
                _ => throw new ArgumentOutOfRangeException(nameof(filter)),
            };
        }
        return output;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        var typeBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            typeBytes[i] = (byte)type[i];
        }
        stream.Write(typeBytes);
        stream.Write(data);

        // The decoder does not verify CRCs, but writing real ones keeps these fixtures loadable by
        // any other tool -- which matters the moment one of them needs eyeballing.
        uint crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in type)
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }
        foreach (byte b in data)
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }
        return c ^ 0xFFFFFFFF;
    }
}
