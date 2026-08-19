using UAF.Serialization;

namespace UAFedit.RoundTrip.Tests;

/// <summary>
/// The JSON character file, in the round trip.
/// </summary>
/// <remarks>
/// <b>The one file in the corpus that comes back byte-identical.</b> Every binary format restamps
/// its version on save, so none of them can (see <c>RoundTripTests</c>); JSON carries its version
/// as an ordinary field and has nothing to restamp. Worth its own test because the sweep's headline
/// claim — "not one file comes back identical" — now has an exception, and an exception nobody
/// asserts is one somebody later deletes.
/// </remarks>
public class JsonCharacterCoverageTests
{
    private static string? UrilKabo()
    {
        if (DesignCorpus.Find("SomethingWild") is not { } design)
        {
            return null;
        }

        string path = Path.Combine(design.DataDirectory, "Uril Kabo.CHAR");
        return File.Exists(path) ? path : null;
    }

    /// <summary>The sweep has a codec for it, so it is no longer skipped.</summary>
    [Fact]
    public void The_json_character_has_a_codec()
    {
        if (UrilKabo() is not { } path)
        {
            return;
        }

        var design = DesignCorpus.Find("SomethingWild")!;

        Assert.NotNull(DesignFiles.CodecFor(path, DesignFiles.GlobalVersion(design)));

        // And it is in the file list the sweep walks, or the codec would never be reached.
        Assert.Contains(DesignCorpus.Files(design), f => f == path);
    }

    /// <summary>It writes back byte for byte.</summary>
    [Fact]
    public void The_json_character_round_trips_exactly()
    {
        if (UrilKabo() is not { } path)
        {
            return;
        }

        var design = DesignCorpus.Find("SomethingWild")!;
        var codec = DesignFiles.CodecFor(path, DesignFiles.GlobalVersion(design))!;

        Assert.Equal(File.ReadAllBytes(path), codec.Write(DesignFiles.ReadFile(codec, path)));
    }
}
