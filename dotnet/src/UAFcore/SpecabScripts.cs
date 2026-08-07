using UAF.Scripting;
using UAF.Serialization;

namespace UAFcore;

/// <summary>Why the walk is calling a callback (<c>CBFUNC</c>).</summary>
public enum ScriptCallbackKind
{
    /// <summary>A script has just run and left a result.</summary>
    ExamineScript,

    /// <summary>Every script has run and none stopped the walk.</summary>
    EndOfScripts,

    /// <summary>There were no scripts at all.</summary>
    Default,
}

/// <summary>What a callback wants the walk to do next (<c>CBRESULT</c>).</summary>
public enum ScriptCallbackResult
{
    Continue,
    Stop,
}

/// <summary>
/// Examines a script's result and says whether to keep going. The result is passed by reference
/// because a callback may rewrite it — <see cref="ScriptCallbacks.LookForChar"/> trims it to one
/// character and <c>CBF_ENDOFSCRIPTS</c> blanks it.
/// </summary>
public delegate ScriptCallbackResult ScriptCallback(ScriptCallbackKind kind, ref string result);

/// <summary>
/// The callbacks a hook chooses between (<c>Specab.cpp:1678</c> onwards).
/// </summary>
/// <remarks>
/// <b>They are what makes two hooks with identical plumbing behave differently.</b> The walk is
/// the same for all of them; the callback decides whether one script's answer ends the search,
/// whether the answer is trimmed, and what an exhausted search leaves behind.
/// </remarks>
public static class ScriptCallbacks
{
    /// <summary>
    /// <c>ScriptCallback_RunAllScripts</c> (<c>Specab.cpp:1678</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Its body is dead code.</b> The function opens with an unconditional
    /// <c>return CBR_CONTINUE;</c> and everything after it — a Y/N accumulator, and an
    /// <c>ENDOFSCRIPTS</c> arm that would blank the result and stop — is unreachable. Kept as a
    /// separate callback rather than folded away because the reference still names it at fifteen
    /// call sites, and the name is what a reader will look for.
    /// </para>
    /// <para>
    /// <b>What that leaves is "every script runs and the last one wins".</b> Nothing stops the
    /// walk, nothing rewrites the result, and — because <c>ENDOFSCRIPTS</c> never blanks it — a
    /// hook with no scripts at all comes back with whatever the caller left in slot 0. That is the
    /// mechanism behind <see cref="FixSpells.WantsFixing"/> being the engine's real answer rather
    /// than a stand-in.
    /// </para>
    /// </remarks>
    public static ScriptCallbackResult RunAll(ScriptCallbackKind kind, ref string result) =>
        ScriptCallbackResult.Continue;

    /// <summary>
    /// <c>ScriptCallback_LookForChar</c> (<c>Specab.cpp:1683</c>) — stop at the first script whose
    /// answer contains one of <paramref name="wanted"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The result is trimmed to the single character found</b>, so a script answering
    /// "NO, THE WARD HOLDS" comes back as "N" and the caller's <c>result[0] == 'N'</c> test works
    /// on it. <c>DOES_SPELL_ATTACK_SUCCEED</c> passes <c>"YN"</c>.
    /// </para>
    /// <para>
    /// <b>An exhausted search blanks the result</b>, where <see cref="RunAll"/> leaves it — which
    /// is the whole difference between the two, and what lets a caller chain to the next source
    /// on an empty answer.
    /// </para>
    /// <para>
    /// <b>The search is <c>FindOneOf</c>, not a prefix test</b>, so the character may be anywhere
    /// in the answer and the first one in <i>the answer</i> wins, not the first in
    /// <paramref name="wanted"/>.
    /// </para>
    /// </remarks>
    public static ScriptCallback LookForChar(string wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        return (ScriptCallbackKind kind, ref string result) =>
        {
            switch (kind)
            {
                case ScriptCallbackKind.ExamineScript:
                    int index = result.IndexOfAny(wanted.ToCharArray());
                    if (index < 0)
                    {
                        return ScriptCallbackResult.Continue;
                    }

                    result = result.Substring(index, 1);
                    return ScriptCallbackResult.Stop;

                case ScriptCallbackKind.EndOfScripts:
                    result = string.Empty;
                    return ScriptCallbackResult.Stop;

                default:
                    return ScriptCallbackResult.Continue;
            }
        };
    }
}

/// <summary>
/// Running the scripts a record's own special abilities carry
/// (<c>SPECIAL_ABILITIES::RunScripts</c>, <c>Specab.cpp:1876</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the shape every named hook in the engine goes through.</b> A record — a character, a
/// spell, a race, a monster, an item — carries a list of ability names; each named ability may
/// define a script under a hook's name; the walk collects the ones that do, runs them in order,
/// and hands each answer to a callback.
/// </para>
/// <para>
/// <b>The answer lives in hook parameter 0 and is both input and output.</b> The caller seeds it
/// before calling and reads it after; each script that runs overwrites it. A walk that runs no
/// scripts leaves the seed untouched, which is how a design that overrides nothing keeps the
/// engine's own defaults.
/// </para>
/// <para>
/// <b>An ability the design does not define is skipped, silently</b> — as is one that defines no
/// script under this name. Neither is an error, and a record listing ten abilities of which one
/// carries the hook runs exactly one script.
/// </para>
/// </remarks>
public static class SpecabScripts
{
    /// <summary>
    /// How many scripts one walk will collect (<c>MAX_SPEC_AB</c>).
    /// </summary>
    /// <remarks>
    /// <b>Abilities past the limit are skipped rather than the walk being cut short</b> — the
    /// reference's <c>continue</c> keeps scanning, so which ones are dropped depends on the order
    /// the record lists them.
    /// </remarks>
    public const int MaxScripts = 20;

    /// <summary>
    /// Runs a record's scripts for one hook.
    /// </summary>
    /// <param name="abilities">The record's own special-ability names, in its own order.</param>
    /// <param name="scriptName">The hook, e.g. <c>FIX_CHARACTER</c>.</param>
    /// <param name="scripts">Compiles and runs one named script; the design's ability table.</param>
    /// <param name="host">
    /// What the scripts talk to. Its hook-parameter block carries the answer in and out.
    /// </param>
    /// <param name="callback">
    /// What to do with each answer. <see cref="ScriptCallbacks.RunAll"/> for a hook that wants the
    /// last word, <see cref="ScriptCallbacks.LookForChar"/> for one that wants the first match.
    /// </param>
    /// <returns>Hook parameter 0 as the walk left it.</returns>
    public static string Run(IReadOnlyList<SpecabPair> abilities, string scriptName,
                             GlobalScripts scripts, GpdlUnhostedEnvironment host,
                             ScriptCallback callback)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        ArgumentNullException.ThrowIfNull(scriptName);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(callback);

        // The pair, not just the name: the ability's own value is its parameter, and a script
        // reads it with $SA_PARAM_GET().
        var found = new List<(string Name, string Parameter)>();

        foreach (var ability in abilities)
        {
            if (found.Count >= MaxScripts)
            {
                continue;                       // skipped, not a break -- see MaxScripts
            }

            if (scripts.Has(ability.Key, scriptName))
            {
                found.Add((ability.Key, ability.Value));
            }
        }

        string result = host.GetHookParam(GpdlHookParameters.ResultSlot);

        if (found.Count == 0)
        {
            callback(ScriptCallbackKind.Default, ref result);
            host.SetHookParam(GpdlHookParameters.ResultSlot, result);
            return result;
        }

        foreach (var (ability, parameter) in found)
        {
            // The reference sets the pair on the context before each script
            // (SPECIAL_ABILITIES::RunScripts, Specab.cpp:1929), which is what $SA_NAME() and
            // $SA_PARAM_GET() read -- so a script can tell which of a record's abilities is
            // running it, and what that ability was configured with.
            host.Context.SetAbility(ability, parameter);

            result = scripts.Run(ability, scriptName, host);

            if (callback(ScriptCallbackKind.ExamineScript, ref result)
                == ScriptCallbackResult.Stop)
            {
                host.SetHookParam(GpdlHookParameters.ResultSlot, result);
                return result;
            }
        }

        callback(ScriptCallbackKind.EndOfScripts, ref result);
        host.SetHookParam(GpdlHookParameters.ResultSlot, result);
        return result;
    }

    /// <summary>
    /// <inheritdoc cref="Run(IReadOnlyList{SpecabPair}, string, GlobalScripts, GpdlUnhostedEnvironment, ScriptCallback)"/>
    /// </summary>
    /// <param name="block">The record's whole special-abilities block.</param>
    public static string Run(SpecabBlock? block, string scriptName, GlobalScripts scripts,
                             GpdlUnhostedEnvironment host, ScriptCallback callback) =>
        Run(block?.Pairs ?? [], scriptName, scripts, host, callback);
}
