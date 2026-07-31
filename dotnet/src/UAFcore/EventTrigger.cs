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
/// Only the conditions that need no party state are evaluated: always, random chance, the two
/// facing forms, and party position. Everything else asks about inventory, quests, spells or party
/// composition and returns <see cref="TriggerResult.Unknown"/> rather than a guess.
/// </para>
/// <para>
/// <b>Both facing forms return true unconditionally in the original.</b> <c>FacingDirection</c>
/// and <c>FacingDirectionAnyTime</c> fall through to <c>shouldTrigger = TRUE</c> without comparing
/// anything to the stored <c>facing</c> field. That reads like an unfinished feature — the field is
/// parsed, stored and never consulted — but it is what the engine does, and a design whose author
/// set a direction has been getting an always-trigger for twenty years.
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
                                         Func<int>? roll = null)
    {
        ArgumentNullException.ThrowIfNull(control);

        return (EventTriggerType)control.EventTrigger switch
        {
            EventTriggerType.Always => TriggerResult.Fire,

            // RollDice(100, 1, 0) <= chance, so a chance of 100 always fires and 0 never does.
            EventTriggerType.RandomChance =>
                (roll ?? DefaultRoll)() <= control.Chance ? TriggerResult.Fire
                                                          : TriggerResult.Suppress,

            // Both forms ignore the stored facing -- see the remarks.
            EventTriggerType.FacingDirection or EventTriggerType.FacingDirectionAnyTime =>
                TriggerResult.Fire,

            EventTriggerType.PartyAtXy =>
                partyX == control.PartyX && partyY == control.PartyY
                    ? TriggerResult.Fire
                    : TriggerResult.Suppress,

            _ => TriggerResult.Unknown,
        };
    }

    /// <summary>Whether a trigger type can be evaluated with the state the engine currently has.</summary>
    public static bool IsEvaluable(EventTriggerType type) => type is
        EventTriggerType.Always or EventTriggerType.RandomChance or
        EventTriggerType.FacingDirection or EventTriggerType.FacingDirectionAnyTime or
        EventTriggerType.PartyAtXy;

    private static readonly Random Dice = new();

    private static int DefaultRoll() => Dice.Next(1, 101);
}
