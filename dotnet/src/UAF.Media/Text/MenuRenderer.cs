namespace UAF.Media;

/// <summary>
/// The colours a menu draws with.
/// </summary>
/// <param name="Text">Unselected entries. <c>defTextColorNum</c>, white by default.</param>
/// <param name="HighlightInk">
/// The selected entry's glyphs. Black, because the selection is reverse video.
/// </param>
/// <param name="HighlightBackground">
/// The bar painted behind the selected entry. White.
/// </param>
/// <param name="Shortcut">
/// The shortcut letter. <c>KeyboardCharColor</c>, whose default is 251,133,15
/// (<c>Shared/Globals.cpp:568</c>) and which <c>COLOR_KEYBOARD_SHORTCUT</c> overrides.
/// </param>
/// <remarks>
/// <b>The original expresses all of this as four pre-rasterised fonts, not as colours.</b>
/// <c>HighlightFont</c> is built with foreground black on background white,
/// <c>KeyboardFont</c> with the shortcut colour on the normal background, and
/// <c>KeyboardHighlightFont</c> with the shortcut colour on white
/// (<c>GlobalData.cpp:5900-5926</c>) — GDI baked the colour into the glyphs, so a colour pair
/// needed its own font. Tinting one atlas gives the same result, for the same reason the text layer
/// does it.
/// </remarks>
public sealed record MenuPalette(
    uint Text = 0xFFFFFFFF,
    uint HighlightInk = 0xFF000000,
    uint HighlightBackground = 0xFFFFFFFF,
    uint Shortcut = 0xFFFB850F)
{
    public static readonly MenuPalette Default = new();
}

/// <summary>
/// Lays out and draws a <see cref="Menu"/> (<c>CMyMenu::DisplayMenu</c>,
/// <c>UAFWin/GameMenu.cpp:1661</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Colour tags are not interpreted in a menu label.</b> <c>DrawFont</c> turns them off for the
/// duration and restores the previous setting afterwards (<c>GameMenu.cpp:1885</c>), so a label
/// containing <c>/R</c> draws those two characters literally. This is the one place in the engine
/// where text is drawn without the markup applying, and getting it wrong would silently eat two
/// characters from any label with a slash in it.
/// </para>
/// <para>
/// <b>Layout and drawing are one pass in the original</b>, which is why an undrawn menu hit-tests
/// as nothing. They are separable here — <see cref="Layout"/> can run headless — but
/// <see cref="Draw"/> still lays out as it goes, so the coupling the engine relies on holds.
/// </para>
/// </remarks>
public static class MenuRenderer
{
    /// <summary>
    /// Computes each entry's bounds and stores them on the items, without drawing.
    /// </summary>
    /// <returns>The rectangle enclosing everything laid out, title included.</returns>
    public static SurfaceRect Layout(Menu menu, BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(font);

        return Run(menu, font, destination: null, MenuPalette.Default);
    }

    /// <summary>Lays the menu out and draws it.</summary>
    /// <returns>The rectangle enclosing everything drawn.</returns>
    public static SurfaceRect Draw(Surface destination, Menu menu, BitmapFont font,
                                   MenuPalette? palette = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(font);

        return Run(menu, font, destination, palette ?? MenuPalette.Default);
    }

    private static SurfaceRect Run(Menu menu, BitmapFont font, Surface? destination,
                                   MenuPalette palette)
    {
        foreach (var item in menu.Items)
        {
            item.Bounds = default;
        }

        if (menu.Count == 0 || !menu.Items.Any(i => i.Enabled))
        {
            return default;
        }

        NormalizeSelection(menu);
        ApplySeparationAdjustment(menu, font);

        int textHeight = font.Atlas.MaxCharHeight;
        int x = menu.StartX;
        int y = menu.StartY;

        int right = x;
        int bottom = y;

        if (menu.Title is { Length: > 0 } title)
        {
            if (menu.TitlePosition is (int titleX, int titleY))
            {
                // Placed explicitly: the entries are unaffected and start where they would have.
                if (destination is not null)
                {
                    font.Draw(destination, titleX, titleY, title, tint: palette.Text);
                }

                right = Math.Max(right, titleX + font.GetTextWidth(title));
                bottom = Math.Max(bottom, titleY + textHeight);
            }
            else
            {
                // Inline: drawn at the menu's own origin, and the entries are pushed PAST it in X
                // -- even for a vertical menu, where the whole column shifts right rather than the
                // title sitting above it.
                if (destination is not null)
                {
                    font.Draw(destination, x, y, title, tint: palette.Text);
                }

                x += 10 + menu.ItemSeparation + font.GetTextWidth(title);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y + textHeight);
            }
        }

        for (int i = 0; i < menu.Count; i++)
        {
            var item = menu.Items[i];
            if (!item.Enabled)
            {
                continue;
            }

            int width = font.GetTextWidth(item.Text);
            item.Bounds = new SurfaceRect(x, y, x + width, y + textHeight);

            if (destination is not null)
            {
                bool selected = menu.ShowSelection && i == menu.ActiveItem;
                DrawItem(destination, font, item, x, y, textHeight, selected, menu, palette);
            }

            right = Math.Max(right, x + width);
            bottom = Math.Max(bottom, y + textHeight);

            if (menu.Orientation == MenuOrientation.Horizontal)
            {
                x += menu.ItemSeparation + width;
            }
            else
            {
                y += menu.ItemSeparation + textHeight;
            }
        }

        return new SurfaceRect(menu.StartX, menu.StartY, right, bottom);
    }

    /// <summary>
    /// Pulls the selection back into range, as <c>DisplayMenu</c> does before drawing.
    /// </summary>
    /// <remarks>
    /// <see cref="Menu.NoSelection"/> is left alone — that is the point of the sentinel, and the
    /// original guards this whole block on it.
    /// </remarks>
    private static void NormalizeSelection(Menu menu)
    {
        if (menu.ActiveItem == Menu.NoSelection)
        {
            return;
        }

        if (menu.ActiveItem < 0 || menu.ActiveItem >= menu.Count)
        {
            menu.SetCurrentItem(0);
        }
    }

    /// <summary>
    /// Widens the gap between horizontal entries, once (<c>GameMenu.cpp:1690</c>).
    /// </summary>
    /// <remarks>
    /// <b>Two things about this are load-bearing and neither is obvious.</b> The adjustment is a
    /// space's width plus two, applied to <c>itemSeparation</c> itself rather than at each step —
    /// so a horizontal menu's entries sit a word apart while a vertical menu's sit five pixels
    /// apart. And the guard is set <i>whether or not the adjustment applies</i>: a menu drawn
    /// vertically first and switched to horizontal afterwards never gets the wider gap, because
    /// <c>initCharSize</c> is already true. Reproduced rather than corrected — the entries would
    /// move, and where they sit is what a design's art is drawn around.
    /// </remarks>
    private static void ApplySeparationAdjustment(Menu menu, BitmapFont font)
    {
        if (menu.CharSizeInitialized)
        {
            return;
        }

        if (menu.Orientation == MenuOrientation.Horizontal && menu.ItemSeparation > 0)
        {
            menu.ItemSeparation += font.GetCharacterWidth((byte)' ') + 2;
        }

        menu.CharSizeInitialized = true;
    }

    /// <summary>
    /// Draws one entry, reverse-video if selected, with its shortcut letter picked out.
    /// </summary>
    /// <remarks>
    /// <b>The bar is filled explicitly where the original swapped fonts.</b> <c>HighlightFont</c>
    /// is rasterised with a white <i>background</i> rather than a transparent one, and drawn with
    /// transparency off (<c>Graphics.cpp:2067</c>), so the white came out of the glyph cells
    /// themselves. A tinted atlas has no background pixels to carry it, so the bar is painted
    /// first and the glyphs go over it — the same picture, and it keeps one atlas instead of the
    /// original's separate highlight face.
    /// </remarks>
    private static void DrawItem(Surface destination, BitmapFont font, MenuItem item,
                                 int x, int y, int textHeight, bool selected, Menu menu,
                                 MenuPalette palette)
    {
        if (selected)
        {
            var bar = new SurfaceRect(x, y, x + font.GetTextWidth(item.Text), y + textHeight);
            destination.FillRect(bar, palette.HighlightBackground);
        }

        uint ink = selected ? palette.HighlightInk : palette.Text;
        int shortcut = menu.UseKeyboardShortcuts ? item.ShortcutIndex : -1;

        // Drawn byte by byte so the shortcut letter can take its own colour. The original walks the
        // string through FORMATTED_TEXT for the same reason and swaps the font at that one index.
        int cursor = x;
        for (int i = 0; i < item.Text.Length; i++)
        {
            uint tint = i == shortcut ? palette.Shortcut : ink;
            cursor = font.Draw(destination, cursor, y, item.Text.AsSpan(i, 1), tint: tint);
        }
    }
}
