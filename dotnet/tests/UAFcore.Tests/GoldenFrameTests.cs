using UAF.Media;
using UAF.Media.Sdl;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Locks the rendered viewport for a set of known party positions, so a change in what the engine
/// draws has to be deliberate.
/// </summary>
/// <remarks>
/// <para>
/// The testing strategy in <c>docs/PORTING-PLAN.md</c> §8 calls for golden framebuffers, and this
/// is the engine's half of it. It exists because of a specific, repeated failure in this port:
/// rendering defects pass every unit test. The front-face-only skip that discarded an entire
/// viewport square — the corridor's side walls — was invisible to 700-odd passing tests and only
/// showed up when a frame was rendered and looked at.
/// </para>
/// <para>
/// <b>This is a regression guard, not an oracle.</b> It cannot say the engine draws what the C++
/// build draws; nothing here can, because a running game has no equivalent of the serialization
/// dump to diff against. What it says is that today's output matches yesterday's, so a change is
/// noticed at the moment it happens rather than discovered later by eye. Establishing
/// correspondence with the original still needs screenshots captured from a Windows build.
/// </para>
/// <para>
/// The hashes cover the <b>viewport only</b>, not the whole screen. Chrome and text move for
/// reasons that have nothing to do with the renderer — a font metric, a config value — and a
/// whole-screen hash would go off constantly for changes that are not what this is watching.
/// </para>
/// <para>
/// Regenerate with <c>UAF_GOLDEN_UPDATE=1</c>. Do that only after looking at the frames: a golden
/// file updated to match a bug is worse than no golden file, because it converts a visible defect
/// into a permanent expectation.
/// </para>
/// </remarks>
public class GoldenFrameTests
{
    private const string GoldenFile = "viewport-golden.txt";

    private static string? DesignRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string design = Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");
        return Directory.Exists(design) ? design : null;
    }

    /// <summary>
    /// The scenes rendered. Facings only, because the party's position is the design's own start
    /// and movement from it is constrained by walls — turning is the one axis that always varies.
    /// </summary>
    private static IEnumerable<(string Name, Facing Facing, int Steps)> Scenes()
    {
        foreach (Facing facing in Enum.GetValues<Facing>())
        {
            yield return ($"start-{facing}", facing, 0);
            yield return ($"step-{facing}", facing, 1);
        }
    }

    private static ulong Render(LoadedDesign design, Facing facing, int steps,
                                out int distinctColours)
    {
        var game = new Game(design);
        while (game.Facing != facing)
        {
            game.Update(InputEvent.KeyDown(VirtualKey.Right));
        }

        for (int i = 0; i < steps; i++)
        {
            game.Update(InputEvent.KeyDown(VirtualKey.Up));
        }

        // This test measures the dungeon view, and a full-screen event replaces it rather than
        // drawing over it -- the treasure screen never calls updateViewport (Screen.cpp:340). The
        // south scene walks onto a treasure event with no items in it, so without this the frame
        // is a blank viewport and the hash guards nothing. Event presentation is covered by
        // EventRunnerTests; dismissing it here restores the scene this golden was written for.
        game.Runner.Cancel();

        var frame = game.Render();
        design.Config.Rewind();
        if (!design.Config.TryGetRect("VIEWPORT_RECT", out int l, out int t, out int r, out int b))
        {
            (l, t, r, b) = (0, 0, frame.Width, frame.Height);
        }

        var seen = new HashSet<uint>();
        ulong hash = 14695981039346656037;
        for (int y = t; y < Math.Min(b, frame.Height); y++)
        {
            for (int x = l; x < Math.Min(r, frame.Width); x++)
            {
                uint pixel = frame[x, y];
                seen.Add(pixel);
                hash ^= pixel;
                hash *= 1099511628211;
            }
        }

        distinctColours = seen.Count;
        return hash;
    }

    [Fact]
    public void The_viewport_matches_its_golden_hashes()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer());

        var actual = new Dictionary<string, ulong>();
        foreach (var (name, facing, steps) in Scenes())
        {
            actual[name] = Render(design, facing, steps, out int colours);

            // A uniform viewport is what a silently broken render produces, and it would hash
            // stably forever. Requiring real variety keeps the golden from locking in a blank.
            Assert.True(colours > 200,
                $"{name}: only {colours} distinct colours; the viewport is not drawing art");
        }

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", GoldenFile);

        if (Environment.GetEnvironmentVariable("UAF_GOLDEN_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path,
                ["# Viewport hashes for SomethingWild.dsn, regenerated by UAF_GOLDEN_UPDATE=1.",
                 "# A regression guard, not an oracle -- see GoldenFrameTests.",
                 .. actual.Select(e => $"{e.Key}|{e.Value}")]);
            return;
        }

        if (!File.Exists(path))
        {
            // Absent rather than failing: the file is generated from gitignored art, so a fresh
            // checkout legitimately has none until someone runs the update.
            return;
        }

        var expected = File.ReadLines(path)
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split('|'))
            .ToDictionary(f => f[0], f => ulong.Parse(f[1]));

        var changed = expected
            .Where(e => actual.TryGetValue(e.Key, out ulong got) && got != e.Value)
            .Select(e => e.Key)
            .ToList();

        Assert.True(changed.Count == 0,
            $"the viewport changed for {changed.Count} scene(s): {string.Join(", ", changed)}. " +
            "Render them and look before regenerating with UAF_GOLDEN_UPDATE=1.");
    }

    [Fact]
    public void Turning_and_walking_produce_distinct_views()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer());

        var hashes = Scenes()
            .Select(s => (s.Name, Hash: Render(design, s.Facing, s.Steps, out _)))
            .ToList();

        // Not all eight need differ -- a party facing two identical corridors sees the same thing,
        // and walking into a wall leaves the view unchanged. But collapsing to one or two values
        // would mean the renderer is not reading the party's state at all, which is exactly the
        // shape of the bug this file exists for.
        int distinct = hashes.Select(h => h.Hash).Distinct().Count();
        Assert.True(distinct >= 3,
            $"only {distinct} distinct views across {hashes.Count} scenes; the renderer may not be " +
            "reading position and facing");
    }
}
