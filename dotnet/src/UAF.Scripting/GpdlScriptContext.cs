namespace UAF.Scripting;

/// <summary>
/// What kind of record a script came from (<c>SCRIPT_SOURCE_TYPE</c>, and the words
/// <c>GetSourceTypeName</c> returns for it, <c>Specab.cpp:231</c>).
/// </summary>
/// <remarks>
/// <b>The words are the wire format as far as a script is concerned</b> — a design compares
/// <c>$SA_SOURCE_TYPE()</c> against these literals, so "EVENT TRIGGER" keeps its space and
/// anything unrecognised is "Unknown" with that capitalisation.
/// </remarks>
public enum GpdlScriptSource
{
    Unknown,
    Class,
    Item,
    Race,
    Baseclass,
    Spell,
    Character,
    Monster,
    Combatant,
    Aura,
    Event,
    EventTrigger,
}

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

    // ---- the ability that is running -------------------------------------------------------------

    /// <summary>
    /// What a script gets when there is no such ability (<c>NO_SUCH_SA</c>, <c>Specab.h:735</c>).
    /// </summary>
    /// <remarks>
    /// <b>A sentinel string, not an empty one</b> — five characters a design can compare against,
    /// which is how a script tells "the ability has no parameter" from "the parameter is blank".
    /// </remarks>
    public const string NoSuchAbility = "-?-?-";

    /// <summary>
    /// The ability whose script is running, as a name and a parameter
    /// (<c>SCRIPT_CONTEXT::specAb</c>, a key/value pair).
    /// </summary>
    /// <remarks>
    /// <b>One pair, not a list.</b> The specab that triggered the script is a single entry, and
    /// <c>$SA_NAME()</c> and <c>$SA_PARAM_GET()</c> are its two halves.
    /// </remarks>
    public void SetAbility(string name, string parameter)
    {
        if (frames.Count > 0)
        {
            ability[Depth - 1] = (name ?? string.Empty, parameter ?? string.Empty);
        }
    }

    private readonly Dictionary<int, (string Name, string Parameter)> ability = [];

    /// <summary>The running ability's name, or <see cref="NoSuchAbility"/>.</summary>
    public string AbilityName =>
        Running is { } running ? running.Name : NoSuchAbility;

    /// <summary>The running ability's parameter, or <see cref="NoSuchAbility"/>.</summary>
    public string AbilityParameter =>
        Running is { } running ? running.Parameter : NoSuchAbility;

    private (string Name, string Parameter)? Running =>
        frames.Count > 0 && ability.TryGetValue(Depth - 1, out var found) ? found : null;

    /// <summary>
    /// Rewrites the running ability's parameter (<c>$SA_PARAM_SET</c>, <c>GPDLexec.cpp:3242</c>).
    /// </summary>
    /// <remarks>
    /// <b>It yields what it was given, not the empty string.</b> The reference pushes the same
    /// value back (<c>m_pushString1</c> on the argument it just popped), where the character and
    /// party setters push nothing — so this one call is usable as an expression.
    /// </remarks>
    public void SetAbilityParameter(string parameter)
    {
        if (Running is { } running)
        {
            ability[Depth - 1] = (running.Name, parameter ?? string.Empty);
        }
    }

    /// <summary>
    /// Removes the running ability from the record that carries it (<c>RemoveAbility</c>,
    /// <c>Specab.cpp:676</c>).
    /// </summary>
    /// <returns>
    /// The removed ability's value, or <see cref="NoSuchAbility"/> when there was nothing to
    /// remove — which is also what a nameless ability answers.
    /// </returns>
    public string RemoveAbility()
    {
        if (Running is not { } running || running.Name.Length == 0)
        {
            return NoSuchAbility;
        }

        ability.Remove(Depth - 1);
        removed.Add(running.Name);
        return running.Parameter;
    }

    private readonly List<string> removed = [];

    /// <summary>
    /// Abilities a script asked to have removed, in order.
    /// </summary>
    /// <remarks>
    /// The reference deletes from the record's own list; this port has no writable specab store on
    /// a record yet, so the requests are recorded and the caller applies them. Named rather than
    /// silently dropped.
    /// </remarks>
    public IReadOnlyList<string> Removed => removed;

    /// <summary>What kind of record this script came from.</summary>
    public GpdlScriptSource Source { get; set; }

    /// <summary>The name of the record it came from (<c>sourceName</c>).</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>The word <c>$SA_SOURCE_TYPE()</c> answers (<c>Specab.cpp:231</c>).</summary>
    public static string NameOf(GpdlScriptSource source) => source switch
    {
        GpdlScriptSource.Class => "CLASS",
        GpdlScriptSource.Item => "ITEM",
        GpdlScriptSource.Race => "RACE",
        GpdlScriptSource.Baseclass => "BASECLASS",
        GpdlScriptSource.Spell => "SPELL",
        GpdlScriptSource.Character => "CHARACTER",
        GpdlScriptSource.Monster => "MONSTER",
        GpdlScriptSource.Combatant => "COMBATANT",
        GpdlScriptSource.Aura => "AURA",
        GpdlScriptSource.Event => "EVENT",
        GpdlScriptSource.EventTrigger => "EVENT TRIGGER",
        _ => "Unknown",
    };

    /// <summary>What the reference's alert says (<c>GPDLexec.cpp:5683</c>).</summary>
    public static string MessageFor(GpdlContext which) => which switch
    {
        GpdlContext.Attacker => "$AttackerContext() called when no attacker context exists",
        GpdlContext.Target => "$TargetContext() called when no target context exists",
        GpdlContext.Combatant => "$CombatantContext() called when no combatant context exists",
        _ => "$MonsterTypeContext() called when no monster type context exists",
    };
}
