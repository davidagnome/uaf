using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// A level's number against its position in the directory listing.
/// </summary>
/// <remarks>
/// <para>
/// <b>They are different things and a shipped design proves it.</b> A level file is named for its
/// index plus one (<c>Shared/Level.cpp:3643</c>) and a design may skip numbers — <c>Case.dsn</c>
/// ships ten levels numbered 001–004, 011–013, 016, 018 and 255. Its last file sits at
/// <i>position</i> nine and is <i>level</i> 255.
/// </para>
/// <para>
/// <b>The confusion survived a long time because almost nothing exposes it.</b> Every other design
/// used to test this port is numbered from 1 with no gaps, and on such a design position and
/// index agree everywhere. These tests exist to keep the distinction from quietly collapsing
/// again.
/// </para>
/// </remarks>
public class LevelNumberingTests
{
    private static string? Corpus(string design)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? root = dir is null ? null : Path.Combine(dir.FullName, "reference", design);
        return root is not null && Directory.Exists(root) ? root : null;
    }

    private static LoadedDesign? Open(string design) =>
        Corpus(design) is { } root
            ? LoadedDesign.Open(root, role: ArchiveRole.Editor)
            : null;

    /// <summary>
    /// The premise: <c>Case</c> really is numbered with holes.
    /// </summary>
    /// <remarks>
    /// Everything below is vacuous on a design without gaps, so this is what stops the file
    /// passing while proving nothing.
    /// </remarks>
    [Fact]
    public void The_corpus_has_a_design_numbered_with_holes()
    {
        if (Open("Case.dsn") is not { } design)
        {
            return;
        }

        using (design)
        {
            var names = design.LevelFiles.Select(Path.GetFileName).ToList();

            Assert.Equal(10, names.Count);
            Assert.Equal("Level255.lvl", names[^1]);

            // The hole itself: position 9 is level 255, so the two spaces cannot be the same.
            Assert.Null(design.LevelFileFor(9));
        }
    }

    /// <summary>
    /// Looking a level up by number finds the file named for it.
    /// </summary>
    [Fact]
    public void A_level_is_found_by_its_number_not_its_position()
    {
        if (Open("Case.dsn") is not { } design)
        {
            return;
        }

        using (design)
        {
            // Level 255 is index 254, and it is the tenth file.
            Assert.Equal("Level255.lvl", Path.GetFileName(design.LevelFileFor(254)));

            // Position nine is that same file; index nine is nothing at all.
            Assert.Equal("Level255.lvl", Path.GetFileName(design.LevelFiles[9]));
            Assert.Null(design.LevelFileFor(9));

            // And the low numbers really are the low files.
            Assert.Equal("Level001.lvl", Path.GetFileName(design.LevelFileFor(0)));
            Assert.Equal("Level011.lvl", Path.GetFileName(design.LevelFileFor(10)));
        }
    }

    /// <summary>
    /// The engine walks onto a level by number, and gets the level that number names.
    /// </summary>
    /// <remarks>
    /// <b>This is the bug the distinction was hiding.</b> <c>Game.LevelIndex</c> is an index
    /// everywhere it is used — it keys <c>LEVEL_INFO</c>, it is what a script's
    /// <c>$CurrentLevel</c> reports plus one — but <c>LoadLevel</c> passed it to a lookup that
    /// wanted a directory position. On <c>Case</c> that loaded the wrong level or none.
    /// </remarks>
    [Fact]
    public void The_engine_loads_the_level_its_index_names()
    {
        if (Open("Case.dsn") is not { } design)
        {
            return;
        }

        using (design)
        {
            // Index 254 is level 255, the tenth and last file. A position-based lookup would have
            // refused it outright, there being no position 254.
            var byNumber = design.LevelNumbered(254);
            var byPosition = design.Level(9);

            Assert.NotNull(byNumber);
            Assert.NotNull(byPosition);
            Assert.Equal(byPosition!.Width, byNumber!.Width);
            Assert.Equal(byPosition.Height, byNumber.Height);

            // An index the design does not have is nothing, rather than whatever sits at that
            // position -- index 9 exists as a position and not as a level.
            Assert.Null(design.LevelNumbered(9));
            Assert.NotNull(design.Level(9));
        }
    }

    /// <summary>The map-only fallback answers by number too.</summary>
    /// <remarks>
    /// Movement uses the grid alone, so a level that will not fully decode still walks — but only
    /// if the fallback agrees with the main path about which level it is.
    /// </remarks>
    [Fact]
    public void The_map_fallback_agrees_with_the_full_read()
    {
        if (Open("Case.dsn") is not { } design)
        {
            return;
        }

        using (design)
        {
            var level = design.LevelNumbered(254);
            var map = design.MapNumbered(254);

            Assert.NotNull(level);
            Assert.NotNull(map);
            Assert.Equal(level!.Width, map!.Width);
            Assert.Equal(level.Height, map.Height);

            Assert.Null(design.MapNumbered(9));
        }
    }

    /// <summary>
    /// On a design numbered without gaps the two agree, which is why this went unnoticed.
    /// </summary>
    [Fact]
    public void Without_gaps_number_and_position_agree()
    {
        if (Open("SomethingWild.dsn") is not { } design)
        {
            return;
        }

        using (design)
        {
            for (int position = 0; position < design.LevelFiles.Count; position++)
            {
                Assert.Equal(design.LevelFiles[position], design.LevelFileFor(position));
            }
        }
    }
}
