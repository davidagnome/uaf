using System.Text.RegularExpressions;
using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers <see cref="Strength"/>.</summary>
public partial class StrengthTests
{
    [Theory]
    [InlineData(1, 0, -5, -4)]
    [InlineData(3, 0, -3, -1)]
    [InlineData(5, 0, -2, -1)]      // the 4-5 band
    [InlineData(9, 0, 0, 0)]
    [InlineData(16, 0, 0, 1)]       // damage before hit, which is the AD&D shape
    [InlineData(17, 0, 1, 1)]
    [InlineData(19, 0, 3, 7)]
    [InlineData(25, 0, 7, 16)]
    public void The_table_gives_the_hit_and_damage_bonuses(
        int score, int mod, int hit, int damage)
    {
        Assert.Equal(hit, Strength.HitBonus(score, mod));
        Assert.Equal(damage, Strength.DamageBonus(score, mod));
    }

    [Theory]
    [InlineData(0, 1, 2)]           // exactly 18, no percentile
    [InlineData(1, 1, 3)]
    [InlineData(50, 1, 3)]
    [InlineData(51, 2, 3)]
    [InlineData(75, 2, 3)]
    [InlineData(76, 2, 4)]
    [InlineData(90, 2, 4)]
    [InlineData(91, 2, 5)]
    [InlineData(99, 2, 5)]
    [InlineData(100, 3, 6)]         // 18/00
    public void Eighteen_splits_by_percentile(int mod, int hit, int damage)
    {
        Assert.Equal(hit, Strength.HitBonus(18, mod));
        Assert.Equal(damage, Strength.DamageBonus(18, mod));
    }

    [Fact]
    public void The_exact_zero_case_is_distinct_from_the_first_band()
    {
        // The chain opens `if (strengthMod == 0)`, so an 18 with no percentile is its own row
        // rather than falling into the "< 51" band above it.
        Assert.Equal(2, Strength.DamageBonus(18, 0));
        Assert.Equal(3, Strength.DamageBonus(18, 1));
    }

    [Fact]
    public void An_unhandled_score_is_all_zeroes()
    {
        // The reference logs "Unhandled strength" and leaves its out-parameters alone. Zero is the
        // same thing for a freshly-zeroed caller and predictable for everyone else.
        Assert.Equal(Strength.None, Strength.For(0));
        Assert.Equal(Strength.None, Strength.For(26));
        Assert.Equal(0, Strength.DamageBonus(99));
    }

    [Fact]
    public void Every_score_from_one_to_twenty_five_is_covered()
    {
        for (int score = 1; score <= 25; score++)
        {
            Assert.NotEqual(Strength.None, Strength.For(score, mod: 0));
        }
    }

    // ---- against the C++ source ----------------------------------------------------------------

    [GeneratedRegex(@"case (\d+):")]
    private static partial Regex CaseLabel();

    [GeneratedRegex(@"(hitBonus|dmgBon|openDoor|openMagicDoor|BB_LG) *= *(-?\d+);")]
    private static partial Regex Assignment();

    /// <summary>
    /// Re-derives the table from <c>GameRules.cpp</c> and compares it against the transcription.
    /// </summary>
    /// <remarks>
    /// The table was generated from the source rather than typed, and this is what keeps it that
    /// way: 24 rows of six numbers with irregular bands is exactly where a hand-copied digit hides
    /// forever. It reads the switch's <c>case</c> labels and assignments and checks that the same
    /// score yields the same hit and damage bonus on both sides.
    /// </remarks>
    [Fact]
    public void The_transcription_still_matches_the_reference_switch()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null) { return; }

        string path = Path.Combine(dir.FullName, "src", "Shared", "GameRules.cpp");
        if (!File.Exists(path)) { return; }

        var lines = File.ReadAllLines(path);
        int start = Array.FindIndex(lines, l => l.StartsWith("void determineStrengthProperties",
                                                             StringComparison.Ordinal));
        Assert.True(start > 0, "determineStrengthProperties has moved or been renamed");

        // Begin at the switch, not the signature: the parameter list itself contains the words
        // "strengthMod", which set the percentile flag before the first case and silently dropped
        // strength 1 from the comparison.
        start = Array.FindIndex(lines, start, l => l.Contains("switch (strength)",
                                                              StringComparison.Ordinal));
        Assert.True(start > 0, "the strength switch has moved");

        // Walk the switch, tracking the scores each block covers and the bonuses it assigns. Only
        // the percentile-free rows are compared, since matching the if/else chain here would mean
        // reimplementing the thing under test.
        var pending = new List<int>();
        int? hit = null, damage = null;
        bool sawModifier = false;
        int checkedRows = 0;

        for (int i = start; i < lines.Length && !lines[i].StartsWith('}'); i++)
        {
            string line = lines[i].Trim();

            if (CaseLabel().Match(line) is { Success: true } label)
            {
                pending.Add(int.Parse(label.Groups[1].ValueSpan));
                continue;
            }

            if (line.Contains("strengthMod", StringComparison.Ordinal)) { sawModifier = true; }

            if (Assignment().Match(line) is { Success: true } assignment)
            {
                int value = int.Parse(assignment.Groups[2].ValueSpan);
                if (assignment.Groups[1].Value == "hitBonus") { hit = value; }
                if (assignment.Groups[1].Value == "dmgBon") { damage = value; }
                continue;
            }

            if (line == "break;")
            {
                if (!sawModifier && hit is { } h && damage is { } d)
                {
                    foreach (int score in pending)
                    {
                        Assert.Equal(h, Strength.HitBonus(score));
                        Assert.Equal(d, Strength.DamageBonus(score));
                        checkedRows++;
                    }
                }

                pending.Clear();
                hit = damage = null;
                sawModifier = false;
            }
        }

        // 24 scores have no percentile: 1-17 and 19-25.
        Assert.Equal(24, checkedRows);
    }
}
