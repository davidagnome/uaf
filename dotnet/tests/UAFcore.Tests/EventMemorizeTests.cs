using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Drives the MEMORIZE screen through the runner.</summary>
public class EventMemorizeTests
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

    private const int CampMagic = 3;

    private static SchoolAbility School(string id, params int[] slots)
    {
        var ability = new SchoolAbility(id, SpellAbility.MaxSpellLevel)
        {
            MaxSpellLevel = slots.Length,
        };

        for (int i = 0; i < slots.Length; i++)
        {
            ability.Base[i] = slots[i];
        }

        return ability;
    }

    /// <summary>A book of level-one wizard spells, with the slots the caster has.</summary>
    private static (SpellList Book, MemorizeList List) Books(int spells, int slots)
    {
        var book = new SpellList();
        for (int i = 0; i < spells; i++)
        {
            book.Add($"spell{i}", level: 1).Selected = 0;
        }

        var list = MemorizeList.Build(
            book.Entries, _ => ("wizard", 1),
            new Dictionary<string, SchoolAbility> { ["wizard"] = School("wizard", slots) });

        return (book, list);
    }

    private static EventRunner Started(MemorizeList? list, bool canCast = true,
                                       Action<MemorizeList>? apply = null)
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            ZoneHere = () => new EventRunner.ZoneRules(true, true),
            InCombat = () => false,
            CanCastSpells = () => canCast,
            CanMemorizeSpells = () => true,
            ScribeLabel = () => null,
            MemorizeFor = () => list,
            ApplyMemorize = apply,
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

    private static void OpenIt(EventRunner runner)
    {
        Choose(runner, CampMagic);
        Choose(runner, EventRunner.MagicMemorize);
    }

    [Fact]
    public void Magic_opens_the_memorise_screen()
    {
        var (_, list) = Books(spells: 2, slots: 2);
        var runner = Started(list);

        OpenIt(runner);

        Assert.NotNull(runner.Memorizing);
        Assert.Equal(["SELECT", "UNSELECT", "FORGET", "NEXT", "PREV", "EXIT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void A_character_who_cannot_cast_gets_a_live_entry_that_opens_nothing()
    {
        // The two gates are different predicates: MAGIC darkens MEMORIZE on CanMemorizeSpells(0),
        // and the screen itself checks CanCastSpells in OnInitialEvent and pops straight back out.
        // So a character who may memorise but may not cast presses a live entry and nothing
        // happens -- the refusal is the screen failing to appear, not a message.
        var (_, list) = Books(spells: 1, slots: 1);
        var runner = Started(list, canCast: false);

        Choose(runner, CampMagic);
        Assert.True(runner.Menu.Items[EventRunner.MagicMemorize].Enabled);

        Choose(runner, EventRunner.MagicMemorize);

        Assert.Null(runner.Memorizing);

        // And the magic hub is still the screen underneath -- the reference pushes the screen and
        // its OnInitialEvent pops it, which lands back where it started.
        Assert.True(runner.MagicOpen);
        Assert.Equal(6, runner.Menu.Count);
    }

    [Fact]
    public void A_host_with_no_list_to_offer_opens_nothing_either()
    {
        var runner = Started(list: null);

        Choose(runner, CampMagic);
        Choose(runner, EventRunner.MagicMemorize);

        Assert.Null(runner.Memorizing);
    }

    [Fact]
    public void Selecting_takes_a_shared_slot_from_every_spell_at_that_level()
    {
        var (_, list) = Books(spells: 2, slots: 2);
        var runner = Started(list);
        OpenIt(runner);

        Choose(runner, EventRunner.MemorizeSelect);

        Assert.Equal(1, list.Items[0].Selected);
        Assert.Equal(1, list.Items[0].Available);
        Assert.Equal(1, list.Items[1].Available);
    }

    [Fact]
    public void Select_darkens_when_the_slots_run_out()
    {
        var (_, list) = Books(spells: 1, slots: 1);
        var runner = Started(list);
        OpenIt(runner);

        Assert.True(runner.Menu.Items[EventRunner.MemorizeSelect].Enabled);

        Choose(runner, EventRunner.MemorizeSelect);

        Assert.False(runner.Menu.Items[EventRunner.MemorizeSelect].Enabled);
        Assert.True(runner.Menu.Items[EventRunner.MemorizeUnselect].Enabled);
    }

    [Fact]
    public void Unselect_gives_the_slot_back()
    {
        var (_, list) = Books(spells: 1, slots: 2);
        var runner = Started(list);
        OpenIt(runner);

        Choose(runner, EventRunner.MemorizeSelect);
        Choose(runner, EventRunner.MemorizeUnselect);

        Assert.Equal(0, list.Items[0].Selected);
        Assert.Equal(2, list.Items[0].Available);
    }

    [Fact]
    public void Forget_is_dark_with_nothing_memorised_and_live_with_something()
    {
        var (_, list) = Books(spells: 1, slots: 2);
        var runner = Started(list);
        OpenIt(runner);

        Assert.False(runner.Menu.Items[EventRunner.MemorizeForget].Enabled);

        list.Items[0].Memorized = 1;
        Press(runner, VirtualKey.Down);           // any key that refreshes the enable pass

        Assert.True(runner.Menu.Items[EventRunner.MemorizeForget].Enabled);
    }

    [Fact]
    public void The_list_takes_the_vertical_keys_and_the_menu_the_horizontal()
    {
        var (_, list) = Books(spells: 3, slots: 3);
        var runner = Started(list);
        OpenIt(runner);

        Press(runner, VirtualKey.Down);
        Assert.Equal(1, runner.MemorizeIndex);

        Choose(runner, EventRunner.MemorizeSelect);

        // The second spell got the selection, not the first.
        Assert.Equal(0, list.Items[0].Selected);
        Assert.Equal(1, list.Items[1].Selected);
    }

    [Fact]
    public void An_empty_list_leaves_only_the_way_out()
    {
        var (_, list) = Books(spells: 0, slots: 2);
        var runner = Started(list);
        OpenIt(runner);

        for (int item = EventRunner.MemorizeSelect; item <= EventRunner.MemorizePrev; item++)
        {
            Assert.False(runner.Menu.Items[item].Enabled);
        }

        Assert.True(runner.Menu.Items[EventRunner.MemorizeExit].Enabled);
    }

    [Fact]
    public void Nothing_reaches_the_character_until_exit()
    {
        var (book, list) = Books(spells: 1, slots: 2);
        MemorizeList? committed = null;

        var runner = Started(list, apply: l => { committed = l; l.Commit(book); });
        OpenIt(runner);

        Choose(runner, EventRunner.MemorizeSelect);
        Assert.Null(committed);
        Assert.Equal(0, book.Entries[0].Selected);

        Choose(runner, EventRunner.MemorizeExit);

        Assert.NotNull(committed);
        Assert.Equal(1, book.Entries[0].Selected);
    }

    [Fact]
    public void Escape_commits_too_because_escape_is_exit()
    {
        var (book, list) = Books(spells: 1, slots: 2);
        var runner = Started(list, apply: l => l.Commit(book));
        OpenIt(runner);

        Choose(runner, EventRunner.MemorizeSelect);
        Press(runner, VirtualKey.Escape);

        Assert.Null(runner.Memorizing);
        Assert.Equal(1, book.Entries[0].Selected);
    }

    [Fact]
    public void Leaving_goes_back_to_the_magic_hub()
    {
        var (_, list) = Books(spells: 1, slots: 1);
        var runner = Started(list);
        OpenIt(runner);

        Choose(runner, EventRunner.MemorizeExit);

        Assert.Null(runner.Memorizing);
        Assert.True(runner.MagicOpen);
        Assert.Equal(6, runner.Menu.Count);
    }
}
