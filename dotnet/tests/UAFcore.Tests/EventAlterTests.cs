using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Drives ALTER and its ORDER screen through the runner.
/// </summary>
/// <remarks>
/// ALTER is a hub of nine, and this covers the three that run: ORDER, DROP and EXIT. The other six
/// are settings screens and the two art pickers, which are named.
/// </remarks>
public class EventAlterTests
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

    private static CampEvent Camp() =>
        new(new GameEventBase(
                new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
                new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                (int)EventType.Camp, 1, 0, 0, ChainEventHappen: 77, ChainEventNotHappen: 0,
                "You make camp.", "", "", []),
            0);

    private const int CampAlter = 5;

    private static EventRunner Started(int partySize = 4,
                                       Action<bool>? move = null,
                                       Action<EventRunner.PartyConfirm>? confirm = null)
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            PartySize = () => partySize,
            MoveActive = move,
            ApplyPartyConfirm = confirm,
            ActiveCharacterName = () => "Aramil",
        };

        runner.Begin(Camp(), Font(), Box, Anchors);
        return runner;
    }

    private static void Press(EventRunner runner, VirtualKey key) =>
        runner.Handle(InputEvent.KeyDown(key));

    private static void Choose(EventRunner runner, int item)
    {
        for (int i = 0; i < runner.Menu.Count && runner.Menu.ActiveItem != item; i++)
        {
            Press(runner, VirtualKey.Right);
        }

        Assert.Equal(item, runner.Menu.ActiveItem);
        Press(runner, VirtualKey.Return);
    }

    [Fact]
    public void Camp_opens_the_alter_hub()
    {
        var runner = Started();
        Choose(runner, CampAlter);

        Assert.True(runner.AlterOpen);
        Assert.Equal(["ORDER", "DROP", "SPEED", "ICON", "PIC", "LEVEL", "VOLUME", "MUSIC", "EXIT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void Order_and_drop_are_dark_with_one_character()
    {
        // There is no order to alter with one, and dropping the last member leaves no party.
        var runner = Started(partySize: 1);
        Choose(runner, CampAlter);

        Assert.False(runner.Menu.Items[EventRunner.AlterOrder].Enabled);
        Assert.False(runner.Menu.Items[EventRunner.AlterDrop].Enabled);
    }

    [Fact]
    public void Order_and_drop_light_up_with_two()
    {
        var runner = Started(partySize: 2);
        Choose(runner, CampAlter);

        Assert.True(runner.Menu.Items[EventRunner.AlterOrder].Enabled);
        Assert.True(runner.Menu.Items[EventRunner.AlterDrop].Enabled);
    }

    [Fact]
    public void The_order_screen_is_the_arrow_keys_and_one_exit()
    {
        var moves = new List<bool>();
        var runner = Started(move: moves.Add);

        Choose(runner, CampAlter);
        Choose(runner, EventRunner.AlterOrder);

        Assert.True(runner.OrderingParty);
        Assert.Equal(1, runner.Menu.Count);

        Press(runner, VirtualKey.Up);
        Press(runner, VirtualKey.Down);

        Assert.Equal([true, false], moves);
    }

    [Fact]
    public void Return_on_the_order_screen_goes_back_to_alter()
    {
        var runner = Started();
        Choose(runner, CampAlter);
        Choose(runner, EventRunner.AlterOrder);

        Press(runner, VirtualKey.Return);

        Assert.False(runner.OrderingParty);
        Assert.True(runner.AlterOpen);
        Assert.Equal(9, runner.Menu.Count);
    }

    [Fact]
    public void Escape_leaves_the_order_screen_too()
    {
        var runner = Started();
        Choose(runner, CampAlter);
        Choose(runner, EventRunner.AlterOrder);

        Press(runner, VirtualKey.Escape);

        Assert.False(runner.OrderingParty);
        Assert.True(runner.AlterOpen);
    }

    [Fact]
    public void Drop_asks_the_same_question_the_party_menu_does_and_comes_back_to_alter()
    {
        var answered = new List<EventRunner.PartyConfirm>();
        var runner = Started(confirm: answered.Add);

        Choose(runner, CampAlter);
        Choose(runner, EventRunner.AlterDrop);

        Assert.Equal(EventRunner.PartyConfirm.Remove, runner.Confirming);
        Assert.Equal(["YES", "NO"], runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));

        // It opens on NO, like every other irreversible prompt in this port.
        Assert.Equal(1, runner.Menu.ActiveItem);

        Press(runner, VirtualKey.Left);          // NO -> YES
        Press(runner, VirtualKey.Return);

        Assert.Equal([EventRunner.PartyConfirm.Remove], answered);
        Assert.True(runner.AlterOpen);
        Assert.Equal(9, runner.Menu.Count);
    }

    [Fact]
    public void Declining_the_drop_changes_nothing_and_still_comes_back()
    {
        var answered = new List<EventRunner.PartyConfirm>();
        var runner = Started(confirm: answered.Add);

        Choose(runner, CampAlter);
        Choose(runner, EventRunner.AlterDrop);
        Press(runner, VirtualKey.Return);        // NO, where the cursor starts

        Assert.Empty(answered);
        Assert.True(runner.AlterOpen);
    }

    [Fact]
    public void Exiting_alter_puts_the_camp_bar_back()
    {
        var runner = Started();
        Choose(runner, CampAlter);
        Choose(runner, EventRunner.AlterExit);

        Assert.False(runner.AlterOpen);
        Assert.Equal(12, runner.Menu.Count);
        Assert.Equal("SAVE", BitmapFont.Decode(runner.Menu.Items[0].Text));
    }

    [Fact]
    public void The_settings_entries_are_named_rather_than_silently_doing_nothing()
    {
        foreach ((int item, string label) in new[]
                 { (2, "SPEED"), (3, "ICON"), (4, "PIC"), (5, "LEVEL"), (6, "VOLUME"),
                   (7, "MUSIC") })
        {
            var runner = Started();
            Choose(runner, CampAlter);
            Choose(runner, item);

            Assert.Contains(label, runner.Unimplemented);
        }
    }
}
