using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers <c>ENTER_PASSWORD</c> on screen — the first event type in the port that takes typed
/// text rather than a menu choice.
/// </summary>
public class EventPasswordTests
{
    private const uint Key = 0xFF000000;

    private static readonly TextBoxMetrics Box = new(18, 328, 400, 96, 6);

    private static readonly MenuAnchors Anchors =
        new((16, 460), (200, 200), (20, 328), (16, 460));

    private static BitmapFont Font()
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (10, 16));

        var glyphs = FontAtlas.Layout(extents, FontAtlas.DefaultSheetWidth, out int sheetHeight);
        var sheet = new Surface(FontAtlas.DefaultSheetWidth, sheetHeight, SurfaceKind.Font);
        sheet.Fill(Key);
        sheet.ColorKey = Key;

        return new BitmapFont(new FontAtlas(sheet, glyphs));
    }

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static readonly TransferData NoTransfer =
        new(0, 0, 0, 0, 0, 0);

    private static PasswordEvent Password(string password, int tries = 3,
                                          PasswordMatch mode = PasswordMatch.Exact,
                                          bool matchCase = true,
                                          uint success = 11, uint fail = 22)
    {
        List<AslEntry> attributes =
        [
            new(UAFcore.Password.MatchCriteriaAttribute, 0, ((int)mode).ToString()),
            new(UAFcore.Password.MatchCaseAttribute, 0, matchCase ? "1" : "0"),
        ];

        var basis = new GameEventBase(
            new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
            NoPic, NoPic, (int)EventType.EnterPassword, 1, 0, 0, 0, 0,
            "SPEAK FRIEND AND ENTER", "", "", attributes);

        return new PasswordEvent(basis, tries, success, fail, 0, 0, password,
                                 NoTransfer, NoTransfer);
    }

    private static EventRunner Started(PasswordEvent password)
    {
        var runner = new EventRunner { IsValidEvent = _ => true };
        runner.Begin(password, Font(), Box, Anchors);
        return runner;
    }

    private static void Type(EventRunner runner, string text)
    {
        foreach (char c in text)
        {
            runner.Handle(InputEvent.Text(c));
        }
    }

    private static EventStep Enter(EventRunner runner) =>
        runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

    [Fact]
    public void The_screen_shows_the_question_and_takes_typing()
    {
        var runner = Started(Password("xyzzy"));

        Assert.NotNull(runner.Typing);
        Assert.Contains("SPEAK FRIEND", BitmapFont.Decode(runner.Text.Lines[0].Text));

        Type(runner, "xyz");

        Assert.Equal("xyz", runner.Typing!.Text);
    }

    [Fact]
    public void A_password_screen_has_no_menu_to_escape_to()
    {
        // Every other event answers with a menu; this one takes raw characters until Return, so
        // there is no way out but to answer.
        var runner = Started(Password("xyzzy"));

        Assert.Equal(0, runner.Menu.Count);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Escape));

        Assert.True(runner.IsActive);
        Assert.NotNull(runner.Typing);
    }

    [Fact]
    public void Backspace_and_left_both_delete()
    {
        var runner = Started(Password("xyzzy"));
        Type(runner, "abcd");

        runner.Handle(InputEvent.KeyDown(VirtualKey.Backspace));
        Assert.Equal("abc", runner.Typing!.Text);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Left));
        Assert.Equal("ab", runner.Typing.Text);
    }

    [Fact]
    public void The_right_answer_takes_the_success_chain()
    {
        var runner = Started(Password("xyzzy"));
        Type(runner, "xyzzy");

        var step = Enter(runner);

        Assert.Equal(11u, step.ChainTo);
        Assert.Null(runner.Typing);
    }

    [Fact]
    public void A_wrong_answer_that_is_not_the_last_says_so_and_stays()
    {
        // Only the try that exhausts nbrTries takes the failure chain, so this is a retry loop
        // with a counter rather than a single question.
        var runner = Started(Password("xyzzy", tries: 3));
        Type(runner, "nope");

        var step = Enter(runner);

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.Equal(2, runner.TriesLeft);
        Assert.Empty(runner.Typing!.Text);
        Assert.Contains("not the correct answer", BitmapFont.Decode(runner.Text.Lines[0].Text));
    }

    [Fact]
    public void The_last_wrong_answer_takes_the_failure_chain()
    {
        var runner = Started(Password("xyzzy", tries: 2));

        Type(runner, "nope");
        Enter(runner);

        Type(runner, "still nope");
        var step = Enter(runner);

        Assert.Equal(22u, step.ChainTo);
        Assert.Null(runner.Typing);
    }

    [Fact]
    public void A_design_asking_for_no_tries_still_gets_one()
    {
        // currTry is compared after the first answer, so nbrTries of 0 means one attempt.
        var runner = Started(Password("xyzzy", tries: 0));
        Type(runner, "nope");

        Assert.Equal(22u, Enter(runner).ChainTo);
    }

    [Fact]
    public void The_match_mode_and_case_come_off_the_events_attributes()
    {
        // MtchCri and MtchCse are written as ASL entries rather than fields (PreSerialize), so a
        // reader that only took the record's members would lose both.
        var loose = Started(Password("xyzzy", mode: PasswordMatch.PasswordInTyped,
                                     matchCase: false));
        Type(loose, "I SAY XYZZY NOW");

        Assert.Equal(11u, Enter(loose).ChainTo);

        var strict = Started(Password("xyzzy", mode: PasswordMatch.Exact, matchCase: true));
        Type(strict, "XYZZY");

        Assert.Equal(EventStepKind.Running, Enter(strict).Kind);
    }

    [Fact]
    public void The_typed_line_does_not_leak_into_the_next_event()
    {
        var runner = Started(Password("xyzzy"));
        Type(runner, "xyz");

        runner.Begin(Password("other"), Font(), Box, Anchors);

        Assert.Empty(runner.Typing!.Text);
        Assert.Equal(3, runner.TriesLeft);
    }
}
