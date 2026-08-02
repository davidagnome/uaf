using UAF.Rules;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Checks <see cref="DiceExpression"/> against every dice expression the shipped designs contain.
/// </summary>
/// <remarks>
/// A grammar reconstructed from a handful of examples is a guess; run against the corpus it is a
/// fact. Skips silently when a design is not present, so the suite still runs on a bare checkout.
/// </remarks>
public class DiceExpressionCorpusTests
{
    private const string Corpus = "/Volumes/Data/Dev/uaf/reference";

    /// <summary>
    /// The two fractional expressions the reference cannot compile either — see
    /// <see cref="DiceExpression.Evaluate"/>. They evaluate to nothing in the shipped engine.
    /// </summary>
    private static readonly string[] KnownUnparsable = [".5*level", "1.5*level"];

    private static List<string> ExpressionsIn(string design)
    {
        var spells = LoadedDesign.Open(Path.Combine(Corpus, design)).Spells ?? [];

        return [.. spells.SelectMany(s => s.Effects).Select(e => e.ChangeData?.Text ?? string.Empty)
                    .Concat(spells.SelectMany(s => s.Parameters).Select(d => d.Text ?? string.Empty))
                    .Concat(spells.Select(s => s.EffectDuration?.Text ?? string.Empty))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()];
    }

    [Theory]
    [InlineData("ci-tier3")]
    [InlineData("SomethingWild.dsn")]
    [InlineData("Case.dsn")]
    [InlineData("Ambassador's_Letter")]
    public void Every_dice_expression_a_design_contains_evaluates(string design)
    {
        if (!Directory.Exists(Path.Combine(Corpus, design)))
        {
            return;
        }

        var expressions = ExpressionsIn(design);
        Assert.NotEmpty(expressions);

        var unparsed = expressions
            .Where(x => DiceExpression.Evaluate(x, sides => sides, _ => 5) is null)
            .Except(KnownUnparsable)
            .ToList();

        Assert.Empty(unparsed);
    }

    [Fact]
    public void The_fractional_expressions_are_still_there_and_still_do_nothing()
    {
        // If a design is ever fixed, this fails and the exemption above can shrink -- which is the
        // point of asserting it rather than quietly allowing anything to fail.
        if (!Directory.Exists(Path.Combine(Corpus, "ci-tier3")))
        {
            return;
        }

        var expressions = ExpressionsIn("ci-tier3");

        Assert.Contains(".5*level", expressions);
        Assert.All(KnownUnparsable,
                   x => Assert.Null(DiceExpression.Evaluate(x, sides => sides, _ => 5)));
    }
}
