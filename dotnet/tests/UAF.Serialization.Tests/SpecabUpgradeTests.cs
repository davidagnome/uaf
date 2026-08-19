using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Converting a pre-0.921 special-abilities block into modern named pairs.
/// </summary>
/// <remarks>
/// <b>Unit tests, and deliberately so.</b> No design in the corpus exercises this: the editor's
/// template is below 0.921 but every one of its 9,120 legacy slots is empty, and the other two
/// designs are above it and never had slots. Testing it against a fixture is the only way to cover
/// the half that matters — that converting a record also invents the abilities it now names.
/// </remarks>
public class SpecabUpgradeTests
{
    private static LegacySpecabSlot Empty() =>
        new(string.Empty, string.Empty, string.Empty, string.Empty, 0, 0, []);

    private static LegacySpecabSlot Scripted(string activation, string deactivation = "",
                                             params string[] messages) =>
        new(activation, string.Empty, deactivation, string.Empty, 0, 0, messages);

    private static SpecabBlock Legacy(params LegacySpecabSlot[] slots) =>
        new([], slots, []);

    /// <summary>An empty slot becomes nothing at all — no pair, no ability.</summary>
    /// <remarks>
    /// The reference's test sums every string's length and adds one for a non-zero message mask.
    /// A design's unused slots are all like this, which is why the editor's template converts to
    /// nothing.
    /// </remarks>
    [Fact]
    public void An_empty_slot_is_dropped()
    {
        var done = SpecabUpgrade.Convert(Legacy(Empty(), Empty()), "item", "Sword");

        Assert.Empty(done.Block.Pairs);
        Assert.Empty(done.Added);
        Assert.False(SpecabUpgrade.NeedsUpgrade(done.Block));
    }

    /// <summary>
    /// A slot with a script becomes a named pair on the record and an ability beside it.
    /// </summary>
    /// <remarks>
    /// <b>Both halves, because either alone is broken.</b> A pair with no ability names something
    /// that does not exist; an ability nothing names is dead weight.
    /// </remarks>
    [Fact]
    public void A_scripted_slot_becomes_a_pair_and_an_ability()
    {
        var done = SpecabUpgrade.Convert(
            Legacy(Empty(), Scripted("$RETURN 1;")), "item", "Sword");

        // Slot 1 is "Bless".
        var pair = Assert.Single(done.Block.Pairs);
        Assert.Equal("item_Sword_Bless", pair.Key);
        Assert.Equal(string.Empty, pair.Value);

        var ability = Assert.Single(done.Added);
        Assert.Equal("item_Sword_Bless", ability.Name);

        var entry = Assert.Single(ability.Entries);
        Assert.Equal("Activation Script", entry.Name);
        Assert.Equal("$RETURN 1;", entry.Value);
        Assert.True(entry.IsScript);
    }

    /// <summary>The slot index picks the name, so slot 22 is the vorpal one.</summary>
    [Theory]
    [InlineData(0, "item_Sword_None")]
    [InlineData(1, "item_Sword_Bless")]
    [InlineData(22, "item_Sword_Vorpal Attack")]
    [InlineData(31, "item_Sword_Diseased")]
    public void The_slot_index_names_the_ability(int slot, string expected) =>
        Assert.Equal(expected, SpecabUpgrade.AbilityName("item", "Sword", slot));

    /// <summary>The owner type is part of the name, so the three databases cannot collide.</summary>
    [Fact]
    public void The_owner_type_is_part_of_the_name()
    {
        Assert.Equal("monster_Kobold_Bless", SpecabUpgrade.AbilityName("monster", "Kobold", 1));
        Assert.Equal("spell_Bless_Bless", SpecabUpgrade.AbilityName("spell", "Bless", 1));
    }

    /// <summary>Both scripts and every message come across, each under its own name.</summary>
    /// <remarks>
    /// Messages are positional: the j'th is named for the j'th action, so a message for "Cast
    /// Spell" is at index 2 whether or not the earlier ones are set.
    /// </remarks>
    [Fact]
    public void Scripts_and_messages_all_come_across()
    {
        var slot = Scripted("$RETURN 1;", "$RETURN 0;", "", "", "he glows");

        var ability = Assert.Single(SpecabUpgrade.Convert(Legacy(slot), "item", "Rod").Added);

        Assert.Equal(
            [("Activation Script", "$RETURN 1;", true),
             ("DeActivation Script", "$RETURN 0;", true),
             ("Cast Spell Msg", "he glows", false)],
            ability.Entries.Select(e => (e.Name, e.Value, e.IsScript)));
    }

    /// <summary>
    /// The compiled bytecode is dropped and only the source travels.
    /// </summary>
    /// <remarks>
    /// A slot carries both. Carrying the bytecode into the new ability would pin the design to
    /// whichever compiler build produced it; the reference copies only the source.
    /// </remarks>
    [Fact]
    public void The_compiled_binary_is_dropped()
    {
        var slot = new LegacySpecabSlot("$RETURN 1;", "COMPILED", "$RETURN 0;", "ALSO COMPILED",
                                        0, 0, []);

        var ability = Assert.Single(SpecabUpgrade.Convert(Legacy(slot), "item", "Rod").Added);

        Assert.All(ability.Entries,
                   e => Assert.DoesNotContain("COMPILED", e.Value, StringComparison.Ordinal));
    }

    /// <summary>A modern block is returned as it is.</summary>
    [Fact]
    public void A_modern_block_is_left_alone()
    {
        var block = new SpecabBlock([new SpecabPair("already", "there")], [], []);

        Assert.False(SpecabUpgrade.NeedsUpgrade(block));
        Assert.Same(block, SpecabUpgrade.Convert(block, "item", "Sword").Block);
    }

    /// <summary>
    /// The oldest shape — a bare ordinal array — is left alone and still cannot be written.
    /// </summary>
    /// <remarks>
    /// Below 0.850 an object stores ability <i>numbers</i>, and the reference does not invent
    /// definitions for those either: it calls <c>EnableSpecAb</c>, which turns on a built-in
    /// ability. Converting them would need the built-in table and is a separate question.
    /// </remarks>
    [Fact]
    public void An_ordinal_array_is_not_converted()
    {
        var block = new SpecabBlock([], [], [3, 7]);

        var done = SpecabUpgrade.Convert(block, "item", "Sword");

        Assert.Empty(done.Added);
        Assert.Equal<IEnumerable<ushort>>([3, 7], done.Block.LegacyOrdinals);
        Assert.True(SpecabUpgrade.NeedsUpgrade(done.Block));
        Assert.False(SpecabWriter.CanWrite(done.Block));
    }

    /// <summary>A converted block is one a writer will take, which is the point.</summary>
    [Fact]
    public void A_converted_block_can_be_written()
    {
        var done = SpecabUpgrade.Convert(Legacy(Scripted("$RETURN 1;")), "item", "Sword");

        Assert.True(SpecabWriter.CanWrite(done.Block));
    }
}
