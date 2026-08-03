using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips whole <c>items.dat</c> databases taken from shipped designs.
/// </summary>
/// <remarks>
/// The second record type the port can write. Unlike a monster's, an item record ends <i>at</i> its
/// ASL — but the database does not: an ammo-type list follows the records, the same shape as the
/// item list that follows a monster's attributes.
/// </remarks>
public class ItemWriterCorpusTests
{
    private static DirectoryInfo? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        return dir;
    }

    private static string? PathTo(string relativeDataDir)
    {
        var root = RepoRoot();
        string? path = root is null
            ? null
            : Path.Combine(root.FullName, Path.Combine(relativeDataDir.Split('/')), "items.dat");
        return path is not null && File.Exists(path) ? path : null;
    }

    private static ItemDatabase? Read(string relativeDataDir)
    {
        if (PathTo(relativeDataDir) is not { } path)
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var header = DesignFileHeader.Read(stream, DesignFileKind.Database);
        stream.Seek(header.PayloadOffset, SeekOrigin.Begin);

        return header.Tier == ArchiveTier.CompressedCar
            ? ItemRecordReader.ReadDatabase(CarArchiveReader.Open(stream), header.Version,
                                            ArchiveRole.Engine)
            : ItemRecordReader.ReadDatabase(new MfcArchiveReader(stream), header.Version,
                                            ArchiveRole.Engine);
    }

    private static byte[] WriteCompressed(ItemDatabase database)
    {
        var stream = new MemoryStream();
        using (var car = CarArchiveWriter.Open(stream))
        {
            ItemRecordWriter.WriteDatabase(ArchiveWriteCursor.For(car),
                                           database.Items, database.AmmoTypes);
        }

        return stream.ToArray();
    }

    private static byte[]? CompressedPayload(string relativeDataDir)
    {
        if (PathTo(relativeDataDir) is not { } path)
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var header = DesignFileHeader.Read(stream, DesignFileKind.Database);
        if (header.Tier != ArchiveTier.CompressedCar)
        {
            return null;
        }

        stream.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var payload = new MemoryStream();
        stream.CopyTo(payload);
        return payload.ToArray();
    }

    public static TheoryData<string> Designs =>
    [
        "reference/SomethingWild.dsn/Data",
        "reference/Case.dsn/Data",
        "reference/ci-tier3/Data",
        "reference/dc-default/data-files",
    ];

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_real_item_round_trips(string dataDir)
    {
        var database = Read(dataDir);
        if (database is null)
        {
            return;
        }

        Assert.NotEmpty(database.Items);

        var stream = new MemoryStream(WriteCompressed(database));
        var read = ItemRecordReader.ReadDatabase(CarArchiveReader.Open(stream),
                                                 ItemRecordWriter.WrittenVersion,
                                                 ArchiveRole.Engine);

        Assert.Equal(database.Items.Count, read.Items.Count);
        Assert.Equal(database.AmmoTypes, read.AmmoTypes);

        for (int i = 0; i < database.Items.Count; i++)
        {
            AssertSameItem(database.Items[i], read.Items[i]);
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_record_in_a_modern_design_is_writable(string dataDir)
    {
        // Without this the round trip could pass by having nothing to do.
        var database = Read(dataDir);
        if (database is null)
        {
            return;
        }

        Assert.All(database.Items,
                   i => Assert.True(ItemRecordWriter.CanWrite(i, out string reason), reason));
    }

    [Fact]
    public void A_design_at_the_written_version_comes_back_byte_for_byte()
    {
        // The same claim as for monsters, and the same two conditions: ci-tier3 is 5.29, so
        // nothing is added on the way out, and no item record needed repairing as it was read.
        byte[]? shipped = CompressedPayload("reference/ci-tier3/Data");
        var database = Read("reference/ci-tier3/Data");
        if (shipped is null || database is null)
        {
            return;
        }

        Assert.Equal(shipped, WriteCompressed(database));
    }

    [Fact]
    public void The_ammo_list_after_the_records_is_not_forgotten()
    {
        // It sits outside the record loop, the same shape as the item list after a monster's ASL.
        // A writer that stopped at the records would leave the reader taking the list's count from
        // whatever followed -- so this guards that the corpus has one to lose.
        var database = Read("reference/ci-tier3/Data");
        if (database is null)
        {
            return;
        }

        Assert.NotEmpty(database.AmmoTypes);
    }

    [Fact]
    public void Both_copies_of_the_hit_art_are_written()
    {
        // HitArt goes out twice and MissileArt once. Writing one copy would leave a whole
        // PIC_DATA missing and every record after it misaligned -- which the round trip would
        // catch, but this says what to look at when it does.
        var database = Read("reference/ci-tier3/Data");
        if (database is null)
        {
            return;
        }

        Assert.Contains(database.Items, i => i.HitArt is not null && i.Tail.HitArt is not null);
    }

    private static void AssertSameItem(ItemRecord expected, ItemRecord actual)
    {
        // Field by field: the records hold lists, which compare by reference.
        Assert.Equal(expected.Names, actual.Names);
        Assert.Equal(expected.HitArt, actual.HitArt);
        Assert.Equal(expected.MissileArt, actual.MissileArt);
        Assert.Equal(expected.Scalars, actual.Scalars);
        Assert.Equal(expected.Combat, actual.Combat);

        var e = expected.Tail;
        var a = actual.Tail;
        Assert.Equal(e.WeaponType, a.WeaponType);
        Assert.Equal(e.UsageFlags, a.UsageFlags);
        Assert.Equal(e.UsableByBaseclass, a.UsableByBaseclass);
        Assert.Equal(e.RangeMax, a.RangeMax);
        Assert.Equal(e.UseEvent, a.UseEvent);
        Assert.Equal(e.ExamineEvent, a.ExamineEvent);
        Assert.Equal(e.ExamineLabel, a.ExamineLabel);
        Assert.Equal(e.AttackMessage, a.AttackMessage);
        Assert.Equal(e.RechargeRate, a.RechargeRate);
        Assert.Equal(e.IsNonLethal, a.IsNonLethal);
        Assert.Equal(e.HitArt, a.HitArt);
        Assert.Equal(e.CanBeHalvedJoined, a.CanBeHalvedJoined);
        Assert.Equal(e.CanBeTradeDropSoldDep, a.CanBeTradeDropSoldDep);
        Assert.Equal(e.SpecialAbilities.Pairs, a.SpecialAbilities.Pairs);
        Assert.Equal(e.Attributes, a.Attributes);
    }
}
