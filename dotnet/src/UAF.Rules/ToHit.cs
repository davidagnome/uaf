namespace UAF.Rules;

/// <summary>
/// Resolving an attack roll (<c>ToHitComputation</c>, <c>UAFWin/Combatant.cpp:5060</c>).
/// </summary>
/// <remarks>
/// <para>
/// The first piece of combat in the port, and the payoff for the rest of <c>UAF.Rules</c>: it
/// consumes <see cref="Thac0"/>, <see cref="ArmorClass"/> and <see cref="Strength"/> and produces
/// the number a d20 has to beat.
/// </para>
/// <para>
/// <b>Everything is folded into one target number rather than into the roll.</b> The bonuses are
/// <i>subtracted</i> from the attacker's THAC0, along with the target's armour class, and the
/// result is what the die must reach. That is why a better armour class — a lower number — raises
/// the target and a bonus lowers it.
/// </para>
/// </remarks>
public static class ToHit
{
    /// <summary>The floor below which the target number collapses to zero (<c>MIN_THAC0</c>).</summary>
    public const int MinimumThac0 = -500;

    /// <summary>
    /// The number the die must reach (<c>ComputeEffectiveTHAC0</c>).
    /// </summary>
    /// <param name="attackerThac0">The attacker's own THAC0 — <see cref="Thac0.ForCharacter"/>.</param>
    /// <param name="targetArmorClass">
    /// The target's effective armour class. <b>Subtracted</b>, so a target at -2 is harder to hit
    /// than one at 10 — the sign does the work.
    /// </param>
    /// <param name="environmentalBonus">
    /// Range, cover, lighting and the like. Subtracted, so a positive value helps the attacker.
    /// </param>
    /// <param name="weaponBonus">The weapon's own to-hit bonus, likewise subtracted.</param>
    /// <remarks>
    /// <b>A target number below <see cref="MinimumThac0"/> becomes 0, not the floor.</b> The
    /// reference tests <c>&lt; MIN_THAC0</c> and then assigns <c>0</c> (<c>Combatant.cpp:5211</c>)
    /// — so an absurdly favourable attack lands on "any roll hits" rather than on -500. Clamping to
    /// the constant instead would look tidier and be wrong.
    /// </remarks>
    public static int TargetNumber(int attackerThac0, int targetArmorClass,
                                   int environmentalBonus = 0, int weaponBonus = 0)
    {
        int target = attackerThac0 - environmentalBonus - weaponBonus - targetArmorClass;
        return target < MinimumThac0 ? 0 : target;
    }

    /// <summary>
    /// Whether a roll hits (<c>Combatant.cpp:5395</c>).
    /// </summary>
    /// <remarks>
    /// <b>Equalling the target hits</b> — the test is <c>&gt;=</c>. There is no natural-20 or
    /// natural-1 rule here: a 20 is simply a high roll, and the special treatment a 20 gets
    /// elsewhere is a vorpal special ability rather than an automatic hit.
    /// </remarks>
    public static bool Hits(int roll, int targetNumber) => roll >= targetNumber;

    /// <summary>Resolves an attack end to end.</summary>
    public static bool Resolve(int roll, int attackerThac0, int targetArmorClass,
                               int environmentalBonus = 0, int weaponBonus = 0) =>
        Hits(roll, TargetNumber(attackerThac0, targetArmorClass, environmentalBonus, weaponBonus));

    /// <summary>
    /// The damage an attack does, before the target's own modifiers.
    /// </summary>
    /// <param name="rolledDice">The weapon's damage dice, already rolled.</param>
    /// <param name="weaponBonus">The weapon's own damage bonus.</param>
    /// <param name="strengthBonus">
    /// The wielder's strength bonus — <see cref="Strength.DamageBonus"/> — or zero for a missile,
    /// which carries none.
    /// </param>
    /// <remarks>
    /// <b>Damage floors at 1, not 0.</b> A penalty large enough to cancel the dice still leaves a
    /// point through: an attack that lands always does something.
    /// </remarks>
    public static int Damage(int rolledDice, int weaponBonus = 0, int strengthBonus = 0)
    {
        int total = rolledDice + weaponBonus + strengthBonus;
        return total < 1 ? 1 : total;
    }
}
