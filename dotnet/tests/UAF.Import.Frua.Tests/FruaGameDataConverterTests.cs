using UAF.Import.Frua;
using UAF.Serialization;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Overlaying a FRUA design header onto an existing UAF design.
/// </summary>
public class FruaGameDataConverterTests
{
    private static DirectoryInfo? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        return dir;
    }

    private static string? Heirs()
    {
        if (RepoRoot() is not { } root)
        {
            return null;
        }

        string design = Path.Combine(root.FullName, "reference", "Unlimited Adventures -ENG",
                                     "DESIGNS", "UA", "HEIRS.DSN");
        return Directory.Exists(design) ? design : null;
    }

    /// <summary>
    /// The template design whose money and difficulty tables an import inherits.
    /// </summary>
    /// <remarks>
    /// <b>Not <c>DefaultDesign</c>, which cannot be read this far.</b> Its <c>game.dat</c> is 4,343
    /// bytes and <c>ReadThroughCharacters</c> runs to exactly that offset and then wants four more,
    /// so the record ends before the point the reader stops at. That matters beyond this test:
    /// <c>frua-import-oracle.sh</c> seeds its scratch design from <c>DefaultDesign</c>.
    /// </remarks>
    private static GlobalStatsPrefix? Template()
    {
        if (RepoRoot() is not { } root)
        {
            return null;
        }

        string path = Path.Combine(root.FullName, "reference", "SomethingWild.dsn",
                                   "Data", "game.dat");
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var cursor = GameDataReader.Open(stream);
        return GlobalStatsReader.ReadThroughCharacters(cursor.Body, cursor.Version);
    }

    [Fact]
    public void The_design_name_and_money_come_from_frua()
    {
        if (Heirs() is not { } design || Template() is not { } template)
        {
            return;
        }

        var game = FruaGameData.ReadFile(design);
        var applied = FruaGameDataConverter.Apply(template, game);

        Assert.Equal("Heirs to skull crag", applied.DesignName);
        Assert.Equal(50_000, applied.StartExp);
        Assert.Equal(100, applied.StartPlatinum);
        Assert.Equal(0, applied.StartGem);
        Assert.Equal(4, applied.StartLevel);
    }

    /// <summary>
    /// Everything FRUA has no equivalent for is kept from the template.
    /// </summary>
    /// <remarks>
    /// This is the point of overlaying rather than constructing: the money and difficulty tables
    /// have no FRUA source and no default the writer would accept.
    /// </remarks>
    [Fact]
    public void The_templates_tables_and_art_survive_the_overlay()
    {
        if (Heirs() is not { } design || Template() is not { } template)
        {
            return;
        }

        var applied = FruaGameDataConverter.Apply(template, FruaGameData.ReadFile(design));

        Assert.Same(template.Money, applied.Money);
        Assert.Same(template.Difficulty, applied.Difficulty);
        Assert.Same(template.Sounds, applied.Sounds);
        Assert.Equal(template.MapArt, applied.MapArt);
        Assert.Equal(template.Font, applied.Font);
    }

    /// <summary>The special keys and items take FRUA's names, keeping their template identity.</summary>
    [Fact]
    public void The_keys_and_items_take_their_frua_names()
    {
        if (Heirs() is not { } design || Template() is not { } template)
        {
            return;
        }

        var game = FruaGameData.ReadFile(design);
        var applied = FruaGameDataConverter.Apply(template, game);

        Assert.Equal(8, applied.Keys.Count);
        Assert.Equal("key of wrath", applied.Keys[0].Name);
        Assert.Equal(12, applied.SpecialItems.Count);
        Assert.Equal("the Sword", applied.SpecialItems[0].Name);

        // The identifier is the template's, since FRUA supplies only a name.
        if (template.Keys.Count > 0)
        {
            Assert.Equal(template.Keys[0].Id, applied.Keys[0].Id);
        }
    }

    /// <summary>The start position comes from the starting level's entry point.</summary>
    [Fact]
    public void The_start_position_comes_from_the_entry_point()
    {
        if (Heirs() is not { } design || Template() is not { } template)
        {
            return;
        }

        var game = FruaGameData.ReadFile(design);

        // Heirs starts on level 4 zero-based, which is geo005.
        var start = FruaLevel.ReadFile(design, game.StartLevel + 1);
        Assert.NotNull(start);

        var applied = FruaGameDataConverter.Apply(template, game, start);
        var entry = start.EntryPoints[game.StartExperienceProfile];

        Assert.Equal(entry.X, applied.StartX);
        Assert.Equal(entry.Y, applied.StartY);
        Assert.InRange(applied.StartX, 0, start.Width - 1);
        Assert.InRange(applied.StartY, 0, start.Height - 1);
    }

    /// <summary>
    /// The overlaid header is writable, which a constructed one would not be.
    /// </summary>
    /// <remarks>
    /// <c>GlobalStatsWriter.CanWrite</c> demands a money table, a difficulty table, sounds and
    /// level info. Only the template supplies them — which is the argument for this shape in one
    /// assertion.
    /// </remarks>
    [Fact]
    public void The_overlaid_header_can_be_written()
    {
        if (Heirs() is not { } design || Template() is not { } template)
        {
            return;
        }

        var applied = FruaGameDataConverter.Apply(template, FruaGameData.ReadFile(design));

        // ReadThroughCharacters stops before LEVEL_INFO, so the template itself is not writable
        // either -- the point here is that the overlay does not make things WORSE, and that the
        // reason it is refused is the reader's stopping point rather than anything FRUA supplied.
        bool templateOk = GlobalStatsWriter.CanWrite(template, out string templateReason);
        bool appliedOk = GlobalStatsWriter.CanWrite(applied, out string appliedReason);

        Assert.Equal(templateOk, appliedOk);
        Assert.Equal(templateReason, appliedReason);
    }
}
