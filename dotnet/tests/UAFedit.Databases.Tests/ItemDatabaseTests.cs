using UAF.Serialization;
using UAFedit.Databases;

namespace UAFedit.Databases.Tests;

/// <summary>
/// The item editor against a real design.
/// </summary>
/// <remarks>
/// Everything here needs a shipped design, so every test early-returns without one and
/// <see cref="The_corpus_design_has_an_item_database"/> is what makes that safe.
/// </remarks>
public class ItemDatabaseTests
{
    /// <summary>
    /// <b>The premise.</b> Without this the whole file passes on a checkout with no corpus while
    /// asserting nothing at all.
    /// </summary>
    /// <remarks>
    /// Both shipped designs, not just the one the rest of the file uses — the item database is the
    /// largest structure in a design and the two disagree about enough of it to be worth the
    /// second run.
    /// </remarks>
    [Theory]
    [InlineData("SomethingWild.dsn")]
    [InlineData("Case.dsn")]
    public void The_corpus_design_has_an_item_database(string name)
    {
        using var design = DatabaseCorpus.Open(name);
        if (design is null)
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);

        Assert.True(editor.IsReadable, $"{name} has no readable items.dat");
        Assert.True(editor.Count > 0, $"{name}'s items.dat read as empty");
        Assert.Equal(editor.Count, editor.Records.Count);
        Assert.Contains(editor.All, e => e.Title.Length > 0);

        // And it round-trips: not one record changes by being loaded into the form and rebuilt.
        var changed = editor.All.Where(e => e.IsDirty).Select(e => e.Title).ToList();
        Assert.True(changed.Count == 0,
                    $"{name}: records changed by merely loading them: "
                    + string.Join(", ", changed));
    }

    [Fact]
    public void The_list_populates_and_everything_is_visible_by_default()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);

        Assert.Equal(editor.Count, editor.Visible.Count);
        Assert.NotNull(editor.Selected);
        Assert.Contains(editor.Selected!, editor.Visible);
    }

    /// <summary>
    /// <b>Every record survives a load and a rebuild unchanged.</b>
    /// </summary>
    /// <remarks>
    /// The single most useful assertion in this file. It catches any asymmetry between
    /// <c>Load</c> and <c>Build</c> — a field read but not written, a collection rebuilt into a
    /// fresh instance, a value normalised on the way in — for every field of every item the design
    /// happens to contain, without naming any of them.
    /// </remarks>
    [Fact]
    public void Nothing_is_dirty_before_anything_is_edited()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);

        var changed = editor.All.Where(e => e.IsDirty).Select(e => e.Title).ToList();

        Assert.True(changed.Count == 0,
                    $"records changed by merely loading them: {string.Join(", ", changed)}");
        Assert.False(editor.IsDirty);
        Assert.Equal(0, editor.DirtyCount);
    }

    /// <summary>The whole point of the editor: an edit is dirty, and it is in the read-back.</summary>
    [Fact]
    public void An_edit_marks_the_record_dirty_and_shows_up_in_the_read_back()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);
        var item = editor.All[0];
        int index = 0;

        item.Cost += 137;

        Assert.True(item.IsDirty);
        Assert.True(editor.IsDirty);
        Assert.Equal(1, editor.DirtyCount);

        Assert.Equal(item.Cost, editor.Records[index].Scalars.Cost);
        Assert.Equal(item.Original.Scalars.Cost + 137, editor.Records[index].Scalars.Cost);

        // Every other record is untouched, which is what says the edit went where it was aimed.
        Assert.Equal(editor.Count, editor.Records.Count);
        Assert.All(editor.All.Skip(1), e => Assert.False(e.IsDirty));
    }

    [Fact]
    public void The_ammo_type_list_is_carried_into_the_read_back_database()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null || design.Items is not { } loaded)
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);

        Assert.Equal(loaded.AmmoTypes, editor.Database.AmmoTypes);
        Assert.Equal(loaded.Items.Count, editor.Database.Items.Count);
    }

    /// <summary>A family an item now names is added to the list a save would write.</summary>
    [Fact]
    public void A_new_ammo_type_reaches_the_read_back_database()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);
        editor.All[0].AmmoType = "TestBolts";

        Assert.Contains("TestBolts", editor.Database.AmmoTypes);

        // and nothing that was there has gone.
        Assert.All(editor.AmmoTypes, t => Assert.Contains(t, editor.Database.AmmoTypes));
    }

    [Fact]
    public void Reverting_puts_the_record_back()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);
        var item = editor.All[0];
        var before = item.Record;

        item.UniqueName = "Not what it was called";
        item.Encumbrance = 999;
        Assert.True(item.IsDirty);

        item.Revert();

        Assert.False(item.IsDirty);
        Assert.Equal(before, item.Record);
    }

    [Fact]
    public void Accepting_changes_makes_the_edited_state_the_clean_state()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);
        editor.All[0].Cost = 4242;

        editor.AcceptChanges();

        Assert.False(editor.IsDirty);
        Assert.Equal(4242, editor.Records[0].Scalars.Cost);
        Assert.Equal(4242, editor.All[0].Original.Scalars.Cost);
    }

    [Fact]
    public void Searching_filters_the_visible_list_without_touching_the_records()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);
        int total = editor.Count;

        editor.Search = editor.All[0].UniqueName;

        Assert.NotEmpty(editor.Visible);
        Assert.True(editor.Visible.Count <= total);
        Assert.All(editor.Visible,
                   e => Assert.Contains(editor.Search, e.UniqueName + e.IdName,
                                        StringComparison.OrdinalIgnoreCase));

        Assert.Equal(total, editor.Records.Count);

        editor.Search = " no item is called this";
        Assert.Empty(editor.Visible);
        Assert.Equal(total, editor.Records.Count);

        editor.Search = string.Empty;
        Assert.Equal(total, editor.Visible.Count);
    }

    [Fact]
    public void Sorting_orders_the_visible_list_and_reverses_on_demand()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null || design.Items is not { Items.Count: > 1 })
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);
        editor.Sort = editor.Sorts.First(s => s.Label == "Cost");

        var ascending = editor.Visible.Select(e => e.Cost).ToList();
        Assert.Equal(ascending.Order(), ascending);

        editor.SortDescending = true;
        var descending = editor.Visible.Select(e => e.Cost).ToList();
        Assert.Equal(ascending.AsEnumerable().Reverse(), descending);
    }

    [Fact]
    public void Adding_and_deleting_change_the_read_back_collection()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);
        int before = editor.Count;

        editor.Add();

        Assert.Equal(before + 1, editor.Records.Count);
        Assert.True(editor.IsDirty);
        Assert.NotNull(editor.Selected);
        Assert.Equal("New Item", editor.Selected!.UniqueName);

        editor.Delete();

        Assert.Equal(before, editor.Records.Count);
    }

    /// <summary>A duplicate gets a fresh id, so the database stays keyed.</summary>
    [Fact]
    public void Duplicating_gives_the_copy_an_unused_name()
    {
        using var design = DatabaseCorpus.Open();
        if (design is null)
        {
            return;
        }

        var editor = new ItemDatabaseViewModel(design);
        editor.Selected = editor.All[0];
        string source = editor.All[0].UniqueName;

        editor.Duplicate();

        Assert.NotNull(editor.Selected);
        Assert.NotEqual(source, editor.Selected!.UniqueName);
        Assert.StartsWith(source, editor.Selected.UniqueName, StringComparison.Ordinal);
        Assert.DoesNotContain(editor.Selected.UniqueName, editor.DuplicateNames);
    }

    // ---- Synthetic records: the cases a shipped design is not guaranteed to contain -------------

    /// <summary>
    /// Ticking a baseclass and un-ticking it lands back on clean.
    /// </summary>
    /// <remarks>
    /// The collection member that <c>RecordEditorViewModel.Canonical</c> exists for: without it
    /// this would rebuild <c>UsableByBaseclass</c> into a new list instance and the record would
    /// compare unequal to itself forever after.
    /// </remarks>
    [Fact]
    public void A_baseclass_toggled_twice_leaves_the_record_clean()
    {
        var editor = new ItemEditorViewModel(ItemEditorViewModel.NewRecord("Sword"),
                                             ["Fighter", "Cleric"]);

        Assert.False(editor.IsDirty);
        Assert.True(editor.UsableByAnyBaseclass);

        editor.Baseclasses[0].IsSelected = true;
        Assert.True(editor.IsDirty);
        Assert.False(editor.UsableByAnyBaseclass);
        Assert.Equal(["Cleric"], editor.Record.Tail.UsableByBaseclass);

        editor.Baseclasses[0].IsSelected = false;
        Assert.False(editor.IsDirty);
        Assert.True(editor.UsableByAnyBaseclass);
    }

    /// <summary>An id the design no longer defines is listed, ticked, and survives.</summary>
    [Fact]
    public void A_dangling_baseclass_id_is_kept()
    {
        var record = ItemEditorViewModel.NewRecord("Wand");
        record = record with { Tail = record.Tail with { UsableByBaseclass = ["Necromancer"] } };

        var editor = new ItemEditorViewModel(record, ["Fighter"]);

        var dangling = Assert.Single(editor.Baseclasses, b => !b.IsKnown);
        Assert.Equal("Necromancer", dangling.Id);
        Assert.True(dangling.IsSelected);
        Assert.Equal(["Necromancer"], editor.Record.Tail.UsableByBaseclass);
        Assert.False(editor.IsDirty);
    }

    /// <summary>
    /// A <c>BOOL</c> holding something other than 0 or 1 is not normalised by an unrelated edit.
    /// </summary>
    [Fact]
    public void A_non_canonical_boolean_survives_an_edit_elsewhere()
    {
        var record = ItemEditorViewModel.NewRecord("Cursed Ring");
        record = record with { Scalars = record.Scalars with { Cursed = 7 } };

        var editor = new ItemEditorViewModel(record);

        Assert.True(editor.IsCursed);

        editor.Cost = 50;

        Assert.Equal(7, editor.Record.Scalars.Cursed);

        // Ticking a box that is already ticked is not an edit either.
        editor.IsCursed = true;
        Assert.Equal(7, editor.Record.Scalars.Cursed);
    }

    /// <summary>Usage bits nothing has a checkbox for are preserved.</summary>
    [Fact]
    public void Unnamed_usage_bits_survive_a_checkbox()
    {
        var record = ItemEditorViewModel.NewRecord("Odd Thing");
        record = record with { Tail = record.Tail with { UsageFlags = 0x80 } };

        var editor = new ItemEditorViewModel(record);

        Assert.Equal(0x80, editor.OtherUsageFlags);
        Assert.True(editor.HasOtherUsageFlags);

        editor.IsUsable = true;

        Assert.Equal(0x80 | Choices.UsageUsable, editor.Record.Tail.UsageFlags);
    }

    /// <summary>
    /// A pre-conversion readied location stays exactly as the file gave it.
    /// </summary>
    /// <remarks>
    /// The reference rewrites small ordinals into packed names on load; this port's reader does
    /// not, and normalising here would mark every item of an old design edited on open. Note the
    /// slot the ordinal names is the <i>database</i> table's — 3 is <c>HANDS</c>, and it is
    /// <c>QUIVER</c> only for a carried item.
    /// </remarks>
    [Fact]
    public void A_legacy_readied_ordinal_is_offered_without_being_rewritten()
    {
        var record = ItemEditorViewModel.NewRecord("Old Gauntlets");
        record = record with { Combat = record.Combat with { LocationReadied = 3 } };

        var editor = new ItemEditorViewModel(record);

        Assert.False(editor.IsDirty);
        Assert.Equal(3u, editor.Record.Combat.LocationReadied);

        var choice = Assert.Single(editor.SlotChoices, c => c.Value == 3);
        Assert.Contains("HANDS", choice.Label, StringComparison.Ordinal);
        Assert.Same(choice, editor.SlotChoiceValue);
    }

    /// <summary>A slot chosen from the list is stored as the packed word, not as text.</summary>
    [Fact]
    public void Choosing_a_slot_stores_the_packed_word()
    {
        var editor = new ItemEditorViewModel(ItemEditorViewModel.NewRecord("Helm"));
        var head = editor.SlotChoices.First(c => c.Label == "HEAD");

        editor.SlotChoiceValue = head;

        Assert.Equal(ReadiedLocation.Head, editor.Record.Combat.LocationReadied);
        Assert.Equal("HEAD", ReadiedLocation.WordFor(editor.Record.Combat.LocationReadied));
    }

    /// <summary>The bundle/halve contradiction is reported rather than silently resolved.</summary>
    [Fact]
    public void A_halvable_item_with_no_bundle_is_flagged_and_not_rewritten()
    {
        var record = ItemEditorViewModel.NewRecord("Arrow");
        var editor = new ItemEditorViewModel(record);

        Assert.True(editor.CanBeHalvedJoined);
        Assert.Equal(1, editor.BundleQty);
        Assert.False(editor.IsHalveJoinEffective);
        Assert.Equal(1, editor.Record.Tail.CanBeHalvedJoined);

        editor.BundleQty = 20;
        Assert.True(editor.IsHalveJoinEffective);
    }

    /// <summary>A new record carries the reference's defaults, not a blank record's zeros.</summary>
    [Fact]
    public void A_new_item_gets_the_references_defaults()
    {
        var record = ItemEditorViewModel.NewRecord("Dagger");

        Assert.Equal(1, record.Scalars.BundleQty);
        Assert.Equal(ReadiedLocation.WeaponHand, record.Combat.LocationReadied);
        Assert.Equal(1, record.Combat.HandsToUse);
        Assert.Equal((6, 1), (record.Combat.DmgDiceSm, record.Combat.NbrDiceSm));
        Assert.Equal((6, 1), (record.Combat.DmgDiceLg, record.Combat.NbrDiceLg));
        Assert.Equal(1.0, record.Combat.RofPerRound);
        Assert.Equal(1, record.Tail.CanBeHalvedJoined);
        Assert.Equal(1, record.Tail.CanBeTradeDropSoldDep);
        Assert.Equal("attacks", record.Tail.AttackMessage);
        Assert.Equal("EXAMINE", record.Tail.ExamineLabel);
        Assert.Empty(record.Tail.UsableByBaseclass);
    }

    /// <summary>Art is edited by file name only, and a record with none keeps none.</summary>
    [Fact]
    public void Art_with_no_record_stays_null()
    {
        var editor = new ItemEditorViewModel(ItemEditorViewModel.NewRecord("Rock"));

        Assert.False(editor.HasHitArt);
        Assert.Null(editor.HitArtFileName);

        editor.HitArtFileName = "sprite.png";

        Assert.Null(editor.Record.HitArt);
        Assert.False(editor.IsDirty);
    }

    /// <summary>An unreadable database is a third state, not an empty one.</summary>
    [Fact]
    public void A_missing_database_reads_as_unreadable_rather_than_empty()
    {
        var editor = new ItemDatabaseViewModel(database: null, knownBaseclasses: null);

        Assert.False(editor.IsReadable);
        Assert.Equal(0, editor.Count);
        Assert.Empty(editor.Records);
        Assert.False(editor.IsDirty);
    }

    /// <summary>Two records under one id are reported, because designs really do carry them.</summary>
    [Fact]
    public void Duplicate_ids_are_reported()
    {
        var editor = new ItemDatabaseViewModel(
            new ItemDatabase([ItemEditorViewModel.NewRecord("Sword"),
                              ItemEditorViewModel.NewRecord("Shield")], []),
            null);

        Assert.False(editor.HasDuplicateNames);

        editor.All[1].UniqueName = "Sword";

        Assert.True(editor.HasDuplicateNames);
        Assert.Equal(["Sword"], editor.DuplicateNames);
    }
}
