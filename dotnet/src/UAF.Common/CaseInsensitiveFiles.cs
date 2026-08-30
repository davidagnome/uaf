using System.Collections.Frozen;

namespace UAF.Common;

/// <summary>
/// Resolves a file within a directory by case-insensitive name, for designs that assume Windows
/// path semantics throughout.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a correctness shim on case-sensitive filesystems, not a convenience.</b> A design
/// references art and data by the case the Windows filesystem happened to hand back when it was
/// authored; macOS and Linux honour that case, so a design whose record says
/// <c>WYA_UD_Medieval.png</c> while the file is <c>wya_ud_medieval.png</c> fails every open. Windows
/// does not care, which is why the mismatch ships unnoticed.
/// </para>
/// <para>
/// <b>The index is built once per directory and cached.</b> The plan asks for exactly this shape:
/// "build the directory index once per design and resolve every constructed filename through it".
/// </para>
/// <para>
/// <b>Ambiguity resolves to the ordinal-first match</b> rather than throwing. A directory holding
/// both <c>Title.png</c> and <c>title.png</c> is possible on a case-sensitive filesystem and
/// impossible on the one the data came from; picking deterministically beats refusing a design that
/// Windows would have loaded without comment.
/// </para>
/// </remarks>
public static class CaseInsensitiveFiles
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
