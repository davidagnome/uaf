namespace UAF.Serialization.Tests;

/// <summary>
/// Covers the <c>.pty</c> savegame: its version header, the compressed archive underneath, the
/// <c>PARTY</c> scalars and the event-trigger flags. The rest of the body is not ported — see
/// <see cref="SaveGameReader"/> for what remains.
/// </summary>
public class SaveGameTests
{
    /// <summary>path, version, tasks, party level, x, y, characters, event-flag levels.</summary>
    public static TheoryData<string, double, int, byte, int, int, byte, int> Saves => new()
    {
        { "SomethingWild.dsn/Saves/SaveA.pty", 3.65, 5, 1, 24, 17, 6, 2 },
        { "Ambassador's_Letter/Saves/SaveA.pty", 2.81, 4, 49, 19, 14, 1, 50 },
    };

    private static string? Path_(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string path = System.IO.Path.Combine(dir.FullName, "reference",
            relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void A_save_reads_its_party_and_lands_on_the_engines_own_alignment_tag(
        string relative, double version, int tasks, byte level, int x, int y,
        byte characters, int flagLevels)
    {
        string? path = Path_(relative);
        if (path is null)
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var save = SaveGameReader.Read(stream);

        // The version is read straight off the file rather than through the archive, which is why
        // it is legible in a hex dump while everything after it is not.
        Assert.Equal(version, save.Version.Value, 6);

        Assert.Equal(tasks, save.Party.TaskStack.Count);
        Assert.Equal(level, save.Party.Level);
        Assert.Equal(x, save.Party.PosX);
        Assert.Equal(y, save.Party.PosY);
        Assert.Equal(characters, save.Party.CharacterCount);
        Assert.Equal(flagLevels, save.EventFlags.Count);

        // Reaching this line at all is the real assertion: SaveGameReader throws unless the
        // VISIT_DATA tag lands exactly where the C++ asserts it should, so every field width in
        // PARTY is checked rather than merely plausible. The values above only pin *which*
        // alignment was reached.
        Assert.All(save.EventFlags, f => Assert.Equal(16, f.StepCounts.Length));
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void Party_scalars_are_the_narrow_types_they_are_declared_as(
        string relative, double version, int tasks, byte level, int x, int y,
        byte characters, int flagLevels)
    {
        string? path = Path_(relative);
        if (path is null)
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var party = SaveGameReader.Read(stream).Party;

        // BYTEs interleaved among 4-byte BOOLs (Party.h:599-613). Reading them as ints gives a
        // party at map position (196608, 131072) -- a value that is obviously wrong only if you
        // happen to look, which is how this was found.
        Assert.InRange(party.PosX, 0, 255);
        Assert.InRange(party.PosY, 0, 255);
        Assert.InRange(party.Facing, (byte)0, (byte)7);
        Assert.InRange(party.CharacterCount, (byte)0, (byte)8);
        Assert.InRange(party.Days, 0, 100000);
        Assert.InRange(party.Hours, 0, 23);
        Assert.InRange(party.Minutes, 0, 59);

        _ = (version, tasks, level, x, y, characters, flagLevels);
    }

    [Fact]
    public void A_misaligned_body_is_reported_against_the_tag_rather_than_read_on()
    {
        string? path = Path_("SomethingWild.dsn/Saves/SaveA.pty");
        if (path is null)
        {
            return;
        }

        // Corrupting one byte of the party scalars must surface as a tag mismatch, not as a
        // wrong-but-accepted party. This is what makes the tag worth checking at all.
        byte[] bytes = File.ReadAllBytes(path);
        bytes[64] ^= 0xFF;
        using var stream = new MemoryStream(bytes);

        Assert.ThrowsAny<Exception>(() => SaveGameReader.Read(stream));
    }

    [Fact]
    public void A_version_below_the_engine_floor_is_refused_by_name()
    {
        var bytes = new MemoryStream();
        var writer = new BinaryWriter(bytes);
        writer.Write(0.5);
        writer.Flush();
        bytes.Seek(0, SeekOrigin.Begin);

        var error = Assert.Throws<NotSupportedException>(() => SaveGameReader.Read(bytes));
        Assert.Contains("pre-dates", error.Message);
    }

    [Fact]
    public void A_version_below_the_compressed_threshold_is_refused_separately()
    {
        // Between 0.573 and VersionSpellNames the engine has a second, distinct refusal. Both are
        // reproduced because they report different causes, and a save in that window is a
        // different problem from one that pre-dates the event conversion.
        var bytes = new MemoryStream();
        var writer = new BinaryWriter(bytes);
        writer.Write(0.6);
        writer.Flush();
        bytes.Seek(0, SeekOrigin.Begin);

        var error = Assert.Throws<NotSupportedException>(() => SaveGameReader.Read(bytes));
        Assert.Contains("VersionSpellNames", error.Message);
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void The_whole_party_record_reads(string relative, double version, int tasks,
                                             byte level, int x, int y, byte characters,
                                             int flagLevels)
    {
        string? path = Path_(relative);
        if (path is null)
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var save = SaveGameReader.Read(stream);

        // Twelve slots always, however many are occupied -- the storing side writes
        // MAX_PARTY_MEMBERS records regardless. Reading by the active count would misalign the
        // rest of the save by six whole CHARACTER records.
        Assert.Equal(SaveGameReader.MaxPartyMembers, save.Characters.Count);

        // ...and the occupied ones match the prologue's separate count.
        Assert.Equal(characters, save.Characters.Count(c => c.Name.Length > 0));

        // VISIT_DATA is 255 slots whatever the design's level count, so a one-level design still
        // writes 254 empty pairs.
        Assert.NotEmpty(save.Visited);
        Assert.All(save.Visited, v => Assert.NotEmpty(v.Bitmap));

        Assert.NotNull(save.Pool);
        _ = (version, tasks, level, x, y, flagLevels);
    }

    [Fact]
    public void The_saved_party_is_the_same_six_characters_the_design_ships_as_chr_files()
    {
        string? save = Path_("SomethingWild.dsn/Saves/SaveA.pty");
        if (save is null)
        {
            return;
        }

        string saves = System.IO.Path.GetDirectoryName(save)!;
        var onDisk = Directory.EnumerateFiles(saves, "*.chr")
                              .Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
                              .OrderBy(n => n, StringComparer.Ordinal)
                              .ToList();
        if (onDisk.Count == 0)
        {
            return;
        }

        using var stream = File.OpenRead(save);
        var inSave = SaveGameReader.Read(stream).Characters
                                   .Select(c => c.Name)
                                   .Where(n => n.Length > 0)
                                   .OrderBy(n => n, StringComparer.Ordinal)
                                   .ToList();

        // Two entirely separate readers over two different container framings -- .pty is a
        // compressed CAR, .chr is plain MFC behind a magic -- arriving at the same six names.
        // Nothing in either code path knows about the other.
        Assert.Equal(onDisk, inSave);
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void The_tables_after_the_party_read(string relative, double version, int tasks,
                                                byte level, int x, int y, byte characters,
                                                int flagLevels)
    {
        string? path = Path_(relative);
        if (path is null)
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var save = SaveGameReader.Read(stream);

        // Always the full MAX_GLOBAL_VAULTS -- the count is written as the constant, not as the
        // number in use, so an empty vault still occupies a money sack and an item list.
        Assert.Equal(SaveGameReader.MaxGlobalVaults, save.Vaults.Count);
        Assert.All(save.Vaults, v => Assert.NotNull(v.Money));

        // Quests and special objects are lists the design also carries; a save snapshots them.
        Assert.NotNull(save.Quests);
        Assert.NotNull(save.SpecialItems);
        Assert.NotNull(save.Keys);

        _ = (version, tasks, level, x, y, characters, flagLevels);
    }

    [Fact]
    public void A_saves_special_items_match_the_design_it_was_saved_from()
    {
        string? save = Path_("SomethingWild.dsn/Saves/SaveA.pty");
        string? design = Path_("SomethingWild.dsn/Data/game.dat");
        if (save is null || design is null)
        {
            return;
        }

        using var designStream = File.OpenRead(design);
        var cursor = GameDataReader.Open(designStream);
        var globals = GlobalStatsReader.ReadThroughCharacters(cursor.Body, cursor.Version);

        using var saveStream = File.OpenRead(save);
        var saved = SaveGameReader.Read(saveStream);

        // A save snapshots the design's tables, so the counts agree -- and they are read here by
        // two different paths through two different container framings, neither aware of the
        // other. That agreement is worth more than either count on its own.
        Assert.Equal(globals.SpecialItems.Count, saved.SpecialItems.Count);
        Assert.Equal(globals.Quests.Count, saved.Quests.Count);
    }
}
