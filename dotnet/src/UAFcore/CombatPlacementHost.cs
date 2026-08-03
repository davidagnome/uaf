using UAF.Scripting;

namespace UAFcore;

/// <summary>
/// The host a <c>CombatPlacement</c> script talks to while monsters are being placed.
/// </summary>
/// <remarks>
/// <para>
/// A placement script does exactly two things: read the party's facing and call
/// <c>$MonsterPlacement</c> with a turtle program. This holds the arrangement, map and icons that
/// call needs, which is why it exists as its own host rather than as members on
/// <see cref="GameScriptHost"/> — they are meaningful only for the length of one placement.
/// </para>
/// <para>
/// <b>The script runs once per side, not once per fight.</b> The reference resets the turtle and
/// calls the hook inside its direction loop, so a design's script is invoked for each side that
/// has monsters on it and sees a freshly-reset arrangement each time.
/// </para>
/// </remarks>
public sealed class CombatPlacementHost : GpdlUnhostedEnvironment
{
    private readonly MonsterArrangement arrangement;
    private readonly CombatMap map;
    private readonly IReadOnlyList<CombatantIcon> icons;

    public CombatPlacementHost(MonsterArrangement arrangement, CombatMap map,
                               IReadOnlyList<CombatantIcon> icons, Facing partyFacing)
    {
        this.arrangement = arrangement ?? throw new ArgumentNullException(nameof(arrangement));
        this.map = map ?? throw new ArgumentNullException(nameof(map));
        this.icons = icons ?? throw new ArgumentNullException(nameof(icons));
        PartyFacing = (int)partyFacing;
    }

    /// <inheritdoc/>
    public override int PartyFacing { get; }

    /// <summary>Every turtle program the script ran, in order — for a test to look at.</summary>
    public List<string> Programs { get; } = [];

    /// <inheritdoc/>
    /// <remarks>
    /// The turtle writes monsters onto the map as it goes and returns its output string, which the
    /// script may inspect. Nothing in the shipped scripts does, but the value is pushed either way.
    /// </remarks>
    public override string MonsterPlacement(string turtleCode)
    {
        ArgumentNullException.ThrowIfNull(turtleCode);

        Programs.Add(turtleCode);
        return TurtlePlacement.Run(turtleCode, arrangement, map, icons);
    }
}
