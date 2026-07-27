using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads the ASL (attribute/string list) block that terminates most records.
/// </summary>
/// <remarks>
/// Located in the real <c>game.dat</c> by searching for its map-name marker rather than by
/// reading every preceding structure — the block is self-describing enough to verify in place,
/// which is exactly the property that makes it a good desync detector.
/// </remarks>
public class AslReaderTests
{
    private const string GlobalStatsMap = AslMaps.GlobalStats;

    private static string GameDat()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "UAFWinEd", "DefaultDesign.dsn", "Data", "game.dat");
    }

    /// <summary>Finds the counted-string map marker and returns the offset of its length byte.</summary>
    private static long FindMapMarker(byte[] data, string mapName)
    {
        byte[] needle = System.Text.Encoding.ASCII.GetBytes(mapName);
        for (int i = 1; i < data.Length - needle.Length; i++)
        {
            if (data[i - 1] != needle.Length) continue;
            bool match = true;
            for (int j = 0; j < needle.Length && match; j++)
            {
                match = data[i + j] == needle[j];
            }
            if (match) return i - 1;
        }
        return -1;
    }

    [Fact]
    public void Global_stats_asl_reads_its_entries()
    {
        byte[] data = File.ReadAllBytes(GameDat());
        long offset = FindMapMarker(data, GlobalStatsMap);
        Assert.True(offset > 0, "ASL marker not found");

        using var ms = new MemoryStream(data);
        ms.Seek(offset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(ms);

        var entries = AslReader.Read(ar, new DesignVersion(0.915025), GlobalStatsMap);

        // Verified byte-for-byte against an independent decode of the real file. Pinning the
        // exact contents rather than just "4 printable entries" is deliberate: an off-by-one in
        // the count width (WORD vs int) still yields plausible-looking output, which is precisely
        // how SPELL_ID was mis-modelled three times before the oracle diff caught it.
        Assert.Equal(
            [
                ("RunAsVersion",             (byte)0x05, "0.9140"),
                ("GuidedTourVersion",        (byte)0x05, "0.9140"),
                ("SpecialItemKeyQtyVersion", (byte)0x05, "0.9140"),
                ("ItemUseEventVersion",      (byte)0x05, "0.9140"),
            ],
            entries.Select(e => (e.Key, e.Flags, e.Value)));

        // 0x05 == ASLF_READONLY | ASLF_DESIGN == ASLF_EDITOR (ASL.h:154).
        Assert.All(entries, e => Assert.Equal(AslFlags.Editor, (AslFlags)e.Flags));
    }

    [Fact]
    public void Stream_is_left_positioned_exactly_at_the_end_of_the_block()
    {
        // The whole reason ASL blocks the port is that records cannot be walked past them. A
        // reader that gets the contents right but leaves the position wrong is still useless,
        // and every per-field test would still pass.
        byte[] data = File.ReadAllBytes(GameDat());
        long offset = FindMapMarker(data, GlobalStatsMap);

        using var ms = new MemoryStream(data);
        ms.Seek(offset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(ms);
        AslReader.Read(ar, new DesignVersion(0.915025), GlobalStatsMap);

        // Independently observed continuation: four zero bytes, then a counted string of 12.
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, ar.ReadBytes(4));
        Assert.Equal(12, ms.ReadByte());
    }

    [Fact]
    public void Savegame_variant_drops_readonly_entries_but_keeps_the_wire_format()
    {
        // ASL.cpp:1489 -- Save() counts and writes only entries WITHOUT ASLF_READONLY, while
        // Serialize() (the design path, ASL.cpp:1386) writes all of them. Same layout either
        // way, so one reader serves both; the distinction matters only when writing.
        //
        // Note what this means for GLOBAL_STATS_ATTRIBUTES: every entry is 0x05, which includes
        // ASLF_READONLY, so a savegame writes a count of 0 for a block that has 4 entries in
        // the design. That is correct behaviour, not data loss.
        AslEntry[] entries =
        [
            new("RunAsVersion", (byte)AslFlags.Editor, "0.9140"),
            new("PlayerChoice", (byte)AslFlags.Modified, "yes"),
        ];

        Assert.Equal(1, entries.Count(AslReader.IsSavedInSavegame));
        Assert.Equal("PlayerChoice", entries.Single(AslReader.IsSavedInSavegame).Key);
    }

    [Fact]
    public void Wrong_map_name_is_treated_as_a_desynchronised_stream()
    {
        byte[] data = File.ReadAllBytes(GameDat());
        long offset = FindMapMarker(data, GlobalStatsMap);

        using var ms = new MemoryStream(data);
        ms.Seek(offset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(ms);

        // The reference throws 7 on a mismatch (ASL.cpp:1420). Preserving that is the point:
        // the marker is the cheapest reliable signal that the stream has drifted, and a reader
        // that skipped past it would lose the one built-in checkpoint the format offers.
        var ex = Assert.Throws<InvalidDataException>(
            () => AslReader.Read(ar, new DesignVersion(0.915025), "SOMETHING_ELSE"));
        Assert.Contains(GlobalStatsMap, ex.Message);
    }

    [Fact]
    public void Block_is_absent_entirely_below_0_505()
    {
        // _ASL_LEVEL_ is _VERSION_0505_ (Externs.h:185). Below it the block is not merely empty
        // -- nothing is read at all, so a reader must not consume a name or a count.
        using var ms = new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF]);
        var ar = new MfcArchiveReader(ms);

        var entries = AslReader.Read(ar, new DesignVersion(0.500), GlobalStatsMap);

        Assert.Empty(entries);
        Assert.Equal(0, ms.Position);        // nothing consumed
        Assert.False(AslReader.IsPresent(new DesignVersion(0.500)));
        Assert.True(AslReader.IsPresent(DesignVersion.V0505));
    }

    [Fact]
    public void Compressed_path_applies_a_key_fixup_the_plain_path_does_not()
    {
        // ASL.cpp:1236 -- the CAR overload adds 0x20 to every character below 0x20; the CArchive
        // twin (ASL.cpp:1247) reads the key verbatim. The same key therefore differs between a
        // compressed and an uncompressed design, so the fixup must NOT be applied unconditionally.
        // 0x01 -> 0x21 '!', 0x02 -> 0x22 '"'
        Assert.Equal("!\"", AslReader.FixUpCompressedKey("\u0001\u0002"));
        Assert.Equal("Normal", AslReader.FixUpCompressedKey("Normal"));   // untouched
        Assert.Equal("\u0020", AslReader.FixUpCompressedKey("\u0020"));   // 0x20 is NOT below 0x20
        Assert.Equal("?", AslReader.FixUpCompressedKey("\u001f"));   // 0x1f -> 0x3f, the highest shifted char
    }
}
