using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers typed text: what each screen accepts, and how a password is checked.
/// </summary>
/// <remarks>
/// Two screens share one behaviour and disagree about the rules — punctuation, leading spaces,
/// refused characters and length — so the disagreements are data and the behaviour is not
/// duplicated.
/// </remarks>
public class TextEntryTests
{
    private static TextEntry Typing(TextEntryRules rules, string text)
    {
        var entry = new TextEntry(rules);
        foreach (char c in text)
        {
            entry.Type(c);
        }
        return entry;
    }

    // ---- what gets typed -----------------------------------------------------------------------

    [Fact]
    public void Letters_digits_and_spaces_go_in()
    {
        Assert.Equal("Aramil 2", Typing(TextEntryRules.Name, "Aramil 2").Text);
    }

    [Fact]
    public void A_name_takes_punctuation_and_a_password_does_not()
    {
        // KC_PUNCTUATION is in the name screen's accepted set and not the password screen's.
        Assert.Equal("O'Hara", Typing(TextEntryRules.Name, "O'Hara").Text);
        Assert.Equal("OHara", Typing(TextEntryRules.Password, "O'Hara").Text);
    }

    [Fact]
    public void A_name_cannot_start_with_a_space()
    {
        var entry = new TextEntry(TextEntryRules.Name);

        Assert.False(entry.Type(' '));
        Assert.Empty(entry.Text);

        entry.Type('A');
        Assert.True(entry.Type(' '));
        Assert.Equal("A ", entry.Text);
    }

    [Fact]
    public void A_password_may_start_with_a_space()
    {
        Assert.Equal(" open", Typing(TextEntryRules.Password, " open").Text);
    }

    [Fact]
    public void A_name_refuses_the_two_characters_that_would_break_its_file()
    {
        // A character is saved as <name>.chr, so ? and / would make a file name that cannot be
        // written or that matches the wrong things. The reference refuses both with no comment.
        Assert.Equal("Whoare", Typing(TextEntryRules.Name, "Who?are/").Text);
    }

    [Fact]
    public void The_two_screens_refuse_a_slash_for_different_reasons()
    {
        // The name screen takes punctuation and then singles out ? and / by hand; the password
        // screen takes no punctuation at all, so / never reaches a refusal list. Same outcome,
        // and the only way to tell them apart is a character the name screen allows.
        Assert.Equal("ab", Typing(TextEntryRules.Name, "a/b").Text);
        Assert.Equal("ab", Typing(TextEntryRules.Password, "a/b").Text);

        Assert.Equal("a-b", Typing(TextEntryRules.Name, "a-b").Text);
        Assert.Equal("ab", Typing(TextEntryRules.Password, "a-b").Text);
    }

    [Fact]
    public void Typing_stops_at_the_length_limit()
    {
        var entry = Typing(TextEntryRules.Name, new string('x', TextEntryRules.Name.MaxLength));

        Assert.False(entry.Type('x'));
        Assert.Equal(TextEntryRules.Name.MaxLength, entry.Text.Length);
    }

    [Fact]
    public void A_control_character_is_not_punctuation()
    {
        var entry = new TextEntry(TextEntryRules.Name);

        Assert.False(entry.Type('\n'));
        Assert.False(entry.Type('\t'));
        Assert.Empty(entry.Text);
    }

    // ---- deleting ------------------------------------------------------------------------------

    [Fact]
    public void Backspace_takes_the_last_character()
    {
        var entry = Typing(TextEntryRules.Name, "Aram");

        Assert.True(entry.Backspace());
        Assert.Equal("Ara", entry.Text);
    }

    [Fact]
    public void Backspace_on_an_empty_line_does_nothing()
    {
        Assert.False(new TextEntry(TextEntryRules.Name).Backspace());
    }

    // ---- checking a password -------------------------------------------------------------------

    [Fact]
    public void An_exact_match_is_the_whole_string()
    {
        Assert.True(Password.Matches("xyzzy", "xyzzy", PasswordMatch.Exact, matchCase: true));
        Assert.False(Password.Matches("xyzzy!", "xyzzy", PasswordMatch.Exact, matchCase: true));
    }

    [Fact]
    public void Case_can_be_made_not_to_matter()
    {
        Assert.False(Password.Matches("XYZZY", "xyzzy", PasswordMatch.Exact, matchCase: true));
        Assert.True(Password.Matches("XYZZY", "xyzzy", PasswordMatch.Exact, matchCase: false));
    }

    [Fact]
    public void The_password_may_be_looked_for_inside_the_answer()
    {
        // For a designer who wants "say the word somewhere in your sentence".
        Assert.True(Password.Matches("I say xyzzy now", "xyzzy",
                                     PasswordMatch.PasswordInTyped, matchCase: true));
        Assert.False(Password.Matches("nothing", "xyzzy",
                                      PasswordMatch.PasswordInTyped, matchCase: true));
    }

    [Fact]
    public void The_answer_may_be_looked_for_inside_the_password()
    {
        Assert.True(Password.Matches("xyz", "xyzzy",
                                     PasswordMatch.TypedInPassword, matchCase: true));
    }

    [Fact]
    public void An_empty_answer_passes_the_typed_in_password_check()
    {
        // strstr(password, "") returns the password rather than null, so pressing Return without
        // typing anything succeeds -- for the very mode a designer would reach for to accept a
        // partial answer. The other two reject it.
        Assert.True(Password.Matches("", "xyzzy", PasswordMatch.TypedInPassword, matchCase: true));
        Assert.False(Password.Matches("", "xyzzy", PasswordMatch.Exact, matchCase: true));
        Assert.False(Password.Matches("", "xyzzy", PasswordMatch.PasswordInTyped,
                                      matchCase: true));
    }

    [Fact]
    public void An_unknown_match_mode_is_treated_as_exact()
    {
        // The reference puts the exact case under `default` rather than its own label, so a
        // corrupt value falls into the strictest behaviour rather than the loosest.
        Assert.True(Password.Matches("xyzzy", "xyzzy", (PasswordMatch)99, matchCase: true));
        Assert.False(Password.Matches("xyz", "xyzzy", (PasswordMatch)99, matchCase: true));
    }
}
