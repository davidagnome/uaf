namespace UAF.Media;

/// <summary>Which way a menu's items flow.</summary>
/// <remarks>
/// The ordinals are the original's <c>orient</c> field, which is compared against literal 0 and 1
/// throughout <c>GameMenu.cpp</c>. <b><see cref="Vertical"/> is the default</b> — <c>reset</c> sets
/// <c>orient=1</c> (<c>GameMenu.cpp:1337</c>), so a menu that never calls <c>setHorzOrient</c> is a
/// column.
/// </remarks>
public enum MenuOrientation
{
    Horizontal = 0,
    Vertical = 1,
}

/// <summary>
/// Where a menu anchors itself (<c>setStartCoord</c>, <c>GameMenu.cpp:1901</c>).
/// </summary>
/// <remarks>
/// The original encodes these as negative X values in <c>MENU_DATA_TYPE</c>, with any
/// <c>x &gt;= 0</c> meaning absolute coordinates. Kept as an enum because the negative-X trick is
/// exactly the kind of thing that reads as a bug at the call site.
/// </remarks>
public enum MenuAnchor
{
    /// <summary>Use the coordinates given, not one of the config anchors.</summary>
    Absolute = 0,

    /// <summary><c>DEFAULT_MENU_HORZ</c> — the bottom bar. <c>x == -1</c>.</summary>
    DefaultHorizontal = -1,

    /// <summary><c>DEFAULT_MENU_VERT</c>. <c>x == -2</c>.</summary>
    DefaultVertical = -2,

    /// <summary><c>DEFAULT_MENU_TEXTBOX</c> — mid-textbox, for question lists. <c>x == -3</c>.</summary>
    DefaultTextBox = -3,
}

/// <summary>
/// The four menu anchor points a design's config supplies
/// (<c>LoadConfigFile</c>, <c>Shared/Globals.cpp:2558-2580</c>).
/// </summary>
/// <remarks>
/// <b><see cref="CombatHorizontal"/> is null rather than a point when unset.</b> The original
/// pre-seeds <c>DEFAULT_MENU_COMBAT_HORZ_X = -1</c> before reading it and then tests
/// <c>&gt;= 0</c> to decide whether a combat menu overrides the normal horizontal anchor
/// (<c>GameMenu.cpp:1966</c>) — so "absent" is a real state, not a coordinate.
/// </remarks>
public sealed record MenuAnchors(
    (int X, int Y) Horizontal,
    (int X, int Y) Vertical,
    (int X, int Y) TextBox,
    (int X, int Y)? CombatHorizontal)
{
    /// <summary>The engine's own defaults, for a design that configures none of them.</summary>
    /// <remarks>
    /// All zero, because the globals are zero-initialised and <c>LoadConfigFile</c> only assigns
    /// when the token is present. A design that omits <c>DEFAULT_MENU_HORZ</c> really does get a
    /// menu in the top-left corner.
    /// </remarks>
    public static readonly MenuAnchors Default = new((0, 0), (0, 0), (0, 0), null);

    /// <summary>
    /// Reads the four anchors from a design's config, leaving any that are absent at their default.
    /// </summary>
    /// <param name="lookup">
    /// Resolves a config key to a point. Case-insensitive, matching the reference's
    /// <c>FindTokens</c>, which compares with <c>CompareNoCase</c>.
    /// </param>
    public static MenuAnchors FromConfig(Func<string, (int X, int Y)?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        return new MenuAnchors(
            lookup("DEFAULT_MENU_HORZ") ?? Default.Horizontal,
            lookup("DEFAULT_MENU_VERT") ?? Default.Vertical,
            lookup("DEFAULT_MENU_TEXTBOX") ?? Default.TextBox,
            lookup("DEFAULT_MENU_COMBAT_HORZ"));
    }

    /// <summary>
    /// Resolves an anchor to a point.
    /// </summary>
    /// <param name="combat">
    /// When true and <see cref="CombatHorizontal"/> is set, the horizontal anchor is replaced by
    /// it. It falls back to the normal one rather than to the origin when unset.
    /// </param>
    public (int X, int Y) Resolve(MenuAnchor anchor, int x = 0, int y = 0, bool combat = false) =>
        anchor switch
        {
            MenuAnchor.DefaultHorizontal => combat ? CombatHorizontal ?? Horizontal : Horizontal,
            MenuAnchor.DefaultVertical => Vertical,
            MenuAnchor.DefaultTextBox => TextBox,
            _ => (x, y),
        };
}

/// <summary>One selectable entry.</summary>
/// <param name="Text">
/// The label, in the same single-byte codepage everything else draws. <b>Colour tags are not
/// interpreted in a menu label</b> — see <see cref="MenuRenderer"/>.
/// </param>
/// <param name="ShortcutIndex">
/// Which byte of <paramref name="Text"/> is the keyboard shortcut, or −1 for none. An index rather
/// than a character, because the letter is drawn in a different colour in place.
/// </param>
/// <param name="Enabled">
/// Whether the entry is shown and selectable. The original calls this <c>displayItem</c> and drives
/// it from <c>setItemActive</c>/<c>setItemInactive</c>, so the two words mean one thing.
/// </param>
public sealed record MenuItem(byte[] Text, int ShortcutIndex = -1, bool Enabled = true)
{
    /// <summary>Where this entry was last laid out, for hit-testing.</summary>
    /// <remarks>
    /// Empty until the menu has been laid out at least once. The original computes these inside
    /// <c>DisplayMenu</c>, so a menu that has never been drawn hit-tests as nothing — reproduced,
    /// because a click arriving before the first frame must not select an entry that is not on
    /// screen yet.
    /// </remarks>
    public SurfaceRect Bounds { get; internal set; }
}

/// <summary>
/// A menu: a list of entries, a selection, and the geometry to draw and hit-test them
/// (<c>CMyMenu</c>, <c>UAFWin/GameMenu.cpp:1314</c>).
/// </summary>
/// <remarks>
/// <para>
/// The original is a single global instance (<c>CMyMenu menu</c>, <c>RunEvent.cpp:145</c>) reset
/// between uses, which is why <see cref="Reset"/> exists at all rather than construction doing the
/// job. This port allows several, but keeps <see cref="Reset"/> so the transcription reads the same
/// and so a caller can reuse one the way the engine does.
/// </para>
/// <para>
/// <b>Indices are 0-based here and the original mixes both.</b> <c>activeItem</c> is 0-based,
/// but <c>setCurrentItem</c>, <c>getMenuItem</c>, <c>isItemActive</c> and
/// <c>AttemptToCreateUniqueShortcut</c> all take 1-based ones. That inconsistency is a bug
/// generator, so the 1-based entry points are named
/// <see cref="SetCurrentItemOneBased"/> and marked, rather than silently kept.
/// </para>
/// </remarks>
public sealed class Menu
{
    /// <summary><c>Max_Menu_Items</c> (<c>GameMenu.h:344</c>).</summary>
    public const int MaxItems = 20;

    /// <summary><c>Max_Item_Len</c> (<c>GameMenu.h:345</c>) — labels are truncated to this.</summary>
    public const int MaxItemLength = 85;

    /// <summary><c>Item_Separation</c> (<c>GameMenu.h:343</c>), in pixels.</summary>
    public const int DefaultItemSeparation = 5;

    /// <summary>
    /// <c>activeItem</c>'s "nothing is selected" value (<c>GameMenu.cpp:1578</c>).
    /// </summary>
    /// <remarks>
    /// An arbitrary sentinel — the digits of π — checked by identity in three places. Kept rather
    /// than replaced with null because it is compared against directly and a design's event flow
    /// can set it; a nullable would read better and diverge on the comparisons.
    /// </remarks>
    public const int NoSelection = -314159;

    private readonly List<MenuItem> items = [];

    public Menu() => Reset();

    public IReadOnlyList<MenuItem> Items => items;

    public int Count => items.Count;

    /// <summary>The selected entry, 0-based, or <see cref="NoSelection"/>.</summary>
    public int ActiveItem { get; private set; }

    /// <summary>Whether the selection is drawn highlighted at all (<c>useActive</c>).</summary>
    public bool ShowSelection { get; set; } = true;

    public MenuOrientation Orientation { get; set; } = MenuOrientation.Vertical;

    public int StartX { get; private set; }

    public int StartY { get; private set; }

    /// <summary>
    /// Gap between entries. Grows once on a horizontal menu — see <see cref="MenuRenderer"/>.
    /// </summary>
    /// <remarks>
    /// Settable because events override it before display: a question list uses 2 and a question
    /// button row uses 7 (<c>RunEvent.cpp:13158</c>, <c>:13299</c>). The horizontal adjustment then
    /// applies on top of whatever was set, as it does to the default.
    /// </remarks>
    public int ItemSeparation { get; set; } = DefaultItemSeparation;

    /// <summary>Set once the separation has had its one-shot adjustment (<c>initCharSize</c>).</summary>
    internal bool CharSizeInitialized { get; set; }

    /// <summary>An optional heading drawn before the entries.</summary>
    public byte[]? Title { get; private set; }

    /// <summary>
    /// Where the title goes. When null it is drawn inline at the menu's own origin and pushes the
    /// entries along — see <see cref="MenuRenderer.Layout"/>.
    /// </summary>
    public (int X, int Y)? TitlePosition { get; set; }

    /// <summary>Whether keyboard shortcuts are active (<c>UseKeyboardShortcuts</c>, default true).</summary>
    public bool UseKeyboardShortcuts { get; set; } = true;

    /// <summary>Clears everything back to the state <c>reset</c> leaves (<c>GameMenu.cpp:1323</c>).</summary>
    public void Reset()
    {
        items.Clear();
        ShowSelection = true;
        CharSizeInitialized = false;
        Orientation = MenuOrientation.Vertical;
        Title = null;
        TitlePosition = null;
        StartX = 0;
        StartY = 0;
        ActiveItem = 0;
        ItemSeparation = DefaultItemSeparation;
    }

    /// <summary>Places the menu, resolving a config anchor if one is named.</summary>
    public void SetStartCoord(MenuAnchor anchor, MenuAnchors anchors, int x = 0, int y = 0,
                              bool combat = false)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        (StartX, StartY) = anchors.Resolve(anchor, x, y, combat);
    }

    /// <summary>Places the menu at absolute coordinates.</summary>
    public void SetStartCoord(int x, int y) => (StartX, StartY) = (x, y);

    public void SetTitle(string? title) =>
        Title = string.IsNullOrEmpty(title) ? null : BitmapFont.Encode(title);

    /// <summary>
    /// Appends an entry, truncated to <see cref="MaxItemLength"/>.
    /// </summary>
    /// <returns>The new entry's 1-based position, or −1 when the menu is full.</returns>
    /// <remarks>
    /// The 1-based return is the original's (<c>addMenuItem</c> returns <c>numItems</c>), and it
    /// feeds straight into <see cref="SetCurrentItemOneBased"/>, so converting it here would break
    /// the pairing rather than fix it.
    /// </remarks>
    public int AddItem(string text, int shortcutIndex = -1)
    {
        if (items.Count >= MaxItems)
        {
            return -1;
        }

        var encoded = BitmapFont.Encode(text ?? string.Empty);
        if (encoded.Length > MaxItemLength - 1)
        {
            encoded = encoded[..(MaxItemLength - 1)];
        }

        items.Add(new MenuItem(encoded, shortcutIndex));
        return items.Count;
    }

    /// <summary>Removes the last entry, moving the selection off it if it was selected.</summary>
    public void DeleteLastItem()
    {
        if (items.Count == 0)
        {
            return;
        }

        items.RemoveAt(items.Count - 1);
        if (ActiveItem >= items.Count || (ActiveItem >= 0 && !items[ActiveItem].Enabled))
        {
            ActiveItem = FirstEnabled();
        }
    }

    public void SetItemEnabled(int index, bool enabled)
    {
        if ((uint)index < (uint)items.Count)
        {
            items[index] = items[index] with { Enabled = enabled };
        }
    }

    public void SetAllItemsEnabled(bool enabled)
    {
        for (int i = 0; i < items.Count; i++)
        {
            items[i] = items[i] with { Enabled = enabled };
        }
    }

    /// <summary>The first enabled entry, or 0 when there is none.</summary>
    /// <remarks>
    /// The original returns −1 for "none found" and every caller immediately rewrites that to 0, so
    /// the rewrite happens here instead of at five call sites.
    /// </remarks>
    private int FirstEnabled()
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Enabled)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Puts the selection back in range if it has fallen out of it.</summary>
    private void NormalizeActive()
    {
        if (ActiveItem < 0 || ActiveItem >= items.Count)
        {
            ActiveItem = FirstEnabled();
        }
    }

    /// <summary>
    /// Moves the selection forward, wrapping and skipping disabled entries
    /// (<c>nextItem</c>, <c>GameMenu.cpp:1494</c>).
    /// </summary>
    /// <remarks>
    /// A menu with nothing enabled leaves the selection where it was rather than moving it to a
    /// disabled entry — the original counts a full loop and restores the old value.
    /// </remarks>
    public void NextItem()
    {
        if (items.Count == 0)
        {
            return;
        }

        NormalizeActive();
        int previous = ActiveItem;

        ActiveItem = (ActiveItem + 1) % items.Count;

        int count = 0;
        while (count < items.Count && !items[ActiveItem].Enabled)
        {
            ActiveItem = (ActiveItem + 1) % items.Count;
            count++;
        }

        if (count == items.Count)
        {
            ActiveItem = previous;
        }
    }

    /// <summary>
    /// Moves the selection backward (<c>prevItem</c>, <c>GameMenu.cpp:1524</c>).
    /// </summary>
    /// <remarks>
    /// <b>Not a mirror of <see cref="NextItem"/>.</b> It wraps from the first <i>enabled</i> entry
    /// to the last entry — enabled or not — and then walks back to an enabled one, where
    /// <see cref="NextItem"/> wraps to index 0 and walks forward. The visible result is the same;
    /// the intermediate state is not, which matters only if something observes it mid-step.
    /// </remarks>
    public void PrevItem()
    {
        if (items.Count == 0)
        {
            return;
        }

        NormalizeActive();

        int firstEnabled = 0;
        while (firstEnabled < items.Count && !items[firstEnabled].Enabled)
        {
            firstEnabled++;
        }

        if (firstEnabled == items.Count)
        {
            return;
        }

        ActiveItem = ActiveItem == firstEnabled ? items.Count - 1 : ActiveItem - 1;

        int count = 0;
        while (count < items.Count && !items[ActiveItem].Enabled)
        {
            ActiveItem--;
            if (ActiveItem < 0)
            {
                ActiveItem = items.Count - 1;
            }

            count++;
        }

        if (count == items.Count)
        {
            ActiveItem = firstEnabled;
        }
    }

    /// <summary>Selects an entry by its 0-based index, or <see cref="NoSelection"/>.</summary>
    public void SetCurrentItem(int index)
    {
        if (index == NoSelection)
        {
            ActiveItem = NoSelection;
            return;
        }

        if ((uint)index < (uint)items.Count)
        {
            ActiveItem = index;
        }

        if (ActiveItem >= 0 && ActiveItem < items.Count && !items[ActiveItem].Enabled)
        {
            ActiveItem = FirstEnabled();
        }
    }

    /// <summary>
    /// Selects an entry by the 1-based number <see cref="AddItem"/> returned
    /// (<c>setCurrentItem</c>, <c>GameMenu.cpp:1577</c>).
    /// </summary>
    /// <remarks>
    /// <b>The original silently ignores 0.</b> Its guard is <c>item &gt; 0</c>, so passing the
    /// 0-based index of the first entry leaves the selection untouched instead of selecting it.
    /// Preserved: call sites pass <c>addMenuItem</c>'s return value, which is never 0.
    /// </remarks>
    public void SetCurrentItemOneBased(int item)
    {
        if (item == NoSelection)
        {
            ActiveItem = NoSelection;
            return;
        }

        if (item > 0 && item <= MaxItems)
        {
            SetCurrentItem(item - 1);
        }
    }

    /// <summary>
    /// Finds the entry whose shortcut letter is <paramref name="key"/>
    /// (<c>LookupShortcut</c>, <c>GameMenu.cpp:2017</c>).
    /// </summary>
    /// <returns>A 0-based index, or −1.</returns>
    /// <remarks>Case-insensitive, as <c>strnicmp</c> makes it.</remarks>
    public int LookupShortcut(char key)
    {
        if (!UseKeyboardShortcuts)
        {
            return -1;
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!item.Enabled || item.ShortcutIndex < 0
                || item.ShortcutIndex >= item.Text.Length)
            {
                continue;
            }

            if (char.ToUpperInvariant((char)item.Text[item.ShortcutIndex])
                == char.ToUpperInvariant(key))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether every entry starts with a different letter
    /// (<c>FirstLettersUnique</c>, <c>GameMenu.cpp:2034</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Disabled entries still collide.</b> The outer loop skips them, the inner one does not
    /// (<c>for (y=i+1; y&lt;numItems; y++)</c> with no <c>displayItem</c> test), so a hidden entry
    /// sharing a first letter with a visible one suppresses shortcuts for the whole menu.
    /// </para>
    /// <para>
    /// The comparison is also case-<i>sensitive</i> here while <see cref="LookupShortcut"/>'s is
    /// not, so "Buy" and "buy" are treated as distinct when assigning and identical when pressed.
    /// Both are transcribed as they are.
    /// </para>
    /// </remarks>
    public bool FirstLettersUnique()
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (!items[i].Enabled)
            {
                continue;
            }

            for (int y = i + 1; y < items.Count; y++)
            {
                if (First(items[i]) == First(items[y]))
                {
                    return false;
                }
            }
        }

        return true;

        static byte First(MenuItem item) => item.Text.Length > 0 ? item.Text[0] : (byte)0;
    }

    /// <summary>
    /// Makes each entry's first letter its shortcut, when they are all distinct
    /// (<c>SetFirstLettersShortcuts</c>, <c>GameMenu.cpp:2093</c>).
    /// </summary>
    /// <param name="onlyWhenUnset">
    /// Leave entries that already have a shortcut alone. False overwrites all of them.
    /// </param>
    /// <remarks>
    /// <b>A one-entry menu skips the uniqueness test entirely</b> and always takes the first
    /// letter, since there is nothing to collide with.
    /// </remarks>
    public void SetFirstLetterShortcuts(bool onlyWhenUnset = true)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (items.Count == 1)
        {
            items[0] = items[0] with { ShortcutIndex = 0 };
            return;
        }

        if (!FirstLettersUnique())
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (onlyWhenUnset && items[i].ShortcutIndex >= 0)
            {
                continue;
            }

            items[i] = items[i] with { ShortcutIndex = 0 };
        }
    }

    /// <summary>
    /// Gives one entry a shortcut letter no other entry is already using
    /// (<c>AttemptToCreateUniqueShortcut</c>, <c>GameMenu.cpp:2050</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Walks the label for the first A–Z letter that is free, so "Cast Spell" takes 'C', or 'a' if
    /// something else already holds 'C'. Non-letters are skipped rather than ending the search.
    /// </para>
    /// <para>
    /// Does nothing when the entry already has a shortcut or is disabled — the original's reasoning
    /// being that a disabled entry needs none.
    /// </para>
    /// </remarks>
    public void AttemptUniqueShortcut(int index)
    {
        if ((uint)index >= (uint)items.Count)
        {
            return;
        }

        var target = items[index];
        if (target.ShortcutIndex >= 0 || !target.Enabled)
        {
            return;
        }

        var taken = new bool[26];
        foreach (var item in items)
        {
            if (!item.Enabled || item.ShortcutIndex < 0
                || item.ShortcutIndex >= item.Text.Length)
            {
                continue;
            }

            if (LetterSlot(item.Text[item.ShortcutIndex]) is int slot)
            {
                taken[slot] = true;
            }
        }

        for (int i = 0; i < target.Text.Length; i++)
        {
            if (LetterSlot(target.Text[i]) is int slot && !taken[slot])
            {
                items[index] = target with { ShortcutIndex = i };
                return;
            }
        }
    }

    /// <summary>A–Z index for a byte, upper-casing it first, or null if it is not a letter.</summary>
    private static int? LetterSlot(byte value)
    {
        char letter = (char)value;
        if (letter >= 'a')
        {
            letter = (char)(letter - ('a' - 'A'));
        }

        return letter is >= 'A' and <= 'Z' ? letter - 'A' : null;
    }

    /// <summary>
    /// The entry under a point, or −1 (<c>IntersectPointWithMenu</c>, <c>GameMenu.cpp:1983</c>).
    /// </summary>
    /// <remarks>
    /// Reads the bounds the last layout produced, so a menu that has not been drawn hit-tests as
    /// nothing.
    /// </remarks>
    public int HitTest(int x, int y)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Enabled && item.Bounds.Width > 0
                && x >= item.Bounds.Left && x < item.Bounds.Right
                && y >= item.Bounds.Top && y < item.Bounds.Bottom)
            {
                return i;
            }
        }

        return -1;
    }
}
