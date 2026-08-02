namespace UAFcore;

/// <summary>
/// The round clock and the state dispatch that drives it
/// (<c>COMBAT_DATA</c>'s round half, <c>Combatants.cpp:4514</c> and <c>:6580</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the skeleton the encounter runs on: which round it is, whose turn, and what the
/// engine should be doing right now. It deliberately holds no combatant model — the per-combatant
/// handler (<c>COMBATANT::HandleCurrState</c>, <c>Combatant.cpp:3903</c>) needs attack resolution,
/// movement and the GPDL script hooks, none of which exist yet, and building the clock first is
/// what makes those testable when they arrive.
/// </para>
/// <para>
/// <b>The round starts at zero with initiative 1</b>, not at one. The original's comment explains
/// it: "Characters start with initiative=0 so we will need to start a new round before we can
/// begin" (<c>Combatants.cpp:203</c>). So the first thing any encounter does is roll over into
/// round 1.
/// </para>
/// </remarks>
public sealed class CombatRound
{
    /// <summary>
    /// How many rounds of nobody doing anything ends the fight
    /// (<c>MAX_COMBAT_IDLE_ROUNDS</c>, <c>Combatants.h:31</c>).
    /// </summary>
    public const int MaxIdleRounds = 20;

    private readonly Func<int, CombatantState> stateOf;

    /// <param name="stateOf">
    /// What a combatant is doing, by index. Supplied by the caller because the combatant model is
    /// not ported; <see cref="State"/> falls through to it when nothing overrides.
    /// </param>
    public CombatRound(Func<int, CombatantState>? stateOf = null) =>
        this.stateOf = stateOf ?? (_ => CombatantState.None);

    /// <summary>Whose turn it is, and who is interrupting.</summary>
    public TurnQueue Queue { get; } = new();

    /// <summary>The round number. Zero until the first <see cref="StartNewRound"/>.</summary>
    public int Round { get; private set; }

    /// <summary>Rounds in which nothing happened, counted toward <see cref="MaxIdleRounds"/>.</summary>
    public int IdleRounds { get; private set; }

    /// <summary>Set once the fight is decided; <see cref="State"/> reports it above all else.</summary>
    public bool IsOver { get; set; }

    /// <summary>
    /// Set when the round is due to roll over, and cleared by <see cref="StartNewRound"/>.
    /// </summary>
    /// <remarks>
    /// Starts <b>true</b>: an encounter has to cross a round boundary before anyone acts.
    /// </remarks>
    public bool IsStartingNewRound { get; set; } = true;

    /// <summary>
    /// Set when this round is the last one; the next rollover ends the fight
    /// (<c>m_bLastRound</c>).
    /// </summary>
    public bool IsLastRound { get; set; }

    /// <summary>
    /// Whether the acting combatant has just arrived at the top of the queue
    /// (<c>IsStartOfTurn</c>, <c>Combatants.cpp:6613</c>).
    /// </summary>
    /// <remarks>
    /// True for a fresh turn <i>or</i> a resumed one — the original ORs the two flags, so a
    /// combatant coming back from an interruption also counts as starting.
    /// </remarks>
    public bool IsStartOfTurn => Queue.StartOfTurn || Queue.RestartInterruptedTurn;

    /// <summary>
    /// Whether the acting combatant is taking a free attack rather than its own turn
    /// (<c>IsFreeAttacker</c>, <c>Combatants.cpp:6628</c>).
    /// </summary>
    /// <remarks>
    /// Note the <c>!AffectsStats</c> term: an interrupting attacker is queued with
    /// <c>AffectStats</c> false, which is what distinguishes it from an ordinary turn that merely
    /// happens to have attacks left.
    /// </remarks>
    public bool IsFreeAttacker =>
        Queue.Top != CombatMap.NoDude && !Queue.AffectsStats && Queue.FreeAttacks > 0;

    /// <summary>
    /// Whether the acting combatant is taking a guarding attack
    /// (<c>IsGuardAttacker</c>, <c>Combatants.cpp:6636</c>).
    /// </summary>
    public bool IsGuardAttacker =>
        Queue.Top != CombatMap.NoDude && !Queue.AffectsStats && Queue.GuardAttacks > 0;

    /// <summary>
    /// What the engine should be doing (<c>GetCombatState</c>, <c>Combatants.cpp:6580</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The precedence is the state machine: combat over beats a new round, which beats a new
    /// combatant, which beats whatever the acting combatant is doing. Getting the order wrong
    /// makes an encounter that has already been won keep taking turns.
    /// </para>
    /// <para>
    /// The final case is a straight cast from <see cref="CombatantState"/>, which is safe only
    /// because the two enums agree over the values a combatant is ever actually in — see
    /// <see cref="CombatState"/> for where they stop agreeing.
    /// </para>
    /// </remarks>
    public CombatState State()
    {
        if (IsOver)
        {
            return CombatState.CombatOver;
        }

        if (IsStartingNewRound)
        {
            return CombatState.StartNewRound;
        }

        if (IsStartOfTurn)
        {
            return CombatState.NewCombatant;
        }

        int top = Queue.Top;
        return top == CombatMap.NoDude
            ? CombatState.None
            : (CombatState)stateOf(top);
    }

    /// <summary>
    /// Whether nobody has attacked for long enough to call the fight off
    /// (<c>CheckIdleTime</c>, <c>Combatants.cpp:4480</c>).
    /// </summary>
    /// <param name="lastAttackRounds">
    /// Each combatant's <see cref="Combatant.LastAttackRound"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>It is the minimum across every combatant, not a per-round flag.</b> The reference takes
    /// the smallest <c>currentRound − lastAttackRound</c> over the whole list and calls the fight
    /// idle when even that exceeds <see cref="MaxIdleRounds"/> — so <i>one</i> combatant still
    /// swinging keeps everybody in it, and a fight only ends when nothing has attacked at all for
    /// twenty rounds.
    /// </para>
    /// <para>
    /// An earlier revision of this port had a per-round "did anything happen" counter here
    /// instead. It was an invention, and a fragile one: any caller that treated a miss or a step
    /// as activity would never fire it. The real rule keys on attacks alone.
    /// </para>
    /// </remarks>
    public bool IsIdle(IEnumerable<int> lastAttackRounds)
    {
        ArgumentNullException.ThrowIfNull(lastAttackRounds);

        int smallest = int.MaxValue;
        foreach (int last in lastAttackRounds)
        {
            smallest = Math.Min(smallest, Round - last);
        }

        // An empty list is not idle: there is nobody to be idle.
        return smallest != int.MaxValue && smallest > MaxIdleRounds;
    }

    /// <summary>
    /// Ends the fight when <see cref="IsIdle"/> says so, and reports the current idle span.
    /// </summary>
    public void CheckIdleTime(IEnumerable<int> lastAttackRounds)
    {
        if (IsIdle(lastAttackRounds))
        {
            IdleRounds = MaxIdleRounds;
            IsOver = true;
        }
    }

    /// <summary>
    /// The highest initiative value a round walks through
    /// (<c>m_iCurrInitiative &lt;= 22</c>, <c>Combatants.cpp:1639</c>).
    /// </summary>
    public const int MaxInitiative = 22;

    /// <summary>
    /// The initiative slot that never comes round (<c>INITIATIVE_Never</c>, <c>Combatant.h:557</c>).
    /// </summary>
    /// <remarks>
    /// One past <see cref="MaxInitiative"/>, so a combatant sitting on it is skipped by the walk.
    /// Casting uses it as the ceiling: a spell whose casting time would push it here is re-timed
    /// to land at the end of the round instead (see <see cref="PendingSpellList.Schedule"/>).
    /// </remarks>
    public const int NeverInitiative = 23;

    /// <summary>
    /// How far through the initiative order this round has got. Reset to 1 each round.
    /// </summary>
    public int CurrentInitiative { get; private set; } = 1;

    /// <summary>
    /// Picks the combatant to act (<c>getNextCombatant</c>, <c>Combatants.cpp:1610</c>).
    /// </summary>
    /// <param name="isDone">Whether a combatant has finished its turn.</param>
    /// <param name="initiativeOf">A combatant's rolled initiative.</param>
    /// <param name="combatantCount">How many combatants there are.</param>
    /// <returns>The combatant now acting, or <see cref="CombatMap.NoDude"/> when the round is spent.</returns>
    /// <remarks>
    /// <para>
    /// Two stages. First it drains the queue of anyone already finished, and returns the first who
    /// is not — that is how an interrupting attacker gets its turn, and how an interrupted
    /// combatant resumes. Only when the queue is empty does it walk the initiative order looking
    /// for someone who has not acted, and <see cref="TurnQueue.Push"/> that combatant.
    /// </para>
    /// <para>
    /// <b>It pushes at the head, not the tail.</b> A round is therefore never queued up in
    /// advance — combatants are pulled one at a time as their initiative comes round, which is
    /// what lets a spell resolving mid-round insert somebody.
    /// </para>
    /// <para>
    /// Returning <see cref="CombatMap.NoDude"/> is the signal to roll the round over; the caller
    /// does that (<c>Combatants.cpp:4403</c>), not this method.
    /// </para>
    /// </remarks>
    /// <param name="onInitiative">
    /// Run at each initiative slot before anybody is looked for there. This is where the casting
    /// clock ticks: a spell coming due requeues its caster, who then acts at that slot rather than
    /// at their own initiative.
    /// </param>
    public int Advance(Func<int, bool> isDone, Func<int, int> initiativeOf, int combatantCount,
                       Action<int>? onInitiative = null)
    {
        ArgumentNullException.ThrowIfNull(isDone);
        ArgumentNullException.ThrowIfNull(initiativeOf);

        // Stage one: whoever is queued and still has something to do.
        int dude = TakeFromQueue(isDone);
        if (dude != CombatMap.NoDude)
        {
            return dude;
        }

        // Stage two: the next initiative slot with somebody in it.
        while (CurrentInitiative <= MaxInitiative)
        {
            onInitiative?.Invoke(CurrentInitiative);

            // A spell that just came due put its caster back on the queue.
            dude = TakeFromQueue(isDone);
            if (dude != CombatMap.NoDude)
            {
                return dude;
            }

            for (int i = 0; i < combatantCount; i++)
            {
                if (initiativeOf(i) == CurrentInitiative && !isDone(i))
                {
                    Queue.Push(i, affectStats: true, freeAttacks: 0, guardAttacks: 0);
                    return i;
                }
            }

            CurrentInitiative++;
        }

        return CombatMap.NoDude;
    }

    /// <summary>Drains the finished off the queue and returns the first who is not.</summary>
    private int TakeFromQueue(Func<int, bool> isDone)
    {
        int dude;
        while ((dude = Queue.Top) != CombatMap.NoDude)
        {
            if (!isDone(dude))
            {
                return dude;
            }

            Queue.Pop();
        }

        return CombatMap.NoDude;
    }

    /// <summary>
    /// Rolls the clock over (the tail of <c>StartNewRound</c>, <c>Combatants.cpp:4671</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resets the initiative walk to 1 and leaves <see cref="IsStartingNewRound"/> set, which is
    /// what makes <see cref="State"/> report <see cref="CombatState.StartNewRound"/> once before
    /// anyone acts. The reference calls <c>getNextCombatant</c> from inside here; this leaves that
    /// to the caller so the round clock needs no combatant model of its own.
    /// </para>
    /// <para>
    /// <b>A last round ends the fight on the way in, not on the way out.</b> The reference tests
    /// <c>m_bLastRound</c> as the <i>first</i> act of <c>StartNewRound</c>
    /// (<c>Combatants.cpp:4520</c>) and sets combat over there, so the round marked last still
    /// runs to completion and the <i>next</i> rollover stops the encounter.
    /// </para>
    /// </remarks>
    public void BeginRound()
    {
        if (IsLastRound)
        {
            IsOver = true;
        }

        Round++;
        CurrentInitiative = 1;
        IsStartingNewRound = true;
    }
}
