using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>Covers writing <c>PIC_DATA</c> by reading every written record back.</summary>
public class PicDataWriterTests
{
    private static readonly PicRecord Sample = new(
        PicType: 3, FileName: "goblin.pcx", TimeDelay: 120, NumFrames: 4,
        FrameWidth: 64, FrameHeight: 48, Flags: 0x11, MaxLoops: 7,
        Style: 2, UseAlpha: 1, AlphaValue: 0xBEEF, RestartFrame: 2);

    private static MemoryStream Written(Action<IArchiveWriteCursor> write)
    {
        var stream = new MemoryStream();
        write(ArchiveWriteCursor.For(new MfcArchiveWriter(stream)));
        stream.Position = 0;
        return stream;
    }

    private static PicRecord ReadBack(MemoryStream stream, PicArchiveVariant variant) =>
        PicDataReader.Read(new MfcArchiveReader(stream), MonsterRecordWriter.WrittenVersion,
                           variant);

    [Fact]
    public void Every_field_survives_the_car_round_trip()
    {
        var stream = Written(w => PicDataWriter.Write(w, Sample, PicArchiveVariant.Car));

        Assert.Equal(Sample, ReadBack(stream, PicArchiveVariant.Car));
    }

    [Fact]
    public void The_plain_archive_form_omits_style_and_only_style()
    {
        // Not a version question: the CArchive twin has the style line commented out on both
        // halves (PicData.cpp:135), so the two forms differ by exactly four bytes.
        var car = Written(w => PicDataWriter.Write(w, Sample, PicArchiveVariant.Car));
        var plain = Written(w => PicDataWriter.Write(w, Sample, PicArchiveVariant.CArchive));

        Assert.Equal(car.Length - 4, plain.Length);
        Assert.Equal(Sample with { Style = 0 }, ReadBack(plain, PicArchiveVariant.CArchive));
    }

    [Fact]
    public void Writing_one_variant_and_reading_the_other_desynchronises()
    {
        // Four bytes, with nothing in the record to announce which form it is -- so the fields
        // after style all shift and still decode to plausible numbers.
        var stream = Written(w => PicDataWriter.Write(w, Sample, PicArchiveVariant.Car));

        Assert.NotEqual(Sample, ReadBack(stream, PicArchiveVariant.CArchive));
    }

    [Fact]
    public void The_alpha_value_is_two_bytes_so_what_follows_stays_aligned()
    {
        // A WORD among 4-byte fields. Writing four here shifts RestartFrame and every record
        // after it, which no alignment check catches.
        var stream = Written(w =>
        {
            PicDataWriter.Write(w, Sample, PicArchiveVariant.Car);
            w.WriteInt32(0x5EA1);
        });

        var reader = new MfcArchiveReader(stream);
        Assert.Equal(Sample, PicDataReader.Read(reader, MonsterRecordWriter.WrittenVersion,
                                                PicArchiveVariant.Car));
        Assert.Equal(0x5EA1, reader.ReadInt32());
    }

    [Fact]
    public void An_empty_filename_goes_out_as_the_blank_sentinel_and_comes_back_empty()
    {
        var pic = Sample with { FileName = string.Empty };

        var stream = Written(w => PicDataWriter.Write(w, pic, PicArchiveVariant.Car));

        Assert.Equal(pic, ReadBack(stream, PicArchiveVariant.Car));

        // The sentinel really is on the wire -- a zero-length string would be a different file.
        stream.Position = 0;
        var reader = new MfcArchiveReader(stream);
        reader.ReadInt32();
        Assert.Equal(ArchiveStringConventions.ArchiveBlank, reader.ReadString());
    }

    // ---- StripFilenamePath -----------------------------------------------------------------------

    [Theory]
    [InlineData("goblin.pcx", "goblin.pcx")]                    // nothing to strip
    [InlineData("art\\goblin.pcx", "goblin.pcx")]               // the ordinary case
    [InlineData("c:\\art\\icons\\goblin.pcx", "goblin.pcx")]    // only the last separator counts
    [InlineData("", "")]
    public void The_directory_is_stripped_as_the_reference_strips_it(string input, string expected)
    {
        Assert.Equal(expected, PicDataWriter.StripFilenamePath(input));
    }

    [Theory]
    [InlineData("a\\b", "a\\b")]              // separator at index 1
    [InlineData("\\goblin.pcx", "\\goblin.pcx")]  // leading separator
    public void A_separator_before_index_two_is_left_alone(string input, string expected)
    {
        // The reference's index >= 2 test. "Everything after the last separator" would be tidier
        // and would differ here, so a design holding such a name would come out changed.
        Assert.Equal(expected, PicDataWriter.StripFilenamePath(input));
    }

    [Fact]
    public void A_trailing_separator_is_dropped_and_nothing_else()
    {
        // The early return: it takes off the backslash and stops, rather than going on to strip
        // the directory that is now at the end.
        Assert.Equal("art\\icons", PicDataWriter.StripFilenamePath("art\\icons\\"));
    }

    [Fact]
    public void A_bare_drive_letter_strips_to_nothing()
    {
        // "c:\" is four characters short of the trailing-separator rule, so it falls through to
        // "take everything after the last separator" -- and there is nothing after it. Not a
        // useful outcome, but it is the reference's, and a name reaching here is already lost.
        Assert.Equal(string.Empty, PicDataWriter.StripFilenamePath("c:\\"));
    }
}
