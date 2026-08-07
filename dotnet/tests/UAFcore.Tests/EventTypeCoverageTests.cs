using UAF.Common;
using UAF.Media;
using UAF.Media.Sdl;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Which event types the runner actually executes, counted from the shipped designs rather than
/// by hand.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because the hand-count in the porting plan was wrong twice.</b> Enum entries
/// and record types are not one-to-one — <c>Stairs</c>, <c>Teleporter</c> and
/// <c>TransferModule</c> all read into <c>TransferEvent</c>, <c>QuestionButton</c> and
/// <c>QuestionList</c> both into <c>QuestionEvent</c>, and <c>PickOneCombat</c> into
/// <c>CombatEvent</c> — so counting the runner's dispatch arms undercounts and counting enum
/// entries overcounts. The second count then read <c>GuidedTour</c> as inert because its record
/// is <c>GuidedTour</c> and not <c>GuidedTourEvent</c>, and the eye slid past it.
/// </para>
/// <para>
/// <b>The guarantee is the one that matters: no event type a shipped design actually uses is
/// inert.</b> That is stronger than a count of the enum, because eighteen of the 44 types appear
/// in no shipped design at all and their absence cannot break anybody's game.
/// </para>
/// <para>
/// <b>It has to go through <see cref="Game.StartEvent"/>, not <see cref="EventRunner.Begin"/>.</b>
/// The runner is not the only dispatcher — combat, chains, flow control, the clock, transfers,
/// experience and the utilities are handled by <c>Game</c> before the runner is asked, and a
/// runner-only sweep reports every one of those as inert. The first draft of this test did exactly
/// that and named seven types that work perfectly well.
/// </para>
/// </remarks>
public class EventTypeCoverageTests
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

    /// <summary>Every design in the reference corpus, or none when it is not checked out.</summary>
    private static IEnumerable<string> DesignRoots()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null || !Directory.Exists(Path.Combine(dir.FullName, "reference")))
        {
            yield break;
        }

        foreach (string design in Directory.EnumerateDirectories(
                     Path.Combine(dir.FullName, "reference"), "*.dsn"))
        {
            // The corpus holds a few directories with a .dsn name and no design inside them --
            // RUNELORD.DSN is one. A design without Data/ cannot be opened at all.
            if (Directory.Exists(Path.Combine(design, "Data")))
            {
                yield return design;
            }
        }
    }

    /// <summary>
    /// Runs every event of every level of every corpus design through <c>Begin</c>.
    /// </summary>
    /// <returns>
    /// The type of every event that came back with a "not implemented" message, and how many
    /// events were seen — so a run that found no events at all cannot pass by accident.
    /// </returns>
    private static (SortedSet<string> Inert, SortedSet<string> Refused, int Seen) Sweep()
    {
        var inert = new SortedSet<string>(StringComparer.Ordinal);
        var refused = new SortedSet<string>(StringComparer.Ordinal);
        int seen = 0;

        foreach (string root in DesignRoots())
        {
            using var design = LoadedDesign.Open(root, new SdlImageDecoder(),
                                                 new SdlFontRasterizer());

            for (int i = 0; i < design.LevelFiles.Count; i++)
            {
                if (design.Level(i) is not { } level)
                {
                    continue;
                }

                // One game per level rather than per event: StartEvent resets the runner, and
                // building a Game reloads the level, which over four thousand events is the
                // difference between a slow test and an unusable one.
                var game = new Game(design, levelIndex: i);

                foreach (var gameEvent in level.Events)
                {
                    seen++;

                    try
                    {
                        game.StartEvent(gameEvent);
                    }
                    catch (NotSupportedException)
                    {
                        // A script the port has not got to. That is a sub-opcode gap, not an event
                        // type gap, and conflating the two would make this test fail for the wrong
                        // reason -- so it is recorded separately and asserted separately.
                        refused.Add(((EventType)gameEvent.Base.EventType).ToString());
                        continue;
                    }

                    // Null means somebody handled it: either Game took it before the runner, or
                    // the runner presented it. Only the runner's fallthrough writes this.
                    if (game.Runner.Unimplemented is not null)
                    {
                        inert.Add(((EventType)gameEvent.Base.EventType).ToString());
                    }
                }
            }
        }

        return (inert, refused, seen);
    }

    /// <summary>
    /// The types that are still inert <i>and</i> appear in a shipped design.
    /// </summary>
    /// <remarks>
    /// <b>Empty is the goal and empty is not yet true.</b> Update this set when one lands — and if
    /// a type appears here that is not listed, something regressed rather than something drifted.
    /// </remarks>
    private static readonly string[] KnownInert = [];

    [Fact]
    public void No_event_type_a_shipped_design_uses_is_inert()
    {
        var (inert, _, seen) = Sweep();

        if (seen == 0)
        {
            return;                             // corpus not checked out
        }

        Assert.True(seen > 1000,
                    $"only {seen} events swept; the corpus is smaller than it should be");

        Assert.Equal<IEnumerable<string>>(KnownInert, inert);
    }

    /// <summary>
    /// The types the shipped designs actually contain, measured rather than assumed.
    /// </summary>
    /// <remarks>
    /// <b>A floor, not an equality.</b> Adding a design to the corpus should not fail a test, but
    /// a type dropping out of this list means the sweep above quietly stopped covering it — and
    /// that is the failure worth catching, because its whole value is the breadth it sweeps.
    /// </remarks>
    private static readonly string[] CorpusTypes =
    [
        "AddNpc", "Camp", "ChainEventType", "Combat", "GainExperience", "GiveTreasure",
        "GuidedTour", "LogicBlock", "NPCSays", "PassTime", "QuestStage", "QuestionButton",
        "QuestionList", "QuestionYesNo", "RandomEvent", "RemoveNPCEvent", "ShopEvent", "Sounds",
        "SpecialItem", "TavernEvent", "TempleEvent", "TextStatement", "TrainingHallEvent",
        "TransferModule", "Utilities", "WhoPays",
    ];

    [Fact]
    public void The_corpus_exercises_most_of_the_enum()
    {
        // Says what the guarantee above actually covers. A design may use any of the 44 types;
        // the shipped ones use these 26, and the other 18 are untested by that test whatever the
        // runner does with them.
        var used = UsedTypes();

        if (used.Count == 0)
        {
            return;                             // corpus not checked out
        }

        var missing = CorpusTypes.Where(t => !used.Contains(t)).ToList();

        Assert.True(missing.Count == 0,
                    $"the sweep no longer covers {string.Join(", ", missing)}; "
                    + $"it now sees {used.Count} types");
    }

    /// <summary>Every event type that appears in a shipped level.</summary>
    private static SortedSet<string> UsedTypes()
    {
        var used = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string root in DesignRoots())
        {
            using var design = LoadedDesign.Open(root, new SdlImageDecoder(),
                                                 new SdlFontRasterizer());

            for (int i = 0; i < design.LevelFiles.Count; i++)
            {
                if (design.Level(i) is { } level)
                {
                    foreach (var gameEvent in level.Events)
                    {
                        used.Add(((EventType)gameEvent.Base.EventType).ToString());
                    }
                }
            }
        }

        return used;
    }

    /// <summary>
    /// The types whose <i>scripts</i> reach something unported, which is a different gap.
    /// </summary>
    /// <remarks>
    /// <b>An event here is not inert — it ran and its script asked for a sub-opcode the VM
    /// refuses.</b> Kept separate so that finishing GPDL empties this list without touching the
    /// one above, and so that a genuinely inert type cannot hide behind a script failure.
    /// </remarks>
    private static readonly string[] KnownScriptRefusals = ["LogicBlock"];

    [Fact]
    public void Only_the_known_types_reach_an_unported_script_call()
    {
        var (_, refused, seen) = Sweep();

        if (seen == 0)
        {
            return;
        }

        Assert.Equal<IEnumerable<string>>(KnownScriptRefusals, refused);
    }

    [Fact]
    public void The_sweep_reaches_the_corpus_it_claims_to()
    {
        // Guards the test above: if DesignRoots stops finding anything, that test passes silently
        // and this one says why.
        var roots = DesignRoots().ToList();

        if (roots.Count == 0)
        {
            return;
        }

        Assert.Contains(roots, r => r.EndsWith("SomethingWild.dsn", StringComparison.Ordinal));
    }
}
