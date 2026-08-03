namespace UAF.Scripting;

/// <summary>Shape of the hook-parameter block (<c>NUMHOOKPARAM</c>, <c>Shared/Specab.h:576</c>).</summary>
public static class GpdlHookParameters
{
    /// <summary>Ten slots. Slot 0 is also where a global script's return value lands.</summary>
    public const int Count = 10;

    /// <summary>The slot <c>RunGlobalScript</c> writes its result into and returns.</summary>
    public const int ResultSlot = 0;
}

/// <summary>
/// Everything the GPDL VM needs from outside itself.
/// </summary>
/// <remarks>
/// <para>
/// In the C++ these are direct calls into engine globals (<c>party</c>, <c>globalData</c>,
/// <c>combatData</c>, the discourse text box, the Spencer regex engine). None of that exists in the
/// port yet, so the VM reaches them through this interface and
/// <see cref="GpdlUnhostedEnvironment"/> throws for the ones that have no defensible stand-in.
/// </para>
/// <para>
/// The distinction that matters: a member here is one the <i>bytecode-level</i> semantics depend on.
/// The ~250 character, party, combat and special-ability sub-opcodes are <b>not</b> here — they need
/// the data model from Phase 1 and the rules layer from the rest of Phase 2, and
/// <see cref="GpdlVirtualMachine"/> raises <see cref="NotSupportedException"/> for each with a
/// source citation rather than guessing.
/// </para>
/// </remarks>
/// <summary>
/// A stat a script can read off a character (the <c>GET_CHAR_*</c> family,
/// <c>GPDLexec.cpp:3200</c> onward).
/// </summary>
/// <remarks>
/// One enum rather than a method each: the sub-opcodes differ only in which accessor they call,
/// and the reference itself collapses most of them into two macros
/// (<c>GET_CHAR_INT</c>, <c>GET_CHAR_STRING</c>).
/// </remarks>
public enum GpdlCharStat
{
    /// <summary>The character's name (<c>GetName</c>).</summary>
    Name,

    /// <summary>
    /// Current hit points, <b>adjusted by active spell effects</b> as the reference's
    /// <c>GetAdjHitPoints</c> is. There is no unadjusted form in the sub-opcode set: a script
    /// asking for hit points always gets the adjusted number.
    /// </summary>
    HitPoints,

    /// <summary>Hit points at full health (<c>GetMaxHitPoints</c>).</summary>
    MaxHitPoints,

    /// <summary>
    /// Base armour class (<c>$GET_CHAR_AC</c> → <c>GetBaseAC</c>), before spell effects.
    /// </summary>
    ArmorClass,

    /// <summary>
    /// Armour class with spell effects applied (<c>$GET_CHAR_ADJAC</c> → <c>GetAdjAC</c>).
    /// </summary>
    /// <remarks>
    /// <c>$GET_CHAR_EFFAC</c> is a third form again — the <i>effective</i> class, which also folds
    /// in the target's size and the attacker — and is not answered here.
    /// </remarks>
    AdjustedArmorClass,

    /// <summary>
    /// Total experience across every baseclass.
    /// </summary>
    /// <remarks>
    /// <b>No sub-opcode reaches this one yet.</b> <c>$GET_CHAR_Exp</c> takes a baseclass argument
    /// and reports that class's experience alone (<c>GPDLexec.cpp:3218</c>), so it is not a plain
    /// stat; the total is here because a host can answer it and the eventual per-class call will
    /// want somewhere to sit.
    /// </remarks>
    Experience,

    /// <summary>The base to-hit number (<c>$GET_CHAR_THAC0</c> → <c>GetTHAC0</c>).</summary>
    Thac0,

    /// <summary>
    /// The to-hit number with bonuses and spell effects applied
    /// (<c>$GET_CHAR_ADJTHAC0</c> → <c>GetAdjTHAC0</c>).
    /// </summary>
    AdjustedThac0,

    /// <summary>Whether the character has enough experience to train (<c>GetAdjReadyToTrain</c>).</summary>
    ReadyToTrain,

    /// <summary>The character's gender, as the serialized numbering.</summary>
    Gender,
}

/// <summary>Which attribute store a GPDL script is reaching for.</summary>
public enum GpdlAslScope
{
    /// <summary>The design's global store (<c>globalData.global_asl</c>).</summary>
    Global,

    /// <summary>The party's own store (<c>party.party_asl</c>).</summary>
    Party,
}

public interface IGpdlHost
{
    /// <summary>
    /// True when the script was started from a discourse event, i.e. <c>m_pGPDLevent != NULL</c>.
    /// </summary>
    /// <remarks>
    /// This is not a convenience flag — it changes the stack. When it is false, <c>SUBOP_SAY</c>
    /// <b>does not pop its argument</b> (GPDLexec.cpp:5328 breaks before <c>m_popString1</c>) and
    /// pushes nothing; the balance is restored only because the compiler always emits a
    /// <c>SUBOP_POP</c> after a statement-level call. And <c>SUBOP_LISTEN</c> pushes an empty string
    /// instead of suspending.
    /// </remarks>
    bool HasDiscourse { get; }

    /// <summary>
    /// The player's most recent typed input (<c>m_listenText</c>). Empty when
    /// <see cref="HasDiscourse"/> is false — GPDLexec.cpp:5064 clears it in that case.
    /// </summary>
    string ListenText { get; }

    /// <summary>Displays a line of discourse text; only called when <see cref="HasDiscourse"/>.</summary>
    void Say(string text);

    /// <summary>
    /// <c>$GREP(pattern, string)</c>. The original compiles <paramref name="pattern"/> with the
    /// vendored Spencer regex engine in egrep mode after upper-casing both sides
    /// (GPDLexec.cpp:4382). <b>Not</b> a .NET <c>Regex</c> dialect: the two disagree on
    /// backreferences, character classes and greediness, so substituting <c>Regex</c> would change
    /// which <c>$RESPOND</c> arm fires.
    /// </summary>
    bool Grep(string pattern, string text);

    /// <summary>
    /// <c>$GET_HOOK_PARAM(n)</c> — one slot of the current hook-parameter block
    /// (<c>HOOK_PARAMETERS</c>, <c>Shared/Specab.h:580</c>).
    /// </summary>
    /// <remarks>
    /// <b>The block is a stack, not a global.</b> Its constructor pushes itself as the current one
    /// and its destructor pops, so a hook that runs another hook gets its own set and the caller's
    /// survives underneath. Slot 0 doubles as the <i>return value</i>:
    /// <c>RunGlobalScript</c> writes the script's result there and then returns it.
    /// </remarks>
    string GetHookParam(int index);

    /// <summary>
    /// <c>$SET_HOOK_PARAM(n, value)</c> — <b>a swap, not a setter</b>.
    /// </summary>
    /// <returns>
    /// The slot's <i>previous</i> contents, which the reference pushes back onto the stack
    /// (<c>GPDLexec.cpp:3213</c>). A script can therefore save a slot, borrow it and restore it,
    /// and one written as though this returned nothing leaves a value on the stack.
    /// </returns>
    /// <inheritdoc cref="GetHookParam"/>
    string SetHookParam(int index, string value);

    /// <summary>
    /// <c>$WIGGLE(n)</c> — the nth capture group from the last <see cref="Grep"/>, or empty when
    /// the group did not participate (GPDLexec.cpp:4409).
    /// </summary>
    string Wiggle(int group);

    /// <summary>
    /// Reads an attribute from a named store (<c>$GET_GLOBAL_ASL</c>, <c>$GET_PARTY_ASL</c>).
    /// </summary>
    /// <param name="scope">Which store — see <see cref="GpdlAslScope"/>.</param>
    /// <returns>
    /// The value, or <b>the empty string when the key is absent</b>. The reference's
    /// <c>Lookup</c> returns a reference to a shared empty string rather than signalling
    /// (<c>ASL.cpp:1089</c>), so a script cannot tell an unset attribute from one set to nothing.
    /// </returns>
    string GetAsl(GpdlAslScope scope, string key);

    /// <summary>
    /// Writes an attribute (<c>$SET_GLOBAL_ASL</c>, <c>$SET_PARTY_ASL</c>).
    /// </summary>
    /// <remarks>
    /// <b>Written with no flags at all.</b> <c>InsertGlobalASL</c> defaults its <c>flags</c>
    /// parameter to zero and the sub-opcode passes nothing (<c>GPDLexec.cpp:5501</c>), so a
    /// script-set attribute is never marked modified. It still reaches a save game — only
    /// read-only entries are excluded — so nothing observable turns on it, but the flag is not
    /// evidence a script wrote the value.
    /// </remarks>
    void SetAsl(GpdlAslScope scope, string key, string value);

    /// <summary>
    /// Reads an attribute from one character's store
    /// (<c>$GET_CHAR_ASL</c>, and <c>$IF_CHAR_ASL</c> — see the remarks).
    /// </summary>
    /// <param name="actor">
    /// The actor string the script pushed. The reference turns it into a character with
    /// <c>m_StringToActor</c> and complains to the player when it names nobody
    /// (<c>GPDLexec.cpp:908</c>); resolution is the host's business either way.
    /// </param>
    /// <remarks>
    /// <b><c>$IF_CHAR_ASL</c> is the same call, not a test.</b> Despite the name it pushes the
    /// <i>value</i> exactly as <c>$GET_CHAR_ASL</c> does (<c>GPDLexec.cpp:4452</c>) — there is no
    /// existence check anywhere in it, and the commented-out code above shows it was a lookup
    /// before too. A script using it as a boolean is really testing the value for emptiness, which
    /// is only accidentally the same thing.
    /// </remarks>
    string GetCharAsl(string actor, string key);

    /// <summary>Writes an attribute to one character's store (<c>$SET_CHAR_ASL</c>).</summary>
    void SetCharAsl(string actor, string key, string value);

    /// <summary>
    /// Reads a stat off a character (the <c>GET_CHAR_*</c> family).
    /// </summary>
    /// <returns>
    /// The value as a string, because GPDL's stack holds nothing else. An integer stat is
    /// formatted plainly; an actor that resolves to nobody yields the empty string.
    /// </returns>
    string GetCharStat(string actor, GpdlCharStat stat);

    /// <summary>Whether an attribute exists (<c>$IF_PARTY_ASL</c>).</summary>
    bool HasAsl(GpdlAslScope scope, string key);

    /// <summary>Removes an attribute (<c>$DELETE_PARTY_ASL</c>).</summary>
    void DeleteAsl(GpdlAslScope scope, string key);

    /// <summary>
    /// <c>$RANDOM(n)</c>. The original is <c>RollDice(n, 1, 0.0) - 1</c> (GPDLexec.cpp:5094), i.e.
    /// a value in <c>[0, n)</c> drawn from the engine's shared generator — <b>not</b>
    /// <c>rand() % n</c>, which is what the commented-out line above it used to do.
    /// </summary>
    int Random(int sides);

    /// <summary><c>$DEBUG(x)</c> — logs and returns its argument (GPDLexec.cpp:3349).</summary>
    void Debug(string message);

    /// <summary><c>$DebugWrite(x)</c> — logs without consuming the stack (GPDLexec.cpp:3355).</summary>
    void DebugWrite(string message);
}

/// <summary>
/// A host for running scripts with no game around them: enough to execute pure computation, and an
/// explicit refusal for anything that would need the engine.
/// </summary>
/// <remarks>
/// This is the right host for compiler tests and for a <c>gpdlc</c>-style dry run. It is not a stub
/// to be filled in later with plausible values — a wrong <see cref="Grep"/> or
/// <see cref="Random"/> produces a trace that looks fine and is not the reference's, which is
/// exactly the failure mode the oracle exists to catch.
/// </remarks>
public class GpdlUnhostedEnvironment : IGpdlHost
{
    /// <summary>Lines that <see cref="Say"/> was asked to display, in order.</summary>
    public List<string> Said { get; } = [];

    /// <summary>Messages passed to <see cref="Debug"/> and <see cref="DebugWrite"/>, in order.</summary>
    public List<string> DebugLog { get; } = [];

    /// <summary>
    /// The attribute stores, by scope. Present rather than throwing because an unhosted script that
    /// reads an attribute should see an empty one, which is what a design with nothing set sees.
    /// </summary>
    public Dictionary<GpdlAslScope, Dictionary<string, string>> Attributes { get; } = new()
    {
        [GpdlAslScope.Global] = new(StringComparer.Ordinal),
        [GpdlAslScope.Party] = new(StringComparer.Ordinal),
    };

    /// <inheritdoc/>
    public virtual string GetAsl(GpdlAslScope scope, string key) =>
        Attributes[scope].TryGetValue(key, out string? value) ? value : string.Empty;

    /// <inheritdoc/>
    public virtual void SetAsl(GpdlAslScope scope, string key, string value) =>
        Attributes[scope][key] = value;

    /// <summary>Per-character stores, by whatever the actor string was.</summary>
    public Dictionary<string, Dictionary<string, string>> CharacterAttributes { get; } =
        new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public virtual string GetCharAsl(string actor, string key) =>
        CharacterAttributes.TryGetValue(actor, out var store)
        && store.TryGetValue(key, out string? value)
            ? value
            : string.Empty;

    /// <inheritdoc/>
    public virtual void SetCharAsl(string actor, string key, string value)
    {
        if (!CharacterAttributes.TryGetValue(actor, out var store))
        {
            store = new Dictionary<string, string>(StringComparer.Ordinal);
            CharacterAttributes[actor] = store;
        }

        store[key] = value;
    }

    /// <summary>Stat values by actor, for a script running with no game behind it.</summary>
    public Dictionary<string, Dictionary<GpdlCharStat, string>> CharacterStats { get; } =
        new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public virtual string GetCharStat(string actor, GpdlCharStat stat) =>
        CharacterStats.TryGetValue(actor, out var stats)
        && stats.TryGetValue(stat, out string? value)
            ? value
            : string.Empty;

    /// <inheritdoc/>
    public virtual bool HasAsl(GpdlAslScope scope, string key) =>
        Attributes[scope].ContainsKey(key);

    /// <inheritdoc/>
    public virtual void DeleteAsl(GpdlAslScope scope, string key) =>
        Attributes[scope].Remove(key);

    /// <inheritdoc/>
    public virtual bool HasDiscourse => false;

    /// <inheritdoc/>
    public virtual string ListenText => string.Empty;

    /// <inheritdoc/>
    public virtual void Say(string text) => Said.Add(text);

    /// <summary>The hook-parameter block, ten slots as <c>NUMHOOKPARAM</c> gives it.</summary>
    /// <remarks>
    /// Held here rather than refused, because unlike <see cref="Grep"/> there is nothing external
    /// to port: the block is ten strings and a scope. Callers that need the reference's stacking
    /// discipline should use <c>UAFcore</c>'s <c>HookParameters</c>, which pushes and pops.
    /// </remarks>
    public string[] HookParameters { get; } = new string[GpdlHookParameters.Count];

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The reference guards only the upper bound here</b> (<c>GPDLexec.cpp:3198</c>) where its
    /// setter guards both, so a negative index reads off the front of the array. C# cannot
    /// reproduce that read; empty is returned instead, which is what the guarded path yields.
    /// </remarks>
    public virtual string GetHookParam(int index) =>
        index >= 0 && index < HookParameters.Length ? HookParameters[index] ?? string.Empty
                                                    : string.Empty;

    /// <inheritdoc/>
    public virtual string SetHookParam(int index, string value)
    {
        if (index < 0 || index >= HookParameters.Length)
        {
            return string.Empty;
        }

        string previous = HookParameters[index] ?? string.Empty;
        HookParameters[index] = value ?? string.Empty;
        return previous;
    }

    /// <inheritdoc/>
    public virtual bool Grep(string pattern, string text) =>
        throw new NotSupportedException(
            "$GREP needs the vendored Spencer regex engine (src/Shared/regexp.cpp, driven from " +
            "GPDLexec.cpp:4382). It is not ported; System.Text.RegularExpressions is a different " +
            "dialect and would silently change which patterns match.");

    /// <inheritdoc/>
    public virtual string Wiggle(int group) =>
        throw new NotSupportedException(
            "$WIGGLE returns capture groups from the last $GREP (GPDLexec.cpp:4409); $GREP is not " +
            "ported.");

    /// <inheritdoc/>
    public virtual int Random(int sides) =>
        throw new NotSupportedException(
            "$RANDOM calls the engine's shared RollDice (GPDLexec.cpp:5094), which lives in the " +
            "unported rules layer. Inject a host with a fixed sequence to get reproducible traces.");

    /// <inheritdoc/>
    public virtual void Debug(string message) => DebugLog.Add(message);

    /// <inheritdoc/>
    public virtual void DebugWrite(string message) => DebugLog.Add(message);
}
