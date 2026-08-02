using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>PIC_DATA</c> (<c>PicData.cpp:112</c> and <c>:203</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every field, whatever the version</b> — the storing branch has no version gates at all, so
/// the output is the shape a reader at 5.24 and above expects. See <see cref="MonsterRecordWriter"/>
/// for why writing the modern shape unconditionally is the only behaviour the format has.
/// </para>
/// <para>
/// <b>The variant still matters.</b> It is not a version question: <c>style</c> is written on the
/// <c>CAR</c> path and commented out on the <c>CArchive</c> one (<c>PicData.cpp:135</c>), matching
/// each path's reader. Four bytes, with nothing in the record to say which — so callers must say,
/// exactly as they must for <see cref="PicDataReader"/>.
/// </para>
/// </remarks>
public static class PicDataWriter
{
    public static void Write(MfcArchiveWriter ar, PicRecord pic, PicArchiveVariant variant)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(pic);

        ar.WriteInt32(pic.PicType);

        // The reference strips the directory as it stores (twice, in fact: PreSerialize does it
        // again). This is the one place the writer is not a pure inverse of the reader -- the
        // reader keeps whatever the file held, and only designs below 0.930269 hold a path at all.
        ar.WriteString(ArchiveStringConventions.Encode(StripFilenamePath(pic.FileName)));

        ar.WriteInt32(pic.TimeDelay);
        ar.WriteInt32(pic.NumFrames);
        ar.WriteInt32(pic.FrameWidth);
        ar.WriteInt32(pic.FrameHeight);
        ar.WriteUInt32(pic.Flags);
        ar.WriteUInt32(pic.MaxLoops);

        if (variant == PicArchiveVariant.Car)
        {
            ar.WriteUInt32(pic.Style);
        }

        ar.WriteUInt32(pic.UseAlpha);
        ar.WriteUInt16(pic.AlphaValue);          // WORD, not 4 bytes
        ar.WriteInt32(pic.RestartFrame);
    }

    /// <summary>
    /// The reference's <c>StripFilenamePath</c> (<c>Utilities.cpp</c>), quirks included.
    /// </summary>
    /// <remarks>
    /// Three behaviours worth keeping literal, because each is reachable from design data: a name
    /// longer than three characters ending in a backslash loses just that backslash; a name with
    /// no backslash is returned untouched; and a backslash found before index 2 — <c>"a\b"</c>, or
    /// a leading one — is <b>not</b> stripped, so such a name keeps its directory. A tidier
    /// "everything after the last separator" would differ on all three.
    /// </remarks>
    public static string StripFilenamePath(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);

        if (filename.Length == 0)
        {
            return filename;
        }

        if (filename.Length > 3 && filename[^1] == '\\')
        {
            return filename[..^1];
        }

        int index = filename.LastIndexOf('\\');
        return index >= 2 ? filename[(index + 1)..] : filename;
    }
}
