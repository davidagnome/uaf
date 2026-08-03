using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Resolves a scripted teleporter destination
/// (<c>GameEvent::HandleTransfer</c>, <c>UAFWin/RunEvent.cpp:975</c>).
/// </summary>
/// <remarks>
/// <para>
/// When a transfer's <c>destEP</c> is <see cref="Game.ScriptedDestination"/> its three stored
/// fields are <b>arguments, not coordinates</b>: they are formatted into a script name and the
/// design's <c>TeleporterDestinations</c> ability is asked for the real destination.
/// </para>
/// <para>
/// <b>The script <i>name</i> carries the arguments.</b> There is no parameter passing here — the
/// name is <c>/level+1/x/y</c> and the design authors one script per source square. So a design
/// with fifty scripted teleporters has fifty named scripts inside one ability, and the lookup is
/// an exact string match.
/// </para>
/// <para>
/// <b>The level in the name is one-based and the level that comes back is too.</b> The reference
/// formats <c>destLevel+1</c> and then subtracts one from what it parses, so both directions carry
/// the same off-by-one and a script written against the displayed level number is correct.
/// </para>
/// </remarks>
public static class TeleporterDestinations
{
    /// <summary>The ability a design puts its teleporter scripts in.</summary>
    public const string AbilityName = "TeleporterDestinations";

    /// <summary>
    /// The script name for a source square — <c>/level+1/x/y</c>.
    /// </summary>
    public static string ScriptName(int level, int x, int y) => $"/{level + 1}/{x}/{y}";

    /// <summary>
    /// Asks the design where this teleporter goes.
    /// </summary>
    /// <returns>
    /// The resolved destination, or null when there is no such script or its answer does not
    /// parse — in which case the reference logs "Cannot find TeleporterDestination" and
    /// <b>leaves the stored fields alone</b>, so the transfer proceeds to whatever they held.
    /// This port refuses instead; see <see cref="Game"/>.
    /// </returns>
    /// <remarks>
    /// <b>The answer must be <c>/level/x/y</c> and all three must parse</b>, because the reference
    /// tests <c>sscanf(...) == 3</c>. A script returning two numbers, or trailing text, changes
    /// nothing at all rather than partially applying.
    /// </remarks>
    public static TransferData? Resolve(TransferData destination, GlobalScripts scripts,
                                        GameScriptHost host)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(host);

        string name = ScriptName(destination.DestLevel, destination.DestX, destination.DestY);
        if (!scripts.Has(AbilityName, name))
        {
            return null;
        }

        return Parse(scripts.Run(AbilityName, name, host)) is var (level, x, y)
            ? destination with { DestLevel = level, DestX = x, DestY = y }
            : null;
    }

    /// <summary>
    /// Reads <c>/level/x/y</c> the way <c>sscanf("/%d/%d/%d")</c> does, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The returned level is one-based and is decremented here</b>, matching the reference's
    /// <c>destLevel = l - 1</c>.
    /// </para>
    /// <para>
    /// <c>sscanf</c> is lenient in ways worth keeping: it skips leading whitespace before each
    /// number, accepts a sign, and <b>ignores anything after the third number</b> — it reports 3
    /// conversions and stops. What it will not do is skip the slashes, so the separators must be
    /// present and in order.
    /// </para>
    /// </remarks>
    public static (int Level, int X, int Y)? Parse(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        int at = 0;
        int[] values = new int[3];

        for (int i = 0; i < 3; i++)
        {
            if (at >= answer.Length || answer[at] != '/')
            {
                return null;
            }

            at++;
            if (!ScanInt(answer, ref at, out values[i]))
            {
                return null;
            }
        }

        // l - 1: the script speaks in one-based level numbers, as the name it was found by does.
        return (values[0] - 1, values[1], values[2]);
    }

    /// <summary><c>%d</c>: optional whitespace, an optional sign, then digits.</summary>
    private static bool ScanInt(string text, ref int at, out int value)
    {
        value = 0;

        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }

        int sign = 1;
        if (at < text.Length && (text[at] == '+' || text[at] == '-'))
        {
            sign = text[at] == '-' ? -1 : 1;
            at++;
        }

        int start = at;
        long scanned = 0;
        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            scanned = Math.Min((scanned * 10) + (text[at] - '0'), int.MaxValue);
            at++;
        }

        if (at == start)
        {
            return false;                                // no digits: the conversion failed
        }

        value = (int)(scanned * sign);
        return true;
    }
}
