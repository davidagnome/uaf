using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers which saved file a character's name maps to.
/// </summary>
/// <remarks>
/// <b>This had no test, and the port had it backwards.</b> The prefix was keyed on the constant
/// <c>1</c>, documented as <c>NPC_TYPE</c> — but <c>CHAR_TYPE</c> is 1 and <c>NPC_TYPE</c> is 2
/// (<c>Externs.h:965</c>), so DELETE looked for every player character at
/// <c>DCNPC_&lt;name&gt;.chr</c> and every NPC at <c>&lt;name&gt;.chr</c>. Both misses were
/// silent: the delete failed, was caught, and reported a file that was never there.
/// </remarks>
public class CharacterFileNameTests
{
    private static CharacterRecord Record(byte type) =>
        NewCharacter.Blank with { Name = "Kagain", Type = type };

    /// <summary>The rule DELETE applies (<c>Party.cpp:2109</c>).</summary>
    private static string FileName(CharacterRecord record) =>
        (EventNpc.KindOf(record) == EventNpc.NpcType ? CharacterRoster.NpcFilePrefix : "")
        + record.Name + ".chr";

    [Fact]
    public void A_player_character_has_no_prefix()
    {
        Assert.Equal("Kagain.chr", FileName(Record((byte)CombatantKind.Character)));
    }

    [Fact]
    public void An_npc_carries_the_DCNPC_prefix()
    {
        Assert.Equal("DCNPC_Kagain.chr", FileName(Record((byte)CombatantKind.Npc)));
    }

    [Fact]
    public void The_in_party_flag_does_not_change_the_answer()
    {
        // type holds a kind in its low bits and a membership flag in the top one, and GetType()
        // masks the flag off. A raw comparison against the kind misses every record saved while
        // its subject was in the party -- which is every record DELETE ever sees.
        Assert.Equal("DCNPC_Kagain.chr",
                     FileName(Record((byte)((byte)CombatantKind.Npc | EventNpc.InPartyFlag))));

        Assert.Equal("Kagain.chr",
                     FileName(Record((byte)((byte)CombatantKind.Character
                                            | EventNpc.InPartyFlag))));
    }

    [Fact]
    public void The_three_kinds_are_one_two_and_three()
    {
        Assert.Equal(1, (int)CombatantKind.Character);
        Assert.Equal(2, (int)CombatantKind.Npc);
        Assert.Equal(3, (int)CombatantKind.Monster);
        Assert.Equal(2, EventNpc.NpcType);
    }
}
