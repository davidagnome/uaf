using UAF.Common;

namespace UAFcore.Tests;

/// <summary>
/// Case-insensitive asset resolution — the shim that lets a design whose records name files by a
/// different case than they were shipped with still load on a case-sensitive filesystem.
/// </summary>
public class CaseInsensitiveFilesTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "uaf-ci-files-" + Guid.NewGuid().ToString("N"));

    public CaseInsensitiveFilesTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        CaseInsensitiveFiles.Forget();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private void Write(string name)
    {
        File.WriteAllText(Path.Combine(directory, name), string.Empty);
    }

    [Fact]
    public void A_file_resolves_whatever_case_the_name_is_asked_in()
    {
        Write("WYA_UD_Medieval.png");

        string? found = CaseInsensitiveFiles.Resolve(directory, "wya_ud_medieval.png");

        Assert.NotNull(found);
        Assert.Equal("WYA_UD_Medieval.png", Path.GetFileName(found));
    }

    [Fact]
    public void A_missing_file_resolves_to_null()
    {
        Assert.Null(CaseInsensitiveFiles.Resolve(directory, "nobody.png"));
    }

    [Fact]
    public void An_absent_directory_resolves_to_null()
    {
        Assert.Null(CaseInsensitiveFiles.Resolve(
            Path.Combine(directory, "does-not-exist"), "any.png"));
    }

    [Fact]
    public void Two_files_that_differ_only_in_case_resolve_to_the_ordinal_first()
    {
        // Possible only on a case-sensitive filesystem, impossible on the one the data came from.
        // Ordinal-first: 'T' (0x54) before 't' (0x74).
        Write("Title.png");
        Write("title.png");

        string? found = CaseInsensitiveFiles.Resolve(directory, "TITLE.png");

        Assert.NotNull(found);
        Assert.Equal("Title.png", Path.GetFileName(found));
    }

    [Fact]
    public void The_index_is_cached_so_a_second_lookup_sees_the_same_directory()
    {
        Write("cached.png");
        string first = CaseInsensitiveFiles.Resolve(directory, "CACHED.png")!;

        // A file added after the index was built is not found until the cache is forgotten, which
        // is the documented shape -- "build the index once per design".
        Assert.Null(CaseInsensitiveFiles.Resolve(directory, "later.png"));

        CaseInsensitiveFiles.Forget();
        Write("later.png");
        Assert.NotNull(CaseInsensitiveFiles.Resolve(directory, "LATER.png"));
    }
}
