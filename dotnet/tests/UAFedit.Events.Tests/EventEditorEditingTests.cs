using UAF.Serialization;
using UAFcore;

namespace UAFedit.Events.Tests;

/// <summary>
/// Editing, dirtiness and the edited collection, on data this file owns.
/// </summary>
/// <remarks>
/// No corpus and no window. The view models are driven exactly as the XAML drives them — set a
/// property, read the collection back — so these cover the path a user takes without needing a
/// display.
/// </remarks>
public class EventEditorEditingTests
{
    private static EventEditorViewModel Editor() =>
        new(EventFixture.Level(
                EventFixture.Text(1, "You are in a dark room.", chainHappen: 2),
                EventFixture.Question(2, "Which way?", ("North", 3), ("South", 1)),
                EventFixture.Text(3, "A dead end.")),
            "test");

    [Fact]
    public void A_level_lists_its_events_with_ids_types_and_positions()
    {
        var editor = Editor();

        Assert.Equal(3, editor.Events.Count);
        Assert.Equal([1u, 2u, 3u], editor.Events.Select(e => e.Id));
        Assert.Equal("Text Statement", editor.Events[0].TypeName);
        Assert.Equal("Question List", editor.Events[1].TypeName);
        Assert.Equal("0,0", editor.Events[0].Position);
        Assert.Equal("You are in a dark room.", editor.Events[0].Summary);
    }

    /// <summary>
    /// A question with no text of its own summarises from its heading, not from nothing.
    /// </summary>
    [Fact]
    public void An_event_without_text_summarises_from_its_own_fields()
    {
        var editor = Editor();

        Assert.Equal("Which way?", editor.Events[1].Summary);
    }

    [Fact]
    public void Editing_a_text_field_marks_the_level_dirty_and_shows_in_the_edited_collection()
    {
        var editor = Editor();
        editor.SelectedEvent = editor.Events[0];

        var text = editor.TextFields.OfType<EventTextFieldViewModel>()
                                    .First(f => f.Label == "Text");

        Assert.False(editor.IsDirty);

        text.Value = "You are in a bright room.";

        Assert.True(editor.IsDirty);
        Assert.True(editor.Events[0].IsModified);
        Assert.Equal("You are in a bright room.", editor.EditedEvents[0].Base.Text);
        Assert.Single(editor.ChangedEvents);

        // The row's own summary follows the record, so the list updates with the edit.
        Assert.Equal("You are in a bright room.", editor.Events[0].Summary);
    }

    /// <summary>
    /// An edit undone leaves the level clean.
    /// </summary>
    /// <remarks>
    /// Records compare structurally, and a <c>with</c> expression shares the list references it did
    /// not touch — so equality with the loaded record is the honest dirtiness test rather than a
    /// sticky flag. This is the test that would fail if anything started mutating a list in place.
    /// </remarks>
    [Fact]
    public void Undoing_an_edit_makes_the_level_clean_again()
    {
        var editor = Editor();
        editor.SelectedEvent = editor.Events[0];

        var text = editor.TextFields.OfType<EventTextFieldViewModel>()
                                    .First(f => f.Label == "Text");

        string startedAs = text.Value;
        text.Value = "something else";
        Assert.True(editor.IsDirty);

        text.Value = startedAs;

        Assert.False(editor.IsDirty);
        Assert.False(editor.Events[0].IsModified);
    }

    [Fact]
    public void A_flag_field_writes_through_to_the_record()
    {
        var editor = Editor();
        editor.SelectedEvent = editor.Events[0];

        var once = editor.ControlFields.OfType<EventFlagFieldViewModel>()
                                       .First(f => f.Label == "Once Only");

        Assert.False(once.IsChecked);

        once.IsChecked = true;

        Assert.True(once.IsChecked);
        Assert.Equal(1, editor.EditedEvents[0].Base.Control.OnceOnly);
    }

    [Fact]
    public void A_choice_field_offers_the_editors_own_labels()
    {
        var editor = Editor();
        editor.SelectedEvent = editor.Events[0];

        var trigger = editor.ControlFields.OfType<EventChoiceFieldViewModel>()
                                          .First(f => f.Label == "Event Trigger");

        Assert.Equal("Always", trigger.Selected!.Label);
        Assert.Contains(trigger.Choices, c => c.Label == "Party has spell memorized");

        trigger.Selected = trigger.Choices.First(c => c.Value == (int)EventTriggerType.RandomChance);

        Assert.Equal((int)EventTriggerType.RandomChance,
                     editor.EditedEvents[0].Base.Control.EventTrigger);
    }

    /// <summary>
    /// Type-specific fields write into the concrete record, not the header.
    /// </summary>
    [Fact]
    public void A_type_specific_field_writes_into_its_own_record()
    {
        var editor = Editor();
        editor.SelectedEvent = editor.Events[0];

        var backup = editor.DetailFields.OfType<EventFlagFieldViewModel>()
                                        .First(f => f.Label == "Backup party one step");

        backup.IsChecked = true;

        var edited = Assert.IsType<TextEvent>(editor.EditedEvents[0]);
        Assert.Equal(1, edited.ForceBackup);
    }

    /// <summary>
    /// A nested block's rows edit the collection they came from.
    /// </summary>
    /// <remarks>
    /// The question options are the case that matters: they carry the chains, so an editor that
    /// could not change them could not rewire a conversation.
    /// </remarks>
    [Fact]
    public void A_grouped_field_edits_the_collection_it_came_from()
    {
        var editor = Editor();
        editor.SelectedEvent = editor.Events[1];

        var group = editor.DetailGroups.First(g => g.Label == "Button 1");
        var label = group.Fields.OfType<EventTextFieldViewModel>().First(f => f.Label == "Label");

        label.Value = "Northwest";

        var edited = Assert.IsType<QuestionEvent>(editor.EditedEvents[1]);
        Assert.Equal("Northwest", edited.Options[0].Label);
        Assert.Equal("South", edited.Options[1].Label);
    }

    /// <summary>
    /// Text that does not parse as a number leaves the record alone.
    /// </summary>
    /// <remarks>
    /// The field holds no state of its own, so a rejected write shows as the box reverting to what
    /// the record still says rather than as a value nothing accepted.
    /// </remarks>
    [Fact]
    public void An_unparseable_number_does_not_change_the_record()
    {
        var editor = Editor();
        editor.SelectedEvent = editor.Events[0];

        var chance = editor.TriggerFields.OfType<EventTextFieldViewModel>()
                                         .First(f => f.Label == "Chance %");

        chance.Value = "not a number";

        Assert.Equal("100", chance.Value);
        Assert.False(editor.IsDirty);
    }

    /// <summary>A number outside the stored width is clamped rather than truncated.</summary>
    [Fact]
    public void A_number_is_clamped_to_the_range_its_field_allows()
    {
        var editor = Editor();
        editor.SelectedEvent = editor.Events[0];

        var chance = editor.TriggerFields.OfType<EventTextFieldViewModel>()
                                         .First(f => f.Label == "Chance %");

        chance.Value = "5000";

        Assert.Equal("100", chance.Value);
        Assert.Equal(100, editor.EditedEvents[0].Base.Control.Chance);
    }

    /// <summary>
    /// The trigger operands grey out to match the selected trigger.
    /// </summary>
    /// <remarks>
    /// The original hides them <i>and clears them</i> (<c>SetControlStates</c>,
    /// <c>EventViewer.cpp:2967</c>); this greys them and keeps the value, so the assertion is that
    /// relevance moved and the stored item id did not.
    /// </remarks>
    [Fact]
    public void Changing_the_trigger_moves_relevance_without_clearing_anything()
    {
        var editor = Editor();
        editor.SelectedEvent = editor.Events[0];

        var item = editor.TriggerFields.OfType<EventTextFieldViewModel>()
                                       .First(f => f.Label == "Item");
        item.Value = "Sword +1";

        var quest = editor.TriggerFields.First(f => f.Label == "Quest");
        Assert.False(quest.IsRelevant);

        var trigger = editor.ControlFields.OfType<EventChoiceFieldViewModel>()
                                          .First(f => f.Label == "Event Trigger");
        trigger.Selected = trigger.Choices.First(c => c.Value == (int)EventTriggerType.QuestComplete);

        Assert.True(quest.IsRelevant);
        Assert.False(editor.TriggerFields.First(f => f.Label == "Item").IsRelevant);
        Assert.Equal("Sword +1", editor.EditedEvents[0].Base.Control.ItemId);
    }

    /// <summary>
    /// The quest stage and party X are one field with two names.
    /// </summary>
    /// <remarks>
    /// The original's DDX map exchanges <c>m_questStage</c> and <c>m_PartyX</c> with each other
    /// (<c>EventViewer.cpp:906</c>), so there is one <c>partyX</c> on the wire holding either. The
    /// editor shows it once, under both names, and this pins that it is genuinely one field.
    /// </remarks>
    [Fact]
    public void Quest_stage_and_party_x_are_the_same_stored_field()
    {
        var editor = Editor();
        editor.SelectedEvent = editor.Events[0];

        var shared = editor.TriggerFields.OfType<EventTextFieldViewModel>()
                                         .First(f => f.Label == "Party X / Quest Stage");

        shared.Value = "65001";

        Assert.Equal(65001, editor.EditedEvents[0].Base.Control.PartyX);
    }
}
