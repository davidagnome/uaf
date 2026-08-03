using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the roster behind ADD CHARACTER: where the names come from, how they sort, and how the
/// paging entries share the menu with them.
/// </summary>
public class CharacterRosterTests : IDisposable
{
    private readonly string scratch =
        Path.Combine(Path.GetTempPath(), $"uaf-roster-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static CharacterRecord Member(string name, int preGenerated = 1) =>
        new(0, 0, 0, "human", 0, "fighter", 0, 0, 0, "", 0, name, "",
            0, 0, 0, 0, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, [], [], [], preGenerated, 0, 0, null, 0,
            null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic,
            new ItemList([], ReadyItems.Empty), new SpecabBlock([], [], []), []);

    /// <summary>Drops a file with the right extension; the roster reads names, not contents.</summary>
    private string Touch(string fileName)
    {
        Directory.CreateDirectory(scratch);
        string path = Path.Combine(scratch, fileName);
        File.WriteAllText(path, "");
        return path;
    }

    // ---- where the names come from -------------------------------------------------------------

    [Fact]
    public void An_empty_design_and_no_saves_is_an_empty_roster()
    {
        Assert.Equal(0, CharacterRoster.Build(null, []).Count);
        Assert.Equal(0, CharacterRoster.Build(scratch, []).Count);
    }

    [Fact]
    public void Only_the_designs_pre_generated_characters_are_offered()
    {
        var roster = CharacterRoster.Build(
            null, [Member("Aramil"), Member("Bruenor", preGenerated: 0), Member("Cattie")]);

        Assert.Equal(["Aramil", "Cattie"], roster.Entries.Select(e => e.Name));
        Assert.All(roster.Entries, e => Assert.Equal(RosterSource.PreGenerated, e.Source));
    }

    [Fact]
    public void Saved_files_join_the_designs_own_characters()
    {
        Touch("Zoltan.chr");

        var roster = CharacterRoster.Build(scratch, [Member("Aramil")]);

        Assert.Equal(["Aramil", "Zoltan"], roster.Entries.Select(e => e.Name));
        Assert.Equal(RosterSource.SavedFile, roster.Entries[1].Source);
        Assert.NotNull(roster.Entries[1].Path);
    }

    [Fact]
    public void An_npcs_file_prefix_is_not_part_of_its_name()
    {
        // A saved NPC's file is DCNPC_<name>.chr; the roster shows the character, not the file.
        Touch($"{CharacterRoster.NpcFilePrefix}Kagain.chr");

        var roster = CharacterRoster.Build(scratch, []);

        Assert.Equal("Kagain", roster.Entries[0].Name);
    }

    [Fact]
    public void Files_that_are_not_characters_are_ignored()
    {
        Touch("SaveA.pty");
        Touch("notes.txt");
        Touch("Zoltan.chr");

        Assert.Equal(["Zoltan"], CharacterRoster.Build(scratch, []).Entries.Select(e => e.Name));
    }

    [Fact]
    public void The_roster_sorts_by_name_ignoring_case()
    {
        // The reference sorts "so that their order will not depend on the operating system that
        // supplied the file names" -- directory order is not stable across platforms.
        Touch("zoltan.chr");
        Touch("Aramil.chr");

        var roster = CharacterRoster.Build(scratch, [Member("mordenkainen")]);

        Assert.Equal(["Aramil", "mordenkainen", "zoltan"], roster.Entries.Select(e => e.Name));
    }

    [Fact]
    public void Characters_already_in_the_party_start_marked()
    {
        var roster = CharacterRoster.Build(null, [Member("Aramil"), Member("Cattie")],
                                           inParty: ["cattie"]);

        Assert.False(roster.Entries[0].InParty);
        Assert.True(roster.Entries[1].InParty);
    }

    [Fact]
    public void Toggling_flips_one_mark_and_leaves_the_rest()
    {
        var roster = CharacterRoster.Build(null, [Member("Aramil"), Member("Cattie")]);

        roster.Toggle(1);

        Assert.False(roster.Entries[0].InParty);
        Assert.True(roster.Entries[1].InParty);

        roster.Toggle(1);
        Assert.False(roster.Entries[1].InParty);
    }

    [Fact]
    public void Toggling_outside_the_list_changes_nothing()
    {
        var roster = CharacterRoster.Build(null, [Member("Aramil")]);

        roster.Toggle(5);
        roster.Toggle(-1);

        Assert.False(roster.Entries[0].InParty);
    }

    // ---- the menu ------------------------------------------------------------------------------

    private static CharacterRoster Of(int count) =>
        CharacterRoster.Build(null, [.. Enumerable.Range(0, count).Select(i => Member($"c{i:00}"))]);

    [Fact]
    public void A_short_roster_is_the_names_and_EXIT()
    {
        var lines = RosterMenu.Lines(Of(3), first: 0, pageSize: 14);

        Assert.Equal(["c00", "c01", "c02", "EXIT"], lines.Select(l => l.Label));
        Assert.Equal(RosterLine.Exit, lines[^1].Kind);
    }

    [Fact]
    public void A_marked_character_is_starred()
    {
        var roster = Of(2);
        roster.Toggle(1);

        var lines = RosterMenu.Lines(roster, first: 0, pageSize: 14);

        Assert.Equal(["c00", "* c01", "EXIT"], lines.Select(l => l.Label));
    }

    [Fact]
    public void A_long_roster_makes_room_for_NEXT()
    {
        // A page of 5 holds EXIT, NEXT and three names -- the paging entries are part of the list,
        // not a control strip beside it.
        var lines = RosterMenu.Lines(Of(10), first: 0, pageSize: 5);

        Assert.Equal(["c00", "c01", "c02", "NEXT --->", "EXIT"], lines.Select(l => l.Label));
    }

    [Fact]
    public void A_later_page_makes_room_for_PREV_as_well()
    {
        var lines = RosterMenu.Lines(Of(10), first: 3, pageSize: 5);

        Assert.Equal(["<--- PREV", "c03", "c04", "NEXT --->", "EXIT"],
                     lines.Select(l => l.Label));
    }

    [Fact]
    public void The_last_page_drops_NEXT_and_shows_one_more_name()
    {
        var lines = RosterMenu.Lines(Of(6), first: 3, pageSize: 5);

        Assert.Equal(["<--- PREV", "c03", "c04", "c05", "EXIT"], lines.Select(l => l.Label));
    }

    [Fact]
    public void A_line_knows_which_entry_it_stands_for()
    {
        // The page offset means a line's position is not the entry's index -- the same trap the
        // inventory has, and the reason a line carries one.
        var lines = RosterMenu.Lines(Of(10), first: 3, pageSize: 5);

        Assert.Equal(3, lines[1].Index);
        Assert.Equal(4, lines[2].Index);
        Assert.Equal(-1, lines[0].Index);           // PREV stands for no entry
    }

    [Fact]
    public void Stepping_back_never_leaves_one_character_behind()
    {
        // Landing on 1 would show a PREV line for a single character; the reference corrects it
        // to zero rather than allow that.
        Assert.Equal(0, RosterMenu.PreviousPage(first: 4, pageSize: 5));
        Assert.Equal(0, RosterMenu.PreviousPage(first: 3, pageSize: 5));
        Assert.Equal(0, RosterMenu.PreviousPage(first: 2, pageSize: 5));
        Assert.Equal(5, RosterMenu.PreviousPage(first: 8, pageSize: 5));
    }

    [Fact]
    public void Paging_forward_and_back_returns_to_the_start()
    {
        var roster = Of(10);
        int first = 0;

        first += RosterMenu.Lines(roster, first, 5).Count(l => l.Kind is RosterLine.Character);
        Assert.Equal(3, first);

        Assert.Equal(0, RosterMenu.PreviousPage(first, 5));
    }
}
