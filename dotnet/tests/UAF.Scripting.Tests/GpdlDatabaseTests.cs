using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>Covers what a script can read out of the design's databases.</summary>
public class GpdlDatabaseTests
{
    private static string Run(string body, GpdlUnhostedEnvironment host)
    {
        var compiler = new GpdlCompiler();
        string source = "$PUBLIC $FUNC f() { " + body + " } f;";
        Assert.True(compiler.Compile(source) == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);
        string value = vm.Execute("f");
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        return value;
    }

    private sealed class Database : GpdlUnhostedEnvironment
    {
        /// <summary>Rolls a fresh number each call, as the reference's height dice do.</summary>
        public int Rolls { get; private set; }

        public override int RaceMeasurement(string actor, bool weight)
        {
            Rolls++;
            return (weight ? 1000 : 0) + Rolls;
        }

        public override int BaseclassProgression(string baseclassId, int value,
                                                 bool wantExperience) =>
            (wantExperience ? 10000 : 0) + value;

        public override string ClassBaseclasses(string classId) =>
            classId == "fighterMage" ? "$fighter$magicuser" : string.Empty;

        /// <summary>An actor producer: the race reads take an actor, not an id string.</summary>
        public override string MostDamaged(GpdlDamageQuery query) => "hero";

        /// <inheritdoc cref="MostDamaged"/>
        public override bool InCombat => true;
    }

    /// <summary>An actor, since an actor-typed parameter refuses a literal.</summary>
    private const string AnActor = "$MOST_DAMAGED_ENEMY()";

    // ---- item fields -------------------------------------------------------------------------

    [Theory]
    [InlineData("$DAT_Item_CommonName", GpdlItemField.CommonName)]
    [InlineData("$DAT_Item_IDName", GpdlItemField.IdName)]
    [InlineData("$DAT_Item_Priority", GpdlItemField.Priority)]
    [InlineData("$DAT_Item_MaxRange", GpdlItemField.MaxRange)]
    [InlineData("$DAT_Item_MediumRange", GpdlItemField.MediumRange)]
    [InlineData("$DAT_Item_ShortRange", GpdlItemField.ShortRange)]
    [InlineData("$DAT_Item_DamageSmall", GpdlItemField.DamageSmall)]
    [InlineData("$DAT_Item_DamageLarge", GpdlItemField.DamageLarge)]
    [InlineData("$DAT_Item_AttackBonus", GpdlItemField.AttackBonus)]
    public void Each_item_field_reads_its_own(string call, GpdlItemField field)
    {
        var host = new Database();
        host.ItemFields[("sword", field)] = "wanted";

        Assert.Equal("wanted", Run($"""$RETURN {call}("sword");""", host));
    }

    [Fact]
    public void An_item_the_design_does_not_define_answers_the_empty_string()
    {
        // The reference clears its scratch string before the lookup -- the guard GET_CHARACTER_SA
        // forgets -- so this family is safe where that one is not.
        Assert.Equal("", Run("""$RETURN $DAT_Item_CommonName("ghost");""", new Database()));
    }

    [Fact]
    public void Damage_arrives_as_three_numbers_in_one_delimited_string()
    {
        var host = new Database();
        host.ItemFields[("sword", GpdlItemField.DamageSmall)] = "$1$8$2";

        Assert.Equal("$1$8$2", Run("""$RETURN $DAT_Item_DamageSmall("sword");""", host));
    }

    // ---- race measurements -------------------------------------------------------------------

    [Fact]
    public void Height_and_weight_are_two_different_reads()
    {
        var host = new Database();

        Assert.Equal("1", Run($"""$RETURN $DAT_Race_Height({AnActor});""", host));
        Assert.Equal("1002", Run($"""$RETURN $DAT_Race_Weight({AnActor});""", host));
    }

    [Fact]
    public void A_measurement_is_rolled_rather_than_looked_up()
    {
        // The race's height is a dice field, so two calls about the same character give two
        // answers -- a script wanting a stable number has to keep the first.
        var host = new Database();

        string first = Run($"""$RETURN $DAT_Race_Height({AnActor});""", host);
        string second = Run($"""$RETURN $DAT_Race_Height({AnActor});""", host);

        Assert.NotEqual(first, second);
        Assert.Equal(2, host.Rolls);
    }

    // ---- baseclass progression ---------------------------------------------------------------

    [Fact]
    public void Level_and_experience_are_the_two_directions_of_one_table()
    {
        var host = new Database();

        Assert.Equal("5", Run("""$RETURN $DAT_Baseclass_Level("fighter", "5");""", host));
        Assert.Equal("10005",
                     Run("""$RETURN $DAT_Baseclass_Experience("fighter", "5");""", host));
    }

    // ---- a class's baseclasses ---------------------------------------------------------------

    [Fact]
    public void The_delimiter_leads_rather_than_separates()
    {
        // Each name is appended after a $, so one baseclass is "$fighter" and never a bare name.
        var host = new Database();

        Assert.Equal("$fighter$magicuser",
                     Run("""$RETURN $DAT_Class_Baseclasses("fighterMage");""", host));
    }

    [Fact]
    public void A_class_the_design_does_not_define_answers_the_empty_string()
    {
        Assert.Equal("", Run("""$RETURN $DAT_Class_Baseclasses("ghost");""", new Database()));
    }

    // ---- walking the party -------------------------------------------------------------------

    [Fact]
    public void For_each_party_member_takes_the_ability_then_the_script()
    {
        var host = new Database();

        Run("""$ForEachPartyMember("Blessing", "Tick");""", host);

        Assert.Equal([("Blessing", "Tick")], host.PartyWalks);
    }

    [Fact]
    public void For_each_party_member_yields_what_the_walk_left()
    {
        // Only the last run's answer survives -- and since the reference counts down, that is
        // party member zero's.
        Assert.Equal("", Run("""$RETURN $ForEachPartyMember("Blessing", "Tick");""",
                             new Database()));
    }
}
