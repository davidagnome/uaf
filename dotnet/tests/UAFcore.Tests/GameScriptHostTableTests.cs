using UAF.Data;
using UAF.Media.Sdl;
using UAF.Scripting;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// <c>$IntegerTable</c> and <c>$RollHitPointDice</c> against a loaded design.
/// </summary>
public class GameScriptHostTableTests
{
    private static Game? Load()
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

        // Every die shows its maximum, so a roll is arithmetic rather than a range.
        return new Game(design, levelIndex: 1) { Dice = sides => sides };
    }

    /// <summary>
    /// The premise: the design has baseclasses with hit dice, and special abilities to hold tables.
    /// </summary>
    /// <remarks>
    /// The tests below early-return without them, so this is what stops the file passing while
    /// proving nothing.
    /// </remarks>
    [Fact]
    public void The_corpus_has_baseclasses_and_abilities()
    {
        if (Load() is not { } game)
        {
            return;
        }

        Assert.NotNull(game.Design.Baseclasses);
        Assert.NotEmpty(game.Design.Baseclasses!);
        Assert.Contains(game.Design.Baseclasses!.Values, b => b.HitDice.Count > 0);

        Assert.NotEmpty(game.Design.SpecialAbilities);
    }

    /// <summary>
    /// A level's roll is the baseclass's own dice for that level.
    /// </summary>
    /// <remarks>
    /// With every die showing its maximum the answer is arithmetic, so this checks the dice were
    /// read from the right row rather than that a number came back.
    /// </remarks>
    [Fact]
    public void One_level_rolls_that_levels_dice()
    {
        if (Load() is not { } game
            || game.Design.Baseclasses?.Values.FirstOrDefault(b => b.HitDice.Count > 0)
               is not { } baseclass)
        {
            return;
        }

        var host = new GameScriptHost(game);
        string id = game.Design.Baseclasses.First(b => b.Value == baseclass).Key;

        var dice = baseclass.HitDice[0];
        Assert.Equal((dice.Nbr * dice.Sides) + dice.Bonus,
                     host.RollHitPointDice(id, 1, 1));
    }

    /// <summary>
    /// A range sums a roll per level, not one roll repeated.
    /// </summary>
    /// <remarks>
    /// <b>A baseclass's hit dice change as it advances</b>, so levels 1–3 is three different rows
    /// added together. An implementation that rolled level 1 three times would agree wherever the
    /// rows happen to match and diverge where they do not.
    /// </remarks>
    [Fact]
    public void A_range_sums_a_roll_for_each_level()
    {
        if (Load() is not { } game
            || game.Design.Baseclasses?.Values.FirstOrDefault(b => b.HitDice.Count >= 3)
               is not { } baseclass)
        {
            return;
        }

        var host = new GameScriptHost(game);
        string id = game.Design.Baseclasses.First(b => b.Value == baseclass).Key;

        int expected = 0;
        for (int level = 1; level <= 3; level++)
        {
            var dice = baseclass.HitDice[level - 1];
            expected += (dice.Nbr * dice.Sides) + dice.Bonus;
        }

        Assert.Equal(expected, host.RollHitPointDice(id, 1, 3));
    }

    /// <summary>
    /// The level range is clamped properly, where the reference clamps the wrong variable.
    /// </summary>
    /// <remarks>
    /// <b>A divergence.</b> The reference writes <c>if (low &gt; HIGHEST) high = HIGHEST;</c> and
    /// <c>if (high &lt; 1) low = 1;</c> (<c>class.cpp:5579</c>) — assigning the wrong variable in
    /// both. So <c>low</c> is never clamped from above and <c>high</c> never from below, and both
    /// mistakes happen to leave an empty range and therefore zero, which is why nothing caught
    /// them. Asking for levels 1 to 999 gets 40 levels here rather than nothing.
    /// </remarks>
    [Fact]
    public void The_level_range_is_clamped_to_the_real_bounds()
    {
        if (Load() is not { } game
            || game.Design.Baseclasses?.Values.FirstOrDefault(b => b.HitDice.Count > 0)
               is not { } baseclass)
        {
            return;
        }

        var host = new GameScriptHost(game);
        string id = game.Design.Baseclasses.First(b => b.Value == baseclass).Key;

        // Levels 1..999 is levels 1..40, which is more than levels 1..3 and not zero.
        int capped = host.RollHitPointDice(id, 1, 999);
        Assert.True(capped > host.RollHitPointDice(id, 1, 3));
        Assert.Equal(host.RollHitPointDice(id, 1, 40), capped);

        // A low below one starts at one rather than counting backwards.
        Assert.Equal(host.RollHitPointDice(id, 1, 2), host.RollHitPointDice(id, -5, 2));

        // An inverted range rolls nothing, which is the one case the reference also gets right.
        Assert.Equal(0, host.RollHitPointDice(id, 5, 2));
    }

    /// <summary>A baseclass the design does not have rolls nothing.</summary>
    [Fact]
    public void An_unknown_baseclass_rolls_nothing()
    {
        if (Load() is not { } game)
        {
            return;
        }

        Assert.Equal(0, new GameScriptHost(game).RollHitPointDice("NoSuchBaseclass", 1, 5));
    }

    /// <summary>Each way of failing has its own code, so a script can tell them apart.</summary>
    [Fact]
    public void Each_lookup_failure_has_its_own_code()
    {
        if (Load() is not { } game || game.Design.SpecialAbilities.Count == 0)
        {
            return;
        }

        var host = new GameScriptHost(game);
        var ability = game.Design.SpecialAbilities[0];

        Assert.Equal(GpdlIntegerTable.NoSuchAbility,
                     host.IntegerTable("NoSuchAbility", "t", 0, GpdlTableQuery.Index));

        Assert.Equal(GpdlIntegerTable.NoSuchTable,
                     host.IntegerTable(ability.Name, "NoSuchTable", 0, GpdlTableQuery.Index));

        // An entry that exists but is not a table is a different failure again.
        var notATable = game.Design.SpecialAbilities
            .SelectMany(a => a.Entries.Select(e => (a.Name, e)))
            .FirstOrDefault(x => x.e.Kind != SpecialAbilityEntryKind.IntegerTable);

        if (notATable.Name is not null)
        {
            Assert.Equal(GpdlIntegerTable.NotATable,
                         host.IntegerTable(notATable.Name, notATable.e.Name, 0,
                                           GpdlTableQuery.Index));
        }
    }

    /// <summary>
    /// A real table's numbers are read back in order, negatives included.
    /// </summary>
    /// <remarks>
    /// <b>Both corpus designs carry nineteen integer tables</b>, and
    /// <c>AbilityAdjustments[DexInit]</c> is one of them: <c>3, 2, 1, 0 …</c> running down to
    /// <c>-3</c>. Checking against known values is what separates "a number came back" from "the
    /// right row was read", and the negatives are what prove the parser is not just reading
    /// digits.
    /// </remarks>
    [Fact]
    public void A_real_tables_numbers_are_read_in_order()
    {
        if (Load() is not { } game)
        {
            return;
        }

        var host = new GameScriptHost(game);

        var tables = game.Design.SpecialAbilities
            .SelectMany(a => a.Entries.Select(e => (a.Name, Entry: e)))
            .Where(x => x.Entry.Kind == SpecialAbilityEntryKind.IntegerTable)
            .ToList();

        // The premise: the corpus really does carry tables to read.
        Assert.NotEmpty(tables);

        var dex = tables.FirstOrDefault(x => x.Entry.Name == "DexInit");

        if (dex.Name is null)
        {
            return;
        }

        // The first three rows, in order.
        Assert.Equal(3, host.IntegerTable(dex.Name, "DexInit", 0, GpdlTableQuery.Index));
        Assert.Equal(2, host.IntegerTable(dex.Name, "DexInit", 1, GpdlTableQuery.Index));
        Assert.Equal(1, host.IntegerTable(dex.Name, "DexInit", 2, GpdlTableQuery.Index));

        // And a negative further down -- so the parser is reading signs, not just digits.
        var values = dex.Entry.Value.Split('\n')
                        .TakeWhile(l => l.Length > 0)
                        .Select(int.Parse)
                        .ToList();

        Assert.Contains(values, v => v < 0);

        int negative = values.FindIndex(v => v < 0);
        Assert.Equal(values[negative],
                     host.IntegerTable(dex.Name, "DexInit", negative, GpdlTableQuery.Index));

        // Equal answers the position of a value, which is the inverse of the above.
        Assert.Equal(0, host.IntegerTable(dex.Name, "DexInit", 3, GpdlTableQuery.Equal));

        // And an unrecognised query is refused whatever the table holds.
        Assert.Equal(GpdlIntegerTable.NoSuchQuery,
                     host.IntegerTable(dex.Name, "DexInit", 0, GpdlTableQuery.Unknown));
    }
}
