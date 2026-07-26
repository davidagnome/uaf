using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads the <c>PIC_DATA</c> records embedded in <c>game.dat</c>, following the loading branch at
/// <c>PicData.cpp:112</c>.
/// </summary>
/// <remarks>
/// The record is version-gated and contains a <b>2-byte <c>WORD</c></b> among otherwise 4-byte
/// fields, which is the trap here: reading <c>AlphaValue</c> as an <c>int</c> desynchronises every
/// following record. See docs/PORTING-PLAN.md section 3.2.
/// </remarks>
public class PicDataTests
{
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

    private sealed record PicRecord(
        int PicType, string FileName, int TimeDelay, int NumFrames,
        int FrameWidth, int FrameHeight, uint Flags, uint MaxLoops,
        uint UseAlpha, ushort AlphaValue);

    /// <summary>Reads one PIC_DATA at the given content version.</summary>
    private static PicRecord ReadPic(MfcArchiveReader ar, DesignVersion version)
    {
        int picType = ar.ReadInt32();
        string fileName = ArchiveStringConventions.Decode(ar.ReadString());
        int timeDelay = ar.ReadInt32();
        int numFrames = ar.ReadInt32();
        int frameWidth = ar.ReadInt32();
        int frameHeight = ar.ReadInt32();

        uint flags = version >= DesignVersion.V0790 ? ar.ReadUInt32() : 0;
        uint maxLoops = version >= DesignVersion.V0810 ? ar.ReadUInt32() : 0;

        uint useAlpha = 0;
        ushort alphaValue = 0;
        if (version >= DesignVersion.V0906)
        {
            useAlpha = ar.ReadUInt32();     // BOOL -> 4 bytes
            alphaValue = ar.ReadUInt16();   // WORD -> 2 bytes, NOT 4
        }

        if (version >= DesignVersion.V524)
        {
            ar.ReadInt32();                 // RestartFrame -- absent below 5.24
        }

        return new PicRecord(picType, fileName, timeDelay, numFrames,
                             frameWidth, frameHeight, flags, maxLoops, useAlpha, alphaValue);
    }

    /// <summary>Positions the reader at the SmallPicImport count and returns the content version.</summary>
    private static MfcArchiveReader SeekToSmallPicImports(FileStream fs, out DesignVersion version)
    {
        var header = DesignFileHeader.Read(fs, DesignFileKind.GameData);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);

        version = new DesignVersion(ar.ReadDouble());
        ar.ReadString();                                   // designName
        ar.ReadInt32();                                    // startLevel
        ar.Skip(3);                                        // startX/Y/Facing
        for (int i = 0; i < 7; i++) ar.ReadInt32();        // startTime..startJewelry
        for (int i = 0; i < 8; i++) ar.ReadInt32();        // time deltas + darken block
        for (int i = 0; i < 3; i++) ar.ReadInt32();        // minPCs, maxParty_maxPCs, flags

        ar.ReadString();                                   // m_MapArt
        ar.Skip(60);                                       // LOGFONTA blob (version >= 0.830)
        ar.ReadString();                                   // IconBgArt
        ar.ReadString();                                   // BackgroundArt
        ar.ReadString();                                   // CreditsBgArt (0.566 <= v < 5.25)
        return ar;
    }

    [Fact]
    public void Small_pic_imports_read_as_a_coherent_sequence()
    {
        using var fs = File.OpenRead(GameDat());
        var ar = SeekToSmallPicImports(fs, out var version);

        int count = ar.ReadInt32();
        Assert.Equal(18, count);

        var records = new List<PicRecord>();
        for (int i = 0; i < count; i++)
        {
            records.Add(ReadPic(ar, version));
        }

        // Filenames are sequential -- prt_SPic1.png .. prt_SPic18.png. A width error anywhere in
        // the record would break this long before the 18th entry, so it is a strong integrity
        // check rather than a cosmetic one.
        for (int i = 0; i < count; i++)
        {
            Assert.Equal($"prt_SPic{i + 1}.png", records[i].FileName);
        }

        // Every entry is 176x211 -- which is exactly the SmallPic size documented in the design's
        // own config.txt ("SmallPic 176 x 211"), an independent corroboration of the layout.
        Assert.All(records, r =>
        {
            Assert.Equal(176, r.FrameWidth);
            Assert.Equal(211, r.FrameHeight);
            Assert.Equal(1, r.NumFrames);
        });
    }

    [Fact]
    public void AlphaValue_is_a_two_byte_WORD()
    {
        // PicData.h declares `WORD AlphaValue` among int/DWORD neighbours. Reading it as 4 bytes
        // shifts every subsequent record by two; the IconPicImport count that follows the
        // SmallPic block is the canary.
        using var fs = File.OpenRead(GameDat());
        var ar = SeekToSmallPicImports(fs, out var version);

        int count = ar.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            ReadPic(ar, version);
        }

        int iconPicCount = ar.ReadInt32();
        Assert.InRange(iconPicCount, 0, 1000);
        Assert.Equal(12, iconPicCount);
    }

    [Fact]
    public void RestartFrame_is_absent_below_5_24()
    {
        // The field was added at _VERSION_524. DefaultDesign is 0.915025, so it must NOT be read
        // -- consuming 4 phantom bytes here would desynchronise the icon block that follows.
        using var fs = File.OpenRead(GameDat());
        SeekToSmallPicImports(fs, out var version);
        Assert.True(version < DesignVersion.V524);
    }
}
