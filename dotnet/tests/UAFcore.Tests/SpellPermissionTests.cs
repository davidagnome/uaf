using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the two gates on casting and memorising.</summary>
/// <remarks>
/// <b>The point of these is the default.</b> Both start from permission where
/// <see cref="ClassChange"/> starts from refusal, so a design with no scripts casts and memorises
/// freely and can never change class.
/// </remarks>
public class SpellPermissionTests
{
    private static Character Who(byte type = 1) =>
        new(NewCharacter.Blank with { Name = "Aramil", Type = type }, MoneyRules.Default);

    [Fact]
    public void With_no_scripts_a_character_may_cast_and_memorise()
    {
        Assert.True(SpellPermissions.CanCast(Who()));
        Assert.True(SpellPermissions.CanMemorize(Who(), SpellPermissions.ForTheMagicMenu));
        Assert.True(SpellPermissions.CanMemorize(Who(), SpellPermissions.WhileResting));
    }

    [Fact]
    public void The_default_is_the_opposite_of_changing_class()
    {
        // CanChangeToClass starts from an empty answer only a script can fill in; these two start
        // from "YYYYY" and a script can only take it away.
        Assert.False(ClassChange.NoScripts("Fighter", "Cleric"));
        Assert.True(SpellPermissions.CanCast(Who()));
    }

    [Fact]
    public void A_script_can_take_the_permission_away()
    {
        Assert.False(SpellPermissions.CanCast(Who(), deniedByScript: true));
        Assert.False(SpellPermissions.CanMemorize(Who(), SpellPermissions.WhileResting,
                                                  deniedByScript: true));
    }

    [Fact]
    public void A_monster_is_refused_before_any_script_runs()
    {
        var monster = Who(ClassChange.MonsterType);

        Assert.False(SpellPermissions.CanCast(monster));
        Assert.False(SpellPermissions.CanMemorize(monster, SpellPermissions.ForTheMagicMenu));
    }

    [Fact]
    public void An_npc_is_not_a_monster()
    {
        Assert.True(SpellPermissions.CanCast(Who((byte)CombatantKind.Npc)));
    }

    [Fact]
    public void A_record_saved_in_a_party_is_still_recognised_as_a_monster()
    {
        var monster = Who((byte)(ClassChange.MonsterType | EventNpc.InPartyFlag));

        Assert.False(SpellPermissions.CanCast(monster));
    }

    [Fact]
    public void The_two_circumstances_the_engine_asks_about_are_nought_and_one()
    {
        Assert.Equal(0, SpellPermissions.ForTheMagicMenu);
        Assert.Equal(1, SpellPermissions.WhileResting);
    }
}
