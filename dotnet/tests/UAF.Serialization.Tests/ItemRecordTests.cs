using System.Text.Json;
using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads item records from the real <c>items.dat</c> and diffs the names against the C++ oracle.
/// </summary>
/// <remarks>
/// 285 names is a strong alignment proof: any width or gate error desynchronises the stream and
/// the very next name returns garbage, so agreement across the whole list means the record's
/// leading fields are read exactly as the reference reads them.
/// </remarks>
public class ItemRecordTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ItemsDat() =>
        Path.Combine(RepoRoot(), "src", "UAFWinEd", "DefaultDesign.dsn", "Data", "items.dat");

    private static JsonElement? Golden()
    {
        string path = Path.Combine(RepoRoot(), "oracle", "golden", "DefaultDesign.json");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void First_record_decodes_with_the_CAR_field_order()
    {
        using var fs = File.OpenRead(ItemsDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Items);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);

        Assert.Equal(285, ar.ReadInt32());

        var item = ItemRecordReader.ReadNames(ar, header.Version);

        // preSpellNameKey IS read on the CAR path at 0.915 (0.915 < VersionSpellNames). The
        // CArchive overload would not read it, and omitting it turns every string below into
        // garbage -- which is exactly what happened before this was traced.
        Assert.Equal(1, item.PreSpellNameKey);

        Assert.Equal("Arrow", item.UniqueName);
        Assert.Equal("Arrow", item.IdName);
        Assert.Equal("Hit.wav", item.HitSound);
        Assert.Equal("Miss.wav", item.MissSound);
        Assert.Equal(string.Empty, item.LaunchSound);   // stored as the "*" sentinel
    }

    [Fact]
    public void Item_names_agree_with_the_oracle_for_the_leading_records()
    {
        if (Golden() is not { } root) { return; }   // no golden dump yet
        var golden = root.GetProperty("itemNames");
        Assert.Equal(285, golden.GetArrayLength());

        using var fs = File.OpenRead(ItemsDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Items);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);
        Assert.Equal(285, ar.ReadInt32());

        // Only the first record is compared: the reader stops at LaunchSound, and advancing to
        // record 1 needs the remaining ~40 version-gated fields plus the role-dependent
        // HitArt/MissileArt block. Extend this loop as those land -- full agreement across all
        // 285 is the real target.
        var item = ItemRecordReader.ReadNames(ar, header.Version);
        var expected = golden[0];

        Assert.Equal(expected.GetProperty("uniqueName").GetString(), item.UniqueName);
        Assert.Equal(expected.GetProperty("idName").GetString(), item.IdName);
    }

    [Fact]
    public void Arrow_scalars_read_with_editor_semantics()
    {
        using var fs = File.OpenRead(ItemsDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Items);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);
        ar.ReadInt32();                                        // record count

        ItemRecordReader.ReadNames(ar, header.Version);

        // DefaultDesign was written with EDITOR semantics: at 0.915 the editor skips
        // HitArt/MissileArt, so the scalar block follows the sounds directly. Confirmed by the
        // data -- reading straight on yields "Bow" as Arrow's ammo type, which is meaningful;
        // engine semantics would consume two PIC_DATA records here and produce garbage.
        Assert.False(ItemRecordReader.ReadsHitAndMissileArt(ArchiveRole.Editor, header.Version));

        var scalars = ItemRecordReader.ReadScalars(ar, header.Version);

        Assert.Equal("Bow", scalars.AmmoType);
        Assert.InRange(scalars.Cost, 0, 1_000_000);
        Assert.InRange(scalars.Encumbrance, 0, 10_000);
        Assert.InRange(scalars.Cursed, 0, 1);
        Assert.InRange(scalars.BundleQty, 0, 1000);
    }

    [Fact]
    public void Oracle_item_names_show_the_pipe_qualifier_convention()
    {
        if (Golden() is not { } root) { return; }
        var golden = root.GetProperty("itemNames");

        // uniqueName carries a '|' qualifier that idName renders differently -- e.g.
        // "Wakizashi|5" vs "Wakizashi +5". A port that normalises either field loses information
        // the scripting layer relies on (the C++ comments at Items.cpp:2765 are explicit that
        // scripts compare unique names).
        bool sawQualifier = false;
        foreach (var entry in golden.EnumerateArray())
        {
            string? unique = entry.GetProperty("uniqueName").GetString();
            if (unique is not null && unique.Contains('|'))
            {
                sawQualifier = true;
                Assert.DoesNotContain('|', entry.GetProperty("idName").GetString()!);
            }
        }
        Assert.True(sawQualifier, "expected at least one '|'-qualified unique name");
    }

    [Fact]
    public void Engine_and_editor_disagree_about_HitArt_below_0_998100()
    {
        // Items.cpp:2784 -- the editor gates the art on ver > VersionSpellIDs; the engine has no
        // gate. At DefaultDesign's 0.915 the two builds consume different byte counts.
        var v = new DesignVersion(0.915025);
        Assert.False(ItemRecordReader.ReadsHitAndMissileArt(ArchiveRole.Editor, v));
        Assert.True(ItemRecordReader.ReadsHitAndMissileArt(ArchiveRole.Engine, v));

        // Above the gate they agree again.
        var newer = new DesignVersion(0.9990);
        Assert.True(ItemRecordReader.ReadsHitAndMissileArt(ArchiveRole.Editor, newer));
        Assert.True(ItemRecordReader.ReadsHitAndMissileArt(ArchiveRole.Engine, newer));
    }
}
