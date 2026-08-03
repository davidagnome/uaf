namespace UAFcore;

/// <summary>One save slot: its letter, its file, and whether anything is in it.</summary>
public sealed record SaveSlot(int Index, string Letter, string FileName, bool Exists);

/// <summary>
/// The ten save slots (<c>SaveGameMenu</c>, <c>GameMenu.cpp:1113</c>) and the files behind them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Save and load are the same eleven-entry menu.</b> <c>SaveMenuData</c> and
/// <c>LoadMenuData</c> are two <c>MENU_DATA_TYPE</c>s pointing at one <c>SaveGameMenu</c> array,
/// so the slot letters can never drift apart between the two screens — and neither can be given
/// an extra entry without giving it to both.
/// </para>
/// <para>
/// <b><c>MAX_SAVE_GAME_SLOTS</c> is derived from the menu, not the other way round</b> — it is
/// <c>#define</c>d as <c>SaveGameMenuItems-1</c> (<c>GameMenu.h:296</c>). The number of saves a
/// player may keep is therefore a fact about a menu table, and adding a letter to that table is
/// all it would take to change it.
/// </para>
/// </remarks>
public static class SaveSlots
{
    /// <summary><c>MAX_SAVE_GAME_SLOTS</c> — the menu's eleven entries less EXIT.</summary>
    public const int Count = 10;

    /// <summary>The menu both screens show: ten letters and a way out.</summary>
    public static readonly (string Label, int Shortcut)[] Menu =
        [.. Enumerable.Range(0, Count).Select(i => (Letter(i), 0)), ("EXIT", 1)];

    /// <summary>The entry that leaves, which is also what Escape selects.</summary>
    public const int Exit = Count;

    /// <summary>Whether a number names one of the ten slots.</summary>
    public static bool IsValidIndex(int index) => index >= 0 && index < Count;

    /// <summary>A slot's letter: 0 is A (<c>'A' + num</c>, <c>Dgngame.cpp:103</c>).</summary>
    public static string Letter(int index) => ((char)('A' + index)).ToString();

    /// <summary>A slot's file name — <c>SaveA.pty</c> through <c>SaveJ.pty</c>.</summary>
    public static string FileName(int index) => $"Save{Letter(index)}.pty";

    /// <summary>
    /// The slots under a save directory, in menu order (<c>SaveGameExists</c>,
    /// <c>Globals.cpp:3619</c>).
    /// </summary>
    /// <remarks>
    /// A missing directory is ten empty slots rather than an error — a design that has never been
    /// played has no <c>Saves</c> folder, and the load screen has to draw something.
    /// </remarks>
    public static List<SaveSlot> Under(string? saveDirectory)
    {
        var slots = new List<SaveSlot>(Count);
        for (int i = 0; i < Count; i++)
        {
            string file = FileName(i);
            bool exists = saveDirectory is not null
                          && File.Exists(Path.Combine(saveDirectory, file));

            slots.Add(new SaveSlot(i, Letter(i), file, exists));
        }
        return slots;
    }

    /// <summary>Whether any slot holds a game — what the load screen's wording turns on.</summary>
    public static bool Any(IReadOnlyList<SaveSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        return slots.Any(s => s.Exists);
    }
}
