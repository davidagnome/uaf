using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Walks the tail of <c>GLOBAL_STATS</c> using the <b>loading</b> branch
/// (<c>GlobalData.cpp:3992</c>), which is not a mirror of the storing branch.
/// </summary>
/// <remarks>
/// Two independent traps are pinned here, both of which silently desynchronise the record:
/// transcribing the writer instead of the reader (which omits <c>CreditsBgArt</c>), and using the
/// container version instead of the payload's own version for the content gates.
/// See docs/PORTING-PLAN.md section 3.2.
/// </remarks>
public class GlobalStatsLoadBranchTests
{
    private const int LogFontSize = 60;   // sizeof(LOGFONTA): 5 LONG + 8 BYTE + CHAR[32]

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

    private static MfcArchiveReader SeekPastScalarPrefix(FileStream fs, out DesignVersion contentVersion)
    {
        var header = DesignFileHeader.Read(fs, DesignFileKind.GameData);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);

        contentVersion = new DesignVersion(ar.ReadDouble());   // ar >> version -- the CONTENT version
        ar.ReadString();                                       // designName
        ar.ReadInt32();                                        // startLevel
        ar.Skip(3);                                            // startX/Y/Facing
        for (int i = 0; i < 7; i++) ar.ReadInt32();            // startTime..startJewelry (incl. junk)
        for (int i = 0; i < 4; i++) ar.ReadInt32();            // time deltas
        for (int i = 0; i < 4; i++) ar.ReadInt32();            // darken block
        for (int i = 0; i < 3; i++) ar.ReadInt32();            // minPCs, maxParty_maxPCs, flags
        return ar;
    }

    [Fact]
    public void For_game_dat_the_container_and_content_versions_are_the_same_bytes()
    {
        using var fs = File.OpenRead(GameDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.GameData);

        SeekPastScalarPrefix(fs, out var contentVersion);

        // For an unstamped game.dat these are literally the same eight bytes, read twice: once by
        // GetDesignVersion to choose the archive (Globals.cpp:3460 seeks back to 0 when there is
        // no magic), and once by GLOBAL_STATS::Serialize as its first field.
        //
        // An earlier revision of this test asserted they DIFFER, on the mistaken belief that
        // game.dat used LoadLevel's 0.572 fallback. It does not -- that rule belongs to *.lvl.
        Assert.Equal(header.Version.Value, contentVersion.Value, precision: 10);
        Assert.Equal(0.915025, contentVersion.Value, precision: 10);

        // The distinction between the two is still real for *.lvl, where the container falls back
        // to a literal 0.572 that has nothing to do with the payload's contents.
        Assert.Equal(DesignVersion.V0572, DesignFileKind.LevelData.UnstampedFallback);
        Assert.Equal(UnstampedVersionSource.PayloadFirstField, DesignFileKind.GameData.UnstampedSource);
        Assert.Equal(UnstampedVersionSource.FixedFallback, DesignFileKind.LevelData.UnstampedSource);

        Assert.True(contentVersion >= DesignVersion.V0830, "content version selects the LOGFONT blob branch");
    }

    [Fact]
    public void Load_branch_reads_CreditsBgArt_which_the_store_branch_never_hints_at()
    {
        using var fs = File.OpenRead(GameDat());
        var ar = SeekPastScalarPrefix(fs, out var version);

        string mapArt = ArchiveStringConventions.Decode(ar.ReadString());

        // version >= 0.830, so the font is a raw 60-byte LOGFONTA blob rather than name + size.
        Assert.True(version >= DesignVersion.V0830);
        byte[] logFont = ar.ReadBytes(LogFontSize);

        // version >= 0.800, so TitleBgArt is NOT present.
        Assert.True(version >= DesignVersion.V0800);

        // version >= 0.660 -> IconBgArt then BackgroundArt.
        Assert.True(version >= DesignVersion.V0660);
        string iconBgArt = ArchiveStringConventions.Decode(ar.ReadString());
        string backgroundArt = ArchiveStringConventions.Decode(ar.ReadString());

        // 0.566 <= version < 5.25 -> CreditsBgArt. Omitting this is what produces a garbage count.
        Assert.True(version >= DesignVersion.V0566 && version < DesignVersion.V525);
        string creditsBgArt = ArchiveStringConventions.Decode(ar.ReadString());

        int smallPicCount = ar.ReadInt32();

        Assert.Equal("AreaViewArt.png", mapArt);
        Assert.Equal("defib.png", iconBgArt);
        Assert.Equal(string.Empty, backgroundArt);   // stored as "*", the ArchiveBlank sentinel
        Assert.Equal("Credits.jpg", creditsBgArt);

        // A plausible count is the payoff: drop CreditsBgArt and this reads as 1701987083.
        Assert.Equal(18, smallPicCount);

        // The LOGFONT blob matches FillDefaultFontData("SYSTEM", 16, &logfont).
        Assert.Equal(16, BitConverter.ToInt32(logFont, 0));    // lfHeight
        Assert.Equal(700, BitConverter.ToInt32(logFont, 16));  // lfWeight (FW_BOLD)
        string face = System.Text.Encoding.ASCII.GetString(logFont, 28, 32).TrimEnd('\0');
        Assert.Equal("SYSTEM", face);
    }
}
