using System.Collections.Frozen;

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
/// <b>The index is built once per directory and cached.</b> A design has a few dozen files and an
/// import asks for each of them by a constructed name, so scanning per lookup would be quadratic
/// for no reason. The plan asks for exactly this shape: "build the directory index once per design
/// and resolve every constructed filename through it".
/// </para>
/// <para>
/// <b>Ambiguity resolves to the ordinal-first match</b> rather than throwing. A directory holding
/// both <c>GEO001.DAT</c> and <c>geo001.dat</c> is possible on a case-sensitive filesystem and
/// impossible on the one the data came from; picking deterministically beats refusing to import a
/// design that Windows would have loaded without comment.
/// </para>
/// </remarks>
public static class FruaFiles
{
    private static readonly Dictionary<string, FrozenDictionary<string, string>> Indexes = [];

    private static readonly Lock Gate = new();

    /// <summary>
    /// The full path to <paramref name="fileName"/> within <paramref name="directory"/>, or null.
    /// </summary>
    public static string? Resolve(string directory, string fileName)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(fileName);

        return Index(directory).GetValueOrDefault(fileName);
    }

    /// <summary>Every file in the directory, keyed case-insensitively by bare filename.</summary>
    public static FrozenDictionary<string, string> Index(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        lock (Gate)
        {
            if (Indexes.TryGetValue(directory, out var cached))
            {
                return cached;
            }

            var index = Build(directory);
            Indexes[directory] = index;
            return index;
        }
    }

    /// <summary>Forgets every cached index. For tests that write into a directory.</summary>
    public static void Forget()
    {
        lock (Gate)
        {
            Indexes.Clear();
        }
    }

    private static FrozenDictionary<string, string> Build(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return FrozenDictionary<string, string>.Empty;
        }

        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(directory)
                                         .OrderBy(p => p, StringComparer.Ordinal))
        {
            // First writer wins, so the ordinal ordering above decides -- see the remarks.
            found.TryAdd(Path.GetFileName(path), path);
        }

        return found.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
