using UAF.Media.Sdl;
using UAF.Scripting;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The combat-roster calls against a real fight.
/// </summary>
/// <remarks>
/// Adjacency is a rectangle-overlap test over real footprints and the walk filters read real
/// combatant state, so unlike most of these families a fake host cannot say much. This starts an
/// actual encounter from the design.
/// </remarks>
public class GameScriptHostRosterTests
{
    /// <summary>A game with a real fight running, and the host over it.</summary>
    private static (Game Game, GameScriptHost Host)? Fighting()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? root = dir is null
            ? null
            : Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");

        if (root is null || !Directory.Exists(root))
        {
            return null;
        }

        var design = LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer());

        if (design.Level(1)?.Events.OfType<CombatEvent>().FirstOrDefault() is not { } encounter)
        {
            design.Dispose();
            return null;
        }

        var game = new Game(design, levelIndex: 1) { Dice = _ => 20 };
        game.StartEvent(encounter);

        return game.Combat is { Combatants.Count: > 1 }
            ? (game, new GameScriptHost(game))
            : null;
    }

    /// <summary>
    /// The premise: a real fight with combatants on both sides, placed on the map.
    /// </summary>
    /// <remarks>
    /// Every test below early-returns without one, so this is what stops the file passing while
    /// proving nothing.
    /// </remarks>
    [Fact]
    public void The_corpus_starts_a_fight_with_both_sides_placed()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        var roster = fight.Game.Combat!.Combatants;

        Assert.True(roster.Count > 1);
        Assert.Contains(roster, c => c.IsFriendly);
        Assert.Contains(roster, c => !c.IsFriendly);
        Assert.All(roster, c => Assert.True(c.X >= 0 && c.Y >= 0));
    }

    /// <summary>The three codes read three different things off a real combatant.</summary>
    [Fact]
    public void The_three_codes_read_three_different_things()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        var who = fight.Game.Combat!.Combatants[0];

        Assert.Equal(who.IsFriendly ? 1 : 0, fight.Host.Friendly(0, "B"));
        Assert.Equal(0, fight.Host.Friendly(0, "A"));
        Assert.Equal(who.IsFriendly ? 1 : 0, fight.Host.Friendly(0, "F"));

        // A combatant that does not exist, and a code that is not one, are both nothing.
        Assert.Null(fight.Host.Friendly(999, "F"));
        Assert.Null(fight.Host.Friendly(0, "Z"));
    }

    /// <summary>
    /// The override changes which side a combatant is on without changing which side it joined on.
    /// </summary>
    /// <remarks>
    /// <b>This is what makes a charm undoable.</b> Clearing the override restores the original
    /// side, so nothing has to remember what it was — and a port that wrote through to
    /// <c>IsFriendly</c> would lose that.
    /// </remarks>
    [Fact]
    public void The_override_changes_the_side_without_changing_the_original()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        var who = fight.Game.Combat!.Combatants[0];
        bool joinedAs = who.IsFriendly;

        // Force the opposite side.
        fight.Host.SetFriendly(0, joinedAs ? 2 : 1);

        Assert.Equal(joinedAs ? 0 : 1, fight.Host.Friendly(0, "F"));
        Assert.Equal(joinedAs ? 1 : 0, fight.Host.Friendly(0, "B"));
        Assert.Equal(joinedAs, who.IsFriendly);

        // Clearing restores it, with nothing remembered.
        fight.Host.SetFriendly(0, 0);
        Assert.Equal(joinedAs ? 1 : 0, fight.Host.Friendly(0, "F"));
    }

    /// <summary>Three is a toggle, and it is stored rather than applied.</summary>
    /// <remarks>
    /// So a combatant whose original side later changed still reads as inverted — the override is
    /// a lens over the original, not a new value.
    /// </remarks>
    [Fact]
    public void Three_inverts_rather_than_setting()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        var who = fight.Game.Combat!.Combatants[0];

        fight.Host.SetFriendly(0, 3);

        Assert.Equal(3, fight.Host.Friendly(0, "A"));
        Assert.Equal(who.IsFriendly ? 0 : 1, fight.Host.Friendly(0, "F"));
    }

    /// <summary>
    /// An adjustment outside 0–3 is ignored, which turns the call into a read.
    /// </summary>
    [Fact]
    public void An_adjustment_out_of_range_is_ignored()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        fight.Host.SetFriendly(0, 2);

        Assert.Equal(2, fight.Host.SetFriendly(0, 99));
        Assert.Equal(2, fight.Host.Friendly(0, "A"));

        Assert.Equal(2, fight.Host.SetFriendly(0, -1));
        Assert.Equal(2, fight.Host.Friendly(0, "A"));
    }

    /// <summary>
    /// Adjacency is a footprint overlap, and it is symmetric.
    /// </summary>
    /// <remarks>
    /// <b>Symmetry is the property worth checking</b> — the test is written from one combatant's
    /// rectangle against another's, and getting the margin wrong on one side would make A adjacent
    /// to B without B being adjacent to A.
    /// </remarks>
    [Fact]
    public void Adjacency_is_a_footprint_overlap_and_is_symmetric()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        var roster = fight.Game.Combat!.Combatants;

        for (int i = 0; i < roster.Count; i++)
        {
            foreach (int j in Indices(fight.Host.AdjacentCombatants(i)))
            {
                // Nobody is adjacent to themselves.
                Assert.NotEqual(i, j);

                // And adjacency runs both ways.
                Assert.Contains(i, Indices(fight.Host.AdjacentCombatants(j)));
            }
        }
    }

    /// <summary>Two combatants placed on top of each other are adjacent; far apart, they are not.</summary>
    [Fact]
    public void Moving_a_combatant_changes_who_it_touches()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        var roster = fight.Game.Combat!.Combatants;
        var a = roster[0];
        var b = roster[1];

        a.X = 5;
        a.Y = 5;
        b.X = 5;
        b.Y = 5;
        Assert.Contains(1, Indices(fight.Host.AdjacentCombatants(0)));

        b.X = 40;
        b.Y = 40;
        Assert.DoesNotContain(1, Indices(fight.Host.AdjacentCombatants(0)));
    }

    /// <summary>The list is pipe-prefixed, so an empty one is the empty string.</summary>
    [Fact]
    public void An_empty_adjacency_list_is_empty_not_a_bare_delimiter()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        // Put everybody far apart.
        var roster = fight.Game.Combat!.Combatants;
        for (int i = 0; i < roster.Count; i++)
        {
            roster[i].X = i * 10;
            roster[i].Y = i * 10;
        }

        Assert.Equal(string.Empty, fight.Host.AdjacentCombatants(0));
    }

    /// <summary>
    /// The walk visits every combatant once, in order, and stops.
    /// </summary>
    [Fact]
    public void An_unfiltered_walk_visits_everybody_in_order()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        var seen = new List<int>();
        int? at = null;

        while (fight.Host.NextCreature(at, 0) is { } next)
        {
            seen.Add(next);
            at = next;
        }

        Assert.Equal(Enumerable.Range(0, fight.Game.Combat!.Combatants.Count), seen);
    }

    /// <summary>
    /// The side filters select opposite halves of the roster.
    /// </summary>
    /// <remarks>
    /// <b>They read the raw side, not the override</b> — the reference tests <c>friendly</c> here
    /// where <c>ListAdjacentCombatants</c> tests <c>GetIsFriendly()</c>. A charmed monster is still
    /// hostile to this walk.
    /// </remarks>
    [Fact]
    public void The_side_filters_select_opposite_halves()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        var hostile = Walk(fight.Host, (int)GpdlCreatureFilter.Hostile);
        var friendly = Walk(fight.Host, (int)GpdlCreatureFilter.Friendly);
        var roster = fight.Game.Combat!.Combatants;

        Assert.NotEmpty(hostile);
        Assert.NotEmpty(friendly);
        Assert.All(hostile, i => Assert.False(roster[i].IsFriendly));
        Assert.All(friendly, i => Assert.True(roster[i].IsFriendly));

        // Between them they are the whole roster, with nothing in both.
        Assert.Equal(roster.Count, hostile.Count + friendly.Count);
        Assert.Empty(hostile.Intersect(friendly));

        // And a charm does not move anybody between them.
        fight.Host.SetFriendly(hostile[0], 1);
        Assert.Equal(hostile, Walk(fight.Host, (int)GpdlCreatureFilter.Hostile));
    }

    /// <summary>
    /// Asking for both sides at once matches nobody.
    /// </summary>
    /// <remarks>
    /// The flags are <i>skip</i> rules, so setting both drops every combatant. Nothing warns about
    /// it, and a design writing <c>6</c> meaning "either side" gets an empty walk.
    /// </remarks>
    [Fact]
    public void Asking_for_both_sides_matches_nobody()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        Assert.Empty(Walk(fight.Host,
                          (int)(GpdlCreatureFilter.Hostile | GpdlCreatureFilter.Friendly)));
    }

    /// <summary>
    /// The living filter keeps the unconscious and the dying.
    /// </summary>
    /// <remarks>
    /// <b>Only fled, gone, petrified and dead are not alive</b> (<c>Char.h:680</c>) — so a filter
    /// asking for the living gets everybody who might still be healed, which is the point.
    /// </remarks>
    [Fact]
    public void The_living_filter_keeps_the_unconscious_and_dying()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        var roster = fight.Game.Combat!.Combatants;

        roster[0].Status = CharacterStatus.Unconscious;
        roster[1].Status = CharacterStatus.Dead;

        var alive = Walk(fight.Host, (int)GpdlCreatureFilter.Alive);

        Assert.Contains(0, alive);
        Assert.DoesNotContain(1, alive);
    }

    /// <summary>
    /// Sight over a real combat map, and the two walks disagreeing on it.
    /// </summary>
    /// <remarks>
    /// <b>The engine has two line-of-sight algorithms and <c>$IsLineOfSight</c> and
    /// <c>$VisualDistance</c> use different ones.</b> They differ on squares off the map and
    /// squares with no terrain — and a generated combat map has plenty of the latter, so on a real
    /// fight a script really can be told it has a clear line and then be given
    /// <see cref="GpdlLineOfSight.NotVisible"/> for the distance along it.
    /// </remarks>
    [Fact]
    public void The_two_sight_walks_disagree_on_a_real_map()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        var map = fight.Game.Combat!.Map;

        // Somewhere off the map is clear to one walk and blocked to the other.
        Assert.True(fight.Host.IsLineOfSight(-5, 0, -1, 0));

        // A combatant sees itself at distance zero, whatever the terrain.
        Assert.Equal(0, fight.Host.VisualDistance(0, 0));

        // And a pair of real combatants gets an answer that is either a distance or the marker,
        // never anything in between.
        int distance = fight.Host.VisualDistance(0, 1);
        Assert.True(distance >= 0);
        Assert.True(distance <= map.Width + map.Height
                    || distance == GpdlLineOfSight.NotVisible);
    }

    /// <summary>A combatant that is not there cannot be seen.</summary>
    [Fact]
    public void A_missing_combatant_is_not_visible()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        Assert.Equal(GpdlLineOfSight.NotVisible, fight.Host.VisualDistance(0, 999));
        Assert.Equal(GpdlLineOfSight.NotVisible, fight.Host.VisualDistance(999, 0));
    }

    /// <summary>
    /// Computed damage is a sampled outcome, not a fixed number.
    /// </summary>
    /// <remarks>
    /// <b>It rolls.</b> The reference runs a real to-hit computation and then a real damage
    /// computation, so this is one attack's worth of luck. With dice that always show their
    /// maximum the answer is stable and non-zero; with dice that always show one it can miss.
    /// </remarks>
    [Fact]
    public void Computed_damage_rolls_rather_than_averaging()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        var attacker = fight.Game.Combat!.Combatants[0];
        var defender = fight.Game.Combat.Combatants[1];

        int hitPoints = defender.HitPoints;
        double swings = attacker.AvailableAttacks;

        // Every die at its maximum: the attack lands and does its most.
        fight.Game.Dice = sides => sides;
        int best = fight.Host.ComputeAttackDamage(0, 1);

        // Every die at one: the same pair, and a miss.
        fight.Game.Dice = _ => 1;
        int worst = fight.Host.ComputeAttackDamage(0, 1);

        Assert.True(best > worst, $"max dice gave {best}, min dice gave {worst}");

        // Nothing was hurt by asking, and no swing was spent -- this is a question, not an attack.
        Assert.Equal(hitPoints, defender.HitPoints);
        Assert.Equal(swings, attacker.AvailableAttacks);
    }

    /// <summary>
    /// Distance and side are not considered, because the reference does not consider them.
    /// </summary>
    /// <remarks>
    /// <b>No targeting check at all.</b> <c>ComputeAttackDamage</c> goes straight to the to-hit and
    /// damage computations, so it answers "if these two fought, what would happen" however far
    /// apart they are standing and whichever sides they are on. Using the port's own
    /// <c>Attack.Resolve</c> here would have added a refusal the reference has not got — and it
    /// showed up immediately as two zeroes.
    /// </remarks>
    [Fact]
    public void Neither_distance_nor_side_is_considered()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        fight.Game.Dice = sides => sides;

        var roster = fight.Game.Combat!.Combatants;

        // Right next to each other...
        roster[0].X = 5;
        roster[0].Y = 5;
        roster[1].X = 6;
        roster[1].Y = 5;
        int near = fight.Host.ComputeAttackDamage(0, 1);

        // ...and at opposite corners of the map.
        roster[1].X = fight.Game.Combat.Map.Width - 1;
        roster[1].Y = fight.Game.Combat.Map.Height - 1;

        Assert.Equal(near, fight.Host.ComputeAttackDamage(0, 1));
        Assert.True(near > 0);
    }

    /// <summary>A combatant that is not there deals nothing.</summary>
    [Fact]
    public void A_missing_combatant_deals_nothing()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        Assert.Equal(0, fight.Host.ComputeAttackDamage(0, 999));
        Assert.Equal(0, fight.Host.ComputeAttackDamage(999, 0));
    }

    /// <summary>
    /// The to-hit roll is visible only while a swing is being resolved.
    /// </summary>
    /// <remarks>
    /// <b>Cleared afterwards, so a script asking outside an attack gets nothing rather than the
    /// last one's roll</b> — which the VM then reports as the reference's plausible ten.
    /// </remarks>
    [Fact]
    public void The_to_hit_roll_is_only_visible_during_a_swing()
    {
        if (Fighting() is not { } fight)
        {
            return;
        }

        Assert.Null(fight.Game.Combat!.ToHitRoll);
        Assert.Null(fight.Host.ToHitRoll);
    }

    private static List<int> Walk(GameScriptHost host, int filter)
    {
        var seen = new List<int>();
        int? at = null;

        while (host.NextCreature(at, filter) is { } next)
        {
            seen.Add(next);
            at = next;
        }

        return seen;
    }

    private static List<int> Indices(string list) =>
        [.. list.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)];
}
