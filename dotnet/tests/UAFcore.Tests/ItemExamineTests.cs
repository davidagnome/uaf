using UAF.Data;
using UAF.Scripting;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The inventory's EXAMINE entry, which a design renames and gates per item.
/// </summary>
/// <remarks>
/// <b>"EXAMINE" is a default label, not the command's name.</b> The reference's own comments call
/// it "EXAMINE (or whatever)" throughout, and this is the machinery behind READ, DRINK, LIGHT and
/// whatever else a design puts there.
/// </remarks>
public class ItemExamineTests
{
    /// <summary>An item whose scripts answer whatever the test wants.</summary>
    private static ItemRecord Item(string examineLabel, params (string Name, string Body)[] scripts)
        => new(new ItemNames(0, "", "", "", "", "", ""),
               HitArt: null, MissileArt: null,
               new ItemScalars("", 0, 0, 0, 0, 0, 0, 0),
               new ItemCombat(ReadiedLocation.WeaponHand, 1, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
               new ItemTail(0, 0, 0, [], 0, 0, 0, examineLabel, "", 0, 0, null, 0, 0,
                            Block(scripts), []));

    private static CharacterRecord Who(params (string Name, string Body)[] scripts) =>
        new(0, 0, 0, "human", 0, "fighter", 0, 0, 0, "", 0, "Aramil", "",
            0, 0, 0, 100, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, [], [], [], 0, 0, 0, null, 0,
            null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [],
            new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            new ItemList([], new ReadyItems([])), Block(scripts), []);

    /// <summary>A specab block naming one ability, whose scripts the global table holds.</summary>
    private static SpecabBlock Block((string Name, string Body)[] scripts) =>
        new([.. scripts.Select(s => new SpecabPair(s.Name, ""))], [], []);

    /// <summary>A script table where each ability's named script is its body.</summary>
    private static GlobalScripts Scripts(params (string Ability, string Script, string Body)[] all)
        => new([.. all.GroupBy(a => a.Ability).Select(
                     g => new SpecialAbility(g.Key,
                         [.. g.Select(a => new SpecialAbilityEntry(
                                a.Script, a.Body, SpecialAbilityEntryKind.Script))]))]);

    /// <summary>An item with no label offers no entry, before any script runs.</summary>
    /// <remarks>
    /// <b>That is the gate.</b> It is what greys the entry out for most of what a character
    /// carries — and it is checked first, so a design cannot script its way past it.
    /// </remarks>
    [Fact]
    public void An_item_with_no_label_has_no_entry()
    {
        var entry = ItemExamine.EntryFor(Who(), Item(string.Empty), 0,
                                         Scripts(), new GpdlUnhostedEnvironment());

        Assert.Equal(ItemExamine.ExamineEntry.None, entry);
        Assert.False(entry.Enabled);
    }

    /// <summary>The item's own label becomes the entry's text.</summary>
    [Fact]
    public void The_items_label_names_the_entry()
    {
        var entry = ItemExamine.EntryFor(Who(), Item("READ"), 0,
                                         Scripts(), new GpdlUnhostedEnvironment());

        Assert.Equal("READ", entry.Label);

        // No script answered, so the entry stays enabled -- see the next test.
        Assert.True(entry.Enabled);
    }

    /// <summary>
    /// An empty answer means yes, so a design that does not answer keeps the entry.
    /// </summary>
    /// <remarks>
    /// Only the first character is looked at, and only <c>Y</c> enables — so anything else
    /// disables, and saying nothing does not.
    /// </remarks>
    [Theory]
    [InlineData("Y", true)]
    [InlineData("YES", true)]
    [InlineData("N", false)]
    [InlineData("no", false)]
    [InlineData("1", false)]
    public void Only_Y_enables_and_silence_leaves_it_alone(string answer, bool enabled)
    {
        var host = new GpdlUnhostedEnvironment();
        var scripts = Scripts(("Scroll", ItemExamine.CanExamineHook,
                               $"""$RETURN "{answer}";"""));

        var entry = ItemExamine.EntryFor(Who(), Item("READ", ("Scroll", "")), 0, scripts, host);

        Assert.Equal(enabled, entry.Enabled);
    }

    /// <summary>
    /// A script renames the entry by writing to the label slot.
    /// </summary>
    /// <remarks>
    /// <b>The slot is the whole channel</b> — there is no return value for the text, so a script
    /// changes the menu by writing hook parameter 5.
    /// </remarks>
    [Fact]
    public void A_script_can_rename_the_entry()
    {
        var host = new GpdlUnhostedEnvironment();
        var scripts = Scripts(("Scroll", ItemExamine.CanExamineHook,
                               """$SET_HOOK_PARAM("5", "LIGHT"); $RETURN "Y";"""));

        var entry = ItemExamine.EntryFor(Who(), Item("READ", ("Scroll", "")), 0, scripts, host);

        Assert.Equal("LIGHT", entry.Label);
        Assert.True(entry.Enabled);
    }

    /// <summary>
    /// A character's script renames the entry AND can disable it, because the answers chain.
    /// </summary>
    /// <remarks>
    /// <b>The reference never reads the character's return value into a variable — and does not
    /// need to.</b> Every run seeds its result from hook parameter 0 and writes back to it, and
    /// slot 0 <i>is</i> the return value. So the character's answer carries into the item's run and
    /// stands whenever the item has nothing of its own to say. Reading the C++ alone suggests the
    /// character cannot decide; the hook-parameter block says otherwise.
    /// </remarks>
    [Fact]
    public void The_characters_answer_chains_into_the_items()
    {
        var host = new GpdlUnhostedEnvironment();
        var scripts = Scripts(
            ("Scholar", ItemExamine.CanExamineHook,
             """$SET_HOOK_PARAM("5", "STUDY"); $RETURN "N";"""));

        // The item has no scripts, so the character's "N" is what the entry is judged on.
        var entry = ItemExamine.EntryFor(Who(("Scholar", "")), Item("READ"), 0, scripts, host);

        Assert.Equal("STUDY", entry.Label);
        Assert.False(entry.Enabled);
    }

    /// <summary>And the item overrules it when it does answer.</summary>
    [Fact]
    public void The_items_answer_overrules_the_characters()
    {
        var host = new GpdlUnhostedEnvironment();
        var scripts = Scripts(
            ("Scholar", ItemExamine.CanExamineHook, """$RETURN "N";"""),
            ("Scroll", ItemExamine.CanExamineHook, """$RETURN "Y";"""));

        var entry = ItemExamine.EntryFor(Who(("Scholar", "")), Item("READ", ("Scroll", "")),
                                         0, scripts, host);

        Assert.True(entry.Enabled);
    }

    /// <summary>
    /// An unset shortcut is −1, which means "derive one later".
    /// </summary>
    [Fact]
    public void An_unset_shortcut_asks_for_one_to_be_chosen()
    {
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal(-1, ItemExamine.EntryFor(Who(), Item("READ"), 0, Scripts(), host).Shortcut);

        var scripts = Scripts(("Scroll", ItemExamine.CanExamineHook,
                               """$SET_HOOK_PARAM("6", "82"); $RETURN "Y";"""));

        Assert.Equal(82,
                     ItemExamine.EntryFor(Who(), Item("READ", ("Scroll", "")), 0, scripts, host)
                                .Shortcut);
    }

    /// <summary>The row the item sits on is handed to the scripts.</summary>
    [Fact]
    public void The_scripts_are_told_which_row_it_is()
    {
        var host = new GpdlUnhostedEnvironment();
        var scripts = Scripts(("Scroll", ItemExamine.CanExamineHook,
                               """$SET_HOOK_PARAM("5", $GET_HOOK_PARAM("4")); $RETURN "Y";"""));

        var entry = ItemExamine.EntryFor(Who(), Item("READ", ("Scroll", "")), 7, scripts, host);

        Assert.Equal("7", entry.Label);
    }

    /// <summary>Choosing it answers what the item's scripts said.</summary>
    [Fact]
    public void Choosing_answers_the_items_result()
    {
        var host = new GpdlUnhostedEnvironment();
        var scripts = Scripts(("Scroll", ItemExamine.ExamineHook,
                               """$RETURN "CastSpell";"""));

        Assert.Equal("CastSpell",
                     ItemExamine.Choose(Who(), Item("READ", ("Scroll", "")), scripts, host));
    }

    /// <summary>
    /// And an item with no scripts answers whatever the character said.
    /// </summary>
    /// <remarks>
    /// The same chaining as the gate: hook parameter 0 carries the result from one run to the next,
    /// which is why the reference can ignore the character's return value and still see it.
    /// </remarks>
    [Fact]
    public void Choosing_falls_through_to_the_characters_answer()
    {
        var host = new GpdlUnhostedEnvironment();
        var scripts = Scripts(
            ("Scholar", ItemExamine.ExamineHook, """$RETURN "CastSpell";"""));

        Assert.Equal("CastSpell",
                     ItemExamine.Choose(Who(("Scholar", "")), Item("READ"), scripts, host));
    }
}
