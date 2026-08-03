using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips the body of a shipped <c>.pty</c> savegame, as far as the reader goes.
/// </summary>
/// <remarks>
/// The writer stops where the reader does — a save continues past the vaults with an
/// <c>ACTIVE_SPELL_LIST</c> and seven <c>Save</c> calls, none of which has a reader yet. So this
/// proves the body round-trips, not that the file loads.
/// </remarks>
public class SaveGameWriterTests
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

    private static List<string> Saves()
    {
        var root = RepoRoot();
        if (root is null)
        {
            return [];
        }

        var paths = new List<string>();
        foreach (string design in (string[])["Ambassador's_Letter", "SomethingWild.dsn"])
        {
            string dir = Path.Combine(root.FullName, "reference", design, "Saves");
            if (Directory.Exists(dir))
            {
                paths.AddRange(Directory.EnumerateFiles(dir, "*.pty"));
            }
        }
        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    private static byte[] Write(SaveGame save)
    {
        var stream = new MemoryStream();
        SaveGameWriter.Write(stream, save);
        return stream.ToArray();
    }

    private static SaveGame ReadBack(byte[] file)
    {
        var stream = new MemoryStream(file);
        return SaveGameReader.Read(stream);
    }

    [Fact]
    public void Every_shipped_save_round_trips()
    {
        var saves = Saves();
        if (saves.Count == 0)
        {
            return;
        }

        // Both of them. An earlier revision of this list named a design that ships no save at all
        // and covered one file while looking like it covered two.
        Assert.Equal(2, saves.Count);

        foreach (string path in saves)
        {
            var save = SaveGameReader.Read(path);
            Assert.True(SaveGameWriter.CanWrite(save, out string reason),
                        $"{Path.GetFileName(path)}: {reason}");

            AssertSame(save, ReadBack(Write(save)));
        }
    }

    [Fact]
    public void Writing_what_was_read_gives_the_same_bytes_the_second_time()
    {
        foreach (string path in Saves())
        {
            byte[] first = Write(SaveGameReader.Read(path));
            byte[] second = Write(ReadBack(first));

            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void The_visit_data_tag_lands_where_the_engine_asserts_on_it()
    {
        // The engine's own alignment check, and the only thing that makes PARTY's field widths
        // checkable at all -- a save is a dozen structures deep and nothing downstream would
        // notice a drift. That the written file reads back at all means the tag landed.
        var saves = Saves();
        if (saves.Count == 0)
        {
            return;
        }

        var save = SaveGameReader.Read(saves[0]);

        // Reading the written bytes exercises the same check the reader applies.
        var read = ReadBack(Write(save));
        Assert.Equal(save.Visited.Count, read.Visited.Count);
    }

    [Fact]
    public void The_fixed_tables_are_written_at_full_size()
    {
        var saves = Saves();
        if (saves.Count == 0)
        {
            return;
        }

        var read = ReadBack(Write(SaveGameReader.Read(saves[0])));

        // MAX_GLOBAL_VAULTS vaults, whatever the game holds -- the loading branch dies on any
        // other count.
        Assert.Equal(SaveGameReader.MaxGlobalVaults, read.Vaults.Count);

        // And every step counter is its full sixteen zones, because it is a raw struct blit.
        Assert.All(read.EventFlags,
                   f => Assert.Equal(SaveGameWriter.StepCounterZones, f.StepCounts.Length));
    }

    [Fact]
    public void The_header_is_uncompressed_and_the_body_is_not()
    {
        // The sixth container framing, and the only one where compression starts after a bare
        // scalar rather than after a magic.
        var saves = Saves();
        if (saves.Count == 0)
        {
            return;
        }

        byte[] written = Write(SaveGameReader.Read(saves[0]));

        Assert.Equal(SaveGameWriter.WrittenVersion.Value, BitConverter.ToDouble(written, 0), 6);

        // Byte 8 is CAR's compression-type marker, which the reference writes as 2.
        Assert.Equal(2, written[8]);
    }

    [Fact]
    public void The_tail_carries_a_saved_attribute_list_per_database()
    {
        // The seven Save/Restore pairs. Each is a count, then a name and an attribute list per
        // record -- the name being how the loader matches a saved record against the design it is
        // loading into.
        var saves = Saves();
        if (saves.Count == 0)
        {
            return;
        }

        var tail = SaveGameReader.Read(saves[0]).Tail;

        Assert.NotEmpty(tail.Spells);
        Assert.NotEmpty(tail.Items);
        Assert.NotEmpty(tail.Monsters);
        Assert.All(tail.Spells, s => Assert.NotEmpty(s.Name));
        Assert.Equal(SaveGameTailReaders.LevelSlots, tail.Levels.Count);
    }

    [Fact]
    public void Neither_shipped_save_has_an_active_spell()
    {
        // Which is why the one place the format's storing and loading branches disagree cannot be
        // checked against real data: ACTIVE_SPELL stores Lingers/casterLevel/lingerData and loads
        // Lingers/lingerData/casterLevel. The writer follows the loading order deliberately -- see
        // SaveGameTailWriters.WriteActiveSpell -- and no corpus file can tell the difference.
        foreach (string path in Saves())
        {
            Assert.Empty(SaveGameReader.Read(path).Tail.ActiveSpells);
        }
    }

    [Fact]
    public void A_saved_level_carries_a_sparse_wall_override_table()
    {
        // The gap that had been documented as never mattering, mattering: a savegame's LEVEL_STATS
        // really does carry a table whose entries are mostly absent -1 placeholders. Keeping only
        // the present rows made it unwritable, which is what forced the entries list.
        var saves = Saves();
        if (saves.Count == 0)
        {
            return;
        }

        var tail = SaveGameReader.Read(saves[0]).Tail;

        var sparse = tail.Levels
            .Select(l => l.Overrides)
            .FirstOrDefault(o => o is not null && o.Entries.Count != o.Rows.Count);

        Assert.NotNull(sparse);
        Assert.Contains(sparse!.Entries, e => e.Row is null);
        Assert.Contains(sparse.Entries, e => e.Row is not null);
    }

    private static void AssertSame(SaveGame expected, SaveGame actual)
    {
        Assert.Equal(expected.Party.TaskStack.Count, actual.Party.TaskStack.Count);
        for (int i = 0; i < expected.Party.TaskStack.Count; i++)
        {
            Assert.Equal(expected.Party.TaskStack[i].Id, actual.Party.TaskStack[i].Id);
            Assert.Equal(expected.Party.TaskStack[i].Flags, actual.Party.TaskStack[i].Flags);
            Assert.Equal(expected.Party.TaskStack[i].Data, actual.Party.TaskStack[i].Data);
        }

        Assert.Equal(expected.Party with { TaskStack = actual.Party.TaskStack },
                     actual.Party);

        Assert.Equal(expected.EventFlags.Count, actual.EventFlags.Count);
        for (int i = 0; i < expected.EventFlags.Count; i++)
        {
            Assert.Equal(expected.EventFlags[i].StepCounts, actual.EventFlags[i].StepCounts);
            Assert.Equal(expected.EventFlags[i].EventResults, actual.EventFlags[i].EventResults);
        }

        Assert.Equal(expected.Visited.Count, actual.Visited.Count);
        for (int i = 0; i < expected.Visited.Count; i++)
        {
            Assert.Equal(expected.Visited[i].Level, actual.Visited[i].Level);
            Assert.Equal(expected.Visited[i].Bitmap, actual.Visited[i].Bitmap);
        }

        Assert.Equal(expected.Blockages, actual.Blockages);
        Assert.Equal(expected.Journal, actual.Journal);
        Assert.Equal(expected.Attributes, actual.Attributes);

        Assert.Equal(expected.Characters.Count, actual.Characters.Count);
        Assert.Equal(expected.Characters.Select(c => c.Name), actual.Characters.Select(c => c.Name));

        Assert.Equal(expected.Pool!.Coins, actual.Pool!.Coins);
        Assert.Equal(expected.Pool.Gems, actual.Pool.Gems);

        Assert.Equal(expected.Quests.Count, actual.Quests.Count);
        Assert.Equal(expected.Quests.Select(q => q.Name), actual.Quests.Select(q => q.Name));
        Assert.Equal(expected.SpecialItems.Select(s => s.Name),
                     actual.SpecialItems.Select(s => s.Name));
        Assert.Equal(expected.Keys.Select(k => k.Name), actual.Keys.Select(k => k.Name));

        // The vault table is padded to its full size on the way out, so the count only has to be
        // at least what came in.
        Assert.True(actual.Vaults.Count >= expected.Vaults.Count);
    }
}
