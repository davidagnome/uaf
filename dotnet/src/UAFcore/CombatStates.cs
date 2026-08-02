namespace UAFcore;

/// <summary>
/// What one combatant is doing (<c>individualCombatantState</c>, <c>Combatant.h:30</c>).
/// </summary>
/// <remarks>
/// <b>Values 11 and above are never assigned as a state.</b> The header marks them "Not used as an
/// ICS_STATE...only for script", and a repo-wide search confirms it — nothing ever calls
/// <c>State(ICS_Dead)</c> or its neighbours. They exist so a GPDL script can ask about a
/// combatant's condition using the same vocabulary.
/// </remarks>
public enum CombatantState
{
    None = 0,
    Casting = 1,
    Attacking = 2,
    Guarding = 3,
    Bandaging = 4,
    Using = 5,
    Moving = 6,
    Turning = 7,
    Fleeing = 8,
    Fled = 9,

    /// <summary>
    /// Still guarding from last round, until this combatant's initiative comes round again.
    /// </summary>
    ContinueGuarding = 10,

    // Script-only from here. See the remarks.
    Petrified = 11,
    Dying = 12,
    Unconscious = 13,
    Dead = 14,
    Gone = 15,
}

/// <summary>
/// What the encounter as a whole is doing (<c>overallCombatState</c>, <c>Combatants.h:34</c>).
/// </summary>
/// <remarks>
/// <para>
/// The first eleven values mirror <see cref="CombatantState"/>, and
/// <see cref="CombatRound.State"/> casts straight across
/// (<c>GetCombatState</c>, <c>Combatants.cpp:6604</c>).
/// </para>
/// <para>
/// <b>The two enums do not actually match, despite both headers insisting they must.</b>
/// <see cref="CombatantState"/> has <c>Unconscious</c> at 13; this one has no equivalent, so from
/// 13 onward they are off by one and the cast would turn <c>Unconscious</c> into
/// <see cref="Dead"/>, <c>Dead</c> into <see cref="Gone"/>, and <c>Gone</c> into
/// <see cref="NewCombatant"/>. It is latent rather than live only because those values are never
/// assigned as a state. Transcribed as-is: silently inserting the missing member would renumber
/// everything after it, and the numbering reaches save games (§4.4).
/// </para>
/// </remarks>
public enum CombatState
{
    None = 0,
    Casting = 1,
    Attacking = 2,
    Guarding = 3,
    Bandaging = 4,
    Using = 5,
    Moving = 6,
    Turning = 7,
    Fleeing = 8,
    Fled = 9,
    ContinueGuarding = 10,
    Petrified = 11,
    Dying = 12,

    // No Unconscious here -- see the remarks. This is where the two enums part company.
    Dead = 13,
    Gone = 14,

    /// <summary>A combatant has just reached the top of the queue and has not acted yet.</summary>
    NewCombatant = 15,

    CombatOver = 16,
    Delaying = 17,
    EndingTurn = 18,
    ForcingWin = 19,
    StartNewRound = 20,
    ActivateSpell = 21,
    DisplayingAttacker = 22,
}
