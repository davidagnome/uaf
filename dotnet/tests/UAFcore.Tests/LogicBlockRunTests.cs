using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers running a whole logic block — inputs, gates, actions and chaining together.
/// </summary>
/// <remarks>
/// The three halves were ported separately and left unwired, because a gate network fed all-false
/// inputs takes a branch rather than failing visibly. These are the tests that could not exist
/// until all three did.
/// </remarks>
public class LogicBlockRunTests
{
    private sealed class Host : ILogicBlockActionHost
    {
        public int PartySize { get; init; } = 2;
        public int ActiveCharacter => 0;
        public int Facing => 1;
        public int CurrentLevel => 4;

        public Dictionary<(LogicAslScope, int, string), string> Attributes { get; } = [];
        public Dictionary<(int, string), string> LevelAttributes { get; } = [];
        public Dictionary<string, int> Quests { get; } = [];

        public string CharacterName(int index) => index == 0 ? "Aramil" : "Kloppin";

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
        public void SetIconIndex(int character, int iconIndex) { }
    }

    private static readonly GameEventBase Base = new(
        new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
        new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        (int)EventType.LogicBlock, 1, 0, 0, 0, 0, "", "", "", []);

    /// <summary>
    /// A block whose every gate passes the top input through, so A reaches the final gate.
    /// </summary>
    private static LogicBlockEvent Block(
        LogicInput inputA = LogicInput.Literal, string paramA = "1",
        LogicAction action1 = LogicAction.Nothing, string actionParam1 = "",
        LogicActionWhen when1 = LogicActionWhen.Always,
        LogicBlockChaining chaining = LogicBlockChaining.Never,
        uint trueChain = 0, uint falseChain = 0,
        byte chainIfTrue = 1, byte chainIfFalse = 1)
    {
        // Gate 'C' takes (side=A, top=B) -- see LogicBlock.Evaluate -- so passing the SIDE through
        // is what carries A forward. Every other gate then passes its top through.
        byte[] gates =
        [
            (byte)LogicGate.Side,   // C: side is A
            (byte)LogicGate.Top,    // E
            (byte)LogicGate.Side,   // H: side is A
            (byte)LogicGate.Top,    // I
            (byte)LogicGate.Top,    // J
            (byte)LogicGate.Top,    // K
            (byte)LogicGate.Top,    // L
        ];

        return new LogicBlockEvent(
            Base, falseChain, trueChain,
            Inputs: [paramA, "", "", "", ""],
            ActionParams: [actionParam1, ""],
            GateTypes: gates,
            InputTypes: [(byte)inputA, 0, 0, 0, 0],
            ActionTypes: [(byte)action1, (byte)LogicAction.Nothing],
            ChainIfFalse: chainIfFalse, ChainIfTrue: chainIfTrue,
            NoChain: (byte)chaining,
            Negations: [0, 0, 0, 0, 0, 0],
            IfTrue: [(byte)when1, (byte)LogicActionWhen.Always],
            Flags: 0, Misc: string.Empty);
    }

    private static LogicBlockOutcome Run(LogicBlockEvent block, Host host,
                                         Func<uint, bool>? isValid = null) =>
        LogicBlockRun.Run(block, host, isValid ?? (_ => true));

    [Fact]
    public void A_literal_input_reaches_the_final_gate()
    {
        var outcome = Run(Block(paramA: "yes"), new Host());

        Assert.Equal(1, outcome.Result);
        Assert.Equal("yes", outcome.Values.Values[11]);
    }

    [Fact]
    public void An_empty_input_makes_the_block_false()
    {
        Assert.Equal(0, Run(Block(paramA: ""), new Host()).Result);
    }

    [Fact]
    public void A_party_size_of_zero_still_makes_the_block_true()
    {
        // Truth is "not empty", so "0" is true. This is the single most surprising thing about a
        // logic block, and it only shows up once the inputs are wired to the gates.
        var outcome = Run(Block(LogicInput.PartySize), new Host { PartySize = 0 });

        Assert.Equal("0", outcome.Values.Values[0]);
        Assert.Equal(1, outcome.Result);
    }

    [Fact]
    public void An_attribute_input_reads_the_hosts_store()
    {
        var host = new Host();
        host.Attributes[(LogicAslScope.Global, 0, "flag")] = "set";

        Assert.Equal(1, Run(Block(LogicInput.GlobalAsl, "flag"), host).Result);
        Assert.Equal(0, Run(Block(LogicInput.GlobalAsl, "other"), host).Result);
    }

    [Fact]
    public void An_action_runs_after_the_network_and_sees_its_slots()
    {
        // The point of ordering the actions after the gates: a design writes a COMPUTED value.
        var host = new Host();
        Run(Block(paramA: "computed",
                  action1: LogicAction.SetGlobalAsl, actionParam1: "result=&A"), host);

        Assert.Equal("computed", host.Attribute(LogicAslScope.Global, 0, "result"));
    }

    [Fact]
    public void An_action_gated_on_the_result_is_held_back_when_it_does_not_match()
    {
        var host = new Host();

        Run(Block(paramA: "", action1: LogicAction.SetGlobalAsl,
                  actionParam1: "k=v", when1: LogicActionWhen.IfTrue), host);
        Assert.Empty(host.Attributes);

        Run(Block(paramA: "", action1: LogicAction.SetGlobalAsl,
                  actionParam1: "k=v", when1: LogicActionWhen.IfFalse), host);
        Assert.Equal("v", host.Attribute(LogicAslScope.Global, 0, "k"));
    }

    // ---- chaining ------------------------------------------------------------------------------

    [Fact]
    public void A_block_that_never_chains_stops_the_run()
    {
        var outcome = Run(Block(chaining: LogicBlockChaining.Never, trueChain: 9), new Host());

        Assert.Null(outcome.ChainTo);
        Assert.False(outcome.ChainsNormally);
    }

    [Fact]
    public void A_block_that_always_chains_defers_to_the_events_own_chain()
    {
        var outcome = Run(Block(chaining: LogicBlockChaining.Always, trueChain: 9), new Host());

        Assert.True(outcome.ChainsNormally);
        Assert.Null(outcome.ChainTo);
    }

    [Fact]
    public void A_block_that_chains_on_its_result_picks_the_matching_target()
    {
        var onTrue = Run(Block(paramA: "1", chaining: LogicBlockChaining.OnResult,
                               trueChain: 9, falseChain: 8), new Host());
        Assert.Equal(9u, onTrue.ChainTo);

        var onFalse = Run(Block(paramA: "", chaining: LogicBlockChaining.OnResult,
                                trueChain: 9, falseChain: 8), new Host());
        Assert.Equal(8u, onFalse.ChainTo);
    }

    [Fact]
    public void A_conditional_branch_whose_flag_is_clear_stops_the_run()
    {
        var outcome = Run(Block(paramA: "1", chaining: LogicBlockChaining.OnResult,
                                trueChain: 9, chainIfTrue: 0), new Host());

        // Not a fallback to the ordinary chain -- unlike WHO_TRIES, the run simply ends.
        Assert.Null(outcome.ChainTo);
        Assert.False(outcome.ChainsNormally);
    }

    [Fact]
    public void An_unreachable_target_stops_the_run()
    {
        var outcome = LogicBlockRun.Run(
            Block(paramA: "1", chaining: LogicBlockChaining.OnResult, trueChain: 9),
            new Host(), _ => false);

        Assert.Null(outcome.ChainTo);
    }

    // ---- the runner --------------------------------------------------------------------------

    [Fact]
    public void The_event_runner_finishes_a_logic_block_without_drawing()
    {
        // The only event type with no text, no menu and no keypress: it finishes inside Begin and
        // never reaches Handle.
        var runner = new EventRunner();
        var host = new Host();
        var block = Block(paramA: "1", chaining: LogicBlockChaining.OnResult, trueChain: 9);

        runner.ResolveLogicBlock = b => LogicBlockRun.Run(b, host, _ => true);

        var step = runner.Begin(block, Font(), Box, Anchors);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(9u, step.ChainTo);
        Assert.Equal(1, runner.LastLogicBlock!.Result);
        Assert.False(runner.IsActive);
    }

    [Fact]
    public void A_logic_block_with_no_host_is_named_rather_than_run()
    {
        var runner = new EventRunner();

        runner.Begin(Block(), Font(), Box, Anchors);

        Assert.NotNull(runner.Unimplemented);
        Assert.Contains("LogicBlock", runner.Unimplemented);
    }

    private const uint Key = 0xFF000000;
    private const uint Ink = 0xFFFFFFFF;

    private static readonly TextBoxMetrics Box = new(18, 328, 400, 96, 6);

    private static readonly MenuAnchors Anchors =
        new((16, 460), (200, 200), (20, 328), (16, 460));

    /// <summary>A font with uniform glyphs — the runner only needs one to measure with.</summary>
    private static BitmapFont Font(int advance = 10, int height = 16)
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (advance, height));

        var glyphs = FontAtlas.Layout(extents, FontAtlas.DefaultSheetWidth, out int sheetHeight);
        var sheet = new Surface(FontAtlas.DefaultSheetWidth, sheetHeight, SurfaceKind.Font);
        sheet.Fill(Key);
        sheet.ColorKey = Key;

        return new BitmapFont(new FontAtlas(sheet, glyphs));
    }
}
