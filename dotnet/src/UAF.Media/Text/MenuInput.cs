namespace UAF.Media;

/// <summary>What an input event did to a menu.</summary>
public enum MenuInputResult
{
    /// <summary>Nothing the menu cares about.</summary>
    None,

    /// <summary>The selection moved; redraw.</summary>
    Moved,

    /// <summary>The selected entry was chosen. The caller acts on <see cref="Menu.ActiveItem"/>.</summary>
    Accepted,

    /// <summary>The player backed out.</summary>
    Cancelled,
}

/// <summary>
/// Drives a <see cref="Menu"/> from input events
/// (<c>GameEvent::StandardMenuKeyboardAction</c>, <c>UAFWin/RunEvent.cpp:653</c>, and
/// <c>MouseMenu</c>, <c>:768</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A shortcut letter or a mouse click chooses an entry outright — it does not merely move to
/// it.</b> Both paths select the entry and then push a synthetic <c>VK_RETURN</c> into the input
/// queue (<c>RunEvent.cpp:619</c>, <c>:775</c>), so one keystroke both moves and confirms. Modelled
/// as <see cref="MenuInputResult.Accepted"/> rather than by synthesising an event, because the
/// queue exists in the original to get the keypress back through a message pump this port does not
/// have.
/// </para>
/// <para>
/// <b>All four arrows work on every menu, whatever its orientation.</b> The dispatch has no
/// orientation test: up and left both step back, down and right both step forward. A horizontal bar
/// therefore responds to up and down, and a vertical list to left and right. That is not an
/// oversight to tidy — it is what makes a design's menus usable however the player reaches for
/// them.
/// </para>
/// </remarks>
public static class MenuInput
{
    /// <summary>
    /// Applies one input event to <paramref name="menu"/>.
    /// </summary>
    /// <param name="acceptOnEscape">
    /// Whether <see cref="VirtualKey.Escape"/> reports <see cref="MenuInputResult.Cancelled"/>.
    /// The original has no menu-level escape — every menu that can be backed out of carries an
    /// explicit "Exit" entry — so this defaults to off and exists for callers that want it.
    /// </param>
    public static MenuInputResult Handle(Menu menu, InputEvent input, bool acceptOnEscape = false)
    {
        ArgumentNullException.ThrowIfNull(menu);

        switch (input.Kind)
        {
            case InputEventKind.KeyDown:
                return HandleKey(menu, input.Key, acceptOnEscape);

            case InputEventKind.TextInput:
                return HandleShortcut(menu, input.Character);

            case InputEventKind.MouseDown:
                return HandleClick(menu, input.X, input.Y);

            default:
                return MenuInputResult.None;
        }
    }

    private static MenuInputResult HandleKey(Menu menu, VirtualKey key, bool acceptOnEscape)
    {
        switch (key)
        {
            case VirtualKey.Up or VirtualKey.Left:
                menu.PrevItem();
                return MenuInputResult.Moved;

            case VirtualKey.Down or VirtualKey.Right:
                menu.NextItem();
                return MenuInputResult.Moved;

            case VirtualKey.Return:
                // Nothing is chosen when nothing is selected -- the sentinel is a real state and a
                // menu showing no highlight must not act on whatever index happens to be stored.
                return menu.ActiveItem == Menu.NoSelection
                    ? MenuInputResult.None
                    : MenuInputResult.Accepted;

            case VirtualKey.Escape when acceptOnEscape:
                return MenuInputResult.Cancelled;

            default:
                return MenuInputResult.None;
        }
    }

    /// <summary>Selects and chooses the entry whose shortcut matches, if any.</summary>
    private static MenuInputResult HandleShortcut(Menu menu, char character)
    {
        if (character == '\0')
        {
            return MenuInputResult.None;
        }

        int item = menu.LookupShortcut(character);
        if (item < 0)
        {
            return MenuInputResult.None;
        }

        menu.SetCurrentItemOneBased(item + 1);
        return MenuInputResult.Accepted;
    }

    /// <summary>Selects and chooses the entry under the cursor, if any.</summary>
    /// <remarks>
    /// A click outside every entry is ignored rather than dismissing the menu, which is what
    /// <c>MouseMenu</c>'s <c>item &gt;= 0</c> guard gives.
    /// </remarks>
    private static MenuInputResult HandleClick(Menu menu, int x, int y)
    {
        int item = menu.HitTest(x, y);
        if (item < 0)
        {
            return MenuInputResult.None;
        }

        menu.SetCurrentItemOneBased(item + 1);
        return MenuInputResult.Accepted;
    }
}
