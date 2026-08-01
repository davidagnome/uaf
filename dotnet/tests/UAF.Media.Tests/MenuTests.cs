namespace UAF.Media.Tests;

/// <summary>
/// Covers the menu: item state, selection movement, keyboard shortcuts, layout and drawing.
/// </summary>
/// <remarks>
/// Expectations are hand-traced from <c>UAFWin/GameMenu.cpp</c>. Several of the behaviours pinned
/// here look like defects and are transcribed deliberately — the disabled-entry collision in
/// <c>FirstLettersUnique</c>, the one-shot separation adjustment, and the inline title shifting a
/// vertical menu sideways. Each has its own test so that "correcting" one later fails loudly.
/// </remarks>
public class MenuTests
{
    private const uint Key = 0xFF000000;
    private const uint Ink = 0xFFFFFFFF;

    /// <summary>
    /// An atlas where every character is <paramref name="advance"/> wide, with its <b>first column
    /// left keyed</b>.
    /// </summary>
    /// <remarks>
    /// The gap is what makes the highlight bar observable. A fully solid cell covers every pixel of
    /// its own box, so reverse video and plain text would be indistinguishable by sampling — a real
    /// glyph has background around it and this fixture has to as well, or the drawing tests assert
    /// nothing. Column 0 of each cell is therefore background; column 1 onward is ink.
    /// </remarks>
    private static BitmapFont Font(int advance = 10, int height = 16)
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (advance, height));

        var glyphs = FontAtlas.Layout(extents, FontAtlas.DefaultSheetWidth, out int sheetHeight);
        var sheet = new Surface(FontAtlas.DefaultSheetWidth, sheetHeight, SurfaceKind.Font);
        sheet.Fill(Key);
        sheet.ColorKey = Key;

        foreach (var glyph in glyphs)
        {
            for (int y = glyph.Source.Top; y < glyph.Source.Bottom; y++)
            {
                for (int x = glyph.Source.Left + 1; x < glyph.Source.Right; x++)
                {
                    sheet[x, y] = Ink;
                }
            }
        }

        return new BitmapFont(new FontAtlas(sheet, glyphs));
    }

    private static Menu Build(params string[] labels)
    {
        var menu = new Menu();
        foreach (string label in labels)
        {
            menu.AddItem(label);
        }

        return menu;
    }

    // ---- state and navigation ----------------------------------------------------------------

    [Fact]
    public void A_new_menu_is_vertical_with_the_first_item_selected()
    {
        // reset() sets orient=1 and activeItem=0, so a menu that never asks is a column.
        var menu = Build("One", "Two");

        Assert.Equal(MenuOrientation.Vertical, menu.Orientation);
        Assert.Equal(0, menu.ActiveItem);
        Assert.True(menu.ShowSelection);
    }

    [Fact]
    public void Adding_returns_a_one_based_position_that_feeds_the_one_based_setter()
    {
        var menu = new Menu();

        Assert.Equal(1, menu.AddItem("One"));
        Assert.Equal(2, menu.AddItem("Two"));

        menu.SetCurrentItemOneBased(2);
        Assert.Equal(1, menu.ActiveItem);
    }

    [Fact]
    public void The_one_based_setter_ignores_zero_as_the_original_does()
    {
        // Its guard is item > 0, so a caller passing a 0-based index by mistake gets no movement
        // rather than the first entry. Call sites pass addMenuItem's return, which is never 0.
        var menu = Build("One", "Two");
        menu.SetCurrentItemOneBased(2);

        menu.SetCurrentItemOneBased(0);
        Assert.Equal(1, menu.ActiveItem);
    }

    [Fact]
    public void The_selection_wraps_in_both_directions()
    {
        var menu = Build("One", "Two", "Three");

        menu.NextItem();
        Assert.Equal(1, menu.ActiveItem);
        menu.NextItem();
        menu.NextItem();
        Assert.Equal(0, menu.ActiveItem);

        menu.PrevItem();
        Assert.Equal(2, menu.ActiveItem);
    }

    [Fact]
    public void Disabled_entries_are_skipped_in_both_directions()
    {
        var menu = Build("One", "Two", "Three", "Four");
        menu.SetItemEnabled(1, false);
        menu.SetItemEnabled(2, false);

        menu.NextItem();
        Assert.Equal(3, menu.ActiveItem);

        menu.PrevItem();
        Assert.Equal(0, menu.ActiveItem);
    }

    [Fact]
    public void A_menu_with_nothing_enabled_leaves_the_selection_alone()
    {
        // nextItem counts a full loop and restores rather than landing on a disabled entry.
        var menu = Build("One", "Two");
        menu.SetAllItemsEnabled(false);

        menu.NextItem();
        Assert.Equal(0, menu.ActiveItem);
    }

    [Fact]
    public void The_no_selection_sentinel_survives_being_set_and_drawn()
    {
        var menu = Build("One", "Two");
        menu.SetCurrentItem(Menu.NoSelection);

        Assert.Equal(Menu.NoSelection, menu.ActiveItem);

        // DisplayMenu guards its range-fixup on the sentinel, so drawing must not consume it.
        MenuRenderer.Draw(new Surface(320, 200), menu, Font());
        Assert.Equal(Menu.NoSelection, menu.ActiveItem);
    }

    [Fact]
    public void Deleting_the_selected_last_entry_moves_the_selection_back_into_range()
    {
        var menu = Build("One", "Two");
        menu.NextItem();
        Assert.Equal(1, menu.ActiveItem);

        menu.DeleteLastItem();
        Assert.Equal(1, menu.Count);
        Assert.Equal(0, menu.ActiveItem);
    }

    [Fact]
    public void A_menu_holds_at_most_twenty_entries()
    {
        var menu = new Menu();
        for (int i = 0; i < Menu.MaxItems; i++)
        {
            Assert.NotEqual(-1, menu.AddItem($"Item{i}"));
        }

        Assert.Equal(-1, menu.AddItem("One too many"));
        Assert.Equal(Menu.MaxItems, menu.Count);
    }

    // ---- keyboard shortcuts ------------------------------------------------------------------

    [Fact]
    public void First_letters_become_shortcuts_when_they_are_all_distinct()
    {
        var menu = Build("Buy", "Sell", "Exit");
        menu.SetFirstLetterShortcuts();

        Assert.All(menu.Items, i => Assert.Equal(0, i.ShortcutIndex));
        Assert.Equal(1, menu.LookupShortcut('s'));
        Assert.Equal(2, menu.LookupShortcut('E'));
    }

    [Fact]
    public void Shortcut_lookup_is_case_insensitive()
    {
        // strnicmp, so a design's capitalisation does not decide what the player must press.
        var menu = Build("Buy", "Sell");
        menu.SetFirstLetterShortcuts();

        Assert.Equal(0, menu.LookupShortcut('b'));
        Assert.Equal(0, menu.LookupShortcut('B'));
    }

    [Fact]
    public void Colliding_first_letters_suppress_shortcuts_for_the_whole_menu()
    {
        var menu = Build("Buy", "Barter");
        menu.SetFirstLetterShortcuts();

        Assert.All(menu.Items, i => Assert.Equal(-1, i.ShortcutIndex));
    }

    [Fact]
    public void A_disabled_entry_still_blocks_shortcuts_for_the_visible_ones()
    {
        // FirstLettersUnique skips disabled entries in its outer loop and NOT in its inner one, so
        // a hidden "Barter" suppresses the shortcut on a visible "Buy". Transcribed, not fixed.
        var menu = Build("Buy", "Barter", "Sell");
        menu.SetItemEnabled(1, false);

        Assert.False(menu.FirstLettersUnique());

        menu.SetFirstLetterShortcuts();
        Assert.All(menu.Items, i => Assert.Equal(-1, i.ShortcutIndex));
    }

    [Fact]
    public void A_single_entry_takes_its_first_letter_without_a_uniqueness_test()
    {
        var menu = Build("Continue");
        menu.SetFirstLetterShortcuts();

        Assert.Equal(0, menu.Items[0].ShortcutIndex);
    }

    [Fact]
    public void A_unique_shortcut_falls_through_to_a_later_letter_when_the_first_is_taken()
    {
        var menu = Build("Cast", "Camp");

        // The colliding first letters block the cheap route, leaving both without a shortcut.
        menu.SetFirstLetterShortcuts();
        Assert.All(menu.Items, i => Assert.Equal(-1, i.ShortcutIndex));

        // Assigned one at a time, each takes the first A-Z letter of its own label that is free.
        menu.AttemptUniqueShortcut(0);
        Assert.Equal(0, menu.Items[0].ShortcutIndex);        // 'C' was free

        menu.AttemptUniqueShortcut(1);
        Assert.Equal(1, menu.Items[1].ShortcutIndex);        // 'C' taken, so 'a' of "Camp"

        Assert.Equal(0, menu.LookupShortcut('C'));
        Assert.Equal(1, menu.LookupShortcut('A'));
    }

    [Fact]
    public void A_shortcut_is_not_reassigned_once_it_exists()
    {
        var menu = Build("Buy", "Sell");
        menu.SetFirstLetterShortcuts();

        menu.AttemptUniqueShortcut(1);
        Assert.Equal(0, menu.Items[1].ShortcutIndex);
    }

    [Fact]
    public void A_disabled_entry_is_given_no_shortcut()
    {
        var menu = Build("Cast");
        menu.SetItemEnabled(0, false);

        menu.AttemptUniqueShortcut(0);
        Assert.Equal(-1, menu.Items[0].ShortcutIndex);
    }

    [Fact]
    public void Turning_shortcuts_off_stops_lookup_without_clearing_the_indices()
    {
        var menu = Build("Buy", "Sell");
        menu.SetFirstLetterShortcuts();
        menu.UseKeyboardShortcuts = false;

        Assert.Equal(-1, menu.LookupShortcut('b'));
        Assert.Equal(0, menu.Items[0].ShortcutIndex);
    }

    // ---- anchors -----------------------------------------------------------------------------

    [Fact]
    public void The_anchor_sentinels_resolve_to_the_designs_configured_points()
    {
        // SomethingWild's values.
        var anchors = new MenuAnchors((16, 460), (200, 200), (20, 328), (16, 460));
        var menu = new Menu();

        menu.SetStartCoord(MenuAnchor.DefaultHorizontal, anchors);
        Assert.Equal((16, 460), (menu.StartX, menu.StartY));

        menu.SetStartCoord(MenuAnchor.DefaultVertical, anchors);
        Assert.Equal((200, 200), (menu.StartX, menu.StartY));

        menu.SetStartCoord(MenuAnchor.DefaultTextBox, anchors);
        Assert.Equal((20, 328), (menu.StartX, menu.StartY));

        menu.SetStartCoord(MenuAnchor.Absolute, anchors, 43, 18);
        Assert.Equal((43, 18), (menu.StartX, menu.StartY));
    }

    [Fact]
    public void An_absent_combat_anchor_falls_back_to_the_normal_horizontal_one()
    {
        // The original seeds DEFAULT_MENU_COMBAT_HORZ_X to -1 and tests >= 0, so "unset" is a real
        // state -- not the origin, which is what a plain zero-initialised point would give.
        var anchors = new MenuAnchors((16, 460), (200, 200), (20, 328), CombatHorizontal: null);
        var menu = new Menu();

        menu.SetStartCoord(MenuAnchor.DefaultHorizontal, anchors, combat: true);
        Assert.Equal((16, 460), (menu.StartX, menu.StartY));
    }

    [Fact]
    public void The_combat_anchor_overrides_the_horizontal_one_when_it_is_set()
    {
        var anchors = new MenuAnchors((16, 460), (200, 200), (20, 328), (100, 300));
        var menu = new Menu();

        menu.SetStartCoord(MenuAnchor.DefaultHorizontal, anchors, combat: true);
        Assert.Equal((100, 300), (menu.StartX, menu.StartY));
    }

    [Fact]
    public void Anchors_read_from_config_keep_the_defaults_for_keys_a_design_omits()
    {
        var configured = new Dictionary<string, (int, int)>
        {
            ["DEFAULT_MENU_HORZ"] = (16, 460),
        };

        var anchors = MenuAnchors.FromConfig(
            key => configured.TryGetValue(key, out var point) ? point : null);

        Assert.Equal((16, 460), anchors.Horizontal);
        Assert.Equal((0, 0), anchors.Vertical);
        Assert.Null(anchors.CombatHorizontal);
    }

    // ---- layout ------------------------------------------------------------------------------

    [Fact]
    public void A_vertical_menu_stacks_by_the_font_height_plus_the_separation()
    {
        var menu = Build("One", "Two");
        menu.SetStartCoord(50, 100);

        MenuRenderer.Layout(menu, Font(advance: 10, height: 16));

        Assert.Equal(new SurfaceRect(50, 100, 80, 116), menu.Items[0].Bounds);
        Assert.Equal(new SurfaceRect(50, 121, 80, 137), menu.Items[1].Bounds);
    }

    [Fact]
    public void A_horizontal_menu_widens_its_separation_by_a_space_plus_two_exactly_once()
    {
        // itemSeparation starts at 5 and gains GetCharacterWidth(' ') + 2 = 12, so 17 -- applied to
        // the field, not per step, and only on the first layout.
        var menu = Build("One", "Two");
        menu.Orientation = MenuOrientation.Horizontal;
        menu.SetStartCoord(0, 0);

        var font = Font(advance: 10, height: 16);
        MenuRenderer.Layout(menu, font);

        Assert.Equal(17, menu.ItemSeparation);
        Assert.Equal(new SurfaceRect(0, 0, 30, 16), menu.Items[0].Bounds);
        Assert.Equal(new SurfaceRect(47, 0, 77, 16), menu.Items[1].Bounds);

        MenuRenderer.Layout(menu, font);
        Assert.Equal(17, menu.ItemSeparation);
        Assert.Equal(new SurfaceRect(47, 0, 77, 16), menu.Items[1].Bounds);
    }

    [Fact]
    public void A_menu_laid_out_vertically_first_never_gets_the_wider_horizontal_gap()
    {
        // initCharSize is set whether or not the adjustment applied, so switching orientation
        // afterwards leaves the narrow gap. Reproduced -- the entries would otherwise move.
        var menu = Build("One", "Two");
        var font = Font();

        MenuRenderer.Layout(menu, font);
        Assert.Equal(Menu.DefaultItemSeparation, menu.ItemSeparation);

        menu.Orientation = MenuOrientation.Horizontal;
        MenuRenderer.Layout(menu, font);
        Assert.Equal(Menu.DefaultItemSeparation, menu.ItemSeparation);

        // Reset clears the guard, so a reused menu behaves like a fresh one.
        menu.Reset();
        menu.AddItem("One");
        menu.Orientation = MenuOrientation.Horizontal;
        MenuRenderer.Layout(menu, font);
        Assert.Equal(17, menu.ItemSeparation);
    }

    [Fact]
    public void An_inline_title_shifts_a_vertical_menus_whole_column_sideways()
    {
        // The title advances x and never y, even for a column -- so it sits to the LEFT of the
        // first entry rather than above it, and every entry is pushed right.
        var menu = Build("One");
        menu.SetTitle("Pick:");
        menu.SetStartCoord(0, 0);

        MenuRenderer.Layout(menu, Font(advance: 10, height: 16));

        // 10 + separation 5 + title width 50 = 65.
        Assert.Equal(65, menu.Items[0].Bounds.Left);
        Assert.Equal(0, menu.Items[0].Bounds.Top);
    }

    [Fact]
    public void A_positioned_title_leaves_the_entries_where_they_would_have_been()
    {
        var menu = Build("One");
        menu.SetTitle("Pick:");
        menu.TitlePosition = (0, 0);
        menu.SetStartCoord(30, 40);

        MenuRenderer.Layout(menu, Font(advance: 10, height: 16));

        Assert.Equal(30, menu.Items[0].Bounds.Left);
        Assert.Equal(40, menu.Items[0].Bounds.Top);
    }

    [Fact]
    public void Disabled_entries_take_no_space_and_hit_test_as_nothing()
    {
        var menu = Build("One", "Two", "Three");
        menu.SetItemEnabled(1, false);
        menu.SetStartCoord(0, 0);

        MenuRenderer.Layout(menu, Font(advance: 10, height: 16));

        Assert.Equal(0, menu.Items[1].Bounds.Width);

        // "Three" takes the row "Two" would have had, so a point there selects Three, not nothing.
        Assert.Equal(21, menu.Items[2].Bounds.Top);
        Assert.Equal(2, menu.HitTest(5, 25));

        // The separation gap between the two drawn entries belongs to neither.
        Assert.Equal(-1, menu.HitTest(5, 18));
    }

    [Fact]
    public void Hit_testing_finds_the_entry_under_a_point_and_nothing_outside_it()
    {
        var menu = Build("One", "Two");
        menu.SetStartCoord(50, 100);
        MenuRenderer.Layout(menu, Font(advance: 10, height: 16));

        Assert.Equal(0, menu.HitTest(50, 100));
        Assert.Equal(0, menu.HitTest(79, 115));
        Assert.Equal(1, menu.HitTest(60, 130));

        // The rect is half-open, as PtInRect makes it.
        Assert.Equal(-1, menu.HitTest(80, 100));
        Assert.Equal(-1, menu.HitTest(50, 116));
    }

    [Fact]
    public void A_menu_that_has_never_been_laid_out_hit_tests_as_nothing()
    {
        // The original computes the rects inside DisplayMenu, so a click arriving before the first
        // frame must not select an entry that is not on screen yet.
        var menu = Build("One", "Two");
        menu.SetStartCoord(50, 100);

        Assert.Equal(-1, menu.HitTest(50, 100));
    }

    // ---- drawing -----------------------------------------------------------------------------

    [Fact]
    public void The_selected_entry_is_drawn_reverse_video()
    {
        var menu = Build("One", "Two");
        menu.SetStartCoord(0, 0);

        var screen = new Surface(320, 200);
        screen.ClipRect = screen.Bounds;
        screen.Fill(0xFF000000);
        MenuRenderer.Draw(screen, menu, Font(advance: 10, height: 16));

        // Row 0 is the selection: a white bar showing through each cell's keyed first column, with
        // black glyphs over the rest. Every pixel inside the entry's box is one or the other.
        var palette = MenuPalette.Default;
        Assert.Equal(palette.HighlightBackground, screen[0, 4]);
        Assert.Equal(palette.HighlightInk, screen[1, 4]);

        for (int x = 0; x < 30; x++)
        {
            uint pixel = screen[x, 4];
            Assert.True(pixel == palette.HighlightBackground || pixel == palette.HighlightInk,
                        $"pixel at {x} was {pixel:X8}");
        }

        // Row 1 is not selected: white glyphs, and the surface's own black where the cell is keyed.
        Assert.Equal(0xFF000000u, screen[0, 25]);
        Assert.Equal(palette.Text, screen[1, 25]);
    }

    [Fact]
    public void Turning_the_selection_off_draws_every_entry_plain()
    {
        var menu = Build("One", "Two");
        menu.ShowSelection = false;
        menu.SetStartCoord(0, 0);

        var screen = new Surface(320, 200);
        screen.ClipRect = screen.Bounds;
        screen.Fill(0xFF000000);
        MenuRenderer.Draw(screen, menu, Font(advance: 10, height: 16));

        Assert.Equal(MenuPalette.Default.Text, screen[1, 4]);
    }

    [Fact]
    public void The_shortcut_letter_takes_the_keyboard_colour()
    {
        var menu = Build("Buy", "Sell");
        menu.SetFirstLetterShortcuts();
        menu.SetCurrentItem(1);           // select the second, so the first draws unhighlighted
        menu.SetStartCoord(0, 0);

        var screen = new Surface(320, 200);
        screen.ClipRect = screen.Bounds;
        screen.Fill(0xFF000000);
        MenuRenderer.Draw(screen, menu, Font(advance: 10, height: 16));

        // 'B' is the shortcut and gets COLOR_KEYBOARD_SHORTCUT; 'u' after it does not. Column 0
        // of each cell is keyed, so the ink is sampled one pixel in.
        Assert.Equal(MenuPalette.Default.Shortcut, screen[1, 4]);
        Assert.Equal(MenuPalette.Default.Text, screen[11, 4]);
    }

    [Fact]
    public void A_label_containing_a_slash_tag_draws_it_literally()
    {
        // DrawFont disables font colour tags for the duration, so a menu label is the one place in
        // the engine where "/R" is two characters rather than a colour change.
        var menu = Build("/Rred");
        menu.ShowSelection = false;
        menu.SetStartCoord(0, 0);

        var font = Font(advance: 10, height: 16);
        MenuRenderer.Layout(menu, font);

        // Four characters would be 40px if the tag were consumed; five means it was not.
        Assert.Equal(50, menu.Items[0].Bounds.Width);

        var screen = new Surface(320, 200);
        screen.ClipRect = screen.Bounds;
        screen.Fill(0xFF000000);
        MenuRenderer.Draw(screen, menu, font);

        Assert.Equal(MenuPalette.Default.Text, screen[1, 4]);
    }

    // ---- input -------------------------------------------------------------------------------

    [Fact]
    public void All_four_arrows_drive_every_menu_whatever_its_orientation()
    {
        // StandardMenuKeyboardAction has no orientation test: up/left step back, down/right
        // forward. A bottom bar really does respond to up and down.
        foreach (var orientation in new[] { MenuOrientation.Horizontal, MenuOrientation.Vertical })
        {
            var menu = Build("One", "Two", "Three");
            menu.Orientation = orientation;

            Assert.Equal(MenuInputResult.Moved,
                         MenuInput.Handle(menu, InputEvent.KeyDown(VirtualKey.Down)));
            Assert.Equal(1, menu.ActiveItem);

            MenuInput.Handle(menu, InputEvent.KeyDown(VirtualKey.Right));
            Assert.Equal(2, menu.ActiveItem);

            MenuInput.Handle(menu, InputEvent.KeyDown(VirtualKey.Up));
            Assert.Equal(1, menu.ActiveItem);

            MenuInput.Handle(menu, InputEvent.KeyDown(VirtualKey.Left));
            Assert.Equal(0, menu.ActiveItem);
        }
    }

    [Fact]
    public void Return_accepts_the_current_entry()
    {
        var menu = Build("One", "Two");
        menu.NextItem();

        Assert.Equal(MenuInputResult.Accepted,
                     MenuInput.Handle(menu, InputEvent.KeyDown(VirtualKey.Return)));
        Assert.Equal(1, menu.ActiveItem);
    }

    [Fact]
    public void Return_chooses_nothing_when_nothing_is_selected()
    {
        var menu = Build("One", "Two");
        menu.SetCurrentItem(Menu.NoSelection);

        Assert.Equal(MenuInputResult.None,
                     MenuInput.Handle(menu, InputEvent.KeyDown(VirtualKey.Return)));
    }

    [Fact]
    public void A_shortcut_letter_selects_and_chooses_in_one_keystroke()
    {
        // The original selects the item and pushes a synthetic VK_RETURN, so the letter both moves
        // and confirms rather than merely highlighting.
        var menu = Build("Buy", "Sell", "Exit");
        menu.SetFirstLetterShortcuts();

        Assert.Equal(MenuInputResult.Accepted, MenuInput.Handle(menu, InputEvent.Text('e')));
        Assert.Equal(2, menu.ActiveItem);
    }

    [Fact]
    public void An_unmatched_letter_leaves_the_selection_alone()
    {
        var menu = Build("Buy", "Sell");
        menu.SetFirstLetterShortcuts();

        Assert.Equal(MenuInputResult.None, MenuInput.Handle(menu, InputEvent.Text('z')));
        Assert.Equal(0, menu.ActiveItem);
    }

    [Fact]
    public void A_click_on_an_entry_selects_and_chooses_it()
    {
        var menu = Build("One", "Two");
        menu.SetStartCoord(50, 100);
        MenuRenderer.Layout(menu, Font(advance: 10, height: 16));

        var click = InputEvent.MouseDown(60, 130, MouseButtons.Left);
        Assert.Equal(MenuInputResult.Accepted, MenuInput.Handle(menu, click));
        Assert.Equal(1, menu.ActiveItem);
    }

    [Fact]
    public void A_click_off_the_menu_is_ignored_rather_than_dismissing_it()
    {
        var menu = Build("One", "Two");
        menu.SetStartCoord(50, 100);
        MenuRenderer.Layout(menu, Font(advance: 10, height: 16));

        var click = InputEvent.MouseDown(5, 5, MouseButtons.Left);
        Assert.Equal(MenuInputResult.None, MenuInput.Handle(menu, click));
        Assert.Equal(0, menu.ActiveItem);
    }

    [Fact]
    public void Escape_cancels_only_when_the_caller_asks_for_it()
    {
        // The original has no menu-level escape -- menus carry an explicit "Exit" entry instead.
        var menu = Build("One", "Two");

        Assert.Equal(MenuInputResult.None,
                     MenuInput.Handle(menu, InputEvent.KeyDown(VirtualKey.Escape)));
        Assert.Equal(MenuInputResult.Cancelled,
                     MenuInput.Handle(menu, InputEvent.KeyDown(VirtualKey.Escape),
                                      acceptOnEscape: true));
    }

    [Fact]
    public void Input_skips_disabled_entries_the_same_way_navigation_does()
    {
        var menu = Build("One", "Two", "Three");
        menu.SetItemEnabled(1, false);

        MenuInput.Handle(menu, InputEvent.KeyDown(VirtualKey.Down));
        Assert.Equal(2, menu.ActiveItem);
    }

    [Fact]
    public void An_empty_menu_draws_nothing_and_reports_an_empty_rect()
    {
        var screen = new Surface(320, 200);
        screen.ClipRect = screen.Bounds;
        screen.Fill(0xFF000000);

        Assert.Equal(default, MenuRenderer.Draw(screen, new Menu(), Font()));
        Assert.Equal(0xFF000000u, screen[0, 0]);
    }
}
