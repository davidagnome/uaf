using UAF.Media;
using UAF.Media.Sdl;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Locks the rendered combat screen for a known encounter.
/// </summary>
/// <remarks>
/// <para>
/// The dungeon viewport has had a regression guard since Phase 4; combat now draws enough —
/// terrain, icons, cursor — to be worth the same treatment. <b>A regression guard, not an
/// oracle</b>: it says today matches yesterday, not that the C++ build draws the same thing.
/// </para>
/// <para>
/// It exists because of a specific defect this frame would have caught. The renderer's origin was
/// left at the C++'s <c>CombatScreenX/Y</c> of (14,16) while this port draws combat in the dungeon
/// viewport at (48,54), so every square was drawn 34 pixels up and left of where it belonged —
/// <i>clipped</i> rather than aligned, which looked almost right. What actually exposed it was the
/// cursor vanishing, because a cursor that overhangs the view is dropped whole.
/// </para>
/// <para>
/// Regenerate with <c>UAF_GOLDEN_UPDATE=1</c>, and only after looking at the frame.
/// </para>
/// </remarks>
public class CombatGoldenFrameTests
{
    private const string GoldenFile = "combat-golden.txt";

    private static DirectoryInfo? Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        return dir;
    }

    /// <summary>
    /// Renders the encounter on level 1 a fixed number of steps in.
    /// </summary>
    /// <remarks>
    /// The dice are a fixed sequence, so the fight is deterministic — a real roller would make the
    /// hash meaningless. The party is driven to guard so the frame does not depend on the AI
    /// picking the same move twice.
    /// </remarks>
    private static ulong Render(LoadedDesign design, int steps, out int colours)
    {
        var level = design.Level(1)!;
        var combat = level.Events.OfType<CombatEvent>().First();

        int roll = 0;
        var game = new Game(design, levelIndex: 1) { Dice = sides => ((roll++ * 7) % sides) + 1 };

        var start = typeof(Game).GetMethod("StartEvent",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        start.Invoke(game, [combat]);

        for (int i = 0; i < steps && game.InCombat; i++)
        {
            if (game.Combat!.AwaitingPlayer)
            {
                while (CombatMenu.At(game.Combat.Menu.ActiveItem) != CombatCommand.Guard)
                {
                    game.Update(InputEvent.KeyDown(VirtualKey.Right));
                }
                game.Update(InputEvent.KeyDown(VirtualKey.Return));
            }
            else
            {
                game.Update(InputEvent.KeyDown(VirtualKey.Down));
            }
        }

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

        colours = seen.Count;
        return hash;
    }

    [Fact]
    public void The_combat_screen_matches_its_golden_hashes()
    {
        var repo = Repo();
        if (repo is null)
        {
            return;
        }

        string root = Path.Combine(repo.FullName, "reference", "SomethingWild.dsn");
        if (!Directory.Exists(root))
        {
            return;
        }

        using var design = LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer());

        var actual = new Dictionary<string, ulong>();
        foreach (int steps in new[] { 0, 3, 9 })
        {
            actual[$"step-{steps}"] = Render(design, steps, out int colours);

            // A blank viewport would hash stably for ever. The bar is deliberately low: this
            // zone's floor tile is a FLAT colour, so terrain alone gives exactly one and the
            // variety comes from the combatant icons. A first draft asked for 200 and failed on a
            // frame that was perfectly correct.
            Assert.True(colours > 50,
                        $"step-{steps}: only {colours} distinct colours; combatants are not drawing");
        }

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", GoldenFile);
        string source = Path.Combine(repo.FullName, "dotnet", "tests", "UAFcore.Tests",
                                     "Assets", GoldenFile);

        if (Environment.GetEnvironmentVariable("UAF_GOLDEN_UPDATE") == "1")
        {
            string[] lines =
                ["# Combat screen hashes for SomethingWild.dsn level 1, regenerated by UAF_GOLDEN_UPDATE=1.",
                 "# A regression guard, not an oracle -- see CombatGoldenFrameTests.",
                 .. actual.Select(e => $"{e.Key}|{e.Value}")];

            foreach (string destination in new[] { path, source })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllLines(destination, lines);
            }
            return;
        }

        if (!File.Exists(path))
        {
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
            $"the combat screen changed for {changed.Count} frame(s): {string.Join(", ", changed)}. " +
            "Render them and look before regenerating with UAF_GOLDEN_UPDATE=1.");
    }

    [Fact]
    public void The_frames_differ_from_each_other()
    {
        // Three identical hashes would mean the fight is not advancing, and the guard would be
        // watching a still image.
        var repo = Repo();
        string root = repo is null ? "" : Path.Combine(repo.FullName, "reference", "SomethingWild.dsn");
        if (!Directory.Exists(root))
        {
            return;
        }

        using var design = LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer());

        var hashes = new[] { 0, 3, 9 }.Select(s => Render(design, s, out _)).ToList();
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
    }
}
