using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Serialization;

namespace UAFedit.Spells;

/// <summary>
/// One of a spell's seven GPDL script slots.
/// </summary>
/// <remarks>
/// <para>
/// <b>All seven slots exist on every spell, whatever version wrote it.</b>
/// <see cref="SpellRecordReader"/> fills the ones a version did not have with
/// <see cref="SpellScript.Empty"/> rather than shortening the list, so a slot's index always means
/// the same script. This view model therefore never has to ask what version the record came from.
/// </para>
/// <para>
/// <b>Editing the source clears the binary</b>, for the reason it is kept at all: the reference
/// empties every compiled script as it loads (<c>Shared/Spell.cpp:4230</c>) to force a recompile,
/// and a binary left beside a changed source is the one state the engine is careful never to be in.
/// </para>
/// </remarks>
public sealed partial class SpellScriptViewModel : EditableViewModel
{
    private readonly SpellScript original;

    public SpellScriptViewModel(SpellScriptSlot slot, SpellScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        Slot = slot;
        original = script;
        Source = script.Source;

        ResetDirty();
    }

    public SpellScriptSlot Slot { get; }

    /// <summary>What the original editor's script dropdown calls this slot.</summary>
    public string Name => SpellChoices.ScriptName(Slot);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string source = string.Empty;

    /// <summary>The last compile, or <see cref="GpdlScriptDiagnostics.NotAttempted"/>.</summary>
    [ObservableProperty]
    private GpdlScriptDiagnostics diagnostics = GpdlScriptDiagnostics.NotAttempted;

    /// <summary>Whether <see cref="Diagnostics"/> reflects a compile that was actually run.</summary>
    [ObservableProperty]
    private bool hasCompiled;

    public bool IsEmpty => Source.Length == 0;

    /// <summary>The name, marked when the slot holds something — for a picker.</summary>
    public string Label => IsEmpty ? Name : $"{Name} •";

    /// <summary>Compiles the body the way the original's <c>Test Syntax</c> button did.</summary>
    /// <remarks>
    /// <b>An empty slot is skipped rather than reported as valid.</b> The wrapper round nothing
    /// compiles, so checking it would put a green tick on six slots the designer never filled in.
    /// </remarks>
    [RelayCommand]
    public void Compile()
    {
        if (IsEmpty)
        {
            Diagnostics = GpdlScriptDiagnostics.NotAttempted;
            HasCompiled = false;
            return;
        }

        Diagnostics = GpdlScriptCheck.Spell(Source);
        HasCompiled = true;
    }

    public SpellScript ToScript() =>
        Source == original.Source ? original : new SpellScript(Source, string.Empty);

    public void Revert()
    {
        Source = original.Source;
        Diagnostics = GpdlScriptDiagnostics.NotAttempted;
        HasCompiled = false;
        ResetDirty();
    }

    protected override bool IsEdit(string? propertyName) =>
        propertyName is not (nameof(Diagnostics) or nameof(HasCompiled));
}
