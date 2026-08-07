namespace UAF.Scripting;

/// <summary>
/// One weapon a combatant could attack with (<c>WEAPON_SUMMARY</c>, <c>CombatSummary.h</c>).
/// </summary>
/// <remarks>
/// <b>Everything here is already reduced to a number the script can compare.</b> The damage dice
/// are averages rather than expressions, and the range is <c>range22</c> — the same
/// <c>4 × (dx² + dy²)</c> units as <see cref="ForthAction.Distance22"/> — so the script can test a
/// weapon's reach against a target's distance without converting either.
/// </remarks>
public sealed class ForthWeapon
{
    public int Type { get; init; }

    public int Range22 { get; init; }

    public int Priority { get; init; }

    public int AttackBonus { get; init; }

    public int RateOfFire { get; init; }

    public int Protection { get; init; }

    public int SmallDamageDice { get; init; }

    public int SmallDamageBonus { get; init; }

    public int LargeDamageDice { get; init; }

    public int LargeDamageBonus { get; init; }
}

/// <summary>
/// One combatant as the AI script sees them (<c>COMBAT_SUMMARY_COMBATANT</c>,
/// <c>CombatSummary.h:57</c>).
/// </summary>
public sealed class ForthCombatant
{
    public int Index { get; init; }

    public int X { get; init; } = -1;

    public int Y { get; init; } = -1;

    public int Width { get; init; } = 1;

    public int Height { get; init; } = 1;

    public int Fleeing { get; init; }

    /// <summary>Friendly <i>to combatant 0</i>, which is whoever is taking the turn.</summary>
    public int Friendly { get; init; }

    /// <summary>From <c>individualCombatantState</c> and various flags — the <c>C:S:*</c> constants.</summary>
    public int State { get; init; }

    public int AvailableAttacks { get; init; }

    public int IsLarge { get; init; }

    public int CanMove { get; init; }

    /// <summary>
    /// From the combatant's <c>AIBaseclass</c> special ability.
    /// </summary>
    /// <remarks>
    /// <b>-1 means not computed and bit 15 means the combatant has no such abilities</b> — two
    /// distinct "no answer" values, both of which a script sees as a number.
    /// </remarks>
    public int AiBaseclass { get; init; } = -1;

    public IReadOnlyList<ForthWeapon> Weapons { get; init; } = [];

    public int ShieldCount { get; init; }

    /// <summary>Which shield the script has asked to be readied. Written by <c>Shield.Ready!</c>.</summary>
    public int ShieldToReady { get; set; }
}

/// <summary>
/// One candidate action, of the two <c>THINK</c> is asked to compare
/// (<c>COMBAT_SUMMARY_ACTION</c>, <c>CombatSummary.h:145</c>).
/// </summary>
public sealed class ForthAction
{
    public int ActionType { get; init; }

    public int TargetOrdinal { get; init; }

    /// <summary>
    /// Which of <see cref="Me"/>'s weapons, <b>one-based, with 0 meaning none</b>.
    /// </summary>
    /// <remarks>
    /// The <c>W:</c> words all test <c>weaponOrd == 0 || weaponOrd > count</c> and push 0 —
    /// <c>NotWeapon</c> — rather than reading out of range. So an action with no weapon and an
    /// action with a nonsense weapon are indistinguishable to the script.
    /// </remarks>
    public int WeaponOrdinal { get; init; }

    public int AmmoOrdinal { get; init; }

    public int AttackOrdinal { get; init; }

    /// <summary>
    /// <b>Not squares.</b> <c>4 × (dx² + dy²)</c> between the nearest edges of the two footprints.
    /// Every threshold in the shipped script is in these units.
    /// </summary>
    public int Distance22 { get; init; }

    public int Damage { get; init; }

    public int Advance { get; init; }

    public int HasLineOfSight { get; init; }

    /// <summary>Who would act.</summary>
    public ForthCombatant Me { get; init; } = new();

    /// <summary>Who it would be done to.</summary>
    public ForthCombatant He { get; init; } = new();
}

/// <summary>
/// What <c>THINK</c> is handed: two candidate actions and everybody in the fight
/// (<c>COMBAT_SUMMARY</c>).
/// </summary>
/// <remarks>
/// <b><c>THINK</c> is a comparator, not a chooser.</b> It is called with two actions and returns
/// A minus B — positive meaning A is preferred — and the caller heap-sorts the candidate list with
/// it (<c>Combatant.cpp:2251</c>). So the script never sees the whole list, and the ordering it
/// expresses has to be transitive for the sort to mean anything.
/// </remarks>
public sealed class ForthCombatSummary
{
    public ForthAction ActionA { get; init; } = new();

    public ForthAction ActionB { get; init; } = new();

    /// <summary>
    /// Everybody in the fight. <b>Combatant 0 is the one taking the turn</b> — it is the one
    /// <c>Shield.Ready!</c> writes to, whichever combatant the script had selected.
    /// </summary>
    public IReadOnlyList<ForthCombatant> Combatants { get; init; } = [];
}
