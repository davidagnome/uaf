using System.Collections;
using System.Reflection;
using UAFcore;

namespace UAFedit.CrossReference;

/// <summary>
/// Finds every art and sound file a design names, and compares that against what it ships.
/// </summary>
/// <remarks>
/// <para>
/// The reference builds this by giving all 62 of its record types a <c>CrossReference</c> method
/// that walks its own fields (<c>UAFWinEd/CrossReference.h</c>). <b>This walks the object graph
/// instead</b>, for the same reason <c>StructuralDiff</c> does in the round-trip harness: 62
/// hand-written walkers are 62 chances to forget a field, and a field added later is covered here
/// without anyone remembering to come back.
/// </para>
/// <para>
/// <b>A reference is a string that names a file.</b> Not a type test — art arrives as
/// <c>PicRecord.FileName</c>, as a bare <c>string</c> on the global art slots, inside sound queues
/// and inside event bodies, and there is no single type they share. Matching on the extension
/// catches all of them and is honest about what it is: a design that stored an art name without
/// its extension would be missed, and no shipped design does.
/// </para>
/// <para>
/// <b>Resolution is case-insensitive</b>, because the reference's is — designs authored on Windows
/// routinely disagree with their own filenames about case, and treating that as a missing file
/// would fill the report with noise. See the filename-case note in docs/PORTING-PLAN.md §3.2.
/// </para>
/// </remarks>
public static class CrossReferenceBuilder
{
    /// <summary>Extensions that make a string an art reference.</summary>
    private static readonly string[] ArtExtensions =
        [".png", ".bmp", ".jpg", ".jpeg", ".gif", ".tga", ".pcx"];

    /// <summary>Extensions that make a string a sound reference.</summary>
    private static readonly string[] SoundExtensions =
        [".wav", ".mp3", ".ogg", ".mid", ".midi", ".xmi"];

    /// <summary>
    /// How deep the walk goes before giving up on a branch.
    /// </summary>
    /// <remarks>
    /// The deepest real path is a level's event list into an event body into its art record, which
    /// is nothing like this deep. The cap is a backstop against a graph that refers to itself, not
    /// a tuning knob.
    /// </remarks>
    private const int MaxDepth = 24;

    /// <summary>Builds the report for an open design.</summary>
    public static CrossReferenceReport Build(LoadedDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);

        var found = new Dictionary<string, List<ResourceReference>>(StringComparer.OrdinalIgnoreCase);

        void Note(string file, string owner, string path)
        {
            if (!found.TryGetValue(file, out var list))
            {
                found[file] = list = [];
            }

            list.Add(new ResourceReference(owner, path));
        }

        Sweep(design.Globals, "Design globals", Note);

        if (design.Items is { } items)
        {
            foreach (var item in items.Items)
            {
                Sweep(item, $"Item '{item.Names.UniqueName}'", Note);
            }
        }

        foreach (var monster in design.Monsters ?? [])
        {
            Sweep(monster, $"Monster '{monster.Name}'", Note);
        }

        foreach (var spell in design.Spells ?? [])
        {
            Sweep(spell, $"Spell '{spell.Name}'", Note);
        }

        var levels = design.LevelFiles;
        for (int i = 0; i < levels.Count; i++)
        {
            // A level that cannot be read contributes nothing rather than stopping the sweep --
            // the report is still useful without it, and Level() answers null for a level holding
            // an event type the port does not know.
            if (design.Level(i) is { } level)
            {
                Sweep(level, Path.GetFileNameWithoutExtension(levels[i]), Note);
            }
        }

        return new CrossReferenceReport(
            [.. Entries(design, found)],
            Directory.Exists(Path.Combine(design.Root, "Resources")));
    }

    /// <summary>Pairs what was named against what is on disk, both directions.</summary>
    private static IEnumerable<CrossReferenceEntry> Entries(
        LoadedDesign design, Dictionary<string, List<ResourceReference>> found)
    {
        string resources = Path.Combine(design.Root, "Resources");

        var onDisk = Directory.Exists(resources)
            ? Directory.EnumerateFiles(resources)
                       .Select(Path.GetFileName)
                       .OfType<string>()
                       .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, references) in found)
        {
            yield return new CrossReferenceEntry(
                KindOf(name) ?? ResourceKind.Art, name, onDisk.Contains(name), references);
        }

        // And the other direction: a file the design ships that nothing names.
        foreach (string file in onDisk.Where(f => !found.ContainsKey(f)))
        {
            if (KindOf(file) is { } kind)
            {
                yield return new CrossReferenceEntry(kind, file, Exists: true, References: []);
            }
        }
    }

    /// <summary>Which kind a filename is, or null when it is neither.</summary>
    private static ResourceKind? KindOf(string name)
    {
        if (ArtExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
        {
            return ResourceKind.Art;
        }

        return SoundExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase))
            ? ResourceKind.Sound
            : null;
    }

    /// <summary>Walks one record, reporting every filename in it.</summary>
    private static void Sweep(object? root, string owner, Action<string, string, string> note) =>
        Walk(root, owner, string.Empty, 0, note, []);

    private static void Walk(object? node, string owner, string path, int depth,
                             Action<string, string, string> note, HashSet<object> seen)
    {
        if (node is null || depth > MaxDepth)
        {
            return;
        }

        if (node is string text)
        {
            if (KindOf(text) is not null)
            {
                // Names on the wire are bare, but a design that stored a folder with one should
                // still match the file -- the reference resolves through a directory index.
                note(Path.GetFileName(text.Replace('\\', '/')), owner,
                     path.Length == 0 ? "(itself)" : path);
            }

            return;
        }

        var type = node.GetType();
        if (type.IsPrimitive || type.IsEnum || node is decimal or DateTime or Guid)
        {
            return;
        }

        // Reference types only: a struct is copied on every read, so tracking it would never
        // match and a record holding many identical ones would be walked once and skipped after.
        if (!type.IsValueType && !seen.Add(node))
        {
            return;
        }

        if (node is IEnumerable list)
        {
            int index = 0;
            foreach (object? element in list)
            {
                Walk(element, owner, $"{path}[{index++}]", depth + 1, note, seen);
            }

            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch (TargetInvocationException)
            {
                // A computed property that throws is not stored data; the wire content is carried
                // by the ones that do not.
                continue;
            }

            Walk(value, owner, path.Length == 0 ? property.Name : $"{path}.{property.Name}",
                 depth + 1, note, seen);
        }
    }
}
