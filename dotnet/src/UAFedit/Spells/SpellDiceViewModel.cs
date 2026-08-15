using CommunityToolkit.Mvvm.ComponentModel;
using UAF.Serialization;

namespace UAFedit.Spells;

/// <summary>
/// One <c>DICEPLUS</c> field of a spell — a duration or one of the five targeting parameters.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only the modern <c>DP2</c> form is editable, and that is not a limitation so much as the
/// shape of the data.</b> A <c>DP2</c> is two strings and nothing else, the expression and its
/// compiled binary; <c>DP0</c> and <c>DP1</c> are packed numeric fields with a list of adjustment
/// terms and no text at all (<see cref="DicePlusReader"/>). Offering a text box over a <c>DP1</c>
/// would let a designer type an expression that the record has nowhere to put.
/// </para>
/// <para>
/// <b>Editing the text clears the compiled binary, deliberately.</b> The reference empties every
/// one of these on load to force a recompile, so a stale binary beside a changed expression is a
/// state the engine never has — and the binary is what actually runs.
/// </para>
/// <para>
/// <b>The label is view state, not content.</b> It changes whenever the spell's targeting type
/// changes (<see cref="SpellChoices.ParameterLabels"/>), which is a relabelling of the same stored
/// number and must not mark the spell as edited.
/// </para>
/// </remarks>
public sealed partial class SpellDiceViewModel : EditableViewModel
{
    private readonly DicePlus original;

    public SpellDiceViewModel(string label, DicePlus dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        original = dice;
        Label = label ?? string.Empty;
        Text = dice.Text;

        ResetDirty();
    }

    /// <summary>What this parameter means for the spell's current targeting type.</summary>
    /// <remarks>Empty for a parameter the targeting type gives no meaning to.</remarks>
    [ObservableProperty]
    private string label = string.Empty;

    /// <summary>The expression source.</summary>
    [ObservableProperty]
    private string text = string.Empty;

    /// <summary>
    /// Whether the spell's other settings leave this field any meaning.
    /// </summary>
    /// <remarks>
    /// <b>Not simply "the label is empty".</b> The five parameters go dark because the targeting
    /// type does not use them, and <c>Duration</c> goes dark because the duration rate is
    /// <c>permanent</c> (<c>SpellDBDlgEx.cpp:247</c>) — which keeps its caption. The reference
    /// disables rather than hides in both cases, and so does this: a greyed box says the value is
    /// still in there and is being ignored, which a missing box does not.
    /// </remarks>
    [ObservableProperty]
    private bool isUsed = true;

    /// <summary>The form actually on the wire — <c>DP0</c>, <c>DP1</c> or <c>DP2</c>.</summary>
    public string Tag => original.Tag;

    /// <summary>False for the two legacy forms, which carry no text to edit.</summary>
    public bool IsEditable => DicePlusReader.IsTextForm(original.Tag);

    /// <summary>What a legacy form holds, since its text box shows nothing.</summary>
    public string LegacyValue =>
        IsEditable
            ? string.Empty
            : $"{original.NumDice}d{original.NumSides}{Signed(original.Bonus)} "
              + $"[{original.Tag}, {original.Adjustments.Count} adjustments]";

    /// <summary>The edited expression.</summary>
    public DicePlus ToDice() =>
        Text == original.Text ? original : original with { Text = Text, Binary = string.Empty };

    public void Revert()
    {
        Text = original.Text;
        ResetDirty();
    }

    /// <remarks>Relabelling is the view following the targeting combo, not an edit.</remarks>
    protected override bool IsEdit(string? propertyName) =>
        propertyName is not (nameof(Label) or nameof(IsUsed));

    private static string Signed(int bonus) => bonus switch
    {
        0 => string.Empty,
        > 0 => $"+{bonus}",
        _ => bonus.ToString(),
    };
}
