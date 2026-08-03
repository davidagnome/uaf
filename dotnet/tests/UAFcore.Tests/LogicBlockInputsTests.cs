using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the input half of a logic block — the two string grammars and the sixteen terminals.
/// </summary>
/// <remarks>
/// The gate network was ported first and left unwired, because all-false inputs would have made
/// every block take a branch rather than fail visibly. This is what wires it.
/// </remarks>
public class LogicBlockInputsTests
{
    private sealed class Host : ILogicBlockHost
    {
        public int PartySize { get; init; } = 3;
        public int ActiveCharacter { get; init; } = 1;
        public int Facing { get; init; } = 2;
        public int CurrentLevel { get; init; } = 7;

        public string[] Names { get; init; } = ["Aramil", "Kloppin", "Sherlas"];
        public Dictionary<(LogicAslScope, int, string), string> Attributes { get; init; } = [];
        public Dictionary<(int, string), string> LevelAttributes { get; init; } = [];
        public Dictionary<string, int> Quests { get; init; } = [];
        public string[] Captures { get; init; } = [];

        public string CharacterName(int index) =>
            index >= 0 && index < Names.Length ? Names[index] : string.Empty;

        public string Attribute(LogicAslScope scope, int character, string key) =>
            Attributes.GetValueOrDefault((scope, character, key), string.Empty);

        public string LevelAttribute(int level, string key) =>
            LevelAttributes.GetValueOrDefault((level, key), string.Empty);

        public int QuestStage(string quest) => Quests.GetValueOrDefault(quest);

        public string ItemList() => "/sword//shield/";

        public string NpcList() => "/Kloppin/1/";

        public string CharInfo() => "/Aramil/";

        public string GrepCapture(int group) =>
            group >= 0 && group < Captures.Length ? Captures[group] : string.Empty;
    }

    private static readonly string[] Slots =
        ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l"];

    // ---- LBsubst -------------------------------------------------------------------------------

    [Fact]
    public void A_parameter_with_no_ampersand_is_returned_unchanged()
    {
        Assert.Equal("plain", LogicBlockInputs.Substitute("plain", Slots));
    }

    [Fact]
    public void Slot_references_are_replaced_by_their_values()
    {
        // This is a logic block's only means of composition: one terminal naming another's result.
        Assert.Equal("xay", LogicBlockInputs.Substitute("x&Ay", Slots));
        Assert.Equal("al", LogicBlockInputs.Substitute("&A&L", Slots));
    }

    [Fact]
    public void An_ampersand_that_names_no_slot_is_kept_where_the_reference_would_hang()
    {
        // THE reference defect in this function: its loop advances only in the else of
        // `if (p[col]=='&')` and in the substitution branch, so an & whose successor is outside
        // A..L spins forever on the same character. "Bell & Dragon" in a logic-block parameter
        // locks the original up. This port advances and keeps the character, which is the only
        // non-hanging reading -- a deliberate divergence, and the sole one here.
        Assert.Equal("Bell & Dragon", LogicBlockInputs.Substitute("Bell & Dragon", Slots));
        Assert.Equal("&M", LogicBlockInputs.Substitute("&M", Slots));

        // And it keeps substituting afterwards rather than giving up on the parameter.
        Assert.Equal("&za", LogicBlockInputs.Substitute("&z&A", Slots));
    }

    [Fact]
    public void A_trailing_ampersand_survives()
    {
        // The same path: `col < len - 1` fails, so the reference never advances past it either.
        Assert.Equal("x&", LogicBlockInputs.Substitute("x&", Slots));
    }

    // ---- LBseparateCharacter -------------------------------------------------------------------

    [Fact]
    public void No_selector_means_the_active_character()
    {
        string parameter = "Courage";
        Assert.Equal(1, LogicBlockInputs.SeparateCharacter(ref parameter, new Host()));
        Assert.Equal("Courage", parameter);
    }

    [Fact]
    public void The_star_selector_means_every_character()
    {
        string parameter = "(*)Courage";
        Assert.Equal(LogicBlockInputs.AllCharacters,
                     LogicBlockInputs.SeparateCharacter(ref parameter, new Host()));
        Assert.Equal("Courage", parameter);
    }

    [Fact]
    public void The_caret_selector_means_the_active_character_and_leaves_the_key_whole()
    {
        // (^) is the one form that does NOT strip itself -- the reference returns before touching
        // sp, so the key still has the selector on it.
        string parameter = "(^)";
        Assert.Equal(1, LogicBlockInputs.SeparateCharacter(ref parameter, new Host()));
        Assert.Equal("(^)", parameter);
    }

    [Fact]
    public void A_numbered_selector_is_one_based()
    {
        string parameter = "(^2)Courage";
        Assert.Equal(1, LogicBlockInputs.SeparateCharacter(ref parameter, new Host()));
        Assert.Equal("Courage", parameter);
    }

    [Fact]
    public void A_numbered_selector_past_the_party_names_nobody()
    {
        string parameter = "(^9)Courage";
        Assert.Equal(LogicBlockInputs.NoCharacter,
                     LogicBlockInputs.SeparateCharacter(ref parameter, new Host()));
    }

    [Fact]
    public void A_named_selector_matches_case_insensitively()
    {
        string parameter = "(kloppin)Courage";
        Assert.Equal(1, LogicBlockInputs.SeparateCharacter(ref parameter, new Host()));
        Assert.Equal("Courage", parameter);
    }

    [Fact]
    public void A_named_selector_matching_nobody_yields_no_character()
    {
        string parameter = "(Nobody)Courage";
        Assert.Equal(LogicBlockInputs.NoCharacter,
                     LogicBlockInputs.SeparateCharacter(ref parameter, new Host()));
    }

    // ---- SplitLevelKey -------------------------------------------------------------------------

    [Fact]
    public void A_qualified_level_key_names_its_level()
    {
        Assert.Equal((3, "Courage"), LogicBlockInputs.SplitLevelKey("/3/Courage", 7));
    }

    [Fact]
    public void An_unqualified_key_addresses_the_current_level()
    {
        // Not level 0 -- which is what a reading that took the leading digits would give.
        Assert.Equal((7, "Courage"), LogicBlockInputs.SplitLevelKey("Courage", 7));
        Assert.Equal((7, "3Courage"), LogicBlockInputs.SplitLevelKey("3Courage", 7));
    }

    [Fact]
    public void A_slash_with_no_closing_slash_falls_back_to_the_current_level()
    {
        Assert.Equal((7, "/3Courage"), LogicBlockInputs.SplitLevelKey("/3Courage", 7));
    }

    [Fact]
    public void Non_digits_inside_the_level_number_are_skipped_rather_than_terminal()
    {
        // Almost certainly unintended in the reference, and reproduced because a design written
        // against it would break otherwise: the loop continues on a digit, returns on a slash, and
        // does neither for anything else.
        Assert.Equal((12, "Courage"), LogicBlockInputs.SplitLevelKey("/1a2/Courage", 7));
    }

    // ---- the terminals -------------------------------------------------------------------------

    [Fact]
    public void A_literal_terminal_substitutes_its_parameter()
    {
        Assert.Equal("xay",
                     LogicBlockInputs.Read(LogicInput.Literal, "x&Ay", Slots, new Host()));
    }

    [Fact]
    public void The_scalar_terminals_render_as_digits()
    {
        var host = new Host { PartySize = 4, Facing = 2 };

        Assert.Equal("4", LogicBlockInputs.Read(LogicInput.PartySize, "", Slots, host));
        Assert.Equal("2", LogicBlockInputs.Read(LogicInput.DirFacing, "", Slots, host));
    }

    [Fact]
    public void An_empty_party_still_reads_true_because_zero_is_a_non_empty_string()
    {
        // The single most surprising thing about this event: truth is "not empty", so a party size
        // of zero is TRUE. A port that returned a bool here would invert the branch.
        string value = LogicBlockInputs.Read(LogicInput.PartySize, "", Slots,
                                             new Host { PartySize = 0 });

        Assert.Equal("0", value);
        Assert.True(LogicBlock.IsTrue(value));
    }

    [Fact]
    public void An_absent_attribute_reads_empty_and_therefore_false()
    {
        string value = LogicBlockInputs.Read(LogicInput.GlobalAsl, "Missing", Slots, new Host());

        Assert.Equal(string.Empty, value);
        Assert.False(LogicBlock.IsTrue(value));
    }

    [Fact]
    public void Each_attribute_terminal_reads_its_own_store()
    {
        var host = new Host
        {
            Attributes =
            {
                [(LogicAslScope.Global, 0, "k")] = "global",
                [(LogicAslScope.Temp, 0, "k")] = "temp",
                [(LogicAslScope.Party, 0, "k")] = "party",
                [(LogicAslScope.Character, 1, "k")] = "character",
            },
        };

        Assert.Equal("global", LogicBlockInputs.Read(LogicInput.GlobalAsl, "k", Slots, host));
        Assert.Equal("temp", LogicBlockInputs.Read(LogicInput.TempAsl, "k", Slots, host));
        Assert.Equal("party", LogicBlockInputs.Read(LogicInput.PartyAsl, "k", Slots, host));
        Assert.Equal("character", LogicBlockInputs.Read(LogicInput.CharAsl, "k", Slots, host));
    }

    [Fact]
    public void A_character_terminal_with_a_bogus_selector_reads_empty_rather_than_throwing()
    {
        // The reference logs "Bogus character selector in Input" and carries on, so one bad
        // terminal must not make a design unplayable.
        Assert.Equal(string.Empty,
                     LogicBlockInputs.Read(LogicInput.CharAsl, "(Nobody)k", Slots, new Host()));
    }

    [Fact]
    public void A_level_terminal_reads_the_level_its_key_names()
    {
        var host = new Host
        {
            LevelAttributes = { [(3, "k")] = "third", [(7, "k")] = "current" },
        };

        Assert.Equal("third", LogicBlockInputs.Read(LogicInput.LevelAsl, "/3/k", Slots, host));
        Assert.Equal("current", LogicBlockInputs.Read(LogicInput.LevelAsl, "k", Slots, host));
    }

    [Fact]
    public void A_quest_terminal_renders_its_stage_as_digits()
    {
        var host = new Host { Quests = { ["Rescue"] = 4 } };

        Assert.Equal("4", LogicBlockInputs.Read(LogicInput.QuestStage, "Rescue", Slots, host));
        Assert.Equal("0", LogicBlockInputs.Read(LogicInput.QuestStage, "Unknown", Slots, host));
    }

    [Fact]
    public void The_three_list_terminals_pass_the_hosts_text_through()
    {
        var host = new Host();

        Assert.Equal("/sword//shield/", LogicBlockInputs.Read(LogicInput.ItemList, "", Slots, host));
        Assert.Equal("/Kloppin/1/", LogicBlockInputs.Read(LogicInput.NpcList, "", Slots, host));
        Assert.Equal("/Aramil/", LogicBlockInputs.Read(LogicInput.CharInfo, "", Slots, host));
    }

    [Fact]
    public void A_wiggle_terminal_reads_a_capture_group()
    {
        var host = new Host { Captures = ["whole", "first", "second"] };

        Assert.Equal("first", LogicBlockInputs.Read(LogicInput.Wiggle, "1", Slots, host));

        // A non-numeric parameter parses as zero, matching atoi -- and group 0 is the whole match,
        // which is meaningful rather than an error.
        Assert.Equal("whole", LogicBlockInputs.Read(LogicInput.Wiggle, "notanumber", Slots, host));

        // A group the last grep did not produce reads empty.
        Assert.Equal(string.Empty, LogicBlockInputs.Read(LogicInput.Wiggle, "9", Slots, host));
    }

    [Fact]
    public void An_unrecognised_type_reads_empty_rather_than_stopping_the_block()
    {
        // The reference logs "Bogus Logic Input-<letter> Type" and leaves the result untouched, so
        // the terminal reads false and the block still runs. Throwing would make a design with one
        // bad terminal unplayable where the original merely misbehaves.
        Assert.Equal(string.Empty,
                     LogicBlockInputs.Read(LogicInput.NotImplemented, "", Slots, new Host()));
    }

    [Fact]
    public void The_gpdl_terminals_run_the_program_they_are_given()
    {
        string value = LogicBlockInputs.Read(
            LogicInput.BinaryGpdl, "program", Slots, new Host(),
            (program, slots) => $"{program}:{slots.Count}");

        Assert.Equal("program:12", value);
    }

    [Fact]
    public void The_terminals_this_port_cannot_reach_say_what_they_need()
    {
        var withoutScripts = Assert.Throws<NotSupportedException>(
            () => LogicBlockInputs.Read(LogicInput.SourceGpdl, "p", Slots, new Host()));
        Assert.Contains("runScript", withoutScripts.Message);

        var runtimeIf = Assert.Throws<NotSupportedException>(
            () => LogicBlockInputs.Read(LogicInput.RunTimeIf, "p", Slots, new Host()));
        Assert.Contains("GetDataSTRING", runtimeIf.Message);
    }
}
