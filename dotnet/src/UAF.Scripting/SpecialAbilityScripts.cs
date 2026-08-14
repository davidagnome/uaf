namespace UAF.Scripting;

/// <summary>
/// What to do after a script has run.
/// </summary>
/// <remarks>
/// The reference's <c>CBRESULT</c>: a callback examines each script's result and may stop the rest
/// (<c>Specab.cpp:1937</c>). Which of the two a caller wants is the whole difference between
/// "run them all and take the last answer" and "ask each until one says yes".
/// </remarks>
public enum SpecialAbilityScriptVerdict
{
    /// <summary>Keep going.</summary>
    Continue,

    /// <summary>Stop; this result is the answer.</summary>
    Stop,
}

/// <summary>
/// Runs the GPDL scripts an object's special abilities carry.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the join the <c>$RUN_*_SCRIPTS</c> family is built on.</b> A spell, item or
/// character holds only ability <i>names</i>; each name is looked up in
/// <c>specialAbilities.dat</c>, and an entry there keyed by the script's name holds the source.
/// <c>SPECIAL_ABILITIES::RunScripts</c> (<c>Specab.cpp:1876</c>) is the original.
/// </para>
/// <para>
/// <b>Each script runs on its own machine, not nested inside the caller's.</b> The reference does
/// <c>gpdlStack.Push()</c> / <c>Pop()</c> around every execution — a stack of interpreters rather
/// than a re-entrant one — so a script that runs another cannot disturb the stack it was called
/// from. A fresh <see cref="GpdlVirtualMachine"/> per script is that, and it is why the host is
/// shared while nothing else is.
/// </para>
/// </remarks>
public static class SpecialAbilityScripts
{
    /// <summary>
    /// What every special-ability script is wrapped in before compiling
    /// (<c>Specab.cpp:1776</c>).
    /// </summary>
    /// <remarks>
    /// The source in the database is a bare <i>body</i> — statements with no function around
    /// them — so it cannot be compiled as it stands. The wrapper supplies the declaration and the
    /// call, which is why <see cref="EntryPoint"/> is the same for every script in the game.
    /// </remarks>
    public const string Prologue = "$PUBLIC $FUNC SA(){";

    /// <inheritdoc cref="Prologue"/>
    /// <remarks>
    /// <b>The newline matters.</b> A script whose last line is a comment would otherwise swallow
    /// the closing brace.
    /// </remarks>
    public const string Epilogue = "\n} SA ;";

    /// <summary>The function the wrapper declares, and the only entry point ever called.</summary>
    public const string EntryPoint = "SA";

    /// <summary>Wraps a script body so it can be compiled.</summary>
    public static string Wrap(string body) => Prologue + body + Epilogue;

    /// <summary>
    /// Compiles one script body, or reports why it would not.
    /// </summary>
    /// <remarks>
    /// <b>A script that fails to compile is dropped, not raised.</b> The reference logs it, marks
    /// the entry <c>SPECAB_SCRIPTERROR</c> so it is never retried, and carries on with the other
    /// abilities — one broken script does not stop the rest from running.
    /// </remarks>
    public static GpdlProgram? Compile(string body, out string error)
    {
        var compiler = new GpdlCompiler();

        if (compiler.Compile(Wrap(body)) != 0)
        {
            error = string.Join("; ", compiler.Errors);
            return null;
        }

        error = string.Empty;
        return GpdlProgram.FromCompiler(compiler);
    }

    /// <summary>
    /// Runs every script the named abilities carry for <paramref name="scriptName"/>.
    /// </summary>
    /// <param name="abilityNames">
    /// The ability names the object carries — the keys of its <c>SpecabBlock</c>.
    /// </param>
    /// <param name="lookup">
    /// Given an ability name and a script name, the GPDL source — or null when that ability has no
    /// such script.
    /// <b>A delegate rather than the database itself, deliberately:</b> <c>UAF.Scripting</c> and
    /// <c>UAF.Serialization</c> are independent siblings, and taking
    /// <c>SpecialAbilityDefinition</c> here would make the scripting layer depend on the
    /// serialization one. The caller that owns both supplies this.
    /// </param>
    /// <param name="scriptName">Which of an ability's scripts to run.</param>
    /// <param name="host">The host every script sees. Shared, unlike the machines.</param>
    /// <param name="examine">
    /// Consulted after each script with that script's result. Returning
    /// <see cref="SpecialAbilityScriptVerdict.Stop"/> ends the run and makes that result the
    /// answer. Null runs them all.
    /// </param>
    /// <param name="onError">
    /// Told about a script that would not compile, with the compiler's message. Null discards it —
    /// which is what the reference does apart from a debug line.
    /// </param>
    /// <returns>
    /// The last result produced, or empty when no ability carried the script. <b>Not a
    /// concatenation:</b> the reference keeps one hook parameter and each script overwrites it, so
    /// the answer is the last script's, not all of them joined.
    /// </returns>
    public static string Run(
        IEnumerable<string> abilityNames,
        Func<string, string, string?> lookup,
        string scriptName,
        IGpdlHost host,
        Func<string, SpecialAbilityScriptVerdict>? examine = null,
        Action<string, string>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(abilityNames);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(host);

        string result = string.Empty;

        foreach (string abilityName in abilityNames)
        {
            if (lookup(abilityName, scriptName) is not { } body)
            {
                continue;
            }

            if (Compile(body, out string error) is not { } program)
            {
                onError?.Invoke(abilityName, error);
                continue;
            }

            // A machine of its own -- the gpdlStack.Push()/Pop() the reference wraps each
            // execution in. The host is shared; the interpreter is not.
            result = new GpdlVirtualMachine(program, host).Execute(EntryPoint);

            if (examine?.Invoke(result) == SpecialAbilityScriptVerdict.Stop)
            {
                break;
            }
        }

        return result;
    }
}
