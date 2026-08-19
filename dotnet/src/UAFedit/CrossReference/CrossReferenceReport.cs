namespace UAFedit.CrossReference;

/// <summary>What kind of thing a cross-referenced resource is.</summary>
/// <remarks>
/// A narrowing of the reference's <c>CR_TYPE</c> (<c>UAFWinEd/CrossReference.h:20</c>), which has
/// 21 members. These are the ones this build can answer <i>both</i> halves of the question for —
/// what names a resource, and whether that resource is really there.
/// </remarks>
public enum ResourceKind
{
    /// <summary>An image file, under the design's <c>Resources</c> folder.</summary>
    Art,

    /// <summary>A sound or music file, in the same folder.</summary>
    Sound,
}

/// <summary>One place a resource is named, and what named it.</summary>
/// <param name="Owner">The record doing the naming, as a person would say it.</param>
/// <param name="Path">Where inside that record the name sits.</param>
public sealed record ResourceReference(string Owner, string Path)
{
    public override string ToString() => $"{Owner} — {Path}";
}

/// <summary>One resource, whether it exists, and everything that names it.</summary>
public sealed record CrossReferenceEntry(
    ResourceKind Kind, string Name, bool Exists, IReadOnlyList<ResourceReference> References)
{
    /// <summary>Named by something, and not on disk. A design that will fail in play.</summary>
    public bool IsMissing => !Exists && References.Count > 0;

    /// <summary>On disk, and named by nothing. Dead weight rather than a defect.</summary>
    public bool IsUnreferenced => Exists && References.Count == 0;
}

/// <summary>
/// Every resource a design names or ships, and how the two sets differ.
/// </summary>
/// <remarks>
/// <para>
/// The two questions worth asking, and the two the reference's own dialog puts filter buttons on
/// (<c>CrossReferenceDlg.cpp:434</c>): <b>what is referenced and missing</b>, which breaks a
/// design in play, and <b>what is shipped and referenced by nothing</b>, which merely makes it
/// bigger than it needs to be.
/// </para>
/// </remarks>
/// <param name="Entries">Every resource named or shipped.</param>
/// <param name="ResourcesPresent">
/// Whether the design has a <c>Resources</c> folder to check names against.
/// </param>
public sealed record CrossReferenceReport(
    IReadOnlyList<CrossReferenceEntry> Entries, bool ResourcesPresent)
{
    /// <summary>
    /// Referenced but not on disk, worst first — and empty when there is nowhere to look.
    /// </summary>
    /// <remarks>
    /// <b>A design with no <c>Resources</c> folder is not a design with 154 broken references.</b>
    /// The editor's own <c>DefaultDesign</c> is exactly that: a template whose art comes from the
    /// shared install rather than from beside it. Reporting every name it uses as missing would be
    /// technically true of this folder and useless to a person, and a tool that cries wolf on the
    /// first design you point it at does not get used on the second.
    /// </remarks>
    public IReadOnlyList<CrossReferenceEntry> Missing =>
        !ResourcesPresent
        ? []
        : [.. Entries.Where(e => e.IsMissing)
                   .OrderByDescending(e => e.References.Count)
                   .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>On disk and named by nothing.</summary>
    public IReadOnlyList<CrossReferenceEntry> Unreferenced =>
        [.. Entries.Where(e => e.IsUnreferenced)
                   .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>A line for the status bar.</summary>
    public string Summary =>
        ResourcesPresent
            ? $"{Entries.Count} resources — {Missing.Count} referenced and missing, "
              + $"{Unreferenced.Count} present and unused"
            : $"{Entries.Count} resources named. This design has no Resources folder, so whether "
              + "they exist cannot be checked here.";
}
