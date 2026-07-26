namespace UAF.Serialization;

/// <summary>
/// The empty-string sentinel convention used by the legacy <c>AS</c> / <c>DAS</c> macros
/// (<c>Externs.h:1937</c>).
/// </summary>
/// <remarks>
/// <para>
/// MFC's archive cannot round-trip an empty <c>CString</c> unambiguously in this codebase's usage,
/// so the writer substitutes a sentinel — <c>ArchiveBlank</c>, which is <c>"*"</c>
/// (<c>Globals.cpp:167</c>) — and the reader maps it back to empty.
/// </para>
/// <para>
/// The reader deliberately accepts a literal <c>"*"</c> <b>as well as</b> the configured
/// sentinel. The C++ comment is explicit that many released versions of Dungeon Craft shipped
/// with <c>"*"</c>, so it must be honoured regardless of what <c>ArchiveBlank</c> is set to.
/// Dropping that leniency would turn a genuine empty string into a literal asterisk in every
/// affected design.
/// </para>
/// </remarks>
public static class ArchiveStringConventions
{
    /// <summary>The sentinel written in place of an empty string. <c>Globals.cpp:167</c>.</summary>
    public const string ArchiveBlank = "*";

    /// <summary>
    /// Applies <c>DAS</c> semantics to a freshly-read string: sentinel becomes empty.
    /// </summary>
    public static string Decode(string value, string archiveBlank = ArchiveBlank) =>
        value == archiveBlank || value == "*" ? string.Empty : value;

    /// <summary>
    /// Applies <c>AS</c> semantics before writing: empty becomes the sentinel.
    /// </summary>
    public static string Encode(string value, string archiveBlank = ArchiveBlank) =>
        string.IsNullOrEmpty(value) ? archiveBlank : value;
}
