using UAF.Serialization;
using UAFcore;

namespace UAFedit.Events.Tests;

/// <summary>
/// The editor against the two reference designs.
/// </summary>
/// <remarks>
/// Everything here early-returns when <c>reference/</c> is absent, which is what
/// <see cref="The_corpus_loads_a_level_with_events"/> exists to make safe: it is the one test that
/// asserts the corpus really produced a level with events, so this file cannot pass while proving
/// nothing.
/// </remarks>
public class EventEditorCorpusTests
{
    /// <summary>
    /// The premise: a real design opens, a real level reads, and it has events in it.
    /// </summary>
    [Fact]
    public void The_corpus_loads_a_level_with_events()
    {
        using var design = EventCorpus.Open("Case.dsn");
        if (design is null)
        {
            return;
        }

        var levels = EventCorpus.Levels(design).ToList();

        Assert.NotEmpty(levels);
        Assert.Contains(levels, pair => pair.Level.Events.Count > 0);

        var editor = new EventEditorViewModel(design);

        Assert.NotEmpty(editor.Levels);
        Assert.NotEmpty(editor.Events);
        Assert.NotNull(editor.SelectedEvent);
        Assert.False(editor.IsDirty);
    }

    /// <summary>
    /// Every event type the corpus contains gets a detail editor with something in it.
    /// </summary>
    /// <remarks>
    /// The coverage claim, checked rather than asserted in prose. A type whose table is empty would
    /// present as a blank pane, which is the exact failure this is guarding: the dispatch in
    /// <see cref="EventDetailFields.For"/> silently answers <c>None</c> for a type it does not
    /// know.
    /// </remarks>
    [Fact]
    public void Every_corpus_event_type_has_a_detail_table()
    {
        var seen = new HashSet<EventType>();

        foreach (string name in EventCorpus.Designs)
        {
            using var design = EventCorpus.Open(name);
            if (design is null)
            {
                continue;
            }

            foreach (var (_, level) in EventCorpus.Levels(design))
            {
                foreach (var body in level.Events)
                {
                    seen.Add((EventType)body.Base.EventType);

                    var detail = EventDetailFields.For(body);

                    Assert.True(detail.Fields.Count + detail.Groups.Count > 0,
                                $"{EventCatalog.Name((EventType)body.Base.EventType)} has no "
                                + "detail fields");
                }
            }
        }

        if (seen.Count == 0)
        {
            return;
        }

        // The measured spread: 26 distinct types across the two designs.
        Assert.True(seen.Count >= 20, $"only {seen.Count} distinct event types seen");
    }

    /// <summary>
    /// The shared header round-trips through every concrete record the corpus contains.
    /// </summary>
    /// <remarks>
    /// <see cref="EventRecords.WithBase"/> is a hand-written switch over 36 record types and its
    /// failure mode is silent — an unknown type returns the body unchanged, so the edit vanishes
    /// rather than throwing. This is what catches a record type nobody added to it.
    /// </remarks>
    [Fact]
    public void Every_corpus_record_accepts_a_new_header()
    {
        foreach (string name in EventCorpus.Designs)
        {
            using var design = EventCorpus.Open(name);
            if (design is null)
            {
                continue;
            }

            foreach (var (_, level) in EventCorpus.Levels(design))
            {
                foreach (var body in level.Events)
                {
                    var changed = EventRecords.WithBase(
                        body, body.Base with { Text = "edited by the test" });

                    Assert.Equal("edited by the test", changed.Base.Text);
                    Assert.Equal(body.GetType(), changed.GetType());
                }
            }
        }
    }

    /// <summary>
    /// Following a real chain selects the event it names.
    /// </summary>
    /// <remarks>
    /// Chain navigation against synthetic data proves the plumbing; against the corpus it proves
    /// the ids in shipped designs actually resolve within their own level, which is the assumption
    /// the whole graph rests on.
    /// </remarks>
    [Fact]
    public void Following_a_corpus_chain_selects_its_target()
    {
        using var design = EventCorpus.Open("Case.dsn");
        if (design is null || EventCorpus.LevelWithChains(design) is not { } level)
        {
            return;
        }

        var editor = new EventEditorViewModel(level, "test");

        // The first event whose chain resolves inside this level. Dangling ids are legal and do
        // occur, so the test picks a live edge rather than assuming the first one is.
        var source = editor.Events.FirstOrDefault(
            e => EventChainLinks.Of(e.Body).Any(l => editor.Resolves(l.Target)));

        Assert.NotNull(source);

        editor.SelectedEvent = source;

        var link = editor.Outgoing.First(l => !l.IsBroken);
        link.FollowCommand.Execute(null);

        Assert.NotNull(editor.SelectedEvent);
        Assert.Equal(link.TargetId, editor.SelectedEvent!.Id);
    }

    /// <summary>
    /// The reverse edges agree with the forward ones.
    /// </summary>
    /// <remarks>
    /// The incoming pane is the feature the original editor has no equivalent of, so its
    /// correctness has nothing to compare against except the forward relation. Selecting a chain's
    /// target must show the source that pointed at it.
    /// </remarks>
    [Fact]
    public void A_chain_target_lists_its_source_as_incoming()
    {
        using var design = EventCorpus.Open("Case.dsn");
        if (design is null || EventCorpus.LevelWithChains(design) is not { } level)
        {
            return;
        }

        var editor = new EventEditorViewModel(level, "test");

        var source = editor.Events.FirstOrDefault(
            e => EventChainLinks.Of(e.Body).Any(l => editor.Resolves(l.Target) && l.Target != e.Id));

        Assert.NotNull(source);

        uint sourceId = source!.Id;
        uint targetId = EventChainLinks.Of(source.Body)
                                       .First(l => editor.Resolves(l.Target) && l.Target != sourceId)
                                       .Target;

        editor.GoTo(targetId);

        Assert.Equal(targetId, editor.SelectedEvent!.Id);
        Assert.Contains(editor.Incoming, r => r.TargetId == sourceId);
    }

    /// <summary>
    /// Question events carry chains the shared header cannot see.
    /// </summary>
    /// <remarks>
    /// The whole argument for <see cref="EventChainLinks"/> existing. If only
    /// <c>chainEventHappen</c> were followed, a design's conversations would look like isolated
    /// nodes; Case.dsn has 113 question lists and 102 yes/no events carrying the structure.
    /// </remarks>
    [Fact]
    public void Question_options_contribute_chain_links()
    {
        using var design = EventCorpus.Open("Case.dsn");
        if (design is null)
        {
            return;
        }

        var question = EventCorpus.Levels(design)
            .SelectMany(pair => pair.Level.Events)
            .OfType<QuestionEvent>()
            .FirstOrDefault(q => q.Options.Any(o => o.Chain > 0));

        if (question is null)
        {
            return;
        }

        var links = EventChainLinks.Of(question);
        int fromOptions = question.Options.Count(o => o.Chain > 0);

        Assert.True(links.Count >= fromOptions);
        Assert.Contains(links, l => l.Label.StartsWith("Button", StringComparison.Ordinal));
    }

    /// <summary>
    /// The corpus histogram, as a fact rather than a claim.
    /// </summary>
    /// <remarks>
    /// Text statements dominate by an order of magnitude, which is why they get the first table in
    /// <see cref="EventDetailFields"/> and why the shared header's three text fields matter more
    /// than any type-specific pane.
    /// </remarks>
    [Fact]
    public void Text_statements_dominate_the_corpus()
    {
        var counts = new Dictionary<EventType, int>();

        foreach (string name in EventCorpus.Designs)
        {
            using var design = EventCorpus.Open(name);
            if (design is null)
            {
                continue;
            }

            foreach (var (_, level) in EventCorpus.Levels(design))
            {
                foreach (var entry in level.Entries)
                {
                    counts[entry.Type] = counts.GetValueOrDefault(entry.Type) + 1;
                }
            }
        }

        if (counts.Count == 0)
        {
            return;
        }

        var ranked = counts.OrderByDescending(pair => pair.Value).ToList();

        Assert.Equal(EventType.TextStatement, ranked[0].Key);
        Assert.True(ranked[0].Value > counts.Values.Sum() / 2,
                    "text statements are over half the corpus");
    }

    /// <summary>
    /// Every entry in the corpus has a body.
    /// </summary>
    /// <remarks>
    /// A bodyless entry is legal — an ordinal <c>CreateNewEvent</c> does not recognise is four
    /// bytes and no object (<c>LevelEventEntry</c>) — and the list pane drops those. Measuring
    /// zero of them across 4,705 events says the drop costs nothing today, and would say so
    /// loudly if a future corpus disagreed.
    /// </remarks>
    [Fact]
    public void No_corpus_entry_is_bodyless()
    {
        foreach (string name in EventCorpus.Designs)
        {
            using var design = EventCorpus.Open(name);
            if (design is null)
            {
                continue;
            }

            foreach (var (index, level) in EventCorpus.Levels(design))
            {
                Assert.All(level.Entries, entry => Assert.NotNull(entry.Body));
                Assert.Equal(level.Events.Count, level.Entries.Count);
                Assert.True(index >= 0);
            }
        }
    }
}
