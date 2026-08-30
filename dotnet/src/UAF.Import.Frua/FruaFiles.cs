using System.Collections.Frozen;
using UAF.Common;

namespace UAF.Import.Frua;

/// <summary>
/// Finds a file in a FRUA design directory whatever case its name is stored in.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is required to import at all on Linux and macOS, not merely nice to have.</b> The
/// reference constructs every path in lower case — <c>"game001.dat"</c>, <c>"geo%03i.dat"</c>,
/// <c>"items.dat"</c> (<c>UAImport.cpp:4402</c>, <c>:4652</c>, <c>:5583</c>) — while every shipped
/// DOS design stores them upper case, because that is what the DOS filesystem gave back. Windows
/// does not care and the reference never noticed; a case-sensitive filesystem fails every open.
/// </para>
/// <para>
/// The actual indexing and case-insensitive lookup live in <see cref="CaseInsensitiveFiles"/>,
/// shared with the engine's own asset resolution. The index is built once per directory and cached;
/// ambiguity resolves to the ordinal-first match.
/// </para>
/// </remarks>
public static class FruaFiles
{
    /// <summary>
    /// The full path to <paramref name="fileName"/> within <paramref name="directory"/>, or null.
    /// </summary>
    public static string? Resolve(string directory, string fileName) =>
        CaseInsensitiveFiles.Resolve(directory, fileName);

    /// <summary>Every file in the directory, keyed case-insensitively by bare filename.</summary>
    public static FrozenDictionary<string, string> Index(string directory) =>
        CaseInsensitiveFiles.Index(directory);

    /// <summary>Forgets every cached index. For tests that write into a directory.</summary>
    public static void Forget() => CaseInsensitiveFiles.Forget();
}
