using UAF.Common;
using UAF.Data;
using UAF.Scripting;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The design's veto over a successful <c>WHO_TRIES</c> attempt, and the self-delimiting list
/// convention it is encoded in.
/// </summary>
/// <remarks>
/// Exercised by real data: <c>SomethingWild</c> authors <c>$EVENT_WhoTries_Attempt</c>.
/// </remarks>
public class WhoTriesVetoTests
{
    // ---- the self-delimiting convention -----------------------------------------------------------

    [Fact]
    public void The_first_character_of_a_value_is_its_delimiter()
    {
        // There is no fixed separator and no escaping, which is what lets one list nest inside
        // another: the outer picks a character the inner does not use.
        Assert.Equal(["a", "b", "c"], Substrings.Fields("/a/b/c/"));
        Assert.Equal(["a/b", "c/d"], Substrings.Fields("|a/b|c/d|"));
    }

    [Fact]
    public void The_head_loses_its_delimiter_and_the_tail_keeps_its_own()
    {
        // That is what lets the tail be split again with no bookkeeping -- and why a caller
        // wanting the tail's TEXT has to drop the character itself, as the reference does.
        Assert.True(Substrings.HeadAndTail("/name/rest/more", out string head, out string tail));

        Assert.Equal("name", head);
        Assert.Equal("/rest/more", tail);
    }

    [Fact]
    public void A_string_too_short_to_hold_a_delimiter_and_a_character_splits_into_nothing()
    {
        Assert.False(Substrings.HeadAndTail("/", out _, out _));
        Assert.False(Substrings.HeadAndTail("", out _, out _));
    }

    [Fact]
    public void A_list_ending_in_its_delimiter_yields_no_empty_final_field()
    {
        // The guard is `column >= length - 1`: a single trailing character cannot start a field.
        Assert.Equal(["a"], Substrings.Fields("/a/"));
        Assert.Empty(Substrings.Fields("/"));
    }

    [Fact]
    public void An_unterminated_final_field_is_still_read()
    {
        Assert.Equal(["a", "b"], Substrings.Fields("/a/b"));
    }

    // ---- the veto ---------------------------------------------------------------------------------

    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static WhoTriesEvent Trial(string? attempt = null) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.WhoTries, 1, 0, 0, 0, 0,
                              "", "", "",
                              attempt is null ? [] : [new AslEntry("Attempt", 0, attempt)]),
            0, 0, [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0, 0, 0], 0, 0, 0, 0, 0, 0, 0, 0,
            new TransferData(0, 0, 0, 0, 0, 0), new TransferData(0, 0, 0, 0, 0, 0));

    private static GlobalScripts Library(params (string Name, string Source)[] scripts) =>
        new(SpecialAbilitiesFile.Parse(
        [
            "\\(BEGIN)",
            $"name = {WhoTriesVeto.AbilityName}",
            .. scripts.Select(s => $"[{s.Name}] = {s.Source}"),
            "\\(END)",
        ]));

    [Fact]
    public void A_failed_attempt_never_reaches_a_script()
    {
        // The whole block is inside `if (!failed)`, so this is a veto and never an override -- a
        // design cannot use it to implement an ability the engine does not know about.
        var library = Library(("s", "$RETURN \"N\";"));

        Assert.False(WhoTriesVeto.Vetoes(Trial("|/s/Strength/12|"), succeeded: false,
                                         library, new GpdlUnhostedEnvironment()));
    }

    [Fact]
    public void An_event_with_no_attempt_attribute_vetoes_nothing()
    {
        Assert.False(WhoTriesVeto.Vetoes(Trial(), succeeded: true,
                                         Library(), new GpdlUnhostedEnvironment()));
    }

    [Fact]
    public void A_script_answering_n_takes_the_success_away()
    {
        var library = Library(("s", "$RETURN \"N\";"));

        Assert.True(WhoTriesVeto.Vetoes(Trial("|/s/Strength/12|"), succeeded: true,
                                        library, new GpdlUnhostedEnvironment()));
    }

    [Fact]
    public void Any_other_answer_leaves_the_success_alone()
    {
        foreach (string answer in new[] { "Y", "", "n", "No" })
        {
            var library = Library(("s", $"$RETURN \"{answer}\";"));

            Assert.False(WhoTriesVeto.Vetoes(Trial("|/s/Strength/12|"), succeeded: true,
                                             library, new GpdlUnhostedEnvironment()));
        }
    }

    [Fact]
    public void The_script_is_handed_the_ability_and_the_need()
    {
        // Slot 5 is the ability being judged and slot 6 the requirement -- which is what
        // SomethingWild's own $EVENT_WhoTries_Attempt reads.
        var library = Library(("s", "$RETURN $GET_HOOK_PARAM(5);"));
        var host = new GpdlUnhostedEnvironment();

        WhoTriesVeto.Vetoes(Trial("|/s/Strength/12|"), succeeded: true, library, host);

        Assert.Equal("Strength", host.GetHookParam(WhoTriesVeto.AbilitySlot));
        Assert.Equal("12", host.GetHookParam(WhoTriesVeto.NeedSlot));
    }

    [Fact]
    public void The_last_script_to_write_slot_zero_decides()
    {
        // All the scripts share one hook-parameter block and slot 0 is read once at the end, so a
        // later script writing anything else CLEARS an earlier veto. They are not independent
        // votes.
        var library = Library(("veto", "$RETURN \"N\";"), ("allow", "$RETURN \"Y\";"));

        Assert.True(WhoTriesVeto.Vetoes(Trial("|/allow/Str/1|/veto/Str/1|"), true,
                                        library, new GpdlUnhostedEnvironment()));

        Assert.False(WhoTriesVeto.Vetoes(Trial("|/veto/Str/1|/allow/Str/1|"), true,
                                         library, new GpdlUnhostedEnvironment()));
    }

    [Fact]
    public void A_named_script_the_design_does_not_have_changes_nothing()
    {
        Assert.False(WhoTriesVeto.Vetoes(Trial("|/missing/Str/1|"), succeeded: true,
                                         Library(), new GpdlUnhostedEnvironment()));
    }

    [Fact]
    public void Slot_zero_is_cleared_before_the_scripts_run()
    {
        // Otherwise a veto left over from an earlier event would carry into this one.
        var host = new GpdlUnhostedEnvironment();
        host.SetHookParam(GpdlHookParameters.ResultSlot, "N");

        Assert.False(WhoTriesVeto.Vetoes(Trial("|/s/Str/1|"), succeeded: true,
                                         Library(("s", "$RETURN \"\";")), host));
    }
}
