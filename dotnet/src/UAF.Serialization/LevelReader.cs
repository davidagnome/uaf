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
    byte Zone, bool EventExists, byte[] Walls, byte[] Blockage);

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
