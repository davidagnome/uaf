namespace UAF.Rules;

/// <summary>
/// Carrying capacity and the movement rate it allows (<c>GameRules.cpp:2109</c>,
/// <c>Char.cpp:5719</c>).
/// </summary>
/// <remarks>
/// The first piece of <c>GameRules.cpp</c> in this port, chosen because it is entirely
/// self-contained: two tables over a strength score, with no equipment, spell effects or combat
/// state involved.
/// </remarks>
public static class Encumbrance
{
    /// <summary>What a character of strength 8–11 can carry, in gold pieces.</summary>
    public const int BaseAllowance = 350;

    /// <summary>The movement rate of an unencumbered character.</summary>
    public const int BaseMovement = 12;

    /// <summary>
    /// The weight a character can carry before being encumbered
    /// (<c>DetermineNormalEncumbrance</c>).
    /// </summary>
    /// <param name="strengthMod">
    /// The percentile on an exceptional 18, and 0 otherwise. It is read as a <c>BYTE</c> in the
    /// reference, so 100 and above all fall in the top band.
    /// </param>
    /// <remarks>
    /// <para>
    /// A table, not a formula — the steps are irregular (350, 100, 200, 350, 500 …) and the
    /// exceptional-strength bands are irregular again. Anything outside 1–25 falls back to the base
    /// allowance rather than extrapolating.
    /// </para>
    /// <para>
    /// <b>The result is floored at 1, not at 0.</b> Strength 3 or below computes to exactly zero
    /// and the reference bumps it to 1, which matters because <see cref="MaxMovementFor"/> divides
    /// the carried weight by this: a zero would make every such character maximally encumbered
    /// regardless of what they carry.
    /// </para>
    /// </remarks>
    public static int NormalAllowance(int strength, int strengthMod = 0)
    {
        int result = strength switch
        {
            <= 3 and >= 1 => BaseAllowance - 350,
            4 or 5 => BaseAllowance - 250,
            6 or 7 => BaseAllowance - 150,
            8 or 9 or 10 or 11 => BaseAllowance,
            12 or 13 => BaseAllowance + 100,
            14 or 15 => BaseAllowance + 200,
            16 => BaseAllowance + 350,
            17 => BaseAllowance + 500,
            18 => BaseAllowance + ExceptionalBonus(strengthMod),

            // 19 to 25 are the giant-strength range, added to the original rules by the project.
            19 => BaseAllowance + 4500,
            20 => BaseAllowance + 5000,
            21 => BaseAllowance + 6000,
            22 => BaseAllowance + 7500,
            23 => BaseAllowance + 9000,
            24 => BaseAllowance + 12000,
            25 => BaseAllowance + 15000,
            _ => BaseAllowance,
        };

        return result <= 0 ? 1 : result;
    }

    /// <summary>The exceptional-strength bands at 18 — irregular, and open at the top.</summary>
    private static int ExceptionalBonus(int strengthMod)
    {
        // Read as a BYTE in the reference, so a modifier of 256 wraps to 0 rather than saturating.
        byte mod = (byte)strengthMod;

        return mod switch
        {
            0 => 750,
            < 51 => 1000,
            < 76 => 1250,
            < 91 => 1500,
            < 100 => 2000,
            _ => 3000,
        };
    }

    /// <summary>
    /// The most a character can carry at all — five times the unencumbered allowance
    /// (<c>determineMaxEncumbrance</c>).
    /// </summary>
    public static int MaxAllowance(int strength, int strengthMod = 0) =>
        NormalAllowance(strength, strengthMod) * 5;

    /// <summary>
    /// The movement rate a carried weight allows (<c>determineMaxMovement</c>).
    /// </summary>
    /// <param name="carried">
    /// The <i>effective</i> weight — <c>determineEffectiveEncumbrance</c>, which ignores magical
    /// items. This takes the number rather than computing it, since that needs the inventory.
    /// </param>
    /// <remarks>
    /// Four steps of the allowance and then a floor: 12, 9, 6, 3, and 1 beyond four times. Note the
    /// last band is <b>1, not 0</b> — a character loaded past four times their allowance still
    /// moves, just barely.
    /// </remarks>
    public static int MaxMovementFor(int carried, int strength, int strengthMod = 0)
    {
        int allowance = NormalAllowance(strength, strengthMod);

        if (carried <= allowance) { return BaseMovement; }
        if (carried <= allowance * 2) { return 9; }
        if (carried <= allowance * 3) { return 6; }
        if (carried <= allowance * 4) { return 3; }
        return 1;
    }
}
