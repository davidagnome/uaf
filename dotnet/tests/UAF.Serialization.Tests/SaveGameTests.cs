namespace UAF.Serialization.Tests;

/// <summary>
/// Covers the <c>.pty</c> savegame framing — the version header and the compressed archive
/// underneath it. The body is not ported; see <see cref="SaveGameReader"/>.
/// </summary>
public class SaveGameTests
{
    /// <summary>design-relative path, expected version, expected leading task count.</summary>
    public static TheoryData<string, double, int> Saves => new()
    {
        { "SomethingWild.dsn/Saves/SaveA.pty", 3.65, 5 },
        { "Ambassador's_Letter/Saves/SaveA.pty", 2.81, 4 },
    };

    private static string? Path_(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string path = System.IO.Path.Combine(dir.FullName, "reference",
            relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void The_version_is_a_raw_double_at_offset_zero(string relative, double expected,
                                                            int taskCount)
    {
        string? path = Path_(relative);
        if (path is null)
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var save = SaveGameReader.Read(stream);

        // Read straight off the file rather than through any archive, which is why it is legible
        // in a hex dump while everything after it is not.
        Assert.Equal(expected, save.Version.Value, 6);
        _ = taskCount;
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void The_body_inflates_and_yields_a_plausible_leading_count(string relative,
                                                                       double expected,
                                                                       int taskCount)
    {
        string? path = Path_(relative);
        if (path is null)
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var save = SaveGameReader.Read(stream);

        // The compression-type byte at offset 8 reads 0x02, so this exercises the same LZW layer
        // as a compressed game.dat -- on a different container, which is the point. If the layer
        // were misaligned the first value out would be noise rather than a small integer.
        int first = save.Body.ReadInt32();

        Assert.Equal(taskCount, first);
        Assert.InRange(first, 0, 64);
        _ = expected;
    }

    [Fact]
    public void A_version_below_the_engine_floor_is_refused_by_name()
    {
        var bytes = new MemoryStream();
        var writer = new BinaryWriter(bytes);
        writer.Write(0.5);
        writer.Flush();
        bytes.Seek(0, SeekOrigin.Begin);

        var error = Assert.Throws<NotSupportedException>(() => SaveGameReader.Read(bytes));
        Assert.Contains("pre-dates", error.Message);
    }

    [Fact]
    public void A_version_below_the_compressed_threshold_is_refused_separately()
    {
        // Between 0.573 and VersionSpellNames the engine has a second, distinct refusal. Both are
        // reproduced because they report different causes, and a save in that window is a
        // different problem from one that pre-dates the event conversion.
        var bytes = new MemoryStream();
        var writer = new BinaryWriter(bytes);
        writer.Write(0.6);
        writer.Flush();
        bytes.Seek(0, SeekOrigin.Begin);

        var error = Assert.Throws<NotSupportedException>(() => SaveGameReader.Read(bytes));
        Assert.Contains("VersionSpellNames", error.Message);
    }
}
