using UAF.Rules;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the temple's GIVE entry and its running total.</summary>
public class DonationTests
{
    private static Donation Typed(string digits, int maximum)
    {
        var entry = Donation.None;

        foreach (char digit in digits)
        {
            entry = entry.Type(digit, maximum);
        }

        return entry;
    }

    [Fact]
    public void Nothing_typed_is_nothing_given()
    {
        // atoi("") is 0, so leaving without typing donates nothing rather than erroring.
        Assert.Equal(0, Donation.None.Amount);
    }

    [Fact]
    public void Digits_accumulate_left_to_right()
    {
        Assert.Equal(123, Typed("123", maximum: 1000).Amount);
    }

    [Fact]
    public void Too_much_snaps_to_the_maximum_rather_than_being_refused()
    {
        // The whole entry is cleared and replaced -- so a player mashing digits ends up offering
        // everything they have.
        Assert.Equal(150, Typed("999", maximum: 150).Amount);
    }

    [Fact]
    public void The_snap_happens_on_the_digit_that_crosses()
    {
        var entry = Typed("14", maximum: 150);
        Assert.Equal(14, entry.Amount);

        entry = entry.Type('9', 150);           // 149 still fits
        Assert.Equal(149, entry.Amount);

        entry = entry.Type('9', 150);           // 1499 does not
        Assert.Equal(150, entry.Amount);
    }

    [Fact]
    public void Typing_on_after_a_snap_snaps_again()
    {
        var entry = Typed("9", maximum: 5);
        Assert.Equal(5, entry.Amount);

        Assert.Equal(5, entry.Type('9', 5).Amount);
    }

    [Fact]
    public void Backspace_takes_the_last_digit_off()
    {
        var entry = Typed("123", maximum: 1000).Backspace();

        Assert.Equal(12, entry.Amount);
    }

    [Fact]
    public void Backspacing_an_empty_entry_is_harmless()
    {
        Assert.Equal(0, Donation.None.Backspace().Amount);
    }

    [Fact]
    public void Anything_that_is_not_a_digit_is_ignored()
    {
        Assert.Equal(12, Typed("1a2", maximum: 100).Amount);
    }

    [Fact]
    public void A_maximum_of_nothing_keeps_the_entry_at_nothing()
    {
        Assert.Equal(0, Typed("500", maximum: 0).Amount);
    }

    // ---- the running total -----------------------------------------------------------------------

    private static Purse Purse(int coins)
    {
        var purse = new Purse(MoneyRules.Default);
        purse.Add(MoneyRules.Default.BaseType, coins);
        return purse;
    }

    [Fact]
    public void Giving_takes_the_money_and_adds_to_the_total()
    {
        var purse = Purse(100);

        int total = TempleDonations.Give(purse, 30, runningTotal: 5);

        Assert.Equal(35, total);
        Assert.Equal(70, purse[MoneyRules.Default.BaseType]);
    }

    [Fact]
    public void A_payment_that_cannot_be_made_adds_nothing()
    {
        // payForItem refuses outright rather than taking what it can, so the total must not move
        // either.
        var purse = Purse(10);

        int total = TempleDonations.Give(purse, 30, runningTotal: 5);

        Assert.Equal(5, total);
        Assert.Equal(10, purse[MoneyRules.Default.BaseType]);
    }

    [Fact]
    public void Giving_nothing_changes_nothing()
    {
        var purse = Purse(100);

        Assert.Equal(5, TempleDonations.Give(purse, 0, runningTotal: 5));
        Assert.Equal(100, purse[MoneyRules.Default.BaseType]);
    }

    [Fact]
    public void The_total_crosses_the_trigger_and_stays_crossed()
    {
        Assert.False(TempleDonations.Triggers(99, trigger: 100));
        Assert.True(TempleDonations.Triggers(100, trigger: 100));
        Assert.True(TempleDonations.Triggers(500, trigger: 100));
    }

    [Fact]
    public void A_trigger_of_nothing_fires_on_every_visit()
    {
        // The total starts at zero and the test is >=, so a design that leaves the field unset
        // chains every time the party walks out -- donation or not.
        Assert.True(TempleDonations.Triggers(0, trigger: 0));
    }

    [Fact]
    public void Several_small_visits_add_up_to_the_trigger()
    {
        // The total lives on the temple rather than the party, so it survives between visits.
        var purse = Purse(100);
        int total = 0;

        for (int visit = 0; visit < 5; visit++)
        {
            total = TempleDonations.Give(purse, 10, total);
        }

        Assert.Equal(50, total);
        Assert.True(TempleDonations.Triggers(total, trigger: 50));
    }
}
