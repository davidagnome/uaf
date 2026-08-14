using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// <c>$IS_AFFECTED_BY_SPELL</c> and <c>$IS_AFFECTED_BY_SPELL_ATTR</c>.
/// </summary>
/// <remarks>
/// They read alike and ask different questions: the first matches an effect's <i>source spell</i>,
/// the second looks for an attribute on that spell — and, failing that, on the character itself.
/// </remarks>
public class GpdlIsAffectedTests
{
    private sealed class AfflictedHost : GpdlUnhostedEnvironment
    {
        public List<(string Actor, string Name)> SpellAsked { get; } = [];

        public List<(string Actor, string Name)> AttributeAsked { get; } = [];

        public override bool IsAffectedBySpell(string actor, string spellId)
        {
            SpellAsked.Add((actor, spellId));
            return spellId == "Bless";
        }

        public override bool IsAffectedBySpellAttribute(string actor, string attribute)
        {
            AttributeAsked.Add((actor, attribute));
            return attribute == "HOLY";
        }
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

    /// <summary>The spell form asks about a spell, by name.</summary>
    [Fact]
    public void The_spell_form_matches_a_spell_name()
    {
        var host = new AfflictedHost();

        Assert.Equal("1",
            Run("""$RETURN $IS_AFFECTED_BY_SPELL($MOST_DAMAGED_ENEMY(), "Bless");""", host));
        Assert.Equal(string.Empty,
            Run("""$RETURN $IS_AFFECTED_BY_SPELL($MOST_DAMAGED_ENEMY(), "Curse");""", host));

        // The name reaches the host as the name, not as the actor.
        Assert.Equal(["Bless", "Curse"], host.SpellAsked.Select(a => a.Name));
        Assert.Empty(host.AttributeAsked);
    }

    /// <summary>The attribute form is a different question, not the same one renamed.</summary>
    [Fact]
    public void The_attribute_form_is_its_own_question()
    {
        var host = new AfflictedHost();

        Assert.Equal("1",
            Run("""$RETURN $IS_AFFECTED_BY_SPELL_ATTR($MOST_DAMAGED_ENEMY(), "HOLY");""", host));
        Assert.Equal(string.Empty,
            Run("""$RETURN $IS_AFFECTED_BY_SPELL_ATTR($MOST_DAMAGED_ENEMY(), "Bless");""", host));

        Assert.Empty(host.SpellAsked);
        Assert.Equal(["HOLY", "Bless"], host.AttributeAsked.Select(a => a.Name));
    }

    /// <summary>An unhosted environment is under nothing.</summary>
    [Fact]
    public void An_unhosted_environment_is_affected_by_nothing()
    {
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal(string.Empty,
            Run("""$RETURN $IS_AFFECTED_BY_SPELL($MOST_DAMAGED_ENEMY(), "Bless");""", host));
        Assert.Equal(string.Empty,
            Run("""$RETURN $IS_AFFECTED_BY_SPELL_ATTR($MOST_DAMAGED_ENEMY(), "HOLY");""", host));
    }
}
