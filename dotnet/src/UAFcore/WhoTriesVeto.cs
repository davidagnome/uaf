using UAF.Common;
using UAF.Scripting;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The design's veto over a successful <c>WHO_TRIES</c> attempt
/// (<c>WHO_TRIES_EVENT_DATA::OnKeypress</c>, <c>UAFWin/RunEvent.cpp:12248</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It can only take a success away, never give one.</b> The whole block sits inside
/// <c>if (!failed)</c>, so a check the character already failed never reaches a script. That makes
/// this a veto rather than an override, and it is the reason a design cannot use it to implement
/// an ability the engine does not know about.
/// </para>
/// <para>
/// <b>The hook is named by the <i>event</i>, not the design.</b> The event's own ASL carries an
/// <c>Attempt</c> entry listing which scripts to run; the ability they live in is always
/// <c>$EVENT_WhoTries_Attempt</c>. So two <c>WHO_TRIES</c> events in one design can run different
/// subsets of the same script library.
/// </para>
/// <para>
/// <b>All the scripts share one hook-parameter block, and slot 0 is read once at the end.</b> The
/// reference constructs its <c>HOOK_PARAMETERS</c> outside the loop and tests it after — so a
/// later script writing anything other than <c>"N"</c> into slot 0 <i>clears an earlier script's
/// veto</i>. The scripts are not independent votes; the last writer wins.
/// </para>
/// </remarks>
public static class WhoTriesVeto
{
    /// <summary>The ability every attempt script lives in.</summary>
    public const string AbilityName = "$EVENT_WhoTries_Attempt";

    /// <summary>The event ASL entry naming which scripts to run.</summary>
    public const string AttributeName = "Attempt";

    /// <summary>The answer in slot 0 that vetoes the attempt.</summary>
    public const string VetoAnswer = "N";

    /// <summary>Where the ability name a script is judging is handed to it.</summary>
    public const int AbilitySlot = 5;

    /// <summary>Where the requirement is handed to it.</summary>
    public const int NeedSlot = 6;

    /// <summary>
    /// Runs whatever the event asks for and says whether the success is vetoed.
    /// </summary>
    /// <param name="succeeded">
    /// Whether the checks passed. <b>False short-circuits</b> — no script runs at all.
    /// </param>
    /// <returns>True when the attempt should now be treated as a failure.</returns>
    /// <remarks>
    /// <b>Each field of the <c>Attempt</c> value is itself a list</b>: a script name, then the two
    /// values handed to the script in slots 5 and 6. The value is read with the self-delimiting
    /// convention (<see cref="Substrings"/>), so an outer delimiter separates the fields and an
    /// inner one separates each field's three parts — and the reference strips the inner
    /// delimiter off the third part by hand, which is what
    /// <c>hookParameters[6].Right(len - 1)</c> is doing.
    /// </remarks>
    public static bool Vetoes(WhoTriesEvent trial, bool succeeded, GlobalScripts scripts,
                              GpdlUnhostedEnvironment host)
    {
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(host);

        if (!succeeded)
        {
            return false;
        }

        var entry = trial.Base.Attributes.FirstOrDefault(
            a => string.Equals(a.Key, AttributeName, StringComparison.Ordinal));

        if (entry is null)
        {
            return false;
        }

        // One block for every script, so the last writer of slot 0 decides.
        host.SetHookParam(GpdlHookParameters.ResultSlot, string.Empty);

        foreach (string field in Substrings.Fields(entry.Value))
        {
            if (!Substrings.HeadAndTail(field, out string scriptName, out string rest))
            {
                continue;
            }

            Substrings.HeadAndTail(rest, out string ability, out string need);

            // The tail keeps its leading delimiter and the reference drops it by hand.
            host.SetHookParam(AbilitySlot, ability);
            host.SetHookParam(NeedSlot, need.Length > 0 ? need[1..] : string.Empty);

            scripts.Run(AbilityName, scriptName, host);
        }

        string answer = host.GetHookParam(GpdlHookParameters.ResultSlot);
        return answer.Length > 0 && string.Equals(answer, VetoAnswer, StringComparison.Ordinal);
    }
}
