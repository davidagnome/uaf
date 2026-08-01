using UAF.Media.Sdl;

namespace UAF.Media.Tests;

/// <summary>
/// Drives <see cref="SdlImageDecoder"/> against the legacy art in the reference designs.
/// </summary>
/// <remarks>
/// <para>
/// These are the formats <c>UAF.Media</c> does not decode itself: 8 JPEGs across two third-party
/// designs, 5 PCX from the 1993 DOS original, and 3 BMPs. Small, but they are the whole reason the
/// SDL3_image dependency exists, so they are checked against an independent decode rather than just
/// "it returned something".
/// </para>
/// <para>
/// The art is gitignored, so the corpus tests return early when it is absent. The availability and
/// round-trip tests do not — they author their own bytes and run anywhere.
/// </para>
/// </remarks>
public class SdlImageDecoderTests
{
    private sealed record LegacyEntry(string RelativePath, ImageFormat Format, int Width,
                                      int Height, string Sha256, double[] Mean,
                                      double[] QuadrantMean);

    /// <summary>
    /// The reference art corpus, or null when this checkout does not carry it.
    /// </summary>
    /// <remarks>
    /// The probe is a file the manifest names rather than the <c>reference/</c> directory, which
    /// the .NET workflow creates itself for the tier-3 data fixture. See the same guard in
    /// <see cref="PngOracleTests"/>.
    /// </remarks>
    private static string? CorpusRoot(IReadOnlyList<LegacyEntry> oracle)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string root = Path.Combine(dir.FullName, "reference");
        return File.Exists(Resolve(root, oracle[0])) ? root : null;
    }

    private static string Resolve(string root, LegacyEntry entry) =>
        Path.Combine(root, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static List<LegacyEntry> LoadOracle()
    {
        var entries = new List<LegacyEntry>();
        foreach (string line in File.ReadLines(TestPaths.Asset("legacy-art-oracle.txt")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('|');
            entries.Add(new LegacyEntry(
                f[0], Enum.Parse<ImageFormat>(f[1]), int.Parse(f[2]), int.Parse(f[3]), f[4],
                f[5].Split(',').Select(double.Parse).ToArray(),
                f[6].Split(',').Select(double.Parse).ToArray()));
        }
        return entries;
    }

    private static string Digest(DecodedImage image)
    {
        var rgb = new byte[image.Pixels.Length * 3];
        for (int i = 0; i < image.Pixels.Length; i++)
        {
            uint p = image.Pixels[i];
            rgb[i * 3] = (byte)(p >> 16);
            rgb[(i * 3) + 1] = (byte)(p >> 8);
            rgb[(i * 3) + 2] = (byte)p;
        }
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rgb));
    }

    /// <summary>Per-channel means over the whole image, or over its top-left quadrant.</summary>
    private static double[] Means(DecodedImage image, bool quadrantOnly)
    {
        int width = quadrantOnly ? Math.Max(1, image.Width / 2) : image.Width;
        int height = quadrantOnly ? Math.Max(1, image.Height / 2) : image.Height;

        double r = 0, g = 0, b = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint p = image.Pixels[(y * image.Width) + x];
                r += (byte)(p >> 16);
                g += (byte)(p >> 8);
                b += (byte)p;
            }
        }

        double count = (double)width * height;
        return [r / count, g / count, b / count];
    }

    [Fact]
    public void The_native_decoder_reports_itself_available()
    {
        var decoder = new SdlImageDecoder();

        // If this fails the native library did not load, and every other test here is vacuous.
        Assert.True(decoder.IsAvailable, decoder.UnavailableReason ?? "no reason given");
        Assert.Null(decoder.UnavailableReason);
    }

    [Fact]
    public void Png_is_left_to_the_managed_decoder()
    {
        var decoder = new SdlImageDecoder();

        // The deliberate abstention. SDL3_image decodes PNG perfectly well, but the managed path
        // reproduces the engine's stripped alpha and gamma convention and is verified against
        // libpng on 1312 files; routing PNG here would change all of them for nothing.
        Assert.False(decoder.CanDecode(ImageFormat.Png));

        Assert.True(decoder.CanDecode(ImageFormat.Jpg));
        Assert.True(decoder.CanDecode(ImageFormat.Pcx));
        Assert.True(decoder.CanDecode(ImageFormat.Bmp));
        Assert.True(decoder.CanDecode(ImageFormat.Tga));

        // SDL3_image has no PSD loader, unlike the C++'s cdximagepsd.cpp.
        Assert.False(decoder.CanDecode(ImageFormat.Psd));
    }

    [Fact]
    public void A_composed_loader_routes_each_format_to_the_right_decoder()
    {
        var loader = new ImageLoader(new SdlImageDecoder());

        // PNG through the managed path...
        var png = loader.Decode(TestPng.Solid(2, 2, 0x10, 0x20, 0x30));
        Assert.Equal(0xFF102030u, png.Pixels[0]);

        // ...and BMP through SDL, from the same call.
        var bmp = loader.Decode(OnePixelBmp(0x40, 0x50, 0x60));
        Assert.Equal(1, bmp.Width);
        Assert.Equal(0xFF405060u, bmp.Pixels[0]);
    }

    [Fact]
    public void Bmp_channels_are_not_swapped()
    {
        // The single most likely native-interop bug: BMP stores BGR, SDL_PIXELFORMAT_ARGB8888 is
        // host-order ARGB, and getting the conversion wrong yields a plausible-looking image with
        // red and blue exchanged. An asymmetric colour catches it; grey would not.
        var loader = new ImageLoader(new SdlImageDecoder());

        var image = loader.Decode(OnePixelBmp(0xFF, 0x80, 0x00));

        Assert.Equal(0xFF, (byte)(image.Pixels[0] >> 16));
        Assert.Equal(0x80, (byte)(image.Pixels[0] >> 8));
        Assert.Equal(0x00, (byte)image.Pixels[0]);
    }

    [Fact]
    public void Native_decodes_are_forced_opaque()
    {
        var decoder = new SdlImageDecoder();

        // A 32-bit BMP with a zero alpha channel. SDL3_image returns the alpha faithfully; the
        // engine's model has no use for it, and honouring it would put holes in solid art.
        var image = decoder.Decode(OnePixelBmp32(0x11, 0x22, 0x33, alpha: 0x00), ImageFormat.Bmp);

        Assert.Equal(0xFF112233u, image.Pixels[0]);
    }

    [Fact]
    public void Lossless_legacy_art_decodes_identically_to_the_oracle()
    {
        var oracle = LoadOracle();
        Assert.NotEmpty(oracle);

        if (CorpusRoot(oracle) is not { } root)
        {
            return;
        }

        var loader = new ImageLoader(new SdlImageDecoder());
        var mismatches = new List<string>();
        int checkedFiles = 0;

        // BMP and PCX only. Both are lossless and simple enough that any two decoders agree
        // exactly, so anything less than a byte-for-byte match is a real defect.
        foreach (var entry in oracle.Where(e => e.Format is ImageFormat.Bmp or ImageFormat.Pcx))
        {
            string path = Resolve(root, entry);
            if (!File.Exists(path))
            {
                continue;
            }

            checkedFiles++;
            try
            {
                var image = loader.Load(path);
                if (image.Width != entry.Width || image.Height != entry.Height)
                {
                    mismatches.Add($"{entry.RelativePath}: {image.Width}x{image.Height} " +
                                   $"vs oracle {entry.Width}x{entry.Height}");
                }
                else if (Digest(image) != entry.Sha256)
                {
                    mismatches.Add($"{entry.RelativePath}: pixels differ");
                }
            }
            catch (Exception e)
            {
                mismatches.Add($"{entry.RelativePath}: threw {e.GetType().Name}: {e.Message}");
            }
        }

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} of {checkedFiles} lossless files differ:\n  " +
            string.Join("\n  ", mismatches));
        Assert.True(checkedFiles >= 8, $"only {checkedFiles} lossless files were checked");
    }

    [Fact]
    public void Jpeg_art_decodes_within_tolerance_of_the_oracle()
    {
        var oracle = LoadOracle();
        Assert.NotEmpty(oracle);

        if (CorpusRoot(oracle) is not { } root)
        {
            return;
        }

        var loader = new ImageLoader(new SdlImageDecoder());
        var mismatches = new List<string>();
        int checkedFiles = 0;

        // JPEG's IDCT is specified only to a precision, so two conformant decoders may differ by a
        // unit or two per channel. Comparing means with a tolerance still catches everything that
        // actually matters: a failed decode, wrong dimensions, swapped channels, or a sheared or
        // flipped image -- the quadrant mean is what covers those last two.
        const double Tolerance = 1.5;

        foreach (var entry in oracle.Where(e => e.Format == ImageFormat.Jpg))
        {
            string path = Resolve(root, entry);
            if (!File.Exists(path))
            {
                continue;
            }

            checkedFiles++;
            try
            {
                var image = loader.Load(path);
                if (image.Width != entry.Width || image.Height != entry.Height)
                {
                    mismatches.Add($"{entry.RelativePath}: {image.Width}x{image.Height} " +
                                   $"vs oracle {entry.Width}x{entry.Height}");
                    continue;
                }

                double[] whole = Means(image, quadrantOnly: false);
                double[] quadrant = Means(image, quadrantOnly: true);

                for (int c = 0; c < 3; c++)
                {
                    if (Math.Abs(whole[c] - entry.Mean[c]) > Tolerance)
                    {
                        mismatches.Add($"{entry.RelativePath}: channel {c} mean " +
                                       $"{whole[c]:F3} vs oracle {entry.Mean[c]:F3}");
                    }

                    if (Math.Abs(quadrant[c] - entry.QuadrantMean[c]) > Tolerance)
                    {
                        mismatches.Add($"{entry.RelativePath}: channel {c} quadrant mean " +
                                       $"{quadrant[c]:F3} vs oracle {entry.QuadrantMean[c]:F3}");
                    }
                }
            }
            catch (Exception e)
            {
                mismatches.Add($"{entry.RelativePath}: threw {e.GetType().Name}: {e.Message}");
            }
        }

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} JPEG comparisons out of tolerance across {checkedFiles} files:\n  " +
            string.Join("\n  ", mismatches.Take(15)));
        Assert.True(checkedFiles >= 8, $"only {checkedFiles} JPEGs were checked");
    }

    [Fact]
    public void Legacy_art_is_always_opaque()
    {
        var oracle = LoadOracle();
        Assert.NotEmpty(oracle);

        if (CorpusRoot(oracle) is not { } root)
        {
            return;
        }

        var loader = new ImageLoader(new SdlImageDecoder());
        int checkedFiles = 0;

        foreach (var entry in oracle)
        {
            string path = Resolve(root, entry);
            if (!File.Exists(path))
            {
                continue;
            }

            checkedFiles++;
            var image = loader.Load(path);
            Assert.All(image.Pixels, p => Assert.Equal(0xFF000000u, p & 0xFF000000u));
        }

        Assert.True(checkedFiles >= 16, $"only {checkedFiles} legacy files were checked");
    }

    /// <summary>A 1x1 24bpp BMP of the given colour.</summary>
    private static byte[] OnePixelBmp(byte r, byte g, byte b) =>
    [
        0x42, 0x4D, 0x3A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
        0x28, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x13, 0x0B, 0x00, 0x00,
        0x13, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        b, g, r, 0x00,                                   // BMP stores BGR, then row padding
    ];

    /// <summary>A 1x1 32bpp BMP, so the alpha channel is real and can be checked for stripping.</summary>
    private static byte[] OnePixelBmp32(byte r, byte g, byte b, byte alpha) =>
    [
        0x42, 0x4D, 0x3A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
        0x28, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x13, 0x0B, 0x00, 0x00,
        0x13, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        b, g, r, alpha,
    ];
}
