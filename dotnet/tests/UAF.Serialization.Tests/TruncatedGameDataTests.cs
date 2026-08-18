using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// The editor's template design, and reading past the end of a file the way <c>CArchive</c> does.
/// </summary>
/// <remarks>
/// <para>
/// <b>The file was never short — it was being read wrongly, and it reads now.</b>
/// <c>src/UAFWinEd/DefaultDesign.dsn/Data/game.dat</c> is 4,343 bytes and the port used to run off
/// the end of it, which looked exactly like truncation. It was not: the unframed path is a real
/// <c>CArchive</c>, whose <c>PIC_DATA</c> records are four bytes shorter than a <c>CAR</c>'s, and
/// a design below <c>VersionSpellNames</c> carries eight starting-equipment lists the port did not
/// read at all. Both are fixed; see docs/PORTING-PLAN.md §12.
/// </para>
/// <para>
/// <b>The zero-fill mechanism is kept and stays switched off.</b> MFC's extraction operators do
/// not check how much they read, so the reference would open a genuinely truncated design and
/// treat the missing tail as zeroes — <see cref="MfcArchiveReader.ZeroFillPastEnd"/> is that
/// behaviour, available on request. Switching it on here was tried and did real damage: given an
/// endless supply of zeroes the mis-parse did not stop, it read a record count out of the tail and
/// asked for millions of records until the process died, taking the whole test suite with it.
/// <b>Tolerating a short read is not a substitute for reading correctly</b>, and a parse that runs
/// away when you feed it zeroes is telling you it is mis-aligned, not that the file is short.
/// </para>
/// <para>
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

    /// <summary>The template is present and is the file these tests mean.</summary>
    [Fact]
    public void The_template_designs_game_data_is_present()
    {
        if (TemplateDesign() is not { } path)
        {
            return;
        }

        Assert.Equal(4343, new FileInfo(path).Length);
    }

    /// <summary>
    /// The template design reads, whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This test used to assert the opposite</b>, and its own remarks said that when the
    /// unframed <c>GLOBAL_STATS</c> was decoded correctly the fix was to assert the design that
    /// came back rather than to loosen the reader. That is what happened.
    /// </para>
    /// <para>
    /// <b>The variant has to come from the framing.</b> An unframed <c>game.dat</c> is genuinely a
    /// <c>CArchive</c>, so its <c>PIC_DATA</c> records are four bytes shorter than a <c>CAR</c>'s —
    /// see <see cref="GameDataReader.Cursor.PicVariant"/>. Reading it with the default variant
    /// still fails, which is why this passes it explicitly rather than relying on the default.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_template_design_reads()
    {
        if (TemplateDesign() is not { } path)
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var cursor = GameDataReader.Open(stream);

        Assert.Equal(GameDataFraming.Plain, cursor.Framing);
        Assert.Equal(PicArchiveVariant.CArchive, cursor.PicVariant);
        Assert.Equal(0.9150250, cursor.Version.Value, 7);

        var globals = GlobalStatsReader.ReadThroughCharacters(
            cursor.Body, cursor.Version, ArchiveRole.Editor, cursor.PicVariant);

        Assert.Equal("DefaultDesign", globals.DesignName);

        // Nothing was tolerated on the way in: the file is not short, it was being mis-read.
        Assert.Null(cursor.TruncatedAt);
    }

    /// <summary>
    /// Read with the compressed variant, the same file still comes apart.
    /// </summary>
    /// <remarks>
    /// <b>Four bytes per <c>PIC_DATA</c> record, and the file has eighteen of them.</b> This is
    /// what makes <see cref="GameDataReader.Cursor.PicVariant"/> load-bearing rather than tidy —
    /// and it is asserted because the failure it prevents is silent everywhere else: no shipped
    /// design is unframed, so nothing but this file would ever notice.
    /// </remarks>
    [Fact]
    public void The_compressed_variant_cannot_read_it()
    {
        if (TemplateDesign() is not { } path)
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var cursor = GameDataReader.Open(stream);

        Assert.ThrowsAny<Exception>(
            () => GlobalStatsReader.ReadThroughCharacters(
                cursor.Body, cursor.Version, ArchiveRole.Editor, PicArchiveVariant.Car));
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
