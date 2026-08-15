using UAF.Scripting;

namespace UAFedit.Spells;

/// <summary>The outcome of compiling one script body: whether it built, and what went wrong.</summary>
/// <param name="Wrapped">
/// The text actually handed to the compiler, wrapper and all. Kept because every error message
/// carries a line number counted in <i>this</i> text, not in what the user typed — see
/// <see cref="GpdlScriptCheck"/>.
/// </param>
public sealed record GpdlScriptDiagnostics(
    bool Succeeded, IReadOnlyList<string> Errors, string Wrapped)
{
    /// <summary>What an unchecked slot reports: nothing compiled, nothing wrong.</summary>
    public static GpdlScriptDiagnostics NotAttempted { get; } = new(true, [], string.Empty);

    /// <summary>The errors as one block, for a single read-only text box.</summary>
    public string Summary => string.Join(Environment.NewLine, Errors);
}

/// <summary>
/// Compiles a script body the way the original editor's <c>Test Syntax</c> button did.
/// </summary>
/// <remarks>
/// <para>
/// <b>No script stored in a design is compilable as it stands.</b> Both kinds are bare
/// <i>bodies</i> — statements with no function around them — and every caller wraps one before
/// handing it to <c>GPDLCOMP</c>. Checking the raw text would fail every script in every design, so
/// the wrapper is not a detail that can be skipped.
/// </para>
/// <para>
/// <b>There are four wrappers in the reference, and the two used here are the editor's.</b> A spell
/// script is tested as <c>spelltest</c> (<c>UAFWinEd/SpellDBDlgEx.cpp:1677</c>) and a
/// special-ability script as <c>SpecAbTest</c> (<c>UAFWinEd/ChooseSpeclAbDlg.cpp:892</c>, and again
/// in <c>TestAllSpecialAbilities</c> at <c>Shared/Specab.cpp:2199</c>). At <i>run</i> time the
/// names differ again — <c>SA</c> for an ability (<c>Specab.cpp:1776</c>) and a per-spell mangled
/// identifier for a spell (<c>SPELL_DATA::CompileScript</c>, <c>Shared/Spell.cpp:5172</c>).
/// </para>
/// <para>
/// <b>Following the editor rather than the engine is deliberate, and it dodges a real bug.</b> The
/// engine's spell wrapper builds its function name from the spell's own name, removing spaces and
/// replacing <c>|</c> and <c>-</c> with <c>_</c>. GPDL identifiers admit only <c>$ _ A-Z a-z</c>
/// to start and <c>0-9 @</c> besides (<c>GPDLcomp.cpp:367</c>), so a spell called "Mage's Armor"
/// mangles to an identifier the lexer cuts short and the compile fails against a body that is
/// perfectly good. A fixed name asks the question the editor is actually asking — does this
/// <i>body</i> build — and cannot be derailed by what the spell is called.
/// </para>
/// <para>
/// <b>Line numbers in the errors are off by the wrapper.</b> Each prologue is a single line with no
/// newline of its own, so a message about "line 1" is about the first line the user typed; the
/// epilogue adds one line at the end. Nothing here rewrites the numbers — an offset that is right
/// for one wrapper and wrong for another is worse than reporting what the compiler said.
/// </para>
/// <para>
/// <b>This compiles and does not run.</b> The reference's button goes further: with a null message
/// pointer <c>IsSyntaxAndSemanticsValid</c> <i>executes</i> the script against a throwaway
/// character called <c>TmpScriptCheck</c> and shows what it returned (<c>EditText.cpp:76</c>).
/// Running a design's scripts as a side effect of opening an editor is not a behaviour worth
/// porting.
/// </para>
/// </remarks>
public static class GpdlScriptCheck
{
    /// <summary>The editor's spell-script wrapper (<c>SpellDBDlgEx.cpp:1677</c>).</summary>
    public const string SpellEntryPoint = "spelltest";

    /// <summary>The editor's special-ability wrapper (<c>ChooseSpeclAbDlg.cpp:892</c>).</summary>
    public const string SpecialAbilityEntryPoint = "SpecAbTest";

    /// <summary>Checks a special-ability script body.</summary>
    public static GpdlScriptDiagnostics SpecialAbility(string body) =>
        Compile(SpecialAbilityEntryPoint, body);

    /// <summary>Checks one of a spell's seven script slots.</summary>
    public static GpdlScriptDiagnostics Spell(string body) => Compile(SpellEntryPoint, body);

    /// <summary>
    /// Wraps and compiles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An empty body still gets compiled.</b> A wrapper round nothing is a legal empty function,
    /// so this answers success — the right answer for a slot the designer left blank, and it saves
    /// every caller a special case.
    /// </para>
    /// <para>
    /// <b>The error list caps itself at twelve.</b> <c>GpdlLexer.Error</c> stops recording after
    /// twelve and appends a bare <c>"."</c>, so a badly broken script reports a dozen problems and
    /// a full stop rather than hundreds. That last entry is the reference's own marker and is left
    /// in place.
    /// </para>
    /// <para>
    /// <b>Every context is declared available.</b> <see cref="GpdlCompiler.AvailableContexts"/>
    /// defaults to all bits set, which is what <c>TestAllSpecialAbilities</c> passes
    /// (<c>Specab.cpp:2208</c>). The consequence is that this pass cannot catch a script calling a
    /// system function its hook does not supply — a real class of error, invisible here, and the
    /// reference is no better.
    /// </para>
    /// </remarks>
    private static GpdlScriptDiagnostics Compile(string entryPoint, string body)
    {
        // The newline before the closing brace is load-bearing: a body whose last line is a `//`
        // comment would otherwise swallow the brace. Both reference wrappers have it.
        string wrapped = $"$PUBLIC $FUNC {entryPoint}() {{ {body ?? string.Empty}\n}} {entryPoint};";

        var compiler = new GpdlCompiler();
        bool ok = compiler.Compile(wrapped) == 0;

        // Errors is the lexer's live list; copy it, since nothing here keeps the compiler.
        return new GpdlScriptDiagnostics(ok, [.. compiler.Errors], wrapped);
    }
}
