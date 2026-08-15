using System.Collections;
using System.Reflection;

namespace UAFedit.RoundTrip.Tests;

/// <summary>
/// What kind of change a round trip made to one field.
/// </summary>
/// <remarks>
/// <b>The distinction that matters is between adding and losing.</b> Saving a design at 2.53
/// upgrades it to 5.24, and an upgrade legitimately <i>materialises</i> fields the older format
/// never carried — <c>creditsData</c> arrived at 5.25, a level's wall overrides and cell contents
/// later still. Those come back as an empty structure where the read found nothing, which is the
/// format working as designed. A field that went the other way, or whose value changed, is a
/// writer losing something it was given.
/// </remarks>
public enum DifferenceKind
{
    /// <summary>Both sides had a value and the values differ. Every one is a defect except the
    /// version stamp, which has to move.</summary>
    Value,

    /// <summary>Nothing became something: a field the source version did not carry.</summary>
    Materialised,

    /// <summary>Something became nothing. A loss.</summary>
    Cleared,

    /// <summary>A fixed table filled out to its modern size; the shared prefix matched.</summary>
    Grown,

    /// <summary>A list came back shorter than it went in. A loss.</summary>
    Shrunk,

    /// <summary>The two sides decoded to different types. A loss, and a strange one.</summary>
    TypeChanged,
}

/// <summary>One field on which two decoded models disagree.</summary>
public sealed record Difference(string Path, DifferenceKind Kind, string Before, string After)
{
    public override string ToString() => $"{Path}: {Before} -> {After} [{Kind}]";
}

/// <summary>
/// Walks two decoded design models and names every field on which they disagree.
/// </summary>
/// <remarks>
/// <para>
/// <b>The record types cannot be compared with <c>==</c>.</b> They are C# <c>record</c>s, so the
/// synthesized equality is member-wise — but nearly every one holds an
/// <c>IReadOnlyList&lt;T&gt;</c>, and a list member compares by <i>reference</i>. Two identical
/// item databases read from two identical streams are therefore never equal, and a test that
/// asserted <c>Assert.Equal(a, b)</c> over them would fail for a reason that has nothing to do
/// with the format. The existing per-type tests work around this by hand-listing every field
/// (see <c>UAF.Serialization.Tests/ItemWriterCorpusTests.AssertSameItem</c>); this walks the graph
/// instead, so a field added to a record is compared without anyone remembering to add it here.
/// </para>
/// <para>
/// <b>It collects every difference rather than stopping at the first, and that is not a
/// convenience.</b> Every design file the port writes changes its version stamp, so a walk that
/// stopped at the first difference would report <c>Version</c> for all of them and never look at
/// the rest — which reads exactly like "nothing else changed" while having checked nothing else.
/// The claim worth making is that the version is the <i>only</i> value that changed, and only an
/// exhaustive walk can make it.
/// </para>
/// <para>
/// <b>Naming the field is the other half of the point.</b> A round trip that loses one value
/// reports it as <c>Items[311].Tail.RechargeRate: 0 -&gt; 12</c>, which says which writer to look
/// at. A bare "not equal" would not.
/// </para>
/// </remarks>
public static class StructuralDiff
{
    /// <summary>How deep the walk goes before giving up, in case a graph ever gains a cycle.</summary>
    private const int MaxDepth = 64;

    /// <summary>
    /// How many differences are collected before the walk stops. A model that has genuinely
    /// diverged produces thousands, and the first few hundred say the same thing as all of them.
    /// </summary>
    public const int Limit = 200;

    /// <summary>
    /// Every field on which the two models differ, up to <see cref="Limit"/>. Empty when they
    /// match.
    /// </summary>
    /// <param name="root">The name paths are reported under, e.g. <c>items.dat</c>.</param>
    public static IReadOnlyList<Difference> All(object? expected, object? actual, string root)
    {
        var found = new List<Difference>();
        Walk(expected, actual, root, 0, found);
        return found;
    }

    /// <summary>
    /// The differences that mean something was lost or altered, as opposed to added by the
    /// upgrade.
    /// </summary>
    /// <param name="versionStampIsExpected">
    /// When true, a changed <c>Version</c> is not counted — every writer stamps its own
    /// <c>WrittenVersion</c>, so on a first save that difference is the upgrade itself.
    /// </param>
    public static IReadOnlyList<Difference> Losses(
        IEnumerable<Difference> differences, bool versionStampIsExpected)
    {
        ArgumentNullException.ThrowIfNull(differences);

        return
        [
            .. differences.Where(d => d.Kind is DifferenceKind.Cleared
                                              or DifferenceKind.Shrunk
                                              or DifferenceKind.TypeChanged
                                  || (d.Kind == DifferenceKind.Value
                                      && !(versionStampIsExpected && IsVersionStamp(d.Path))))
        ];
    }

    /// <summary>
    /// Whether a path names the file's version stamp rather than its content.
    /// </summary>
    /// <remarks>
    /// A level, a character file and <c>GLOBAL_STATS</c> each carry it under a different path, and
    /// <c>DesignVersion</c> surfaces as both the record and its <c>Value</c> — so this matches the
    /// property name rather than any one path.
    /// </remarks>
    public static bool IsVersionStamp(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.EndsWith(".Version", StringComparison.Ordinal)
               || path.Contains(".Version.", StringComparison.Ordinal);
    }

    /// <summary>False once <see cref="Limit"/> differences have been collected.</summary>
    private static bool Walk(object? expected, object? actual, string path, int depth,
                             List<Difference> found)
    {
        if (found.Count >= Limit || depth > MaxDepth)
        {
            return false;
        }

        if (expected is null && actual is null)
        {
            return true;
        }

        if (expected is null)
        {
            return Note(found, path, DifferenceKind.Materialised, "null", Show(actual));
        }

        if (actual is null)
        {
            return Note(found, path, DifferenceKind.Cleared, Show(expected), "null");
        }

        var type = expected.GetType();
        if (type != actual.GetType())
        {
            return Note(found, path, DifferenceKind.TypeChanged, type.Name, actual.GetType().Name);
        }

        if (IsSimple(type))
        {
            return expected.Equals(actual)
                   || Note(found, path, DifferenceKind.Value, Show(expected), Show(actual));
        }

        if (expected is IEnumerable left && actual is IEnumerable right)
        {
            return WalkList(left, right, path, depth, found);
        }

        var properties = Readable(type);
        if (properties.Length == 0)
        {
            // Nothing to walk into -- a struct the port models as opaque. Member-wise equality is
            // all there is, and for a type with no list members it is correct.
            return expected.Equals(actual)
                   || Note(found, path, DifferenceKind.Value, Show(expected), Show(actual));
        }

        foreach (var property in properties)
        {
            object? a;
            object? b;
            try
            {
                a = property.GetValue(expected);
                b = property.GetValue(actual);
            }
            catch (TargetInvocationException)
            {
                // A computed property that throws on one of the two is not a serialized field;
                // the wire content is carried by the ones that do not.
                continue;
            }

            if (!Walk(a, b, $"{path}.{property.Name}", depth + 1, found))
            {
                return false;
            }
        }

        return true;
    }

    private static bool WalkList(IEnumerable expected, IEnumerable actual, string path, int depth,
                                 List<Difference> found)
    {
        var left = Materialise(expected);
        var right = Materialise(actual);

        if (left.Count != right.Count)
        {
            var kind = right.Count > left.Count ? DifferenceKind.Grown : DifferenceKind.Shrunk;
            if (!Note(found, path, kind, $"{left.Count} entries", $"{right.Count} entries"))
            {
                return false;
            }
        }

        // The shared prefix is walked whichever way the count went, so a table that filled out to
        // its modern size is distinguishable from one whose contents also moved.
        int shared = Math.Min(left.Count, right.Count);
        for (int i = 0; i < shared; i++)
        {
            if (!Walk(left[i], right[i], $"{path}[{i}]", depth + 1, found))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Note(List<Difference> found, string path, DifferenceKind kind,
                             string before, string after)
    {
        found.Add(new Difference(path, kind, before, after));
        return found.Count < Limit;
    }

    private static List<object?> Materialise(IEnumerable source)
    {
        var items = new List<object?>();
        foreach (object? item in source)
        {
            items.Add(item);
        }

        return items;
    }

    private static PropertyInfo[] Readable(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)];

    private static bool IsSimple(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(DateTime)
        || type == typeof(Guid);

    private static string Show(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        byte[] b => $"byte[{b.Length}]",

        // A scalar's value is the whole of what it says; a structure's is not, and printing one
        // fills the report with nested ToString noise that hides the path it is attached to.
        _ when IsSimple(value.GetType()) => value.ToString() ?? value.GetType().Name,
        _ => value.GetType().Name,
    };
}
