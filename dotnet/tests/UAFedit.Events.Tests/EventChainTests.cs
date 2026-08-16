using UAF.Serialization;
using UAFcore;

namespace UAFedit.Events.Tests;

/// <summary>
/// The chain graph: which edges exist, which are live, and where "Go" lands.
/// </summary>
/// <remarks>
/// Following a chain is what the original editor is used for, so this is the file that matters
/// most. It runs on hand-built events because the interesting cases — a dangling target, a chain
/// trigger that makes a stored id dead — are ones a shipped design mostly does not contain.
/// </remarks>
public class EventChainTests
{
    [Fact]
    public void The_base_header_contributes_its_happened_chain()
    {
        var links = EventChainLinks.Of(EventFixture.Text(1, "hello", chainHappen: 7));

        var link = Assert.Single(links);
        Assert.Equal("Normal Chain", link.Label);
        Assert.Equal(7u, link.Target);
        Assert.True(link.Taken);
    }

    /// <summary>
    /// A target of zero is not a target.
    /// </summary>
    /// <remarks>
    /// Every consumer guards with <c>&gt; 0</c> (<see cref="EventChain"/>), so event id 0 can never
    /// be chained to and a stored 0 is the null.
    /// </remarks>
    [Fact]
    public void A_zero_target_is_not_a_link()
    {
        Assert.Empty(EventChainLinks.Of(EventFixture.Text(1, "hello")));
    }

    /// <summary>
    /// Under the default chain trigger the not-happened id is stored and dead.
    /// </summary>
    /// <remarks>
    /// <c>AlwaysChain</c> takes <c>chainEventHappen</c> on <b>both</b> paths
    /// (<c>RunEvent.cpp:910</c>), so an id in <c>chainEventNotHappen</c> is never read. The
    /// original's tree does not draw it at all; this reports it as an untaken edge, which is the
    /// answer to "why does my branch never fire".
    /// </remarks>
    [Fact]
    public void Always_makes_the_not_happened_target_dead_but_visible()
    {
        var body = EventFixture.Text(1, "hello", chainHappen: 2, chainNotHappen: 3,
                                     chainTrigger: (int)ChainTrigger.Always);

        var links = EventChainLinks.Of(body);

        Assert.Equal(2, links.Count);
        Assert.True(links[0].Taken);
        Assert.Equal(3u, links[1].Target);
        Assert.False(links[1].Taken);

        // The engine agrees: neither path reaches 3 under Always.
        Assert.Equal(2u, EventChain.Next(body.Base, happened: true));
        Assert.Equal(2u, EventChain.Next(body.Base, happened: false));
    }

    /// <summary>Under <c>IfEventNotHappen</c> the two swap which one is live.</summary>
    [Fact]
    public void If_not_happened_makes_the_other_target_the_live_one()
    {
        var body = EventFixture.Text(1, "hello", chainHappen: 2, chainNotHappen: 3,
                                     chainTrigger: (int)ChainTrigger.IfNotHappened);

        var links = EventChainLinks.Of(body);

        Assert.False(links[0].Taken);
        Assert.True(links[1].Taken);
        Assert.Equal(3u, EventChain.Next(body.Base, happened: false));
        Assert.Null(EventChain.Next(body.Base, happened: true));
    }

    /// <summary>
    /// A question's options are chain edges the shared header cannot see.
    /// </summary>
    [Fact]
    public void Question_options_are_labelled_with_the_designers_own_text()
    {
        var links = EventChainLinks.Of(
            EventFixture.Question(1, "Which way?", ("North", 5), ("South", 6)));

        Assert.Equal(2, links.Count);
        Assert.Equal("Button 1 Chain: North", links[0].Label);
        Assert.Equal(5u, links[0].Target);
        Assert.Equal("Button 2 Chain: South", links[1].Label);
    }

    [Fact]
    public void Yes_no_events_carry_two_named_chains()
    {
        var links = EventChainLinks.Of(EventFixture.YesNo(1, yes: 4, no: 9));

        Assert.Equal(["Yes Chain", "No Chain"], links.Select(l => l.Label));
        Assert.Equal([4u, 9u], links.Select(l => l.Target));
    }

    [Fact]
    public void Following_a_link_selects_the_event_it_names()
    {
        var editor = new EventEditorViewModel(EventFixture.Level(
            EventFixture.Question(1, "Which way?", ("North", 2), ("South", 3)),
            EventFixture.Text(2, "You go north."),
            EventFixture.Text(3, "You go south.")), "test");

        editor.SelectedEvent = editor.Events[0];

        Assert.Equal(2, editor.Outgoing.Count);

        editor.Outgoing[1].FollowCommand.Execute(null);

        Assert.Equal(3u, editor.SelectedEvent!.Id);
        Assert.Equal("You go south.", editor.SelectedEvent.Summary);
    }

    /// <summary>
    /// The reverse relation: an event knows what leads to it.
    /// </summary>
    /// <remarks>
    /// The original editor has no equivalent — the only reverse lookup in the codebase is the
    /// rescan inside <c>GameEventList::DumpEventText</c> (<c>Shared/GameEvent.cpp:4313</c>) and
    /// the separate Cross Reference dialog.
    /// </remarks>
    [Fact]
    public void An_event_lists_everything_that_chains_into_it()
    {
        var editor = new EventEditorViewModel(EventFixture.Level(
            EventFixture.Question(1, "Which way?", ("North", 3), ("South", 3)),
            EventFixture.Text(2, "A corridor.", chainHappen: 3),
            EventFixture.Text(3, "A dead end.")), "test");

        editor.GoTo(3);

        Assert.Equal(3u, editor.SelectedEvent!.Id);
        Assert.Equal(3, editor.Incoming.Count);
        Assert.Equal([1u, 1u, 2u], editor.Incoming.Select(r => r.TargetId));
        Assert.Contains(editor.Incoming, r => r.Label == "Button 1 Chain: North");
        Assert.Contains(editor.Incoming, r => r.Label == "Normal Chain");
    }

    /// <summary>
    /// An event nothing points at reports so.
    /// </summary>
    /// <remarks>
    /// Not an error. An event with map coordinates is reached by walking onto its cell
    /// (<see cref="EventLookup.FirstAt"/>) and never chained to; an event with neither is dead
    /// design data, and telling those apart is what the pane is for.
    /// </remarks>
    [Fact]
    public void An_unreferenced_event_has_no_incoming_chains()
    {
        var editor = new EventEditorViewModel(
            EventFixture.Level(EventFixture.Text(1, "alone")), "test");

        Assert.Empty(editor.Incoming);
        Assert.Empty(editor.Outgoing);
        Assert.False(editor.HasIncoming);
        Assert.False(editor.HasOutgoing);
    }

    /// <summary>
    /// A chain naming no event is shown as dangling and cannot be followed.
    /// </summary>
    /// <remarks>
    /// The engine tolerates it — it pushes a do-nothing event and carries on
    /// (<c>RunEvent.cpp:13224</c>) — which is exactly why an editor has to point it out.
    /// </remarks>
    [Fact]
    public void A_dangling_chain_is_reported_and_not_followable()
    {
        var editor = new EventEditorViewModel(
            EventFixture.Level(EventFixture.Text(1, "hello", chainHappen: 99)), "test");

        var link = Assert.Single(editor.Outgoing);

        Assert.True(link.IsBroken);
        Assert.Contains("no such event", link.Description, StringComparison.Ordinal);

        link.FollowCommand.Execute(null);

        Assert.Equal(1u, editor.SelectedEvent!.Id);
    }

    /// <summary>Editing a chain id re-derives the graph immediately.</summary>
    [Fact]
    public void Editing_a_chain_id_rebuilds_both_directions()
    {
        var editor = new EventEditorViewModel(EventFixture.Level(
            EventFixture.Text(1, "one", chainHappen: 2),
            EventFixture.Text(2, "two"),
            EventFixture.Text(3, "three")), "test");

        editor.SelectedEvent = editor.Events[0];

        var chain = editor.ControlFields.OfType<EventChainFieldViewModel>()
                                        .First(f => f.Label == "Chain on happen");

        Assert.True(chain.CanFollow);
        Assert.False(chain.IsBroken);

        chain.Value = "3";

        Assert.Equal(3u, Assert.Single(editor.Outgoing).TargetId);

        editor.GoTo(2);
        Assert.Empty(editor.Incoming);

        editor.GoTo(3);
        Assert.Single(editor.Incoming);
    }

    /// <summary>
    /// A self-chain is expressible, is listed once, and does not appear as its own caller.
    /// </summary>
    [Fact]
    public void An_event_chained_to_itself_appears_only_as_outgoing()
    {
        var editor = new EventEditorViewModel(
            EventFixture.Level(EventFixture.Text(1, "forever", chainHappen: 1)), "test");

        Assert.Single(editor.Outgoing);
        Assert.Empty(editor.Incoming);
    }

    /// <summary>
    /// The chain field's Go button navigates as well as the chain pane's.
    /// </summary>
    [Fact]
    public void The_chain_field_follows_its_own_target()
    {
        var editor = new EventEditorViewModel(EventFixture.Level(
            EventFixture.Text(1, "one", chainHappen: 2),
            EventFixture.Text(2, "two")), "test");

        editor.SelectedEvent = editor.Events[0];

        var chain = editor.ControlFields.OfType<EventChainFieldViewModel>()
                                        .First(f => f.Label == "Chain on happen");
        chain.FollowCommand.Execute(null);

        Assert.Equal(2u, editor.SelectedEvent!.Id);
    }

    /// <summary>
    /// The list row's chain column marks dead edges.
    /// </summary>
    /// <remarks>
    /// Parenthesised rather than omitted, for the same reason the pane shows them: an id that is
    /// stored and never followed is the usual explanation for a branch that does not work.
    /// </remarks>
    [Fact]
    public void The_list_row_brackets_a_chain_the_trigger_never_follows()
    {
        var editor = new EventEditorViewModel(
            EventFixture.Level(EventFixture.Text(1, "hello", chainHappen: 2, chainNotHappen: 3)),
            "test");

        Assert.Equal("2, (3)", editor.Events[0].Chains);
    }
}
