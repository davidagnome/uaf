namespace UAF.Scripting;

/// <summary>
/// The three aura setters that take a name rather than a number, and disagree about what to do
/// with one they do not recognise (<c>GPDL::AURA_FUNCTION</c>, <c>GPDLexec.cpp:934</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>All three write the pending buffer, never the current one.</b> See <see cref="Aura"/>.
/// </para>
/// <para>
/// <b>And all three handle a bad name differently</b>, which is the only reason they are together
/// here: <c>$AURA_Shape</c> falls back to <see cref="AuraShape.Null"/> and logs,
/// <c>$AURA_Attach</c> falls back to <see cref="AuraAttachment.None"/> and logs, and
/// <c>$AURA_Wavelength</c> does neither — it is written as three bare <c>if</c>s with no
/// <c>else</c>, so an unrecognised wavelength silently leaves the previous pending value in place.
/// A design that misspells "Xray" gets a working visible aura and no complaint.
/// </para>
/// </remarks>
public static class AuraOps
{
    /// <summary>
    /// <c>$AURA_Shape</c>'s body. Unrecognised → <see cref="AuraShape.Null"/>, which covers nothing.
    /// </summary>
    /// <returns>Whether the name was one of the two the reference tests for.</returns>
    public static bool SetShape(Aura aura, string name)
    {
        ArgumentNullException.ThrowIfNull(aura);

        switch (name)
        {
            case "Global":
                aura.Pending.Shape = AuraShape.Global;
                return true;
            case "AnnularSector":
                aura.Pending.Shape = AuraShape.AnnularSector;
                return true;
            default:
                aura.Pending.Shape = AuraShape.Null;
                return false;
        }
    }

    /// <summary>
    /// <c>$AURA_Attach</c>'s body. Unrecognised → <see cref="AuraAttachment.None"/>.
    /// </summary>
    /// <remarks>
    /// <b><c>"XY"</c> also blanks the pending coordinate to (-1, -1).</b> The other two do not
    /// touch it — they take their position off the combatant at placement time — so attaching to XY
    /// and never calling <c>$AURA_Location</c> leaves the aura off the map rather than at the
    /// origin.
    /// </remarks>
    /// <returns>Whether the name was one of the three the reference tests for.</returns>
    public static bool SetAttachment(Aura aura, string name)
    {
        ArgumentNullException.ThrowIfNull(aura);

        switch (name)
        {
            case "Combatant":
                aura.Pending.Attachment = AuraAttachment.Combatant;
                return true;
            case "CombatantFacing":
                aura.Pending.Attachment = AuraAttachment.CombatantFacing;
                return true;
            case "XY":
                aura.Pending.Attachment = AuraAttachment.Xy;
                aura.Pending.X = -1;
                aura.Pending.Y = -1;
                return true;
            default:
                aura.Pending.Attachment = AuraAttachment.None;
                return false;
        }
    }

    /// <summary>
    /// <c>$AURA_Wavelength</c>'s body. <b>Unrecognised → unchanged</b>, silently.
    /// </summary>
    /// <returns>Whether the name matched, for a caller that wants to know what the reference does not.</returns>
    public static bool SetWavelength(Aura aura, string name)
    {
        ArgumentNullException.ThrowIfNull(aura);

        // Three separate ifs in the reference, with no else and no default. Written as a switch
        // here only because it reads better; the absence of a default arm is the transcription.
        switch (name)
        {
            case "Visible":
                aura.Pending.Wavelength = AuraWavelength.Visible;
                return true;
            case "Xray":
                aura.Pending.Wavelength = AuraWavelength.Xray;
                return true;
            case "Neutrino":
                aura.Pending.Wavelength = AuraWavelength.Neutrino;
                return true;
            default:
                return false;
        }
    }
}
