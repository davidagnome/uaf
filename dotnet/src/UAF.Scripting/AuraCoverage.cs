namespace UAF.Scripting;

/// <summary>
/// Filling in an aura's cell mask for its shape (the <c>Determine*Coverage</c> family,
/// <c>Combatants.cpp:8654</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two of the three shapes compute nothing, and only one of those is honest about it.</b>
/// <c>DetermineNULLCoverage</c> zeroes the mask, which is exactly right for a shape that covers
/// nothing. <c>DetermineGlobalCoverage</c> is <c>NotImplemented(0x321abe, false)</c> and returns
/// having touched no cell at all — so a <c>Global</c> aura does not cover the map, it <i>keeps
/// whatever mask it had last</i>. On a newly created aura that is all zeroes; on one that was an
/// annular sector a moment ago it is the sector, frozen.
/// </para>
/// <para>
/// <b>The default arm covers nothing either</b>, after the same non-fatal complaint
/// (<c>NotImplemented(0x5dd9, false)</c> then <c>DetermineNULLCoverage</c>) — but it is
/// unreachable, because <see cref="AuraShape"/> has exactly the three values and
/// <see cref="AuraOps.SetShape"/> cannot produce a fourth.
/// </para>
/// <para>
/// <b>Annular sector coverage is not ported yet.</b> It is ~830 lines across
/// <c>DetermineAnnularCoverage</c>, <c>LocateAuraCenters</c> and the two octant walkers, and it is
/// the only shape that computes anything. Until it lands, an annular aura throws rather than
/// quietly covering nothing — a silent empty mask would look exactly like a working
/// <c>Global</c>.
/// </para>
/// </remarks>
public static class AuraCoverage
{
    /// <summary>
    /// Recomputes <see cref="Aura.Cells"/> from the aura's <i>committed</i> shape.
    /// </summary>
    /// <remarks>
    /// Reads <see cref="Aura.Current"/>, never <see cref="Aura.Pending"/> — the placement check
    /// commits first and computes after.
    /// </remarks>
    public static void Determine(Aura aura)
    {
        ArgumentNullException.ThrowIfNull(aura);

        switch (aura.Current.Shape)
        {
            case AuraShape.Null:
                Array.Clear(aura.Cells);
                break;

            case AuraShape.Global:
                // Deliberately nothing. See the class remarks: the reference's Global arm is
                // NotImplemented and leaves the previous mask standing.
                break;

            case AuraShape.AnnularSector:
                throw new NotSupportedException(
                    "AURA_SHAPE_ANNULARSECTOR needs DetermineAnnularCoverage (Combatants.cpp:8182) " +
                    "with LocateAuraCenters and the AnnularOctantX/Y walkers -- about 830 lines of " +
                    "geometry, not yet ported. Throwing rather than covering nothing, because an " +
                    "empty mask is indistinguishable from a working aura that happens to reach " +
                    "nobody.");

            default:
                Array.Clear(aura.Cells);
                break;
        }
    }

    /// <summary>Whether the aura covers a square, which is <c>cells[i] &amp; 1</c>.</summary>
    /// <remarks>
    /// <b>Only bit 0 is ever read, and nothing writes the other seven.</b> The mask is a byte per
    /// square rather than a bit per square for no reason the source gives.
    /// </remarks>
    public static bool Covers(Aura aura, int x, int y, int mapWidth)
    {
        ArgumentNullException.ThrowIfNull(aura);

        int index = (y * mapWidth) + x;

        return index >= 0 && index < aura.Cells.Length && (aura.Cells[index] & 1) != 0;
    }
}
