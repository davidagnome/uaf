using UAF.Media;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the player's combat menu and the targeting cursor
/// (<c>COMBAT_EVENT_DATA::OnUpdateUI</c>, <c>RunEvent.cpp:19533</c>; <c>GetNextAim</c>,
/// <c>Combatants.cpp:1363</c>).
/// </summary>
public class CombatMenuTests
{
    private static bool IsEnabled(Menu menu, CombatCommand command) =>
        menu.Items[(int)command - 1].Enabled;

    private static string LabelOf(Menu menu, CombatCommand command) =>
        BitmapFont.Decode(menu.Items[(int)command - 1].Text);

    [Fact]
    public void The_menu_has_the_fifteen_commands_in_order()
    {
        var menu = new Menu();
        CombatMenu.Build(menu, new CombatOptions());

        Assert.Equal(15, menu.Count);
        Assert.Equal("MOVE", LabelOf(menu, CombatCommand.Move));
        Assert.Equal("AIM", LabelOf(menu, CombatCommand.Aim));
        Assert.Equal("END", LabelOf(menu, CombatCommand.End));
    }

    [Fact]
    public void The_enum_is_one_based_and_the_menu_is_not()
    {
        // The enum keeps the reference's numbering so it can be checked against the source; the
        // conversion happens in one place. Passing a command straight to the menu would disable
        // its neighbour.
        var menu = new Menu();
        CombatMenu.Build(menu, new CombatOptions());

        Assert.Equal(CombatCommand.Move, CombatMenu.At(0));
        Assert.Equal(CombatCommand.Special, CombatMenu.At(14));

        CombatMenu.Enable(menu, CombatCommand.Guard, false);
        Assert.False(IsEnabled(menu, CombatCommand.Guard));
        Assert.True(IsEnabled(menu, CombatCommand.Quick));      // the neighbour is untouched
    }

    [Fact]
    public void Ready_is_disabled_whatever_the_options_say()
    {
        // Not a stub: the reference simply never enables it.
        var menu = new Menu();
        CombatMenu.Build(menu, new CombatOptions(CanMove: true, CanCast: true, CanGuard: true));

        Assert.False(IsEnabled(menu, CombatCommand.Ready));
    }

    [Fact]
    public void Win_is_editor_only()
    {
        var menu = new Menu();

        CombatMenu.Build(menu, new CombatOptions(IsEditor: false));
        Assert.False(IsEnabled(menu, CombatCommand.Win));

        CombatMenu.Build(menu, new CombatOptions(IsEditor: true));
        Assert.True(IsEnabled(menu, CombatCommand.Win));
    }

    [Fact]
    public void Casting_is_refused_for_two_separate_reasons()
    {
        // The caster cannot, or the zone forbids magic. A no-magic zone silently disables
        // spellcasting for everyone in it.
        var menu = new Menu();

        CombatMenu.Build(menu, new CombatOptions(CanCast: true, ZoneAllowsMagic: true));
        Assert.True(IsEnabled(menu, CombatCommand.Cast));

        CombatMenu.Build(menu, new CombatOptions(CanCast: false, ZoneAllowsMagic: true));
        Assert.False(IsEnabled(menu, CombatCommand.Cast));

        CombatMenu.Build(menu, new CombatOptions(CanCast: true, ZoneAllowsMagic: false));
        Assert.False(IsEnabled(menu, CombatCommand.Cast));
    }

    [Theory]
    [InlineData(false, CombatCommand.Move)]
    [InlineData(false, CombatCommand.Guard)]
    [InlineData(false, CombatCommand.Delay)]
    public void An_option_the_combatant_lacks_disables_its_command(bool available,
                                                                   CombatCommand command)
    {
        var menu = new Menu();
        CombatMenu.Build(menu, new CombatOptions(CanMove: available, CanGuard: available,
                                                 CanDelay: available));

        Assert.Equal(available, IsEnabled(menu, command));
    }

    [Fact]
    public void Turning_and_bandaging_are_off_unless_offered()
    {
        var menu = new Menu();

        CombatMenu.Build(menu, new CombatOptions());
        Assert.False(IsEnabled(menu, CombatCommand.Turn));
        Assert.False(IsEnabled(menu, CombatCommand.Bandage));

        CombatMenu.Build(menu, new CombatOptions(CanTurnUndead: true, CanBandage: true));
        Assert.True(IsEnabled(menu, CombatCommand.Turn));
        Assert.True(IsEnabled(menu, CombatCommand.Bandage));
    }

    [Fact]
    public void The_special_entry_takes_its_label_from_the_script()
    {
        var menu = new Menu();

        CombatMenu.Build(menu, new CombatOptions());
        Assert.False(IsEnabled(menu, CombatCommand.Special));
        Assert.Equal("SWEEP", LabelOf(menu, CombatCommand.Special));

        CombatMenu.Build(menu, new CombatOptions(SpecialActionName: "WHIRL"));
        Assert.True(IsEnabled(menu, CombatCommand.Special));
        Assert.Equal("WHIRL", LabelOf(menu, CombatCommand.Special));
    }

    [Fact]
    public void A_computer_run_combatant_gets_no_menu_at_all()
    {
        var menu = new Menu();
        CombatMenu.Build(menu, new CombatOptions(IsEditor: true), acting: false);

        Assert.Equal(15, menu.Count);
        Assert.All(menu.Items, item => Assert.False(item.Enabled));
    }

    // ---- the aim cursor --------------------------------------------------------------------

    private static List<Combatant> Sides()
    {
        var all = new List<Combatant>();
        for (int i = 0; i < 6; i++)
        {
            bool friendly = i < 3;
            all.Add(new Combatant(i, friendly, new CombatantIcon(1, 1), $"c{i}")
            {
                X = i + 1,
                Y = 5,
            });
        }
        return all;
    }

    [Fact]
    public void Aiming_cycles_only_through_enemies()
    {
        // Party members and anything friendly are skipped outright, so the cursor cannot be walked
        // onto your own side even though the map has no such restriction.
        var all = Sides();
        var cursor = new AimCursor();

        var seen = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            seen.Add(cursor.Next(all, all[0]));
        }

        Assert.Equal([3, 4, 5], seen);
        Assert.DoesNotContain(seen, i => all[i].IsFriendly);
    }

    [Fact]
    public void Aiming_wraps_round_the_list()
    {
        var all = Sides();
        var cursor = new AimCursor();

        for (int i = 0; i < 3; i++)
        {
            cursor.Next(all, all[0]);
        }

        Assert.Equal(3, cursor.Next(all, all[0]));      // back to the first enemy
    }

    [Fact]
    public void The_cursor_follows_the_combatant_it_lands_on()
    {
        var all = Sides();
        var cursor = new AimCursor();

        cursor.Next(all, all[0]);

        Assert.Equal(all[3].X, cursor.X);
        Assert.Equal(all[3].Y, cursor.Y);
    }

    [Fact]
    public void Aiming_steps_backwards_too()
    {
        var all = Sides();
        var cursor = new AimCursor();

        Assert.Equal(5, cursor.Previous(all, all[0]));
        Assert.Equal(4, cursor.Previous(all, all[0]));
    }

    [Fact]
    public void With_nothing_to_aim_at_the_cursor_comes_home()
    {
        // The reference returns the acting combatant rather than staying put or reporting failure.
        var all = Sides();
        foreach (var enemy in all.Where(c => !c.IsFriendly))
        {
            enemy.Status = CharacterStatus.Dead;
        }

        var cursor = new AimCursor();
        int aim = cursor.Next(all, all[0]);

        Assert.Equal(all[0].Index, aim);
        Assert.Equal(all[0].X, cursor.X);
        Assert.Equal(all[0].Y, cursor.Y);
    }

    [Fact]
    public void A_target_a_script_refuses_is_skipped()
    {
        var all = Sides();
        var cursor = new AimCursor();

        int aim = cursor.Next(all, all[0], (_, target) => target.Index != 3);
        Assert.Equal(4, aim);
    }

    [Fact]
    public void Manual_movement_stays_on_the_map_and_reports_who_is_under_it()
    {
        var map = new CombatMap(25, 25);
        map.FillHoles();
        map.CombatantCount = 8;
        map.Place(6, 5, combatant: 3);

        var cursor = new AimCursor { X = 5, Y = 5 };

        cursor.MoveBy(map, 1, 0);
        Assert.Equal((6, 5), (cursor.X, cursor.Y));
        Assert.Equal(3, cursor.Aim);

        cursor.MoveBy(map, -100, -100);
        Assert.Equal((0, 0), (cursor.X, cursor.Y));

        cursor.MoveBy(map, 500, 500);
        Assert.Equal((24, 24), (cursor.X, cursor.Y));
    }

    [Fact]
    public void Centring_puts_the_cursor_on_a_combatant()
    {
        var all = Sides();
        var cursor = new AimCursor();

        cursor.CenterOn(all[2]);

        Assert.Equal(all[2].Index, cursor.Aim);
        Assert.Equal((all[2].X, all[2].Y), (cursor.X, cursor.Y));
    }
}
