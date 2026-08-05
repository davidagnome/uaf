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
    /// <summary>
    /// The design corpus, found by walking up to the repository root.
    /// </summary>
    /// <remarks>
    /// This was an absolute path to one machine, which meant every assertion below silently
    /// skipped on CI and on any other checkout.
    /// </remarks>
    private static string Corpus
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
            {
                dir = dir.Parent;
            }
            return Path.Combine(dir?.FullName ?? ".", "reference");
        }
    }

    /// <summary>
    /// The one expression the reference cannot compile either — see
    /// <see cref="DiceExpression.Evaluate"/>.
    /// </summary>
    /// <remarks>
    /// <c>1.5*level</c> used to be listed here too, and it should not have been: a decimal point
    /// where an <i>operator</i> was expected ends the expression rather than failing it, so that
    /// one is <c>1</c>. Only a decimal point where a <i>term</i> was expected compiles to nothing.
    /// </remarks>
    private static readonly string[] KnownUnparsable = [".5*level"];

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
    public void The_fractional_expressions_are_still_there_and_still_mean_nothing_useful()
    {
        // If a design is ever fixed, this fails and the exemption above can shrink -- which is the
        // point of asserting it rather than quietly allowing anything to fail.
        if (!Directory.Exists(Path.Combine(Corpus, "ci-tier3")))
        {
            return;
        }

        var expressions = ExpressionsIn("ci-tier3");

        Assert.Contains(".5*level", expressions);
        Assert.Contains("1.5*level", expressions);

        // Neither is what the designer wrote it for; only one of them is nothing.
        Assert.Null(DiceExpression.Evaluate(".5*level", sides => sides, _ => 5));
        Assert.Equal(1, DiceExpression.Evaluate("1.5*level", sides => sides, _ => 5));
    }
}
