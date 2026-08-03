namespace UAFcore;

/// <summary>
/// What one text-entry screen will accept.
/// </summary>
/// <param name="MaxLength">How many characters fit.</param>
/// <param name="AllowPunctuation">
/// Whether anything beyond letters, digits and space is taken. The two screens differ here: a
/// character's name takes punctuation (<c>KC_PUNCTUATION</c>, <c>RunEvent.cpp:3691</c>), a
/// password does not (<c>:13118</c>).
/// </param>
/// <param name="AllowLeadingSpace">
/// Whether a space may be the first character. The name screen refuses one outright; the password
/// screen has no such rule.
/// </param>
/// <param name="Refused">
/// Characters this screen will not take at any position. The name screen refuses <c>?</c> and
/// <c>/</c> with no comment — a character is saved as <c>&lt;name&gt;.chr</c>, so both would make
/// a file name that cannot be written or that matches the wrong things.
/// </param>
public sealed record TextEntryRules(int MaxLength, bool AllowPunctuation = true,
                                    bool AllowLeadingSpace = true, string Refused = "")
{
    /// <summary><c>MAX_CHAR_NAME</c> (<c>Char.h:32</c>) — and the file-name refusals.</summary>
    public static TextEntryRules Name { get; } =
        new(30, AllowPunctuation: true, AllowLeadingSpace: false, Refused: "?/");

    /// <summary><c>MAX_PSWD_TEXT</c>, which is <c>MAX_BUTTON_TEXT</c> (<c>GameEvent.h:49</c>).</summary>
    public static TextEntryRules Password { get; } = new(50, AllowPunctuation: false);
}

/// <summary>
/// One line of typed text (<c>GETCHARNAME_MENU_DATA</c> and <c>PASSWORD_DATA</c>'s
/// <c>TASK_PasswordGet</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two screens, one behaviour, different rules.</b> Both accumulate characters, both delete on
/// <b>Backspace or Left</b>, and both commit on Return — but they disagree about punctuation,
/// leading spaces, refused characters and length. The behaviour is shared here and the
/// disagreements are a <see cref="TextEntryRules"/>, rather than two nearly-identical screens
/// that drift.
/// </para>
/// <para>
/// <b>Left deletes; it does not move a cursor.</b> There is no cursor — the reference draws the
/// text as menu items and has nowhere to put one — so a player who expects to move back and
/// insert instead erases.
/// </para>
/// </remarks>
public sealed class TextEntry(TextEntryRules rules)
{
    private readonly TextEntryRules rules =
        rules ?? throw new ArgumentNullException(nameof(rules));

    /// <summary>What has been typed.</summary>
    public string Text { get; private set; } = string.Empty;

    public TextEntryRules Rules => rules;

    /// <summary>Takes one typed character, returning whether it was accepted.</summary>
    public bool Type(char c)
    {
        if (Text.Length >= rules.MaxLength)
        {
            return false;
        }

        if (rules.Refused.Contains(c, StringComparison.Ordinal))
        {
            return false;
        }

        bool ordinary = char.IsLetterOrDigit(c) || c == ' ';
        if (!ordinary && !rules.AllowPunctuation)
        {
            return false;
        }

        // A control character is not punctuation, whatever the screen allows.
        if (!ordinary && char.IsControl(c))
        {
            return false;
        }

        if (c == ' ' && Text.Length == 0 && !rules.AllowLeadingSpace)
        {
            return false;
        }

        Text += c;
        return true;
    }

    /// <summary>Deletes the last character, if there is one.</summary>
    public bool Backspace()
    {
        if (Text.Length == 0)
        {
            return false;
        }

        Text = Text[..^1];
        return true;
    }

    /// <summary>Empties the line.</summary>
    public void Clear() => Text = string.Empty;
}

/// <summary>How a typed answer is compared with the password (<c>passwordMatchType</c>).</summary>
public enum PasswordMatch
{
    /// <summary>The two are the same string.</summary>
    Exact = 0,

    /// <summary>The password appears somewhere in what was typed.</summary>
    PasswordInTyped = 1,

    /// <summary>What was typed appears somewhere in the password.</summary>
    TypedInPassword = 2,
}

/// <summary>
/// Checking a typed answer against an <c>ENTER_PASSWORD</c> event
/// (<c>PASSWORD_DATA::PasswordMatches</c>, <c>GameEvent.cpp:7384</c>).
/// </summary>
public static class Password
{
    /// <summary>The event attribute holding the match mode (<c>PreSerialize</c>).</summary>
    public const string MatchCriteriaAttribute = "MtchCri";

    /// <summary>The event attribute holding whether case matters.</summary>
    public const string MatchCaseAttribute = "MtchCse";

    /// <summary>
    /// Whether a typed answer passes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An empty answer passes a <see cref="PasswordMatch.TypedInPassword"/> check.</b>
    /// <c>strstr(password, "")</c> returns the password rather than null, so pressing Return
    /// without typing anything succeeds — for the mode a designer would reach for to accept a
    /// partial answer. The other two modes reject it.
    /// </para>
    /// <para>
    /// <b>Anything that is not one of the three modes is treated as exact.</b> The reference's
    /// <c>switch</c> has the exact case under <c>default</c> rather than its own label, so a
    /// corrupt or future value falls into the strictest behaviour rather than the loosest.
    /// </para>
    /// </remarks>
    public static bool Matches(string typed, string password, PasswordMatch mode, bool matchCase)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(password);

        var how = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return mode switch
        {
            PasswordMatch.PasswordInTyped => typed.Contains(password, how),
            PasswordMatch.TypedInPassword => password.Contains(typed, how),
            _ => string.Equals(typed, password, how),
        };
    }
}
