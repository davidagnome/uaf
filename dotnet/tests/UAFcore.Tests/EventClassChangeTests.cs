using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Drives CHANGE CLASS through the runner — when the entry lights up, and what the picker does.
/// </summary>
/// <remarks>
/// <b>With no scripting layer the entry is dark in every shipped design</b>, which is the
/// reference's own behaviour and not a gap in the port. These tests supply a list directly so the
/// screen behind it can still be exercised.
/// </remarks>
public class EventClassChangeTests
{
    private const uint Key = 0xFF000000;

    private static readonly TextBoxMetrics Box = new(18, 328, 400, 96, 6);

    private static readonly MenuAnchors Anchors =
        new((16, 460), (200, 200), (20, 328), (16, 460));

    private static BitmapFont Font()
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (10, 16));

        var glyphs = FontAtlas.Layout(extents, FontAtlas.DefaultSheetWidth, out int sheetHeight);
        var sheet = new Surface(FontAtlas.DefaultSheetWidth, sheetHeight, SurfaceKind.Font);
        sheet.Fill(Key);
        sheet.ColorKey = Key;

        return new BitmapFont(new FontAtlas(sheet, glyphs));
    }

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static TrainingHallEvent Hall() =>
        new(new GameEventBase(
                new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
                NoPic, NoPic, (int)EventType.TrainingHallEvent, 1, 0, 0,
                ChainEventHappen: 55, ChainEventNotHappen: 0, "Train here?", "", "", []),
            0, [new TrainableBaseclass("fighter", 1, 20, "")], Cost: 100);

    private const int HallYes = 0;
    private const int PartyChangeClass = 4;

    /// <summary>SELECT, NEXT, PREV, EXIT — the picker the class step shares.</summary>
    private const int Select = 0;
    private const int Exit = 3;

    private static EventRunner Started(IReadOnlyList<string>? classes = null,
                                       Action<string>? applyClass = null)
    {
        var offered = classes ?? [];

        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            CanTrainHere = _ => false,
            ClassChangesFor = () => offered,
            CanChangeClassHere = () => offered.Count > 0,
            ApplyClassChange = applyClass,
        };

        runner.Begin(Hall(), Font(), Box, Anchors);
        return runner;
    }

    private static void Press(EventRunner runner, VirtualKey key) =>
        runner.Handle(InputEvent.KeyDown(key));

    private static EventStep Choose(EventRunner runner, int item)
    {
        // The party menu is vertical; the class picker over it is horizontal, and it eats the
        // vertical keys for its own list.
        var key = runner.PartyMenuOpen && runner.ClassChoices is null
            ? VirtualKey.Down
            : VirtualKey.Right;

        for (int i = 0; i < runner.Menu.Count && runner.Menu.ActiveItem != item; i++)
        {
            runner.Handle(InputEvent.KeyDown(key));
        }

        Assert.Equal(item, runner.Menu.ActiveItem);
        return runner.Handle(InputEvent.KeyDown(VirtualKey.Return));
    }

    [Fact]
    public void With_nothing_to_change_to_the_entry_is_dark()
    {
        var runner = Started(classes: []);
        Choose(runner, HallYes);

        Assert.False(runner.Menu.Items[PartyChangeClass].Enabled);
    }

    [Fact]
    public void With_something_to_change_to_the_entry_lights_up()
    {
        var runner = Started(["Cleric", "Magic User"]);
        Choose(runner, HallYes);

        Assert.True(runner.Menu.Items[PartyChangeClass].Enabled);
    }

    [Fact]
    public void The_picker_lists_what_the_host_offered()
    {
        var runner = Started(["Cleric", "Magic User"]);
        Choose(runner, HallYes);
        Choose(runner, PartyChangeClass);

        Assert.Equal(["Cleric", "Magic User"], runner.ClassChoices);
        Assert.Equal(["SELECT", "NEXT", "PREV", "EXIT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void The_list_takes_the_vertical_keys_and_the_menu_the_horizontal_ones()
    {
        var runner = Started(["Cleric", "Magic User"]);
        Choose(runner, HallYes);
        Choose(runner, PartyChangeClass);

        Press(runner, VirtualKey.Down);
        Assert.Equal(1, runner.ClassIndex);
        Assert.Equal(Select, runner.Menu.ActiveItem);

        Press(runner, VirtualKey.Right);
        Assert.Equal(1, runner.ClassIndex);
        Assert.NotEqual(Select, runner.Menu.ActiveItem);
    }

    [Fact]
    public void Selecting_applies_the_highlighted_class_and_leaves()
    {
        string? applied = null;
        var runner = Started(["Cleric", "Magic User"], c => applied = c);

        Choose(runner, HallYes);
        Choose(runner, PartyChangeClass);

        Press(runner, VirtualKey.Down);          // Magic User
        Press(runner, VirtualKey.Return);        // SELECT is where the cursor starts

        Assert.Equal("Magic User", applied);
        Assert.Null(runner.ClassChoices);
        Assert.True(runner.PartyMenuOpen);
        Assert.Equal(12, runner.Menu.Count);
    }

    [Fact]
    public void Exiting_changes_nothing()
    {
        string? applied = null;
        var runner = Started(["Cleric"], c => applied = c);

        Choose(runner, HallYes);
        Choose(runner, PartyChangeClass);
        Choose(runner, Exit);

        Assert.Null(applied);
        Assert.Null(runner.ClassChoices);
        Assert.True(runner.PartyMenuOpen);
    }

    [Fact]
    public void Escape_exits_rather_than_selecting()
    {
        string? applied = null;
        var runner = Started(["Cleric"], c => applied = c);

        Choose(runner, HallYes);
        Choose(runner, PartyChangeClass);
        Press(runner, VirtualKey.Escape);

        Assert.Null(applied);
        Assert.Null(runner.ClassChoices);
    }
}
