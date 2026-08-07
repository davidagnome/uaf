using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Drives the MAGIC hub through the runner — three separate rules over its six entries.
/// </summary>
/// <remarks>
/// Only REST and EXIT run; CAST, MEMORIZE, SCRIBE and DISPLAY are each a screen of their own.
/// What is covered here is which entries light up, which is where the substance is.
/// </remarks>
public class EventMagicTests
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

    private static EventRunner Started(bool magic = true, bool resting = true,
                                       bool combat = false, bool canCast = true,
                                       bool canMemorize = true, string? scribe = null)
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            ZoneHere = () => new EventRunner.ZoneRules(magic, resting),
            InCombat = () => combat,
            CanCastSpells = () => canCast,
            CanMemorizeSpells = () => canMemorize,
            ScribeLabel = () => scribe,
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

    private static bool[] Enabled(EventRunner runner) =>
        [.. runner.Menu.Items.Select(i => i.Enabled)];

    [Fact]
    public void Camp_opens_the_magic_hub()
    {
        var runner = Started();
        Choose(runner, CampMagic);

        Assert.True(runner.MagicOpen);
        Assert.Equal(["CAST", "MEMORIZE", "SCRIBE", "DISPLAY", "REST", "EXIT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void A_no_magic_zone_means_the_hub_cannot_be_opened_from_camp_at_all()
    {
        // The hub has its own no-magic rule, darkening CAST, MEMORIZE, SCRIBE and REST -- and it
        // is unreachable from here, because camp darkens the MAGIC entry on the same flag first.
        // The branch can only be reached from a magic menu pushed by combat.
        var runner = Started(magic: false);

        Assert.False(runner.Menu.Items[CampMagic].Enabled);
    }

    [Fact]
    public void A_no_rest_zone_darkens_only_rest()
    {
        var runner = Started(resting: false, scribe: "SCRIBE");
        Choose(runner, CampMagic);

        Assert.True(runner.Menu.Items[EventRunner.MagicCast].Enabled);
        Assert.True(runner.Menu.Items[EventRunner.MagicMemorize].Enabled);
        Assert.False(runner.Menu.Items[EventRunner.MagicRest].Enabled);
    }

    [Fact]
    public void In_combat_three_go_dark_and_the_character_is_not_consulted()
    {
        // The whole else branch that reads CanCastSpells and CanMemorizeSpells is skipped, so a
        // character who could not otherwise cast still gets a live CAST entry in a fight.
        var runner = Started(combat: true, canCast: false, canMemorize: false);
        Choose(runner, CampMagic);

        Assert.True(runner.Menu.Items[EventRunner.MagicCast].Enabled);
        Assert.False(runner.Menu.Items[EventRunner.MagicMemorize].Enabled);
        Assert.False(runner.Menu.Items[EventRunner.MagicScribe].Enabled);
        Assert.False(runner.Menu.Items[EventRunner.MagicRest].Enabled);
    }

    [Fact]
    public void A_character_who_cannot_cast_has_a_dark_cast_entry()
    {
        var runner = Started(canCast: false);
        Choose(runner, CampMagic);

        Assert.False(runner.Menu.Items[EventRunner.MagicCast].Enabled);
        Assert.True(runner.Menu.Items[EventRunner.MagicMemorize].Enabled);
    }

    [Fact]
    public void A_character_who_cannot_memorise_has_a_dark_memorize_entry()
    {
        var runner = Started(canMemorize: false);
        Choose(runner, CampMagic);

        Assert.False(runner.Menu.Items[EventRunner.MagicMemorize].Enabled);
        Assert.True(runner.Menu.Items[EventRunner.MagicCast].Enabled);
    }

    [Fact]
    public void Scribe_is_dark_without_a_script_to_name_it()
    {
        var runner = Started(scribe: null);
        Choose(runner, CampMagic);

        Assert.False(runner.Menu.Items[EventRunner.MagicScribe].Enabled);
    }

    [Fact]
    public void A_script_names_the_scribe_entry_whatever_it_likes()
    {
        // The reference's own constant is SCRIBE_OR_WHATEVER: the entry has no fixed name and no
        // fixed meaning, and a design decides both.
        var runner = Started(scribe: "BREW");
        Choose(runner, CampMagic);

        Assert.True(runner.Menu.Items[EventRunner.MagicScribe].Enabled);
        Assert.Equal("BREW", BitmapFont.Decode(runner.Menu.Items[EventRunner.MagicScribe].Text));
    }

    [Fact]
    public void Rest_opens_the_screen_already_built_and_comes_back_to_magic()
    {
        var runner = Started();
        Choose(runner, CampMagic);
        Choose(runner, EventRunner.MagicRest);

        Assert.True(runner.RestOpen);
        Assert.False(runner.MagicOpen);

        Press(runner, VirtualKey.Escape);            // the rest screen's EXIT

        Assert.False(runner.RestOpen);
        Assert.True(runner.MagicOpen);
        Assert.Equal(6, runner.Menu.Count);
    }

    [Fact]
    public void Exiting_puts_the_camp_bar_back()
    {
        var runner = Started();
        Choose(runner, CampMagic);
        Choose(runner, EventRunner.MagicExit);

        Assert.False(runner.MagicOpen);
        Assert.Equal(12, runner.Menu.Count);
        Assert.Equal("SAVE", BitmapFont.Decode(runner.Menu.Items[0].Text));
    }

    [Fact]
    public void The_unbuilt_entries_are_named()
    {
        // CAST is no longer here -- it opens the spell list.
        foreach ((int item, string label) in new[] { (3, "DISPLAY") })
        {
            var runner = Started();
            Choose(runner, CampMagic);
            Choose(runner, item);

            Assert.Contains(label, runner.Unimplemented);
        }
    }

    [Fact]
    public void Cast_opens_the_spell_list()
    {
        var runner = Started();
        runner.CastableSpells = () => [new SpellListEntry("cure", 1) { Memorized = 2 }];

        Choose(runner, CampMagic);
        Choose(runner, 0);

        Assert.True(runner.CastOpen);
        Assert.Equal(["CAST", "NEXT", "PREV", "EXIT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
        Assert.Equal("cure", Assert.Single(runner.CastPageRows).SpellId);
    }

    [Fact]
    public void A_character_who_cannot_cast_never_sees_the_list()
    {
        // OnInitialEvent pops before drawing anything, so the entry looks like it did nothing
        // rather than like it failed.
        var runner = Started();
        runner.CanCastSpells = () => false;
        runner.CastableSpells = () => [new SpellListEntry("cure", 1) { Memorized = 2 }];

        Choose(runner, CampMagic);

        // The menu darkens CAST too, so reach it directly rather than through the cursor.
        runner.Menu.SetItemEnabled(0, true);
        Choose(runner, 0);

        Assert.False(runner.CastOpen);
        Assert.Equal(CastRefusal.CannotCast, runner.LastCast);
    }

    [Fact]
    public void Casting_reports_what_the_host_refused()
    {
        var runner = Started();
        runner.CastableSpells = () => [new SpellListEntry("fireball", 3) { Memorized = 1 }];
        runner.CastSpell = _ => CastRefusal.CombatOnly;

        Choose(runner, CampMagic);
        Choose(runner, 0);
        Choose(runner, 0);

        Assert.Equal(CastRefusal.CombatOnly, runner.LastCast);
        Assert.True(runner.CastOpen);            // and the list stays up
    }

    [Fact]
    public void Leaving_the_cast_list_returns_to_the_magic_menu()
    {
        var runner = Started();
        runner.CastableSpells = () => [new SpellListEntry("cure", 1) { Memorized = 1 }];

        Choose(runner, CampMagic);
        Choose(runner, 0);
        Press(runner, VirtualKey.Escape);

        Assert.False(runner.CastOpen);
        Assert.Equal(6, runner.Menu.Count);       // the magic menu
    }
}
