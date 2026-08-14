using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// <c>$GET_CHAR_TYPE</c>, the race pair, and <c>$GET_VAULT_MONEYAVAILABLE</c>.
/// </summary>
/// <remarks>
/// <b>These take a plain string, not an <c>ACTOR</c>.</b> Their table rows declare every parameter
/// <c>STRING</c>, so an actor-returning call like <c>$MOST_DAMAGED_ENEMY()</c> is rejected at
/// compile time — the reference reaches them through <c>m_popCharacter</c>, which takes an
/// identifier, rather than through the actor context that <c>$GET_ISMAMMAL</c> and its siblings
/// use.
/// </remarks>
public class GpdlCharIdentityTests
{
    private sealed class IdentityHost : GpdlUnhostedEnvironment
    {
        public string Type { get; set; } = IGpdlHost.PlayerCharacterType;

        public string Race { get; set; } = "Elf";

        public List<(string Actor, string Race)> RaceWrites { get; } = [];

        public override string CharacterType(string actor) => Type;

        public override string CharacterRace(string actor) => Race;

        public override bool SetCharacterRace(string actor, string race)
        {
            RaceWrites.Add((actor, race));

            // A design that knows two races and refuses everything else.
            if (race is not ("Elf" or "Dwarf"))
            {
                return false;
            }

            Race = race;
            return true;
        }

        public override int VaultMoneyAvailable(int coinType) => coinType switch
        {
            0 => 500,
            1 => 100,
            _ => 0,
        };
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

    /// <summary>
    /// The type literals carry their at-signs, and a monster answers its own name instead.
    /// </summary>
    /// <remarks>
    /// <b>Two of the three are types and the third is an identity.</b> The reference pushes
    /// <c>"@PC@"</c>, <c>"@NPC@"</c> or the monster's <c>monsterID</c> — so a script cannot switch
    /// on this the way it would on an enum.
    /// </remarks>
    [Fact]
    public void The_type_literals_keep_their_at_signs()
    {
        var host = new IdentityHost();

        Assert.Equal("@PC@", Run("$RETURN $GET_CHAR_TYPE(\"hero\");", host));
        Assert.Equal("@PC@", IGpdlHost.PlayerCharacterType);
        Assert.Equal("@NPC@", IGpdlHost.NpcType);

        host.Type = "Giant Rat";
        Assert.Equal("Giant Rat", Run("$RETURN $GET_CHAR_TYPE(\"hero\");", host));
    }

    /// <summary>A race comes back by name.</summary>
    [Fact]
    public void A_race_comes_back_by_name() =>
        Assert.Equal("Elf", Run("$RETURN $GET_CHAR_RACE(\"hero\");",
                                new IdentityHost()));

    /// <summary>
    /// An unresolved actor answers a literal a script can test, not the empty string.
    /// </summary>
    [Fact]
    public void An_unresolved_actor_says_so()
    {
        // The base environment resolves nobody.
        Assert.Equal("NoSuchCharacter",
                     Run("$RETURN $GET_CHAR_RACE(\"hero\");",
                         new GpdlUnhostedEnvironment()));

        Assert.Equal("NoSuchCharacter", IGpdlHost.NoSuchCharacter);
    }

    /// <summary>Setting a race the design knows takes, and answers with the name.</summary>
    [Fact]
    public void A_known_race_is_set()
    {
        var host = new IdentityHost();
        string result = Run("""$RETURN $SET_CHAR_RACE("hero", "Dwarf");""", host);

        Assert.Equal("Dwarf", result);
        Assert.Equal("Dwarf", host.Race);
        Assert.Single(host.RaceWrites);
    }

    /// <summary>
    /// A race the design does not have is refused, and the character keeps the one it had.
    /// </summary>
    /// <remarks>
    /// <b>The empty answer is how a script tells the difference.</b> The reference looks the name
    /// up and leaves the character alone when it is missing — a script cannot invent a race by
    /// assigning one.
    /// </remarks>
    [Fact]
    public void An_unknown_race_is_refused()
    {
        var host = new IdentityHost();
        string result = Run("""$RETURN $SET_CHAR_RACE("hero", "Balrog");""", host);

        Assert.Equal(string.Empty, result);
        Assert.Equal("Elf", host.Race);
    }

    /// <summary>Vault money: coin type zero is "do not convert", not a denomination.</summary>
    [Fact]
    public void Vault_money_takes_zero_as_no_conversion()
    {
        var host = new IdentityHost();

        Assert.Equal("500", Run("$RETURN $GET_VAULT_MONEYAVAILABLE(0);", host));
        Assert.Equal("100", Run("$RETURN $GET_VAULT_MONEYAVAILABLE(1);", host));

        // Past the ten denominations there is nothing to convert into.
        Assert.Equal("0", Run("$RETURN $GET_VAULT_MONEYAVAILABLE(11);", host));
    }
}
