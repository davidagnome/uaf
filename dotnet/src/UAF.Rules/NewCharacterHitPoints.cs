namespace UAF.Rules;

/// <summary>
/// The little generator a new character's hit points are rolled from (<c>LITTLE_RAN</c>,
/// <c>Char.cpp:5117</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists so that re-rolling ability scores does not re-roll the dice.</b> The comment
/// above <c>DetermineNewCharMaxHitPoints</c> explains it: the hit-point computation is
/// "complex and non-linear because of multiple baseclasses", so the engine keeps its own seeded
/// generator and re-seeds it before every attempt. Change constitution and the same dice come up
/// with a different bonus; change nothing and the total is identical.
/// </para>
/// <para>
/// <b><c>z</c> is initialised and never used.</b> <c>Init</c> sets both halves and guards both
/// against zero, and <c>Random</c> advances only <c>w</c> — the two-word generator the commented-
/// out line above it describes was replaced by a one-word one and the setup was left behind.
/// </para>
/// </remarks>
public sealed class LittleRandom
{
    private uint w;

    public LittleRandom(uint seed)
    {
        w = seed & 0xFFFF;

        // The reference also sets z = seed >> 16 and guards it; nothing reads it.
        if (w == 0)
        {
            w = 0xC451;
        }
    }

    /// <summary><c>w = 69069*w + 1; return w + ((w &gt;&gt; 16) &amp; 65535);</c></summary>
    public uint Next()
    {
        w = (69069 * w) + 1;
        return w + ((w >> 16) & 65535);
    }

    /// <summary>Rolls <paramref name="count"/> dice and adds <paramref name="bonus"/>.</summary>
    /// <remarks>
    /// <b>Zero or fewer sides rolls nothing at all</b> and the bonus is still returned — the loop
    /// is skipped entirely rather than treating the die as a 1.
    /// </remarks>
    public int Roll(int sides, int count, int bonus)
    {
        if (sides <= 0)
        {
            return bonus;
        }

        for (; count > 0; count--)
        {
            bonus += (int)(Next() % (uint)sides) + 1;
        }
        return bonus;
    }
}

/// <summary>One level's hit dice for one baseclass.</summary>
public readonly record struct LevelHitDice(int Sides, int Count, int Constant);

/// <summary>
/// A new character's hit points (<c>DetermineNewCharMaxHitPoints</c>, <c>Char.cpp:5162</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not the formula training uses.</b> <c>DetermineCharMaxHitPoints</c> — the one that
/// runs when a character levels up — rolls only the levels just gained and <i>adds</i> to the
/// existing maximum. This one rolls every level from 1 and replaces it. Same character, two
/// formulas, and they disagree in the two ways below.
/// </para>
/// <para>
/// <b>The per-level constant is only added when the baseclass rolls no dice.</b> The line is
/// <c>HP += (numDice&gt;0) ? ran.Roll(sides, numDice, bonus) : 0 + constant;</c> — and <c>?:</c>
/// binds looser than <c>+</c>, so it parses as <c>… : (0 + constant)</c>. The training path writes
/// <c>RollDice(…) + constant</c> and gets it right. A baseclass with dice therefore loses its
/// constant at creation and gains it on every level-up afterwards. Transcribed as written,
/// because a design's hit points are balanced against what the engine does and not against what
/// it meant.
/// </para>
/// <para>
/// <b>The comment says "take the average" and the code takes the sum.</b> Twenty lines of the
/// function are given over to explaining that a multi-class character's baseclasses should be
/// averaged, and <c>numBaseclass</c> is counted for it — then the result is
/// <c>max(1, totalHP)</c> and the count is never read. A fighter/mage gets both baseclasses' hit
/// points in full.
/// </para>
/// </remarks>
public static class NewCharacterHitPoints
{
    /// <summary>
    /// Rolls the maximum hit points for a freshly made character.
    /// </summary>
    /// <param name="baseclasses">
    /// Each baseclass the character has: its level, its ability bonus, and a lookup for the dice
    /// at a given level.
    /// </param>
    /// <param name="seed">
    /// <c>hitpointSeed</c>. The same seed and the same levels give the same dice, so only a
    /// changed ability bonus moves the total.
    /// </param>
    public static int Roll(
        IEnumerable<(int Level, int Bonus, Func<int, LevelHitDice> DiceAt)> baseclasses,
        uint seed)
    {
        ArgumentNullException.ThrowIfNull(baseclasses);

        var random = new LittleRandom(seed);
        int total = 0;

        foreach (var (level, bonus, diceAt) in baseclasses)
        {
            for (int j = 1; j <= level; j++)
            {
                var dice = diceAt(j);

                // The precedence bug, transcribed: the constant is the *else* branch.
                total += dice.Count > 0
                    ? random.Roll(dice.Sides, dice.Count, bonus)
                    : 0 + dice.Constant;
            }
        }

        // max(1, totalHP) -- summed across baseclasses, never averaged.
        return Math.Max(1, total);
    }
}
