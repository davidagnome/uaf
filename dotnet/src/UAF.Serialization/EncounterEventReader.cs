using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// One button on an encounter's menu (<c>ENCOUNTER_BUTTON_OPTION</c>, <c>GameEvent.h:505</c>).
/// </summary>
/// <param name="AllowedUpClose">Whether the option survives the monsters closing to zero range.</param>
/// <param name="OnlyUpClose">The inverse: an option that appears only at zero range.</param>
/// <param name="OptionResult">An <c>encounterButtonResultType</c> — what choosing it does.</param>
public sealed record EncounterOption(
    string Label, int Present, int AllowedUpClose, int OptionResult, uint Chain, int OnlyUpClose);

/// <summary>
/// An <c>ENCOUNTER_DATA</c> — monsters approaching, with a menu that changes as they close.
/// </summary>
/// <param name="Distance">An <c>eventDistType</c>: how far off the encounter starts.</param>
/// <param name="ZeroRangeResult">
/// What happens when the monsters arrive and the player has chosen nothing.
/// </param>
public sealed record EncounterEvent(
    GameEventBase Base, int Distance, int MonsterSpeed, int ZeroRangeResult,
    uint CombatChain, uint TalkChain, uint EscapeChain,
    int NumButtons, IReadOnlyList<EncounterOption> Options) : IGameEvent;

/// <summary>
/// Reads <c>ENCOUNTER_DATA</c> (<c>GameEvent.cpp:7313</c>).
/// </summary>
/// <remarks>
/// <para>
/// The one event type whose menu is not a simple option list: the monsters advance a step per
/// keypress, and each option declares whether it survives their arrival. That is what
/// <c>allowedUpClose</c> and <c>onlyUpClose</c> are for, and it is why the record carries a
/// <c>zeroRangeResult</c> — the outcome when the player runs out of distance without choosing.
/// </para>
/// <para>
/// <b>Two members of the class are not serialized</b>: <c>currDist</c>, which is runtime state, and
/// <c>Unused</c>. Sizing the read from the declaration rather than from <c>Serialize</c> over-reads
/// by five bytes and desynchronises the rest of the level.
/// </para>
/// </remarks>
public static class EncounterEventReader
{
    /// <summary><c>MAX_BUTTONS</c> (<c>GameEvent.h:50</c>) — always five on the wire.</summary>
    public const int MaxButtons = 5;

    public static EncounterEvent Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int distance = ar.ReadInt32();
        int monsterSpeed = ar.ReadInt32();
        int zeroRangeResult = ar.ReadInt32();
        uint combatChain = ar.ReadUInt32();
        uint talkChain = ar.ReadUInt32();
        uint escapeChain = ar.ReadUInt32();

        // The button block sits OUTSIDE the storing/loading branch, so it is present at every
        // version -- the same shape as WHO_PAYS_EVENT_DATA's trailing transfer blocks.
        int numButtons = ar.ReadInt32();
        var options = new List<EncounterOption>(MaxButtons);
        for (int i = 0; i < MaxButtons; i++)
        {
            options.Add(ReadOption(ar, version));
        }

        return new EncounterEvent(baseEvent, distance, monsterSpeed, zeroRangeResult,
                                  combatChain, talkChain, escapeChain, numButtons, options);
    }

    /// <summary>
    /// Reads one <c>ENCOUNTER_BUTTON_OPTION</c> (<c>GameEvent.cpp:4433</c>).
    /// </summary>
    /// <remarks>
    /// <b>All five slots are read whether the design filled them or not</b>, exactly as the
    /// question events do — <c>numButtons</c> says how many are meant, not how many are stored.
    /// <para>
    /// <c>onlyUpClose</c> arrives at 0.890 and is the <i>last</i> field, so a design below it is
    /// four bytes shorter per option — twenty per event.
    /// </para>
    /// </remarks>
    private static EncounterOption ReadOption(IArchiveCursor ar, DesignVersion version)
    {
        string label = ArchiveStringConventions.Decode(ar.ReadString());
        int present = ar.ReadInt32();
        int allowedUpClose = ar.ReadInt32();
        int optionResult = ar.ReadInt32();
        uint chain = ar.ReadUInt32();

        int onlyUpClose = version >= DesignVersion.V0890 ? ar.ReadInt32() : 0;

        return new EncounterOption(label, present, allowedUpClose, optionResult, chain, onlyUpClose);
    }
}
