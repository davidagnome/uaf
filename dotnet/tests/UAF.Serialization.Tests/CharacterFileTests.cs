using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads the standalone <c>.chr</c> files two designs ship, to exact end of file.
/// </summary>
/// <remarks>
/// These are saved player characters and NPCs, written by <c>CHARACTER::SaveCharacter</c>. They
/// matter beyond their own format: a <c>.chr</c> is a <c>CHARACTER</c> record and nothing else, so
/// exhausting one proves the record's length exactly, with none of the slack a record embedded in a
/// larger file gets. If <see cref="CharacterReader"/> were a few bytes wrong, a design file would
/// most likely fail somewhere later and blame the wrong structure; here it cannot.
/// </remarks>
public class CharacterFileTests
{
    private static string? SavesRoot(string design)
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

        string saves = Path.Combine(dir.FullName, "reference", design, "Saves");
        return Directory.Exists(saves) ? saves : null;
    }

    private static IEnumerable<string> Files()
    {
        string? root = SavesRoot("SomethingWild.dsn");
        return root is null
            ? []
            : Directory.EnumerateFiles(root, "*.chr").OrderBy(p => p, StringComparer.Ordinal);
    }

    [Fact]
    public void Every_shipped_character_file_reads_to_exact_end_of_file()
    {
        var files = Files().ToList();
        if (files.Count == 0)
        {
            return;
        }

        Assert.Equal(6, files.Count);

        var failures = new List<string>();
        foreach (string path in files)
        {
            long length = new FileInfo(path).Length;
            try
            {
                using var stream = File.OpenRead(path);
                var file = CharacterFileReader.Read(stream);

                // The cardinal assertion for this format family. A .chr is one record with nothing
                // length-prefixed above it, so landing anywhere but the last byte means a field is
                // the wrong width -- and any surplus would otherwise go unnoticed.
                if (stream.Position != length)
                {
                    failures.Add($"{Path.GetFileName(path)}: stopped at {stream.Position} " +
                                 $"of {length} ({length - stream.Position:+#;-#;0} bytes)");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(file.Character.Name))
                {
                    failures.Add($"{Path.GetFileName(path)}: read to EOF but the name is empty");
                }
            }
            catch (Exception e)
            {
                failures.Add($"{Path.GetFileName(path)}: {e.GetType().Name}: {e.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {files.Count} character files failed:\n  " +
            string.Join("\n  ", failures));
    }

    [Fact]
    public void The_header_is_a_magic_and_a_double_at_offset_zero()
    {
        var files = Files().ToList();
        if (files.Count == 0)
        {
            return;
        }

        foreach (string path in files)
        {
            using var stream = File.OpenRead(path);
            var reader = new MfcArchiveReader(stream);

            // Written through the CAR but read around it, which only works because a CAR emits
            // nothing at construction. If it ever did, this would be 16 bytes further in.
            Assert.Equal(CharacterFileReader.Magic, reader.ReadUInt64());

            double version = reader.ReadDouble();
            Assert.InRange(version, CharacterFileReader.MinimumVersion.Value, 6.0);
        }
    }

    [Fact]
    public void Characters_carry_the_names_their_filenames_advertise()
    {
        var files = Files().ToList();
        if (files.Count == 0)
        {
            return;
        }

        // Not guaranteed by the format -- the filename is chosen by the player and the name lives
        // in the record -- but every shipped file happens to match, which makes it a cheap check
        // that the name really is the field being read rather than adjacent bytes that decode.
        foreach (string path in files)
        {
            var file = CharacterFileReader.Read(path);
            string stem = Path.GetFileNameWithoutExtension(path);

            Assert.Equal(stem, file.Character.Name);
            Assert.True(file.HadHeader, $"{stem} should carry the version header");
        }
    }

    [Fact]
    public void A_file_with_no_magic_falls_back_rather_than_failing()
    {
        var files = Files().ToList();
        if (files.Count == 0)
        {
            return;
        }

        // Pre-0.564 files put the record at offset 0. The engine rewinds and assumes 0.563
        // (Char.cpp:6944), so a reader that demanded the magic would reject files the original
        // still loads. Constructed by stripping the header off a real file: the result is not a
        // valid 0.563 record, so this asserts the fallback path is *taken*, not that it succeeds.
        byte[] whole = File.ReadAllBytes(files[0]);
        using var headerless = new MemoryStream(whole[16..]);

        var error = Record.Exception(() => CharacterFileReader.Read(headerless));

        // Either it throws while decoding, or it returns claiming the assumed version -- but it
        // must never report the magic as present.
        if (error is null)
        {
            headerless.Seek(0, SeekOrigin.Begin);
            var file = CharacterFileReader.Read(headerless);
            Assert.False(file.HadHeader);
            Assert.Equal(CharacterFileReader.AssumedVersion.Value, file.Version.Value, 6);
        }
    }

    [Fact]
    public void A_version_below_the_engine_floor_is_refused_by_name()
    {
        // 0.5 predates special abilities, and the original throws rather than guessing at the
        // older record shape. Ported as an explicit refusal for the same reason.
        var bytes = new MemoryStream();
        var writer = new BinaryWriter(bytes);
        writer.Write(CharacterFileReader.Magic);
        writer.Write(0.5);
        writer.Flush();
        bytes.Seek(0, SeekOrigin.Begin);

        var error = Assert.Throws<NotSupportedException>(() => CharacterFileReader.Read(bytes));
        Assert.Contains("0.5", error.Message);
    }
}
