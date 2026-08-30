using UAF.Scripting;

namespace UAFcore;

/// <summary>
/// A design's own party-formation hooks — <c>PartyArrangement</c> and the four
/// <c>PartyOrigin&lt;direction&gt;</c> scripts in its <c>Global_Combat</c> ability
/// (<c>RunGlobalScript("Global_Combat", …)</c>, <c>Combatants.cpp:2445</c>, <c>:2488</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b><c>PartyArrangement</c> replaces the whole formation table.</b> Its result is used when it is
/// exactly the length of the built-in indoor table — <c>strlen(partyIndoorCombatArrangement)</c> is
/// the comparison, indoor even for an outdoor fight — and anything else falls back to the built-in
/// with a warning (<c>Combatants.cpp:2491</c>).
/// </para>
/// <para>
/// <b>The four <c>PartyOrigin*</c> hooks supply a per-direction offset, not an absolute square.</b>
/// The reference presets hook parameters 5 and 6 to <c>"0"</c>, runs the one hook for the party's
/// facing, and adds the two results to the party's start — each clamped to −8..+8 by
/// <c>ScriptAtoI</c> (<c>Specab.cpp:1609</c>).
/// </para>
/// </remarks>
public sealed class PartyArrangementScript
{
    private readonly GlobalScripts scripts;

    public PartyArrangementScript(GlobalScripts scripts)
    {
        this.scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
    }

    /// <summary>The ability a design puts its party-formation hooks in.</summary>
    public const string AbilityName = "Global_Combat";

    /// <summary>The hook name that supplies a per-direction origin offset.</summary>
    public static string OriginHookFor(Facing facing) => facing switch
    {
        Facing.North => "PartyOriginNorth",
        Facing.East => "PartyOriginEast",
        Facing.South => "PartyOriginSouth",
        _ => "PartyOriginWest",
    };

    /// <summary>
    /// The origin offset the design's <c>PartyOrigin&lt;direction&gt;</c> script asks for, clamped
    /// to −8..+8. A design with no such script leaves both at zero, which is the reference's own
    /// default.
    /// </summary>
    public (int Dx, int Dy) Origin(Facing facing)
    {
        var host = new Host((int)facing);
        host.HookParameters[5] = "0";
        host.HookParameters[6] = "0";

        scripts.Run(AbilityName, OriginHookFor(facing), host);

        return (ScriptAtoI(host.HookParameters[5]), ScriptAtoI(host.HookParameters[6]));
    }

    /// <summary>
    /// The formation table to use — the design's <c>PartyArrangement</c> result when it is the
    /// right length, otherwise the built-in table for indoor/outdoor.
    /// </summary>
    public string Arrangement(bool outdoor, Facing facing)
    {
        string builtIn = outdoor ? PartyArrangements.Outdoor : PartyArrangements.Indoor;

        var host = new Host((int)facing);
        host.HookParameters[5] = outdoor ? "O" : "I";

        string result = scripts.Run(AbilityName, "PartyArrangement", host);
        return result.Length == PartyArrangements.Indoor.Length ? result : builtIn;
    }

    /// <summary>
    /// Parses a numeric prefix and clamps it into −8..+8, exactly as <c>ScriptAtoI</c> does —
    /// leading sign, then digits, stopping at the first non-digit, then a clamp
    /// (<c>Specab.cpp:1609</c>).
    /// </summary>
    public static int ScriptAtoI(string text)
    {
        int n = 0;
        int sign = 1;
        int i = 0;

        if (i < text.Length && text[i] == '-')
        {
            sign = -1;
            i++;
        }
        else if (i < text.Length && text[i] == '+')
        {
            i++;
        }

        while (i < text.Length && text[i] >= '0' && text[i] <= '9')
        {
            n = (10 * n) + (text[i] - '0');
            i++;
        }

        n *= sign;
        return Math.Clamp(n, -8, 8);
    }

    /// <summary>The host a formation hook sees: the party's facing and the hook-parameter block.</summary>
    private sealed class Host : GpdlUnhostedEnvironment
    {
        public Host(int partyFacing) => PartyFacing = partyFacing;

        public override int PartyFacing { get; }
    }
}
