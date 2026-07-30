namespace UAF.Scripting;

/// <summary>
/// One row of <c>SYSTEMFUNCTION systemfunctions[]</c> (GPDLcomp.cpp:1283).
/// </summary>
/// <param name="Name">Source spelling, always starting with '$'.</param>
/// <param name="ParameterCount">Formal parameter count; the compiler enforces it exactly.</param>
/// <param name="SubOp">The sub-opcode emitted for a call.</param>
/// <param name="Usage">Which script kinds may call it — a mask of <see cref="GpdlUsage"/>.</param>
/// <param name="Types">
/// Eight entries: return type followed by up to seven parameter types.
/// <b>The only value that has any effect is 1 (ACTOR).</b> GPDLcomp.cpp:752 declares
/// <c>STRING = 0, ACTOR = 1</c>, and every type test in the compiler is <c>!= 0</c>
/// (GPDLcomp.cpp:2576, :2886) — so a row saying <c>STRING</c> and a row saying <c>0</c> are
/// indistinguishable. An ACTOR-typed parameter is the special case: it must be a single system
/// function call returning ACTOR, not an arbitrary expression.
/// </param>
public sealed record GpdlSystemFunction(
    string Name,
    int ParameterCount,
    SubOp SubOp,
    int Usage,
    int[] Types);

/// <summary>Script-kind masks from GPDLcomp.h:14–22.</summary>
public static class GpdlUsage
{
    public const int All = 255;
    public const int Talk = 1;
    public const int Spell = 2;
    public const int LogicBlock = 4;
    public const int Event = 8;
    public const int Internal = 16;
    public const int SpecialAbilities = 32;
    public const int Combat = 64;
    public const int Graphics = 128;
}

/// <summary>
/// The system function table and the required-context table, generated from GPDLcomp.cpp so that
/// no row is mistyped.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order is load-bearing.</b> <c>DICTIONARY::localLookup</c> (GPDLcomp.cpp:1737) scans this
/// table linearly and takes the first name match, and <c>GPDLCOMP::list</c> (GPDLcomp.cpp:4068)
/// scans it to turn a sub-opcode back into a mnemonic — taking the first row whose sub-opcode
/// matches. Two names can share a sub-opcode (<c>$SET_CHAR_SEX</c> and <c>$SET_CHAR_GENDER</c> do
/// not, but <c>$GET_CHAR_ADJAC</c>-style aliases exist), so reordering changes listing output.
/// </para>
/// <para>
/// Only 5 of the 7 possible parameter types are ever installed on a definition:
/// <c>localLookup</c> copies <c>types[0..5]</c> only (GPDLcomp.cpp:1766, <c>j &lt; 6</c>), leaving
/// parameters 6 and 7 typeless. So the declared ACTOR types on the sixth and seventh parameters of
/// <c>$SpellAdj</c> and <c>$MODIFY_CHAR_ATTRIBUTE</c> are never enforced. That truncation is
/// reproduced in <c>GpdlCompiler</c>, not here.
/// </para>
/// </remarks>
public static class GpdlSystemFunctions
{
    /// <summary><c>MAX_FUNC_PARAMETERS</c>, GPDLcomp.cpp:15.</summary>
    public const int MaxFuncParameters = 7;

    /// <summary>Type code for an ACTOR-typed value (GPDLcomp.cpp:754).</summary>
    public const int TypeActor = 1;

    /// <summary>Type code for a plain string value; identical to "unspecified".</summary>
    public const int TypeString = 0;

    // Built on first use rather than in a static field initialiser: those run in declaration order,
    // and Table is declared at the bottom of this file.
    private static Dictionary<string, GpdlSystemFunction>? _byName;

    private static Dictionary<string, GpdlSystemFunction> ByName()
    {
        if (_byName is null)
        {
            var map = new Dictionary<string, GpdlSystemFunction>(StringComparer.Ordinal);
            foreach (var f in Table)
            {
                // First row wins, matching the linear scan in localLookup.
                map.TryAdd(f.Name, f);
            }
            _byName = map;
        }
        return _byName;
    }

    /// <summary>First row with the given name, or null — matching the linear scan in localLookup.</summary>
    public static GpdlSystemFunction? Find(string name) =>
        ByName().TryGetValue(name, out var f) ? f : null;

    /// <summary>
    /// First row with the given sub-opcode, or null. Used by the listing writer, which is the only
    /// consumer that goes this direction.
    /// </summary>
    public static GpdlSystemFunction? FindBySubOp(SubOp subOp)
    {
        foreach (var f in Table)
        {
            if (f.SubOp == subOp) { return f; }
        }
        return null;
    }

    /// <summary>
    /// The contexts a sub-opcode requires, or <see cref="GpdlContexts.None"/>
    /// (<c>requiredContexts[]</c>, GPDLcomp.cpp:1669).
    /// </summary>
    public static GpdlContexts RequiredContexts(SubOp subOp)
    {
        foreach (var (op, ctx) in RequiredContextTable)
        {
            if (op == subOp) { return ctx; }
        }
        return GpdlContexts.None;
    }

    /// <summary>The required-context table, in source order.</summary>
    public static readonly (SubOp SubOp, GpdlContexts Contexts)[] RequiredContextTable =
    [
        (SubOp.SUBOP_Myself, GpdlContexts.CTX_Myself),
        (SubOp.SUBOP_ClassContext, GpdlContexts.CTX_Class),
        (SubOp.SUBOP_RaceContext, GpdlContexts.CTX_Race),
        (SubOp.SUBOP_ItemContext, GpdlContexts.CTX_Item),
        (SubOp.SUBOP_SpellContext, GpdlContexts.CTX_Spell),
        (SubOp.SUBOP_SpellgroupContext, GpdlContexts.CTX_SpellGroup),
        (SubOp.SUBOP_MonsterTypeContext, GpdlContexts.CTX_MonsterType),
        (SubOp.SUBOP_AbilityContext, GpdlContexts.CTX_Ability),
        (SubOp.SUBOP_TraitContext, GpdlContexts.CTX_Trait),
        (SubOp.SUBOP_TargetContext, GpdlContexts.CTX_Target),
        (SubOp.SUBOP_AttackerContext, GpdlContexts.CTX_Attacker),
        (SubOp.SUBOP_CombatantContext, GpdlContexts.CTX_Combatant),
        (SubOp.SUBOP_CharacterContext, GpdlContexts.CTX_Character),
    ];

    /// <summary>The system function table, in GPDLcomp.cpp source order.</summary>
    public static readonly GpdlSystemFunction[] Table =
    [
        new("$LISTEN", 0, SubOp.SUBOP_LISTEN, GpdlUsage.Talk, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DEBUG", 1, SubOp.SUBOP_DEBUG, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DebugWrite", 1, SubOp.SUBOP_DebugWrite, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$PLUS", 2, SubOp.SUBOP_iPLUS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$MINUS", 2, SubOp.SUBOP_iMINUS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$TIMES", 2, SubOp.SUBOP_iTIMES, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DIV", 2, SubOp.SUBOP_iDIV, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$MOD", 2, SubOp.SUBOP_iMOD, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$EQUAL", 2, SubOp.SUBOP_iEQUAL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$LESS", 2, SubOp.SUBOP_iLESS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GREATER", 2, SubOp.SUBOP_iGREATER, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$TESTKEY", 1, SubOp.SUBOP_TESTKEY, GpdlUsage.Talk, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SAY", 1, SubOp.SUBOP_SAY, GpdlUsage.Talk, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$NUMERIC", 1, SubOp.SUBOP_NUMERIC, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$LISTENTEXT", 0, SubOp.SUBOP_LISTENTEXT, GpdlUsage.Talk, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$RANDOM", 1, SubOp.SUBOP_RANDOM, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GREP", 2, SubOp.SUBOP_GREP, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$WIGGLE", 1, SubOp.SUBOP_Wiggle, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_GLOBAL_ASL", 2, SubOp.SUBOP_SET_GLOBAL_ASL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_GLOBAL_ASL", 1, SubOp.SUBOP_GET_GLOBAL_ASL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$NOT", 1, SubOp.SUBOP_NOT, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DelimitedStringCount", 1, SubOp.SUBOP_DelimitedStringCount, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DelimitedStringSubstring", 2, SubOp.SUBOP_DelimitedStringSubstring, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DelimitedStringHead", 1, SubOp.SUBOP_DelimitedStringHead, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DelimitedStringTail", 1, SubOp.SUBOP_DelimitedStringTail, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DelimitedStringFilter", 3, SubOp.SUBOP_DelimitedStringFilter, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DelimitedStringAdd", 3, SubOp.SUBOP_DelimitedStringAdd, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$LENGTH", 1, SubOp.SUBOP_LENGTH, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_ASL", 3, SubOp.SUBOP_SET_CHAR_ASL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$PARTYSIZE", 0, SubOp.SUBOP_PARTYSIZE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_SEX", 1, SubOp.SUBOP_GET_CHAR_SEX, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_SEX", 2, SubOp.SUBOP_SET_CHAR_SEX, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ICON_INDEX", 1, SubOp.SUBOP_GET_CHAR_ICON_INDEX, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_ICON_INDEX", 2, SubOp.SUBOP_SET_CHAR_ICON_INDEX, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ASL", 2, SubOp.SUBOP_GET_CHAR_ASL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$IF_CHAR_ASL", 2, SubOp.SUBOP_IF_CHAR_ASL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_NAME", 1, SubOp.SUBOP_GET_CHAR_NAME, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_LEVEL_STATS_ASL", 3, SubOp.SUBOP_SET_LEVEL_STATS_ASL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DELETE_LEVEL_STATS_ASL", 2, SubOp.SUBOP_DELETE_LEVEL_STATS_ASL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_PARTY_ASL", 2, SubOp.SUBOP_SET_PARTY_ASL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_PARTY_ASL", 1, SubOp.SUBOP_GET_PARTY_ASL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$IF_PARTY_ASL", 1, SubOp.SUBOP_IF_PARTY_ASL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DELETE_PARTY_ASL", 1, SubOp.SUBOP_DELETE_PARTY_ASL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SMALL_PICTURE", 1, SubOp.SUBOP_SMALL_PICTURE, GpdlUsage.Talk, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_QUEST", 2, SubOp.SUBOP_SET_QUEST, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_AC", 1, SubOp.SUBOP_GET_CHAR_AC, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ADJAC", 1, SubOp.SUBOP_GET_CHAR_ADJAC, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_EFFAC", 1, SubOp.SUBOP_GET_CHAR_EFFAC, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_AC", 2, SubOp.SUBOP_SET_CHAR_AC, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_HITPOINTS", 1, SubOp.SUBOP_GET_CHAR_HITPOINTS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_HITPOINTS", 2, SubOp.SUBOP_SET_CHAR_HITPOINTS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_MAXHITPOINTS", 1, SubOp.SUBOP_GET_CHAR_MAXHITPOINTS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_MAXHITPOINTS", 2, SubOp.SUBOP_SET_CHAR_MAXHITPOINTS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_THAC0", 1, SubOp.SUBOP_GET_CHAR_THAC0, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ADJTHAC0", 1, SubOp.SUBOP_GET_CHAR_ADJTHAC0, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_THAC0", 2, SubOp.SUBOP_SET_CHAR_THAC0, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_RDYTOTRAIN", 1, SubOp.SUBOP_GET_CHAR_RDYTOTRAIN, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_RDYTOTRAIN", 2, SubOp.SUBOP_SET_CHAR_RDYTOTRAIN, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_Exp", 2, SubOp.SUBOP_GET_CHAR_Exp, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_RACE", 1, SubOp.SUBOP_GET_CHAR_RACE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_RACE", 2, SubOp.SUBOP_SET_CHAR_RACE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_Exp", 3, SubOp.SUBOP_SET_CHAR_Exp, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_AGE", 1, SubOp.SUBOP_GET_CHAR_AGE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_AGE", 2, SubOp.SUBOP_SET_CHAR_AGE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_MAXAGE", 1, SubOp.SUBOP_GET_CHAR_MAXAGE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_MAXAGE", 2, SubOp.SUBOP_SET_CHAR_MAXAGE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_MAXMOVE", 1, SubOp.SUBOP_GET_CHAR_MAXMOVE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_MAXMOVE", 2, SubOp.SUBOP_SET_CHAR_MAXMOVE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_LIMITED_STR", 1, SubOp.SUBOP_GET_CHAR_LIMITED_STR, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ADJ_STR", 1, SubOp.SUBOP_GET_CHAR_ADJ_STR, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_PERM_STR", 1, SubOp.SUBOP_GET_CHAR_PERM_STR, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_PERM_STR", 2, SubOp.SUBOP_SET_CHAR_PERM_STR, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$MODIFY_CHAR_ATTRIBUTE", 7, SubOp.SUBOP_MODIFY_CHAR_ATTRIBUTE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$REMOVE_CHAR_MODIFICATION", 2, SubOp.SUBOP_REMOVE_CHAR_MODIFICATION, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_LIMITED_STRMOD", 1, SubOp.SUBOP_GET_CHAR_LIMITED_STRMOD, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ADJ_STRMOD", 1, SubOp.SUBOP_GET_CHAR_ADJ_STRMOD, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_PERM_STRMOD", 1, SubOp.SUBOP_GET_CHAR_PERM_STRMOD, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_PERM_STRMOD", 2, SubOp.SUBOP_SET_CHAR_PERM_STRMOD, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_LIMITED_INT", 1, SubOp.SUBOP_GET_CHAR_LIMITED_INT, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ADJ_INT", 1, SubOp.SUBOP_GET_CHAR_ADJ_INT, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_PERM_INT", 1, SubOp.SUBOP_GET_CHAR_PERM_INT, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_PERM_INT", 2, SubOp.SUBOP_SET_CHAR_PERM_INT, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_LIMITED_WIS", 1, SubOp.SUBOP_GET_CHAR_LIMITED_WIS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ADJ_WIS", 1, SubOp.SUBOP_GET_CHAR_ADJ_WIS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_PERM_WIS", 1, SubOp.SUBOP_GET_CHAR_PERM_WIS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_PERM_WIS", 2, SubOp.SUBOP_SET_CHAR_PERM_WIS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_LIMITED_DEX", 1, SubOp.SUBOP_GET_CHAR_LIMITED_DEX, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ADJ_DEX", 1, SubOp.SUBOP_GET_CHAR_ADJ_DEX, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_PERM_DEX", 1, SubOp.SUBOP_GET_CHAR_PERM_DEX, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_PERM_DEX", 2, SubOp.SUBOP_SET_CHAR_PERM_DEX, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_LIMITED_CON", 1, SubOp.SUBOP_GET_CHAR_LIMITED_CON, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ADJ_CON", 1, SubOp.SUBOP_GET_CHAR_ADJ_CON, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_PERM_CON", 1, SubOp.SUBOP_GET_CHAR_PERM_CON, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_PERM_CON", 2, SubOp.SUBOP_SET_CHAR_PERM_CON, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_LIMITED_CHA", 1, SubOp.SUBOP_GET_CHAR_LIMITED_CHA, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ADJ_CHA", 1, SubOp.SUBOP_GET_CHAR_ADJ_CHA, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_PERM_CHA", 1, SubOp.SUBOP_GET_CHAR_PERM_CHA, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_PERM_CHA", 2, SubOp.SUBOP_SET_CHAR_PERM_CHA, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_MAXENC", 1, SubOp.SUBOP_GET_CHAR_MAXENC, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_MAXENC", 2, SubOp.SUBOP_SET_CHAR_MAXENC, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ENC", 1, SubOp.SUBOP_GET_CHAR_ENC, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_GENDER", 1, SubOp.SUBOP_GET_CHAR_GENDER, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_GENDER", 2, SubOp.SUBOP_SET_CHAR_GENDER, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_CLASS", 1, SubOp.SUBOP_GET_CHAR_CLASS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_CLASS", 2, SubOp.SUBOP_SET_CHAR_CLASS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_ALIGNMENT", 1, SubOp.SUBOP_GET_CHAR_ALIGNMENT, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_ALIGNMENT", 2, SubOp.SUBOP_SET_CHAR_ALIGNMENT, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_STATUS", 1, SubOp.SUBOP_GET_CHAR_STATUS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_STATUS", 2, SubOp.SUBOP_SET_CHAR_STATUS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_UNDEAD", 1, SubOp.SUBOP_GET_CHAR_UNDEAD, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_UNDEAD", 2, SubOp.SUBOP_SET_CHAR_UNDEAD, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_SIZE", 1, SubOp.SUBOP_GET_CHAR_SIZE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_SIZE", 2, SubOp.SUBOP_SET_CHAR_SIZE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_MAGICRESIST", 1, SubOp.SUBOP_GET_CHAR_MAGICRESIST, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_MAGICRESIST", 2, SubOp.SUBOP_SET_CHAR_MAGICRESIST, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_Lvl", 2, SubOp.SUBOP_GET_CHAR_Lvl, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_Lvl", 3, SubOp.SUBOP_SET_CHAR_Lvl, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_NBRHITDICE", 1, SubOp.SUBOP_GET_CHAR_NBRHITDICE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_NBRATTACKS", 1, SubOp.SUBOP_GET_CHAR_NBRATTACKS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_MORALE", 1, SubOp.SUBOP_GET_CHAR_MORALE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_MORALE", 2, SubOp.SUBOP_SET_CHAR_MORALE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_PARTY_FACING", 0, SubOp.SUBOP_GET_PARTY_FACING, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_PARTY_LOCATION", 0, SubOp.SUBOP_GET_PARTY_LOCATION, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_PARTY_FACING", 1, SubOp.SUBOP_SET_PARTY_FACING, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_PARTY_DAYS", 0, SubOp.SUBOP_GET_PARTY_DAYS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_PARTY_DAYS", 1, SubOp.SUBOP_SET_PARTY_DAYS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_PARTY_HOURS", 0, SubOp.SUBOP_GET_PARTY_HOURS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_PARTY_HOURS", 1, SubOp.SUBOP_SET_PARTY_HOURS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_PARTY_MINUTES", 0, SubOp.SUBOP_GET_PARTY_MINUTES, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_PARTY_MINUTES", 1, SubOp.SUBOP_SET_PARTY_MINUTES, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_PARTY_TIME", 0, SubOp.SUBOP_GET_PARTY_TIME, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_PARTY_TIME", 1, SubOp.SUBOP_SET_PARTY_TIME, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_PARTY_ACTIVECHAR", 0, SubOp.SUBOP_GET_PARTY_ACTIVECHAR, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_PARTY_ACTIVECHAR", 1, SubOp.SUBOP_SET_PARTY_ACTIVECHAR, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_GAME_CURRLEVEL", 0, SubOp.SUBOP_GET_GAME_CURRLEVEL, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_GAME_VERSION", 0, SubOp.SUBOP_GET_GAME_VERSION, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$COMBATANT_AVAILATTACKS", 3, SubOp.SUBOP_COMBATANT_AVAILATTACKS, GpdlUsage.Combat, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$Myself", 0, SubOp.SUBOP_Myself, GpdlUsage.All, [1, 0, 0, 0, 0, 0, 0, 0]),
        new("$Gender", 1, SubOp.SUBOP_Gender, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$Name", 1, SubOp.SUBOP_Name, GpdlUsage.All, [1, 0, 0, 0, 0, 0, 0, 0]),
        new("$ClassContext", 0, SubOp.SUBOP_ClassContext, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$RaceContext", 0, SubOp.SUBOP_RaceContext, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$ItemContext", 0, SubOp.SUBOP_ItemContext, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SpellContext", 0, SubOp.SUBOP_SpellContext, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SpellgroupContext", 0, SubOp.SUBOP_SpellgroupContext, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$MonsterTypeContext", 0, SubOp.SUBOP_MonsterTypeContext, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AbilityContext", 0, SubOp.SUBOP_AbilityContext, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$TraitContext", 0, SubOp.SUBOP_TraitContext, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$TargetContext", 0, SubOp.SUBOP_TargetContext, GpdlUsage.All, [1, 0, 0, 0, 0, 0, 0, 0]),
        new("$AttackerContext", 0, SubOp.SUBOP_AttackerContext, GpdlUsage.All, [1, 0, 0, 0, 0, 0, 0, 0]),
        new("$CombatantContext", 0, SubOp.SUBOP_CombatantContext, GpdlUsage.All, [1, 0, 0, 0, 0, 0, 0, 0]),
        new("$CharacterContext", 0, SubOp.SUBOP_CharacterContext, GpdlUsage.All, [1, 0, 0, 0, 0, 0, 0, 0]),
        new("$NextCreatureIndex", 2, SubOp.SUBOP_NextCreatureIndex, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$Status", 1, SubOp.SUBOP_Status, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$Alignment", 1, SubOp.SUBOP_Alignment, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$AlignmentGood", 1, SubOp.SUBOP_AlignmentGood, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$AlignmentEvil", 1, SubOp.SUBOP_AlignmentEvil, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$AlignmentLawful", 1, SubOp.SUBOP_AlignmentLawful, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$AlignmentNeutral", 1, SubOp.SUBOP_AlignmentNeutral, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$AlignmentChaotic", 1, SubOp.SUBOP_AlignmentChaotic, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$HitPoints", 1, SubOp.SUBOP_HitPoints, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$InParty", 1, SubOp.SUBOP_InParty, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$IsUndead", 1, SubOp.SUBOP_IsUndead, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$MIDDLE", 3, SubOp.SUBOP_MIDDLE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$IndexOf", 1, SubOp.SUBOP_IndexOf, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$IndexToActor", 1, SubOp.SUBOP_IndexToActor, GpdlUsage.All, [1, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_TYPE", 1, SubOp.SUBOP_GET_CHAR_TYPE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$COINNAME", 1, SubOp.SUBOP_COINNAME, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$COINCOUNT", 2, SubOp.SUBOP_COINCOUNT, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$COINRATE", 1, SubOp.SUBOP_COINRATE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_PARTY_MONEYAVAILABLE", 1, SubOp.SUBOP_GET_PARTY_MONEYAVAILABLE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_VAULT_MONEYAVAILABLE", 1, SubOp.SUBOP_GET_VAULT_MONEYAVAILABLE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$LAST_ATTACKER_OF", 1, SubOp.SUBOP_LAST_ATTACKER_OF, GpdlUsage.All, [1, 1, 0, 0, 0, 0, 0, 0]),
        new("$LAST_HITTER_OF", 0, SubOp.SUBOP_NOT_USED_FOR_ANYTHING1, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$LAST_TARGETER_OF", 0, SubOp.SUBOP_NOT_USED_FOR_ANYTHING2, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$LEAST_DAMAGED_FRIENDLY", 0, SubOp.SUBOP_LEAST_DAMAGED_FRIENDLY, GpdlUsage.All, [1, 0, 0, 0, 0, 0, 0, 0]),
        new("$MOST_DAMAGED_FRIENDLY", 0, SubOp.SUBOP_MOST_DAMAGED_FRIENDLY, GpdlUsage.All, [1, 0, 0, 0, 0, 0, 0, 0]),
        new("$NEAREST_TO", 1, SubOp.SUBOP_NEAREST_TO, GpdlUsage.All, [1, 1, 0, 0, 0, 0, 0, 0]),
        new("$NEAREST_ENEMY_TO", 1, SubOp.SUBOP_NEAREST_ENEMY_TO, GpdlUsage.All, [1, 1, 0, 0, 0, 0, 0, 0]),
        new("$LEAST_DAMAGED_ENEMY", 0, SubOp.SUBOP_LEAST_DAMAGED_ENEMY, GpdlUsage.All, [1, 0, 0, 0, 0, 0, 0, 0]),
        new("$MOST_DAMAGED_ENEMY", 0, SubOp.SUBOP_MOST_DAMAGED_ENEMY, GpdlUsage.All, [1, 0, 0, 0, 0, 0, 0, 0]),
        new("$IS_AFFECTED_BY_SPELL", 2, SubOp.SUBOP_IS_AFFECTED_BY_SPELL, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$IS_AFFECTED_BY_SPELL_ATTR", 2, SubOp.SUBOP_IS_AFFECTED_BY_SPELL_ATTR, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$CURR_CHANGE_BY_VAL", 0, SubOp.SUBOP_CURR_CHANGE_BY_VAL, GpdlUsage.Spell, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_ISMAMMAL", 1, SubOp.SUBOP_GET_ISMAMMAL, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_ISANIMAL", 1, SubOp.SUBOP_GET_ISANIMAL, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_ISSNAKE", 1, SubOp.SUBOP_GET_ISSNAKE, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_ISGIANT", 1, SubOp.SUBOP_GET_ISGIANT, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_ISALWAYSLARGE", 1, SubOp.SUBOP_GET_ISALWAYSLARGE, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_HASDWARFACPENALTY", 1, SubOp.SUBOP_GET_HASDWARFACPENALTY, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_HASGNOMEACPENALTY", 1, SubOp.SUBOP_GET_HASGNOMEACPENALTY, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_HASDWARFTHAC0PENALTY", 1, SubOp.SUBOP_GET_HASDWARFTHAC0PENALTY, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_HASGNOMETHAC0PENALTY", 1, SubOp.SUBOP_GET_HASGNOMETHAC0PENALTY, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_HASRANGERDMGPENALTY", 1, SubOp.SUBOP_GET_HASRANGERDMGPENALTY, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_HASPOISONIMMUNITY", 1, SubOp.SUBOP_GET_HASPOISONIMMUNITY, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_HASDEATHIMMUNITY", 1, SubOp.SUBOP_GET_HASDEATHIMMUNITY, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_HASCONFUSIONIMMUNITY", 1, SubOp.SUBOP_GET_HASCONFUSIONIMMUNITY, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_HASVORPALIMMUNITY", 1, SubOp.SUBOP_GET_HASVORPALIMMUNITY, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_CANBEHELDORCHARMED", 1, SubOp.SUBOP_GET_CANBEHELDORCHARMED, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_AFFECTEDBYDISPELEVIL", 1, SubOp.SUBOP_GET_AFFECTEDBYDISPELEVIL, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$SET_AFFECTEDBYDISPELEVIL", 2, SubOp.SUBOP_SET_AFFECTEDBYDISPELEVIL, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GIVE_CHAR_ITEM", 2, SubOp.SUBOP_GIVE_CHAR_ITEM, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$TAKE_CHAR_ITEM", 2, SubOp.SUBOP_TAKE_CHAR_ITEM, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$LOGIC_BLOCK_VALUE", 1, SubOp.SUBOP_LOGIC_BLOCK_VALUE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_DAMAGEBONUS", 1, SubOp.SUBOP_GET_CHAR_DAMAGEBONUS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_DAMAGEBONUS", 2, SubOp.SUBOP_SET_CHAR_DAMAGEBONUS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_HITBONUS", 1, SubOp.SUBOP_GET_CHAR_HITBONUS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_HITBONUS", 2, SubOp.SUBOP_SET_CHAR_HITBONUS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$CHAR_REMOVEALLSPELLS", 2, SubOp.SUBOP_CHAR_REMOVEALLSPELLS, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$MyIndex", 0, SubOp.SUBOP_MYINDEX, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$CastSpellOnTarget", 2, SubOp.SUBOP_CASTSPELLONTARGET, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$CastSpellOnTargetAs", 3, SubOp.SUBOP_CASTSPELLONTARGETAS, GpdlUsage.All, [0, 1, 0, 1, 0, 0, 0, 0]),
        new("$AddCombatant", 2, SubOp.SUBOP_AddCombatant, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$CHAR_DISPELMAGIC", 2, SubOp.SUBOP_CHAR_DISPELMAGIC, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$CHAR_REMOVEALLITEMCURSE", 1, SubOp.SUBOP_CHAR_REMOVEALLITEMCURSE, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHARACTER_SA", 2, SubOp.SUBOP_GET_CHARACTER_SA, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHARACTER_SA", 3, SubOp.SUBOP_SET_CHARACTER_SA, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$DELETE_CHARACTER_SA", 2, SubOp.SUBOP_DELETE_CHARACTER_SA, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$SA_CHARACTER_GET", 1, SubOp.SUBOP_SA_CHARACTER_GET, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_COMBATANT_SA", 2, SubOp.SUBOP_GET_COMBATANT_SA, GpdlUsage.Combat, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$SET_COMBATANT_SA", 3, SubOp.SUBOP_SET_COMBATANT_SA, GpdlUsage.Combat, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$DELETE_COMBATANT_SA", 2, SubOp.SUBOP_DELETE_COMBATANT_SA, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$DUMP_CHARACTER_SAS", 1, SubOp.SUBOP_DUMP_CHARACTER_SAS, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$SA_COMBATANT_GET", 1, SubOp.SUBOP_SA_COMBATANT_GET, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_ITEM_SA", 2, SubOp.SUBOP_GET_ITEM_SA, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_ITEM_GET", 1, SubOp.SUBOP_SA_ITEM_GET, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_SPELL_SA", 2, SubOp.SUBOP_GET_SPELL_SA, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_SPELL_GET", 1, SubOp.SUBOP_SA_SPELL_GET, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_MONSTERTYPE_SA", 2, SubOp.SUBOP_GET_MONSTERTYPE_SA, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_MONSTERTYPE_GET", 1, SubOp.SUBOP_SA_MONSTERTYPE_GET, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_RACE_SA", 2, SubOp.SUBOP_GET_RACE_SA, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_RACE_GET", 1, SubOp.SUBOP_SA_RACE_GET, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_ABILITY_SA", 2, SubOp.SUBOP_GET_ABILITY_SA, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_ABILITY_GET", 1, SubOp.SUBOP_SA_ABILITY_GET, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CLASS_SA", 2, SubOp.SUBOP_GET_CLASS_SA, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_BASECLASS_SA", 2, SubOp.SUBOP_GET_BASECLASS_SA, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_CLASS_GET", 1, SubOp.SUBOP_SA_CLASS_GET, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_BASECLASS_GET", 1, SubOp.SUBOP_SA_BASECLASS_GET, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_SPELL_Level", 1, SubOp.SUBOP_GET_SPELL_Level, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_SPELL_CanBeDispelled", 1, SubOp.SUBOP_GET_SPELL_CanBeDispelled, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_HOOK_PARAM", 1, SubOp.SUBOP_GET_HOOK_PARAM, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_HOOK_PARAM", 2, SubOp.SUBOP_SET_HOOK_PARAM, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CHAR_Ready", 3, SubOp.SUBOP_GET_CHAR_Ready, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$SET_CHAR_Ready", 3, SubOp.SUBOP_SET_CHAR_Ready, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$SA_PARAM_GET", 0, SubOp.SUBOP_SA_PARAM_GET, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_PARAM_SET", 1, SubOp.SUBOP_SA_PARAM_SET, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_NAME", 0, SubOp.SUBOP_SA_NAME, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_SOURCE_TYPE", 0, SubOp.SUBOP_SA_SOURCE_TYPE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_SOURCE_NAME", 0, SubOp.SUBOP_SA_SOURCE_NAME, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SA_REMOVE", 0, SubOp.SUBOP_SA_REMOVE, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$RUN_CHAR_SCRIPTS", 2, SubOp.SUBOP_RUN_CHAR_SCRIPTS, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SET_PARTY_XY", 2, SubOp.SUBOP_SET_PARTY_XY, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$CALL_GLOBAL_SCRIPT", 2, SubOp.SUBOP_CALL_GLOBAL_SCRIPT, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$VisualDistance", 2, SubOp.SUBOP_VisualDistance, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$TeleportCombatant", 3, SubOp.SUBOP_TeleportCombatant, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$CombatantLocation", 2, SubOp.SUBOP_CombatantLocation, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$IsLineOfSight", 5, SubOp.SUBOP_IsLineOfSight, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SetFriendly", 2, SubOp.SUBOP_SetFriendly, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GetFriendly", 2, SubOp.SUBOP_GetFriendly, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GetCombatRound", 0, SubOp.SUBOP_GetCombatRound, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Baseclass_Experience", 2, SubOp.SUBOP_DAT_Baseclass_Experience, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Baseclass_Level", 2, SubOp.SUBOP_DAT_Baseclass_Level, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Race_Weight", 1, SubOp.SUBOP_DAT_Race_Weight, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Race_Height", 1, SubOp.SUBOP_DAT_Race_Height, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Item_CommonName", 1, SubOp.SUBOP_DAT_Item_CommonName, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Item_IDName", 1, SubOp.SUBOP_DAT_Item_IDName, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Item_MaxRange", 1, SubOp.SUBOP_DAT_Item_MaxRange, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Item_MediumRange", 1, SubOp.SUBOP_DAT_Item_MediumRange, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Item_ShortRange", 1, SubOp.SUBOP_DAT_Item_ShortRange, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Item_Priority", 1, SubOp.SUBOP_DAT_Item_Priority, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Item_DamageSmall", 1, SubOp.SUBOP_DAT_Item_DamageSmall, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Item_DamageLarge", 1, SubOp.SUBOP_DAT_Item_DamageLarge, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Item_AttackBonus", 1, SubOp.SUBOP_DAT_Item_AttackBonus, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DAT_Class_Baseclasses", 1, SubOp.SUBOP_DAT_Class_Baseclasses, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_CONFIG", 1, SubOp.SUBOP_GET_CONFIG, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$ListAdjacentCombatants", 1, SubOp.SUBOP_ListAdjacentCombatants, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$ComputeAttackDamage", 2, SubOp.SUBOP_ComputeAttackDamage, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$UpCase", 1, SubOp.SUBOP_UpCase, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$Capitalize", 1, SubOp.SUBOP_Capitalize, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DownCase", 1, SubOp.SUBOP_DownCase, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_SPELLBOOK", 2, SubOp.SUBOP_GetSpellbook, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$SelectSpell", 2, SubOp.SUBOP_SelectSpell, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$Memorize", 1, SubOp.SUBOP_Memorize, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$KNOW_SPELL", 3, SubOp.SUBOP_KNOW_SPELL, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$MonsterPlacement", 1, SubOp.SUBOP_MonsterPlacement, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SetWall", 5, SubOp.SUBOP_SetWall, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SetBackground", 5, SubOp.SUBOP_SetBackground, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SetOverlay", 5, SubOp.SUBOP_SetOverlay, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SetDoor", 5, SubOp.SUBOP_SetDoor, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SetBlockage", 5, SubOp.SUBOP_SetBlockage, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GetWall", 4, SubOp.SUBOP_GetWall, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GetBackground", 4, SubOp.SUBOP_GetBackground, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GetOverlay", 4, SubOp.SUBOP_GetOverlay, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GetDoor", 4, SubOp.SUBOP_GetDoor, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GetBlockage", 4, SubOp.SUBOP_GetBlockage, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$RUN_CHAR_SE_SCRIPTS", 2, SubOp.SUBOP_RUN_CHAR_SE_SCRIPTS, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$REMOVE_SPELL_EFFECT", 2, SubOp.SUBOP_REMOVE_SPELL_EFFECT, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$RUN_AREA_SE_SCRIPTS", 2, SubOp.SUBOP_RUN_AREA_SE_SCRIPTS, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$RUN_CHAR_PS_SCRIPTS", 2, SubOp.SUBOP_RUN_CHAR_PS_SCRIPTS, GpdlUsage.Combat, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$IntegerTable", 4, SubOp.SUBOP_IntegerTable, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$ForEachPartyMember", 2, SubOp.SUBOP_ForEachPartyMember, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$ForEachPossession", 2, SubOp.SUBOP_ForEachPossession, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$GetCombatantState", 1, SubOp.SUBOP_GetCombatantState, GpdlUsage.Combat, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$IsIdentified", 3, SubOp.SUBOP_IsIdentified, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$SkillAdj", 5, SubOp.SUBOP_SkillAdj, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$SpellAdj", 7, SubOp.SUBOP_SpellAdj, GpdlUsage.All, [0, 1, 0, 0, 0, 0, 0, 0]),
        new("$SetMemorizeCount", 3, SubOp.SUBOP_SetMemorizeCount, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GetHighestLevelBaseclass", 1, SubOp.SUBOP_GetHighestLevelBaseclass, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GetBaseclassLevel", 2, SubOp.SUBOP_GetBaseclassLevel, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$RollHitPointDice", 3, SubOp.SUBOP_RollHitPointDice, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_Create", 5, SubOp.SUBOP_AURA_Create, GpdlUsage.Combat | GpdlUsage.Spell, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_Destroy", 0, SubOp.SUBOP_AURA_Destroy, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_AddSA", 2, SubOp.SUBOP_AURA_AddSA, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_GetSA", 1, SubOp.SUBOP_AURA_GetSA, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_RemoveSA", 1, SubOp.SUBOP_AURA_RemoveSA, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_Size", 4, SubOp.SUBOP_AURA_Size, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_Shape", 1, SubOp.SUBOP_AURA_Shape, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_Attach", 1, SubOp.SUBOP_AURA_Attach, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_Location", 2, SubOp.SUBOP_AURA_Location, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_Spell", 1, SubOp.SUBOP_AURA_Spell, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_Combatant", 1, SubOp.SUBOP_AURA_Combatant, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_Wavelength", 1, SubOp.SUBOP_AURA_Wavelength, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_GetData", 1, SubOp.SUBOP_AURA_GetData, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$AURA_SetData", 2, SubOp.SUBOP_AURA_SetData, GpdlUsage.Combat, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GrSet", 3, SubOp.SUBOP_GrSet, GpdlUsage.Graphics, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GrSetLinefeed", 1, SubOp.SUBOP_GrSetLinefeed, GpdlUsage.Graphics, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GrMoveTo", 1, SubOp.SUBOP_GrMoveTo, GpdlUsage.Graphics, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GrFormat", 1, SubOp.SUBOP_GrFormat, GpdlUsage.Graphics, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GrColor", 1, SubOp.SUBOP_GrColor, GpdlUsage.Graphics, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GrPrint", 1, SubOp.SUBOP_GrPrint, GpdlUsage.Graphics, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GrPrtLF", 1, SubOp.SUBOP_GrPrtLF, GpdlUsage.Graphics, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GrMark", 1, SubOp.SUBOP_GrMark, GpdlUsage.Graphics, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GrMove", 1, SubOp.SUBOP_GrMove, GpdlUsage.Graphics, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GrTab", 1, SubOp.SUBOP_GrTab, GpdlUsage.Graphics, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GrPic", 1, SubOp.SUBOP_GrPic, GpdlUsage.Graphics, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$ToHitComputation_Roll", 0, SubOp.SUBOP_ToHitComputation_Roll, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$GET_EVENT_Attribute", 2, SubOp.SUBOP_GET_EVENT_Attribute, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$SLEEP", 1, SubOp.SUBOP_SLEEP, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
        new("$DrawAdventureScreen", 0, SubOp.SUBOP_DRAWADVENTURESCREEN, GpdlUsage.All, [0, 0, 0, 0, 0, 0, 0, 0]),
    ];
}
