using UAF.Serialization;
using UAFcore;

namespace UAFedit.Events;

/// <summary>One value of an enumeration an event stores as a bare ordinal, and its label.</summary>
/// <param name="Value">
/// The number actually on the wire. Never a list index: several of these tables are sparse or start
/// at one, and a design's saved ordinal is the only thing that survives a round trip.
/// </param>
public sealed record EventChoice(int Value, string Label);

/// <summary>
/// The editor's names for every enumeration an event stores as a raw <c>int</c>.
/// </summary>
/// <remarks>
/// <para>
/// Almost every interesting field on an event record is an ordinal with no type — <c>Distance</c>,
/// <c>Operation</c>, <c>Who</c>, <c>Facing</c>, <c>SuccessAction</c>. The record documents which
/// C++ enum each one is but cannot name the members, because <c>UAF.Serialization</c> deliberately
/// stops at the byte layout. Showing "3" where the original showed "Subtracted From" makes the
/// whole detail pane unreadable, so the tables are transcribed here.
/// </para>
/// <para>
/// <b>These are the editor's own strings, from <c>UAFWinEd/Globtext.cpp</c>, not the header's
/// identifiers.</b> They differ in useful ways: <c>eventTriggerType.FacingDirection</c> is shown as
/// "Facing dir, only when arrive" and <c>FacingDirectionAnyTime</c> as "Facing dir, even after
/// arrive", which is the only place the distinction between the two is written down in words.
/// </para>
/// <para>
/// <b>Three tables are shorter than their enums.</b> <see cref="Distance"/> has three entries for a
/// six-member <c>eventDistType</c> (<c>Globtext.cpp:261</c> against <c>GameEvent.h:63</c>) because
/// the last three are set by the engine and never by a designer; <see cref="Facing"/> has five for
/// <c>eventFacingType</c>; <see cref="TavernTaleFlags"/> is a bit list rather than an enum. A stored
/// value outside the table is rendered as its number rather than dropped — the value is real even
/// when the original editor had no word for it.
/// </para>
/// </remarks>
public static class EventCatalog
{
    /// <summary>
    /// The name the original editor's event-type combo shows (<c>EventListText</c>,
    /// <c>Globtext.cpp:118</c>).
    /// </summary>
    /// <remarks>
    /// The table is indexed by the <c>eventType</c> ordinal directly and runs 0..44, so it covers
    /// the enum exactly up to the <c>CONTROL_*</c> gap at 1000. It carries the two obsolete
    /// entries' status in the text itself ("Inn (Obsolete)"), which is worth keeping: a design that
    /// still contains one should look wrong.
    /// </remarks>
    public static string Name(EventType type)
    {
        int ordinal = (int)type;

        return ordinal >= 0 && ordinal < EventTypeNames.Count
            ? EventTypeNames[ordinal]
            : $"Type {ordinal}";
    }

    private static readonly IReadOnlyList<string> EventTypeNames =
    [
        "No Event", "Add NPC", "Camp", "Chain", "Combat", "Combat Treasure", "Damage",
        "Encounter", "Enter Password", "Gain Experience", "Give Treasure", "Guided Tour",
        "Inn (Obsolete)", "NPC Says", "Pass Time", "Pick One Combat", "Quest Stage",
        "Question Button", "Question List", "Question Yes/No", "Remove NPC", "Shop",
        "Small Town", "Sounds", "Special Item", "Stairs", "Tavern", "Tavern Tales",
        "Teleporter", "Temple", "Text Statement", "Training Hall", "Transfer Module",
        "Utilities", "Vault", "Who Pays", "Who Tries", "Take Party Items", "Heal Party",
        "Logic Block", "GPDL", "Random Event", "Play Movie", "Add To Journal", "Flow Control",
    ];

    /// <summary>Renders one stored ordinal against a table, falling back on the number.</summary>
    public static string Label(IReadOnlyList<EventChoice> choices, int value) =>
        choices.FirstOrDefault(c => c.Value == value)?.Label ?? value.ToString();

    private static IReadOnlyList<EventChoice> From(params string[] labels) =>
        labels.Select((label, index) => new EventChoice(index, label)).ToList();

    /// <summary><c>eventTriggerType</c> (<c>EventTriggerText</c>, <c>Globtext.cpp:415</c>).</summary>
    /// <remarks>
    /// Built from <see cref="EventTriggerType"/> rather than a bare index so the two cannot drift:
    /// the port's enum is the authority on the ordinals and this is only the wording.
    /// </remarks>
    public static readonly IReadOnlyList<EventChoice> EventTrigger = From(
        "Always", "Party has item", "Party NOT have item", "Daytime", "Nighttime",
        "Random Chance", "Party is Searching", "Party is NOT Searching",
        "Facing dir, only when arrive", "Quest is complete", "Quest failed",
        "Quest in Progress", "Party detecting traps", "Party NOT detecting traps",
        "Party can see invisible", "Party cannot see invisible", "Specific Class in Party",
        "Specific Class NOT in Party", "Specific Race in Party", "Specific Race NOT in Party",
        "Quest present in Party", "Quest NOT present in Party", "Gender in Party",
        "Gender is NOT in Party", "Facing dir, even after arrive", "NPC in Party",
        "NPC is NOT in Party", "Execute GPDL function", "Party has spell memorized",
        "Party at x,y", "Specific Baseclass in Party", "Specific Baseclass NOT in Party",
        "Party has Special Item", "Party NOT have Special Item", "Party has Special Key",
        "Party NOT have Special Key", "Quest Stage Equal", "Quest Stage NOT Equal");

    /// <summary><c>chainTriggerType</c> (<c>ChainTriggerText</c>, <c>Globtext.cpp:408</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> ChainTrigger = From(
        "Always", "If Event Happens", "If Event does not happen");

    /// <summary>
    /// <c>eventDirType</c>, the control block's <c>facing</c> (<c>DirectionText</c>,
    /// <c>Globtext.cpp:299</c>).
    /// </summary>
    /// <remarks>
    /// Sixteen combinations compared with <c>==</c>, not a bit field — see
    /// <see cref="EventFacing"/>. The name in the header is <c>eventDirType</c> and the field is
    /// called <c>facing</c>, which is why the two facing tables are easy to confuse; this is the
    /// one the trigger uses, <see cref="Facing"/> is the one a transfer's destination uses.
    /// </remarks>
    public static readonly IReadOnlyList<EventChoice> Direction = From(
        "Any/All Side(s)", "North", "South", "East", "West", "North, South", "North, East",
        "North, West", "South, East", "South, West", "East,  West", "North, South, East",
        "North, South, West", "North, West,  East", "West,  South, East", "In Front Of");

    /// <summary><c>eventFacingType</c> (<c>FacingText</c>, <c>Globtext.cpp:267</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> Facing = From(
        "North", "East", "South", "West", "Unchanged");

    /// <summary><c>eventDistType</c> (<c>DistanceText</c>, <c>Globtext.cpp:261</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> Distance = From(
        "Up Close", "Nearby", "Far Away");

    /// <summary><c>MathOperationType</c> (<c>MathOperationText</c>, <c>Globtext.cpp:65</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> MathOperation = From(
        "No Operation", "Stored In", "Added To", "Subtracted From");

    /// <summary><c>MultiItemCheckType</c> (<c>MultiItemCheckText</c>, <c>Globtext.cpp:72</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> MultiItemCheck = From(
        "No Check", "All Items", "At Least 1 Item");

    /// <summary><c>QuestTypeText</c> (<c>Globtext.cpp:78</c>) — what a counter counts.</summary>
    public static readonly IReadOnlyList<EventChoice> QuestObjectType = From("Item", "Key", "Quest");

    /// <summary><c>questAcceptText</c> (<c>Globtext.cpp:91</c>) — a quest stage's operation.</summary>
    public static readonly IReadOnlyList<EventChoice> QuestAccept = From(
        "Impossible", "On Yes", "On No", "On Yes or No", "Impossible (No Question)",
        "Automatic (No Question)");

    /// <summary><c>eventStepType</c> (<c>StepText</c>, <c>Globtext.cpp:506</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> TourStep = From(
        "No Action", "Pause", "Forward", "Turn Left", "Turn Right");

    /// <summary>
    /// <c>labelPostChainOptionsType</c> (<c>ButtonPostChainOptionText</c>,
    /// <c>Globtext.cpp:402</c>) — what happens when the chained event returns.
    /// </summary>
    public static readonly IReadOnlyList<EventChoice> PostChain = From(
        "Do Nothing", "Return To Question", "Backup One Step");

    /// <summary><c>passwordActionType</c> (<c>PasswordActionText</c>, <c>Globtext.cpp:491</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> PasswordAction = From(
        "No Action", "Chain Event", "Teleport", "Backup one step");

    /// <summary><c>encounterButtonResultType</c> (<c>ButtonOptionText</c>, <c>Globtext.cpp:391</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> EncounterButton = From(
        "No Result", "Decrease Range", "Combat, No Surprise", "Combat, Slow Party Surprised",
        "Combat, Slow Monsters Surprised", "Talk", "Escape if Fast Party, Else Combat",
        "Chain to Event");

    /// <summary><c>eventPartyAffectType</c> (<c>AffectsWhoText</c>, <c>Globtext.cpp:324</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> AffectsWho = From(
        "None", "Entire Party", "Active Char", "One Char at Random", "Chance on each Char");

    /// <summary><c>eventSurpriseType</c> (<c>SurpriseText</c>, <c>Globtext.cpp:318</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> Surprise = From("Neither", "Party", "Monster");

    /// <summary><c>eventTurnUndeadModType</c> (<c>TurnModText</c>, <c>Globtext.cpp:332</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> TurnMod = From(
        "None", "Hard", "Difficult", "Impossible");

    /// <summary><c>spellSaveVersusType</c> (<c>SaveVersusText</c>, <c>Globtext.cpp:339</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> SaveVersus = From(
        "Paralysis/Poison/Death Magic", "Petrification/Polymorph", "Rod/Staff/Wand", "Spell",
        "Breath Weapon");

    /// <summary><c>spellSaveEffectType</c> (<c>SaveEffectText</c>, <c>Globtext.cpp:347</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> SaveEffect = From(
        "No Save", "Save Negates", "Save for Half", "Use Player THAC0");

    /// <summary><c>takeItemsAffectsType</c> (<c>TakeItemsAffectsText</c>, <c>Globtext.cpp:255</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> TakeAffects = From(
        "All Characters", "Random Character", "Active Character");

    /// <summary><c>takeItemQtyType</c> (<c>TakeItemsQtyText</c>, <c>Globtext.cpp:168</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> TakeQuantity = From(
        "Specified", "Random", "Percent", "All");

    /// <summary>
    /// The bit names of <c>TAKE_PARTY_ITEMS_DATA::takeItems</c> (<c>TakeWhatText</c>,
    /// <c>Globtext.cpp:175</c>).
    /// </summary>
    /// <remarks>
    /// <c>takeItemsActionType</c> is explicitly valued 1, 2, 4, 8 (<c>GameEvent.h:332</c>) — a mask
    /// rather than the ordinal the name suggests.
    /// </remarks>
    public static readonly IReadOnlyList<string> TakeWhat =
        ["Inventory", "Money", "Gems", "Jewelry"];

    /// <summary>The bit name of <c>FLOW_CONTROL</c>'s flags (<c>Globtext.cpp:182</c>).</summary>
    public static readonly IReadOnlyList<string> FlowControlFlags = ["Local Chain Only"];

    /// <summary>
    /// <c>HEAL_PARTY_DATA::literalOrPercent</c> (<c>LiteralOrPercentText</c>,
    /// <c>Globtext.cpp:233</c>).
    /// </summary>
    /// <remarks>
    /// <b>Three values, not two.</b> The record's own summary calls it "0 means literal, 1 a
    /// percentage"; the editor's table has a third, "Set to Percent of Max", so the field is a
    /// three-way mode and not a flag.
    /// </remarks>
    public static readonly IReadOnlyList<EventChoice> LiteralOrPercent = From(
        "Add to Current", "Add Percent of Max", "Set to Percent of Max");

    /// <summary><c>taleOrderType</c> (<c>TaleOrderTypeText</c>, <c>Globtext.cpp:239</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> TaleOrder = From("In Order", "Random");

    /// <summary>The bit names of a tavern tale's flags (<c>TavernTalesFlagsText</c>, <c>Globtext.cpp:245</c>).</summary>
    public static readonly IReadOnlyList<string> TavernTaleFlags =
        ["Cumulative", "If drink", "If drunk", "Replace", "Highlight"];

    /// <summary>A shop or temple's price multiplier (<c>costFactorText</c>, <c>Globtext.cpp:636</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> CostFactor = From(
        "Free", "Div 100", "Div 50", "Div 20", "Div 10", "Div 5", "Div 4", "Div 3", "Div 2",
        "Div 1.5", "Normal", "Mult 1.5", "Mult 2", "Mult 3", "Mult 4", "Mult 5", "Mult 10",
        "Mult 20", "Mult 50", "Mult 100");

    /// <summary>
    /// A flow-control event's value modification (<c>VALUE_MODIFICATIONText</c>,
    /// <c>Globtext.cpp:207</c>).
    /// </summary>
    /// <remarks>
    /// <b>These four tables are one-based.</b> Index 0 is the literal string "illegal", so a stored
    /// 0 is a defect rather than a default and the meaningful values start at 1. Keeping the
    /// "illegal" entry rather than trimming it is what makes a corrupt event visible.
    /// </remarks>
    public static readonly IReadOnlyList<EventChoice> ValueModification = From(
        "illegal", "none", "set", "increment", "decrement");

    /// <summary>A flow-control event's action (<c>ACTIONText</c>, <c>Globtext.cpp:215</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> FlowAction = From(
        "illegal", "none", "goto", "call", "return", "pop");

    /// <summary>A flow-control action's condition (<c>ACTION_CONDITIONText</c>, <c>Globtext.cpp:224</c>).</summary>
    public static readonly IReadOnlyList<EventChoice> FlowCondition = From(
        "illegal", "always", "equal", "not equal");

    /// <summary>
    /// <c>LOGIC_BLOCK_GATE_TYPE</c> (<c>logicGateText</c>, <c>UAFWinEd/LogicBlock.cpp:36</c>).
    /// </summary>
    /// <remarks>
    /// <b>0xff is a real stored value.</b> <c>LBGT_NotImplemented</c> is 255 and the original
    /// labels it "What Do You Need?" — a placeholder a designer leaves in a half-built block. The
    /// field is a <c>BYTE</c>, so it fits; a table indexed by position would render it as blank.
    /// </remarks>
    public static readonly IReadOnlyList<EventChoice> LogicGate =
    [
        .. From("Copy from Top", "Copy from Side", "Logical AND", "Logical OR", "Numeric Plus",
                "Numeric Minus", "GREP", "Force True", "Force False", "String Equal",
                "Numeric Multiply", "Numeric Divide", "Numeric Greater", "Numeric Modulo"),
        new EventChoice(0xff, "What Do You Need?"),
    ];

    /// <summary>
    /// <c>LOGIC_BLOCK_INPUT_TYPE</c> (<c>logicInputText</c>, <c>LogicBlock.cpp:80</c>).
    /// </summary>
    /// <remarks>
    /// <b>14 is deliberately unlabelled.</b> <c>LBIT_BinaryGPDL</c> is what
    /// <c>LBIT_SourceGPDL</c> compiles into at run time and is never chosen by a designer, so the
    /// original's table has no string for it. The gap is what breaks the dialog's own combo:
    /// <c>OnInitDialog</c> selects input combos by raw enum value (<c>LogicBlock.cpp:452</c>) while
    /// the list is packed by position, so <c>LBIT_tempASL</c> (15) selects entry 14.
    /// </remarks>
    public static readonly IReadOnlyList<EventChoice> LogicInput =
    [
        .. From("Literal", "Global ASL", "Party Size", "Character Info", "Direction Facing",
                "Level ASL", "Quest Stage", "Item List", "NPC List", "Run Time Vars",
                "Character ASL", "Party ASL", "Wiggle (Grep field)", "GPDL Function"),
        new EventChoice(14, "Binary GPDL (compiled)"),
        new EventChoice(15, "Temporary ASL"),
        new EventChoice(0xff, "What Do You Need?"),
    ];

    /// <summary>
    /// <c>LOGIC_BLOCK_ACTION_TYPE</c> (<c>logicActionText</c>, <c>LogicBlock.cpp:130</c>).
    /// </summary>
    public static readonly IReadOnlyList<EventChoice> LogicAction =
    [
        .. From("Do Nothing", "Set Global ASL", "Set Level ASL", "Remove Global ASL",
                "Remove Level ASL", "Set Quest Stage", "Set Temporary ASL",
                "Set Icon Index By Name", "Set Character ASL", "Set Party ASL",
                "Remove Party ASL", "GPDL Function"),
        new EventChoice(12, "Binary GPDL (compiled)"),
        new EventChoice(0xff, "What Do You Need?"),
    ];

    /// <summary>
    /// When a logic block's action runs (<c>GetLogicBlockActionConditionText</c>,
    /// <c>LogicBlock.cpp:173</c>).
    /// </summary>
    public static readonly IReadOnlyList<EventChoice> LogicActionCondition = From(
        "IfTrue", "IfFalse", "Always");

    /// <summary>
    /// Whether a logic block chains at all (<c>GetLogicBlockChainConditionText</c>,
    /// <c>LogicBlock.cpp:198</c>).
    /// </summary>
    /// <remarks>
    /// <b>The stored name is <c>NoChain</c> and it is not a flag.</b> Only value 2, "Conditional",
    /// makes the true and false arms live; 1 defers to the ordinary <c>chainEventHappen</c> and 0
    /// ends the run. The original's spelling of the first is "Supress".
    /// </remarks>
    public static readonly IReadOnlyList<EventChoice> LogicChainCondition = From(
        "Suppress", "Normal", "Conditional");

    /// <summary>
    /// A <c>SPECIAL_OBJECT_EVENT</c>'s operation — a bitmask, not an ordinal
    /// (<c>GameEvent.h:51</c>).
    /// </summary>
    /// <remarks>
    /// <c>TAKE</c>, <c>GIVE</c> and <c>CHECK</c> are 1, 2 and 4 and combine, so this is rendered
    /// rather than selected from.
    /// </remarks>
    public static string SpecialObjectOperation(byte operation)
    {
        if (operation == 0)
        {
            return "Nothing";
        }

        var parts = new List<string>(3);
        if ((operation & 0x01) != 0) { parts.Add("Take"); }
        if ((operation & 0x02) != 0) { parts.Add("Give"); }
        if ((operation & 0x04) != 0) { parts.Add("Check"); }

        return parts.Count > 0 ? string.Join(" + ", parts) : $"0x{operation:x2}";
    }

    /// <summary>Renders a bit list such as <see cref="TavernTaleFlags"/>.</summary>
    public static string Flags(IReadOnlyList<string> names, uint value)
    {
        var set = names.Where((_, bit) => (value & (1u << bit)) != 0).ToList();

        return set.Count > 0 ? string.Join(", ", set) : "none";
    }
}
