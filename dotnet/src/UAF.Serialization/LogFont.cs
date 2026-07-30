using System.Buffers.Binary;
using System.Text;

namespace UAF.Serialization;

/// <summary>
/// A Win32 <c>LOGFONTA</c>, blitted whole into the archive at design version 0.830 and above.
/// </summary>
/// <remarks>
/// <para>
/// Not a serialized field list — <c>ar.Write(&amp;logfont, sizeof(logfont))</c>
/// (<c>GlobalData.cpp:3904</c>) writes the raw struct, so this is a memory image of whatever
/// Windows laid out, padding and all. It is the ANSI variant: 60 bytes, with a 32-byte
/// <c>char</c> face name. The wide variant would be 92 and would desynchronise everything after
/// it.
/// </para>
/// <para>
/// This is the design's *requested* font, not a resolved one. The engine passes it to
/// <c>EnumFontFamiliesEx</c> and warns "Cannot find specified font named %s"
/// (<c>GlobalData.cpp:5846</c>) when the machine has no such face — so a design naming a font that
/// is not installed was already a supported, if degraded, situation on Windows. That matters for
/// the port: resolving these names on Linux or macOS is the same problem the original already had,
/// not a new one.
/// </para>
/// </remarks>
public sealed record LogFont(
    int Height, int Width, int Escapement, int Orientation, int Weight,
    bool Italic, bool Underline, bool StrikeOut,
    byte CharSet, byte OutPrecision, byte ClipPrecision, byte Quality, byte PitchAndFamily,
    string FaceName)
{
    /// <summary>The size of <c>LOGFONTA</c>: five LONGs, eight BYTEs, then <c>char[32]</c>.</summary>
    public const int Size = 60;

    /// <summary>Where <c>lfFaceName</c> starts.</summary>
    private const int FaceNameOffset = 28;

    /// <summary>The face-name buffer length, <c>LF_FACESIZE</c>.</summary>
    private const int FaceNameLength = 32;

    /// <summary>
    /// What the engine substitutes when a design leaves the face name empty
    /// (<c>GlobalData.cpp:3901</c>).
    /// </summary>
    public static LogFont Default { get; } = new(
        16, 0, 0, 0, 0, false, false, false, 0, 0, 0, 0, 0, "SYSTEM");

    /// <summary>
    /// A negative <see cref="Height"/> is a Win32 convention: it requests that <i>character</i>
    /// height rather than <i>cell</i> height, so the absolute value is what a rasteriser wants.
    /// </summary>
    public int PointSizeHint => Math.Abs(Height);

    /// <summary>
    /// Whether this asks for a bold face. Win32 grades weight 0–1000 with 700 as bold; the editor
    /// writes 999 for bold and 001 for normal (<c>GlobalData.cpp:6009</c>) rather than the
    /// documented constants, so a threshold is the only reading that handles both.
    /// </summary>
    public bool IsBold => Weight >= 700;

    /// <summary>Parses the 60-byte blit.</summary>
    public static LogFont Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
        {
            throw new ArgumentException($"LOGFONT is {bytes.Length} bytes, expected {Size}",
                                        nameof(bytes));
        }

        var face = bytes.Slice(FaceNameOffset, FaceNameLength);
        int end = face.IndexOf((byte)0);

        // Windows-1252, as with every other string in these files -- the struct predates any
        // Unicode build of the editor.
        string faceName = MfcArchiveReader.DefaultEncoding.GetString(
            end < 0 ? face : face[..end]);

        var result = new LogFont(
            BinaryPrimitives.ReadInt32LittleEndian(bytes),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]),
            bytes[20] != 0, bytes[21] != 0, bytes[22] != 0,
            bytes[23], bytes[24], bytes[25], bytes[26], bytes[27],
            faceName);

        // The engine's own substitution, applied at read time so callers never see the empty case.
        return faceName.Length == 0 ? Default : result;
    }

    public override string ToString() =>
        $"{FaceName} {PointSizeHint}{(IsBold ? " bold" : string.Empty)}" +
        $"{(Italic ? " italic" : string.Empty)}";
}
