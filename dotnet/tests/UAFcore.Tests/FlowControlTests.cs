using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The design's <c>if</c> statement (<c>FLOW_CONTROL_EVENT_DATA</c>).
/// </summary>
/// <remarks>
/// The most common unexecuted event type in the corpus — 314 across the four designs — and the
/// only one whose whole behaviour is a global-variable read/write plus a branch, so it is testable
/// without a screen, a party or a level.
/// </remarks>
public class FlowControlTests
{
    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static FlowControlEvent Flow(
        string variable = "", string value = "",
        ValueModification modification = ValueModification.NoChange,
        FlowAction action = FlowAction.Goto,
        FlowCondition condition = FlowCondition.Always,
        uint destination = 100,
        uint onHappened = 9) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.FlowControl, 1, 0, 0,
                              (int)onHappened, 0, string.Empty, string.Empty, string.Empty, []),
            Version: 1,
            EntryMarker: "entry", ExitMarker: "exit", DestinationMarker: "dest",
            GlobalVariableName: variable, Value: value,
            DestinationId: destination,
            ValueModification: (int)modification,
            ActionCondition: (int)condition,
            Action: (int)action,
            Flags: 0);

    private static FlowOutcome Run(FlowControlEvent flow, AttributeList globals,
                                   Func<uint, bool>? valid = null) =>
        FlowControl.Run(flow, globals, valid ?? (_ => true));

    // ---- the variable half -----------------------------------------------------------------------

    [Fact]
    public void Set_creates_the_variable_and_the_comparison_succeeds()
    {
        var globals = new AttributeList();

        var outcome = Run(Flow("chapter", "2", ValueModification.Set,
                               condition: FlowCondition.Equals), globals);

        Assert.Equal("2", globals.Find("chapter"));
        Assert.Equal(100u, outcome.GoTo);
    }

    [Fact]
    public void Set_does_not_mark_the_variable_as_modified()
    {
        // The reference passes flags of 0 here, unlike increment and decrement. It decides what a
        // savegame carries, so the difference is not cosmetic.
        var globals = new AttributeList();

        Run(Flow("chapter", "2", ValueModification.Set), globals);

        Assert.Equal(AttributeFlags.None, (AttributeFlags)globals.Entry("chapter")!.Flags);
    }

    [Fact]
    public void Increment_reads_adds_and_writes_back_as_text()
    {
        var globals = new AttributeList();
        globals.Insert("count", "7");

        Run(Flow("count", "8", ValueModification.Increment), globals);

        Assert.Equal("8", globals.Find("count"));
        Assert.Equal(AttributeFlags.Modified, (AttributeFlags)globals.Entry("count")!.Flags);
    }

    [Fact]
    public void Decrement_goes_the_other_way_and_below_zero()
    {
        var globals = new AttributeList();
        globals.Insert("count", "0");

        Run(Flow("count", "", ValueModification.Decrement), globals);

        Assert.Equal("-1", globals.Find("count"));
    }

    [Fact]
    public void Incrementing_a_variable_that_does_not_exist_does_nothing_at_all()
    {
        // The reference breaks out before the insert, so there is no implicit "starts at zero".
        // A design that increments an unset counter gets no counter -- worth knowing before
        // assuming its logic is broken.
        var globals = new AttributeList();

        Run(Flow("missing", "1", ValueModification.Increment), globals);

        Assert.Null(globals.Find("missing"));
        Assert.Equal(0, globals.Count);
    }

    [Fact]
    public void No_change_reads_without_writing()
    {
        var globals = new AttributeList();
        globals.Insert("chapter", "3");

        var outcome = Run(Flow("chapter", "3", ValueModification.NoChange,
                               condition: FlowCondition.Equals), globals);

        Assert.Equal("3", globals.Find("chapter"));
        Assert.Equal(100u, outcome.GoTo);
    }

    [Fact]
    public void The_comparison_is_textual_rather_than_numeric()
    {
        // Increment writes "%d", so a design comparing against "007" never matches.
        var globals = new AttributeList();
        globals.Insert("count", "6");

        var outcome = Run(Flow("count", "007", ValueModification.Increment,
                               condition: FlowCondition.Equals), globals);

        Assert.Equal("7", globals.Find("count"));
        Assert.Null(outcome.GoTo);                       // "007" != "7", so the branch is not taken
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("  -5", -5)]
    [InlineData("+3", 3)]
    [InlineData("12 apples", 12)]                        // atoi takes the leading digits
    [InlineData("apples", 0)]                            // ...and gives zero for the rest
    [InlineData("", 0)]
    public void Atoi_reads_what_c_reads(string input, int expected)
    {
        // int.TryParse is not a substitute: it rejects the third and fourth cases outright, and a
        // design may well have put trailing text there.
        Assert.Equal(expected, FlowControl.Atoi(input));
    }

    // ---- the branch half -------------------------------------------------------------------------

    [Fact]
    public void An_event_with_no_action_still_modifies_its_variable()
    {
        // The modification happens first and the action is tested afterwards, so flow control used
        // purely as a counter works.
        var globals = new AttributeList();
        globals.Insert("count", "1");

        var outcome = Run(Flow("count", "", ValueModification.Increment,
                               action: FlowAction.None), globals);

        Assert.Equal("2", globals.Find("count"));
        Assert.Null(outcome.GoTo);
        Assert.False(outcome.Stop);
    }

    [Fact]
    public void Equals_starts_out_true_when_the_event_carries_no_value()
    {
        // `equals` is initialised from value.IsEmpty() before anything is read, so an event with
        // no value and no variable takes an Equals branch.
        var outcome = Run(Flow(value: "", condition: FlowCondition.Equals), new AttributeList());

        Assert.Equal(100u, outcome.GoTo);
    }

    [Fact]
    public void Not_equals_inverts_it()
    {
        var outcome = Run(Flow(value: "", condition: FlowCondition.NotEquals), new AttributeList());

        Assert.Null(outcome.GoTo);
    }

    [Fact]
    public void Always_takes_the_branch_whatever_the_variable_says()
    {
        var globals = new AttributeList();
        globals.Insert("chapter", "1");

        var outcome = Run(Flow("chapter", "99", ValueModification.NoChange,
                               condition: FlowCondition.Always), globals);

        Assert.Equal(100u, outcome.GoTo);
    }

    [Fact]
    public void A_destination_of_zero_falls_back_on_the_ordinary_chain()
    {
        var outcome = Run(Flow(destination: 0), new AttributeList());

        Assert.Null(outcome.GoTo);
        Assert.False(outcome.Stop);
    }

    [Fact]
    public void A_destination_the_level_lacks_stops_the_run()
    {
        // Same rule as CHAIN_EVENT: the reference pops rather than falling through to the chain.
        var outcome = Run(Flow(destination: 404), new AttributeList(), valid: _ => false);

        Assert.Null(outcome.GoTo);
        Assert.True(outcome.Stop);
    }

    [Theory]
    [InlineData(FlowAction.Goto)]
    [InlineData(FlowAction.Call)]
    [InlineData(FlowAction.Return)]
    [InlineData(FlowAction.Pop)]
    public void Every_action_but_none_behaves_identically(FlowAction action)
    {
        // The reference tests only for ACTION_NONE and treats the other four the same way, so the
        // call stack that CALL/RETURN/POP imply was never built. Reproduced deliberately: a design
        // using CALL today gets a GOTO, and inventing a stack would change its behaviour.
        var outcome = Run(Flow(action: action), new AttributeList());

        Assert.Equal(100u, outcome.GoTo);
    }

    [Fact]
    public void An_illegal_condition_does_not_take_the_branch()
    {
        var outcome = Run(Flow(condition: FlowCondition.Illegal), new AttributeList());

        Assert.Null(outcome.GoTo);
    }
}
