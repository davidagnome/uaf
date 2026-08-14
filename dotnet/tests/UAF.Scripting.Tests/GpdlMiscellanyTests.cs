using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The last six GPDL calls that needed no new subsystem: <c>$GET_CONFIG</c>,
/// <c>$KNOW_SPELL</c>, <c>$REMOVE_SPELL_EFFECT</c>, <c>$DUMP_CHARACTER_SAS</c>,
/// <c>$SMALL_PICTURE</c> and <c>$SLEEP</c>.
/// </summary>
/// <remarks>
/// <b>Three of them take an <c>ACTOR</c> and one takes a plain string, and only the table says
/// which.</b> <c>$KNOW_SPELL</c>, <c>$DUMP_CHARACTER_SAS</c> and <c>$REMOVE_SPELL_EFFECT</c>
/// declare their first parameter <c>ACTOR</c>, so it has to be a system-function call;
/// <c>$GET_CONFIG</c> takes a string. Passing the wrong kind is a compile error, not a wrong
/// answer — which is the one mercy in this distinction.
/// </remarks>
public class GpdlMiscellanyTests
{
    private sealed class MiscHost : GpdlUnhostedEnvironment
    {
        public override string ConfigValue(string token) =>
            token == "SCREEN" ? "640x480" : string.Empty;

        public override bool KnowSpell(string actor, string spellId, bool know)
        {
            base.KnowSpell(actor, spellId, know);
            return spellId == "Magic Missile";
        }

        public override string RemoveSpellEffect(string actor, string scriptName) =>
            scriptName == "onHit" ? "2" : string.Empty;
    }

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

    /// <summary>A config token comes back by name, and an unknown one is empty.</summary>
    [Fact]
    public void A_config_token_is_read_by_name()
    {
        var host = new MiscHost();

        Assert.Equal("640x480", Run("""$RETURN $GET_CONFIG("SCREEN");""", host));
        Assert.Equal(string.Empty, Run("""$RETURN $GET_CONFIG("NOTHING");""", host));
    }

    /// <summary>
    /// Know-spell carries all three arguments, and the flag is the last of them.
    /// </summary>
    /// <remarks>
    /// The know flag pops first, so a crossed pair would teach the actor a spell named by the
    /// flag — which is why each field is checked rather than only the result.
    /// </remarks>
    [Fact]
    public void Know_spell_passes_the_actor_spell_and_flag()
    {
        var host = new MiscHost();

        Assert.Equal("1",
            Run("""$RETURN $KNOW_SPELL($MOST_DAMAGED_ENEMY(), "Magic Missile", 1);""", host));

        var (actor, spell, know) = Assert.Single(host.SpellsTaught);
        Assert.Equal(host.NullActor, actor);
        Assert.Equal("Magic Missile", spell);
        Assert.True(know);
    }

    /// <summary>Zero unteaches, and an unknown spell is refused either way.</summary>
    [Fact]
    public void Zero_unteaches_and_an_unknown_spell_is_refused()
    {
        var host = new MiscHost();

        Run("""$KNOW_SPELL($MOST_DAMAGED_ENEMY(), "Magic Missile", 0);""", host);
        Assert.False(host.SpellsTaught[^1].Know);

        // A spell the design has no record of answers false however the flag is set.
        Assert.Equal(string.Empty,
            Run("""$RETURN $KNOW_SPELL($MOST_DAMAGED_ENEMY(), "No Such Spell", 1);""", host));
    }

    /// <summary>Remove-spell-effect answers with whatever the host reports.</summary>
    /// <remarks>
    /// <b>A string, not a boolean.</b> The reference pushes the removal's own result — so a count
    /// reaches the script where the other removal calls push true or false.
    /// </remarks>
    [Fact]
    public void Remove_spell_effect_answers_with_the_hosts_result()
    {
        var host = new MiscHost();

        Assert.Equal("2",
            Run("""$RETURN $REMOVE_SPELL_EFFECT($MOST_DAMAGED_ENEMY(), "onHit");""", host));
        Assert.Equal(string.Empty,
            Run("""$RETURN $REMOVE_SPELL_EFFECT($MOST_DAMAGED_ENEMY(), "other");""", host));
    }

    /// <summary>The dump is a diagnostic that still leaves a result behind.</summary>
    [Fact]
    public void The_special_ability_dump_writes_to_the_log()
    {
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal(string.Empty, Run("""$RETURN $DUMP_CHARACTER_SAS($MOST_DAMAGED_ENEMY());""", host));
        Assert.Contains(host.DebugLog,
                        line => line.Contains("DUMP_CHARACTER_SAS", StringComparison.Ordinal));
    }

    /// <summary>
    /// Small-picture yields the filename it was given, whether or not anything shows it.
    /// </summary>
    /// <remarks>
    /// The reference pushes the name and <i>then</i> checks for a running event, so the result is
    /// the same when there is none — which is the state this port is always in.
    /// </remarks>
    [Fact]
    public void Small_picture_yields_its_filename()
    {
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal("dragon.png", Run("""$RETURN $SMALL_PICTURE("dragon.png");""", host));
        Assert.Equal("dragon.png", host.Picture);
    }

    /// <summary>Sleep reaches the host with its duration and yields nothing.</summary>
    [Fact]
    public void Sleep_passes_its_duration()
    {
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal(string.Empty, Run("$RETURN $SLEEP(250);", host));
        Assert.Equal([250], host.Sleeps);
    }
}
