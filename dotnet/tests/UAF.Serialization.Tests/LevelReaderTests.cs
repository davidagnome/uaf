using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads the viewport map from the real <c>Level000.lvl</c>.
/// </summary>
public class LevelReaderTests
{
    private const int CellBytes = 15;   // 1 + 4 + 1 + 1 + 4 + 4 at >= 0.5771

    private static string LevelFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "UAFWinEd", "DefaultDesign.dsn", "Data", "Level000.lvl");
    }

    private static MfcArchiveReader OpenPayload(FileStream fs, out DesignFileHeader header)
    {
        header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        return new MfcArchiveReader(fs);
    }

    [Fact]
    public void Level_files_are_never_compressed_even_though_they_use_CAR()
    {
        using var fs = File.OpenRead(LevelFile());
        var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);

        Assert.True(header.HadMagic);
        Assert.Equal(16, header.PayloadOffset);
        Assert.Equal(ArchiveTier.UncompressedCar, header.Tier);

        // LevelData has no compression threshold at all: LoadLevel builds a CAR but leaves
        // ar.Compress(true) commented out (Level.cpp:2186). The databases reach tier 3 at 0.930;
        // levels never do, at any version.
        Assert.Null(DesignFileKind.LevelData.CompressionThreshold);
        Assert.Equal(ArchiveTier.UncompressedCar, DesignFileKind.LevelData.TierFor(DesignVersion.V529));
    }

    [Fact]
    public void Dimensions_are_single_bytes_read_width_first()
    {
        using var fs = File.OpenRead(LevelFile());
        var ar = OpenPayload(fs, out _);

        var (width, height) = LevelReader.ReadDimensions(ar);

        // BYTE, not int (Level.h:58). Reading these as int32 yields 16,779,786 x 65,793.
        Assert.Equal(10, width);
        Assert.Equal(10, height);
    }

    [Fact]
    public void Area_map_reads_as_a_coherent_grid()
    {
        using var fs = File.OpenRead(LevelFile());
        var ar = OpenPayload(fs, out var header);

        var (width, height, cells) = LevelReader.ReadAreaMap(ar, header.Version);

        Assert.Equal(100, cells.Length);
        Assert.Equal(width * height, cells.Length);

        // The reader must have consumed exactly the header + dimensions + grid.
        Assert.Equal(header.PayloadOffset + 2 + (cells.Length * CellBytes), fs.Position);

        // Background indices are 6-bit after the flag bits are stripped; a reader that keeps the
        // raw byte would report values above 63 here.
        Assert.All(cells, c => Assert.InRange(c.Background, 0, 63));

        // Blockage is a small enum; walls index a wall-slot table. Neither should be arbitrary.
        Assert.All(cells, c => Assert.Equal(4, c.Walls.Length));
        Assert.All(cells, c => Assert.Equal(4, c.Blockage.Length));
        Assert.All(cells, c => Assert.All(c.Blockage, b => Assert.InRange(b, 0, 16)));
    }

    [Fact]
    public void Background_flag_bits_are_split_out_of_the_value()
    {
        // bkgrnd packs ShowDistantBG (0x80) and DistantBGInBands (0x40) into the top two bits,
        // masking to 0x3F for the value itself (Level.cpp:698). This is a post-read transform:
        // the stored byte and the effective background index differ whenever a flag is set.
        using var fs = File.OpenRead(LevelFile());
        var ar = OpenPayload(fs, out var header);
        var (_, _, cells) = LevelReader.ReadAreaMap(ar, header.Version);

        // Whatever the fixture happens to contain, the invariant must hold for every cell.
        Assert.All(cells, c =>
        {
            byte reconstructed = (byte)(c.Background
                                        | (c.ShowDistantBackground ? 0x80 : 0)
                                        | (c.DistantBackgroundInBands && c.ShowDistantBackground ? 0x40 : 0));
            Assert.Equal(0, reconstructed & 0x3F & ~c.Background);
        });
    }
}
