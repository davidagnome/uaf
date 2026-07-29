using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Walks the event list of a real level, reading its events as concrete types.
/// </summary>
/// <remarks>
/// <para>
/// The first end-to-end event read. <c>COMBAT_EVENT_DATA</c> is the only concrete subclass ported
/// so far, so this covers DefaultDesign's Level000 — whose two entries happen to be a combat event
/// and a <c>NoEvent</c>, exercising both the full path and the null-dispatch path.
/// </para>
/// <para>
/// Levels with other event types cannot be walked until their subclasses land; the walk stops at
/// the first unported type rather than guessing.
/// </para>
/// </remarks>
public class CombatEventTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>Positions a cursor at the start of the level's event list.</summary>
    private static (IArchiveCursor Cursor, FileStream Stream, DesignVersion Version, int Count)
        OpenEventList(string rel)
    {
        var fs = File.OpenRead(Path.Combine(RepoRoot().FullName, rel));
        var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);
        var ar = new MfcArchiveReader(fs);

        var (w, h) = LevelReader.ReadDimensions(ar);
        for (int i = 0; i < w * h; i++)
        {
            LevelReader.ReadCell(ar, header.Version);
        }

        var cursor = ArchiveCursor.For(ar);
        cursor.ReadInt32();                          // m_level
        return (cursor, fs, header.Version, cursor.ReadInt32());
    }

    [Fact]
    public void Combat_event_reads_its_text_and_sounds()
    {
        var (cursor, fs, version, count) = OpenEventList(
            "src/UAFWinEd/DefaultDesign.dsn/Data/Level000.lvl");
        using (fs)
        {
            Assert.Equal(2, count);

            Assert.Equal(EventType.Combat, (EventType)cursor.ReadInt32());
            var combat = CombatEventReader.Read(cursor, version, ArchiveRole.Editor);

            // Meaningful English from deep inside the record is the strongest signal that the
            // whole base -- control block, two PIC_DATA, ids, chain ids -- was read correctly.
            Assert.Equal("Test Event for combat walls", combat.Base.Text);
            Assert.Equal("CharDeath.wav", combat.DeathSound);
            Assert.Equal(EventType.Combat, (EventType)combat.Base.EventType);

            // This encounter defines no monsters, which is legal: the list is a plain count.
            Assert.Empty(combat.Monsters);
        }
    }

    [Fact]
    public void NoEvent_entry_consumes_exactly_four_bytes()
    {
        var (cursor, fs, version, _) = OpenEventList(
            "src/UAFWinEd/DefaultDesign.dsn/Data/Level000.lvl");
        using (fs)
        {
            cursor.ReadInt32();                      // event 0's ordinal
            CombatEventReader.Read(cursor, version, ArchiveRole.Editor);

            long before = fs.Position;
            var second = (EventType)cursor.ReadInt32();

            // CreateNewEvent returns null, so Serialize is never called and the entry is just its
            // ordinal. Confirmed here against real data rather than inferred from the source.
            Assert.Equal(EventType.NoEvent, second);
            Assert.True(EventDispatch.ReadsNothing(second));
            Assert.Equal(4, fs.Position - before);
        }
    }

    [Fact]
    public void Event_list_ends_before_the_rest_of_the_level()
    {
        var (cursor, fs, version, count) = OpenEventList(
            "src/UAFWinEd/DefaultDesign.dsn/Data/Level000.lvl");
        using (fs)
        {
            for (int i = 0; i < count; i++)
            {
                var type = (EventType)cursor.ReadInt32();
                if (EventDispatch.ReadsNothing(type)) continue;

                Assert.Equal(EventType.Combat, type);
                CombatEventReader.Read(cursor, version, ArchiveRole.Editor);
            }

            // zoneData, the level ASL, step events and the wall sets all follow, so the list must
            // NOT land on EOF -- that would mean it had over-read.
            Assert.True(fs.Position < fs.Length);
            Assert.True(fs.Position > 0);
        }
    }
}
