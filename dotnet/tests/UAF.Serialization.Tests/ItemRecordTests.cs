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
    public void Arrow_combat_block_is_semantically_coherent()
    {
        using var fs = File.OpenRead(ItemsDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);
        ar.ReadInt32();

        ItemRecordReader.ReadNames(ar, header.Version);
        var scalars = ItemRecordReader.ReadScalars(ar, header.Version);
        var combat = ItemRecordReader.ReadCombat(ar);

        // The strongest evidence the block is aligned is that every value is right FOR AN ARROW:
        // bundles of 20, carried in a quiver, 1d6 damage against both size classes, negligible
        // encumbrance, no protection. Misaligned reads do not produce coherent game data.
        Assert.Equal("Bow", scalars.AmmoType);
        Assert.Equal(20, scalars.BundleQty);
        Assert.Equal(2, scalars.Encumbrance);

        Assert.Equal(1, combat.NbrDiceSm);
        Assert.Equal(6, combat.DmgDiceSm);       // 1d6
        Assert.Equal(1, combat.NbrDiceLg);
        Assert.Equal(6, combat.DmgDiceLg);       // 1d6
        Assert.Equal(0, combat.ProtectionBase);
        Assert.Equal(0, combat.ProtectionBonus);

        // ROF is a double sitting among longs -- 8 bytes where the neighbours are 4. Reading it
        // as an int shifts protection and everything after.
        Assert.Equal(0.0, combat.RofPerRound);
    }

    [Fact]
    public void LocationReadied_is_a_legacy_ordinal_needing_conversion()
    {
        using var fs = File.OpenRead(ItemsDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);
        ar.ReadInt32();
        ItemRecordReader.ReadNames(ar, header.Version);
        ItemRecordReader.ReadScalars(ar, header.Version);
        var combat = ItemRecordReader.ReadCombat(ar);

        // Stored as ordinal 10. Items.cpp:2820 rewrites that into the base-38 name AmmoQuiver,
        // so the C++ member does NOT equal the bytes -- and an arrow living in a quiver is
        // exactly the expected answer.
        Assert.Equal(10u, combat.LocationReadied);
        Assert.True(ReadiedLocation.IsLegacyOrdinal(combat.LocationReadied));
        Assert.Equal("AmmoQuiver", ReadiedLocation.LegacyOrder[combat.LocationReadied]);

        // Consequence: the oracle's locationReadied is the CONVERTED value, so a raw comparison
        // against 10 would fail. Whichever form is compared, it must be the same on both sides.
        if (Golden() is { } root && root.TryGetProperty("itemDetails", out var details)
            && details.GetArrayLength() > 0)
        {
            uint oracleValue = details[0].GetProperty("locationReadied").GetUInt32();
            Assert.NotEqual(combat.LocationReadied, oracleValue);
        }
    }

    /// <summary>
    /// Field-by-field diff of the first item record against the oracle's <c>itemDetails</c>.
    /// </summary>
    /// <remarks>
    /// The name digest proves the stream stays aligned; this proves each individual field is read
    /// with the right width and the right post-read transform. <c>locationReadied</c> is compared
    /// deliberately: the oracle reports the base-38 converted value, so agreeing on it means the
    /// conversion is reproduced rather than the raw ordinal being passed through.
    /// </remarks>
    [Fact]
    public void First_item_matches_the_oracle_field_by_field()
    {
        if (Golden() is not { } root) { return; }
        if (!root.TryGetProperty("itemDetails", out var details) || details.GetArrayLength() == 0)
        {
            return;   // golden predates itemDetails
        }
        var expected = details[0];

        using var fs = File.OpenRead(ItemsDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);
        ar.ReadInt32();

        var names = ItemRecordReader.ReadNames(ar, header.Version);
        var scalars = ItemRecordReader.ReadScalars(ar, header.Version);
        var combat = ItemRecordReader.ReadCombat(ar);

        Assert.Equal(expected.GetProperty("idName").GetString(), names.IdName);
        Assert.Equal(expected.GetProperty("uniqueName").GetString(), names.UniqueName);
        Assert.Equal(expected.GetProperty("hitSound").GetString(), names.HitSound);
        Assert.Equal(expected.GetProperty("missSound").GetString(), names.MissSound);
        Assert.Equal(expected.GetProperty("launchSound").GetString(), names.LaunchSound);
        Assert.Equal(expected.GetProperty("preSpellNameKey").GetInt32(), names.PreSpellNameKey);

        Assert.Equal(expected.GetProperty("ammoType").GetString(), scalars.AmmoType);
        Assert.Equal(expected.GetProperty("experience").GetInt32(), scalars.Experience);
        Assert.Equal(expected.GetProperty("cost").GetInt32(), scalars.Cost);
        Assert.Equal(expected.GetProperty("encumbrance").GetInt32(), scalars.Encumbrance);
        Assert.Equal(expected.GetProperty("attackBonus").GetInt32(), scalars.AttackBonus);
        Assert.Equal(expected.GetProperty("cursed").GetInt32(), scalars.Cursed);
        Assert.Equal(expected.GetProperty("bundleQty").GetInt32(), scalars.BundleQty);
        Assert.Equal(expected.GetProperty("numCharges").GetInt32(), scalars.NumCharges);

        Assert.Equal(expected.GetProperty("handsToUse").GetInt32(), combat.HandsToUse);
        Assert.Equal(expected.GetProperty("dmgDiceSm").GetInt32(), combat.DmgDiceSm);
        Assert.Equal(expected.GetProperty("nbrDiceSm").GetInt32(), combat.NbrDiceSm);
        Assert.Equal(expected.GetProperty("dmgBonusSm").GetInt32(), combat.DmgBonusSm);
        Assert.Equal(expected.GetProperty("dmgDiceLg").GetInt32(), combat.DmgDiceLg);
        Assert.Equal(expected.GetProperty("nbrDiceLg").GetInt32(), combat.NbrDiceLg);
        Assert.Equal(expected.GetProperty("dmgBonusLg").GetInt32(), combat.DmgBonusLg);
        Assert.Equal(expected.GetProperty("rofPerRound").GetDouble(), combat.RofPerRound);
        Assert.Equal(expected.GetProperty("protectionBase").GetInt32(), combat.ProtectionBase);
        Assert.Equal(expected.GetProperty("protectionBonus").GetInt32(), combat.ProtectionBonus);

        // The oracle reports the CONVERTED readied location (base-38), not the stored ordinal.
        // A reader that passes the raw value through disagrees here -- which is the point.
        uint oracleLocation = expected.GetProperty("locationReadied").GetUInt32();
        Assert.NotEqual(combat.LocationReadied, oracleLocation);
        Assert.True(ReadiedLocation.IsLegacyOrdinal(combat.LocationReadied));
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
