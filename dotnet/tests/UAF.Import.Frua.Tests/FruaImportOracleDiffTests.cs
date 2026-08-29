using UAF.Import.Frua;
using UAF.Serialization;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Compares a design imported by the <b>C++ reference</b> against one imported by this port.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is Phase 6's exit criterion, and it is the only test here that is not self-referential.</b>
/// Everything else in <c>UAF.Import.Frua.Tests</c> checks internal consistency, the plausibility of
/// decoded values, or agreement with a <i>reading</i> of <c>UAImport.cpp</c>. None of that is the
/// same as producing what the reference binary produces — which is the jump the GPDL goldens made,
/// and which immediately found a use-after-free nobody knew about.
/// </para>
/// <para>
/// <b>Producing the input is a manual step.</b> <c>tools/frua-import-oracle.sh</c> runs the
/// reference editor over a FRUA design under CrossOver or Wine and leaves a UAF design directory
/// behind. Point these tests at it:
/// </para>
/// <code>
/// UAF_FRUA_ORACLE_DIR=/tmp/frua-oracle \
/// UAF_FRUA_ORACLE_DESIGN="reference/Unlimited Adventures -ENG/DESIGNS/UA/HEIRS.DSN" \
/// dotnet test
/// </code>
/// <para>
/// <b>They return early when it is absent</b>, following <c>OracleDiffTests</c> — xUnit 2.9 has no
/// <c>Assert.Skip</c>, so a missing oracle looks identical to a passing comparison from the summary
/// alone. That is the same blind spot the GPDL goldens have, and the same remedy applies: the
/// workflow has to check separately.
/// </para>
/// <para>
/// <b>Not everything is asserted equal, deliberately.</b> The reference drops data the design
/// contains — monsters, NPCs, trained classes — so this port produces <i>more</i>, and asserting
/// equality would enshrine the losses. What must match is the geometry: a level's dimensions, its
/// cells, and the events it carries. Where the port is expected to hold more, the test says so and
/// checks the direction of the difference rather than its absence.
/// </para>
/// </remarks>
public class FruaImportOracleDiffTests
{
    /// <summary>The harness output — a UAF design directory the reference importer wrote.</summary>
    private static string? OracleDirectory()
    {
        string? dir = Environment.GetEnvironmentVariable("UAF_FRUA_ORACLE_DIR");
        return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
    }

    /// <summary>The FRUA design the harness was run over, which this port re-imports.</summary>
    private static string? SourceDesign()
    {
        string? design = Environment.GetEnvironmentVariable("UAF_FRUA_ORACLE_DESIGN");

        if (string.IsNullOrEmpty(design))
        {
            return null;
        }

        if (Directory.Exists(design))
        {
            return design;
        }

        // A relative path is taken against the repository root, so the variable can be set the
        // way the harness itself is invoked.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string rooted = dir is null ? design : Path.Combine(dir.FullName, design);
        return Directory.Exists(rooted) ? rooted : null;
    }

    /// <summary>Both sides, or null when the oracle has not been run.</summary>
    private static (string Oracle, FruaConvertedDesign Ours)? Pair()
    {
        if (OracleDirectory() is not { } oracle || SourceDesign() is not { } design)
        {
            return null;
        }

        return (oracle, FruaDesignConverter.Convert(FruaDesign.Open(design)));
    }

    private static IReadOnlyDictionary<int, LevelFile> OracleLevels(string oracle)
    {
        var levels = new Dictionary<int, LevelFile>();
        string data = Path.Combine(oracle, FruaDesignConverter.DataDirectory);

        if (!Directory.Exists(data))
        {
            return levels;
        }

        foreach (string path in Directory.EnumerateFiles(data, "Level*.lvl"))
        {
            string name = Path.GetFileNameWithoutExtension(path);

            if (!int.TryParse(name.AsSpan("Level".Length), out int number))
            {
                continue;
            }

            using var stream = File.OpenRead(path);
            levels[number] = LevelFileReader.Read(
                stream, ArchiveRole.Editor,
                (ar, type, ver) => EventBodyReader.TryRead(ar, type, ver, ArchiveRole.Editor));
        }

        return levels;
    }

    /// <summary>The reference and this port import the same set of levels.</summary>
    [Fact]
    public void The_same_levels_are_imported()
    {
        if (Pair() is not { } pair)
        {
            return;
        }

        var theirs = OracleLevels(pair.Oracle);

        Assert.NotEmpty(theirs);
        Assert.Equal(theirs.Keys.Order(), pair.Ours.Levels.Keys.Order());
    }

    /// <summary>
    /// Every level has the same shape and the same map.
    /// </summary>
    /// <remarks>
    /// <b>Geometry is where a disagreement means somebody is wrong.</b> Unlike the monsters or the
    /// trained classes, neither side is entitled to a different answer here — a wall is a wall.
    /// This is the assertion that would have caught the north/south/east/west slot permutation if
    /// the reasoning about it had gone the other way.
    /// </remarks>
    [Fact]
    public void Every_levels_geometry_matches()
    {
        if (Pair() is not { } pair)
        {
            return;
        }

        var theirs = OracleLevels(pair.Oracle);
        var differences = new List<string>();

        foreach (var (number, ours) in pair.Ours.Levels.OrderBy(l => l.Key))
        {
            if (!theirs.TryGetValue(number, out var reference))
            {
                differences.Add($"level {number}: not in the oracle output");
                continue;
            }

            if (ours.Width != reference.Width || ours.Height != reference.Height)
            {
                differences.Add(
                    $"level {number}: {ours.Width}x{ours.Height} here, " +
                    $"{reference.Width}x{reference.Height} there");
                continue;
            }

            for (int i = 0; i < ours.Cells.Count; i++)
            {
                var a = ours.Cells[i];
                var b = reference.Cells[i];

                if (!a.Walls.SequenceEqual(b.Walls))
                {
                    differences.Add(
                        $"level {number} cell {i}: walls [{string.Join(",", a.Walls)}] here, " +
                        $"[{string.Join(",", b.Walls)}] there");
                }

                if (!a.Blockage.SequenceEqual(b.Blockage))
                {
                    differences.Add(
                        $"level {number} cell {i}: blockage [{string.Join(",", a.Blockage)}] " +
                        $"here, [{string.Join(",", b.Blockage)}] there");
                }

                if (a.Zone != b.Zone)
                {
                    differences.Add($"level {number} cell {i}: zone {a.Zone} here, {b.Zone} there");
                }

                // Twenty is enough to see the pattern; a systematic fault produces thousands.
                if (differences.Count > 20)
                {
                    break;
                }
            }
        }

        Assert.True(differences.Count == 0,
                    "geometry differs from the reference importer:\n  " +
                    string.Join("\n  ", differences.Take(20)));
    }

    /// <summary>
    /// This port imports at least as many events as the reference, and the same types.
    /// </summary>
    /// <remarks>
    /// <b>Not equality.</b> A <c>SmallTown</c> generates up to six child events here, so this port
    /// legitimately carries more — but every type the reference produced must appear, or something
    /// was dropped rather than added.
    /// </remarks>
    [Fact]
    public void No_event_the_reference_imported_is_missing()
    {
        if (Pair() is not { } pair)
        {
            return;
        }

        var theirs = OracleLevels(pair.Oracle);
        var missing = new List<string>();

        foreach (var (number, ours) in pair.Ours.Levels.OrderBy(l => l.Key))
        {
            if (!theirs.TryGetValue(number, out var reference))
            {
                continue;
            }

            var here = ours.Entries.CountBy(e => e.Type).ToDictionary();
            var there = reference.Entries.CountBy(e => e.Type).ToDictionary();

            foreach (var (type, count) in there)
            {
                int mine = here.GetValueOrDefault(type);

                if (mine < count)
                {
                    missing.Add($"level {number}: {count} {type} there, {mine} here");
                }
            }
        }

        Assert.True(missing.Count == 0,
                    "events the reference imported and this port did not:\n  " +
                    string.Join("\n  ", missing.Take(20)));
    }

    /// <summary>
    /// The design's identity survives both importers the same way.
    /// </summary>
    [Fact]
    public void The_design_header_matches()
    {
        if (Pair() is not { } pair || SourceDesign() is not { } design)
        {
            return;
        }

        string path = Path.Combine(pair.Oracle, FruaDesignConverter.DataDirectory, "game.dat");

        if (!File.Exists(path))
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var cursor = GameDataReader.Open(stream);
        var reference = GlobalStatsReader.ReadThroughCharacters(cursor.Body, cursor.Version);

        var game = FruaGameData.ReadFile(design);

        // Both importers take these straight from game001.dat, so a difference is a mis-read on
        // one side rather than a design decision on either.
        Assert.Equal(game.DesignName, reference.DesignName);
        Assert.Equal((int)game.StartExperience, reference.StartExp);
        Assert.Equal((int)game.StartPlatinum, reference.StartPlatinum);
        Assert.Equal(game.StartLevel, reference.StartLevel);
    }

    /// <summary>
    /// This port resolves the monsters the reference leaves out.
    /// </summary>
    /// <remarks>
    /// <b>The divergence this diff exists to confirm, not to flag.</b> Nine of the reference's
    /// fourteen <c>NotImplemented</c> markers are the one disabled <c>GetMonsterKey</c> lookup, so
    /// a combat it imports carries quantities and no monsters. If the oracle output ever *does*
    /// name monsters, the reference was rebuilt with that code restored and the divergence list
    /// needs revisiting — which is why this asserts the direction rather than assuming it.
    /// </remarks>
    [Fact]
    public void The_reference_drops_the_monsters_this_port_resolves()
    {
        if (Pair() is not { } pair)
        {
            return;
        }

        var theirs = OracleLevels(pair.Oracle);

        int named = Named(theirs.Values);
        int ours = Named(pair.Ours.Levels.Values);

        Assert.True(named == 0,
                    $"the oracle named {named} monsters; GetMonsterKey may have been restored, " +
                    "and the divergence list in docs/PORTING-PLAN.md needs revisiting");

        // A design with no combat in it cannot answer the question either way, and TUTORIAL.DSN
        // is such a design -- one level, one transfer event. Requiring monsters of it failed the
        // port for the fixture's silence. The direction is still worth checking on the fixtures
        // that do have combats, so the requirement is conditioned on there being one rather than
        // dropped, and the count is asserted so a fixture that quietly lost its combats cannot
        // pass this by being empty.
        int combats = pair.Ours.Levels.Values
            .SelectMany(l => l.Entries)
            .Select(e => e.Body)
            .OfType<CombatEvent>()
            .Count();

        if (combats == 0)
        {
            Assert.Equal(0, ours);
            return;
        }

        Assert.True(ours > 0, $"this port resolved no monsters across {combats} combats, so the " +
                              "two now agree by both being empty rather than by this port doing " +
                              "more");
    }

    /// <summary>How many combat slots across these levels name a monster or a character.</summary>
    private static int Named(IEnumerable<LevelFile> levels) =>
        levels.SelectMany(l => l.Entries)
              .Select(e => e.Body)
              .OfType<CombatEvent>()
              .SelectMany(c => c.Monsters)
              .Count(m => !string.IsNullOrEmpty(m.MonsterId) || !string.IsNullOrEmpty(m.CharacterId));
}
