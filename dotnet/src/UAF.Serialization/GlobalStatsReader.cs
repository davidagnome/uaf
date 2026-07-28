using UAF.Common;

namespace UAF.Serialization;

/// <summary>One title/credits screen in the sequence (<c>GlobalData.cpp:278</c>).</summary>
public sealed record TitleScreen(string BackgroundArt, int UseTrans, int UseBlend, uint DisplayBy);

/// <summary>A title or credits sequence: a timeout and a list of screens.</summary>
public sealed record TitleScreenData(uint Timeout, IReadOnlyList<TitleScreen> Titles);

/// <summary>
/// The <c>GLOBAL_STATS</c> prefix, read up to and including its attribute list.
/// </summary>
/// <remarks>
/// Stops at the ASL rather than continuing into the trailing art records, which is where the
/// remaining ~30 structures begin.
/// </remarks>
public sealed record GlobalStatsPrefix(
    DesignVersion Version, string DesignName,
    int StartLevel, byte StartX, byte StartY, byte StartFacing,
    int StartTime, int StartExp, int StartExpType,
    int StartPlatinum, int StartGem, int StartJewelry,
    int AutoDarkenViewport, int AutoDarkenAmount,
    int MinPcs, int MaxPartyMaxPcs, int Flags,
    string MapArt, string IconBackgroundArt, string BackgroundArt,
    IReadOnlyList<PicRecord> SmallPicImports, IReadOnlyList<PicRecord> IconPicImports,
    TitleScreenData? TitleData, TitleScreenData? CreditsData,
    IReadOnlyList<AslEntry> Attributes);

/// <summary>
/// Reads <c>GLOBAL_STATS::Serialize(CAR&amp;)</c> (<c>GlobalData.cpp:4244</c>) as far as the
/// <c>GLOBAL_STATS_ATTRIBUTES</c> block.
/// </summary>
/// <remarks>
/// <para>
/// This is the <b>compressed</b> path. The uncompressed DefaultDesign takes
/// <c>Serialize(CArchive&amp;)</c> (<c>GlobalData.cpp:3855</c>) instead, which is a different
/// function with its own field order — the two are not interchangeable.
/// </para>
/// <para>
/// A second <c>Serialize(CAR&amp;)</c> exists at <c>GlobalData.cpp:4960</c> with an identical
/// signature; it is commented out. Only 4244 is live.
/// </para>
/// <para>
/// The point of reading this far is the ASL. It is the first <b>non-empty</b> compressed
/// attribute list the port can reach, so it exercises the key/flags/value loop and the
/// compressed-only key fixup that <c>items.dat</c> could not — every ASL there has a count of
/// zero.
/// </para>
/// </remarks>
public static class GlobalStatsReader
{
    /// <summary>
    /// Size of the Win32 <c>LOGFONT</c> struct blitted straight into the archive at 0.830 and
    /// above (<c>GlobalData.cpp:4411</c>).
    /// </summary>
    /// <remarks>
    /// 5 × <c>LONG</c> (20) + 8 × <c>BYTE</c> (8) + <c>LF_FACESIZE</c> 32 <c>CHAR</c> = 60, with
    /// no tail padding since every member aligns to 4 or less. This is an MBCS build, so it is
    /// <c>LOGFONTA</c> — the wide variant would be 92 and would desynchronise everything after.
    /// </remarks>
    public const int LogFontSize = 60;

    public static GlobalStatsPrefix Read(CarArchiveReader car, DesignVersion version) =>
        Read(ArchiveCursor.For(car), version);

    public static GlobalStatsPrefix Read(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        // The caller has already consumed the magic/version prologue; see GameDataReader.
        string designName = ReadDas(ar);

        int startLevel = ar.ReadInt32();
        byte startX = ar.ReadByte();            // BYTE, not int -- three of them in a row
        byte startY = ar.ReadByte();
        byte startFacing = ar.ReadByte();
        int startTime = ar.ReadInt32();
        int startExp = ar.ReadInt32();

        int startExpType = version >= DesignVersion.V0770 ? ar.ReadInt32() : 0;

        ar.ReadInt32();                          // retired startEquip slot, always written

        int startPlatinum = ar.ReadInt32();
        int startGem = ar.ReadInt32();
        int startJewelry = ar.ReadInt32();

        if (version >= DesignVersion.V0574)
        {
            ar.ReadInt32();                      // DungeonTimeDelta
            ar.ReadInt32();                      // DungeonSearchTimeDelta
            ar.ReadInt32();                      // WildernessTimeDelta
            ar.ReadInt32();                      // WildernessSearchTimeDelta
        }

        int autoDarkenViewport = 0;
        int autoDarkenAmount = 0;
        if (version >= DesignVersion.V0620)
        {
            // Both declared BOOL, but AutoDarkenAmount holds a magnitude, not a flag -- kept as
            // int rather than narrowed to bool.
            autoDarkenViewport = ar.ReadInt32();
            autoDarkenAmount = ar.ReadInt32();
            ar.ReadInt32();                      // StartDarken
            ar.ReadInt32();                      // EndDarken
        }

        int minPcs = 0;
        int maxPartyMaxPcs = 0;
        int flags = 0;
        if (version >= DesignVersion.V0575)
        {
            minPcs = ar.ReadInt32();
            maxPartyMaxPcs = ar.ReadInt32();     // upper 16 bits maxParty, lower maxPCs
            flags = ar.ReadInt32();
        }

        string mapArt = ReadDas(ar);

        if (version < DesignVersion.V0830)
        {
            ar.ReadString();                     // font name
            if (version >= DesignVersion.V0681)
            {
                ar.ReadByte();                   // fontSize -- a BYTE
            }
        }
        else
        {
            // A raw struct blit, not a field-by-field write.
            ar.ReadBytes(LogFontSize);
        }

        if (version < DesignVersion.V0800)
        {
            ReadDas(ar);                         // a single TitleBgArt, promoted to a sequence
        }

        string iconBackgroundArt = string.Empty;
        string backgroundArt = string.Empty;
        if (version >= DesignVersion.V0660)
        {
            iconBackgroundArt = ReadDas(ar);
            backgroundArt = ReadDas(ar);
        }

        // Note the nesting: the inner test is `< 5.25`, so designs at or above it read NOTHING
        // here and carry their credits in the trailing creditsData instead.
        if (version >= DesignVersion.V0566 && version < DesignVersion.V525)
        {
            ReadDas(ar);                         // legacy single CreditsBgArt
        }

        var smallPics = new List<PicRecord>();
        int count = ar.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            smallPics.Add(PicDataReader.Read(ar, version, PicArchiveVariant.Car));
        }

        var iconPics = new List<PicRecord>();
        count = ar.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            if (version < DesignVersion.V0640)
            {
                // Older designs stored just a filename where later ones store a whole record.
                string icon = ReadDas(ar);
                iconPics.Add(new PicRecord(0, icon, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
            }
            else
            {
                iconPics.Add(PicDataReader.Read(ar, version, PicArchiveVariant.Car));
            }
        }

        TitleScreenData? titleData = version >= DesignVersion.V0800 ? ReadTitleScreens(ar) : null;
        TitleScreenData? creditsData = version >= DesignVersion.V525 ? ReadTitleScreens(ar) : null;

        var attributes = AslReader.Read(ar, version, AslMaps.GlobalStats);

        return new GlobalStatsPrefix(
            version, designName, startLevel, startX, startY, startFacing,
            startTime, startExp, startExpType, startPlatinum, startGem, startJewelry,
            autoDarkenViewport, autoDarkenAmount, minPcs, maxPartyMaxPcs, flags,
            mapArt, iconBackgroundArt, backgroundArt, smallPics, iconPics,
            titleData, creditsData, attributes);
    }

    /// <summary>Reads a <c>TITLE_SCREEN_DATA</c> (<c>GlobalData.cpp:373</c>).</summary>
    private static TitleScreenData ReadTitleScreens(IArchiveCursor ar)
    {
        uint timeout = ar.ReadUInt32();
        uint count = ar.ReadUInt32();            // DWORD, not int

        var titles = new List<TitleScreen>((int)Math.Min(count, 1024));
        for (uint i = 0; i < count; i++)
        {
            titles.Add(new TitleScreen(
                ReadDas(ar),
                ar.ReadInt32(),                  // UseTrans -- BOOL
                ar.ReadInt32(),                  // UseBlend -- BOOL
                ar.ReadUInt32()));               // DisplayBy -- DWORD
        }
        return new TitleScreenData(timeout, titles);
    }

    private static string ReadDas(IArchiveCursor ar) =>
        ArchiveStringConventions.Decode(ar.ReadString());
}
