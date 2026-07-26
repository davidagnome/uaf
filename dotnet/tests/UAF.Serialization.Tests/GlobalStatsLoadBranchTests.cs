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
        var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);
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
    public void Container_version_and_content_version_are_different_values()
    {
        using var fs = File.OpenRead(GameDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);

        // The container has no magic, so it resolves to the 0.572 fallback -- that value selects
        // the archive tier and nothing else.
        Assert.Equal(DesignVersion.V0572, header.Version);

        // The payload's own first field is the version every content gate compares against.
        SeekPastScalarPrefix(fs, out var contentVersion);
        Assert.Equal(0.915025, contentVersion.Value, precision: 10);

        // They must not be conflated: 0.572 would take the pre-0.830 branch (font name + size,
        // no LOGFONT blob) and desynchronise everything after it.
        Assert.NotEqual(header.Version, contentVersion);
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
