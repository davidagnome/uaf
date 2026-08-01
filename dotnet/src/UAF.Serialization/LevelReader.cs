using UAF.Common;

namespace UAF.Serialization;

/// <summary>One viewport map cell (<c>AREA_MAP_DATA</c>).</summary>
/// <remarks>
/// 15 bytes at 0.5771 and above, every field a single <c>BYTE</c> on the wire even where the C++
/// member is wider — <c>eventExists</c> is declared <c>BOOL</c> but serialized as one byte
/// (<c>Level.cpp:720</c>).
/// </remarks>
public sealed record AreaMapCell(
    byte Background, bool ShowDistantBackground, bool DistantBackgroundInBands,
    byte NorthBg, byte EastBg, byte SouthBg, byte WestBg,
    byte Zone, bool EventExists, byte[] Walls, byte[] Blockage)
{
    /// <summary>
    /// Direction (north, east, south, west) to index into <see cref="Walls"/> and
    /// <see cref="Blockage"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The two arrays are stored north, SOUTH, EAST, west — not in compass-traversal order.</b>
    /// <c>Level.h:87</c> declares <c>BYTE wall[4]; // North, south, east, west</c>, the commented-out
    /// members it replaced (<c>northWall</c>, <c>southWall</c>, <c>eastWall</c>, <c>westWall</c>)
    /// say the same, and <c>AREA_MAP_DATA::Serialize</c> writes <c>wall[0]</c>…<c>wall[3]</c> in
    /// that declaration order, so it is the wire order too.
    /// </para>
    /// <para>
    /// Every consumer in the original therefore permutes: <c>AREA_MAP_DATA::walls(int dir)</c> and
    /// <c>blockages(int dir)</c> (<c>Level.cpp:932</c>, <c>:945</c>) build the identical
    /// <c>{0,2,1,3}</c> table, and <c>IsWallAt</c> (<c>Drawtile.cpp:1819</c>) and the four explicit
    /// switches in <c>RunEvent.cpp</c> (<c>:5171</c>, <c>:5420</c>, <c>:14678</c>) spell it out the
    /// same way. Backgrounds are the exception — <c>northBG</c>…<c>westBG</c> really are stored in
    /// compass order, which is why <c>backgrounds(dir)</c> has a different table.
    /// </para>
    /// <para>
    /// Indexing these arrays with <see cref="Facing"/> directly swaps east and south. Confirmed
    /// against real data: taking a shared edge's two faces (a cell's east wall against its east
    /// neighbour's west wall, and its south against the next row's north), this permutation agrees
    /// on <b>9,708 of 9,708</b> edges across <c>SomethingWild</c>'s eight levels, where indexing by
    /// facing agrees on 78.88%.
    /// </para>
    /// </remarks>
    /// <para>
    /// The direction argument is a bare int rather than the engine's <c>Facing</c> because that
    /// enum lives in <c>UAFcore</c>, above this assembly. It matches the original's own
    /// <c>walls(int dir)</c> signature, and <c>dir &amp; 3</c> reproduces its masking.
    /// </para>
    private static ReadOnlySpan<int> DirectionToSlot => [0, 2, 1, 3];

    /// <summary>
    /// The wall index on one face. <paramref name="direction"/> is 0=north, 1=east, 2=south,
    /// 3=west. See the remarks for why this is not a plain index.
    /// </summary>
    public byte WallAt(int direction)
    {
        int slot = DirectionToSlot[direction & 3];
        return slot < Walls.Length ? Walls[slot] : (byte)0;
    }

    /// <summary>
    /// The raw blockage byte on one face, or null when the cell does not carry it.
    /// <paramref name="direction"/> is 0=north, 1=east, 2=south, 3=west.
    /// </summary>
    public byte? BlockageAt(int direction)
    {
        int slot = DirectionToSlot[direction & 3];
        return slot < Blockage.Length ? Blockage[slot] : null;
    }
}

/// <summary>
/// Reads <c>LEVEL</c> data from a <c>.lvl</c> file (<c>Level.cpp:1224</c>).
/// </summary>
/// <remarks>
/// <para>
/// Level files use <see cref="DesignFileKind.LevelData"/>: magic-stamped, archive gate at 0.573,
/// and — unlike the databases — <b>never compressed</b>. <c>LoadLevel</c> constructs a
/// <c>CAR</c> but leaves <c>ar.Compress(true)</c> commented out (<c>Level.cpp:2186</c>), so the
/// payload is plain archive primitives even at 5.29. Do not assume the database rules apply.
/// </para>
/// <para>
/// The dimensions are <c>BYTE</c>, not <c>int</c> (<c>Level.h:58</c>), and are <b>serialized
/// width-then-height while being declared height-then-width</b> — reading them in declaration
/// order transposes every non-square level silently.
/// </para>
/// </remarks>
public static class LevelReader
{
    /// <summary>Reads the level dimensions. Width first — see the remarks.</summary>
    public static (byte Width, byte Height) ReadDimensions(MfcArchiveReader ar)
    {
        byte width = ar.ReadByte();
        byte height = ar.ReadByte();
        return (width, height);
    }

    /// <summary>Reads one map cell (<c>Level.cpp:694</c>).</summary>
    public static AreaMapCell ReadCell(MfcArchiveReader ar, DesignVersion version)
    {
        // bkgrnd carries two flags in its top bits, which are stripped from the value itself.
        // A reader that keeps the raw byte reports background indices up to 255 where the real
        // range is 0..63.
        byte raw = ar.ReadByte();
        bool showDistant = (raw & 0x80) != 0;
        bool inBands = (raw & 0x40) != 0;
        byte background = (byte)(raw & 0x3F);

        // Pre-0.695 designs infer banding from the flag (Level.cpp:702).
        if (version < DesignVersion.V0695 && showDistant)
        {
            inBands = true;
        }

        byte northBg, eastBg, southBg, westBg;
        if (version >= DesignVersion.V05771)
        {
            northBg = ar.ReadByte();
            eastBg = ar.ReadByte();
            southBg = ar.ReadByte();
            westBg = ar.ReadByte();
        }
        else
        {
            // Older levels store one background for all four faces.
            northBg = eastBg = southBg = westBg = background;
        }

        byte zone = ar.ReadByte();
        bool eventExists = ar.ReadByte() != 0;   // BOOL member, one byte on the wire

        byte[] walls = [ar.ReadByte(), ar.ReadByte(), ar.ReadByte(), ar.ReadByte()];
        byte[] blockage = [ar.ReadByte(), ar.ReadByte(), ar.ReadByte(), ar.ReadByte()];

        return new AreaMapCell(background, showDistant, inBands,
                               northBg, eastBg, southBg, westBg,
                               zone, eventExists, walls, blockage);
    }

    /// <summary>Reads the whole viewport map: dimensions followed by width × height cells.</summary>
    public static (byte Width, byte Height, AreaMapCell[] Cells) ReadAreaMap(
        MfcArchiveReader ar, DesignVersion version)
    {
        var (width, height) = ReadDimensions(ar);
        var cells = new AreaMapCell[width * height];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = ReadCell(ar, version);
        }
        return (width, height, cells);
    }
}
