using UAF.Serialization;

namespace UAFcore;

/// <summary>Where a character on the roster came from.</summary>
public enum RosterSource
{
    /// <summary>A <c>.chr</c> file in the design's save directory.</summary>
    SavedFile,

    /// <summary>A pre-generated NPC out of the design's own character list.</summary>
    PreGenerated,
}

/// <summary>One name on the roster, and whether it is marked to join.</summary>
public sealed record RosterEntry(string Name, RosterSource Source, string? Path, int DesignIndex)
{
    /// <summary>Whether this character is marked for the party.</summary>
    public bool InParty { get; set; }
}

/// <summary>
/// The list behind ADD CHARACTER (<c>ADD_CHARACTER_DATA</c>, <c>RunEvent.cpp:3192</c>): every
/// character available to join, marked or not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two sources, one list.</b> Every <c>.chr</c> in the save directory, plus every character in
/// the design's own list flagged <c>IsPreGen</c>. The saved files are characters the player made
/// or kept; the pre-gens are the ones the designer shipped.
/// </para>
/// <para>
/// <b>Sorted case-insensitively, on purpose.</b> The reference bubble-sorts by name with the
/// comment "so that their order will not depend on the operating system that supplied the file
/// names" — directory enumeration order is not stable across platforms, and a roster that
/// reorders itself between machines is a roster a player cannot learn.
/// </para>
/// <para>
/// <b>Nothing happens until EXIT.</b> Selecting a name toggles a mark; only leaving the screen
/// adds the marked and removes the unmarked. So a player can browse the whole roster and change
/// their mind, and a mis-click costs nothing.
/// </para>
/// </remarks>
public sealed class CharacterRoster
{
    private readonly List<RosterEntry> entries = [];

    /// <summary>The prefix an NPC's saved file carries (<c>Char.cpp:6892</c>).</summary>
    public const string NpcFilePrefix = "DCNPC_";

    /// <summary>The roster, sorted.</summary>
    public IReadOnlyList<RosterEntry> Entries => entries;

    public int Count => entries.Count;

    /// <summary>
    /// Builds the roster from a save directory and a design's character list.
    /// </summary>
    /// <param name="inParty">Names already in the party, which start marked.</param>
    public static CharacterRoster Build(string? saveDirectory,
                                        IReadOnlyList<CharacterRecord> designCharacters,
                                        IEnumerable<string>? inParty = null)
    {
        ArgumentNullException.ThrowIfNull(designCharacters);

        var roster = new CharacterRoster();

        if (saveDirectory is not null && Directory.Exists(saveDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(saveDirectory, "*.chr"))
            {
                string name = Path.GetFileNameWithoutExtension(path);

                // An NPC's file is prefixed; the roster shows the character's name, not the file's.
                if (name.StartsWith(NpcFilePrefix, StringComparison.Ordinal))
                {
                    name = name[NpcFilePrefix.Length..];
                }

                roster.entries.Add(new RosterEntry(name, RosterSource.SavedFile, path, -1));
            }
        }

        for (int i = 0; i < designCharacters.Count; i++)
        {
            if (designCharacters[i].IsPreGenerated != 0)
            {
                roster.entries.Add(
                    new RosterEntry(designCharacters[i].Name, RosterSource.PreGenerated, null, i));
            }
        }

        // The reference's bubble sort, which is a stable-enough ordering by name and nothing more.
        roster.entries.Sort(
            (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var joined = inParty is null
            ? []
            : new HashSet<string>(inParty, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in roster.entries)
        {
            entry.InParty = joined.Contains(entry.Name);
        }

        return roster;
    }

    /// <summary>Toggles whether a character is marked to join.</summary>
    public void Toggle(int index)
    {
        if (index >= 0 && index < entries.Count)
        {
            entries[index].InParty = !entries[index].InParty;
        }
    }
}

/// <summary>What one line of the roster menu is.</summary>
public enum RosterLine
{
    /// <summary>A character; <see cref="RosterMenuLine.Index"/> says which.</summary>
    Character,

    /// <summary>Back a page.</summary>
    Previous,

    /// <summary>On a page.</summary>
    Next,

    /// <summary>Leave, applying every mark.</summary>
    Exit,
}

/// <summary>One menu line: its label, what it does, and which entry it stands for.</summary>
public sealed record RosterMenuLine(string Label, RosterLine Kind, int Index);

/// <summary>
/// Lays the roster out as a menu (<c>ADD_CHARACTER_DATA::FillMenu</c>, <c>RunEvent.cpp:3117</c>).
/// </summary>
/// <remarks>
/// <b>The paging entries are part of the list, not a separate control strip.</b> <c>&lt;--- PREV</c>
/// takes the first line when there is a page behind, <c>NEXT ---&gt;</c> takes the last-but-one when
/// there is a page ahead, and <c>EXIT</c> is always last — so how many characters fit depends on
/// which of those are showing. That is the opposite of the inventory, where paging is a fixed menu
/// of commands beside a fixed list, and it is why this arithmetic lives in one place with a name.
/// </remarks>
public static class RosterMenu
{
    /// <summary>Builds the lines showing, starting at <paramref name="first"/>.</summary>
    public static List<RosterMenuLine> Lines(CharacterRoster roster, int first, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(roster);

        var lines = new List<RosterMenuLine>();

        if (first > 0)
        {
            lines.Add(new RosterMenuLine("<--- PREV", RosterLine.Previous, -1));
        }

        // Room for EXIT, and for PREV if it is showing.
        int room = pageSize - lines.Count - 1;
        int remaining = roster.Count - first;

        bool needsNext = remaining > room;
        int showing = needsNext ? room - 1 : remaining;

        for (int i = 0; i < showing; i++)
        {
            var entry = roster.Entries[first + i];

            // A marked character is starred. Nothing else distinguishes one, so the star is the
            // whole of the screen's feedback.
            lines.Add(new RosterMenuLine(entry.InParty ? $"* {entry.Name}" : entry.Name,
                                         RosterLine.Character, first + i));
        }

        if (needsNext)
        {
            lines.Add(new RosterMenuLine("NEXT --->", RosterLine.Next, -1));
        }

        lines.Add(new RosterMenuLine("EXIT", RosterLine.Exit, -1));
        return lines;
    }

    /// <summary>
    /// Where the previous page starts (<c>RunEvent.cpp:3031</c>).
    /// </summary>
    /// <remarks>
    /// <b>Landing on 1 is corrected to 0.</b> Stepping back by a page can leave the first index at
    /// exactly 1, which would show a <c>PREV</c> line for a single character behind it; the
    /// reference special-cases it to zero rather than let that happen.
    /// </remarks>
    public static int PreviousPage(int first, int pageSize)
    {
        int back = first - (pageSize - 2);
        return back <= 1 ? 0 : back;
    }
}
