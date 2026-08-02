using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// What a branch of a trial event does when it is taken
/// (<c>passwordActionType</c>, <c>GameEvent.h:327</c>).
/// </summary>
/// <remarks>
/// Shared with <c>PASSWORD_DATA</c>, which is where the type gets its name — the two events were
/// written from one another and carry the same success/fail tail.
/// </remarks>
public enum TrialAction
{
    /// <summary>Follow the event's ordinary chain (<c>ChainHappened</c>, <c>RunEvent.cpp:855</c>).</summary>
    NoAction = 0,

    /// <summary>Go to the branch's own event (<c>ChainOrQuit</c>, <c>RunEvent.cpp:931</c>).</summary>
    ChainEvent = 1,

    /// <summary>Move the party to the branch's <c>TRANSFER_DATA</c> destination.</summary>
    Teleport = 2,

    /// <summary>Step the party back one square, then follow the ordinary chain.</summary>
    BackupOneStep = 3,
}

/// <summary>
/// The six ability scores a <c>WHO_TRIES_EVENT_DATA</c> can check, in the order they are
/// serialized — the same order as <see cref="TrialEventReaders.AbilityNames"/>.
/// </summary>
public enum Ability
{
    Strength = 0,
    Intelligence = 1,
    Wisdom = 2,
    Dexterity = 3,
    Constitution = 4,
    Charisma = 5,
}

/// <summary>What one press of Return at the prompt produced.</summary>
/// <param name="Succeeded">Whether the character passed. The reference tracks the negation.</param>
/// <param name="Tries">
/// The attempt count after this press (<c>currTry</c>), to be handed back on the next one.
/// </param>
/// <param name="Retry">
/// True when the reference leaves the event sitting on its <i>opening</i> prompt for another
/// press instead of moving to the failure screen. See the remarks on
/// <see cref="EventWhoTries.Attempt"/> — it is silent, and it is unreachable in a modern design.
/// </param>
public readonly record struct WhoTriesAttempt(bool Succeeded, int Tries, bool Retry);

/// <summary>What a settled WhoTries event does next.</summary>
/// <param name="Succeeded">Which branch this is.</param>
/// <param name="Action">The branch's <c>passwordActionType</c>, raw — see the remarks.</param>
/// <param name="Chains">
/// True when the run continues down the event's ordinary chain, as <c>ChainHappened</c> does.
/// </param>
/// <param name="GoTo">The event to run instead, or null.</param>
/// <param name="Destination">The teleport destination, or null when this is not a teleport.</param>
/// <remarks>
/// <b>All three of <see cref="Chains"/>, <see cref="GoTo"/> and <see cref="Destination"/> can be
/// empty at once.</b> That is not a mistake — the reference's two <c>switch</c>es have no
/// <c>default</c> (<c>RunEvent.cpp:12299</c> and <c>:12321</c>), so an action outside the four
/// named values does nothing whatsoever: the event is neither replaced nor popped and the player
/// is left pressing Return at a screen that will not advance. Only a corrupt or hand-edited design
/// reaches it, but a caller that treats "no goto" as "follow the chain" would quietly repair a hang
/// the reference really has.
/// </remarks>
public readonly record struct WhoTriesOutcome(bool Succeeded, TrialAction Action, bool Chains,
                                              uint? GoTo, TransferData? Destination);

/// <summary>
/// Runs a <c>WHO_TRIES_EVENT_DATA</c> (<c>WHO_TRIES_EVENT_DATA::OnKeypress</c>,
/// <c>RunEvent.cpp:12170</c>) — one chosen party member attempts an ability check.
/// </summary>
/// <remarks>
/// <para>
/// The event shows its text, the player picks a character with the party keys and presses Return,
/// and the character either passes or does not. Success and failure each get their own screen
/// (event text 2 and 3) and their own branch, exactly as <c>PASSWORD_DATA</c> does.
/// </para>
/// <para>
/// <b>Nearly all of the check is dead, and the reason is in the writer rather than the runtime.</b>
/// The storing branch of <c>Serialize(CAR&amp;)</c> writes a literal <c>FALSE</c> for all eight
/// thief-skill flags <i>and</i> for <c>compareToDie</c>, and a literal <c>0</c> for
/// <c>compareDie</c> (<c>GameEvent.cpp:9076</c>–<c>:9086</c>). Three consequences follow, and they
/// are the whole character of this event in any design saved by a modern build:
/// </para>
/// <list type="number">
///   <item>
///   <b>The target number is always zero.</b> <c>MinSuccessVal</c> is
///   <c>max(0, compareDie)</c> whenever <c>compareToDie</c> is false (<c>:12199</c>), so every one
///   of the six score comparisons is <c>score &lt; 0</c> — which no undrained character fails. The
///   ability checks are live code that cannot fail.
///   </item>
///   <item>
///   <b>The only failure a check can produce is the strength percentile</b> at <c>:12205</c>:
///   <c>GetAdjStrMod() &lt; strBonus</c>. <c>strBonus</c> survives the flattening, so a design
///   ticking STR and setting a percentile is asking for 18/xx or better and nothing else.
///   </item>
///   <item>
///   <b><c>NbrTries</c> is dead too.</b> The retry test is
///   <c>(currTry &gt;= NbrTries) || (!compareToDie)</c> (<c>:12282</c>) and the second disjunct is
///   always true, so every failure is final on the first press. The field is still read, still
///   editable, and cannot do anything.
///   </item>
/// </list>
/// <para>
/// The eight thief skills are dead twice over: unwritable as above, and their runtime block is
/// commented out (<c>:12223</c>–<c>:12247</c>). <c>CheckOldSkills</c>
/// (<c>GameEvent.cpp:8969</c>) exists to migrate a design old enough to have them set into event
/// attributes named <c>PP</c>, <c>OL</c> and so on — but it is <c>UAFEDITOR</c>-only and nothing at
/// runtime reads those attributes back, so even the migration path leads nowhere. They are not
/// ported. <see cref="WhoTriesEvent.ThiefSkillChecks"/> is read off disk and ignored here, as the
/// engine ignores it.
/// </para>
/// <para>
/// <b>Not ported: the <c>Attempt</c> script hook</b> (<c>:12248</c>–<c>:12275</c>). An event
/// attribute named <c>Attempt</c> lists global scripts, each run through
/// <c>RunGlobalScript("$EVENT_WhoTries_Attempt", …)</c> with the character as context; if hook
/// parameter 0 comes back as exactly <c>"N"</c> the success is vetoed. The port has no
/// <c>HOOK_PARAMETERS</c> block and no run-a-global-script-by-name bridge, so there is nothing to
/// call into and this is state the port does not have rather than something approximated. Note
/// two edges for whoever wires it later: the hook only runs when the checks have <i>already</i>
/// passed, so it can veto a success but never rescue a failure; and parameter 0 is read once
/// <i>after</i> the whole list has run, so a later script's silence does not clear an earlier
/// script's veto — but a later script writing anything else does.
/// </para>
/// <para>
/// <b>No shipped design contains one.</b> <c>WhoTries</c> is one of the eleven event types that
/// appear zero times across the six level-bearing designs in the corpus, so nothing here is
/// confirmed against observed data — see <c>EventTypesAbsentFromCorpusTests</c>.
/// </para>
/// </remarks>
public static class EventWhoTries
{
    /// <summary>
    /// The spell-effect keyword each ability is adjusted through, from the engine's own keyword
    /// table (<c>CHARACTER_IF</c>, <c>RunTimeIF.cpp:144</c>).
    /// </summary>
    private static readonly string[] EffectKeys =
        ["$CHAR_STR", "$CHAR_INT", "$CHAR_WIS", "$CHAR_DEX", "$CHAR_CON", "$CHAR_CHA"];

    /// <summary>The five abilities checked after strength, in the reference's order.</summary>
    private static readonly Ability[] AfterStrength =
        [Ability.Intelligence, Ability.Wisdom, Ability.Dexterity, Ability.Constitution,
         Ability.Charisma];

    /// <summary>Whether the event asks for this ability.</summary>
    /// <remarks>
    /// A list shorter than six — which the reader never produces, but a hand-built event might —
    /// reads as unchecked rather than throwing.
    /// </remarks>
    public static bool Checks(WhoTriesEvent trial, Ability ability)
    {
        ArgumentNullException.ThrowIfNull(trial);

        int index = (int)ability;
        return index >= 0 && index < trial.AbilityChecks.Count && trial.AbilityChecks[index] != 0;
    }

    /// <summary>
    /// A character's ability score with spell effects applied
    /// (<c>GetAdjStr</c> and its five siblings, <c>Char.cpp:13615</c>).
    /// </summary>
    /// <remarks>
    /// <b>Unclamped</b>, like the <c>GetAdj*</c> accessors themselves — it is <c>GetLimitedStr</c>
    /// that bounds the result, and the check does not call it. So a drain effect really can push a
    /// score below zero, which is the one way the otherwise-dead score comparisons in
    /// <see cref="Attempt"/> can bite.
    /// </remarks>
    public static int Adjusted(Character who, Ability ability)
    {
        ArgumentNullException.ThrowIfNull(who);

        int index = (int)ability;
        if (index < 0 || index >= EffectKeys.Length)
        {
            return 0;
        }

        var scores = who.Record.Abilities;
        int permanent = ability switch
        {
            Ability.Strength => scores.Strength,
            Ability.Intelligence => scores.Intelligence,
            Ability.Wisdom => scores.Wisdom,
            Ability.Dexterity => scores.Dexterity,
            Ability.Constitution => scores.Constitution,
            _ => scores.Charisma,
        };

        return (int)who.Effects.Apply((double)permanent, EffectKeys[index]);
    }

    /// <summary>
    /// A character's exceptional-strength percentile with spell effects applied
    /// (<c>GetAdjStrMod</c>, <c>Char.cpp:13648</c>).
    /// </summary>
    /// <remarks>
    /// This is the percentile itself — 0 for anyone who is not 18/xx — and not the bonuses
    /// <see cref="UAF.Rules.Strength"/> derives from it. The check compares it against the event's
    /// <c>strBonus</c> directly, so nothing in the strength table is consulted.
    /// </remarks>
    public static int AdjustedStrengthMod(Character who)
    {
        ArgumentNullException.ThrowIfNull(who);

        return (int)who.Effects.Apply((double)who.Record.Abilities.StrengthMod, "$CHAR_STRMOD");
    }

    /// <summary>
    /// The number a score must reach (<c>MinSuccessVal</c>, <c>RunEvent.cpp:12195</c>).
    /// </summary>
    /// <param name="dice">
    /// Rolls one die of the given number of sides, 1..sides. Injected so a test can pin the
    /// outcome; the original calls <c>RollDice(compareDie, 1, 0)</c>.
    /// </param>
    /// <remarks>
    /// <b>Zero in every design a modern build saved</b>, because <c>compareToDie</c> is written as
    /// <c>FALSE</c> and <c>compareDie</c> as <c>0</c> (<c>GameEvent.cpp:9085</c>). The die path is
    /// kept because an old design read through the <c>CArchive</c> serializer
    /// (<c>GameEvent.cpp:8992</c>) can still carry real values, and because it is what
    /// <c>NbrTries</c> depends on. A negative <c>compareDie</c> floors at zero on the literal path
    /// and rolls nothing at all on the die path — <c>RollDice</c> returns the bonus, here 0, when
    /// the sides are not positive.
    /// </remarks>
    public static int TargetNumber(WhoTriesEvent trial, Func<int, int> dice)
    {
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(dice);

        return trial.CompareToDie != 0
            ? DiceExpression.Roll(1, trial.CompareDie, dice)
            : Math.Max(0, trial.CompareDie);
    }

    /// <summary>
    /// One press of Return at the prompt: the chosen character attempts the check
    /// (<c>TASK_WhoTriesGet</c>, <c>RunEvent.cpp:12181</c>).
    /// </summary>
    /// <param name="who">
    /// The character the player has selected. Choosing one is the presenter's job — the reference
    /// reads it back from the party with <c>GetActiveChar(this)</c> at <c>:12194</c>.
    /// </param>
    /// <param name="dice">Supplies the target roll — see <see cref="TargetNumber"/>.</param>
    /// <param name="tries">
    /// <c>currTry</c> so far, zero on the first press. The reference keeps it on the event and
    /// resets it only when the event is first entered (<c>:12153</c>); this class is pure, so the
    /// count lives with the caller.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>An event with both flags set always fails.</b> <c>alwaysFails</c> is tested first
    /// (<c>:12182</c>) and <c>alwaysSucceeds</c> is only reached in its <c>else</c>. Nothing stops
    /// an editor writing both.
    /// </para>
    /// <para>
    /// <b>An event with no flags and no ticked abilities always succeeds.</b> <c>failed</c> starts
    /// false at <c>:12178</c> and only the checks can set it, so a WhoTries with nothing configured
    /// is a free pass rather than an automatic failure.
    /// </para>
    /// <para>
    /// <b>Strength is the only ability with two ways to fail</b>, and its second is an
    /// <c>else if</c> (<c>:12205</c>): the percentile is consulted only when the score itself
    /// passed. Nothing observable turns on that — both are pure reads and <c>failed</c> is already
    /// true — but it is why the two are nested rather than listed with the other five, which are
    /// independent <c>if</c>s that can only ever set the flag.
    /// </para>
    /// <para>
    /// <b>A retry is silent.</b> When a failure is not final the reference calls
    /// <c>OnInitialEvent</c> with the state still at <c>TASK_WhoTriesGet</c> (<c>:12284</c>), which
    /// falls to the default arm and redraws the <i>opening</i> text and picture. The player is
    /// given no indication that anything failed — the prompt simply reappears. It is also
    /// unreachable in a modern design, since <c>!compareToDie</c> makes every failure final.
    /// </para>
    /// <para>
    /// <b><c>alwaysFails</c> overwrites the try count rather than incrementing it</b>
    /// (<c>currTry = NbrTries</c>, <c>:12185</c>). That is deliberate: it is what stops an
    /// always-failing event with a die comparison from making the player press Return
    /// <c>NbrTries</c> times to be told what it knew at the first press. The increment at
    /// <c>:12277</c> then leaves the count one <i>above</i> <c>NbrTries</c>, which no later code
    /// reads.
    /// </para>
    /// </remarks>
    public static WhoTriesAttempt Attempt(WhoTriesEvent trial, Character who, Func<int, int> dice,
                                          int tries = 0)
    {
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(who);
        ArgumentNullException.ThrowIfNull(dice);

        bool failed = false;

        if (trial.AlwaysFails != 0)
        {
            failed = true;
            tries = trial.NbrTries;
        }
        else if (trial.AlwaysSucceeds != 0)
        {
            failed = false;
        }
        else
        {
            int minimum = TargetNumber(trial, dice);

            if (Checks(trial, Ability.Strength))
            {
                if (Adjusted(who, Ability.Strength) < minimum)
                {
                    failed = true;
                }
                else if (AdjustedStrengthMod(who) < trial.StrengthBonus)
                {
                    failed = true;
                }
            }

            foreach (var ability in AfterStrength)
            {
                if (Checks(trial, ability) && Adjusted(who, ability) < minimum)
                {
                    failed = true;
                }
            }

            // The eight thief-skill comparisons would sit here. They are commented out in the
            // reference and unwritable by the editor -- see the remarks on the class.
        }

        tries++;

        bool retry = failed && tries < trial.NbrTries && trial.CompareToDie != 0;
        return new WhoTriesAttempt(!failed, tries, retry);
    }

    /// <summary>
    /// The branch taken when the player presses Return on the success or failure screen
    /// (<c>RunEvent.cpp:12295</c> and <c>:12318</c>).
    /// </summary>
    /// <param name="isValidEvent">Whether an id names an event this level holds.</param>
    /// <remarks>
    /// <para>
    /// <b>An unreachable chain is <i>not</i> a stop here, unlike <see cref="Quests"/>.</b> Both
    /// branches go through <c>ChainOrQuit</c> (<c>RunEvent.cpp:931</c>), which falls back on
    /// <c>ChainHappened</c> for a chain of zero <i>and</i> for one naming an event the level does
    /// not contain. <c>QUEST_EVENT_DATA</c> pushes a do-nothing event in the same situation and
    /// ends the run, and only its two automatic operations fall back
    /// (<c>RunEvent.cpp:13547</c>). So the rule genuinely differs between the two events and
    /// cannot be assumed from one to the other: a WhoTries pointed at a deleted event carries on
    /// down its ordinary chain.
    /// </para>
    /// <para>
    /// <b>The two branches are otherwise identical</b> — the same four-arm <c>switch</c> written
    /// out twice against the success or fail field. The only asymmetry between them is in the
    /// presentation: the failure screen binds a menu hook (<c>this, "WhoTries"</c>,
    /// <c>:12133</c>) and the success screen passes nulls (<c>:12142</c>), so a design can hang a
    /// menu script off a failure and not off a success.
    /// </para>
    /// <para>
    /// <b>A teleport pops the event and follows no chain.</b> <c>HandleTransfer</c> ends with
    /// <c>PopEvent()</c> (<c>RunEvent.cpp:1079</c>) after moving the party, and re-inserts the
    /// destination's own <c>BEGIN_XY</c> event only when the transfer's <c>execEvent</c> flag is
    /// set. Its <c>bool</c> return is ignored by both branches — the reference's own comment on it
    /// is that it means nothing.
    /// </para>
    /// <para>
    /// <b><see cref="TrialAction.BackupOneStep"/> chains as well as stepping back</b>: it sends
    /// <c>TASKMSG_MovePartyBackward</c> and <i>then</i> calls <c>ChainHappened</c>
    /// (<c>:12311</c>), so it is <see cref="TrialAction.NoAction"/> with a step attached rather
    /// than an alternative to it.
    /// </para>
    /// </remarks>
    public static WhoTriesOutcome Resolve(WhoTriesEvent trial, bool succeeded,
                                          Func<uint, bool> isValidEvent)
    {
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(isValidEvent);

        var action = (TrialAction)(succeeded ? trial.SuccessAction : trial.FailAction);
        uint chain = succeeded ? trial.SuccessChain : trial.FailChain;
        var transfer = succeeded ? trial.SuccessTransfer : trial.FailTransfer;

        return action switch
        {
            TrialAction.NoAction or TrialAction.BackupOneStep =>
                new WhoTriesOutcome(succeeded, action, Chains: true, null, null),

            // ChainOrQuit: a chain of zero, or one naming an event the level does not hold, is the
            // ordinary chain rather than the end of the run.
            TrialAction.ChainEvent when chain > 0 && isValidEvent(chain) =>
                new WhoTriesOutcome(succeeded, action, Chains: false, chain, null),

            TrialAction.ChainEvent =>
                new WhoTriesOutcome(succeeded, action, Chains: true, null, null),

            TrialAction.Teleport =>
                new WhoTriesOutcome(succeeded, action, Chains: false, null, transfer),

            // No default arm in either reference switch: the event stays where it is.
            _ => new WhoTriesOutcome(succeeded, action, Chains: false, null, null),
        };
    }
}
