using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips a whole <c>GLOBAL_STATS</c> — the record a design's <c>game.dat</c> is.
/// </summary>
/// <remarks>
/// The widest record in the format, and the first that pulls in nearly everything else: two
/// picture-import lists, a <c>LOGFONT</c> blit, eleven art slots, the sound queues, three record
/// lists, the character list, the level table with its cell contents, the currency and difficulty
/// configuration, the global event list, the journal and a spellbook.
/// </remarks>
public class GlobalStatsWriterTests
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

    private static string? GameDat(string rel)
    {
        var root = RepoRoot();
        string? path = root is null ? null : Path.Combine(root.FullName, "reference", rel, "game.dat");
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>The record and the global event list, which the reader hands out separately.</summary>
    private sealed record Design(
        GlobalStatsPrefix Global, List<(EventType Type, IGameEvent Body)> Events);

    private static Design? Read(string rel)
    {
        if (GameDat(rel) is not { } path)
        {
            return null;
        }

        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);

        // The reader reports how many global events there were but hands the bodies to a callback
        // rather than keeping them, so the list is collected here.
        var events = new List<(EventType, IGameEvent)>();
        var global = GlobalStatsReader.Read(
            cursor.Body, cursor.Version, ArchiveRole.Editor,
            (ar, type, version) =>
            {
                var body = EventBodyReader.TryRead(ar, type, version, ArchiveRole.Editor);
                if (body is not null)
                {
                    events.Add((type, body));
                }
                return body;
            });

        return new Design(global, events);
    }

    private static byte[] Write(Design design)
    {
        var stream = new MemoryStream();
        using (var car = CarArchiveWriter.Open(stream))
        {
            GlobalStatsWriter.Write(ArchiveWriteCursor.For(car), design.Global, design.Events);
        }
        return stream.ToArray();
    }

    private static Design ReadBack(byte[] payload)
    {
        var stream = new MemoryStream(payload);
        var car = CarArchiveReader.Open(stream);
        var cursor = ArchiveCursor.For(car);

        // The version the writer emitted as the record's first field.
        var version = new DesignVersion(cursor.ReadDouble());

        var events = new List<(EventType, IGameEvent)>();
        var global = GlobalStatsReader.Read(
            cursor, version, ArchiveRole.Editor,
            (ar, type, v) =>
            {
                var body = EventBodyReader.TryRead(ar, type, v, ArchiveRole.Editor);
                if (body is not null)
                {
                    events.Add((type, body));
                }
                return body;
            });

        return new Design(global, events);
    }

    public static TheoryData<string> Designs =>
    [
        "Case.dsn/Data",
        "SomethingWild.dsn/Data",
    ];

    [Theory]
    [MemberData(nameof(Designs))]
    public void A_whole_design_record_round_trips(string rel)
    {
        var design = Read(rel);
        if (design is null)
        {
            return;
        }

        Assert.True(GlobalStatsWriter.CanWrite(design.Global, out string reason), reason);

        var read = ReadBack(Write(design));

        AssertSame(design.Global, read.Global);
        Assert.Equal(design.Events.Count, read.Events.Count);
        Assert.Equal(design.Events.Select(e => e.Type), read.Events.Select(e => e.Type));
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Writing_what_was_read_gives_the_same_bytes_the_second_time(string rel)
    {
        // The assertion that catches a field which never went out at all -- and in a record this
        // wide it is the only one that catches it quickly.
        var design = Read(rel);
        if (design is null)
        {
            return;
        }

        byte[] first = Write(design);
        byte[] second = Write(ReadBack(first));

        Assert.Equal(first, second);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void The_shipped_picture_imports_are_already_normalised(string rel)
    {
        // The reference forces picType and calls SetDefaults on every small-pic import before
        // writing it (GlobalData.cpp:4310), and forces picType on every icon import. This writer
        // does neither -- it writes what it read -- and for everything the wire carries the two
        // agree, because a file the reference produced has already been through that
        // normalisation.
        var design = Read(rel);
        if (design is null)
        {
            return;
        }

        const int SmallPicDib = 1024;
        const int IconDib = 64;

        Assert.NotEmpty(design.Global.SmallPicImports);
        Assert.All(design.Global.SmallPicImports, p =>
        {
            Assert.Equal(SmallPicDib, p.PicType);
            Assert.Equal(1, p.NumFrames);
        });

        Assert.NotEmpty(design.Global.IconPicImports);
        Assert.All(design.Global.IconPicImports, p => Assert.Equal(IconDib, p.PicType));

        // The one field where they do NOT agree, and the reason is the same upgrade that makes a
        // 3.64 .chr grow: RestartFrame arrives at 5.24, so a 2.53 or 3.55 design never had one to
        // read. SetDefaults would set it to 1; the port writes the 0 it read, because it has no
        // way to know that is what the reference would have chosen.
        Assert.True(design.Global.Version < DesignVersion.V524);
        Assert.All(design.Global.SmallPicImports, p => Assert.Equal(0, p.RestartFrame));
    }

    [Fact]
    public void The_record_carries_its_own_version_as_its_first_field()
    {
        // No other record does. It is what lets the loading branch tell a magic-prefixed file from
        // one whose first eight bytes are the version itself.
        var design = Read("Case.dsn/Data");
        if (design is null)
        {
            return;
        }

        var stream = new MemoryStream(Write(design));
        var cursor = ArchiveCursor.For(CarArchiveReader.Open(stream));

        Assert.Equal(GlobalStatsWriter.WrittenVersion.Value, cursor.ReadDouble(), 6);
    }

    [Fact]
    public void The_written_version_is_past_the_two_gates_that_force_it()
    {
        // 5.24 would be enough for the embedded PIC_DATA and is not enough for this record:
        // creditsData is read only at 5.25 and above, and CharViewFrameVPArt at 5.26.
        Assert.Equal(DesignVersion.V526, GlobalStatsWriter.WrittenVersion);
        Assert.True(GlobalStatsWriter.WrittenVersion > DesignVersion.V525);
        Assert.True(GlobalStatsWriter.WrittenVersion > CharacterRecordWriter.WrittenVersion);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void The_wall_override_and_cell_content_tables_are_empty_in_every_design(string rel)
    {
        // Stated rather than implied: both tables round-trip at the right size and their contents
        // have unit coverage alone, the same gap the money sack has.
        var design = Read(rel);
        if (design is null)
        {
            return;
        }

        Assert.NotNull(design.Global.Levels);
        Assert.NotEmpty(design.Global.Levels!.Levels);

        Assert.All(design.Global.Levels.Levels.Values, level =>
        {
            Assert.True(level.Overrides is null || level.Overrides.Rows.Count == 0);
            Assert.True(level.Contents is null || level.Contents.Columns.Count == 0);
        });
    }

    private static void AssertSame(GlobalStatsPrefix expected, GlobalStatsPrefix actual)
    {
        Assert.Equal(expected.DesignName, actual.DesignName);
        Assert.Equal(expected.StartLevel, actual.StartLevel);
        Assert.Equal(expected.StartX, actual.StartX);
        Assert.Equal(expected.StartY, actual.StartY);
        Assert.Equal(expected.StartFacing, actual.StartFacing);
        Assert.Equal(expected.StartTime, actual.StartTime);
        Assert.Equal(expected.StartExp, actual.StartExp);
        Assert.Equal(expected.StartExpType, actual.StartExpType);
        Assert.Equal(expected.RetiredStartEquip, actual.RetiredStartEquip);
        Assert.Equal(expected.StartPlatinum, actual.StartPlatinum);
        Assert.Equal(expected.StartGem, actual.StartGem);
        Assert.Equal(expected.StartJewelry, actual.StartJewelry);
        Assert.Equal(expected.DungeonTimeDelta, actual.DungeonTimeDelta);
        Assert.Equal(expected.DungeonSearchTimeDelta, actual.DungeonSearchTimeDelta);
        Assert.Equal(expected.WildernessTimeDelta, actual.WildernessTimeDelta);
        Assert.Equal(expected.WildernessSearchTimeDelta, actual.WildernessSearchTimeDelta);
        Assert.Equal(expected.AutoDarkenViewport, actual.AutoDarkenViewport);
        Assert.Equal(expected.AutoDarkenAmount, actual.AutoDarkenAmount);
        Assert.Equal(expected.StartDarken, actual.StartDarken);
        Assert.Equal(expected.EndDarken, actual.EndDarken);
        Assert.Equal(expected.MinPcs, actual.MinPcs);
        Assert.Equal(expected.MaxPartyMaxPcs, actual.MaxPartyMaxPcs);
        Assert.Equal(expected.Flags, actual.Flags);
        Assert.Equal(expected.MapArt, actual.MapArt);
        Assert.Equal(expected.IconBackgroundArt, actual.IconBackgroundArt);
        Assert.Equal(expected.BackgroundArt, actual.BackgroundArt);
        Assert.Equal(expected.Font, actual.Font);
        Assert.Equal(expected.SmallPicImports, actual.SmallPicImports);
        Assert.Equal(expected.IconPicImports, actual.IconPicImports);
        Assert.Equal(expected.Attributes, actual.Attributes);
        Assert.Equal(expected.Art, actual.Art);
        Assert.Equal(expected.CursorArt, actual.CursorArt);
        Assert.Equal(expected.GlobalEventCount, actual.GlobalEventCount);
        Assert.Equal(expected.Journal, actual.Journal);

        // These three hold attribute lists, which a record compares by reference -- so each has to
        // be descended into rather than compared whole.
        AssertSameObjects(expected.Keys, actual.Keys);
        AssertSameObjects(expected.SpecialItems, actual.SpecialItems);
        AssertSameQuests(expected.Quests, actual.Quests);

        Assert.Equal(expected.Characters.Count, actual.Characters.Count);
        Assert.Equal(expected.Characters.Select(c => c.Name), actual.Characters.Select(c => c.Name));

        AssertSameSounds(expected.Sounds!, actual.Sounds!);
        AssertSameMoney(expected.Money!, actual.Money!);
        Assert.Equal(expected.Difficulty!.DefaultLevel, actual.Difficulty!.DefaultLevel);
        Assert.Equal(expected.Difficulty.Levels, actual.Difficulty.Levels);

        Assert.Equal(expected.Levels!.NumberOfLevels, actual.Levels!.NumberOfLevels);
        Assert.Equal(expected.Levels.Levels.Keys.Order(), actual.Levels.Levels.Keys.Order());
        foreach ((uint index, var level) in expected.Levels.Levels)
        {
            var other = actual.Levels.Levels[index];
            Assert.Equal(level.Name, other.Name);
            Assert.Equal(level.Height, other.Height);
            Assert.Equal(level.Width, other.Width);
            Assert.Equal(level.Used, other.Used);
            Assert.Equal(level.Overland, other.Overland);
            Assert.Equal(level.AreaViewStyle, other.AreaViewStyle);
            Assert.Equal(level.EntryPoints, other.EntryPoints);
            Assert.Equal(level.StepSound, other.StepSound);
            Assert.Equal(level.BumpSound, other.BumpSound);
            Assert.Equal(level.Attributes, other.Attributes);
        }

        Assert.Equal(expected.TitleData?.Titles ?? [], actual.TitleData?.Titles ?? []);
        Assert.Equal(expected.CreditsData?.Titles ?? [], actual.CreditsData?.Titles ?? []);
    }

    private static void AssertSameObjects(IReadOnlyList<SpecialObject> expected,
                                          IReadOnlyList<SpecialObject> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i] with { Attributes = [] },
                         actual[i] with { Attributes = [] });
            Assert.Equal(expected[i].Attributes, actual[i].Attributes);
        }
    }

    private static void AssertSameQuests(IReadOnlyList<Quest> expected, IReadOnlyList<Quest> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i] with { Attributes = [] },
                         actual[i] with { Attributes = [] });
            Assert.Equal(expected[i].Attributes, actual[i].Attributes);
        }
    }

    private static void AssertSameSounds(GlobalSounds expected, GlobalSounds actual)
    {
        Assert.Equal(expected.CharHit, actual.CharHit);
        Assert.Equal(expected.CharMiss, actual.CharMiss);
        Assert.Equal(expected.PartyBump, actual.PartyBump);
        Assert.Equal(expected.PartyStep, actual.PartyStep);
        Assert.Equal(expected.DeathMusic, actual.DeathMusic);
        Assert.Equal(expected.IntroMusic, actual.IntroMusic);
        Assert.Equal(expected.CreditsMusic, actual.CreditsMusic);
        Assert.Equal(expected.CampMusic, actual.CampMusic);
    }

    private static void AssertSameMoney(MoneyData expected, MoneyData actual)
    {
        Assert.Equal(expected with { Coins = [] }, actual with { Coins = [] });
        Assert.Equal(expected.Coins, actual.Coins);
    }
}
