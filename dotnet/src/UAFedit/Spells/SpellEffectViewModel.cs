using UAF.Serialization;

namespace UAFedit.Spells;

/// <summary>
/// One row of a spell's affected-attributes list, read only.
/// </summary>
/// <remarks>
/// <para>
/// The four columns are the reference's (<c>UAFWinEd/SpellDBDlgEx.cpp:955</c>): <c>Affected</c>,
/// <c>Change By</c>, <c>Cumulative</c> and <c>Activation</c>. Editing one opens a dialog of its own
/// (<c>IDD_SPELLATTRIBUTEEDIT</c>) with its own flag radio group, which is a separate piece of work;
/// this shows what a spell does without pretending to change it.
/// </para>
/// <para>
/// <b>The activation script is <see cref="SpellEffect.String2"/>, not
/// <see cref="SpellEffect.Scripts"/>[0].</b> The port's names hide this and it is easy to get
/// backwards. <c>m_string2</c> is <c>ActivationScript</c> and <c>m_string3</c> — the first entry of
/// the <c>Scripts</c> list — is <c>ActivationBinary</c>, the <i>compiled</i> form
/// (<c>Shared/class.h:2410</c>, <c>:2415</c>). So <c>Scripts</c> begins with a binary, and the
/// source that goes with it sits outside the list entirely. The rest run
/// Modification source, Modification binary, SavingThrow, SavingThrowFailed, SavingThrowSucceeded,
/// each source followed by its binary (<c>class.h:2419</c>–<c>:2451</c>).
/// </para>
/// </remarks>
public sealed class SpellEffectViewModel(SpellEffect effect)
{
    /// <summary><c>EFFECT_CUMULATIVE</c> (<c>Shared/class.h:2352</c>).</summary>
    public const uint CumulativeFlag = 0x00000010;

    private readonly SpellEffect effect = effect
        ?? throw new ArgumentNullException(nameof(effect));

    /// <summary>The attribute this effect changes.</summary>
    public string Affected => effect.IndexKey;

    /// <summary>The amount, as a dice expression.</summary>
    public string ChangeBy => effect.ChangeData.Text;

    public bool IsCumulative => (effect.Flags & CumulativeFlag) != 0;

    public string Cumulative => IsCumulative ? "Yes" : "No";

    /// <summary>
    /// The activation script, flattened onto one line as the list does.
    /// </summary>
    /// <remarks>
    /// The reference strips <c>\r</c>, <c>\n</c> and <c>\t</c> before putting a script in a list
    /// column; without that a multi-line script draws as one very tall row.
    /// </remarks>
    public string Activation =>
        string.Concat(effect.String2.Where(c => c is not ('\r' or '\n' or '\t')));

    public uint Flags => effect.Flags;
}
