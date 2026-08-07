namespace UAF.Scripting;

/// <summary>Which actor a context call is asking for.</summary>
public enum GpdlContext
{
    /// <summary>Whoever is doing it (<c>attackerContext</c>).</summary>
    Attacker,

    /// <summary>Whoever it is being done to (<c>targetContext</c>).</summary>
    Target,

    /// <summary>The combatant whose turn or script this is (<c>combatantContext</c>).</summary>
    Combatant,

    /// <summary>The monster's database id (<c>pMonstertypeContext-&gt;monsterID</c>).</summary>
    MonsterType,
}

/// <summary>
/// The ambient context a script reads its actors from
/// (<c>SCRIPT_CONTEXT</c>, <c>Specab.h:817</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A stack, and the frames are RAII in the reference.</b> Constructing a <c>SCRIPT_CONTEXT</c>
/// pushes it onto the global <c>pScriptContext</c> and its destructor pops it, so a hook that
/// declares one on the stack has established a context for everything it calls and given it back
/// on the way out. Every caller in the engine does exactly that — the declaration <i>is</i> the
/// scope.
/// </para>
/// <para>
/// <b>A new frame inherits nothing.</b> The constructor nulls every field rather than copying the
/// frame below, so a script that pushes a context and then asks for an attacker its caller had set
/// gets nothing. That is why the hooks set the same two or three contexts over and over.
/// </para>
/// <para>
/// <b>This is the only way a script names an actor it was not handed.</b> Actor-typed parameters
/// refuse string literals, so <c>$AttackerContext()</c> and its siblings — together with the
/// four damage selectors — are where every actor in a design's scripts ultimately comes from.
/// </para>
/// </remarks>
public sealed class GpdlScriptContext
{
    private readonly List<Dictionary<GpdlContext, string>> frames = [];

    /// <summary>How many frames are open.</summary>
    public int Depth => frames.Count;

    /// <summary>
    /// Opens a frame, and closes it when disposed.
    /// </summary>
    /// <remarks>
    /// The nearest thing C# has to the reference's stack-allocated frame: a <c>using</c> is the
    /// declaration, and the scope ends where the block does.
    /// </remarks>
    public IDisposable Push()
    {
        frames.Add([]);
        return new Frame(this);
    }

    private sealed class Frame(GpdlScriptContext owner) : IDisposable
    {
        private bool closed;

        public void Dispose()
        {
            if (!closed)
            {
                closed = true;
                owner.Pop();
            }
        }
    }

    /// <summary>Closes the innermost frame. Doing so with none open is not an error.</summary>
    public void Pop()
    {
        if (frames.Count > 0)
        {
            frames.RemoveAt(frames.Count - 1);
        }
    }

    /// <summary>Sets an actor on the innermost frame. Silently ignored when none is open.</summary>
    public void Set(GpdlContext which, string actor)
    {
        if (frames.Count > 0)
        {
            frames[^1][which] = actor ?? string.Empty;
        }
    }

    /// <summary>
    /// Reads an actor off the innermost frame.
    /// </summary>
    /// <returns>
    /// The empty string when the frame does not carry one — which is what the reference returns
    /// too, after putting an error box in front of the player. <see cref="Missing"/> collects what
    /// those boxes would have said.
    /// </returns>
    public string Get(GpdlContext which)
    {
        if (frames.Count > 0 && frames[^1].TryGetValue(which, out string? actor)
            && actor.Length > 0)
        {
            return actor;
        }

        missing.Add(MessageFor(which));
        return string.Empty;
    }

    private readonly List<string> missing = [];

    /// <summary>
    /// Every context a script asked for and did not have, in order.
    /// </summary>
    /// <remarks>
    /// <b>The reference shows a message box and carries on with the empty string.</b> There is no
    /// dialog here, so the complaints are collected instead — a design with a script reaching for
    /// a context nobody set is broken in a way worth surfacing, and silently answering "" hides it.
    /// </remarks>
    public IReadOnlyList<string> Missing => missing;

    /// <summary>What the reference's alert says (<c>GPDLexec.cpp:5683</c>).</summary>
    public static string MessageFor(GpdlContext which) => which switch
    {
        GpdlContext.Attacker => "$AttackerContext() called when no attacker context exists",
        GpdlContext.Target => "$TargetContext() called when no target context exists",
        GpdlContext.Combatant => "$CombatantContext() called when no combatant context exists",
        _ => "$MonsterTypeContext() called when no monster type context exists",
    };
}
