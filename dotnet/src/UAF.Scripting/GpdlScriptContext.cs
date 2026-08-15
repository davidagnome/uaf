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

/// <summary>
/// Which record's ability list a <c>$SA_&lt;record&gt;_GET</c> call reads
/// (<c>GPDLexec.cpp:3253</c>).
/// </summary>
/// <remarks>
/// <b>Nine records, one shape.</b> Each call is <c>SA_Param</c> over a different member of the
/// ambient context — the ability name is popped, looked up in that record's list, and its value
/// pushed. They differ in nothing but which list.
/// </remarks>
public enum GpdlSaRecord
{
    Item,
    Character,
    Combatant,
    Class,
    Baseclass,
    Spell,
    MonsterType,

    Race,
    Ability,
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


    // ---- The five the special-ability scripts read -----------------------------------------------
    //
    // These are set by whatever is running a script on someone's behalf, not by combat: when
    // SPECIAL_ABILITIES::RunScripts executes an ability's script it records what the script is
    // FOR, and $CharacterContext and its siblings read that back. A design's own scripts reach for
    // them immediately, which is what made them worth having.

    /// <summary>The character a script is running on behalf of (<c>$CharacterContext</c>).</summary>
    Character,

    /// <summary>The item, by its unique name (<c>$ItemContext</c>).</summary>
    Item,

    /// <summary>The spell, by its unique name (<c>$SpellContext</c>).</summary>
    Spell,

    /// <summary>The class (<c>$ClassContext</c>).</summary>
    Class,

    /// <summary>The race (<c>$RaceContext</c>).</summary>
    Race,
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

    // ---- the character the engine is operating on -----------------------------------------------

    private readonly List<string> actors = [];

    /// <summary>
    /// Pushes the character the engine is currently working on
    /// (<c>SetCharContext</c>, <c>RunTimeIF.cpp:84</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A different stack from <see cref="GpdlContext.Character"/>, and deliberately so.</b> The
    /// reference keeps two: <c>charContextStack</c>, pushed by whoever is operating on a character
    /// — rolling its stats, updating them, enabling its abilities — and the script context's own
    /// character, set when a script is run <i>for</i> someone. <c>$Myself</c> reads the first and
    /// <c>$CharacterContext</c> the second. They usually agree, which is exactly why collapsing
    /// them would be hard to notice and wrong in the cases that matter.
    /// </para>
    /// <para>
    /// Independent of <see cref="Push"/>: the engine pushes and pops this around operations that
    /// are not scripts at all.
    /// </para>
    /// </remarks>
    public IDisposable PushActor(string actor)
    {
        actors.Add(actor ?? string.Empty);
        return new PopActor(actors);
    }

    /// <summary>
    /// The character being operated on, or empty when nothing is
    /// (<c>GetCharContext</c>).
    /// </summary>
    /// <remarks>
    /// The reference puts an error box in front of the player for an empty stack and carries on
    /// with the null actor; the complaint is collected in <see cref="Missing"/> instead.
    /// </remarks>
    public string CurrentActor
    {
        get
        {
            if (actors.Count > 0 && actors[^1].Length > 0)
            {
                return actors[^1];
            }

            missing.Add("Missing Character Context");
            return string.Empty;
        }
    }

    /// <summary>Undoes one <see cref="PushActor"/>.</summary>
    private sealed class PopActor(List<string> stack) : IDisposable
    {
        private bool done;

        public void Dispose()
        {
            if (!done && stack.Count > 0)
            {
                stack.RemoveAt(stack.Count - 1);
                done = true;
            }
        }
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

    // ---- other records' abilities ----------------------------------------------------------------

    private readonly Dictionary<GpdlSaRecord, IReadOnlyDictionary<string, string>> lists = [];

    /// <summary>
    /// Puts a record's ability list on the context, for <c>$SA_&lt;record&gt;_GET</c> to read.
    /// </summary>
    /// <remarks>
    /// <b>Not scoped to a frame, unlike the actors.</b> The reference keeps these as pointers on
    /// the same <c>SCRIPT_CONTEXT</c>, so they follow the frame — but a caller sets them once and
    /// the port has no record-addressed store to point at, so they are held here until replaced.
    /// Named because it is a real difference: a nested script sees the outer one's record lists
    /// where the reference would have shown it none.
    /// </remarks>
    public void SetAbilities(GpdlSaRecord record, IReadOnlyDictionary<string, string>? abilities)
    {
        if (abilities is null)
        {
            lists.Remove(record);
        }
        else
        {
            lists[record] = abilities;
        }
    }

    /// <summary>
    /// One named ability's value off a record's list (<c>SA_Param</c>, <c>GPDLexec.cpp:1988</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A list that is not there and an ability that is not in it give the same answer</b> —
    /// <see cref="NoSuchAbility"/>. The reference distinguishes them only in what it logs.
    /// </para>
    /// <para>
    /// <b>And it logs the missing list once per process, not once per call.</b> The guard is a
    /// <c>static bool error</c> (<c>GPDLexec.cpp:1990</c>), so a design with a broken lookup in a
    /// loop gets one line and then silence. <see cref="MissingLists"/> keeps that shape.
    /// </para>
    /// </remarks>
    public string Ability(GpdlSaRecord record, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!lists.TryGetValue(record, out var abilities))
        {
            if (!loggedMissingList)
            {
                loggedMissingList = true;
                missingLists.Add(record);
            }

            return NoSuchAbility;
        }

        return abilities.TryGetValue(name, out string? value) ? value : NoSuchAbility;
    }

    private bool loggedMissingList;

    private readonly List<GpdlSaRecord> missingLists = [];

    /// <inheritdoc cref="Ability"/>
    public IReadOnlyList<GpdlSaRecord> MissingLists => missingLists;

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
        GpdlContext.Character => "$CharacterContext() called when no character context exists",
        GpdlContext.Item => "$ItemContext() called when no item context exists",
        GpdlContext.Spell => "$SpellContext() called when no spell context exists",
        GpdlContext.Class => "$ClassContext() called when no class context exists",
        GpdlContext.Race => "$RaceContext() called when no race context exists",
        _ => "$MonsterTypeContext() called when no monster type context exists",
    };
}
