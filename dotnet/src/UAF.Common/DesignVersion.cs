namespace UAF.Common;

/// <summary>
/// A Dungeon Craft design/data file format version.
/// </summary>
/// <remarks>
/// <para>
/// The legacy engine stores this as a bare <see cref="double"/> and compares it directly against
/// ~98 named constants (see <c>DesignVersion.Generated.cs</c>). The values span two eras:
/// legacy formats in <c>0.500 .. 0.998918</c>, and the modern product line at <c>5.24 .. 5.29</c>.
/// </para>
/// <para>
/// The axis is monotonic, so ordering comparisons are meaningful across the whole range, but
/// <b>nothing may assume the value is below 1.0</b> — a mistake the dead
/// <c>src/Shared/ProjectVersion.h</c> invites, since it stops at 0.998110. This type is
/// deliberately opaque: no arithmetic, no "is this plausible?" range validation, only ordering.
/// </para>
/// <para>
/// Not to be confused with <c>CHARACTER_VERSION</c> (<c>0x80000001</c>), which is a separate
/// integer scheme, or with the PE <c>VERSIONINFO</c> resource (5.2.x), which is a marketing string.
/// </para>
/// </remarks>
public readonly partial struct DesignVersion
    : IEquatable<DesignVersion>, IComparable<DesignVersion>
{
    /// <summary>The raw value as stored in the file.</summary>
    public double Value { get; }

    public DesignVersion(double value) => Value = value;

    /// <summary>
    /// Version assumed when a file carries no version stamp at all — the last build that did not
    /// write one. Mirrors the fallback in <c>Shared/Char.cpp:6948</c>.
    /// </summary>
    public static DesignVersion Unstamped => V0563;

    public bool Equals(DesignVersion other) => Value.Equals(other.Value);

    public override bool Equals(object? obj) => obj is DesignVersion v && Equals(v);

    public override int GetHashCode() => Value.GetHashCode();

    public int CompareTo(DesignVersion other) => Value.CompareTo(other.Value);

    /// <summary>
    /// Formats the way the legacy engine does in its diagnostics (<c>"%4.7f"</c>), so log lines
    /// can be compared against the C++ oracle's output verbatim.
    /// </summary>
    public override string ToString() =>
        Value.ToString("F7", System.Globalization.CultureInfo.InvariantCulture);

    public static bool operator ==(DesignVersion a, DesignVersion b) => a.Value == b.Value;

    public static bool operator !=(DesignVersion a, DesignVersion b) => a.Value != b.Value;

    public static bool operator <(DesignVersion a, DesignVersion b) => a.Value < b.Value;

    public static bool operator >(DesignVersion a, DesignVersion b) => a.Value > b.Value;

    public static bool operator <=(DesignVersion a, DesignVersion b) => a.Value <= b.Value;

    public static bool operator >=(DesignVersion a, DesignVersion b) => a.Value >= b.Value;
}
