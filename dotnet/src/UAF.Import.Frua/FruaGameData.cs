using System.Text;

namespace UAF.Import.Frua;

/// <summary>
/// A DOS FRUA design's <c>game001.dat</c> — the whole 388-byte header
/// (<c>ImportGameDat</c>, <c>UAFWinEd/UAImport.cpp:4397</c>).
/// </summary>
/// <param name="DesignName">
/// Blank in the file becomes <c>"NoName FRUA Design"</c>, which is the reference's own substitute
/// rather than an empty string.
/// </param>
/// <param name="StartExperience">Experience each new character begins with.</param>
/// <param name="StartPlatinum">Starting money, in platinum.</param>
/// <param name="StartGems">Starting gems.</param>
/// <param name="StartJewelry">Starting jewellery.</param>
/// <param name="StartLevel">
/// <b>Zero-based</b>. Stored one-based and decremented on read, as the reference does.
/// </param>
/// <param name="StartExperienceProfile">
/// <b>Zero-based</b>, likewise — the reference's <c>StartEP</c>, and its comment says
/// "make zero based" out loud.
/// </param>
/// <param name="StartEquipment">
/// Which starting-kit table every class draws from. Stored raw here: expanding it into seven
/// per-class item lists is <c>AssignStartEquipItems</c>'s job and needs the item database.
/// </param>
/// <param name="SpecialKeys">Eight named keys. A blank slot becomes <c>"Key n"</c>, one-based.</param>
/// <param name="SpecialItems">Twelve named items. A blank slot becomes <c>"Item n"</c>.</param>
public sealed record FruaGameData(
    string DesignName,
    uint StartExperience,
    uint StartPlatinum,
    uint StartGems,
    uint StartJewelry,
    int StartLevel,
    int StartExperienceProfile,
    byte StartEquipment,
    IReadOnlyList<string> SpecialKeys,
    IReadOnlyList<string> SpecialItems)
{
    /// <summary>The file is exactly this long in every shipped design.</summary>
    public const int Length = 388;

    /// <summary>
    /// The codepage the reference reads these bytes through.
    /// </summary>
    /// <remarks>
    /// <b>Not CP437, although the data is DOS-era.</b> <c>UAFWinEd</c> is a
    /// <c>CharacterSet=MultiByte</c> Windows build, so every byte a FRUA file carries is
    /// interpreted as Windows ANSI when it lands in a <c>CString</c> — a name authored in the DOS
    /// line-drawing or accented range therefore imports as the CP1252 character at that byte, not
    /// the character the designer typed. Reproducing the import means reproducing that
    /// reinterpretation, so this is CP1252 and deliberately so.
    /// </remarks>
    public static Encoding TextEncoding { get; } = CreateEncoding();

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// Reads a <c>game001.dat</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layout is fixed and sequential, and it accounts for all 388 bytes: a 32-byte name, four
    /// little-endian <c>DWORD</c>s, four single bytes, then 8 × 16 key names and 12 × 16 item
    /// names, then a 16-byte design password the reference reads past and discards.
    /// </para>
    /// <para>
    /// <b>The reference ignores the password deliberately</b> — its comment is
    /// "ignore design password" — so an imported design has none. That is a security-relevant
    /// difference only in the sense that FRUA's password was never a secret; it gated the editor,
    /// not the data.
    /// </para>
    /// </remarks>
    public static FruaGameData Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Length)
        {
            throw new InvalidDataException(
                $"game001.dat is {bytes.Length} bytes; every FRUA design has {Length}");
        }

        string name = Text(bytes[..32]);

        return new FruaGameData(
            DesignName: name.Length == 0 ? "NoName FRUA Design" : name,
            StartExperience: Dword(bytes, 32),
            StartPlatinum: Dword(bytes, 36),
            StartGems: Dword(bytes, 40),
            StartJewelry: Dword(bytes, 44),

            // Both stored one-based. The reference subtracts without a floor, so a zero byte
            // yields -1; transcribed as written rather than clamped, because a design that stores
            // zero here is one the reference would also import as -1.
            StartLevel: bytes[48] - 1,
            StartExperienceProfile: bytes[49] - 1,
            StartEquipment: bytes[50],

            // byte 51 is read and discarded -- the reference's comment is "unused byte".
            SpecialKeys: Names(bytes, at: 52, count: 8, blank: "Key"),
            SpecialItems: Names(bytes, at: 180, count: 12, blank: "Item"));
    }

    /// <summary>Reads a file, resolving its name case-insensitively.</summary>
    /// <remarks>
    /// <b>The reference builds the path as lower-case <c>"game001.dat"</c></b>
    /// (<c>UAImport.cpp:4402</c>) and every shipped design stores it upper-case. That works on
    /// Windows and fails on Linux, which is why §3.2 calls case-insensitive resolution a Phase 6
    /// requirement rather than a packaging one.
    /// </remarks>
    public static FruaGameData ReadFile(string designDirectory)
    {
        ArgumentNullException.ThrowIfNull(designDirectory);

        string path = FruaFiles.Resolve(designDirectory, "game001.dat")
            ?? throw new FileNotFoundException(
                $"no game001.dat in '{designDirectory}'");

        return Read(File.ReadAllBytes(path));
    }

    private static uint Dword(ReadOnlySpan<byte> bytes, int at) =>
        (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));

    /// <summary>
    /// A fixed-width text field, as <c>CString</c> would take it.
    /// </summary>
    /// <remarks>
    /// <b>The field ends at the first NUL, not at the last non-NUL.</b> The reference reads the
    /// whole field, plants a terminator past its end and hands it to <c>CString</c>, so <c>strlen</c>
    /// decides the length. <c>TUTORIAL.DSN</c> proves the difference is real: its name field holds
    /// <c>"tutorial design\0\0\0g"</c>, and a reader that merely trimmed trailing NULs would carry
    /// that stray <c>g</c> into the design name.
    /// <para>
    /// The surviving text is then trimmed at both ends, which is <c>TrimLeft</c>/<c>TrimRight</c>.
    /// </para>
    /// </remarks>
    private static string Text(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        if (end < 0)
        {
            end = field.Length;
        }

        return TextEncoding.GetString(field[..end]).Trim();
    }

    private static string[] Names(ReadOnlySpan<byte> bytes, int at, int count, string blank)
    {
        var names = new string[count];

        for (int i = 0; i < count; i++)
        {
            string name = Text(bytes.Slice(at + (i * 16), 16));
            names[i] = name.Length == 0 ? $"{blank} {i + 1}" : name;
        }

        return names;
    }
}
