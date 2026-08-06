using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers FIX: who casts, on whom, and what stops the loop.</summary>
public class FixSpellsTests
{
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static Character Member(string name, int hitPoints = 10, int maxHitPoints = 10,
                                    byte type = 0)
    {
        var record = new CharacterRecord(
            0, 0, type, "human", 0, "cleric", 0, 0, 0, "", 0, name, name,
            0, 0, 0, 0, 0, hitPoints, maxHitPoints, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, [], [], [], 0, 0, 0, null, 0,
            null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, new ItemList([], new ReadyItems([])),
            new SpecabBlock([], [], []), []);

        return new Character(record, MoneyRules.Default);
    }

    /// <summary>Gives a character memorised copies of a spell.</summary>
    private static Character Knowing(Character who, string spellId, int memorized = 1)
    {
        who.Book.Add(spellId, 1, memorized);
        return who;
    }

    /// <summary>Always picks the first candidate — a deterministic stand-in for randomMT.</summary>
    private static int First(int count) => 0;

    /// <summary>Picks the last candidate, which is the one a swap-out moves things onto.</summary>
    private static int Last(int count) => count - 1;

    /// <summary>
    /// A cast that heals the target fully and spends the caster's copy — what the reference's
    /// CastSpell amounts to, and what makes the loop terminate.
    /// </summary>
    private static Action<Character, string, Character> Heals(List<string>? log = null) =>
        (caster, spellId, target) =>
        {
            caster.Book.DecrementMemorized(spellId);
            target.HitPoints = target.MaxHitPoints;
            log?.Add($"{caster.Name}->{target.Name}");
        };

    private static List<FixCast> Run(IReadOnlyList<string> book, IReadOnlyList<Character> party,
                                     FixEnvironment environment = FixEnvironment.Encamp,
                                     Func<int, int>? random = null,
                                     Func<string, Character, FixEnvironment, bool>? wants = null,
                                     Action<Character, string, Character>? cast = null,
                                     Func<Character?>? bishop = null) =>
        FixSpells.Run(book, party, environment,
                      random ?? First,
                      wants ?? ((_, who, _) => FixSpells.WantsFixing(who)),
                      cast ?? Heals(),
                      bishop);

    // ---- the default answer ------------------------------------------------------------------

    [Fact]
    public void A_character_below_their_maximum_wants_fixing()
    {
        Assert.True(FixSpells.WantsFixing(Member("Hurt", hitPoints: 4, maxHitPoints: 10)));
        Assert.False(FixSpells.WantsFixing(Member("Well", hitPoints: 10, maxHitPoints: 10)));
    }

    [Fact]
    public void Status_is_not_consulted_only_hit_points()
    {
        // A dead character below their maximum is a candidate; the engine hands the script hit
        // points and nothing else about their condition.
        var dead = Member("Dead", hitPoints: -12, maxHitPoints: 10);
        dead.Status = CharacterStatus.Dead;

        Assert.True(FixSpells.WantsFixing(dead));

        var petrified = Member("Stone", hitPoints: 10, maxHitPoints: 10);
        petrified.Status = CharacterStatus.Petrified;

        Assert.False(FixSpells.WantsFixing(petrified));
    }

    [Fact]
    public void Where_names_the_service_for_a_script_to_branch_on()
    {
        Assert.Equal("ENCAMP", FixSpells.Where(FixEnvironment.Encamp));
        Assert.Equal("TEMPLE", FixSpells.Where(FixEnvironment.Temple));
    }

    // ---- camp ---------------------------------------------------------------------------------

    [Fact]
    public void An_empty_fix_book_does_nothing()
    {
        var party = new[] { Member("Hurt", hitPoints: 1) };

        Assert.Empty(Run([], party));
    }

    [Fact]
    public void A_hurt_character_is_healed_by_a_party_caster()
    {
        var cleric = Knowing(Member("Cleric"), "cure");
        var hurt = Member("Hurt", hitPoints: 1);

        var made = Run(["cure"], [cleric, hurt]);

        Assert.Equal([new FixCast("cure", cleric, hurt)], made);
        Assert.Equal(10, hurt.HitPoints);
    }

    [Fact]
    public void One_spell_heals_the_whole_party_one_cast_at_a_time()
    {
        // A successful cast does not consume the entry -- the loop keeps returning it.
        var cleric = Knowing(Member("Cleric"), "cure", memorized: 3);
        var a = Member("A", hitPoints: 1);
        var b = Member("B", hitPoints: 2);

        var made = Run(["cure"], [cleric, a, b]);

        Assert.Equal(2, made.Count);
        Assert.Equal(10, a.HitPoints);
        Assert.Equal(10, b.HitPoints);
        Assert.Equal(1, cleric.Book.Find("cure")!.Memorized);
    }

    [Fact]
    public void Nobody_hurt_means_nothing_cast()
    {
        var cleric = Knowing(Member("Cleric"), "cure", memorized: 3);

        Assert.Empty(Run(["cure"], [cleric, Member("Well")]));
        Assert.Equal(3, cleric.Book.Find("cure")!.Memorized);
    }

    [Fact]
    public void Running_out_of_memorised_copies_ends_the_spell()
    {
        var cleric = Knowing(Member("Cleric"), "cure", memorized: 1);
        var a = Member("A", hitPoints: 1);
        var b = Member("B", hitPoints: 1);

        var made = Run(["cure"], [cleric, a, b]);

        Assert.Single(made);
        Assert.Equal(0, cleric.Book.Find("cure")!.Memorized);

        // One of the two was healed and the other was left where they were: the copies ran out
        // before the party did.
        Assert.Equal([1, 10], (int[])[a.HitPoints, b.HitPoints], comparer: SetOfTwo);
    }

    /// <summary>Compares two-element arrays without caring which order the random landed in.</summary>
    private static readonly IEqualityComparer<int[]> SetOfTwo =
        EqualityComparer<int[]>.Create(
            (x, y) => x is not null && y is not null && x.Order().SequenceEqual(y.Order()),
            x => x.Order().Aggregate(0, HashCode.Combine));

    [Fact]
    public void A_caster_without_the_spell_is_passed_over()
    {
        var fighter = Member("Fighter");                       // no book at all
        var cleric = Knowing(Member("Cleric"), "cure");
        var hurt = Member("Hurt", hitPoints: 1);

        var made = Run(["cure"], [fighter, cleric, hurt]);

        Assert.Equal(cleric, Assert.Single(made).Caster);
    }

    [Fact]
    public void A_caster_holding_the_spell_unmemorised_does_not_count()
    {
        var cleric = Knowing(Member("Cleric"), "cure", memorized: 0);
        var hurt = Member("Hurt", hitPoints: 1);

        Assert.Empty(Run(["cure"], [cleric, hurt]));
    }

    [Fact]
    public void A_monster_never_casts()
    {
        // CanCastSpells excludes monsters before any script runs.
        var monster = Knowing(Member("Ogre", type: (byte)CombatantKind.Monster),
                              "cure", memorized: 5);
        var hurt = Member("Hurt", hitPoints: 1);

        Assert.Empty(Run(["cure"], [monster, hurt]));
    }

    [Fact]
    public void A_caster_can_heal_themselves()
    {
        // The target pool is every party member with no filter -- including whoever is casting.
        var cleric = Knowing(Member("Cleric", hitPoints: 3), "cure");

        var made = Run(["cure"], [cleric]);

        Assert.Equal([new FixCast("cure", cleric, cleric)], made);
        Assert.Equal(10, cleric.HitPoints);
    }

    [Fact]
    public void Each_spell_keeps_its_own_casters()
    {
        var healer = Knowing(Member("Healer"), "cure");
        var mender = Knowing(Member("Mender"), "mend");
        var hurt = Member("Hurt", hitPoints: 1);

        // First heals and stops wanting; then "mend" finds nobody willing. Both spells get their
        // own caster search, so the one that runs is whichever the random reaches first.
        var made = Run(["cure", "mend"], [healer, mender, hurt]);

        Assert.Equal(["cure"], made.Select(m => m.SpellId));
        Assert.Equal(healer, made[0].Caster);
    }

    // ---- the pools are never rebuilt ---------------------------------------------------------

    [Fact]
    public void A_target_rejected_once_is_never_reconsidered_for_that_spell()
    {
        // The pool is built once per spell and a rejected candidate is dropped for good, so a
        // character who was healthy when the spell first looked cannot be healed by it later
        // however much damage they take meanwhile.
        var cleric = Knowing(Member("Cleric"), "cure", memorized: 5);
        var well = Member("Well");
        var hurt = Member("Hurt", hitPoints: 1);

        int looks = 0;

        var made = FixSpells.Run(
            ["cure"], [cleric, well, hurt], FixEnvironment.Encamp,
            First,
            (_, who, _) =>
            {
                // Wound the healthy one the moment the search passes over them.
                if (who == well && looks++ == 0)
                {
                    well.HitPoints = 1;
                    return false;
                }

                return FixSpells.WantsFixing(who);
            },
            Heals());

        Assert.Equal([hurt], made.Select(m => m.Target));
        Assert.Equal(1, well.HitPoints);          // still hurt, and out of reach
    }

    [Fact]
    public void The_environment_reaches_the_target_test()
    {
        var seen = new List<FixEnvironment>();
        var cleric = Knowing(Member("Cleric"), "cure");

        Run(["cure"], [cleric], FixEnvironment.Encamp,
            wants: (_, _, where) => { seen.Add(where); return false; });

        Assert.Equal([FixEnvironment.Encamp], seen);
    }

    // ---- the temple ---------------------------------------------------------------------------

    [Fact]
    public void The_temple_casts_from_its_bishop_and_the_party_spends_nothing()
    {
        var cleric = Knowing(Member("Cleric"), "cure", memorized: 2);
        var hurt = Member("Hurt", hitPoints: 1);
        var bishop = Knowing(Member("@TempleBishop"), "cure", memorized: 99);

        var made = Run(["cure"], [cleric, hurt], FixEnvironment.Temple,
                       bishop: () => bishop);

        Assert.Equal(bishop, Assert.Single(made).Caster);
        Assert.Equal(hurt, made[0].Target);
        Assert.Equal(2, cleric.Book.Find("cure")!.Memorized);   // untouched
    }

    [Fact]
    public void The_temple_heals_a_party_with_no_casters_at_all()
    {
        var fighter = Member("Fighter", hitPoints: 1);
        var bishop = Knowing(Member("@TempleBishop"), "cure", memorized: 99);

        var made = Run(["cure"], [fighter], FixEnvironment.Temple, bishop: () => bishop);

        Assert.Single(made);
        Assert.Equal(10, fighter.HitPoints);
    }

    [Fact]
    public void The_bishop_is_built_once_and_only_when_a_spell_needs_one()
    {
        int built = 0;
        var bishop = Knowing(Member("@TempleBishop"), "cure", memorized: 99);

        Run(["cure"], [Member("A", hitPoints: 1), Member("B", hitPoints: 1)],
            FixEnvironment.Temple, bishop: () => { built++; return bishop; });

        Assert.Equal(1, built);
    }

    [Fact]
    public void An_empty_fix_book_never_builds_a_bishop()
    {
        int built = 0;

        Run([], [Member("A", hitPoints: 1)], FixEnvironment.Temple,
            bishop: () => { built++; return null; });

        Assert.Equal(0, built);
    }

    [Fact]
    public void No_bishop_means_no_casting()
    {
        var made = Run(["cure"], [Member("A", hitPoints: 1)], FixEnvironment.Temple,
                       bishop: () => null);

        Assert.Empty(made);
    }

    // ---- the shrinking list --------------------------------------------------------------------

    [Fact]
    public void A_spell_nobody_can_cast_is_dropped_and_the_rest_still_run()
    {
        var cleric = Knowing(Member("Cleric"), "cure");
        var hurt = Member("Hurt", hitPoints: 1);

        // "unknown" has no caster; the list swaps it out and carries on with "cure".
        var made = Run(["unknown", "cure"], [cleric, hurt], random: Last);

        Assert.Equal(["cure"], made.Select(m => m.SpellId));
    }

    [Fact]
    public void Every_spell_gets_dropped_before_the_loop_ends()
    {
        // Three spells, none castable: the loop shrinks the list to nothing rather than spinning.
        var made = Run(["a", "b", "c"], [Member("Hurt", hitPoints: 1)]);

        Assert.Empty(made);
    }
}
