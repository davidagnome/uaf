using UAF.Scripting;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The ported Forth VM running a design's real <c>AI_Script.BLK</c>, checked against
/// <see cref="MonsterAiScript"/> — the transcription of that same script.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the assertion the whole Forth port is for.</b> Two independent implementations of one
/// decision function — a 2,500-line interpreter running 143 lines of Forth, and a hand transcription
/// of what those 143 lines mean — have to agree. Neither is checked against the other anywhere else,
/// and a fixture cannot settle it: the transcription and a fixture written from it would share any
/// misreading. The script is real, shipped data.
/// </para>
/// <para>
/// The candidates come from <see cref="AiActions.For"/> rather than being written out, so the pairs
/// compared are ones the engine actually produces.
/// </para>
/// </remarks>
public class ForthAiEquivalenceTests
{
    private static string Corpus
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return Path.Combine(dir.FullName, "reference");
        }
    }

    /// <summary>Both shipped versions: 1.01 and the older 0.999785.</summary>
    public static TheoryData<string> Scripts => new()
    {
        Path.Combine("dc-default", "data-files"),
        Path.Combine("Case.dsn", "Data"),
    };

    private static ForthAiScript Script(string design)
    {
        string dir = Path.Combine(Corpus, design);
        Assert.True(File.Exists(Path.Combine(dir, "AI_Script.BLK")),
                    $"the corpus is missing {design}/AI_Script.BLK");

        var script = ForthAiScript.Load(dir);
        Assert.NotNull(script);
        return script;
    }

    private static CombatMap OpenMap(int size = 25)
    {
        var map = new CombatMap(size, size);
        map.FillHoles();
        map.CombatantCount = 16;
        return map;
    }

    private static Combatant Make(CombatMap map, int index, bool friendly, int x, int y,
                                 int width = 1)
    {
        var c = new Combatant(index, friendly, new CombatantIcon(width, width), $"c{index}")
        {
            X = x,
            Y = y,
            Kind = friendly ? CombatantKind.Character : CombatantKind.Monster,
            IsAuto = true,
            AvailableAttacks = 1,
            TotalAttacks = 2,
            MaxMovement = 12,
        };
        map.Place(x, y, index);
        return c;
    }

    /// <summary>
    /// A monster carrying one of everything, so the candidate list spans every action type the
    /// script distinguishes.
    /// </summary>
    private static List<AiWeapon> Arsenal() =>
    [
        new(WeaponClass.HandCutting, Range: 1, AverageDamage: 45, DamageBonus: 10),
        new(WeaponClass.HandBlunt, Range: 1, AverageDamage: 30),
        new(WeaponClass.Bow, Range: 8, AverageDamage: 33),
        new(WeaponClass.Crossbow, Range: 12, AverageDamage: 50),
        new(WeaponClass.SpellCaster, Range: 6, AverageDamage: 20, HasSpell: true),
        new(WeaponClass.SpellLikeAbility, Range: 4, AverageDamage: 25, HasSpell: true),
    ];

    /// <summary>A fight with targets near, far and large, and an ally to be refused.</summary>
    private static (Combatant Self, List<Combatant> All, CombatMap Map) Scenario()
    {
        var map = OpenMap();
        var self = Make(map, 0, false, 10, 10);
        var all = new List<Combatant>
        {
            self,
            Make(map, 1, true, 11, 10),           // adjacent
            Make(map, 2, true, 14, 10),           // a few squares off
            Make(map, 3, true, 20, 10),           // far
            Make(map, 4, true, 12, 13, width: 2), // large
            Make(map, 5, false, 9, 10),           // an ally of the actor
        };

        all[2].State = CombatantState.Casting;
        all[3].Status = CharacterStatus.Dying;

        return (self, all, map);
    }

    private static List<AiAction> Candidates(Combatant self, List<Combatant> all,
                                             List<AiWeapon> weapons) =>
        AiActions.For(self, all, weapons, unarmedAttacks: 2, canMove: true);

    [Theory]
    [MemberData(nameof(Scripts))]
    public void The_VM_and_the_transcription_rank_every_pair_the_same_way(string design)
    {
        var script = Script(design);
        var (self, all, _) = Scenario();
        var weapons = Arsenal();
        var candidates = Candidates(self, all, weapons);

        Assert.NotEmpty(candidates);

        int compared = 0;
        foreach (var a in candidates)
        {
            foreach (var b in candidates)
            {
                int vm = script.Compare(a, b, self, all, weapons);
                int transcribed = MonsterAiScript.Compare(a, b);

                Assert.True(Math.Sign(vm) == Math.Sign(transcribed),
                            $"{design}: THINK said {vm} and the transcription {transcribed} for\n"
                            + $"  A = {a}\n  B = {b}");
                compared++;
            }
        }

        // A guard against the assertion silently covering nothing, which is how the corpus tests
        // in this repo have gone quiet before. Every ordered pair, both directions and each action
        // against itself.
        Assert.True(candidates.Count >= 12, $"only {candidates.Count} candidates enumerated");
        Assert.Equal(candidates.Count * candidates.Count, compared);
    }

    /// <summary>
    /// <c>THINK</c> is antisymmetric, which is what makes the reference's heap meaningful.
    /// </summary>
    [Theory]
    [MemberData(nameof(Scripts))]
    public void Swapping_the_two_actions_negates_the_result(string design)
    {
        var script = Script(design);
        var (self, all, _) = Scenario();
        var weapons = Arsenal();
        var candidates = Candidates(self, all, weapons);

        foreach (var a in candidates)
        {
            foreach (var b in candidates)
            {
                int forward = script.Compare(a, b, self, all, weapons);
                int backward = script.Compare(b, a, self, all, weapons);

                Assert.Equal(Math.Sign(forward), -Math.Sign(backward));
            }
        }
    }

    /// <summary>
    /// Both orderings put a best action first — but not necessarily the <i>same</i> one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The comparator is a partial order, so "the best action" is not unique.</b> Two spell-caster
    /// actions of equal damage against different targets score 0 against each other: every one of
    /// <c>THINK</c>'s eight tests reads the action or weapon type and neither the target nor the
    /// distance, so nothing separates them. Asking the two paths to name the same action would be
    /// asserting a tie-break neither the script nor the reference defines.
    /// </para>
    /// <para>
    /// <b>They do break it differently, and that is not a defect.</b> The reference sifts into a
    /// binary heap and <see cref="MonsterAiScript.Rank"/> sorts, so among tied actions they can
    /// stop at different ones. What has to hold — and what the engine actually depends on — is
    /// that whatever comes first is beaten by nothing.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Scripts))]
    public void Both_paths_put_an_unbeaten_action_first(string design)
    {
        var script = Script(design);
        var (self, all, _) = Scenario();
        var weapons = Arsenal();
        var candidates = Candidates(self, all, weapons);

        var scripted = script.Rank(candidates, self, all, weapons);
        var transcribed = MonsterAiScript.Rank(candidates);

        Assert.NotEmpty(scripted);
        Assert.Equal(candidates.Count, scripted.Count);

        foreach (var rival in candidates)
        {
            Assert.True(script.Compare(rival, scripted[0], self, all, weapons) <= 0,
                        $"{design}: {rival} beats the scripted choice {scripted[0]}");
            Assert.True(MonsterAiScript.Compare(rival, transcribed[0]) <= 0,
                        $"{rival} beats the transcribed choice {transcribed[0]}");
        }

        // And the two heads are indistinguishable to the script, which is the strongest agreement
        // a partial order permits.
        Assert.Equal(0, script.Compare(scripted[0], transcribed[0], self, all, weapons));
    }

    /// <summary>
    /// The six <c>*Filter</c> words against <see cref="MonsterAiScript.Survives"/>, which is what
    /// they were transcribed into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The senses are opposite.</b> A filter returns non-zero to <i>reject</i>; <c>Survives</c>
    /// answers whether to keep. So the assertion is <c>Survives == !Rejects</c>.
    /// </para>
    /// <para>
    /// <b>The two shipped versions disagree about the dying, and that is the whole difference.</b>
    /// 1.01 lists <c>Dying?</c> in <c>FGDP?</c> and in <c>AdvanceFilter</c>; 0.999785 does not, so
    /// its monsters keep attacking a combatant who is bleeding out. That is exactly what
    /// <c>attacksTheDying</c> selects, and pairing each script with its own flag is what makes this
    /// an equivalence rather than a coincidence.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("dc-default/data-files", false)]    // 1.01 refuses the dying
    [InlineData("Case.dsn/Data", true)]             // 0.999785 attacks them
    public void The_filters_and_the_transcription_agree(string design, bool attacksTheDying)
    {
        var script = Script(design.Replace('/', Path.DirectorySeparatorChar));
        var (self, all, _) = Scenario();
        var weapons = Arsenal();

        // (action type, filter, weapon ordinal into Arsenal, that weapon's range22)
        (AiActionType Type, ForthAiFilter Filter, int Ordinal, int Range22)[] kinds =
        [
            (AiActionType.MeleeWeapon, ForthAiFilter.MeleeWeapon, 1, 9),
            (AiActionType.RangedWeapon, ForthAiFilter.RangedWeapon, 3, 289),
            (AiActionType.SpellCaster, ForthAiFilter.SpellCaster, 5, 169),
            (AiActionType.SpellLikeAbility, ForthAiFilter.SpellLikeAbility, 6, 81),
            (AiActionType.Judo, ForthAiFilter.Judo, 0, 0),
            (AiActionType.Advance, ForthAiFilter.Advance, 0, 0),
        ];

        int checkedPairs = 0;
        foreach (var target in all)
        {
            foreach (var (type, filter, ordinal, range22) in kinds)
            {
                var action = new AiAction(type, target.Index,
                                          Distance: MonsterAiScript.DistanceBetween(self, target),
                                          WeaponOrdinal: ordinal);

                bool survives = MonsterAiScript.Survives(self, target, action, range22,
                                                         attacksTheDying);
                bool rejects = script.Rejects(filter, action, self, all, weapons);

                Assert.True(survives == !rejects,
                            $"{design}: {filter} {(rejects ? "rejected" : "kept")} what the "
                            + $"transcription {(survives ? "kept" : "rejected")} for {action}");
                checkedPairs++;
            }
        }

        Assert.Equal(all.Count * kinds.Length, checkedPairs);
    }

    /// <summary>
    /// A real design hands its compiled script to the fight.
    /// </summary>
    /// <remarks>
    /// The path the game actually takes: <c>LoadedDesign.AiScript</c> compiles the design's own
    /// <c>AI_Script.BLK</c> once, <c>Game</c> hands it to <c>CombatSession.AiScript</c>, and
    /// <c>MonsterAi.Think</c> ranks with it. Without this the whole Forth path is unreachable from
    /// a running game, which is a thing a unit test on the pieces would not have noticed.
    /// </remarks>
    [Fact]
    public void A_real_design_supplies_its_own_script()
    {
        var design = LoadedDesign.Open(Path.Combine(Corpus, "SomethingWild.dsn"));

        Assert.NotNull(design.AiScript);

        // Cached rather than recompiled -- the reference builds its dictionary once per process.
        Assert.Same(design.AiScript, design.AiScript);

        // And it ranks: a spell-caster action beats an advance, which is the script's first rule.
        var (self, all, _) = Scenario();
        var weapons = Arsenal();

        int verdict = design.AiScript.Compare(
            new AiAction(AiActionType.SpellCaster, 1, WeaponClass.SpellCaster, 20, 4, 5),
            new AiAction(AiActionType.Advance, 1, Distance: 4),
            self, all, weapons);

        Assert.True(verdict > 0, "the spell-caster action should have been preferred");
    }

    /// <summary>
    /// A design with no script, or one that will not compile, is not a scripted design.
    /// </summary>
    [Fact]
    public void A_missing_or_broken_script_loads_as_nothing()
    {
        Assert.Null(ForthAiScript.Load(Path.Combine(Corpus, "does-not-exist")));

        // Compiles, but defines no THINK, so there is nothing to rank with.
        Assert.Null(ForthAiScript.FromSource(": Unrelated 1 ; 1 SP+-"));

        // A word that is neither defined nor a number aborts the whole buffer.
        Assert.Null(ForthAiScript.FromSource(": THINK ThisWordDoesNotExist ; 1 SP+-"));
    }
}
