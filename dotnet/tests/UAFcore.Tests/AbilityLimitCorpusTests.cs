using UAF.Media.Sdl;
using UAF.Rules;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Checks the ability limits a real design produces, class by class.
/// </summary>
/// <remarks>
/// The combination rule takes the greatest minimum and the least maximum across a class's
/// baseclasses, out of tables that are easy to read the wrong way round. Against a design it is
/// either coherent or it is not.
/// </remarks>
public class AbilityLimitCorpusTests
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

    private static AbilityLimits Limits(LoadedDesign design, string classId, string ability)
    {
        var record = design.Classes?.GetValueOrDefault(classId);
        if (record is null)
        {
            return AbilityLimits.UnknownClass;
        }

        return AbilityLimits.Combine(record.Baseclasses.Select(id =>
        {
            var baseclass = design.Baseclasses?.GetValueOrDefault(id);
            var requirement = baseclass?.AbilityRequirements.FirstOrDefault(
                r => string.Equals(r.AbilityId, ability, StringComparison.OrdinalIgnoreCase));

            return requirement is null
                ? (AbilityLimits?)null
                : new AbilityLimits(requirement.Min, requirement.MinMod,
                                    requirement.Max, requirement.MaxMod);
        }));
    }

    [Fact]
    public void Every_class_allows_a_range_a_character_can_actually_sit_in()
    {
        using var design = Open();
        if (design?.Classes is not { Count: > 0 } classes)
        {
            return;
        }

        foreach (var (classId, _) in classes)
        {
            foreach (string ability in RolledCharacter.AbilityNames)
            {
                var limits = Limits(design, classId, ability);

                Assert.True(limits.Min <= limits.Max,
                            $"{classId}/{ability} allows {limits.Min}..{limits.Max}");
                Assert.InRange(limits.Max, 1, 255);
            }
        }
    }

    [Fact]
    public void A_multi_baseclass_class_is_at_least_as_tight_as_each_half()
    {
        using var design = Open();
        if (design?.Classes is not { Count: > 0 } classes)
        {
            return;
        }

        var multi = classes.Values.FirstOrDefault(c => c.Baseclasses.Count > 1);
        if (multi is null)
        {
            return;
        }

        foreach (string ability in RolledCharacter.AbilityNames)
        {
            var combined = Limits(design, multi.Name, ability);

            foreach (string baseclassId in multi.Baseclasses)
            {
                var single = AbilityLimits.Combine([
                    design.Baseclasses?.GetValueOrDefault(baseclassId)?.AbilityRequirements
                        .FirstOrDefault(r => string.Equals(r.AbilityId, ability,
                                                           StringComparison.OrdinalIgnoreCase))
                        is { } requirement
                        ? new AbilityLimits(requirement.Min, requirement.MinMod,
                                            requirement.Max, requirement.MaxMod)
                        : null,
                ]);

                Assert.True(combined.Min >= single.Min);
                Assert.True(combined.Max <= single.Max);
            }
        }
    }

    [Fact]
    public void The_design_asks_for_something_rather_than_defaulting_everywhere()
    {
        // Guards against a reader that quietly returns no requirements: every limit would then be
        // the 3-18 default and the two tests above would pass on nothing.
        using var design = Open();
        if (design?.Baseclasses is not { Count: > 0 } baseclasses)
        {
            return;
        }

        Assert.Contains(baseclasses.Values, b => b.AbilityRequirements.Count > 0);
    }
}
