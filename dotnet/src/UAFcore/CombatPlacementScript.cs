using UAF.Scripting;

namespace UAFcore;

/// <summary>
/// A design's own monster-placement script
/// (<c>RunGlobalScript("CombatPlacement", …)</c>, <c>UAFWin/Combatants.cpp:2626</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Three hooks, one per encounter distance</b> — <c>PlaceMonsterClose</c>,
/// <c>PlaceMonsterNear</c> and <c>PlaceMonsterFar</c> — and the reference chooses between them on
/// the distance alone.
/// </para>
/// <para>
/// <b>Only <c>PlaceMonsterFar</c> has a built-in default.</b> The other two exist solely in the
/// <c>CombatPlacement</c> ability that shipped designs carry, so a design with no
/// <c>specialAbilities.txt</c> at all has no script for an up-close or nearby encounter and the
/// reference places nothing. This port falls back on <see cref="TurtlePlacement.Default"/> for all
/// three, which is what the shipped ability's own scripts produce — the alternative is an empty
/// battlefield, which is a worse answer than the right one arrived at differently.
/// </para>
/// </remarks>
public sealed class CombatPlacementScript
{
    private readonly GlobalScripts scripts;

    public CombatPlacementScript(GlobalScripts scripts)
    {
        this.scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
    }

    /// <summary>The ability a design puts its placement scripts in.</summary>
    public const string AbilityName = "CombatPlacement";

    /// <summary>The hook for an encounter distance.</summary>
    public static string HookFor(EncounterDistance distance) => distance switch
    {
        EncounterDistance.UpClose => "PlaceMonsterClose",
        EncounterDistance.Nearby => "PlaceMonsterNear",
        _ => "PlaceMonsterFar",
    };

    /// <summary>Whether the design has a script for this distance.</summary>
    public bool Has(EncounterDistance distance) =>
        scripts.Has(AbilityName, HookFor(distance));

    /// <summary>
    /// Runs the design's script for one side, if it has one.
    /// </summary>
    /// <returns>
    /// True when a script ran — in which case it has already placed whatever it wanted, and the
    /// caller must not also run the built-in program.
    /// </returns>
    /// <remarks>
    /// A fresh host per call, because the arrangement it writes through is reset per side and a
    /// script that stashed state in hook parameters should not carry it across.
    /// </remarks>
    public bool Run(EncounterDistance distance, MonsterArrangement arrangement, CombatMap map,
                    IReadOnlyList<CombatantIcon> icons, Facing facing)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(icons);

        string hook = HookFor(distance);
        if (!scripts.Has(AbilityName, hook))
        {
            return false;
        }

        scripts.Run(AbilityName, hook, new CombatPlacementHost(arrangement, map, icons, facing));
        return true;
    }
}
