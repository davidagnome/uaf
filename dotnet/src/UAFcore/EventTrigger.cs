using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The condition under which an event fires (<c>eventTriggerType</c>,
/// <c>Shared/GameEvent.h:278</c>).
/// </summary>
/// <remarks>
/// Ordinal values, and the order is the header's — the enum is stored as an int and a
/// renumbering would silently repoint every design's conditions. Note it is not grouped tidily:
/// <c>FacingDirectionAnyTime</c> sits between the gender and NPC pairs rather than beside
/// <c>FacingDirection</c>, because entries were appended as they were invented.
/// </remarks>
public enum EventTriggerType
{
    Always = 0,
    PartyHaveItem = 1, PartyNotHaveItem = 2,
    Daytime = 3, Nighttime = 4,
    RandomChance = 5,
    PartySearching = 6, PartyNotSearching = 7,
    FacingDirection = 8,
    QuestComplete = 9, QuestFailed = 10, QuestInProgress = 11,
    PartyDetectingTraps = 12, PartyNotDetectingTraps = 13,
    PartySeeInvisible = 14, PartyNotSeeInvisible = 15,
    ClassInParty = 16, ClassNotInParty = 17,
    RaceInParty = 18, RaceNotInParty = 19,
    QuestPresent = 20, QuestNotPresent = 21,
    GenderInParty = 22, GenderNotInParty = 23,
    FacingDirectionAnyTime = 24,
    NpcInParty = 25, NpcNotInParty = 26,
    ExecuteGpdl = 27,
    SpellMemorized = 28,
    PartyAtXy = 29,
    BaseclassInParty = 30, BaseclassNotInParty = 31,
    PartyHaveSpecialItem = 32, PartyNotHaveSpecialItem = 33,
    PartyHaveSpecialKey = 34, PartyNotHaveSpecialKey = 35,
    QuestStageEqual = 36, QuestStageNotEqual = 37,
}

/// <summary>
/// The set of party facings an event's facing condition accepts
/// (<c>eventFacingType</c>, <c>Shared/GameEvent.h:76</c>).
/// </summary>
/// <remarks>
/// <para>
/// Ordinal values, and each name spells its own set: <see cref="N_S_E"/> accepts a party facing
/// north, south or east. They are compared with <c>==</c> rather than masked, so this is an
/// enumeration of the fifteen useful combinations and not a bit field — treating it as flags would
/// make <c>North | South</c> equal <see cref="East"/>.
/// </para>
/// <para>
/// <see cref="InFront"/> carries the original's own comment that it "shouldn't happen", and is
/// accepted unconditionally alongside <see cref="Any"/>.
/// </para>
/// </remarks>
public enum EventFacing
{
    Any = 0,
    North = 1, South = 2, East = 3, West = 4,
    N_S = 5, N_E = 6, N_W = 7, S_E = 8, S_W = 9, E_W = 10,
    N_S_E = 11, N_S_W = 12, N_W_E = 13, W_S_E = 14,
    InFront = 15,
}

/// <summary>How a trigger evaluated.</summary>
public enum TriggerResult
{
    /// <summary>The condition holds; the event fires.</summary>
    Fire,

    /// <summary>The condition does not hold; the event stays silent.</summary>
    Suppress,

    /// <summary>
    /// The condition cannot be evaluated yet, because the state it asks about is unported.
    /// </summary>
    /// <remarks>
    /// Deliberately distinct from <see cref="Suppress"/>. Most conditions ask about party
    /// inventory, quests or class composition, none of which exist yet — and treating "I cannot
    /// tell" as "no" would make a design look as though it had no content, which is exactly the
    /// wrong impression while the engine is being built out.
    /// </remarks>
    Unknown,
}

/// <summary>
/// Decides whether an event fires, from <c>EVENT_CONTROL::EventShouldTrigger</c>
/// (<c>Shared/GameEvent.cpp:757</c>).
/// </summary>
/// <remarks>
/// <para>
/// All but three conditions are answered. <see cref="EventTriggerType.SpellMemorized"/> needs a
/// character's spellbook consulted by class and level, and <see cref="EventTriggerType.ExecuteGpdl"/>
/// needs the scripting VM attached to a live game; both return <see cref="TriggerResult.Unknown"/>
/// rather than a guess.
/// </para>
/// <para>
/// <b>Three of the operands are ASL attributes, not fields.</b> <c>gender</c>,
/// <c>specialItem</c> and <c>specialKey</c> are moved into <c>eventcontrol_asl</c> under the keys
/// <c>"Gen"</c>, <c>"SpIt"</c> and <c>"SpKy"</c> before writing and pulled back out after reading
/// (<c>PreSerialize</c>/<c>PostSerialize</c>, <c>GameEvent.cpp:1318</c>). They are therefore in
/// <see cref="EventControl.Attributes"/> rather than on the record, and are read back with
/// <c>atoi</c> — which yields 0 for a missing or non-numeric value rather than failing.
/// </para>
/// <para>
/// <b>Both facing forms compare the stored direction against the party's.</b> Only
/// <see cref="EventFacing.Any"/> and <see cref="EventFacing.InFront"/> fire unconditionally
/// (<c>GameEvent.cpp:918</c>); everything else is a four-way switch on the party's facing against
/// a list of the combinations that include it. The two trigger types are handled by one
/// <c>case</c> pair and behave identically here — what distinguishes
/// <c>FacingDirectionAnyTime</c> is when the scheduler consults it, not what it answers.
/// </para>
/// </remarks>
public static class EventTrigger
{
    /// <summary>
    /// Evaluates an event's trigger condition.
    /// </summary>
    /// <param name="roll">
    /// Supplies the 1–100 roll for <see cref="EventTriggerType.RandomChance"/>. Injected so a test
    /// can pin the outcome; the original calls <c>RollDice(100, 1, 0)</c>.
    /// </param>
    public static TriggerResult Evaluate(EventControl control, int partyX, int partyY,
                                         Facing facing = Facing.North, Func<int>? roll = null,
                                         Party? party = null, WorldState? world = null,
                                         int hours = 12)
    {
        ArgumentNullException.ThrowIfNull(control);

        var type = (EventTriggerType)control.EventTrigger;

        // The conditions needing neither a party nor a world are answered first, so a caller with
        // no game state -- a test, or the editor -- still gets a real verdict from them.
        switch (type)
        {
            case EventTriggerType.Always:
                return TriggerResult.Fire;

            // RollDice(100, 1, 0) <= chance, so a chance of 100 always fires and 0 never does.
            case EventTriggerType.RandomChance:
                return Verdict((roll ?? DefaultRoll)() <= control.Chance);

            case EventTriggerType.FacingDirection:
            case EventTriggerType.FacingDirectionAnyTime:
                return Verdict(Accepts((EventFacing)control.Facing, facing));

            case EventTriggerType.PartyAtXy:
                return Verdict(partyX == control.PartyX && partyY == control.PartyY);
        }

        if (party is null || world is null)
        {
            return TriggerResult.Unknown;
        }

        return type switch
        {
            EventTriggerType.PartyHaveItem => Verdict(party.HasItem(control.ItemId)),
            EventTriggerType.PartyNotHaveItem => Verdict(!party.HasItem(control.ItemId)),

            EventTriggerType.Daytime => Verdict(Party.InDaytime(hours)),
            EventTriggerType.Nighttime => Verdict(!Party.InDaytime(hours)),

            // The two searching forms are NOT mirror images -- see Party.Looking.
            EventTriggerType.PartySearching => Verdict(party.Searching || party.Looking),
            EventTriggerType.PartyNotSearching => Verdict(!party.Searching),

            EventTriggerType.PartyDetectingTraps => Verdict(party.DetectingTraps),
            EventTriggerType.PartyNotDetectingTraps => Verdict(!party.DetectingTraps),

            EventTriggerType.PartySeeInvisible => Verdict(party.DetectingInvisible),
            EventTriggerType.PartyNotSeeInvisible => Verdict(!party.DetectingInvisible),

            EventTriggerType.ClassInParty => Verdict(party.HasClass(control.ClassOrBaseclassId)),
            EventTriggerType.ClassNotInParty => Verdict(!party.HasClass(control.ClassOrBaseclassId)),

            EventTriggerType.BaseclassInParty =>
                Verdict(party.HasBaseclass(control.ClassOrBaseclassId)),
            EventTriggerType.BaseclassNotInParty =>
                Verdict(!party.HasBaseclass(control.ClassOrBaseclassId)),

            EventTriggerType.RaceInParty => Verdict(party.HasRace(control.RaceId)),
            EventTriggerType.RaceNotInParty => Verdict(!party.HasRace(control.RaceId)),

            EventTriggerType.NpcInParty => Verdict(party.HasCharacter(control.CharacterId)),
            EventTriggerType.NpcNotInParty => Verdict(!party.HasCharacter(control.CharacterId)),

            EventTriggerType.GenderInParty => Verdict(party.HasGender(GenderOf(control))),
            EventTriggerType.GenderNotInParty => Verdict(!party.HasGender(GenderOf(control))),

            EventTriggerType.QuestPresent => Verdict(world.IsQuestPresent(control.Quest)),
            EventTriggerType.QuestNotPresent => Verdict(!world.IsQuestPresent(control.Quest)),
            EventTriggerType.QuestComplete => Verdict(world.IsQuestComplete(control.Quest)),
            EventTriggerType.QuestFailed => Verdict(world.IsQuestFailed(control.Quest)),
            EventTriggerType.QuestInProgress => Verdict(world.IsQuestInProgress(control.Quest)),

            // partyX doubles as the stage number here -- GameEvent.cpp:1017 passes it straight to
            // StageEqual. The field is not repurposed on any other condition, so a reader that
            // models it only as a coordinate silently compares against the wrong number.
            EventTriggerType.QuestStageEqual =>
                Verdict(world.QuestStageEquals(control.Quest, control.PartyX)),
            EventTriggerType.QuestStageNotEqual =>
                Verdict(!world.QuestStageEquals(control.Quest, control.PartyX)),

            EventTriggerType.PartyHaveSpecialItem =>
                Verdict(world.HasSpecialItem(AslInt(control, SpecialItemKey))),
            EventTriggerType.PartyNotHaveSpecialItem =>
                Verdict(!world.HasSpecialItem(AslInt(control, SpecialItemKey))),

            EventTriggerType.PartyHaveSpecialKey =>
                Verdict(world.HasKey(AslInt(control, SpecialKeyKey))),
            EventTriggerType.PartyNotHaveSpecialKey =>
                Verdict(!world.HasKey(AslInt(control, SpecialKeyKey))),

            // SpellMemorized needs a spellbook indexed by class and level; ExecuteGpdl needs the
            // VM attached to a running game. Both are real work, not oversights.
            _ => TriggerResult.Unknown,
        };
    }

    private static TriggerResult Verdict(bool fires) =>
        fires ? TriggerResult.Fire : TriggerResult.Suppress;

    /// <summary>The ASL key each attribute-stored operand uses (<c>GameEvent.cpp:1323</c>).</summary>
    public const string GenderKey = "Gen";

    /// <inheritdoc cref="GenderKey"/>
    public const string SpecialItemKey = "SpIt";

    /// <inheritdoc cref="GenderKey"/>
    public const string SpecialKeyKey = "SpKy";

    private static Gender GenderOf(EventControl control) =>
        (Gender)AslInt(control, GenderKey);

    /// <summary>
    /// Reads an integer operand out of the control's attribute map.
    /// </summary>
    /// <remarks>
    /// <c>atoi</c> semantics, deliberately: a missing key or unparseable value is 0, which for
    /// gender is <see cref="Gender.Male"/> and for the two object ids is an id that no design
    /// defines. That matches what the reference does with the same input rather than being lenient
    /// by accident.
    /// </remarks>
    public static int AslInt(EventControl control, string key)
    {
        ArgumentNullException.ThrowIfNull(control);

        foreach (var entry in control.Attributes)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                return int.TryParse(entry.Value, out int value) ? value : 0;
            }
        }

        return 0;
    }

    /// <summary>Whether a trigger type can be evaluated with a party and world in hand.</summary>
    public static bool IsEvaluable(EventTriggerType type) => type is not
        (EventTriggerType.SpellMemorized or EventTriggerType.ExecuteGpdl);

    /// <summary>
    /// Whether a facing condition accepts the party's current direction
    /// (<c>GameEvent.cpp:916-962</c>).
    /// </summary>
    /// <remarks>
    /// Transcribed as the original's four per-facing lists rather than derived from the names.
    /// The two agree — each combination really does accept exactly the directions its name spells,
    /// which was checked across all sixteen values — but a table built from the names would be
    /// asserting that agreement rather than reproducing the source, and it is the source that
    /// decides what a design does.
    /// </remarks>
    public static bool Accepts(EventFacing condition, Facing partyFacing)
    {
        // "InFront shouldn't happen", says the original, and treats it as Any anyway.
        if (condition is EventFacing.Any or EventFacing.InFront)
        {
            return true;
        }

        return partyFacing switch
        {
            Facing.North => condition is EventFacing.North or EventFacing.N_S or EventFacing.N_E
                                      or EventFacing.N_W or EventFacing.N_S_E or EventFacing.N_S_W
                                      or EventFacing.N_W_E,

            Facing.East => condition is EventFacing.East or EventFacing.N_E or EventFacing.S_E
                                     or EventFacing.E_W or EventFacing.N_S_E or EventFacing.N_W_E
                                     or EventFacing.W_S_E,

            Facing.South => condition is EventFacing.South or EventFacing.N_S or EventFacing.S_E
                                      or EventFacing.S_W or EventFacing.N_S_E or EventFacing.N_S_W
                                      or EventFacing.W_S_E,

            Facing.West => condition is EventFacing.West or EventFacing.N_W or EventFacing.S_W
                                     or EventFacing.E_W or EventFacing.N_S_W or EventFacing.N_W_E
                                     or EventFacing.W_S_E,

            // The switch has no default, so a facing outside 0..3 leaves shouldTrigger FALSE.
            _ => false,
        };
    }

    private static readonly Random Dice = new();

    private static int DefaultRoll() => Dice.Next(1, 101);
}
