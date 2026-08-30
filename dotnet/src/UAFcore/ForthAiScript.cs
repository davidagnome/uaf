using UAF.Common;
using UAF.Scripting;

namespace UAFcore;

/// <summary>
/// A design's own <c>AI_Script.BLK</c>, compiled and ready to rank actions
/// (<c>LoadAI_Script</c>, <c>Combatant.cpp:2047</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what <see cref="MonsterAiScript"/> is a transcription of.</b> Every shipped design
/// carries the same script bar one line, so the transcription is what runs by default and costs
/// nothing; this class exists for the design that edits its own. Where both can run they must
/// agree, and a test asserts they do on the shipped versions.
/// </para>
/// <para>
/// <b>The machine is stateful and single-threaded.</b> One compiled dictionary is reused for every
/// comparison, exactly as the reference reuses its one static kernel — <c>RunTHINK</c> resets both
/// stacks on entry, so nothing carries between calls, but two combats must not share an instance
/// across threads.
/// </para>
/// </remarks>
public sealed class ForthAiScript
{
    private readonly ForthMachine forth;

    private ForthAiScript(ForthMachine machine) => forth = machine;

    /// <summary>
    /// Compiles a design's script, or returns null when it has none or the script will not build.
    /// </summary>
    /// <remarks>
    /// <b>The reference <c>die()</c>s when the file is missing</b> (<c>Forth.cpp:2335</c>), because
    /// <c>AI_Script.BLK</c> is engine data that always ships and it has nothing to fall back on.
    /// This port does: <see cref="MonsterAiScript"/> is that same script transcribed, so a missing
    /// or broken file returns null and the caller uses it. That is the same behaviour by a
    /// different route, and it is why an unreadable script degrades instead of ending the game.
    /// </remarks>
    public static ForthAiScript? Load(string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);

        string path = CaseInsensitiveFiles.Resolve(dataDirectory, "AI_Script.BLK")
                      ?? Path.Combine(dataDirectory, "AI_Script.BLK");
        return File.Exists(path) ? FromSource(File.ReadAllText(path)) : null;
    }

    /// <summary>Compiles script text, or returns null when the kernel or the script aborts.</summary>
    public static ForthAiScript? FromSource(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var machine = new ForthMachine();

        return machine.Bootstrap() && machine.LoadScript(text) && machine.Lookup("THINK") != 0
            ? new ForthAiScript(machine)
            : null;
    }

    /// <summary>
    /// Puts the best action first (the tree insertion around <c>RunTHINK</c>,
    /// <c>Combatant.cpp:2237</c>–<c>:2255</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a heap build, not a sort, and only the head is meaningful.</b> Each action is
    /// appended and then sifted up while <c>THINK</c> prefers it to its parent; the reference reads
    /// <c>actionIndex[0]</c> and nothing else. Its own comment says why it is shaped this way — so
    /// that it "could easily extract several actions" to choose randomly among the best — but that
    /// never happens, and everything past the head is in heap order rather than rank order.
    /// </para>
    /// <para>
    /// <b>The comparator need not be a total order</b>, which is the other reason this is not a
    /// sort: <c>THINK</c> may call two actions equal that a third separates, and a heap tolerates
    /// that where <c>List.Sort</c> may throw on it.
    /// </para>
    /// <para>
    /// <b>The combatants are projected once, the actions once each.</b> The reference calls
    /// <c>ListCombatants</c> and <c>ListActions</c> before the loop and only swaps which two
    /// actions the summary points at, so nothing here is rebuilt per comparison.
    /// </para>
    /// </remarks>
    public List<AiAction> Rank(IReadOnlyList<AiAction> actions, Combatant self,
                               IReadOnlyList<Combatant> all, IReadOnlyList<AiWeapon> weapons)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var combatants = AiSummary.Combatants(self, all, weapons);
        var projected = actions.Select(a => AiSummary.Action(a, combatants)).ToList();
        var order = new List<int>(actions.Count);

        for (int i = 0; i < projected.Count; i++)
        {
            order.Add(i);

            int j = i;
            while (j > 0)
            {
                int k = ((j - 1) / 2);
                if (Think(combatants, projected[order[j]], projected[order[k]]) <= 0)
                {
                    break;
                }

                (order[j], order[k]) = (order[k], order[j]);
                j = k;
            }
        }

        return [.. order.Select(i => actions[i])];
    }

    /// <summary>
    /// Runs <c>THINK</c> over two candidate actions: positive prefers <paramref name="a"/>.
    /// </summary>
    public int Compare(AiAction a, AiAction b, Combatant self, IReadOnlyList<Combatant> all,
                       IReadOnlyList<AiWeapon> weapons)
    {
        return forth.RunThink(AiSummary.For(self, all, weapons, a, b));
    }

    /// <summary>
    /// Whether the script rejects one candidate action (the six <c>Run*Filter</c> functions).
    /// </summary>
    /// <remarks>
    /// The sense is the reference's: a non-zero result means the action is not offered. This is the
    /// scripted counterpart of <see cref="MonsterAiScript.Survives"/>, which answers the opposite
    /// question.
    /// </remarks>
    public bool Rejects(ForthAiFilter filter, AiAction action, Combatant self,
                        IReadOnlyList<Combatant> all, IReadOnlyList<AiWeapon> weapons)
    {
        var combatants = AiSummary.Combatants(self, all, weapons);
        return forth.Rejects(filter, AiSummary.Action(action, combatants), combatants);
    }

    private int Think(IReadOnlyList<ForthCombatant> combatants, ForthAction a, ForthAction b)
    {
        return forth.RunThink(new ForthCombatSummary
        {
            ActionA = a,
            ActionB = b,
            Combatants = combatants,
        });
    }
}
