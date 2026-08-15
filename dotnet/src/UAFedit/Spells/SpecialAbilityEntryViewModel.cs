using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Data;

namespace UAFedit.Spells;

/// <summary>
/// One entry of a special ability: its kind, its bare name, and its payload.
/// </summary>
/// <remarks>
/// <para>
/// <b>The kind is not stored — it is the brackets round the name.</b>
/// <c>specialAbilities.txt</c> holds nothing but <c>key = value</c> lines, and
/// <c>DecodeSpecAbObject</c> (<c>UAFWinEd/ItemDB.cpp:3202</c>) reads <c>[x]</c> as a script,
/// <c>(x)</c> as a variable, <c>&lt;x&gt;</c> as an integer table and anything else as a constant.
/// So a kind picker in this editor is not a label: it rewrites the entry's identity in the file,
/// and a script renamed to a constant is a script the engine will never find.
/// </para>
/// <para>
/// <b>That makes the round trip a real thing to check, not a formality.</b> Three characters is the
/// minimum for a bracketed form (<see cref="SpecialAbilitiesFile"/>), so a script with an empty
/// name encodes to <c>[]</c> and reads back as a <i>constant literally named <c>[]</c></i>. A
/// constant whose name the user brackets goes the other way and becomes a script. Neither is
/// rejected here — the editor says what would happen through <see cref="ReparsedKind"/> and
/// <see cref="IsFaithful"/> rather than second-guessing the design.
/// </para>
/// </remarks>
public sealed partial class SpecialAbilityEntryViewModel : EditableViewModel
{
    private readonly SpecialAbilityEntry original;

    public SpecialAbilityEntryViewModel(SpecialAbilityEntry entry, string abilityName)
    {
        ArgumentNullException.ThrowIfNull(entry);

        original = entry;
        AbilityName = abilityName ?? string.Empty;

        Name = entry.Name;
        Value = entry.Value;
        Kind = entry.Kind;

        ResetDirty();
    }

    /// <summary>The ability this belongs to. Shown in listings; not part of the entry.</summary>
    public string AbilityName { get; }

    /// <summary>The name with its brackets stripped, as the parser hands it over.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Key))]
    [NotifyPropertyChangedFor(nameof(ReparsedKind))]
    [NotifyPropertyChangedFor(nameof(IsFaithful))]
    private string name = string.Empty;

    /// <summary>
    /// The payload: GPDL source, a parameter's value, a table's numbers, or a constant's text.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TableNumbers))]
    [NotifyPropertyChangedFor(nameof(TableTruncation))]
    private string value = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Key))]
    [NotifyPropertyChangedFor(nameof(ReparsedKind))]
    [NotifyPropertyChangedFor(nameof(IsFaithful))]
    [NotifyPropertyChangedFor(nameof(IsScript))]
    [NotifyPropertyChangedFor(nameof(IsIntegerTable))]
    [NotifyPropertyChangedFor(nameof(WantsMultilineEditor))]
    [NotifyPropertyChangedFor(nameof(TableNumbers))]
    [NotifyPropertyChangedFor(nameof(TableTruncation))]
    private SpecialAbilityEntryKind kind;

    /// <summary>The last compile of this entry, when it is a script.</summary>
    [ObservableProperty]
    private GpdlScriptDiagnostics diagnostics = GpdlScriptDiagnostics.NotAttempted;

    /// <summary>Whether <see cref="Diagnostics"/> reflects a compile that was actually run.</summary>
    [ObservableProperty]
    private bool hasCompiled;

    /// <summary>Every kind, for a picker. Ordered as the enum declares them.</summary>
    public static IReadOnlyList<SpecialAbilityEntryKind> Kinds { get; } =
        [.. Enum.GetValues<SpecialAbilityEntryKind>()];

    public bool IsScript => Kind == SpecialAbilityEntryKind.Script;

    public bool IsIntegerTable => Kind == SpecialAbilityEntryKind.IntegerTable;

    /// <summary>Scripts and tables are many lines; a constant or a parameter is one.</summary>
    public bool WantsMultilineEditor => IsScript || IsIntegerTable;

    /// <summary>The key as it would sit in the file — the name, bracketed for its kind.</summary>
    public string Key => Encode(Name, Kind);

    /// <summary>What <see cref="Key"/> would be read back as.</summary>
    public SpecialAbilityEntryKind ReparsedKind => Decode(Key);

    /// <summary>
    /// Whether this entry survives a write and a read — see the class remarks for when it does not.
    /// </summary>
    public bool IsFaithful => ReparsedKind == Kind && !Name.Contains('=', StringComparison.Ordinal);

    /// <summary>Why the entry would not survive a round trip, or empty when it would.</summary>
    /// <remarks>
    /// The <c>=</c> case is separate from the bracketing one: the decoder splits a line on its
    /// <i>first</i> <c>=</c> with no escape handling at all, so a name containing one is cut in
    /// half and the rest of it becomes the start of the value.
    /// </remarks>
    public string FidelityWarning
    {
        get
        {
            if (Name.Contains('=', StringComparison.Ordinal))
            {
                return "A name containing '=' is split at that character when the file is read.";
            }

            return ReparsedKind == Kind
                ? string.Empty
                : $"'{Key}' reads back as {ReparsedKind}, not {Kind}.";
        }
    }

    /// <summary>
    /// The numbers the engine would actually get from an integer table.
    /// </summary>
    /// <remarks>
    /// <b>Not "the numbers in the text".</b> The reference's loop advances only when its
    /// <c>sscanf</c> matches, so the first line it cannot read is where the table <i>ends</i> —
    /// everything below a blank line or a comment is silently invisible to the game. Showing the
    /// parsed count next to the line count is the cheapest way to make that visible, since a design
    /// with a stray blank line looks perfectly fine in a text box.
    /// </remarks>
    public IReadOnlyList<int> TableNumbers => IsIntegerTable ? ParseTable(Value) : [];

    /// <summary>How the table stops short, or empty when every line of it is read.</summary>
    public string TableTruncation
    {
        get
        {
            if (!IsIntegerTable)
            {
                return string.Empty;
            }

            int lines = Value.Split('\n').Count(l => l.Trim().Length > 0);
            int read = TableNumbers.Count;

            return read >= lines
                ? string.Empty
                : $"{read} of {lines} lines are read; the table stops at the first line that "
                  + "does not begin with a number.";
        }
    }

    /// <summary>Compiles the body, when this is a script. Does nothing for the other kinds.</summary>
    [RelayCommand]
    public void Compile()
    {
        if (!IsScript)
        {
            return;
        }

        Diagnostics = GpdlScriptCheck.SpecialAbility(Value);
        HasCompiled = true;
    }

    /// <summary>The edited entry.</summary>
    public SpecialAbilityEntry ToEntry() => new(Name, Value, Kind);

    /// <summary>Throws away the edits.</summary>
    public void Revert()
    {
        Name = original.Name;
        Value = original.Value;
        Kind = original.Kind;
        ResetDirty();
    }

    /// <remarks>
    /// A compile result is an observation about the entry, not a change to it, so it must not make
    /// the ability look unsaved.
    /// </remarks>
    protected override bool IsEdit(string? propertyName) =>
        propertyName is not (nameof(Diagnostics) or nameof(HasCompiled));

    /// <summary>Puts the brackets back on a name.</summary>
    public static string Encode(string name, SpecialAbilityEntryKind kind) => kind switch
    {
        SpecialAbilityEntryKind.Script => $"[{name}]",
        SpecialAbilityEntryKind.Variable => $"({name})",
        SpecialAbilityEntryKind.IntegerTable => $"<{name}>",
        _ => name,
    };

    /// <summary>
    /// Reads a key's brackets, mirroring the decoder.
    /// </summary>
    /// <remarks>
    /// <b>A copy of a private method, and pinned by a test that runs the real parser over the same
    /// keys.</b> <c>SpecialAbilitiesFile.Entry</c> is not public and this has to answer on every
    /// keystroke, so parsing a synthetic one-entry file to find out is not an option. The
    /// three-character floor is the part that has to be copied exactly: <c>[]</c> is two
    /// characters, so it is a constant.
    /// </remarks>
    public static SpecialAbilityEntryKind Decode(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length < 3)
        {
            return SpecialAbilityEntryKind.Constant;
        }

        return (key[0], key[^1]) switch
        {
            ('[', ']') => SpecialAbilityEntryKind.Script,
            ('(', ')') => SpecialAbilityEntryKind.Variable,
            ('<', '>') => SpecialAbilityEntryKind.IntegerTable,
            _ => SpecialAbilityEntryKind.Constant,
        };
    }

    /// <summary>
    /// The reference's table parse (<c>GameScriptHost.IntegerTable</c>): one number per line,
    /// stopping at the first line that does not start with one.
    /// </summary>
    private static List<int> ParseTable(string text)
    {
        var numbers = new List<int>();

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimStart();

            if (trimmed.Length == 0
                || !(char.IsAsciiDigit(trimmed[0]) || trimmed[0] is '-' or '+'))
            {
                break;
            }

            // The engine uses MfcString.Atoi, which stops at the first character it cannot read;
            // int.TryParse over the leading run is the same answer for every well-formed line and
            // differs only for text this display is already flagging.
            int end = trimmed[0] is '-' or '+' ? 1 : 0;
            while (end < trimmed.Length && char.IsAsciiDigit(trimmed[end]))
            {
                end++;
            }

            numbers.Add(int.TryParse(trimmed.AsSpan(0, end), out int n) ? n : 0);
        }

        return numbers;
    }
}
