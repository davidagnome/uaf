using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reading past the end of a file the way <c>CArchive</c> does, and why <c>game.dat</c> does not.
/// </summary>
/// <remarks>
/// <para>
/// <b>MFC's extraction operators do not check how much they read</b>, so the reference opens a
/// design whose <c>game.dat</c> ends mid-record and treats the missing tail as zeroes.
/// <see cref="MfcArchiveReader.ZeroFillPastEnd"/> is that behaviour, available on request.
/// </para>
/// <para>
/// <b>It was switched on for <c>game.dat</c> and switched back off, and that is the finding.</b>
/// The editor's own <c>src/UAFWinEd/DefaultDesign.dsn/Data/game.dat</c> is 4,343 bytes and its
/// <c>GLOBAL_STATS</c> asks for four more, so tolerating the short read looked like all that stood
/// between the port and File &gt; New. It is not: handed an endless supply of zeroes, the parse
/// reads a record count out of the tail and asks for <i>millions</i> of <c>PIC_DATA</c> records
/// (<c>GlobalStatsReader.cs:227</c>). A file that runs off like that is being mis-parsed, not
/// merely truncated — and the loop allocates until the process dies, which is why this cannot be
/// left switched on and hoped about.
/// </para>
/// <para>
/// So what is tested here is the mechanism, which is correct, and the refusal, which is current.
/// <b>The template design is tracked in git</b>, unlike the rest of the corpus, so these really
/// run on a bare checkout.
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

    /// <summary>
    /// The template's framing and version are read, and then the parse runs out of file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the current state of the File &gt; New blocker, asserted rather than described.</b>
    /// The header is fine — plain framing, version 0.9150250 — so whatever is wrong is inside
    /// <c>GLOBAL_STATS</c> on the unframed path, not in locating it.
    /// </para>
    /// <para>
    /// It fails as an <see cref="EndOfStreamException"/> at the file's own length, which is the
    /// bounded failure worth keeping. Tolerating it instead does not produce a design: it produces
    /// a request for millions of records. When the unframed <c>GLOBAL_STATS</c> is decoded
    /// correctly this test should start failing, and the fix is to assert the design that comes
    /// back rather than to loosen the reader.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_template_design_is_refused_at_its_own_length()
    {
        if (TemplateDesign() is not { } path)
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var cursor = GameDataReader.Open(stream);

        Assert.Equal(GameDataFraming.Plain, cursor.Framing);
        Assert.Equal(0.9150250, cursor.Version.Value, 7);

        // Nothing was tolerated on the way in, so there is nothing to report.
        Assert.Null(cursor.TruncatedAt);

        var ran = Assert.Throws<EndOfStreamException>(
            () => GlobalStatsReader.ReadThroughCharacters(cursor.Body, cursor.Version));

        // At the end of the file, not off in the distance -- the offset is the file's own length.
        Assert.Contains("4343", ran.Message, StringComparison.Ordinal);
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
