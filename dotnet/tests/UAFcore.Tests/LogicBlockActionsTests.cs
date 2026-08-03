using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the write half of a logic block — the result gate, the key/value grammar and the twelve
/// action types.
/// </summary>
public class LogicBlockActionsTests
{
    private sealed class Host : ILogicBlockActionHost
    {
        public int PartySize { get; init; } = 3;
        public int ActiveCharacter { get; init; } = 1;
        public int Facing => 0;
        public int CurrentLevel { get; init; } = 7;

        public string[] Names { get; init; } = ["Aramil", "Kloppin", "Sherlas"];

        public Dictionary<(LogicAslScope, int, string), string> Attributes { get; } = [];
        public Dictionary<(int, string), string> LevelAttributes { get; } = [];
        public Dictionary<string, int> Quests { get; } = [];
        public Dictionary<int, int> IconIndexes { get; } = [];

        public string CharacterName(int index) =>
            index >= 0 && index < Names.Length ? Names[index] : string.Empty;

        public string Attribute(LogicAslScope scope, int character, string key) =>
            Attributes.GetValueOrDefault((scope, character, key), string.Empty);

        public string LevelAttribute(int level, string key) =>
            LevelAttributes.GetValueOrDefault((level, key), string.Empty);

        public int QuestStage(string quest) => Quests.GetValueOrDefault(quest);

        public string ItemList() => string.Empty;
        public string NpcList() => string.Empty;
        public string CharInfo() => string.Empty;
        public string GrepCapture(int group) => string.Empty;

        public void SetAttribute(LogicAslScope scope, int character, string key, string value) =>
            Attributes[(scope, character, key)] = value;

        public void RemoveAttribute(LogicAslScope scope, int character, string key) =>
            Attributes.Remove((scope, character, key));

        public void SetLevelAttribute(int level, string key, string value) =>
            LevelAttributes[(level, key)] = value;

        public void RemoveLevelAttribute(int level, string key) =>
            LevelAttributes.Remove((level, key));

        public void SetQuestStage(string quest, int stage) => Quests[quest] = stage;

        public void SetIconIndex(int character, int iconIndex) =>
            IconIndexes[character] = iconIndex;
    }

    private static readonly string[] Slots =
        ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l"];

    private static bool Run(LogicAction type, string parameter, Host host,
                            LogicActionWhen when = LogicActionWhen.Always, bool result = true,
                            Action<string>? runScript = null) =>
        LogicBlockActions.Run(when, result, type, parameter, Slots, host, runScript);

    // ---- the result gate -----------------------------------------------------------------------

    [Theory]
    [InlineData(LogicActionWhen.IfTrue, true, true)]
    [InlineData(LogicActionWhen.IfTrue, false, false)]
    [InlineData(LogicActionWhen.IfFalse, true, false)]
    [InlineData(LogicActionWhen.IfFalse, false, true)]
    [InlineData(LogicActionWhen.Always, true, true)]
    [InlineData(LogicActionWhen.Always, false, true)]
    public void An_action_runs_only_when_the_result_says_it_should(
        LogicActionWhen when, bool result, bool expected)
    {
        Assert.Equal(expected, LogicBlockActions.Runs(when, result));
    }

    [Fact]
    public void A_held_back_action_writes_nothing()
    {
        var host = new Host();
        Assert.False(Run(LogicAction.SetGlobalAsl, "k=v", host,
                         LogicActionWhen.IfTrue, result: false));

        Assert.Empty(host.Attributes);
    }

    // ---- SplitKeyValue -------------------------------------------------------------------------

    [Fact]
    public void A_parameter_splits_at_its_first_token()
    {
        Assert.Equal(("k", "v"), LogicBlockActions.SplitKeyValue("k=v"));

        // Only the first, so a value may contain more of them.
        Assert.Equal(("k", "a=b"), LogicBlockActions.SplitKeyValue("k=a=b"));
    }

    [Fact]
    public void A_parameter_with_no_token_is_all_key_and_an_empty_value()
    {
        // Meaningful rather than an error: an attribute with an empty value is how a design
        // creates a bare flag.
        Assert.Equal(("flag", ""), LogicBlockActions.SplitKeyValue("flag"));
    }

    // ---- the attribute actions -----------------------------------------------------------------

    [Fact]
    public void The_three_simple_stores_are_written_and_cleared()
    {
        var host = new Host();

        Run(LogicAction.SetGlobalAsl, "g=1", host);
        Run(LogicAction.TempAsl, "t=2", host);
        Run(LogicAction.SetPartyAsl, "p=3", host);

        Assert.Equal("1", host.Attribute(LogicAslScope.Global, 0, "g"));
        Assert.Equal("2", host.Attribute(LogicAslScope.Temp, 0, "t"));
        Assert.Equal("3", host.Attribute(LogicAslScope.Party, 0, "p"));

        Run(LogicAction.RemoveGlobalAsl, "g", host);
        Run(LogicAction.RemovePartyAsl, "p", host);

        Assert.Equal(string.Empty, host.Attribute(LogicAslScope.Global, 0, "g"));
        Assert.Equal(string.Empty, host.Attribute(LogicAslScope.Party, 0, "p"));

        // There is no removeTempASL -- the enum has no such action.
        Assert.Equal("2", host.Attribute(LogicAslScope.Temp, 0, "t"));
    }

    [Fact]
    public void A_parameter_is_substituted_before_it_is_split()
    {
        var host = new Host();
        Run(LogicAction.SetGlobalAsl, "&A=&B", host);

        Assert.Equal("b", host.Attribute(LogicAslScope.Global, 0, "a"));
    }

    [Fact]
    public void A_level_action_writes_the_level_its_key_names()
    {
        var host = new Host();

        Run(LogicAction.SetLevelAsl, "/3/k=third", host);
        Run(LogicAction.SetLevelAsl, "k=current", host);

        Assert.Equal("third", host.LevelAttribute(3, "k"));
        Assert.Equal("current", host.LevelAttribute(7, "k"));

        Run(LogicAction.RemoveLevelAsl, "/3/k", host);
        Assert.Equal(string.Empty, host.LevelAttribute(3, "k"));
    }

    [Fact]
    public void A_wall_override_key_says_what_it_needs()
    {
        // "$Wall," reroutes into Convert$Wall, which sets a per-cell WALL_OVERRIDE_USER entry --
        // tables this port reads but does not thread through the viewport or the combat map.
        var thrown = Assert.Throws<NotSupportedException>(
            () => Run(LogicAction.SetLevelAsl, "/2/$Wall,1,2=3", new Host()));

        Assert.Contains("Convert$Wall", thrown.Message);
    }

    [Fact]
    public void A_character_action_writes_the_character_its_selector_names()
    {
        var host = new Host();

        Run(LogicAction.SetCharAsl, "(Kloppin)k=v", host);
        Assert.Equal("v", host.Attribute(LogicAslScope.Character, 1, "k"));

        // No selector at all means the active character, which is also 1 here -- so a different
        // key keeps the two claims apart.
        Run(LogicAction.SetCharAsl, "other=w", host);
        Assert.Equal("w", host.Attribute(LogicAslScope.Character, 1, "other"));
    }

    [Fact]
    public void The_star_selector_writes_every_character()
    {
        var host = new Host();
        Run(LogicAction.SetCharAsl, "(*)k=v", host);

        for (int i = 0; i < host.PartySize; i++)
        {
            Assert.Equal("v", host.Attribute(LogicAslScope.Character, i, "k"));
        }
    }

    [Fact]
    public void A_character_action_with_a_bogus_selector_writes_nothing()
    {
        // The reference logs "Bogus character identifier in Set Character ASL" and stops -- it
        // does NOT fall back to the active character, which the read side effectively does.
        var host = new Host();
        Run(LogicAction.SetCharAsl, "(Nobody)k=v", host);

        Assert.Empty(host.Attributes);
    }

    // ---- the rest ------------------------------------------------------------------------------

    [Fact]
    public void A_quest_action_parses_its_stage_as_a_number()
    {
        var host = new Host();

        Run(LogicAction.SetQuestStage, "Rescue=4", host);
        Assert.Equal(4, host.QuestStage("Rescue"));

        // atoi semantics: no digits is zero, not an error.
        Run(LogicAction.SetQuestStage, "Rescue=none", host);
        Assert.Equal(0, host.QuestStage("Rescue"));
    }

    [Fact]
    public void An_icon_action_finds_its_character_by_name_and_floors_the_index_at_one()
    {
        var host = new Host();

        Run(LogicAction.SetIconIndexByName, "Sherlas=4", host);
        Assert.Equal(4, host.IconIndexes[2]);

        // Index 0 is not a usable icon, so the reference clamps.
        Run(LogicAction.SetIconIndexByName, "Sherlas=0", host);
        Assert.Equal(1, host.IconIndexes[2]);
    }

    [Fact]
    public void An_icon_action_matches_its_name_case_sensitively()
    {
        // Unlike the character selector's name form, which uses CompareNoCase.
        var host = new Host();
        Run(LogicAction.SetIconIndexByName, "sherlas=4", host);

        Assert.Empty(host.IconIndexes);
    }

    [Fact]
    public void An_icon_action_does_not_substitute_its_parameter()
    {
        // The only action that skips LBsubst (RunEvent.cpp:14262). Almost certainly an oversight,
        // and reproduced -- a design that worked around it would break if it were "fixed".
        var host = new Host { Names = ["a", "Kloppin", "Sherlas"] };

        // &A would substitute to "a" and match the first character, if it were substituted.
        Run(LogicAction.SetIconIndexByName, "&A=4", host);

        Assert.Empty(host.IconIndexes);
    }

    [Fact]
    public void The_nothing_action_and_a_bogus_one_both_write_nothing_and_still_count_as_run()
    {
        var host = new Host();

        Assert.True(Run(LogicAction.Nothing, "k=v", host));
        Assert.True(Run(LogicAction.NotImplemented, "k=v", host));
        Assert.Empty(host.Attributes);
    }

    [Fact]
    public void A_gpdl_action_runs_its_program_with_no_arguments()
    {
        // Unlike the input side, which passes the six working slots -- so an action script cannot
        // see the terminals an input script can.
        string? ran = null;
        Run(LogicAction.BinaryGpdl, "program", new Host(), runScript: p => ran = p);

        Assert.Equal("program", ran);
    }

    [Fact]
    public void A_gpdl_action_with_no_runner_says_what_it_needs()
    {
        var thrown = Assert.Throws<NotSupportedException>(
            () => Run(LogicAction.SourceGpdl, "p", new Host()));

        Assert.Contains("runScript", thrown.Message);
    }
}
