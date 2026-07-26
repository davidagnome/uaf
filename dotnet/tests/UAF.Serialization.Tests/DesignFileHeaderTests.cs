using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads the real design files committed at src/UAFWinEd/DefaultDesign.dsn/Data/.
/// These are the primary golden fixtures, so a failure here means the container model in
/// docs/PORTING-PLAN.md section 3.2 is wrong — not that a test needs adjusting.
/// </summary>
public class DesignFileHeaderTests
{
    /// <summary>Walks up from the test binary to the repo root.</summary>
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

    private static string DataFile(string name) =>
        Path.Combine(RepoRoot(), "src", "UAFWinEd", "DefaultDesign.dsn", "Data", name);

    [Fact]
    public void GameDat_has_no_magic_and_falls_back_to_the_plain_archive()
    {
        using var fs = File.OpenRead(DataFile("game.dat"));

        // game.dat carries no magic, so it takes the fallback path. For level/game data the
        // fallback is 0.572 (Level.cpp:2163) -- which is below the 0.573 archive switch, so the
        // payload is a plain CArchive starting at offset 0, with no LZW.
        var header = DesignFileHeader.Read(fs, DesignVersion.V0572);

        Assert.False(header.HadMagic);
        Assert.Equal(0, header.PayloadOffset);
        Assert.Equal(DesignVersion.V0572, header.Version);
        Assert.Equal(ArchiveKind.PlainArchive, header.Archive);
    }

    [Fact]
    public void GameDat_payload_begins_with_GLOBAL_STATS_version_then_design_name()
    {
        using var fs = File.OpenRead(DataFile("game.dat"));
        var header = DesignFileHeader.Read(fs, DesignVersion.V0572);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var reader = new MfcArchiveReader(fs);

        // GlobalData.cpp:3863 -- `ar << version; ar << GetDesignName();`
        // These leading bytes are payload, NOT a container header. Mistaking them for one is the
        // trap called out in the worked example in section 3.2.
        double version = reader.ReadDouble();
        string designName = reader.ReadString();

        Assert.Equal(0.915025, version, precision: 10);
        Assert.Equal("DefaultDesign", designName);
    }

    [Theory]
    [InlineData("items.dat")]
    [InlineData("monsters.dat")]
    [InlineData("spells.dat")]
    [InlineData("Level000.lvl")]
    public void Magic_stamped_files_expose_version_at_offset_8(string fileName)
    {
        using var fs = File.OpenRead(DataFile(fileName));
        var header = DesignFileHeader.Read(fs, DesignVersion.V0572);

        Assert.True(header.HadMagic);
        Assert.Equal(16, header.PayloadOffset);
        Assert.Equal(0.915025, header.Version.Value, precision: 10);
        // 0.915025 >= 0.573, so these use the CAR wrapper -- and Monster.cpp:1305 calls
        // Compress(true), so the payload is LZW-compressed.
        Assert.Equal(ArchiveKind.CompressedArchive, header.Archive);
    }

    [Theory]
    [InlineData("ability.dat", "AbilityV1")]
    [InlineData("baseclass.dat", "BaseclassV1")]
    [InlineData("classes.dat", "ClassV1")]
    [InlineData("races.dat", "RaceV1")]
    [InlineData("spellgroups.dat", "SpGrpV1")]
    [InlineData("traits.dat", "TraitV1")]
    public void Tagged_databases_lead_with_a_counted_type_tag_and_no_version(
        string fileName, string expectedTag)
    {
        using var fs = File.OpenRead(DataFile(fileName));

        // Shape 2: no magic and no version double at all -- the schema version rides in the tag
        // suffix ("V1"), so the DesignVersion gates do not apply to these files.
        var reader = new MfcArchiveReader(fs);
        Assert.Equal(expectedTag, reader.ReadString());

        fs.Seek(0, SeekOrigin.Begin);
        Assert.NotEqual(DesignFileHeader.Magic, new MfcArchiveReader(fs).ReadUInt64());
    }

    [Fact]
    public void Unreliable_range_flag_matches_the_editors_own_warning()
    {
        // Level.cpp:3340 warns for [0.998101, 0.9988]. DefaultDesign at 0.915025 sits below it,
        // so it is safe to use as ground truth.
        using var fs = File.OpenRead(DataFile("game.dat"));
        var header = DesignFileHeader.Read(fs, DesignVersion.V0572);
        Assert.False(header.IsInUnreliableRange);

        Assert.True(new DesignFileHeader(DesignVersion.SpellNames, 16, true,
            ArchiveKind.CompressedArchive).IsInUnreliableRange);
        Assert.False(new DesignFileHeader(DesignVersion.SaveIDs, 16, true,
            ArchiveKind.CompressedArchive).IsInUnreliableRange);
    }
}
