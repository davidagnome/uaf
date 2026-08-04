using UAF.Common;
using UAF.Media;
using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the character generator's spell screen — the one screen behind both spell steps.
/// </summary>
/// <remarks>
/// <b>It has no EXIT.</b> Both menus are three entries, so the only way off is for the
/// acquisition rules to say every level is finished.
/// </remarks>
public class EventSpellScreenTests
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
                NoPic, NoPic, (int)EventType.TrainingHallEvent, 1, 0, 0, 55, 0, "Train?", "", "",
                []),
            0, [new TrainableBaseclass("fighter", 1, 20, "")], 100);

    private static SpellRecord Spell(string name, int level) =>
        new(0, name, "", "magic", ["mage"], level, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0,
            0, 0, [], [], null, [], [], "", [], null, new SpecabBlock([], [], []), []);

    /// <summary>Spells at levels 1 and 2, with the counts that bound them.</summary>
    private static SpellScreenData Screen(int certain = 9, int max = 9, int min = 0,
                                          int globalMax = 99, int probability = 100)
    {
        List<List<AvailableSpell>> byLevel =
        [
            [],
            [new AvailableSpell(Spell("Sleep", 1), probability),
             new AvailableSpell(Spell("Shield", 1), probability)],
            [new AvailableSpell(Spell("Web", 2), probability)],
        ];

        List<SpellLevelState> levels =
        [
            new(new SpellCounts(min, 0, globalMax, 0), 0),
            new(new SpellCounts(min, 0, max, certain), byLevel[1].Count),
            new(new SpellCounts(min, 0, max, certain), byLevel[2].Count),
        ];

        return new SpellScreenData("SELECT", byLevel, levels);
    }

    private static EventRunner AtSpells(SpellScreenData? screen = null,
                                        Func<int, int>? roll = null)
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            CanTrainHere = _ => false,
            CreationChoicesFor = making => making.Step switch
            {
                CreationStep.Race => [new CreationChoice("Elf", "ELF")],
                CreationStep.Gender => CreationChoices.Genders,
                CreationStep.Class => [new CreationChoice("Mage", "MAGE")],
                CreationStep.Alignment => CreationChoices.Alignments,
                _ => [],
            },
            ArtFor = _ => ["cn_Icon1.png"],
            SpellScreenFor = _ => screen,
            RollPercent = roll ?? (_ => 1),
        };

        runner.Begin(Hall(), Font(), Box, Anchors);

        Choose(runner, 0);                        // YES at the hall
        Choose(runner, 6);                        // CREATE CHARACTER

        Pick(runner);                             // race
        Pick(runner);                             // gender
        Pick(runner);                             // class
        Pick(runner);                             // alignment

        foreach (char c in "Aramil")
        {
            runner.Handle(InputEvent.Text(c));
        }
        runner.Handle(InputEvent.KeyDown(VirtualKey.Return));   // name

        Choose(runner, 2);                        // icon SELECT
        Choose(runner, 2);                        // portrait SELECT
        return runner;
    }

    /// <summary>SELECT on a creation picker.</summary>
    private static void Pick(EventRunner runner) => Choose(runner, 0);

    /// <summary>
    /// Walks to a menu entry whichever way the menu is laid out.
    /// </summary>
    /// <remarks>
    /// The generator alternates between horizontal menus (the pickers) and a vertical one (the
    /// party menu), so a helper that presses one arrow silently fails to move on the other and
    /// commits whatever was already selected.
    /// </remarks>
    private static void Choose(EventRunner runner, int item)
    {
        for (int i = 0; i < runner.Menu.Count * 2 && runner.Menu.ActiveItem != item; i++)
        {
            int before = runner.Menu.ActiveItem;
            runner.Handle(InputEvent.KeyDown(VirtualKey.Right));

            if (runner.Menu.ActiveItem == before)
            {
                runner.Handle(InputEvent.KeyDown(VirtualKey.Down));
            }
        }

        Assert.Equal(item, runner.Menu.ActiveItem);
        runner.Handle(InputEvent.KeyDown(VirtualKey.Return));
    }

    private static string[] Labels(EventRunner runner) =>
        [.. runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text))];

    [Fact]
    public void The_screen_offers_the_spells_of_the_level_showing()
    {
        var runner = AtSpells(Screen());

        Assert.NotNull(runner.SpellChoices);
        Assert.Equal(1, runner.SpellLevel);
        Assert.Equal(["Sleep", "Shield"], runner.SpellChoices!.Select(s => s.Spell.Name));
    }

    [Fact]
    public void There_is_no_way_out_but_to_pick()
    {
        // Three entries and no EXIT, so Escape has nothing to select.
        var runner = AtSpells(Screen());

        Assert.Equal(["SELECT", "NEXT", "PREV"], Labels(runner));

        runner.Handle(InputEvent.KeyDown(VirtualKey.Escape));

        Assert.NotNull(runner.SpellChoices);
    }

    [Fact]
    public void A_free_pick_succeeds_and_says_so()
    {
        var runner = AtSpells(Screen(certain: 9), roll: _ => 100);

        Choose(runner, 0);                        // SELECT

        Assert.Contains("successfully acquired Sleep", runner.SpellMessage);
    }

    [Fact]
    public void Past_the_allowance_a_failed_roll_says_so()
    {
        // A 50% spell and a roll of 100: the allowance is spent, so this really can fail.
        var runner = AtSpells(Screen(certain: 0, probability: 50), roll: _ => 100);

        Choose(runner, 0);

        Assert.Contains("failed to acquire Sleep", runner.SpellMessage);
    }

    [Fact]
    public void A_failed_attempt_still_consumes_the_spell()
    {
        // The reference marks the entry either way, so there is no second go at it -- which is
        // what makes "how many he must try" a bound at all.
        var runner = AtSpells(Screen(certain: 0, probability: 50), roll: _ => 100);

        Choose(runner, 0);

        Assert.DoesNotContain(runner.SpellChoices ?? [], s => s.Spell.Name == "Sleep");
    }

    [Fact]
    public void Next_and_prev_move_between_spell_levels()
    {
        var runner = AtSpells(Screen());

        Choose(runner, 1);                        // NEXT
        Assert.Equal(2, runner.SpellLevel);
        Assert.Equal(["Web"], runner.SpellChoices!.Select(s => s.Spell.Name));

        Choose(runner, 2);                        // PREV
        Assert.Equal(1, runner.SpellLevel);
    }

    [Fact]
    public void Reaching_the_global_maximum_closes_the_screen()
    {
        // One spell allowed in total: the first pick ends it.
        var runner = AtSpells(Screen(globalMax: 1));

        Choose(runner, 0);

        Assert.Null(runner.SpellChoices);
    }

    [Fact]
    public void A_character_with_no_spells_never_sees_the_screen()
    {
        var runner = AtSpells(screen: null);

        Assert.Null(runner.SpellChoices);
        Assert.Null(runner.Creating);            // the wizard ran past it and stopped
    }
}
