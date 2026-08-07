using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Drives the non-combat target picker through the runner, from camp's MAGIC menu.</summary>
public class EventAimTests
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

    private static CampEvent Camp() =>
        new(new GameEventBase(
                new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
                NoPic, NoPic, (int)EventType.Camp, 1, 0, 0,
                ChainEventHappen: 77, ChainEventNotHappen: 0,
                "You make camp.", "", "", []),
            ForceExit: 0);

    private const int CampMagic = 3;
    private const int MagicCast = 0;

    /// <summary>A selection over a party of <paramref name="partySize"/>, taking N targets.</summary>
    private static SpellTargetSelection Selection(int targets, int partySize = 4,
                                                  SpellTargeting targeting =
                                                      SpellTargeting.SelectedByCount) =>
        new(targeting,
            SpellTargets.Setup(targeting, targets, range: 0, width: 1, height: 1,
                               partySize: partySize, inCombat: false),
            partySize);

    private static EventRunner Started(
        int targets = 2, int partySize = 4,
        SpellTargeting targeting = SpellTargeting.SelectedByCount,
        List<(string Spell, int[] Slots)>? cast = null,
        Func<int, double>? hitDice = null)
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            PartySize = () => partySize,
            CastableSpells = () => [new SpellListEntry("hold", 2) { Memorized = 1 }],
            CastSpell = _ => CastRefusal.NeedsTargets,
            BeginAiming = _ => Selection(targets, partySize, targeting),
            HitDiceOf = hitDice ?? (_ => 1),
            CastAtTargets = (spell, slots) => cast?.Add((spell, [.. slots])),
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

    private static string[] Labels(EventRunner runner) =>
        [.. runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text))];

    /// <summary>
    /// The one cast that was made, as a spell and its slots.
    /// </summary>
    /// <remarks>
    /// Compared field by field rather than as a tuple: the slots are an array, and a tuple
    /// comparison would match those by reference and pass whatever they hold.
    /// </remarks>
    private static void AssertCast(List<(string Spell, int[] Slots)> cast,
                                   string spell, params int[] slots)
    {
        var made = Assert.Single(cast);
        Assert.Equal(spell, made.Spell);
        Assert.Equal(slots, made.Slots);
    }

    /// <summary>What is still wanted, which the reference puts on the menu's title.</summary>
    private static string Title(EventRunner runner) =>
        runner.Menu.Title is { } title ? BitmapFont.Decode(title) : string.Empty;

    /// <summary>Opens MAGIC, then CAST, then picks the spell — which opens the picker.</summary>
    private static EventRunner Aiming(EventRunner runner)
    {
        Choose(runner, CampMagic);
        Choose(runner, MagicCast);
        Choose(runner, 0);
        return runner;
    }

    // ---- opening --------------------------------------------------------------------------------

    [Fact]
    public void A_spell_that_names_its_targets_opens_the_picker()
    {
        var runner = Aiming(Started());

        Assert.NotNull(runner.Aiming);
        Assert.Equal("hold", runner.AimingSpell);
        Assert.Equal(["CAST SPELL ON?", "EXIT"], Labels(runner));
    }

    [Fact]
    public void The_title_says_how_many_are_still_wanted()
    {
        var runner = Aiming(Started(targets: 3));

        Assert.Contains("CHOOSE 3 TARGETS", Title(runner));
    }

    [Fact]
    public void The_remaining_count_falls_as_targets_are_taken()
    {
        var runner = Aiming(Started(targets: 3));

        Choose(runner, 0);

        Assert.Contains("CHOOSE 2 TARGETS", Title(runner));
    }

    [Fact]
    public void A_selection_the_engine_cannot_make_sense_of_never_opens()
    {
        var runner = Started();
        runner.BeginAiming = _ => null;

        Choose(runner, CampMagic);
        Choose(runner, MagicCast);
        Choose(runner, 0);

        Assert.Null(runner.Aiming);
        Assert.True(runner.CastOpen);           // still on the spell list
    }

    // ---- choosing -------------------------------------------------------------------------------

    [Fact]
    public void The_cursor_walks_the_party_with_the_vertical_keys()
    {
        // HMenuVPartyKeyboardAction: the menu takes left and right, the party takes up and down.
        var runner = Aiming(Started(partySize: 4));

        Press(runner, VirtualKey.Down);
        Assert.Equal(1, runner.AimCursor);

        Press(runner, VirtualKey.Up);
        Press(runner, VirtualKey.Up);
        Assert.Equal(3, runner.AimCursor);      // wraps
    }

    [Fact]
    public void The_last_target_closes_the_picker_by_itself()
    {
        // No confirmation step: AllTargetsChosen pops immediately, so a one-target spell is aimed
        // with a single press.
        var cast = new List<(string, int[])>();
        var runner = Aiming(Started(targets: 1, cast: cast));

        Choose(runner, 0);

        Assert.Null(runner.Aiming);
        AssertCast(cast, "hold", 0);
    }

    [Fact]
    public void Targets_are_taken_in_the_order_they_were_picked()
    {
        var cast = new List<(string, int[])>();
        var runner = Aiming(Started(targets: 2, cast: cast));

        Press(runner, VirtualKey.Down);
        Press(runner, VirtualKey.Down);
        Choose(runner, 0);                       // slot 2

        Press(runner, VirtualKey.Up);
        Choose(runner, 0);                       // slot 1

        AssertCast(cast, "hold", 2, 1);
    }

    [Fact]
    public void The_same_member_cannot_be_chosen_twice()
    {
        // STD_AddTarget refuses a duplicate; the reference logs it and leaves the menu up.
        var cast = new List<(string, int[])>();
        var runner = Aiming(Started(targets: 2, cast: cast));

        Choose(runner, 0);
        Choose(runner, 0);                       // the same slot again -- refused

        Assert.NotNull(runner.Aiming);           // still one target short
        Assert.Contains("CHOOSE 1 TARGETS", Title(runner));
        Assert.Empty(cast);
    }

    // ---- leaving --------------------------------------------------------------------------------

    [Fact]
    public void Exit_casts_at_whatever_has_been_chosen_so_far()
    {
        // Not an abandonment: the picker pops and the screen underneath casts if it has any
        // targets, so leaving a three-target spell after one pick casts it at one.
        var cast = new List<(string, int[])>();
        var runner = Aiming(Started(targets: 3, cast: cast));

        Press(runner, VirtualKey.Down);
        Choose(runner, 0);                       // slot 1
        Press(runner, VirtualKey.Escape);        // EXIT

        Assert.Null(runner.Aiming);
        AssertCast(cast, "hold", 1);
    }

    [Fact]
    public void Exit_with_nothing_chosen_casts_nothing_and_asks_nothing()
    {
        // The combat picker asks before abandoning an empty selection. This one never asks.
        var cast = new List<(string, int[])>();
        var runner = Aiming(Started(cast: cast));

        Press(runner, VirtualKey.Escape);

        Assert.Null(runner.Aiming);
        Assert.Empty(cast);
    }

    [Fact]
    public void Closing_returns_to_the_cast_list()
    {
        var runner = Aiming(Started(targets: 1));

        Choose(runner, 0);

        Assert.True(runner.CastOpen);
        Assert.Equal(["CAST", "NEXT", "PREV", "EXIT"], Labels(runner));
    }

    [Fact]
    public void Closing_puts_the_party_cursor_back()
    {
        // Choosing targets really does move the active character, which is why the caster's
        // screen saves it into tempActive and restores it on every exit path.
        var runner = Aiming(Started(targets: 1));

        Press(runner, VirtualKey.Down);
        Press(runner, VirtualKey.Down);
        Choose(runner, 0);

        Assert.Equal(0, runner.AimCursor);
    }

    // ---- the hit-dice budget --------------------------------------------------------------------

    [Fact]
    public void A_hit_dice_spell_counts_down_dice_rather_than_targets()
    {
        var cast = new List<(string, int[])>();
        var runner = Aiming(Started(targets: 5, targeting: SpellTargeting.SelectByHitDice,
                                    cast: cast, hitDice: _ => 2));

        Assert.Contains("CHOOSE 5.0 HIT DICE", Title(runner));

        Choose(runner, 0);
        Assert.Contains("CHOOSE 3.0 HIT DICE", Title(runner));

        Press(runner, VirtualKey.Down);
        Choose(runner, 0);
        Assert.Contains("CHOOSE 1.0 HIT DICE", Title(runner));

        // A third would exceed the budget, so it is refused and the picker stays up.
        Press(runner, VirtualKey.Down);
        Choose(runner, 0);

        Assert.NotNull(runner.Aiming);
        Assert.Empty(cast);
    }

    [Fact]
    public void A_target_that_exactly_spends_the_budget_lands_and_ends_it()
    {
        var cast = new List<(string, int[])>();
        var runner = Aiming(Started(targets: 4, targeting: SpellTargeting.SelectByHitDice,
                                    cast: cast, hitDice: _ => 2));

        Choose(runner, 0);
        Press(runner, VirtualKey.Down);
        Choose(runner, 0);

        Assert.Null(runner.Aiming);
        AssertCast(cast, "hold", 0, 1);
    }
}
