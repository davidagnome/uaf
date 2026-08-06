namespace UAFcore;

/// <summary>
/// The amount a player is typing into the temple's GIVE screen
/// (<c>TEMPLE::OnKeypress</c>'s <c>TASK_TempleGive</c>, <c>RunEvent.cpp:12753</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a menu, not a text field.</b> Each digit is its own menu entry with a blank one on the
/// end, and the amount is the entries concatenated and run through <c>atoi</c>. Backspace deletes
/// the last entry. Modelled here as a string because what the screen does with it is arithmetic,
/// but the shape explains the behaviour below.
/// </para>
/// <para>
/// <b>Too much snaps to the maximum rather than being refused.</b> A digit that would take the
/// amount past what the party can pay — or below zero — clears the whole entry and replaces it
/// with the maximum. So a player mashing digits ends up offering everything they have, which is
/// presumably the point.
/// </para>
/// </remarks>
public readonly record struct Donation(string Digits)
{
    /// <summary>Nothing typed yet.</summary>
    public static Donation None => new(string.Empty);

    /// <summary>What has been typed, as a number.</summary>
    /// <remarks>
    /// <b>Empty is zero</b>, because <c>atoi("")</c> is 0 — so leaving the screen without typing
    /// donates nothing rather than being an error.
    /// </remarks>
    public int Amount => int.TryParse(Digits, out int value) ? value : 0;

    /// <summary>
    /// Types a digit.
    /// </summary>
    /// <param name="maximum">
    /// What the party could give — the pooled gold when money is pooled, otherwise the active
    /// character's own, converted to the default denomination.
    /// </param>
    public Donation Type(char digit, int maximum)
    {
        if (digit is < '0' or > '9')
        {
            return this;
        }

        var typed = new Donation(Digits + digit);

        // Past what they have, or wrapped negative: the whole entry becomes the maximum.
        return typed.Amount > maximum || typed.Amount < 0
            ? new Donation(maximum.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : typed;
    }

    /// <summary>Deletes the last digit.</summary>
    public Donation Backspace() =>
        Digits.Length == 0 ? this : new Donation(Digits[..^1]);
}

/// <summary>
/// What a temple has been given, and what that eventually triggers.
/// </summary>
/// <remarks>
/// <para>
/// <b>The total is the temple's, not the party's.</b> It lives on the event and is saved with it,
/// so a party that gives a little on each of several visits still crosses the threshold.
/// </para>
/// <para>
/// <b>It is only tested on the way out.</b> Crossing the trigger mid-visit does nothing; the check
/// happens in the EXIT branch, and only when the party is not being asked about pooled money
/// first.
/// </para>
/// </remarks>
public static class TempleDonations
{
    /// <summary>
    /// Takes a donation from a purse.
    /// </summary>
    /// <returns>The new running total, or the old one when the payment could not be made.</returns>
    /// <remarks>
    /// <b>Nothing is added to the total unless the payment happened.</b> The reference's
    /// <c>payForItem</c> refuses outright when the purse cannot cover the amount, and the entry
    /// screen has already capped what can be typed — so the two agree unless something else spent
    /// the money in between.
    /// </remarks>
    public static int Give(UAF.Rules.Purse purse, int amount, int runningTotal)
    {
        ArgumentNullException.ThrowIfNull(purse);

        if (amount <= 0 || !purse.HaveEnough(purse.Rules.BaseType, amount))
        {
            return runningTotal;
        }

        purse.Subtract(purse.Rules.BaseType, amount);
        return runningTotal + amount;
    }

    /// <summary>
    /// Whether leaving now fires the temple's donation chain.
    /// </summary>
    /// <remarks>
    /// <b>A trigger of zero fires on any visit at all</b>, since the total starts at zero and the
    /// test is <c>&gt;=</c>. A design that leaves the field unset therefore chains every time the
    /// party walks out, donation or not.
    /// </remarks>
    public static bool Triggers(int runningTotal, int trigger) => runningTotal >= trigger;
}
