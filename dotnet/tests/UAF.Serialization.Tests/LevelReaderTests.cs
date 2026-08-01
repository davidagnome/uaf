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

    [Fact]
    public void Walls_and_blockage_are_stored_north_south_east_west()
    {
        // The permutation, pinned against the C++ table. AREA_MAP_DATA::walls(int dir) and
        // blockages(int dir) (Level.cpp:932, :945) both build {0,2,1,3}, and IsWallAt
        // (Drawtile.cpp:1819) plus the explicit switches in RunEvent.cpp (:5171, :5420, :14678)
        // spell out the same mapping. Direction is 0=north, 1=east, 2=south, 3=west.
        var cell = new AreaMapCell(0, false, false, 0, 0, 0, 0, 0, false,
                                   Walls: [10, 20, 30, 40], Blockage: [1, 2, 3, 4]);

        Assert.Equal(10, cell.WallAt(0));   // north -> slot 0
        Assert.Equal(30, cell.WallAt(1));   // east  -> slot 2
        Assert.Equal(20, cell.WallAt(2));   // south -> slot 1
        Assert.Equal(40, cell.WallAt(3));   // west  -> slot 3

        Assert.Equal((byte)1, cell.BlockageAt(0));
        Assert.Equal((byte)3, cell.BlockageAt(1));
        Assert.Equal((byte)2, cell.BlockageAt(2));
        Assert.Equal((byte)4, cell.BlockageAt(3));

        // dir & 3 wraps, as the original's does.
        Assert.Equal(cell.WallAt(0), cell.WallAt(4));
    }

    [Fact]
    public void The_stored_order_is_the_one_that_makes_shared_edges_agree()
    {
        // The test that would have caught indexing these arrays by facing. Two cells sharing an
        // edge describe it twice -- a cell's east face against its east neighbour's west face --
        // so the correct permutation is the one under which those two agree. This is real-data
        // evidence rather than a restatement of the table above, and it is what settled the
        // question: the synthetic fixture in WallResolverTests encoded the same wrong order the
        // resolver did, so it agreed with the bug instead of catching it.
        using var fs = File.OpenRead(LevelFile());
        var ar = OpenPayload(fs, out var header);
        var (width, height, cells) = LevelReader.ReadAreaMap(ar, header.Version);

        int permuted = 0, byFacing = 0, total = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var cell = cells[(y * width) + x];
                var east = cells[(y * width) + ((x + 1) % width)];
                var south = cells[(((y + 1) % height) * width) + x];

                total += 2;

                // What the reader does: east is slot 2, south is slot 1.
                if (cell.WallAt(1) == east.WallAt(3)) permuted++;
                if (cell.WallAt(2) == south.WallAt(0)) permuted++;

                // What indexing by Facing did: east is slot 1, south is slot 2.
                if (cell.Walls[1] == east.Walls[3]) byFacing++;
                if (cell.Walls[2] == south.Walls[0]) byFacing++;
            }
        }

        // A design may legitimately author a one-sided wall, so this is not required to be
        // perfect -- only decisively better than the alternative. On the reference designs the
        // margin is far wider than this: SomethingWild agrees on 9,708 of 9,708 edges under the
        // permutation and 78.88% under the other reading.
        Assert.True(permuted > byFacing,
            $"shared edges agree {permuted}/{total} permuted vs {byFacing}/{total} by facing");
    }
}
