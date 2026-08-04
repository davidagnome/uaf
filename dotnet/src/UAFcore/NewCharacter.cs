using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The parts of <c>generateNewCharacter</c> (<c>Char.cpp:4278</c>) that a new character needs
/// beyond its rolled abilities.
/// </summary>
/// <remarks>
/// <para>
/// <b>One of its two paths is dead code that would crash.</b> The function handles
/// <c>START_EXP_VALUE</c> and then calls <c>die("Not Needed?")</c> for the "start experience is a
/// minimum level" case — so a design configured that way takes the reference down. Only the value
/// path is ported, because only the value path runs.
/// </para>
/// <para>
/// Age, weight and height all come from a race's <c>DICEPLUS</c>, which the reference compiles
/// through the GPDL toolchain. <see cref="DiceFormula"/> evaluates the subset every shipped
/// design actually uses and refuses the rest by name — see its remarks.
/// </para>
/// </remarks>
public static class NewCharacter
{
    /// <summary>How many days a birthday is rolled across (<c>RollDice(365,1)</c>).</summary>
    public const int DaysInYear = 365;

    /// <summary>
    /// A new character's purse (<c>getNewCharStartingMoney</c>, <c>Char.cpp:5636</c>).
    /// </summary>
    /// <remarks>
    /// <b>The field is called <c>StartPlatinum</c> and the coins are not platinum.</b> The amount
    /// goes in at <c>money.GetDefaultType()</c> — the design's own base denomination — so a design
    /// whose currency is copper starts its characters with that many copper pieces. The name is a
    /// leftover from when the denominations were fixed.
    /// </remarks>
    public static Purse StartingMoney(int coins, int gems, int jewelry, MoneyRules money)
    {
        ArgumentNullException.ThrowIfNull(money);

        var purse = new Purse(money);
        purse.Add(money.BaseType, coins);

        for (int g = 0; g < gems; g++)
        {
            purse.AddGem(new GemType(0, 0));
        }

        for (int j = 0; j < jewelry; j++)
        {
            purse.AddJewelry(new GemType(0, 0));
        }

        return purse;
    }

    /// <summary>
    /// A new character's kit (<c>getNewCharStartingEquip</c>, <c>Char.cpp:5652</c>).
    /// </summary>
    /// <remarks>
    /// <b>Copied off the class, wholesale.</b> There is no per-baseclass contribution and no
    /// merging — a multi-class character gets its class record's list and nothing from the
    /// baseclasses underneath it.
    /// </remarks>
    public static List<ItemInstance> StartingEquipment(ClassRecord? record) =>
        record is null ? [] : [.. record.StartingEquipment.Items];

    /// <summary>
    /// The baseclass rows a new character starts with (<c>Char.cpp:4310</c>).
    /// </summary>
    /// <remarks>
    /// <b>One row per baseclass of the class, all at level 1 with no experience</b> — the starting
    /// experience is given afterwards and the levelling walks up from there, so a character who
    /// begins above first level does so by being awarded experience and then trained, not by being
    /// created at that level.
    /// </remarks>
    public static List<BaseclassStats> BaseclassRows(ClassRecord? record) =>
        record is null
            ? []
            : [.. record.Baseclasses.Select(
                id => new BaseclassStats(id, CurrentLevel: 1, PreviousLevel: 0,
                                         PreDrainLevel: 0, Experience: 0))];

    /// <summary>
    /// Rolls a race's dice field — its age, maximum age, weight, height or movement.
    /// </summary>
    /// <param name="male">
    /// What the expression's one identifier resolves to. Weight and height are the fields that
    /// use it, to add a gender bonus.
    /// </param>
    /// <returns>The rolled value, or null when the expression is empty or unsupported.</returns>
    /// <remarks>
    /// <b>Null is the reference's own "did not roll".</b> <c>RACE_DATA::GetStartAge</c> returns 0
    /// when <c>Roll</c> answers false, so an empty field and a refused one are the same answer
    /// there; keeping them distinct here is what lets a caller say which happened.
    /// </remarks>
    public static int? Roll(DicePlus? dice, Func<int, int, int> roll, bool male,
                            out string? unsupported)
    {
        unsupported = null;
        if (dice is null)
        {
            return null;
        }

        return DiceFormula.TryEvaluate(dice.Text, roll,
                                       n => n == DiceFormula.MaleSymbol ? (male ? 1 : 0) : null,
                                       out int value, out unsupported)
            ? value
            : null;
    }

    /// <summary>Rolls a birthday — a day of the year, 1 to 365.</summary>
    /// <param name="roll">Rolls <c>count</c> dice of <c>sides</c> and totals them.</param>
    public static int Birthday(Func<int, int, int> roll)
    {
        ArgumentNullException.ThrowIfNull(roll);
        return roll(1, DaysInYear);
    }

    /// <summary>
    /// A starting age, floored at the design's minimum (<c>determineCharStartAge</c>,
    /// <c>GameRules.cpp:2076</c>).
    /// </summary>
    /// <param name="rolledAge">
    /// What the race's age dice gave. Passed in because <c>DICEPLUS::Roll</c> is not ported — see
    /// the remarks on this class.
    /// </param>
    /// <remarks>
    /// <b>The floor only applies when it is positive.</b> <c>START_AGE</c> is design
    /// configuration and a zero or negative one leaves the race's roll alone rather than clamping
    /// everything to it.
    /// </remarks>
    public static int StartAge(int rolledAge, int minimumAge) =>
        minimumAge > 0 && rolledAge < minimumAge ? minimumAge : rolledAge;

    /// <summary>
    /// Caps an age at the character's maximum (<c>age = min(maxAge, age)</c>, <c>Char.cpp:4413</c>).
    /// </summary>
    /// <remarks>
    /// Applied after the floor, so a race whose maximum age is below the design's minimum
    /// starting age produces a character born at its own limit — the two clamps are not checked
    /// against each other.
    /// </remarks>
    public static int CapAge(int age, int maxAge) => Math.Min(age, maxAge);
}
