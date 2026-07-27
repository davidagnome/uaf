using UAF.Common;

namespace UAF.Serialization;

/// <summary>Which of the two <c>PIC_DATA::Serialize</c> overloads produced the bytes.</summary>
/// <remarks>
/// <para>
/// This is <b>not</b> the same question as whether the stream is compressed. An archive with
/// compressType 0 or 1 still runs the <c>CAR</c> code path, just without LZW, so the choice is
/// made by the calling structure, not by the encoding.
/// </para>
/// <para>
/// The two genuinely differ: <c>PicData.cpp:203</c> reads <c>style</c> at 0.900 and above, while
/// the <c>CArchive</c> twin has that line commented out (<c>PicData.cpp:139</c>). Four bytes,
/// present in one and not the other, with nothing in the record to signal which — so callers
/// must say.
/// </para>
/// </remarks>
public enum PicArchiveVariant
{
    /// <summary><c>PIC_DATA::Serialize(CArchive&amp;, …)</c> — no <c>style</c> field.</summary>
    CArchive,

    /// <summary><c>PIC_DATA::Serialize(CAR&amp;, …)</c> — reads <c>style</c> at 0.900 and above.</summary>
    Car,
}

/// <summary>One <c>PIC_DATA</c> record — an art reference plus its animation parameters.</summary>
public sealed record PicRecord(
    int PicType, string FileName, int TimeDelay, int NumFrames,
    int FrameWidth, int FrameHeight, uint Flags, uint MaxLoops,
    uint Style, uint UseAlpha, ushort AlphaValue, int RestartFrame);

/// <summary>
/// Reads <c>PIC_DATA</c> (<c>PicData.cpp:112</c>), embedded both standalone in <c>game.dat</c> and
/// inline in records such as <c>ITEM_DATA</c>.
/// </summary>
/// <remarks>
/// The trap here is <c>AlphaValue</c>: a 2-byte <c>WORD</c> among otherwise 4-byte fields.
/// Reading it as an <c>int</c> desynchronises every record that follows.
/// </remarks>
public static class PicDataReader
{
    public static PicRecord Read(IArchiveCursor cursor, DesignVersion version,
                                 PicArchiveVariant variant)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        int picType = cursor.ReadInt32();
        string fileName = ArchiveStringConventions.Decode(cursor.ReadString());
        int timeDelay = cursor.ReadInt32();
        int numFrames = cursor.ReadInt32();
        int frameWidth = cursor.ReadInt32();
        int frameHeight = cursor.ReadInt32();

        uint flags = version >= DesignVersion.V0790 ? cursor.ReadUInt32() : 0;
        uint maxLoops = version >= DesignVersion.V0810 ? cursor.ReadUInt32() : 0;

        // Present only on the CAR path, and only from 0.900. Four bytes that shift every field
        // after them -- and the fields that follow still decode to plausible-looking values when
        // it is missed, so nothing announces the mistake until a much later record.
        uint style = variant == PicArchiveVariant.Car && version >= DesignVersion.V0900
            ? cursor.ReadUInt32()
            : 0;

        uint useAlpha = 0;
        ushort alphaValue = 0;
        if (version >= DesignVersion.V0906)
        {
            useAlpha = cursor.ReadUInt32();     // BOOL -> 4 bytes
            alphaValue = cursor.ReadUInt16();   // WORD -> 2 bytes, NOT 4
        }

        // _VERSION_524 is 5.24, not 0.524, despite the unpadded name (Externs.h:174).
        int restartFrame = version >= DesignVersion.V524 ? cursor.ReadInt32() : 0;

        return new PicRecord(picType, fileName, timeDelay, numFrames, frameWidth, frameHeight,
                             flags, maxLoops, style, useAlpha, alphaValue, restartFrame);
    }

    public static PicRecord Read(MfcArchiveReader reader, DesignVersion version,
                                 PicArchiveVariant variant) =>
        Read(ArchiveCursor.For(reader), version, variant);

    public static PicRecord Read(CarArchiveReader reader, DesignVersion version,
                                 PicArchiveVariant variant) =>
        Read(ArchiveCursor.For(reader), version, variant);
}
