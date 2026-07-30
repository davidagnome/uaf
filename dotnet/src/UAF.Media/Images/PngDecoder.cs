using System.Buffers.Binary;
using System.IO.Compression;

namespace UAF.Media;

/// <summary>
/// Decodes PNG to <see cref="Surface"/> pixels, reproducing what
/// <c>CDXImagePNG::GetImage</c> (<c>cdx/cdximagepng.cpp:44</c>) got out of libpng.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why hand-rolled, and what it costs.</b> <c>UAF.Media</c> is deliberately free of native
/// dependencies so it can be tested on a headless runner (see the project file), and a PNG decoder
/// is small once .NET hands you zlib as <see cref="ZLibStream"/>: a chunk walk, five row filters
/// and a pixel unpack. It also lets this file reproduce the engine's specific quirks exactly —
/// stripped alpha, the <c>gAMA</c>-versus-2.2 gamma convention below, and 16-bit chopping rather
/// than rescaling — none of which a general-purpose decoder does.
/// </para>
/// <para>
/// <c>SDL3_image</c> is <i>not</i> excluded for being platform-bound; it is not. The
/// <c>ppy.SDL3_image-CS</c> binding is published by the same author as the <c>ppy.SDL3-CS</c> this
/// port already uses, at matching versions, with native binaries for all 13 RIDs the core package
/// covers, under MIT over zlib. It would be a legitimate choice, and it covers JPG, PCX and TGA,
/// which this decoder does not and the C++ dispatch table did
/// (<c>cdx/cdximage.cpp:262-312</c>). The trade taken here is narrow: PNG is 1286 of the 1302
/// decodable art files in the reference designs and the only format the modern editor writes, so
/// the managed path covers what matters while keeping this assembly headless. The legacy formats
/// are the case for adding the dependency, and if they are wanted it belongs in
/// <c>UAF.Media.Sdl</c> alongside the other native code — not here.
/// </para>
/// <para>
/// The options genuinely ruled out are narrower: <c>System.Drawing.Common</c> throws on anything but
/// Windows, and ImageSharp's v3 split licence is the kind of question that already cost this port
/// once with the FFmpeg bindings.
/// </para>
/// <para>
/// <b>Alpha is discarded, by design.</b> The C++ calls <c>png_set_strip_alpha</c> and passes
/// <c>PNG_TRANSFORM_STRIP_ALPHA</c> (<c>cdximagepng.cpp:105,124</c>), and never reads <c>tRNS</c>.
/// Transparency in this engine comes from the colour key — the top-left pixel, see
/// <see cref="Surface.SetColorKeyFromTopLeft"/> — not from the image's alpha channel. So every
/// pixel here comes back opaque, and an RGBA source loses its alpha. Producing real alpha instead
/// would look like an improvement and would break keyed art, because a sprite's key pixel would
/// stop matching.
/// </para>
/// <para>
/// <b>Two libpng calls are deliberately not ported.</b> <c>png_set_bgr</c>
/// (<c>cdximagepng.cpp:106</c>) and the bottom-up row writes (<c>cdximagepng.cpp:238-244</c>, which
/// walk <c>bits</c> backwards) are both Windows-DIB plumbing, not image semantics: a 24bpp DIB
/// stores blue first, and a <c>BITMAPINFOHEADER</c> with positive <c>biHeight</c> is bottom-up.
/// The loader never calls <c>SetInverted</c>, and <c>m_IsInverted</c> defaults to
/// <c>FALSE</c> (<c>cdximagebase.cpp:55</c>), so <c>biHeight</c> stays positive
/// (<c>cdximagebase.cpp:120</c>) and both conventions cancel against the writes. This decoder
/// emits top-down ARGB directly. Porting either one literally would give a vertically flipped
/// picture with red and blue swapped — and porting both would flip it while looking correct in a
/// colour test.
/// </para>
/// <para>
/// <b>The dead <c>png_set_expand</c> calls.</b> <c>cdximagepng.cpp:130-134</c> asks libpng to
/// expand palette and sub-8-bit grey images, but it runs *after* <c>png_read_png</c> has already
/// decoded everything, so it does nothing. The C++ therefore keeps palette images paletted and
/// hands the PLTE to the DIB as <c>bmiColors</c> (<c>cdximagepng.cpp:206-216</c>). Expanding the
/// palette here reaches the same pixels, because a DIB palette lookup and a PLTE lookup are the
/// same operation.
/// </para>
/// </remarks>
public static class PngDecoder
{
    private static readonly byte[] Signature =
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// <c>screen_gamma</c> as the C++ hard-codes it (<c>cdximagepng.cpp:53</c>).
    /// </summary>
    private const double ScreenGamma = 2.2;

    /// <summary>
    /// The file gamma assumed when there is no usable <c>gAMA</c> chunk
    /// (<c>cdximagepng.cpp:113</c>). Note 0.45455 × 2.2 = 1.00001: the default is chosen to make
    /// gamma correction a no-op, which is why most art is untouched.
    /// </summary>
    private const double DefaultFileGamma = 0.45455;

    /// <summary>
    /// libpng's <c>PNG_GAMMA_THRESHOLD</c>. A composed gamma this close to 1.0 is treated as no
    /// correction at all, rather than building a table that rounds back to the identity.
    /// </summary>
    private const double GammaThreshold = 0.05;

    /// <summary>
    /// The signature test, matching <c>CDXImagePNG::Validate</c>'s <c>png_sig_cmp</c> over the
    /// first 8 bytes (<c>cdximagepng.cpp:21</c>).
    /// </summary>
    public static bool IsPng(ReadOnlySpan<byte> head) =>
        head.Length >= 8 && head[..8].SequenceEqual(Signature);

    /// <summary>Decodes a whole PNG file.</summary>
    /// <exception cref="InvalidDataException">The file is malformed.</exception>
    /// <exception cref="NotSupportedException">
    /// The file is interlaced. No file in any shipped design is: all 1312 PNGs across the reference
    /// designs declare interlace method 0. libpng handled Adam7 transparently, so this is a real
    /// gap rather than a difference — it is left explicit instead of guessed at, the same way the
    /// unported serialization branches are.
    /// </exception>
    public static DecodedImage Decode(ReadOnlySpan<byte> file)
    {
        if (!IsPng(file))
        {
            throw new InvalidDataException("not a PNG: signature mismatch");
        }

        var header = default(PngHeader);
        byte[]? palette = null;
        double fileGamma = 0;
        var compressed = new MemoryStream();
        bool sawHeader = false;

        // Chunk walk. CRCs are not verified: libpng would have rejected a bad one, but the IDAT
        // payload -- almost the whole file -- is covered by zlib's own Adler-32, which
        // ZLibStream does check. Corruption confined to a header chunk's CRC is the only case
        // this misses, and it is not worth a second pass over every byte.
        int pos = 8;
        while (pos + 8 <= file.Length)
        {
            uint rawLength = BinaryPrimitives.ReadUInt32BigEndian(file[pos..]);
            if (rawLength > int.MaxValue)
            {
                throw new InvalidDataException($"chunk length {rawLength} out of range");
            }

            int length = (int)rawLength;
            var type = file.Slice(pos + 4, 4);
            int dataAt = pos + 8;

            // The 4 trailing bytes are the CRC; a chunk that claims more than the file holds is
            // truncated, and reading it would walk off the end.
            if (length > file.Length - dataAt - 4)
            {
                throw new InvalidDataException(
                    $"chunk '{Name(type)}' claims {length} bytes past the end of the file");
            }

            var data = file.Slice(dataAt, length);

            if (Is(type, "IHDR"))
            {
                header = PngHeader.Parse(data);
                sawHeader = true;
            }
            else if (Is(type, "PLTE"))
            {
                palette = data.ToArray();
            }
            else if (Is(type, "IDAT"))
            {
                compressed.Write(data);
            }
            else if (Is(type, "gAMA") && length >= 4)
            {
                // Stored as the gamma times 100000. One shipped file (SomethingWild's
                // avz_outdoors.png) carries a zero here, which libpng rejects as a bad value and
                // ignores -- so a non-positive gamma has to fall through to the default, not be
                // used as an exponent.
                fileGamma = BinaryPrimitives.ReadUInt32BigEndian(data) / 100000.0;
            }
            else if (Is(type, "IEND"))
            {
                break;
            }

            pos = dataAt + length + 4;
        }

        if (!sawHeader)
        {
            throw new InvalidDataException("no IHDR chunk");
        }

        if (compressed.Length == 0)
        {
            throw new InvalidDataException("no IDAT data");
        }

        compressed.Position = 0;
        byte[] rows = Inflate(compressed, header);
        return ToArgb(rows, header, palette, BuildGammaTable(fileGamma));
    }

    /// <summary>
    /// Inflates the IDAT stream and undoes the per-row filters, returning tightly packed rows.
    /// </summary>
    private static byte[] Inflate(Stream compressed, PngHeader header)
    {
        int stride = header.Stride;
        var image = new byte[checked(stride * header.Height)];

        // The filter unit is whole bytes per pixel, rounded up -- so 1 for anything under 8bpp.
        int filterUnit = Math.Max(1, header.Channels * header.BitDepth / 8);

        var prior = new byte[stride];
        var current = new byte[stride];

        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        for (int y = 0; y < header.Height; y++)
        {
            int filter = inflater.ReadByte();
            if (filter < 0)
            {
                throw new InvalidDataException($"IDAT ended at row {y} of {header.Height}");
            }

            inflater.ReadExactly(current);
            Unfilter(filter, current, prior, filterUnit);
            current.CopyTo(image, y * stride);

            (prior, current) = (current, prior);
        }

        return image;
    }

    /// <summary>
    /// Reverses one row filter in place (PNG spec §9.2). <paramref name="prior"/> is the already
    /// reconstructed row above, all zeroes for the first row.
    /// </summary>
    /// <remarks>
    /// Every filter is defined on the *reconstructed* left neighbour, so these loops must read the
    /// bytes they are writing — which is why they run forwards and cannot be vectorised naively.
    /// Bytes before the first whole pixel treat the missing left neighbour as zero, which collapses
    /// Average to a halved upper byte and Paeth to a plain upper byte.
    /// </remarks>
    private static void Unfilter(int filter, Span<byte> row, ReadOnlySpan<byte> prior, int unit)
    {
        switch (filter)
        {
            case 0: // None
                break;

            case 1: // Sub
                for (int i = unit; i < row.Length; i++)
                {
                    row[i] += row[i - unit];
                }
                break;

            case 2: // Up
                for (int i = 0; i < row.Length; i++)
                {
                    row[i] += prior[i];
                }
                break;

            case 3: // Average
                for (int i = 0; i < unit; i++)
                {
                    row[i] += (byte)(prior[i] >> 1);
                }
                for (int i = unit; i < row.Length; i++)
                {
                    row[i] += (byte)((row[i - unit] + prior[i]) >> 1);
                }
                break;

            case 4: // Paeth
                for (int i = 0; i < unit; i++)
                {
                    row[i] += prior[i];
                }
                for (int i = unit; i < row.Length; i++)
                {
                    row[i] += Paeth(row[i - unit], prior[i], prior[i - unit]);
                }
                break;

            default:
                throw new InvalidDataException($"unknown row filter {filter}");
        }
    }

    /// <summary>The Paeth predictor: whichever of the three neighbours is closest to a + b - c.</summary>
    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        int estimate = left + above - upperLeft;
        int toLeft = Math.Abs(estimate - left);
        int toAbove = Math.Abs(estimate - above);
        int toUpperLeft = Math.Abs(estimate - upperLeft);

        // The tie-breaking order is normative -- left, then above, then upper-left.
        return toLeft <= toAbove && toLeft <= toUpperLeft ? left
             : toAbove <= toUpperLeft ? above
             : upperLeft;
    }

    /// <summary>Expands reconstructed rows to opaque ARGB8888.</summary>
    private static DecodedImage ToArgb(byte[] rows, PngHeader header, byte[]? palette,
                                      byte[]? gamma)
    {
        var pixels = new uint[checked(header.Width * header.Height)];
        int stride = header.Stride;
        int channels = header.Channels;
        int depth = header.BitDepth;

        // Sub-8-bit grey samples are scaled to the full range rather than left as small integers,
        // which is what libpng's expansion does. Palette indices must NOT be scaled.
        int greyMax = (1 << depth) - 1;

        for (int y = 0; y < header.Height; y++)
        {
            var row = rows.AsSpan(y * stride, stride);
            int outAt = y * header.Width;

            for (int x = 0; x < header.Width; x++)
            {
                byte r, g, b;

                switch (header.ColorType)
                {
                    case 0: // Greyscale
                        r = g = b = (byte)(Sample(row, x * channels, depth) * 255 / greyMax);
                        break;

                    case 4: // Greyscale + alpha; the alpha sample is read and dropped
                        r = g = b = (byte)(Sample(row, x * channels, depth) * 255 / greyMax);
                        break;

                    case 2: // Truecolour
                    case 6: // Truecolour + alpha; likewise dropped
                        r = (byte)Sample(row, (x * channels) + 0, depth);
                        g = (byte)Sample(row, (x * channels) + 1, depth);
                        b = (byte)Sample(row, (x * channels) + 2, depth);
                        break;

                    case 3: // Palette
                    {
                        int index = Sample(row, x, depth);
                        if (palette is null)
                        {
                            throw new InvalidDataException("palette image has no PLTE chunk");
                        }
                        if ((index * 3) + 2 >= palette.Length)
                        {
                            throw new InvalidDataException(
                                $"palette index {index} exceeds the {palette.Length / 3}-entry PLTE");
                        }
                        r = palette[index * 3];
                        g = palette[(index * 3) + 1];
                        b = palette[(index * 3) + 2];
                        break;
                    }

                    default:
                        throw new InvalidDataException($"unknown colour type {header.ColorType}");
                }

                if (gamma is not null)
                {
                    r = gamma[r];
                    g = gamma[g];
                    b = gamma[b];
                }

                pixels[outAt + x] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            }
        }

        return new DecodedImage(header.Width, header.Height, pixels);
    }

    /// <summary>
    /// Reads the <paramref name="index"/>th sample of a row at the given bit depth.
    /// </summary>
    /// <remarks>
    /// 16-bit samples keep the high byte and drop the low one. That is a truncation, not a rescale,
    /// because <c>png_set_strip_16</c> chops — libpng's rescaling variant is the separate
    /// <c>png_set_scale_16</c>, which the C++ does not call. No shipped design uses 16-bit art, so
    /// this path is exercised only by synthetic tests.
    /// </remarks>
    private static int Sample(ReadOnlySpan<byte> row, int index, int bitDepth) => bitDepth switch
    {
        8 => row[index],
        16 => row[index * 2],
        _ => (row[index * bitDepth / 8] >> (8 - bitDepth - (index * bitDepth % 8)))
             & ((1 << bitDepth) - 1),
    };

    /// <summary>
    /// Builds the 256-entry gamma table, or null when the correction would be the identity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exponent is <c>1 / (file_gamma × screen_gamma)</c>, which is libpng's
    /// <c>png_reciprocal2(colorspace.gamma, screen_gamma)</c>, and also what the physics gives: a
    /// sample encodes intensity as <c>s^(1/file_gamma)</c>, and a display with exponent
    /// <c>screen_gamma</c> needs <c>s^(1/(file_gamma × screen_gamma))</c> to reproduce it. Getting
    /// this the wrong way up is invisible in the common case — both directions collapse to the
    /// identity when the product is 1 — and only shows as wrongly lightened or darkened art on the
    /// handful of files that declare something else.
    /// </para>
    /// <para>
    /// Across the reference designs this fires for 10 files out of 1312, all of them
    /// <c>SomethingWild</c> art declaring gamma 0.55531, and all of them truecolour. Nothing
    /// paletted ever needs it, which is convenient: whether libpng gamma-corrects a palette in
    /// place or corrects the expanded pixels is a question this port never has to answer.
    /// </para>
    /// </remarks>
    private static byte[]? BuildGammaTable(double fileGamma)
    {
        if (fileGamma <= 0)
        {
            fileGamma = DefaultFileGamma;
        }

        double product = fileGamma * ScreenGamma;
        if (Math.Abs(product - 1.0) <= GammaThreshold)
        {
            return null;
        }

        double exponent = 1.0 / product;
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            table[i] = (byte)Math.Clamp(Math.Round(255.0 * Math.Pow(i / 255.0, exponent)), 0, 255);
        }
        return table;
    }

    private static bool Is(ReadOnlySpan<byte> type, string name) =>
        type[0] == name[0] && type[1] == name[1] && type[2] == name[2] && type[3] == name[3];

    private static string Name(ReadOnlySpan<byte> type)
    {
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            chars[i] = (char)type[i];
        }
        return new string(chars);
    }

    /// <summary>The IHDR fields, plus the two sizes derived from them.</summary>
    private readonly record struct PngHeader(
        int Width, int Height, int BitDepth, int ColorType, int Channels)
    {
        /// <summary>Bytes per reconstructed row, rounded up for sub-8-bit depths.</summary>
        public int Stride => ((Width * Channels * BitDepth) + 7) / 8;

        public static PngHeader Parse(ReadOnlySpan<byte> data)
        {
            if (data.Length < 13)
            {
                throw new InvalidDataException($"IHDR is {data.Length} bytes, expected 13");
            }

            uint width = BinaryPrimitives.ReadUInt32BigEndian(data);
            uint height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
            int bitDepth = data[8];
            int colorType = data[9];
            int compression = data[10];
            int filter = data[11];
            int interlace = data[12];

            if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
            {
                throw new InvalidDataException($"implausible dimensions {width}x{height}");
            }

            if (compression != 0)
            {
                throw new InvalidDataException($"unknown compression method {compression}");
            }

            if (filter != 0)
            {
                throw new InvalidDataException($"unknown filter method {filter}");
            }

            if (interlace != 0)
            {
                throw new NotSupportedException(
                    "interlaced (Adam7) PNG is not supported; no shipped design uses it");
            }

            int channels = colorType switch
            {
                0 => 1,   // greyscale
                2 => 3,   // truecolour
                3 => 1,   // palette index
                4 => 2,   // greyscale + alpha
                6 => 4,   // truecolour + alpha
                _ => throw new InvalidDataException($"unknown colour type {colorType}"),
            };

            // Per PNG spec table 11.1: which depths each colour type allows.
            bool depthOk = colorType switch
            {
                0 => bitDepth is 1 or 2 or 4 or 8 or 16,
                3 => bitDepth is 1 or 2 or 4 or 8,
                _ => bitDepth is 8 or 16,
            };

            if (!depthOk)
            {
                throw new InvalidDataException(
                    $"bit depth {bitDepth} is not legal for colour type {colorType}");
            }

            return new PngHeader((int)width, (int)height, bitDepth, colorType, channels);
        }
    }
}
