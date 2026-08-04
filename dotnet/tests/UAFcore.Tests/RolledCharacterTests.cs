using UAF.Media.Sdl;
using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Rolls a new character against a real design — the seam where the generator's rules finally meet
/// the design's own tables.
/// </summary>
/// <remarks>
/// The design is gitignored, so these return early without <c>reference/</c>.
/// </remarks>
public class RolledCharacterTests
{
    private static LoadedDesign? Open()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string root = Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");
        return Directory.Exists(root)
            ? LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer())
            : null;
    }

    /// <summary>A character part-way through the wizard: race, gender, class and a name.</summary>
    private static CharacterCreation Made(LoadedDesign design)
    {
        var made = new CharacterCreation();

        made.Choose(design.Races?.Keys.FirstOrDefault() ?? "Human");
        made.Choose(nameof(Gender.Male));
        made.Choose(design.Classes?.Keys.FirstOrDefault() ?? "Fighter");
        made.Choose("4");
        made.SkipStats();
        made.Name("Aramil");

        return made;
    }

    /// <summary>Every die shows its top face.</summary>
    private static int Max(int count, int sides) => count * Math.Max(sides, 1);

    [Fact]
    public void A_rolled_character_has_six_ability_scores()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var rolled = RolledCharacter.Roll(Made(design), design, Max, seed: 42);
        var a = rolled.Abilities;

        // Every ability in this design reads "3|<2d6+6+(Race_Something*1)>|18" -- bounds around
        // an expression that tests the character's race. Both had to be understood before a
        // score could come out as anything but zero.
        foreach (int score in new[] { a.Strength, a.Intelligence, a.Wisdom,
                                      a.Dexterity, a.Constitution, a.Charisma })
        {
            Assert.True(score > 0, "an ability came out at zero; the dice were not consulted");
        }
    }

    [Fact]
    public void A_rolled_character_has_hit_points()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var rolled = RolledCharacter.Roll(Made(design), design, Max, seed: 42);

        Assert.True(rolled.MaxHitPoints > 0);
    }

    [Fact]
    public void The_same_seed_gives_the_same_hit_points()
    {
        // The private generator's whole purpose: re-rolling abilities must not re-roll the dice.
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var first = RolledCharacter.Roll(Made(design), design, Max, seed: 7);
        var again = RolledCharacter.Roll(Made(design), design, Max, seed: 7);

        Assert.Equal(first.MaxHitPoints, again.MaxHitPoints);
    }

    [Fact]
    public void A_birthday_lands_in_the_year()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var rolled = RolledCharacter.Roll(Made(design), design, (c, s) => c * s / 2, seed: 1);

        Assert.InRange(rolled.Birthday, 1, NewCharacter.DaysInYear);
    }

    [Fact]
    public void The_start_age_floor_is_applied()
    {
        // A race whose age dice roll low still produces a character at least this old.
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var rolled = RolledCharacter.Roll(Made(design), design, (_, _) => 1, seed: 1,
                                          startAge: 25);

        // ...unless the race's maximum age is lower, because the two clamps never look at each
        // other -- which is the finding, so this asserts the floor OR the cap, not both.
        Assert.True(rolled.Age >= 25 || rolled.Age == rolled.MaxAge,
                    $"age {rolled.Age} is neither the floor nor the race's maximum "
                    + $"{rolled.MaxAge}");
    }

    [Fact]
    public void The_default_start_age_is_seventeen()
    {
        // The clamp to at least 1 only runs when the config token is present; the initialiser is
        // what stands otherwise.
        Assert.Equal(17, RolledCharacter.DefaultStartAge);
    }

    [Fact]
    public void An_unknown_race_and_class_roll_nothing_rather_than_throwing()
    {
        using var design = Open();
        if (design is null)
        {
            return;
        }

        var made = new CharacterCreation();
        made.Choose("no-such-race");
        made.Choose(nameof(Gender.Female));
        made.Choose("no-such-class");
        made.Choose("0");

        var rolled = RolledCharacter.Roll(made, design, Max, seed: 3);

        Assert.Equal(0, rolled.Age);
        Assert.Equal(1, rolled.MaxHitPoints);       // the floor, from no baseclasses
    }

    [Fact]
    public void The_six_names_are_the_ones_the_generator_asks_for()
    {
        Assert.Equal(["Strength", "Intelligence", "Wisdom", "Dexterity", "Constitution",
                      "Charisma"],
                     RolledCharacter.AbilityNames);
    }
}
