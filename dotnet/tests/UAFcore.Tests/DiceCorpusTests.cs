using UAF.Media.Sdl;
using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Runs every <c>DICEPLUS</c> expression in every shipped design through the evaluator.
/// </summary>
/// <remarks>
/// <para>
/// <b>This replaces a measurement that was taken twice and was wrong the first time.</b> The first
/// sample read races and classes only, concluded that <c>Male</c> was the corpus's one identifier,
/// and was contradicted the moment a character was actually rolled. The fields that carry one of
/// these expressions are: an ability's roll, a class's strength bonus, a race's weight, height,
/// age, maximum age and movement, and a spell's parameters, effect duration and effect change
/// data. All of them are walked here, so the next time the answer changes a test says so.
/// </para>
/// <para>
/// The designs are gitignored, so this returns early without <c>reference/</c>.
/// </para>
/// </remarks>
public class DiceCorpusTests
{
    private static DirectoryInfo? Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        return dir;
    }

    /// <summary>Every die shows its top face, so a total is a fact rather than a range.</summary>
    private static int Max(int count, int sides) => count * sides;

    private sealed record Expression(string Design, string Where, string Text);

    private static List<Expression> Corpus()
    {
        var all = new List<Expression>();
        if (Root() is not { } root)
        {
            return all;
        }

        foreach (string path in Directory.EnumerateDirectories(
                     Path.Combine(root.FullName, "reference")).Order())
        {
            if (!File.Exists(Path.Combine(path, "Data", "game.dat")))
            {
                continue;
            }

            using var design = LoadedDesign.Open(path, new SdlImageDecoder(),
                                                 new SdlFontRasterizer());
            string name = Path.GetFileName(path);

            void Add(string where, DicePlus? dice)
            {
                if (dice is not null)
                {
                    all.Add(new Expression(name, where, dice.Text));
                }
            }

            foreach (var ability in design.Abilities?.Values ?? [])
            {
                Add("ability.Roll", ability.Roll);
            }

            foreach (var cls in design.Classes?.Values ?? [])
            {
                Add("class.StrengthBonus", cls.StrengthBonusDice);
            }

            foreach (var race in design.Races?.Values ?? [])
            {
                Add("race.Weight", race.Weight);
                Add("race.Height", race.Height);
                Add("race.Age", race.Age);
                Add("race.MaxAge", race.MaxAge);
                Add("race.Movement", race.BaseMovement);
            }

            foreach (var spell in design.Spells ?? [])
            {
                foreach (var parameter in spell.Parameters)
                {
                    Add("spell.Parameter", parameter);
                }

                Add("spell.Duration", spell.EffectDuration);

                foreach (var effect in spell.Effects)
                {
                    Add("spell.Effect", effect.ChangeData);
                }
            }
        }

        return all;
    }

    /// <summary>A character every race test misses, which is the common case for any one race.</summary>
    private static readonly DiceSymbols Someone =
        new(Male: true, RaceId: "Elf", ClassId: "Fighter", Level: 5);

    [Fact]
    public void The_corpus_covers_every_field_that_carries_an_expression()
    {
        var corpus = Corpus();
        if (corpus.Count == 0)
        {
            return;
        }

        // The guard against the failure that produced the wrong answer last time: a database that
        // silently fails to open contributes nothing, and a sample missing a field looks exactly
        // like a field that has no expressions.
        Assert.Equal(
            ["ability.Roll", "class.StrengthBonus", "race.Age", "race.Height", "race.MaxAge",
             "race.Movement", "race.Weight", "spell.Duration", "spell.Effect", "spell.Parameter"],
            corpus.Select(e => e.Where).Distinct().Order(StringComparer.Ordinal));

        // And more than one design, so a single broken one cannot hide behind the others.
        Assert.True(corpus.Select(e => e.Design).Distinct().Count() > 1);
    }

    [Fact]
    public void Every_expression_in_every_design_either_evaluates_or_is_empty()
    {
        var corpus = Corpus();
        if (corpus.Count == 0)
        {
            return;
        }

        var refused = new List<(Expression Where, string Why)>();
        int evaluated = 0, empty = 0;

        foreach (var expression in corpus)
        {
            if (string.IsNullOrEmpty(expression.Text))
            {
                empty++;
                continue;
            }

            if (DiceFormula.TryEvaluate(expression.Text, Max, Someone.Resolver, out _,
                                        out string? why))
            {
                evaluated++;
            }
            else
            {
                refused.Add((expression, why!));
            }
        }

        // The only expressions the reference itself cannot compile are the ones that open with a
        // character its tokeniser has no term for -- the corpus's ".5*level". Anything else
        // appearing here is a gap in this port, not in the design.
        Assert.All(refused, r => Assert.StartsWith(".", r.Where.Text, StringComparison.Ordinal));

        Assert.True(evaluated > 0);
        Assert.True(empty > 0, "the corpus has empty dice fields and they must stay distinguishable");
    }

    [Fact]
    public void The_expressions_that_reference_a_race_are_ability_rolls()
    {
        var corpus = Corpus();
        if (corpus.Count == 0)
        {
            return;
        }

        // Which matters because abilities were the field the first measurement missed.
        var withRace = corpus
            .Where(e => e.Text.Contains(DiceFormula.RacePrefix, StringComparison.Ordinal))
            .Select(e => e.Where)
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(["ability.Roll"], withRace);
    }

    [Fact]
    public void Only_ability_rolls_carry_the_clamps()
    {
        var corpus = Corpus();
        if (corpus.Count == 0)
        {
            return;
        }

        // Every ability roll is clamped and nothing else is, which is why treating the bars as a
        // wrapper went unnoticed for so long: it only ever mattered to one field.
        var clamped = corpus.Where(e => e.Text.Contains("|<", StringComparison.Ordinal)).ToList();

        Assert.All(clamped, e => Assert.Equal("ability.Roll", e.Where));
        Assert.All(corpus.Where(e => e.Where == "ability.Roll"),
                   e => Assert.Contains("|<", e.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void An_ability_roll_stays_inside_its_clamps()
    {
        var corpus = Corpus().Where(e => e.Where == "ability.Roll").ToList();
        if (corpus.Count == 0)
        {
            return;
        }

        // With every die at its top face and again at 1, so both clamps are exercised. Three is
        // the floor every design writes and nineteen the highest ceiling.
        foreach (var expression in corpus)
        {
            foreach (var roll in new Func<int, int, int>[] { Max, (count, _) => count })
            {
                Assert.True(DiceFormula.TryEvaluate(expression.Text, roll, Someone.Resolver,
                                                    out int value, out string? why), why);
                Assert.InRange(value, 3, 19);
            }
        }
    }
}
