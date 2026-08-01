using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the runtime party, the world state, and the trigger conditions they answer.
/// </summary>
/// <remarks>
/// The characters here are minimal records rather than ones read from a design: what is being
/// tested is which field each condition consults, and a real 200-field <c>CHARACTER</c> makes that
/// harder to see rather than easier. The readers are proven against real designs elsewhere.
/// </remarks>
public class PartyTests
{
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static readonly ItemList NoItems = new([], new ReadyItems([]));

    /// <summary>A character with only the fields the trigger conditions read.</summary>
    private static CharacterRecord Member(string name, string race = "human",
                                          string classId = "fighter", Gender gender = Gender.Male,
                                          string characterId = "", int hitPoints = 10,
                                          string[]? baseclasses = null,
                                          ItemInstance[]? items = null)
    {
        var stats = (baseclasses ?? ["fighter"])
            .Select(b => new BaseclassStats(b, 0, 0, 0, 0))
            .ToList();

        var carried = items is null ? NoItems : new ItemList(items, new ReadyItems([]));

        return new CharacterRecord(
            0, 0, race, (int)gender, classId, 0, 0, 0, "", 0, name, characterId,
            0, 0, 0, 0, 0, hitPoints, hitPoints, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, stats, [], [], 0, 0, 0, null, 0,
            null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, carried,
            new SpecabBlock([], [], []), []);
    }

    private static Party Roster(params CharacterRecord[] members)
    {
        var party = new Party();
        foreach (var member in members)
        {
            party.Add(new Character(member, UAF.Rules.MoneyRules.Default));
        }

        return party;
    }

    private static EventControl Control(EventTriggerType type, string itemId = "",
                                        string raceId = "", string classId = "",
                                        string characterId = "", int quest = 0,
                                        int partyX = 0, params AslEntry[] attributes) =>
        new(0, 0, 0, 0, (int)type, itemId, quest, 0, 0, raceId, classId, characterId,
            attributes, "", 0, partyX, 0, "", 0, 0);

    // ---- the party ---------------------------------------------------------------------------

    [Fact]
    public void Daylight_runs_from_six_to_eighteen_inclusive_at_both_ends()
    {
        // hours >= 6 && hours <= 18 (Party.cpp:1163), so the hour beginning at 18:00 is still day
        // and daylight is thirteen hours. An exclusive upper bound is the natural guess.
        Assert.False(Party.InDaytime(5));
        Assert.True(Party.InDaytime(6));
        Assert.True(Party.InDaytime(18));
        Assert.False(Party.InDaytime(19));
        Assert.False(Party.InDaytime(0));
    }

    [Fact]
    public void A_party_holds_at_most_twelve_members()
    {
        var party = new Party();
        for (int i = 0; i < 20; i++)
        {
            party.Add(new Character(Member($"M{i}"), UAF.Rules.MoneyRules.Default));
        }

        Assert.Equal(Party.MaxMembers, party.Count);
        Assert.Equal(12, Party.MaxMembers);
    }

    [Fact]
    public void Composition_questions_read_the_field_each_one_names()
    {
        var party = Roster(
            Member("Alia", race: "elf", classId: "mage", gender: Gender.Female),
            Member("Borin", race: "dwarf", classId: "cleric", characterId: "npc_borin",
                   baseclasses: ["cleric", "fighter"]));

        Assert.True(party.HasRace("elf"));
        Assert.False(party.HasRace("halfling"));

        Assert.True(party.HasClass("mage"));
        Assert.False(party.HasClass("thief"));

        // Baseclasses come from the multiclass list, not from ClassId -- so a cleric/fighter
        // answers to "fighter" as a baseclass while its class is "cleric".
        Assert.True(party.HasBaseclass("fighter"));
        Assert.False(party.HasClass("fighter"));

        Assert.True(party.HasCharacter("npc_borin"));
        Assert.False(party.HasCharacter("npc_someone_else"));

        Assert.True(party.HasGender(Gender.Female));
        Assert.True(party.HasGender(Gender.Male));
        Assert.False(party.HasGender(Gender.Bishop));
    }

    [Fact]
    public void An_empty_operand_matches_nothing_rather_than_everything()
    {
        // A condition whose id was never set must not fire against the first member -- which is
        // what a plain string comparison against "" would do for a character with no NPC id.
        var party = Roster(Member("Alia", characterId: ""));

        Assert.False(party.HasCharacter(""));
        Assert.False(party.HasRace(""));
        Assert.False(party.HasItem(""));
    }

    [Fact]
    public void Item_possession_looks_across_the_whole_party()
    {
        var party = Roster(
            Member("Alia"),
            Member("Borin", items: [new ItemInstance(0, "Rope", 0, 0, 1, 1, 0, 0, 0)]));

        Assert.True(party.HasItem("Rope"));
        Assert.True(party.HasItem("rope"));      // ITEM_ID comparisons are not case-sensitive
        Assert.False(party.HasItem("Ladder"));
    }

    // ---- the world ---------------------------------------------------------------------------

    private static WorldState World(params (int Id, QuestState State, int Stage)[] quests) =>
        WorldState.FromDesign(
            [.. quests.Select(q => new Quest($"Q{q.Id}", (int)q.State, (ushort)q.Stage, q.Id, []))],
            [new SpecialObject("Amulet", 7, 0, 0, "", 0, [])],
            [new SpecialObject("Brass key", 3, 2, 0, "", 0, [])]);

    [Fact]
    public void A_quest_is_present_once_it_is_anything_but_not_started()
    {
        var world = World((1, QuestState.NotStarted, 0), (2, QuestState.InProgress, 1),
                          (3, QuestState.Complete, 9), (4, QuestState.Failed, 5));

        Assert.False(world.IsQuestPresent(1));
        Assert.True(world.IsQuestPresent(2));

        // Present is not the same as unfinished: a completed or failed quest is still present.
        Assert.True(world.IsQuestPresent(3));
        Assert.True(world.IsQuestPresent(4));

        Assert.True(world.IsQuestComplete(3));
        Assert.True(world.IsQuestFailed(4));
        Assert.True(world.IsQuestInProgress(2));
    }

    [Fact]
    public void A_quest_the_design_does_not_define_answers_false_to_everything()
    {
        var world = World();

        Assert.False(world.IsQuestPresent(99));
        Assert.False(world.IsQuestComplete(99));
        Assert.False(world.QuestStageEquals(99, 0));
    }

    [Fact]
    public void Special_items_and_keys_are_held_when_their_stage_is_above_zero()
    {
        // hasSpecialItem is GetStage(item) > 0 -- the stage doubles as the possession flag, so
        // stage 0 means "not held" rather than "held, at stage zero".
        var world = World();

        Assert.False(world.HasSpecialItem(7));       // stage 0
        Assert.True(world.HasKey(3));                // stage 2

        world.SetSpecialItemStage(7, 1);
        Assert.True(world.HasSpecialItem(7));
    }

    // ---- the wiring --------------------------------------------------------------------------

    [Fact]
    public void Conditions_return_unknown_only_when_there_is_no_state_to_read()
    {
        // Without a party and world the answer is honestly unknown, which is what keeps a design
        // from looking empty. With them, the same condition gives a verdict.
        var control = Control(EventTriggerType.ClassInParty, classId: "mage");

        Assert.Equal(TriggerResult.Unknown, EventTrigger.Evaluate(control, 0, 0));
        Assert.Equal(TriggerResult.Fire,
                     EventTrigger.Evaluate(control, 0, 0,
                                           party: Roster(Member("A", classId: "mage")),
                                           world: World()));
    }

    [Fact]
    public void Only_the_spellbook_and_script_conditions_remain_unanswerable()
    {
        var party = Roster(Member("A"));
        var world = World();

        foreach (var type in Enum.GetValues<EventTriggerType>())
        {
            var verdict = EventTrigger.Evaluate(Control(type), 0, 0, party: party, world: world);
            bool answerable = type is not (EventTriggerType.SpellMemorized
                                           or EventTriggerType.ExecuteGpdl);

            Assert.Equal(answerable, verdict != TriggerResult.Unknown);
            Assert.Equal(answerable, EventTrigger.IsEvaluable(type));
        }
    }

    [Fact]
    public void The_two_searching_conditions_are_not_mirror_images()
    {
        // PartySearching ORs in `looking`; PartyNotSearching negates PartyIsSearching() alone and
        // ignores it. So a party that is looking but not searching satisfies BOTH at once --
        // transcribed, because a design may well have been written around it.
        var party = Roster(Member("A"));
        party.Searching = false;
        party.Looking = true;

        var world = World();

        Assert.Equal(TriggerResult.Fire,
                     EventTrigger.Evaluate(Control(EventTriggerType.PartySearching), 0, 0,
                                           party: party, world: world));
        Assert.Equal(TriggerResult.Fire,
                     EventTrigger.Evaluate(Control(EventTriggerType.PartyNotSearching), 0, 0,
                                           party: party, world: world));
    }

    [Fact]
    public void The_quest_stage_condition_reads_the_stage_out_of_partyX()
    {
        // GameEvent.cpp:1017 passes partyX straight to StageEqual. The field is a coordinate on
        // every other condition, so modelling it only as one compares against the wrong number.
        var world = World((5, QuestState.InProgress, 3));
        var party = Roster(Member("A"));

        var atThree = Control(EventTriggerType.QuestStageEqual, quest: 5, partyX: 3);
        var atFour = Control(EventTriggerType.QuestStageEqual, quest: 5, partyX: 4);

        Assert.Equal(TriggerResult.Fire,
                     EventTrigger.Evaluate(atThree, 0, 0, party: party, world: world));
        Assert.Equal(TriggerResult.Suppress,
                     EventTrigger.Evaluate(atFour, 0, 0, party: party, world: world));

        Assert.Equal(TriggerResult.Suppress,
                     EventTrigger.Evaluate(Control(EventTriggerType.QuestStageNotEqual,
                                                   quest: 5, partyX: 3),
                                           0, 0, party: party, world: world));
    }

    [Fact]
    public void Gender_and_the_two_object_ids_come_out_of_the_attribute_map()
    {
        // PreSerialize moves them into eventcontrol_asl as "Gen", "SpIt" and "SpKy"
        // (GameEvent.cpp:1323), so they are attributes rather than fields on the record.
        var party = Roster(Member("A", gender: Gender.Female));
        var world = World();

        var female = Control(EventTriggerType.GenderInParty,
                             attributes: new AslEntry("Gen", 0, "1"));
        Assert.Equal(TriggerResult.Fire,
                     EventTrigger.Evaluate(female, 0, 0, party: party, world: world));

        var male = Control(EventTriggerType.GenderInParty,
                           attributes: new AslEntry("Gen", 0, "0"));
        Assert.Equal(TriggerResult.Suppress,
                     EventTrigger.Evaluate(male, 0, 0, party: party, world: world));

        // The key with stage 2 is held; the special item with stage 0 is not.
        var hasKey = Control(EventTriggerType.PartyHaveSpecialKey,
                             attributes: new AslEntry("SpKy", 0, "3"));
        Assert.Equal(TriggerResult.Fire,
                     EventTrigger.Evaluate(hasKey, 0, 0, party: party, world: world));

        var hasItem = Control(EventTriggerType.PartyHaveSpecialItem,
                              attributes: new AslEntry("SpIt", 0, "7"));
        Assert.Equal(TriggerResult.Suppress,
                     EventTrigger.Evaluate(hasItem, 0, 0, party: party, world: world));
    }

    [Fact]
    public void A_missing_attribute_reads_as_zero_the_way_atoi_does()
    {
        // RetrieveIntFromASL is atoi(Lookup(key)), which is 0 for a missing key -- so an event
        // whose gender was never authored asks about Male rather than failing.
        var control = Control(EventTriggerType.GenderInParty);

        Assert.Equal(0, EventTrigger.AslInt(control, EventTrigger.GenderKey));
        Assert.Equal(TriggerResult.Fire,
                     EventTrigger.Evaluate(control, 0, 0,
                                           party: Roster(Member("A", gender: Gender.Male)),
                                           world: World()));
    }

    [Fact]
    public void The_clock_decides_the_daytime_conditions()
    {
        var party = Roster(Member("A"));
        var world = World();

        Assert.Equal(TriggerResult.Fire,
                     EventTrigger.Evaluate(Control(EventTriggerType.Daytime), 0, 0,
                                           party: party, world: world, hours: 12));
        Assert.Equal(TriggerResult.Fire,
                     EventTrigger.Evaluate(Control(EventTriggerType.Nighttime), 0, 0,
                                           party: party, world: world, hours: 23));
    }

    // ---- experience ----------------------------------------------------------------------------

    private static Character Adventurer(params string[] baseclasses) =>
        new(Member("A", baseclasses: baseclasses), UAF.Rules.MoneyRules.Default);

    [Fact]
    public void Experience_splits_across_baseclasses_and_rounds_the_share_up()
    {
        // curExp = (points + n - 1) / n, and EACH baseclass gets that full share -- so a multiclass
        // character gains more in total than was awarded. 100 across 3 is 34 each, 102 in all.
        var single = Adventurer("fighter");
        Assert.Equal(100, single.GiveExperience(100));
        Assert.Equal(100, single.TotalExperience);

        var triple = Adventurer("fighter", "cleric", "mage");
        Assert.Equal(102, triple.GiveExperience(100));
        Assert.All(triple.Baseclasses, b => Assert.Equal(34, b.Experience));
    }

    [Fact]
    public void A_drained_baseclass_gains_nothing_while_the_others_still_do()
    {
        // IncCurExperience refuses when previousLevel > 0 -- the level-drain marker.
        var character = Adventurer("fighter", "cleric");
        character.Baseclasses[0].PreviousLevel = 3;

        Assert.Equal(50, character.GiveExperience(100));
        Assert.Equal(0, character.Baseclasses[0].Experience);
        Assert.Equal(50, character.Baseclasses[1].Experience);
    }

    [Fact]
    public void An_award_of_zero_does_nothing_at_all()
    {
        var character = Adventurer("fighter");
        Assert.Equal(0, character.GiveExperience(0));
        Assert.Equal(0, character.TotalExperience);
    }

    [Fact]
    public void A_characters_mutable_state_starts_from_its_record_without_changing_it()
    {
        var record = Member("Alia", hitPoints: 12,
                            items: [new ItemInstance(0, "Rope", 0, 0, 1, 1, 0, 0, 0)]);
        var character = new Character(record, UAF.Rules.MoneyRules.Default);

        Assert.Equal(12, character.HitPoints);
        Assert.Single(character.Items);

        character.HitPoints = 3;
        character.Items.Clear();
        character.GiveExperience(75);

        // The record stays the snapshot of the file it was read from.
        Assert.Equal(12, record.HitPoints);
        Assert.Single(record.Items.Items);
        Assert.Equal(0, record.BaseclassStats[0].Experience);
        Assert.Equal(75, character.TotalExperience);
    }

    [Fact]
    public void A_treasure_pickup_answers_the_party_have_item_condition()
    {
        // Items land in the party list rather than a character's inventory today, and HasItem
        // searches both -- otherwise a design gating on "do you have the key" never fires.
        var party = Roster(Member("A"));
        Assert.False(party.HasItem("Brass Key"));

        party.Carried.Add(new ItemInstance(0, "Brass Key", 0, 0, 1, 1, 0, 0, 0));
        Assert.True(party.HasItem("Brass Key"));
    }
}
