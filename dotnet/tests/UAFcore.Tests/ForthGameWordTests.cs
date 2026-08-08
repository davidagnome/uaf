using UAF.Scripting;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The twenty-one words that read a <c>COMBAT_SUMMARY</c>, each run against a real projection
/// (<c>Forth.cpp:2132</c>–<c>:2230</c>).
/// </summary>
/// <remarks>
/// Each case defines its own <c>THINK</c> that pushes the value under test, so the word is reached
/// through the same path the engine uses rather than through a back door.
/// </remarks>
public class ForthGameWordTests
{
    private static CombatMap OpenMap()
    {
        var map = new CombatMap(25, 25);
        map.FillHoles();
        map.CombatantCount = 16;
        return map;
    }

    /// <summary>
    /// A monster at (10,10) and a friendly target two squares east.
    /// </summary>
    /// <remarks>
    /// The gap is 2 squares on one axis, so <c>distance22</c> is <c>4 × (2² + 0²)</c> = 16.
    /// </remarks>
    private static (Combatant Self, List<Combatant> All, List<AiWeapon> Weapons) Fight()
    {
        var map = OpenMap();

        var self = new Combatant(0, false, new CombatantIcon(1, 1), "ogre")
        {
            X = 10, Y = 10, Kind = CombatantKind.Monster, IsAuto = true,
            AvailableAttacks = 3, TotalAttacks = 3, MaxMovement = 12,
            State = CombatantState.Guarding,
        };
        map.Place(10, 10, 0);

        var foe = new Combatant(1, true, new CombatantIcon(2, 2), "knight")
        {
            X = 12, Y = 10, Kind = CombatantKind.Character,
            AvailableAttacks = 1, MaxMovement = 12,
            State = CombatantState.Casting,
        };
        map.Place(12, 10, 1);

        List<AiWeapon> weapons =
        [
            new(WeaponClass.HandCutting, Range: 1, AverageDamage: 45, DamageBonus: 10),
            new(WeaponClass.Bow, Range: 8, AverageDamage: 33),
        ];

        return (self, [self, foe], weapons);
    }

    /// <summary>The melee action the words below are pointed at.</summary>
    private static AiAction Melee() =>
        new(AiActionType.MeleeWeapon, Target: 1, WeaponType: WeaponClass.HandCutting,
            Damage: 55, Distance: 16, WeaponOrdinal: 1);

    /// <summary>Compiles <paramref name="body"/> as THINK and returns what it leaves on the stack.</summary>
    private static int Value(string body, AiAction? action = null)
    {
        var (self, all, weapons) = Fight();
        var script = ForthAiScript.FromSource($": THINK {body} ; 1 SP+-");

        Assert.NotNull(script);

        var one = action ?? Melee();
        return script.Compare(one, one, self, all, weapons);
    }

    [Theory]
    // -- the action, under Me ---------------------------------------------------------------
    [InlineData("Me A A:Type", 4)]              // AT_MeleeWeapon
    [InlineData("Me A A:Damage", 55)]
    [InlineData("Me A C:Distance", 16)]         // 4 * (2^2), not 2

    // -- the weapon at the action's ordinal, from the selected combatant ---------------------
    [InlineData("Me A W:Type", 2)]              // HandCutting
    [InlineData("Me A W:Range", 9)]             // (2*1 + 1)^2
    [InlineData("Me A W:Damage", 55)]
    [InlineData("Me A W:Protection", 0)]
    [InlineData("Me A W:ROF", 0)]
    [InlineData("Me A W:AttackBonus", 0)]
    [InlineData("Me A W:Priority", 0)]

    // -- the combatant ----------------------------------------------------------------------
    [InlineData("Me A C:Friendly", 1)]          // the actor is on its own side
    [InlineData("He A C:Friendly", 0)]          // the target is not
    [InlineData("Me A C:State", 3)]             // C:S:Guarding
    [InlineData("He A C:State", 1)]             // C:S:Casting
    [InlineData("Me A Fleeing@", 0)]
    [InlineData("Me A C:AIBaseclass", -1)]      // "not computed", which is what the port supplies
    [InlineData("Me A C:HasLineOfSight", 0)]
    public void A_word_reads_what_the_projection_holds(string body, int expected)
    {
        Assert.Equal(expected, Value(body));
    }

    /// <summary>
    /// <b>Only combatant 0 has weapons</b>, so every <c>W:</c> word under <c>He</c> pushes
    /// <c>NotWeapon</c>.
    /// </summary>
    /// <remarks>
    /// The reference calls <c>ListWeapons</c> for the active combatant alone. This is not an
    /// oversight to correct — a script that reads a target's weapon gets 0, and the shipped script
    /// never does.
    /// </remarks>
    [Theory]
    [InlineData("He A W:Type")]
    [InlineData("He A W:Range")]
    [InlineData("He A W:Damage")]
    public void A_weapon_word_under_He_finds_nothing(string body)
    {
        Assert.Equal(0, Value(body));
    }

    /// <summary>
    /// An ordinal past the end of the weapon list pushes <c>NotWeapon</c> rather than reading on.
    /// </summary>
    [Theory]
    [InlineData(0)]     // the reference's "no weapon"
    [InlineData(3)]     // one past the two the actor carries
    [InlineData(99)]
    public void A_weapon_ordinal_out_of_range_is_NotWeapon(int ordinal)
    {
        var action = Melee() with { WeaponOrdinal = ordinal };

        Assert.Equal(0, Value("Me A W:Type", action));
        Assert.Equal(0, Value("Me A W:Damage", action));
    }

    /// <summary>The second weapon, to prove the ordinal indexes rather than always taking the first.</summary>
    [Fact]
    public void The_ordinal_selects_which_weapon_is_read()
    {
        var bow = Melee() with { WeaponOrdinal = 2 };

        Assert.Equal(5, Value("Me A W:Type", bow));      // W:T:Bow
        Assert.Equal(289, Value("Me A W:Range", bow));   // (2*8 + 1)^2
        Assert.Equal(33, Value("Me A W:Damage", bow));
    }

    /// <summary>
    /// <c>Shield.Next</c> cycles through <i>n+1</i> values, and a combatant with no shields
    /// therefore only ever yields 0.
    /// </summary>
    [Fact]
    public void Shield_Next_on_a_combatant_with_no_shields_stays_at_zero()
    {
        Assert.Equal(0, Value("Me A 0 Shield.Next"));
        Assert.Equal(0, Value("Me A 5 Shield.Next"));
    }

    /// <summary>
    /// <c>Me</c> and <c>He</c> set the end on <b>both</b> candidate actions at once.
    /// </summary>
    /// <remarks>
    /// The reference stores the choice per action — <c>pActionA->pCSC</c> and
    /// <c>pActionB->pCSC</c> — but always writes both, so switching candidates with <c>A</c> or
    /// <c>B</c> keeps whichever end was last selected. This is what lets the shipped script write
    /// <c>Me B W:Type A W:Type</c> and read the actor's weapon for each candidate in turn.
    /// </remarks>
    [Fact]
    public void The_end_selection_survives_switching_candidates()
    {
        // Select He, then switch to B and back to A: still reading the target.
        Assert.Equal(0, Value("He B A C:Friendly"));

        // And the same with Me.
        Assert.Equal(1, Value("Me B A C:Friendly"));
    }

    /// <summary>
    /// A summary word reached outside a run has nothing to read and says so.
    /// </summary>
    [Fact]
    public void A_summary_word_outside_a_run_refuses()
    {
        var forth = new ForthMachine();
        Assert.True(forth.Bootstrap());

        var thrown = Assert.Throws<InvalidOperationException>(() => forth.Evaluate("C:Distance"));
        Assert.Contains("RunThink", thrown.Message, StringComparison.Ordinal);
    }
}
