using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// A design whose <c>game.dat</c> ends before its records do.
/// </summary>
/// <remarks>
/// <para>
/// <b>The editor's own template design is one</b>, which is why this matters rather than being a
/// curiosity: <c>src/UAFWinEd/DefaultDesign.dsn/Data/game.dat</c> is 4,343 bytes and its
/// <c>GLOBAL_STATS</c> consumes every one of them and then asks for four more. The reference opens
/// it — <c>CArchive</c>'s <c>&gt;&gt;</c> discards the read count, so the missing tail reads as
/// zero — and File &gt; New cannot work if this port refuses it.
/// </para>
/// <para>
/// <b>This design is tracked in git</b>, unlike the rest of the corpus, so these tests really run
/// on a bare checkout.
/// </para>
/// </remarks>
public class TruncatedGameDataTests
{
    private static string? TemplateDesign()
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

        string path = Path.Combine(dir.FullName, "src", "UAFWinEd", "DefaultDesign.dsn",
                                   "Data", "game.dat");
        return File.Exists(path) ? path : null;
    }

    /// <summary>The template really is short, which is the premise everything here rests on.</summary>
    [Fact]
    public void The_template_designs_game_data_is_present_and_short()
    {
        if (TemplateDesign() is not { } path)
        {
            return;
        }

        Assert.Equal(4343, new FileInfo(path).Length);
    }

    /// <summary>It opens, and says how far it got before the file ran out.</summary>
    /// <remarks>
    /// <b>The truncation is reported, not swallowed.</b> A short read is a fact about the file, and
    /// a caller that tolerated it still needs to be able to say so.
    /// </remarks>
    [Fact]
    public void The_template_design_opens_and_reports_where_it_ran_out()
    {
        if (TemplateDesign() is not { } path)
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var cursor = GameDataReader.Open(stream);

        Assert.Equal(GameDataFraming.Plain, cursor.Framing);

        var globals = GlobalStatsReader.ReadThroughCharacters(cursor.Body, cursor.Version);

        Assert.Equal("DefaultDesign", globals.DesignName);

        // It ran out exactly AT the end, not somewhere in the middle -- which is what separates a
        // short file from a mis-parse that ran off into the distance.
        Assert.Equal(4343, cursor.TruncatedAt);
    }

    /// <summary>
    /// A file that is not short reports no truncation, so the flag means something.
    /// </summary>
    /// <remarks>
    /// Without this, <see cref="MfcArchiveReader.TruncatedAt"/> could be set on every read and the
    /// test above would still pass.
    /// </remarks>
    [Fact]
    public void A_complete_file_reports_no_truncation()
    {
        using var stream = new MemoryStream();
        var writer = new MfcArchiveWriter(stream);
        writer.WriteInt32(7);
        writer.WriteInt32(9);
        stream.Position = 0;

        var reader = new MfcArchiveReader(stream) { ZeroFillPastEnd = true };

        Assert.Equal(7, reader.ReadInt32());
        Assert.Equal(9, reader.ReadInt32());
        Assert.Null(reader.TruncatedAt);
    }

    /// <summary>Past the end, a read yields zero rather than throwing — and only when asked to.</summary>
    [Fact]
    public void Past_the_end_reads_zero_only_when_enabled()
    {
        using var stream = new MemoryStream([1, 0, 0, 0]);

        var tolerant = new MfcArchiveReader(stream) { ZeroFillPastEnd = true };
        Assert.Equal(1, tolerant.ReadInt32());
        Assert.Equal(0, tolerant.ReadInt32());
        Assert.Equal(4, tolerant.TruncatedAt);

        stream.Position = 0;
        var strict = new MfcArchiveReader(stream);
        Assert.Equal(1, strict.ReadInt32());
        Assert.Throws<EndOfStreamException>(() => strict.ReadInt32());
    }

    /// <summary>
    /// A partly-available read keeps the bytes that WERE there.
    /// </summary>
    /// <remarks>
    /// <c>CArchive</c> copies what it can and leaves the rest — it does not discard the whole
    /// field. Zeroing the entire destination would change the last value in a truncated file.
    /// </remarks>
    [Fact]
    public void A_partial_read_keeps_what_was_there()
    {
        using var stream = new MemoryStream([0x2A, 0x00]);
        var reader = new MfcArchiveReader(stream) { ZeroFillPastEnd = true };

        // Two of the four bytes exist; the value is what those two say.
        Assert.Equal(42, reader.ReadInt32());
        Assert.Equal(2, reader.TruncatedAt);
    }
}
