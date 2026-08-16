using UAF.Data;

namespace UAFedit.Spells.Tests;

/// <summary>The special-abilities editor, over abilities built by hand.</summary>
public class SpecialAbilityEditorTests
{
    private static SpecialAbility Ability() => new("Bless",
    [
        new SpecialAbilityEntry("Activation", "$RETURN 1;", SpecialAbilityEntryKind.Script),
        new SpecialAbilityEntry("bonus", "5", SpecialAbilityEntryKind.Variable),
        new SpecialAbilityEntry("levels", "1\n2\n3", SpecialAbilityEntryKind.IntegerTable),
        new SpecialAbilityEntry("note", "a plain string", SpecialAbilityEntryKind.Constant),
    ]);

    /// <summary>
    /// The bracket rule, checked against the real parser rather than restated.
    /// </summary>
    /// <remarks>
    /// <b>This is the test that pins a deliberate duplication.</b>
    /// <c>SpecialAbilityEntryViewModel.Decode</c> copies <c>SpecialAbilitiesFile</c>'s private
    /// <c>Entry</c> because it has to answer on every keystroke. Encoding a key, running the real
    /// loader over a synthetic file containing it, and comparing is what keeps the copy honest —
    /// including the three-character floor, which is the part that surprises.
    /// </remarks>
    [Theory]
    [InlineData("Activation", SpecialAbilityEntryKind.Script)]
    [InlineData("bonus", SpecialAbilityEntryKind.Variable)]
    [InlineData("levels", SpecialAbilityEntryKind.IntegerTable)]
    [InlineData("note", SpecialAbilityEntryKind.Constant)]
    [InlineData("x", SpecialAbilityEntryKind.Script)]
    [InlineData("x", SpecialAbilityEntryKind.Variable)]
    [InlineData("x", SpecialAbilityEntryKind.IntegerTable)]
    public void An_encoded_key_reads_back_as_the_kind_it_was_encoded_for(
        string name, SpecialAbilityEntryKind kind)
    {
        string key = SpecialAbilityEntryViewModel.Encode(name, kind);

        var parsed = SpecialAbilitiesFile.Parse(
            ["\\(BEGIN)", "name = A", $"{key} = v", "\\(END)"]);

        var entry = Assert.Single(Assert.Single(parsed).Entries);

        Assert.Equal(kind, entry.Kind);
        Assert.Equal(name, entry.Name);
        Assert.Equal(kind, SpecialAbilityEntryViewModel.Decode(key));
    }

    /// <summary>
    /// A bracketed kind with no name is not that kind: <c>[]</c> is two characters.
    /// </summary>
    /// <remarks>
    /// The floor is three (<c>SpecialAbilitiesFile</c>), so an empty-named script encodes to a key
    /// the loader reads as a <i>constant literally called <c>[]</c></i>. The editor says so instead
    /// of pretending the entry is fine.
    /// </remarks>
    [Fact]
    public void An_empty_name_cannot_carry_a_bracketed_kind()
    {
        string key = SpecialAbilityEntryViewModel.Encode(string.Empty,
                                                         SpecialAbilityEntryKind.Script);
        Assert.Equal("[]", key);
        Assert.Equal(SpecialAbilityEntryKind.Constant,
                     SpecialAbilityEntryViewModel.Decode(key));

        var parsed = SpecialAbilitiesFile.Parse(["\\(BEGIN)", "name = A", "[] = v", "\\(END)"]);
        var entry = Assert.Single(Assert.Single(parsed).Entries);

        Assert.Equal(SpecialAbilityEntryKind.Constant, entry.Kind);
        Assert.Equal("[]", entry.Name);

        var vm = new SpecialAbilityEntryViewModel(
            new SpecialAbilityEntry(string.Empty, "v", SpecialAbilityEntryKind.Script), "A");

        Assert.False(vm.IsFaithful);
        Assert.Contains("Constant", vm.FidelityWarning);
    }

    /// <remarks>
    /// The splitter is a plain <c>Find('=')</c> with no escape handling, so a name carrying one is
    /// cut in half and its tail becomes the start of the value.
    /// </remarks>
    [Fact]
    public void A_name_containing_an_equals_sign_is_flagged()
    {
        var vm = new SpecialAbilityEntryViewModel(
            new SpecialAbilityEntry("a=b", "v", SpecialAbilityEntryKind.Constant), "A");

        Assert.False(vm.IsFaithful);
        Assert.Contains("'='", vm.FidelityWarning);
    }

    /// <remarks>
    /// The kind picker is not a label: it rewrites the key, and the engine finds a script by that
    /// key or not at all.
    /// </remarks>
    [Fact]
    public void Changing_the_kind_rewrites_the_key()
    {
        var vm = new SpecialAbilityEntryViewModel(
            new SpecialAbilityEntry("Attempt", "$RETURN 1;", SpecialAbilityEntryKind.Script), "A");

        Assert.Equal("[Attempt]", vm.Key);

        vm.Kind = SpecialAbilityEntryKind.Variable;

        Assert.Equal("(Attempt)", vm.Key);
        Assert.True(vm.IsDirty);
        Assert.True(vm.IsFaithful);
        Assert.Equal(SpecialAbilityEntryKind.Variable, vm.ToEntry().Kind);
    }

    /// <summary>
    /// A table stops at the first line that is not a number, and everything below is lost.
    /// </summary>
    /// <remarks>
    /// The reference's loop advances only inside its <c>sscanf</c> success branch, so a blank line
    /// in the middle of a design's table hides the rest of it from the engine while the text box
    /// still looks perfectly fine. Making the count visible is the only way a designer finds this.
    /// </remarks>
    [Fact]
    public void An_integer_table_reports_where_it_stops()
    {
        var whole = new SpecialAbilityEntryViewModel(
            new SpecialAbilityEntry("t", "1\n2\n3", SpecialAbilityEntryKind.IntegerTable), "A");

        Assert.Equal([1, 2, 3], whole.TableNumbers);
        Assert.Equal(string.Empty, whole.TableTruncation);

        var truncated = new SpecialAbilityEntryViewModel(
            new SpecialAbilityEntry("t", "1\n2\n\n3\n4", SpecialAbilityEntryKind.IntegerTable),
            "A");

        Assert.Equal([1, 2], truncated.TableNumbers);
        Assert.Contains("2 of 4", truncated.TableTruncation);
    }

    /// <remarks>Only a table is parsed as one; the same text under another kind is just text.</remarks>
    [Fact]
    public void Only_an_integer_table_is_parsed_as_numbers()
    {
        var vm = new SpecialAbilityEntryViewModel(
            new SpecialAbilityEntry("t", "1\n2", SpecialAbilityEntryKind.Constant), "A");

        Assert.Empty(vm.TableNumbers);

        vm.Kind = SpecialAbilityEntryKind.IntegerTable;

        Assert.Equal([1, 2], vm.TableNumbers);
    }

    [Fact]
    public void An_untouched_ability_is_not_dirty_and_round_trips()
    {
        var original = Ability();
        using var editor = new SpecialAbilityEditorViewModel(original);

        Assert.False(editor.IsDirty);
        Assert.Equal(original.Name, editor.ToAbility().Name);
        Assert.Equal(original.Entries, editor.ToAbility().Entries);
    }

    /// <remarks>
    /// Entries are their own view models; nothing but the ability's subscription joins them.
    /// </remarks>
    [Fact]
    public void Editing_an_entry_makes_the_ability_dirty()
    {
        using var editor = new SpecialAbilityEditorViewModel(Ability());

        editor.Entries[0].Value = "$RETURN 2;";

        Assert.True(editor.IsDirty);
        Assert.Equal("$RETURN 2;", editor.ToAbility().Entries[0].Value);
    }

    [Fact]
    public void Adding_and_removing_entries_is_an_edit_and_reverts()
    {
        var original = Ability();
        using var editor = new SpecialAbilityEditorViewModel(original);

        editor.AddEntryCommand.Execute(null);
        Assert.Equal(5, editor.Entries.Count);
        Assert.True(editor.IsDirty);

        // A fresh row is a nameless constant, which is the one combination that is faithful empty.
        Assert.True(editor.Entries[4].IsFaithful);

        editor.RemoveEntryCommand.Execute(null);
        Assert.Equal(4, editor.Entries.Count);

        editor.Revert();

        Assert.False(editor.IsDirty);
        Assert.Equal(original.Entries, editor.ToAbility().Entries);
    }

    /// <remarks>
    /// The four kinds are counted because that split is the only structure an ability has — the
    /// file says nothing about what entries an ability should carry.
    /// </remarks>
    [Fact]
    public void The_summary_counts_the_four_kinds_and_follows_a_kind_change()
    {
        using var editor = new SpecialAbilityEditorViewModel(Ability());

        Assert.Equal(1, editor.Count(SpecialAbilityEntryKind.Script));
        Assert.Equal(1, editor.ScriptCount);

        editor.Entries[1].Kind = SpecialAbilityEntryKind.Script;

        Assert.Equal(2, editor.ScriptCount);
        Assert.Contains("2 scripts", editor.Summary);
    }

    /// <remarks>
    /// A compile result is an observation about the entry, not a change to it — otherwise pressing
    /// Test Syntax would make an untouched design claim unsaved changes.
    /// </remarks>
    [Fact]
    public void Compiling_does_not_dirty_anything()
    {
        using var editor = new SpecialAbilityEditorViewModel(Ability());

        Assert.Equal(0, editor.CompileScripts());

        Assert.False(editor.IsDirty);
        Assert.False(editor.Entries[0].IsDirty);
        Assert.True(editor.Entries[0].HasCompiled);
        Assert.True(editor.Entries[0].Diagnostics.Succeeded);
    }

    [Fact]
    public void A_broken_script_is_reported_and_the_rest_still_compile()
    {
        var ability = new SpecialAbility("Broken",
        [
            new SpecialAbilityEntry("bad", "$IF (", SpecialAbilityEntryKind.Script),
            new SpecialAbilityEntry("good", "$RETURN 1;", SpecialAbilityEntryKind.Script),
        ]);

        using var editor = new SpecialAbilityEditorViewModel(ability);

        Assert.Equal(1, editor.CompileScripts());
        Assert.False(editor.Entries[0].Diagnostics.Succeeded);
        Assert.NotEmpty(editor.Entries[0].Diagnostics.Errors);
        Assert.True(editor.Entries[1].Diagnostics.Succeeded);
    }
}
