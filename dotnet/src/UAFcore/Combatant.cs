namespace UAFcore;

/// <summary>
/// A character's condition (<c>charStatusType</c>, <c>GameRules.h:153</c>).
/// </summary>
/// <remarks>
/// The numbering is serialized into save games, so it is transcribed rather than tidied.
/// <see cref="Unconscious"/> and <see cref="Dying"/> are different: the first is below one hit
/// point and stable, the second is below one and losing another every round.
/// </remarks>
public enum CharacterStatus
{
    Okay = 0,
    Unconscious = 1,
    Dead = 2,
    Fled = 3,
    Petrified = 4,
    Gone = 5,
    Animated = 6,
    TempGone = 7,
    Running = 8,
    Dying = 9,
}

/// <summary>
/// One participant in a fight (<c>COMBATANT</c>, <c>Combatant.h:103</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A slice of the original, not the whole of it.</b> <c>Combatant.h</c> declares some 90 members
/// and most of them forward to the underlying <c>CHARACTER</c>; what is here is what the round
/// clock and placement need — identity, position, the turn's resources, and the predicates that
/// decide whether this combatant still has something to do. Spell casting, animation, the
/// targeting queue and the auto-combat "thinking" are not ported.
/// </para>
/// <para>
/// The combatant does not own its position on the grid: <see cref="CombatMap"/> holds occupancy and
/// this holds the coordinate, exactly as the original splits them. Moving means updating both.
/// </para>
/// </remarks>
public sealed class Combatant
{
    /// <param name="index">
    /// Its place in the combatant list. This is the value written into the grid's occupancy
    /// layer, so it must match the caller's own ordering.
    /// </param>
    public Combatant(int index, bool isFriendly, CombatantIcon icon, string name = "")
    {
        Index = index;
        IsFriendly = isFriendly;
        Icon = icon;
        Name = name;
    }

    /// <summary>Its place in the combatant list, and its id in the grid's occupancy layer.</summary>
    public int Index { get; }

    /// <summary>Party and NPCs; everything else is a monster.</summary>
    public bool IsFriendly { get; }

    /// <summary>The footprint, up to 4×4.</summary>
    public CombatantIcon Icon { get; }

    public string Name { get; }

    /// <summary>Top-left square, or −1 when not on the map.</summary>
    public int X { get; set; } = -1;

    /// <inheritdoc cref="X"/>
    public int Y { get; set; } = -1;

    public CharacterStatus Status { get; set; } = CharacterStatus.Okay;

    /// <summary>What this combatant is doing. Only ever set to a value below 11 — see the enum.</summary>
    public CombatantState State { get; set; } = CombatantState.None;

    /// <summary>Rolled initiative, 1..<see cref="CombatRound.MaxInitiative"/>.</summary>
    public int Initiative { get; set; }

    /// <summary>Squares of movement left this round (<c>m_iMovement</c>).</summary>
    public int Movement { get; set; }

    /// <summary>
    /// Diagonal steps taken this round (<c>m_iNumDiagonalMoves</c>), tracked separately because
    /// diagonals are rationed rather than merely costed.
    /// </summary>
    public int DiagonalMoves { get; set; }

    /// <summary>Attacks left this round (<c>availAttacks</c>). Fractional by design.</summary>
    public double AvailableAttacks { get; set; }

    /// <summary>
    /// Set once this combatant has finished its turn. <see cref="IsDone"/> returns it, and the
    /// round's <c>Advance</c> uses that to decide who acts.
    /// </summary>
    public bool TurnIsDone { get; set; }

    /// <summary>Who this combatant is attacking, or <see cref="CombatMap.NoDude"/>.</summary>
    public int Target { get; set; } = CombatMap.NoDude;

    /// <summary>The last combatant to attack this one (<c>m_iLastAttacker</c>).</summary>
    public int LastAttacker { get; set; } = CombatMap.NoDude;

    /// <summary>The last combatant this one attacked (<c>m_iLastAttacked</c>).</summary>
    public int LastAttacked { get; set; } = CombatMap.NoDude;

    /// <summary>Whether this combatant moved this round (<c>didMove</c>).</summary>
    public bool DidMove { get; set; }

    /// <summary>Whether a turning attempt has already been spent — once per combat, not per round.</summary>
    public bool HasTurnedUndead { get; set; }

    /// <summary>
    /// Whether a script has declared this combatant unable to act
    /// (<c>m_isCombatReady</c>, <c>Combatant.cpp:6969</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// In the reference this is a tri-state: negative means "ask the scripts", and the
    /// <c>IS_COMBAT_READY</c> hook is run against both the character and the combatant, either of
    /// which can veto by returning a non-empty string. Zero means not ready, one means ready.
    /// </para>
    /// <para>
    /// <b>The script half is not wired up</b> — it needs GPDL running global scripts, the same gap
    /// monster placement worked around. This is the settled answer only, defaulting to ready. A
    /// design whose scripts gate readiness (a sleep or hold effect) will have its combatants act
    /// when they should not.
    /// </para>
    /// </remarks>
    public bool IsCombatReady { get; set; } = true;

    /// <summary>
    /// Whether this combatant is on the map and able to be there
    /// (<c>charOnCombatMap</c>, <c>Combatant.cpp:10463</c>).
    /// </summary>
    /// <remarks>
    /// Note that <see cref="CharacterStatus.Animated"/> and <see cref="CharacterStatus.Running"/>
    /// are <i>not</i> excluded — the test names the statuses that keep a combatant off the map
    /// rather than the ones that allow it, so anything unlisted counts as present.
    /// </remarks>
    public bool IsOnCombatMap(bool unconsciousOk = false, bool petrifiedOk = false)
    {
        if (IsUnconscious && !unconsciousOk)
        {
            return false;
        }

        if (Status == CharacterStatus.Petrified && !petrifiedOk)
        {
            return false;
        }

        return Status is not (CharacterStatus.Fled or CharacterStatus.Gone
                              or CharacterStatus.TempGone or CharacterStatus.Dead);
    }

    /// <summary>
    /// Whether this combatant is out of the fight but not dead
    /// (<c>charUnconscious</c>, <c>Combatant.cpp:10483</c>).
    /// </summary>
    /// <remarks><see cref="CharacterStatus.Dying"/> counts, which is why this is not a plain
    /// equality test.</remarks>
    public bool IsUnconscious =>
        Status is CharacterStatus.Dying or CharacterStatus.Unconscious;

    /// <summary>
    /// Whether this combatant has nothing left to do (<c>IsDone</c>, <c>Combatant.cpp:6951</c>).
    /// </summary>
    /// <param name="freeAttack">
    /// True when asking on behalf of an interrupting attack, which needs a target to be worth
    /// taking.
    /// </param>
    /// <remarks>
    /// <para>
    /// The single most load-bearing predicate in the round: <see cref="CombatRound.Advance"/>
    /// calls it for every combatant it considers, so a wrong answer either skips somebody's turn
    /// or hangs the round on a combatant that can never finish.
    /// </para>
    /// <para>
    /// <b>It mutates.</b> Being off the map, or being a free attacker with no target, sets
    /// <see cref="TurnIsDone"/> rather than just returning true — so asking the question changes
    /// the answer to later ones. Reproduced; the round depends on the latch.
    /// </para>
    /// <para>
    /// Petrified short-circuits <i>before</i> the readiness check, so a petrified combatant is
    /// done regardless of what any script would have said.
    /// </para>
    /// </remarks>
    public bool IsDone(bool freeAttack = false)
    {
        if (Status == CharacterStatus.Petrified)
        {
            return true;
        }

        if (!IsCombatReady)
        {
            return true;
        }

        if (!IsOnCombatMap())
        {
            TurnIsDone = true;
        }

        if (freeAttack && Target == CombatMap.NoDude)
        {
            TurnIsDone = true;
        }

        return TurnIsDone;
    }

    /// <summary>
    /// Ends this combatant's turn and hands the queue on
    /// (<c>EndTurn</c>, <c>Combatant.cpp:6877</c>).
    /// </summary>
    /// <param name="queue">The round's turn queue.</param>
    /// <param name="newState">What to leave this combatant doing. Guarding persists; most do not.</param>
    /// <remarks>
    /// <para>
    /// <b>Only acts when this combatant is at the top of the queue.</b> Calling it for anybody else
    /// sets the state and nothing more — the original guards on <c>qcomb.Top() == self</c>, so an
    /// interrupted combatant cannot end a turn it is not currently taking.
    /// </para>
    /// <para>
    /// The latch condition reads oddly and is transcribed as-is:
    /// <c>ChangeStats() || NumFreeAttacks() || NumGuardAttacks()</c>. So an ordinary turn marks
    /// itself done, and so does an interrupting attacker that still has attacks banked — the one
    /// case that does <i>not</i> latch is a spent interrupter, which is exactly the entry about to
    /// be popped anyway.
    /// </para>
    /// </remarks>
    public void EndTurn(TurnQueue queue, CombatantState newState = CombatantState.None)
    {
        ArgumentNullException.ThrowIfNull(queue);

        State = newState;

        if (queue.Top != Index)
        {
            return;
        }

        if (queue.AffectsStats || queue.FreeAttacks > 0 || queue.GuardAttacks > 0)
        {
            TurnIsDone = true;
        }

        queue.Pop();
    }

    /// <summary>
    /// Resets the per-round state (the body of <c>StartNewRound</c>'s combatant loop,
    /// <c>Combatants.cpp:4553</c>).
    /// </summary>
    /// <param name="attacksThisRound">
    /// What <c>determineNbrAttacks</c> / <c>determineAvailAttacks</c> worked out for this round.
    /// </param>
    /// <param name="continueGuarding">
    /// Whether the <c>GUARDING_START_OF_ROUND</c> hook said to keep guarding. Not wired up — see
    /// <see cref="IsCombatReady"/> for the same gap.
    /// </param>
    /// <param name="isAuto">
    /// Whether this combatant is computer-run (<c>OnAuto</c>). It takes a different branch of the
    /// guarding reset — see the remarks.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Nothing happens unless the combatant can act <i>and</i> has finished its last turn.</b>
    /// The reference gates the whole block on
    /// <c>charCanTakeAction() &amp;&amp; IsDone(...)</c> (<c>:4553</c>), so a combatant still
    /// mid-turn keeps everything it had, and one that is unconscious, dead, fled or petrified is
    /// skipped entirely.
    /// </para>
    /// <para>
    /// <b>Casting skips only the state reset, not the rest.</b> A spell in progress spans the round
    /// boundary, so the state is left alone — but attacks and movement are still recomputed,
    /// because that block sits outside the <c>ICS_Casting</c> check (<c>:4592</c>). Returning early
    /// for a caster would leave it with last round's movement.
    /// </para>
    /// <para>
    /// <b>Guarding persists by two different rules.</b> An auto combatant keeps
    /// <see cref="CombatantState.Guarding"/> outright; a player-run one is moved to
    /// <see cref="CombatantState.ContinueGuarding"/>, and only when the
    /// <c>GUARDING_START_OF_ROUND</c> hook said so.
    /// </para>
    /// <para>
    /// <b>Leftover attacks carry over, capped at this round's own maximum.</b> The reference adds
    /// the previous <c>availAttacks</c> back <i>after</i> recomputing, then clamps to the ceiling
    /// of the new value (<c>:4597</c>) — so an unused half-attack survives but cannot be banked
    /// indefinitely.
    /// </para>
    /// </remarks>
    public void BeginRound(double attacksThisRound, bool continueGuarding = false,
                           bool isAuto = false)
    {
        bool canTakeAction = Status is CharacterStatus.Okay or CharacterStatus.Running
                                       or CharacterStatus.Animated;
        if (!canTakeAction || !IsDone())
        {
            return;
        }

        if (State != CombatantState.Casting)
        {
            TurnIsDone = false;

            if (isAuto)
            {
                if (State != CombatantState.Guarding)
                {
                    State = CombatantState.None;
                }
            }
            else
            {
                State = State == CombatantState.Guarding && continueGuarding
                    ? CombatantState.ContinueGuarding
                    : CombatantState.None;
            }
        }

        double leftover = AvailableAttacks;
        AvailableAttacks = Math.Min(attacksThisRound + leftover, Math.Ceiling(attacksThisRound));

        Movement = 0;
        DiagonalMoves = 0;
    }
}
