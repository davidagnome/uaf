using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Walks the scalar prefix of <c>GLOBAL_STATS::Serialize(CArchive&amp;)</c> against the real
/// <c>game.dat</c>, in the exact field order written at <c>GlobalData.cpp:3862</c>.
/// </summary>
/// <remarks>
/// This is the plain-<c>CArchive</c> path (version 0.572 &lt; 0.573), so no LZW is involved.
/// The point is to prove the primitive widths are right: an <c>int</c> read where the writer
/// wrote a <c>BYTE</c> shifts every subsequent field and produces plausible-looking garbage
/// rather than an error.
/// </remarks>
public class GlobalStatsPayloadTests
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

    [Fact]
    public void Scalar_prefix_reads_with_the_widths_the_writer_used()
    {
        using var fs = File.OpenRead(GameDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.GameData);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);

        double version    = ar.ReadDouble();   // ar << version
        string designName = ar.ReadString();   // ar << GetDesignName()
        int startLevel    = ar.ReadInt32();    // ar << startLevel
        byte startX       = ar.ReadByte();     // BYTE startX
        byte startY       = ar.ReadByte();     // BYTE startY
        byte startFacing  = ar.ReadByte();     // BYTE startFacing
        int startTime     = ar.ReadInt32();
        int startExp      = ar.ReadInt32();
        int startExpType  = ar.ReadInt32();
        int junk          = ar.ReadInt32();    // `long int junk = 0` -- retired startEquip slot
        int startPlatinum = ar.ReadInt32();
        int startGem      = ar.ReadInt32();
        int startJewelry  = ar.ReadInt32();

        Assert.Equal(0.915025, version, precision: 10);
        Assert.Equal("DefaultDesign", designName);

        // The retired field is written as a literal zero, so it is a free integrity check:
        // if the byte-width of anything above is wrong, this is the first thing to break.
        Assert.Equal(0, junk);

        // Round decimal values are the strongest available evidence of correct alignment: a
        // one-byte slip yields arbitrary 32-bit noise, not 800 and 30,000,000.
        Assert.Equal(800, startTime);
        Assert.Equal(30_000_000, startExp);

        Assert.Equal(0, startLevel);
        Assert.Equal(0, startX);
        Assert.Equal(0, startY);
        Assert.Equal(1, startFacing);               // FACE_EAST (Externs.h:1039)
        Assert.Equal(0, startExpType);
        Assert.Equal(0, startPlatinum);
        Assert.Equal(0, startGem);
        Assert.Equal(0, startJewelry);
    }

    [Fact]
    public void Time_and_darken_block_then_party_limits_then_flags()
    {
        using var fs = File.OpenRead(GameDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.GameData);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);

        ar.ReadDouble(); ar.ReadString(); ar.ReadInt32();
        ar.Skip(3);                                     // startX/Y/Facing
        for (int i = 0; i < 7; i++) ar.ReadInt32();     // time..jewelry (incl. junk)

        ar.ReadInt32();  // DungeonTimeDelta
        ar.ReadInt32();  // DungeonSearchTimeDelta
        ar.ReadInt32();  // WildernessTimeDelta
        ar.ReadInt32();  // WildernessSearchTimeDelta

        // BOOL is a 4-byte int in Win32, not a single byte -- a common porting slip.
        int autoDarkenViewport = ar.ReadInt32();
        int autoDarkenAmount   = ar.ReadInt32();
        ar.ReadInt32();  // StartDarken
        ar.ReadInt32();  // EndDarken

        int minPCs          = ar.ReadInt32();
        int maxPartyMaxPCs  = ar.ReadInt32();
        int flags           = ar.ReadInt32();

        // Exact observed values for this fixture. These are LAYOUT assertions, not semantic
        // ones: the alignment is corroborated by the round decimals earlier in the record
        // (startTime 800, startExp 30_000_000) -- a misaligned read does not produce round
        // decimal numbers by chance.
        //
        // Deliberately NOT asserting that the BOOL fields are 0/1. AutoDarkenAmount is declared
        // `BOOL` (GlobalData.h) but actually holds 256 here, so it is an integer amount wearing
        // a BOOL type. Mapping it to a C# `bool` would silently destroy the value.
        Assert.Equal(0, autoDarkenViewport);
        Assert.Equal(256, autoDarkenAmount);

        Assert.Equal(1, minPCs);
        Assert.Equal(1, flags);

        // maxParty_maxPCs packs partySize in the high 16 bits and maxPCs in the low 16. The RAW
        // stored value is 8 -> maxPCs=8, partySize=0. That zero is not the effective value: the
        // loading branch repairs it with
        //     if (GetMaxPartySize() == 0) SetMaxPartySize(GetMaxPCs() + 2);   GlobalData.cpp:3983
        // so the effective party size is 10, which is what the C++ oracle reports. A reader that
        // stops at the raw bytes yields a party size of zero.
        Assert.Equal(8, maxPartyMaxPCs);
        Assert.Equal(8, maxPartyMaxPCs & 0xffff);      // maxPCs, as stored
        Assert.Equal(0, maxPartyMaxPCs >> 16);         // partySize, as stored -- needs repair
        Assert.Equal(10, (maxPartyMaxPCs >> 16) == 0
                            ? (maxPartyMaxPCs & 0xffff) + 2
                            : maxPartyMaxPCs >> 16);   // effective value after the repair
    }

    [Fact]
    public void Empty_strings_round_trip_through_the_ArchiveBlank_sentinel()
    {
        Assert.Equal(string.Empty, ArchiveStringConventions.Decode("*"));
        Assert.Equal("Mapart.png", ArchiveStringConventions.Decode("Mapart.png"));
        Assert.Equal("*", ArchiveStringConventions.Encode(""));

        // A literal "*" must decode to empty even when a different sentinel is configured --
        // released builds shipped with "*" and their designs still have to load.
        Assert.Equal(string.Empty, ArchiveStringConventions.Decode("*", archiveBlank: "#"));
        Assert.Equal(string.Empty, ArchiveStringConventions.Decode("#", archiveBlank: "#"));
    }
}
