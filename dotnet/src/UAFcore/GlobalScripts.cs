using UAF.Data;
using UAF.Scripting;

namespace UAFcore;

/// <summary>
/// Runs a design's GPDL scripts by name (<c>RunGlobalScript</c>, <c>Shared/Specab.cpp:2097</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the bridge every hook in the port has been waiting on.</b> Turning undead's
/// <c>TURN_ATTEMPT</c>, <c>WHO_TRIES</c>'s <c>Attempt</c> veto, scripted teleporter destinations,
/// combat placement and two of the logic block's input types all call it. A caller names an
/// <i>ability</i> and a <i>script within it</i>; the source comes from the design's
/// <c>specialAbilities.txt</c> (<see cref="SpecialAbilitiesFile"/>), or from the one built-in
/// default when the design defines no such ability.
/// </para>
/// <para>
/// <b>Compilation is cached in place, and in the reference that cache is the record itself.</b>
/// <c>RunGlobalScript</c> compiles the source, writes the bytecode <i>back over</i> the entry and
/// flips its flag to <c>SPECAB_BINARYCODE</c> — or to <c>SPECAB_SCRIPTERROR</c> with the error
/// text in place of the source, so a script that fails to compile is never retried and its source
/// is gone. This port caches beside the ability rather than overwriting it, which keeps the source
/// available for a second look; the observable behaviour — compile once, fail once — is the same.
/// </para>
/// <para>
/// <b>The result lands in hook parameter 0 and is returned from there.</b> That slot is both the
/// return value and an ordinary parameter slot, so a script can read what a previous one left.
/// </para>
/// </remarks>
public sealed class GlobalScripts
{
    private readonly IReadOnlyList<SpecialAbility> abilities;
    private readonly Dictionary<(string Ability, string Script), GpdlProgram?> compiled = [];

    public GlobalScripts(IReadOnlyList<SpecialAbility> abilities)
    {
        this.abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
    }

    /// <summary>What a script's source is wrapped in before compiling (<c>Specab.cpp:1776</c>).</summary>
    public const string FrontEnd = "$PUBLIC $FUNC SA(){";

    /// <summary>The tail of that wrapper. The newline matters: a trailing <c>//</c> would eat it.</summary>
    public const string BackEnd = "\n} SA ;";

    /// <summary>The function the wrapper declares, and therefore the entry point to execute.</summary>
    public const string EntryPoint = "SA";

    /// <summary>
    /// The built-in scripts, used when the design defines no ability of that name
    /// (<c>defaultGlobalScripts</c>, <c>Specab.cpp:2081</c>).
    /// </summary>
    /// <remarks>
    /// <b>There is exactly one.</b> Not a table that grew — a single entry, combat placement's
    /// far-monster program. Everything else a hook might ask for has no default at all, so
    /// <c>TeleporterDestinations</c> and the <c>WHO_TRIES</c> <c>Attempt</c> veto exist only in
    /// designs that author them.
    /// </remarks>
    public static readonly IReadOnlyList<(string Ability, string Script, string Source)> Defaults =
    [
        ("CombatPlacement", "PlaceMonsterFar",
         "$IF($GET_PARTY_FACING() >=#2)"
         + "{"
         + "$MonsterPlacement(\"17FbPV500E\");"
         + "}"
         + "$ELSE"
         + "{"
         + "$MonsterPlacement(\"16FbPV500E\");"
         + "};"),
    ];

    /// <summary>Whether a script of this name exists, from the design or the defaults.</summary>
    public bool Has(string ability, string script) => Source(ability, script) is not null;

    /// <summary>
    /// The GPDL source for a script, or null.
    /// </summary>
    /// <remarks>
    /// <b>The defaults are consulted only when the design defines no ability of that name at
    /// all</b> — not per script. An ability that exists but lacks the named script falls through
    /// to nothing, because the reference looks the defaults up in the <c>pSpecAb == NULL</c> branch
    /// alone. So a design that defines <c>CombatPlacement</c> without <c>PlaceMonsterFar</c> loses
    /// the built-in rather than inheriting it.
    /// </remarks>
    public string? Source(string ability, string script)
    {
        var found = abilities.FirstOrDefault(
            a => string.Equals(a.Name, ability, StringComparison.Ordinal));

        if (found is not null)
        {
            return found.Script(script);
        }

        foreach (var (name, scriptName, source) in Defaults)
        {
            if (string.Equals(name, ability, StringComparison.Ordinal)
                && string.Equals(scriptName, script, StringComparison.Ordinal))
            {
                return source;
            }
        }

        return null;
    }

    /// <summary>
    /// Compiles a script, caching both success and failure.
    /// </summary>
    /// <returns>The program, or null when there is no such script or it did not compile.</returns>
    /// <remarks>
    /// <b>A compile failure is cached as a failure and never retried</b>, matching the reference's
    /// <c>SPECAB_SCRIPTERROR</c> flag — a design with a broken script pays the error once, not once
    /// per invocation.
    /// </remarks>
    public GpdlProgram? Compile(string ability, string script)
    {
        var key = (ability, script);
        if (compiled.TryGetValue(key, out var cached))
        {
            return cached;
        }

        GpdlProgram? program = null;

        if (Source(ability, script) is { } source)
        {
            var compiler = new GpdlCompiler();
            if (compiler.Compile(FrontEnd + source + BackEnd, compilingScript: true) == 0)
            {
                program = GpdlProgram.FromCompiler(compiler);
            }
            else
            {
                LastErrors = [.. compiler.Errors];
            }
        }

        compiled[key] = program;
        return program;
    }

    /// <summary>The compiler's complaints about the most recent failure, for a caller to report.</summary>
    public IReadOnlyList<string> LastErrors { get; private set; } = [];

    /// <summary>
    /// Runs a script and returns what it left in hook parameter 0.
    /// </summary>
    /// <param name="host">
    /// The host the script talks to. Its hook-parameter block is what the script reads and writes,
    /// so a caller wanting the reference's stacking must push a fresh block before calling.
    /// </param>
    /// <remarks>
    /// <b>A script that does not exist is not an error.</b> The reference's <c>else</c> arm runs
    /// its callbacks and returns whatever is already in slot 0 — so a missing hook yields the
    /// caller's own default rather than throwing, which is exactly how a design that overrides
    /// nothing keeps working.
    /// </remarks>
    public string Run(string ability, string script, GpdlUnhostedEnvironment host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (Compile(ability, script) is not { } program)
        {
            return host.GetHookParam(GpdlHookParameters.ResultSlot);
        }

        var vm = new GpdlVirtualMachine(program, host);
        string result = vm.Execute(EntryPoint);

        host.SetHookParam(GpdlHookParameters.ResultSlot, result);
        return result;
    }
}
