using UAF.Import.Frua;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// The two composite events: <c>SmallTown</c>'s generator and <c>Encounter</c>'s buttons.
/// </summary>
public class FruaSmallTownTests
{
    private static string? Heirs()
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

        string design = Path.Combine(dir.FullName, "reference", "Unlimited Adventures -ENG",
                                     "DESIGNS", "UA", "HEIRS.DSN");
        return Directory.Exists(design) ? design : null;
    }

    private static FruaEvent Make(byte type, params (int Offset, byte Value)[] payload)
    {
        var record = new byte[FruaEvent.Length];
        record[0] = type;

        foreach (var (offset, value) in payload)
        {
            record[4 + (offset - 5)] = value;
        }

        return FruaEvent.Read(record);
    }

    // ---- small town ---------------------------------------------------------------------------

    [Fact]
    public void The_flag_byte_names_which_services_the_town_has()
    {
        var t = FruaSmallTownEvent.Read(Make(22, (8, 1 | 4 | 32)));

        Assert.Equal(FruaTownServices.Temple | FruaTownServices.Shop | FruaTownServices.Vault,
                     t.Services);
        Assert.False(t.Services.HasFlag(FruaTownServices.Tavern));
    }

    /// <summary>The picture's high bit shares the services byte and is not a service.</summary>
    [Fact]
    public void The_large_picture_bit_is_not_a_service()
    {
        var t = FruaSmallTownEvent.Read(Make(22, (8, 128 | 1)));

        Assert.True(t.PictureIsLarge);
        Assert.Equal(FruaTownServices.Temple, t.Services);
    }

    /// <summary>The temple's spell level is the top three bits, in steps of 32.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(32, 1)]
    [InlineData(96, 3)]
    [InlineData(224, 7)]
    public void The_temple_spell_level_is_the_top_three_bits(byte stored, int expected)
    {
        Assert.Equal(expected, FruaSmallTownEvent.Read(Make(22, (10, stored))).TempleMaxLevel);
    }

    /// <summary>
    /// The generated shop masks its cost to five bits, where every other cost byte is whole.
    /// </summary>
    [Fact]
    public void The_generated_shops_cost_is_masked_to_five_bits()
    {
        // 224 | 12 -> the top bits are the temple's spell level, the low five the shop's cost.
        var t = FruaSmallTownEvent.Read(Make(22, (10, 224 | 12)));

        Assert.Equal(7, t.TempleMaxLevel);
        Assert.Equal(FruaCostFactor.Mult2, t.ShopCost);
    }

    /// <summary>The generated shop reads two stock groups, not the standalone shop's four.</summary>
    [Fact]
    public void The_generated_shop_reads_two_stock_groups()
    {
        var t = FruaSmallTownEvent.Read(Make(22,
            (8, 4),
            (15, 16), (17, 1),      // group 1 -> item 1
            (18, 16), (20, 2)));    // group 2 -> item 2

        Assert.Equal([(byte)1, (byte)2], t.ShopItems);
    }

    /// <summary>The children's prompts are hard-coded, so a writer must emit them verbatim.</summary>
    [Fact]
    public void The_child_prompts_are_the_references_own_literals()
    {
        Assert.Equal("WELCOME TO THE TEMPLE", FruaSmallTownEvent.TempleText);
        Assert.Equal("HOW MAY WE AID YOU?", FruaSmallTownEvent.TempleText2);
        Assert.Equal("WELCOME TO THE TRAINING HALL", FruaSmallTownEvent.TrainingHallText);
        Assert.Equal("WELCOME TO THE SHOP", FruaSmallTownEvent.ShopText);
        Assert.Equal("WELCOME TO THE INN", FruaSmallTownEvent.InnText);
        Assert.Equal("WELCOME TO THE VAULT", FruaSmallTownEvent.VaultText);
    }

    [Fact]
    public void The_training_cost_is_a_factor_on_one_thousand()
    {
        var t = FruaSmallTownEvent.Read(Make(22, (13, 12)));

        Assert.Equal(FruaCostFactor.Mult2, t.TrainingCost);
        Assert.Equal(2000, t.TrainingCostValue);
    }

    // ---- encounter ----------------------------------------------------------------------------

    [Fact]
    public void A_button_is_offered_only_when_its_presence_bit_is_set()
    {
        var e = FruaEncounterEvent.Read(Make(15, (10, 8 | 4), (12, 8 | 3)));

        Assert.True(e.Buttons[0].Present);
        Assert.False(e.Buttons[1].Present);
        Assert.True(e.Buttons[2].Present);
        Assert.Equal(2, e.ButtonCount);
        Assert.Equal(FruaEncounterResult.Talk, e.Buttons[0].Result);
        Assert.Equal(FruaEncounterResult.CombatNoSurprise, e.Buttons[2].Result);
    }

    /// <summary>
    /// Each button configures itself, correcting the reference's mis-targeted writes.
    /// </summary>
    /// <remarks>
    /// In the reference, button 2's block sets button 0's <c>onlyUpClose</c> and button 4's sets
    /// buttons 2 and 0 — so Talk never configured itself. See
    /// <see cref="FruaEncounterEvent.ButtonFlagsFixed"/>.
    /// </remarks>
    [Fact]
    public void Each_button_configures_its_own_range_flags()
    {
        var two = FruaEncounterEvent.Read(Make(15, (12, 8 | 16)));

        Assert.True(two.Buttons[2].AllowedUpClose);
        Assert.True(two.Buttons[2].OnlyUpClose);
        Assert.False(two.Buttons[0].OnlyUpClose);   // no longer clobbered

        var four = FruaEncounterEvent.Read(Make(15, (14, 8 | 16)));

        Assert.True(four.Buttons[4].AllowedUpClose);
        Assert.True(four.Buttons[4].OnlyUpClose);
        Assert.False(four.Buttons[2].AllowedUpClose);
        Assert.False(four.Buttons[0].OnlyUpClose);
    }

    /// <summary>Buttons 1 and 3 have their range handling commented out entirely.</summary>
    [Fact]
    public void Buttons_one_and_three_never_set_a_range_flag()
    {
        var e = FruaEncounterEvent.Read(Make(15, (11, 8 | 16), (13, 8 | 16)));

        foreach (int i in new[] { 1, 3 })
        {
            Assert.True(e.Buttons[i].Present);
            Assert.False(e.Buttons[i].AllowedUpClose);
            Assert.False(e.Buttons[i].OnlyUpClose);
        }
    }

    [Fact]
    public void An_unmapped_result_is_not_guessed_at()
    {
        var e = FruaEncounterEvent.Read(Make(15, (10, 8 | 7)));

        Assert.Equal(FruaEncounterResult.Unmapped, e.Buttons[0].Result);
    }

    // ---- the real DOS levels -------------------------------------------------------------------

    [Fact]
    public void The_shipped_small_towns_and_encounter_decode()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        int towns = 0;
        int encounters = 0;

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            foreach (var e in level.Events)
            {
                if (e.Type == FruaEventType.SmallTown)
                {
                    var t = FruaSmallTownEvent.Read(e);
                    Assert.InRange(t.TempleMaxLevel, 0, 7);
                    Assert.True(Enum.IsDefined(t.ShopCost));
                    towns++;
                }
                else if (e.Type == FruaEventType.Encounter)
                {
                    var x = FruaEncounterEvent.Read(e);
                    Assert.Equal(5, x.Buttons.Count);
                    encounters++;
                }
            }
        }

        Assert.Equal(3, towns);
        Assert.Equal(1, encounters);
    }
}
