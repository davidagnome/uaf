using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Resolving the numeric database keys a pre-0.998101 event carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>The point of the conversion is that a legacy level becomes writable.</b> Without it
/// <see cref="GameEventWriter"/> refuses every event still carrying a key, so a design below
/// 0.998101 — the editor's own template among them — could be read and never saved, and the
/// reference would offer to convert its level files every time it opened one.
/// </para>
/// <para>
/// <b>Most of these cases exist because the five fields do not share a rule.</b> The first
/// implementation applied one guard to all of them and was wrong for four: the item admits zero,
/// the race has no guard at all, the class and character want strictly positive, and the spell is
/// gated on the event's trigger rather than on its key.
/// </para>
/// </remarks>
public class EventIdUpgradeTests
{
    private static readonly DicePlus NoDice = new("", "", "", 0, 0, 0, 0, 0, 0, []);
    private static readonly SpecabBlock NoSpecabs = new([], [], []);
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private const int SpellMemorized = 28;

    private static ItemRecord Item(int key, string name) =>
        new(new ItemNames(key, "", name, "", "", "", ""),
            HitArt: null, MissileArt: null,
            new ItemScalars("", 0, 0, 0, 0, 0, 0, 0),
            new ItemCombat(ReadiedLocation.WeaponHand, 1, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
            new ItemTail(0, 0, 0, [], 0, 0, 0, "", "", 0, 0, null, 0, 0, NoSpecabs, []));

    private static RaceRecord Race(int key, string name) =>
        new(key, name, NoDice, NoDice, NoDice, NoDice, [], NoDice, 0, 0, 0, 0, 0,
            [], [], [], [], [], [], NoSpecabs);

    private static ClassRecord Class(int key, string name) =>
        new("ClassV1", key, name, [], NoSpecabs, [], NoDice,
            new ItemList([], ReadyItems.Empty), "");

    private static SpellRecord Spell(int key, string name) =>
        new(key, name, string.Empty, string.Empty, [],
            Level: 1, CastingTime: 0, CastingTimeType: 0,
            CanTargetFriend: 1, CanTargetEnemy: 0, IsCumulative: 1, Restrictions: 0,
            CanBeDispelled: 1, CanMemorize: 1, AllowScribe: 0, AutoScribe: 0,
            Lingers: 0, LingerOnceOnly: 0,
            SaveVersus: 0, SaveResult: 0, Targeting: 0,
            DurationRate: 0, CastCost: 0, CastPriority: 0,
            Parameters: [], Effects: [], CastArt: null, Art: [],
            Sounds: [], CastMessage: string.Empty, Scripts: [], EffectDuration: null,
            SpecialAbilities: null!, Attributes: []);

    private static EventIdTables Tables() =>
        new([Item(0, "zeroth item"), Item(7, "a sword")],
            [Race(0, "human"), Race(3, "dwarf")],
            [Class(0, "the zeroth class"), Class(2, "a ranger")],
            [Spell(4, "magic missile")]);

    /// <summary>A control block carrying legacy keys, with everything else neutral.</summary>
    private static EventControl Control(string item = "-1", string race = "0",
                                        string cls = "0", string character = "0",
                                        string spell = "0", int trigger = 0) =>
        new(0, 0, 0, 0, trigger, item, 0, 0, 0, race, cls, character,
            [], "", 0, 0, 0, spell, 0, 0, LegacyIds: true);

    private static EventControl Upgraded(EventControl control) =>
        EventIdUpgrade.Upgrade(control, Tables());

    /// <summary>
    /// An item key of zero is a reference, and a negative one is not.
    /// </summary>
    /// <remarks>
    /// <b>The guard is <c>id &gt;= 0</c> (<c>GameEvent.cpp:1403</c>)</b> — unlike the monster
    /// attack's spell, where -1 is the sentinel and the port tests <c>&gt; 0</c>. Carrying the
    /// monster's rule over here would drop every reference to item zero.
    /// </remarks>
    [Theory]
    [InlineData("0", "zeroth item")]
    [InlineData("7", "a sword")]
    [InlineData("-1", "")]
    [InlineData("99", "")]
    public void An_item_key_resolves_from_zero_upwards(string key, string expected) =>
        Assert.Equal(expected, Upgraded(Control(item: key)).ItemId);

    /// <summary>
    /// A race key is looked up whatever it is.
    /// </summary>
    /// <remarks>
    /// <c>GameEvent.cpp:1430</c> reads it as a <c>DWORD</c> and hands it straight to the finder
    /// with no test at all, so a zero here means race zero rather than "no race".
    /// </remarks>
    [Theory]
    [InlineData("0", "human")]
    [InlineData("3", "dwarf")]
    [InlineData("42", "")]
    public void A_race_key_is_resolved_unguarded(string key, string expected) =>
        Assert.Equal(expected, Upgraded(Control(race: key)).RaceId);

    /// <summary>
    /// A class key must be positive, and falls back to the classic class names.
    /// </summary>
    /// <remarks>
    /// <b>The class is the only one of the five with a fallback</b>: when no class in the database
    /// carries the key, the reference indexes <c>ClassText</c> with it (<c>class.cpp:7187</c>), so
    /// an event gating on the fighter still says so in a design whose class database was rewritten.
    /// Key 2 is a ranger in both the table and the fixture, so a case that resolves and one that
    /// falls back are told apart by key 5.
    /// </remarks>
    [Theory]
    [InlineData("2", "a ranger")]
    [InlineData("5", "Thief")]
    [InlineData("0", "")]
    [InlineData("-1", "")]
    [InlineData("500", "")]
    public void A_class_key_falls_back_to_the_classic_names(string key, string expected) =>
        Assert.Equal(expected, Upgraded(Control(cls: key)).ClassOrBaseclassId);

    /// <summary>
    /// A memorised-spell key counts only under the trigger that reads it.
    /// </summary>
    /// <remarks>
    /// <c>GameEvent.cpp:1507</c> tests <c>eventTrigger == SpellMemorized</c>, not the key. The
    /// same number under any other trigger is not a reference and is dropped — which is why this
    /// is the one field whose rule cannot be read off the value at all.
    /// </remarks>
    [Theory]
    [InlineData(SpellMemorized, "4", "magic missile")]
    [InlineData(SpellMemorized, "9", "")]
    [InlineData(0, "4", "")]
    public void A_spell_key_counts_only_under_its_trigger(int trigger, string key, string expected) =>
        Assert.Equal(expected,
                     Upgraded(Control(spell: key, trigger: trigger)).MemorizedSpellId);

    /// <summary>
    /// A character key this port cannot resolve keeps its digits and stays unwritable.
    /// </summary>
    /// <remarks>
    /// <b>This is the honest half of the conversion.</b> The reference resolves the key through the
    /// design's NPC roster, which the port reads past rather than into, so there is nothing to look
    /// it up in. Blanking the field would silently discard the reference and let the level save as
    /// though nothing were missing; keeping the digits leaves
    /// <see cref="EventControl.LegacyIds"/> set and the writer refusing, which is a failure
    /// somebody can see.
    /// </remarks>
    [Fact]
    public void An_unresolvable_character_key_keeps_the_event_unwritable()
    {
        var upgraded = Upgraded(Control(character: "6"));

        Assert.Equal("6", upgraded.CharacterId);
        Assert.True(upgraded.LegacyIds);
    }

    /// <summary>A character key of zero or less is simply no character.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void A_character_key_at_or_below_zero_clears(string key)
    {
        var upgraded = Upgraded(Control(character: key));

        Assert.Equal(string.Empty, upgraded.CharacterId);
        Assert.False(upgraded.LegacyIds);
    }

    /// <summary>
    /// A key naming nothing empties the field rather than blocking the save.
    /// </summary>
    /// <remarks>
    /// A reference to a record the design does not have is a defect in the <i>design</i>, and the
    /// reference itself carries on with an empty ID after saying so. Refusing to write the level
    /// over one would strand it in a format nothing can open.
    /// </remarks>
    [Fact]
    public void An_unresolved_key_still_clears_the_marker() =>
        Assert.False(Upgraded(Control(item: "99", race: "99", cls: "99")).LegacyIds);

    /// <summary>A control block that never carried keys is returned untouched.</summary>
    [Fact]
    public void A_modern_control_block_is_left_alone()
    {
        var modern = Control(item: "a sword") with { LegacyIds = false };

        Assert.Same(modern, EventIdUpgrade.Upgrade(modern, Tables()));
    }

    /// <summary>
    /// A field holding something that is not a number is left as it stands.
    /// </summary>
    /// <remarks>
    /// The version gate is per design rather than per field, so a design can carry an event whose
    /// field already holds a name. Parsing it as a key and blanking it would lose it.
    /// </remarks>
    [Fact]
    public void A_field_that_is_not_a_number_survives() =>
        Assert.Equal("a sword", Upgraded(Control(item: "a sword")).ItemId);

    /// <summary>
    /// The event body is rebuilt around the new control block, keeping its own fields.
    /// </summary>
    /// <remarks>
    /// <b>Every event type goes through one reflective rebuild</b> rather than sixty-eight hand
    /// written <c>with</c> expressions, so a case that proves the rebuild carries the body's own
    /// fields across is the check that stands in for all of them.
    /// </remarks>
    [Fact]
    public void An_event_body_keeps_its_own_fields_across_the_rebuild()
    {
        var journal = new JournalEvent(Base(Control(item: "7")), Entry: 12);

        var upgraded = Assert.IsType<JournalEvent>(EventIdUpgrade.Upgrade(journal, Tables()));

        Assert.Equal(12, upgraded.Entry);
        Assert.Equal("a sword", upgraded.Base.Control.ItemId);
        Assert.False(upgraded.Base.Control.LegacyIds);
    }

    /// <summary>An event with a list field rebuilds too — the list is carried, not rebuilt.</summary>
    [Fact]
    public void An_event_with_a_list_field_rebuilds()
    {
        var sound = new SoundEvent(Base(Control(race: "3")), ["horn.wav", "drum.wav"]);

        var upgraded = Assert.IsType<SoundEvent>(EventIdUpgrade.Upgrade(sound, Tables()));

        Assert.Equal(["horn.wav", "drum.wav"], upgraded.Sounds);
        Assert.Equal("dwarf", upgraded.Base.Control.RaceId);
    }

    /// <summary>
    /// A level's chain and its body list are upgraded together.
    /// </summary>
    /// <remarks>
    /// <see cref="LevelFile.Events"/> is <see cref="LevelFile.Entries"/> with the bodyless tags
    /// dropped, and both hold the same instances. Replacing only one would leave the writer
    /// emitting the originals — the level would look converted and save unconverted.
    /// </remarks>
    [Fact]
    public void A_level_upgrades_its_chain_and_its_bodies_together()
    {
        var body = new JournalEvent(Base(Control(item: "7")), Entry: 1);
        var level = Level([body], [new LevelEventEntry(EventType.JournalEvent, body),
                                   new LevelEventEntry(EventType.JournalEvent, null)]);

        var upgraded = EventIdUpgrade.Upgrade(level, Tables());

        Assert.Equal("a sword", upgraded.Events[0].Base.Control.ItemId);
        Assert.Same(upgraded.Events[0], upgraded.Entries[0].Body);
        Assert.Null(upgraded.Entries[1].Body);
    }

    /// <summary>A level with nothing to convert is the same object.</summary>
    [Fact]
    public void A_level_with_no_legacy_keys_is_returned_as_it_is()
    {
        var body = new JournalEvent(
            Base(Control(item: "7") with { LegacyIds = false }), Entry: 1);
        var level = Level([body], [new LevelEventEntry(EventType.JournalEvent, body)]);

        Assert.Same(level, EventIdUpgrade.Upgrade(level, Tables()));
    }

    private static GameEventBase Base(EventControl control) =>
        new(control, NoPic, NoPic, 0, 0, 0, 0, 0, 0, "", "", "", []);

    private static LevelFile Level(IReadOnlyList<IGameEvent> events,
                                   IReadOnlyList<LevelEventEntry> entries) =>
        new(DesignVersion.V0915, 1, 1, [], Level: 0,
            EventCount: events.Count, events, entries,
            new ZoneData([], ""), [], [], [], [], []);
}
