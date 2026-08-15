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

    // ---- the ability scores, in three layers each ------------------------------------------------
    //
    // GPDL exposes every score as PERM, ADJ and LIMITED (GPDLexec.cpp:3696-3717): the stored
    // value, that value with spell effects applied, and the adjusted one clamped to the score's
    // own range. A script asking for the wrong layer gets a real but different answer -- the
    // adjusted form is unbounded and the limited one is what the rules read.

    PermanentStrength,
    AdjustedStrength,
    LimitedStrength,

    /// <summary>
    /// The strength percentile — a separate score with its own range, not a derived one.
    /// </summary>
    PermanentStrengthMod,

    /// <inheritdoc cref="PermanentStrengthMod"/>
    AdjustedStrengthMod,

    /// <inheritdoc cref="PermanentStrengthMod"/>
    LimitedStrengthMod,

    PermanentIntelligence,
    AdjustedIntelligence,
    LimitedIntelligence,

    PermanentWisdom,
    AdjustedWisdom,
    LimitedWisdom,

    PermanentDexterity,
    AdjustedDexterity,
    LimitedDexterity,

    PermanentConstitution,
    AdjustedConstitution,
    LimitedConstitution,

    PermanentCharisma,
    AdjustedCharisma,
    LimitedCharisma,

    // ---- the rest of the character block ---------------------------------------------------------

    /// <summary>Current age (<c>GetAdjAge</c>), with spell effects.</summary>
    Age,

    /// <summary>The age at which the character dies of it (<c>GetAdjMaxAge</c>).</summary>
    MaxAge,

    /// <summary>What is being carried (<c>GetEncumbrance</c>). <b>Not adjusted</b>.</summary>
    Encumbrance,

    /// <summary>What may be carried (<c>GetAdjMaxEncumbrance</c>).</summary>
    MaxEncumbrance,

    /// <summary>Movement rate (<c>GetAdjMaxMovement_GPDL</c>).</summary>
    MaxMovement,

    /// <summary>Morale (<c>GetAdjMorale</c>).</summary>
    Morale,

    /// <summary>Percentage magic resistance (<c>GetAdjMagicResistance</c>).</summary>
    MagicResistance,

    /// <summary>Damage bonus (<c>GetAdjDmgBonus</c>).</summary>
    DamageBonus,

    /// <summary>To-hit bonus (<c>GetAdjHitBonus</c>).</summary>
    HitBonus,

    /// <summary>Which portrait (<c>GetIconIndex</c>).</summary>
    IconIndex,

    /// <summary>The class id, as a string (<c>GetClass</c>).</summary>
    Class,

    /// <summary>The undead type, as a string (<c>GetUndeadType</c>).</summary>
    UndeadType,

    /// <summary>Alignment, as its ordinal (<c>GetAdjAlignment</c>).</summary>
    Alignment,

    /// <summary>Condition, as its ordinal (<c>GetAdjStatus</c>).</summary>
    Status,

    /// <summary>Creature size, as its ordinal (<c>GetAdjSize</c>).</summary>
    Size,

    /// <summary>
    /// Hit dice (<c>GetNbrHD</c>). <b>Formatted to eight decimal places</b> — see
    /// <c>GET_CHAR_FLOAT</c>, <c>GPDLexec.cpp:2285</c>.
    /// </summary>
    HitDice,

    /// <inheritdoc cref="HitDice"/>
    NumberOfAttacks,

    // ---- The sixteen creature traits (GPDLexec.cpp:3493 onward) -------------------------------
    //
    // These are BOOLs, not numbers, and they all reach the same place: CHARACTER::IsMammal and
    // its fifteen siblings test one bit of one of the FOUR unrelated bitfields a MONSTER_DATA
    // carries -- form, penalty, immunity and misc options (Monster.h:60-126).
    //
    // *** THE DEFAULT FOR A NON-MONSTER IS NOT ALWAYS FALSE. ***
    //
    // Each accessor tests `GetType() == MONSTER_TYPE` first and returns a literal for anything
    // else. Fourteen return FALSE -- but IsMammal and CanBeHeldOrCharmed return TRUE, because a
    // player character IS a mammal and CAN be held or charmed. A host that answered false for
    // everything would make hold-person fail against the whole party, which is a rules bug that
    // looks like a spell bug.

    /// <summary>
    /// <c>$GET_ISMAMMAL</c> — <c>FormMammal</c>. <b>TRUE</b> for a non-monster.
    /// </summary>
    IsMammal,

    /// <summary><c>$GET_ISANIMAL</c> — <c>FormAnimal</c>. False for a non-monster.</summary>
    IsAnimal,

    /// <summary><c>$GET_ISSNAKE</c> — <c>FormSnake</c>. False for a non-monster.</summary>
    IsSnake,

    /// <summary><c>$GET_ISGIANT</c> — <c>FormGiant</c>. False for a non-monster.</summary>
    IsGiant,

    /// <summary>
    /// <c>$GET_ISALWAYSLARGE</c> — <c>FormLarge</c>, the "large even if 1x1" flag. False for a
    /// non-monster.
    /// </summary>
    IsAlwaysLarge,

    /// <summary><c>$GET_HASDEATHIMMUNITY</c> — <c>ImmuneDeath</c>.</summary>
    HasDeathImmunity,

    /// <summary><c>$GET_HASPOISONIMMUNITY</c> — <c>ImmunePoison</c>.</summary>
    HasPoisonImmunity,

    /// <summary><c>$GET_HASCONFUSIONIMMUNITY</c> — <c>ImmuneConfusion</c>.</summary>
    HasConfusionImmunity,

    /// <summary><c>$GET_HASVORPALIMMUNITY</c> — <c>ImmuneVorpal</c>.</summary>
    HasVorpalImmunity,

    /// <summary><c>$GET_HASDWARFACPENALTY</c> — <c>PenaltyDwarfAC</c>.</summary>
    HasDwarfArmorClassPenalty,

    /// <summary><c>$GET_HASDWARFTHAC0PENALTY</c> — <c>PenaltyDwarfTHAC0</c>.</summary>
    HasDwarfThac0Penalty,

    /// <summary><c>$GET_HASGNOMEACPENALTY</c> — <c>PenaltyGnomeAC</c>.</summary>
    HasGnomeArmorClassPenalty,

    /// <summary><c>$GET_HASGNOMETHAC0PENALTY</c> — <c>PenaltyGnomeTHAC0</c>.</summary>
    HasGnomeThac0Penalty,

    /// <summary><c>$GET_HASRANGERDMGPENALTY</c> — <c>PenaltyRangerDmg</c>.</summary>
    HasRangerDamagePenalty,

    /// <summary>
    /// <c>$GET_CANBEHELDORCHARMED</c> — <c>OptionCanBeHeldCharmed</c>. <b>TRUE</b> for a
    /// non-monster.
    /// </summary>
    CanBeHeldOrCharmed,

    /// <summary>
    /// <c>$GET_AFFECTEDBYDISPELEVIL</c> — <c>OptionAffectedByDispelEvil</c>. False for a
    /// non-monster.
    /// </summary>
    AffectedByDispelEvil,
}

/// <summary>
/// A value a script can read or write about the party (the <c>GET/SET_PARTY_*</c> family,
/// <c>GPDLexec.cpp:5551</c> onward).
/// </summary>
public enum GpdlPartyValue
{
    /// <summary>Days elapsed (<c>party.days</c>).</summary>
    Days,

    /// <summary>Hours (<c>party.hours</c>). <b>Not clamped on write</b> — see the setter.</summary>
    Hours,

    /// <summary>Minutes (<c>party.minutes</c>). Also unclamped.</summary>
    Minutes,

    /// <summary>
    /// The whole clock as minutes: <c>(days * 24 + hours) * 60 + minutes</c>. Writing it
    /// decomposes back into the three.
    /// </summary>
    Time,

    /// <summary>
    /// Which member is active. <b>Read and written in different units</b> — see
    /// <see cref="IGpdlHost.GetPartyValue"/>.
    /// </summary>
    ActiveCharacter,

    /// <summary>How many are in the party (<c>PARTYSIZE</c>).</summary>
    Size,

    /// <summary>Which way the party faces (<c>party.facing</c>).</summary>
    Facing,
}

/// <summary>Which combatant a selector wants (the <c>NEAREST</c>/<c>DAMAGED</c> family).</summary>
public enum GpdlCombatantQuery
{
    /// <summary>The closest combatant of any side (<c>GetNearestTo</c>).</summary>
    Nearest,

    /// <summary>The closest on the other side (<c>GetNearestEnemyTo</c>).</summary>
    NearestEnemy,

    /// <summary>Whoever last struck this one (<c>LAST_ATTACKER_OF</c>).</summary>
    LastAttacker,
}

/// <summary>Which side, and which end of the wound scale, a damage selector wants.</summary>
public enum GpdlDamageQuery
{
    MostDamagedEnemy,
    MostDamagedFriendly,
    LeastDamagedEnemy,
    LeastDamagedFriendly,
}

/// <summary>
/// A field of an item's database record a script can read (the <c>DAT_Item_*</c> family,
/// <c>GPDLexec.cpp:6471</c>).
/// </summary>
public enum GpdlItemField
{
    /// <summary>The name shown before the item is identified.</summary>
    CommonName,

    /// <summary>The name shown after it is.</summary>
    IdName,

    /// <summary>The AI's use priority.</summary>
    Priority,

    MaxRange,
    MediumRange,
    ShortRange,

    /// <summary>
    /// Damage against a small target, as <c>"$dice$sides$bonus"</c> — three numbers in one
    /// <c>$</c>-delimited string.
    /// </summary>
    DamageSmall,

    /// <inheritdoc cref="DamageSmall"/>
    DamageLarge,

    AttackBonus,
}

/// <summary>Which attribute store a GPDL script is reaching for.</summary>
public enum GpdlAslScope
{
    /// <summary>The design's global store (<c>globalData.global_asl</c>).</summary>
    Global,

    /// <summary>The party's own store (<c>party.party_asl</c>).</summary>
    Party,
}

/// <summary>
/// Which layer of a map square an override applies to (<c>OVERRIDE_TYPE</c>,
/// <c>GlobalData.h:471</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every square of every level carries four bytes per side — one for each of these — that let a
/// script repaint the map while the game runs. The five sub-opcode pairs
/// (<c>$GetWall</c>/<c>$SetWall</c> and friends) differ only in which of these they pass.
/// </para>
/// <para>
/// <b>These are the "user" halves of a wider enum.</b> The reference's <c>OVERRIDE_TYPE</c> also
/// has <c>_INDEX</c> forms that shift the value by one to convert between what a designer numbers
/// from and what is stored. Scripts never reach those — every one of the ten sub-opcodes passes a
/// user form, where the adjustment is zero — so only these five are here.
/// </para>
/// </remarks>
public enum GpdlMapOverrideKind
{
    /// <summary>The wall drawn on that side (<c>WALL_OVERRIDE_USER</c>).</summary>
    Wall = 0,

    /// <summary>A door in it (<c>DOOR_OVERRIDE_USER</c>).</summary>
    Door = 1,

    /// <summary>What is behind it (<c>BACKGROUND_OVERRIDE_USER</c>).</summary>
    Background = 2,

    /// <summary>Something drawn over it (<c>OVERLAY_OVERRIDE_USER</c>).</summary>
    Overlay = 3,

    /// <summary>
    /// Whether it can be walked through (<c>BLOCKAGE_OVERRIDE</c>).
    /// </summary>
    /// <remarks>
    /// The one kind with no <c>_INDEX</c> twin, because it is a fact rather than a picture number.
    /// </remarks>
    Blockage = 4,
}

/// <summary>Values every map override shares.</summary>
public static class GpdlMapOverride
{
    /// <summary>
    /// "No override here" — and the largest value a square can hold.
    /// </summary>
    /// <remarks>
    /// <b>One value doing two jobs.</b> A square stores a single byte, so 255 has to mean both the
    /// top of the range and the absence of an entry. Reading an unset square gives it; writing it
    /// clears the square instead of storing 255; and a read that fails for any other reason — no
    /// such level, a row or column that was never allocated — gives it as well.
    /// </remarks>
    public const int None = 255;
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
    /// <c>$GET_PARTY_FACING()</c> — the party's facing as a bare number
    /// (<c>GPDLexec.cpp:5550</c>).
    /// </summary>
    /// <remarks>
    /// Zero through three, north-clockwise. The one built-in placement script branches on
    /// <c>&gt;=# 2</c>, which is south or west.
    /// </remarks>
    int PartyFacing { get; }

    /// <summary>
    /// <c>$MonsterPlacement(turtleCode)</c> — runs a turtle program against the arrangement now
    /// being placed (<c>MonsterPlacementCallback</c>, <c>UAFWin/Combatants.cpp:2680</c>).
    /// </summary>
    /// <remarks>
    /// <b>Only meaningful while a placement hook is running.</b> The reference guards on
    /// <c>monsterArrangement.active</c> and returns <c>"0"</c> with a debug complaint when a
    /// script calls it at any other time, rather than refusing outright.
    /// </remarks>
    string MonsterPlacement(string turtleCode);

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

    /// <summary>
    /// Writes a stat back to a character (the <c>SET_CHAR_*</c> family, <c>GPDLexec.cpp:5417</c>).
    /// </summary>
    /// <remarks>
    /// <b>The value arrives as text, because the stack holds nothing else</b> — the reference pops
    /// it through <c>m_popInteger1</c> for the numeric setters and <c>m_popString1</c> for the
    /// others, and the sub-opcode is what decides which. A host that cannot write the named stat
    /// does nothing; the call still yields the empty string either way, because
    /// <c>m_SetCharInt</c> ends in <c>m_pushEmptyString</c>.
    /// </remarks>
    void SetCharStat(string actor, GpdlCharStat stat, string value);

    /// <summary>
    /// Reads a party value.
    /// </summary>
    /// <remarks>
    /// <b><see cref="GpdlPartyValue.ActiveCharacter"/> is read and written in different units.</b>
    /// Reading gives the active member's <c>uniquePartyID</c>; writing takes an <i>index</i> and
    /// wraps it with <c>% numCharacters</c> (<c>GPDLexec.cpp:5588</c>). So a script cannot round-trip
    /// it, and feeding a read straight back into the write lands somewhere arbitrary.
    /// </remarks>
    string GetPartyValue(GpdlPartyValue value);

    /// <summary>
    /// Writes a party value.
    /// </summary>
    /// <remarks>
    /// <b>The clock fields are not clamped.</b> <c>SET_LITERAL_INT</c> assigns straight through, so
    /// a script may set hours to 99 or minutes to −5 and the party's clock simply holds it.
    /// <see cref="GpdlPartyValue.Facing"/> is the one that clamps, and
    /// <see cref="GpdlPartyValue.Size"/> cannot be written at all.
    /// </remarks>
    void SetPartyValue(GpdlPartyValue value, string setting);

    /// <summary>
    /// Where the party is, as <c>"/level/x/y"</c> (<c>GPDLexec.cpp:5551</c>).
    /// </summary>
    /// <remarks>
    /// <b>A string with a leading slash, and the level is one-based</b> — <c>party.level + 1</c>,
    /// where every other level reference in the engine is zero-based.
    /// </remarks>
    string PartyLocation { get; }

    /// <summary>
    /// Every character's money, totalled and optionally converted
    /// (<c>$GET_PARTY_MONEYAVAILABLE</c>, <c>GPDLexec.cpp:4215</c>).
    /// </summary>
    /// <param name="coinType">
    /// 0 for the raw total in the base coin; 1 to 10 to convert into that denomination.
    /// <b>Anything else answers zero</b> rather than falling back to the total.
    /// </param>
    int MoneyAvailable(int coinType);

    /// <summary>
    /// Runs the scripts a character's own special abilities carry
    /// (<c>$RUN_CHAR_SCRIPTS</c>, <c>GPDLexec.cpp:5098</c>).
    /// </summary>
    /// <remarks>
    /// <b>The character's own abilities, not a spell's.</b> <c>RunCharacterScripts</c>
    /// (<c>Char.h:1237</c>) hands the character's <c>specAbs</c> straight to
    /// <c>SPECIAL_ABILITIES::RunScripts</c> — so this is the plainest of the family.
    /// </remarks>
    /// <returns>The last script's result, or empty when no ability carried one.</returns>
    string RunCharacterScripts(string actor, string scriptName);

    /// <summary>
    /// Runs the scripts carried by the <i>spells</i> currently affecting a character
    /// (<c>$RUN_CHAR_SE_SCRIPTS</c>, <c>GPDLexec.cpp:5144</c>).
    /// </summary>
    /// <remarks>
    /// <b>A different set from <see cref="RunCharacterScripts"/> entirely.</b>
    /// <c>RunSEScripts</c> (<c>Char.cpp:11537</c>) walks the character's active spell effects,
    /// finds each one's source spell, and runs <i>that spell's</i> abilities — so an unaffected
    /// character runs nothing however many abilities it has of its own. The results are
    /// concatenated here rather than overwritten, which is the one place in the family that
    /// accumulates.
    /// </remarks>
    string RunSpellEffectScripts(string actor, string scriptName);

    /// <summary>
    /// Runs one named ability's script (<c>$CALL_GLOBAL_SCRIPT</c>, <c>GPDLexec.cpp</c>).
    /// </summary>
    /// <param name="abilityName">The ability to look up, rather than a set to walk.</param>
    string CallGlobalScript(string abilityName, string scriptName);

    /// <summary>
    /// A value from the design's configuration (<c>$GET_CONFIG</c>, <c>GPDLexec.cpp</c>).
    /// </summary>
    /// <returns>The value, or empty for a token the design does not set.</returns>
    string ConfigValue(string token);

    /// <summary>
    /// Teaches or unteaches a spell (<c>$KNOW_SPELL</c>, <c>GPDLexec.cpp:6141</c>).
    /// </summary>
    /// <param name="know">True to add it, false to take it away.</param>
    /// <returns>
    /// Whether it worked. <b>A spell the design has no record of is refused</b> — the reference
    /// looks it up before touching the character, so a typo answers false rather than teaching a
    /// spell nothing can cast.
    /// </returns>
    bool KnowSpell(string actor, string spellId, bool know);

    /// <summary>
    /// Removes the spell effects a named script put on an actor
    /// (<c>$REMOVE_SPELL_EFFECT</c>).
    /// </summary>
    /// <returns>Whatever the removal reports — the reference pushes its result string.</returns>
    string RemoveSpellEffect(string actor, string scriptName);

    /// <summary>
    /// Writes an actor's special abilities to the debug log
    /// (<c>$DUMP_CHARACTER_SAS</c>).
    /// </summary>
    /// <remarks>A diagnostic: it changes nothing and is only ever read in a log.</remarks>
    void DumpCharacterSpecialAbilities(string actor);

    /// <summary>
    /// Sets the picture the current event shows (<c>$SMALL_PICTURE</c>).
    /// </summary>
    /// <remarks>
    /// Does nothing when no event is running, which is the reference's own guard — it returns
    /// early on a null event after pushing the filename back.
    /// </remarks>
    void SmallPicture(string fileName);

    /// <summary>
    /// Pauses (<c>$SLEEP</c>).
    /// </summary>
    /// <remarks>
    /// <b>The reference blocks the thread outright</b> with <c>Sleep(ms)</c>, which stops the
    /// whole game. A host is free to do something less drastic; the sub-opcode's own contract is
    /// only that it yields the empty string.
    /// </remarks>
    void Sleep(int milliseconds);

    /// <summary>
    /// Whether an actor is under a named spell
    /// (<c>$IS_AFFECTED_BY_SPELL</c>, <c>GPDLexec.cpp:4456</c>).
    /// </summary>
    /// <remarks>
    /// Matches on the effect's <i>source spell</i>, so it answers "is this spell on them", not
    /// "does anything on them do what this spell does". A spell the design does not have is
    /// refused before the effects are walked at all.
    /// </remarks>
    bool IsAffectedBySpell(string actor, string spellId);

    /// <summary>
    /// Whether an actor is under anything carrying a named attribute
    /// (<c>$IS_AFFECTED_BY_SPELL_ATTR</c>).
    /// </summary>
    /// <remarks>
    /// <b>It falls back to the character's own attributes.</b> The reference walks the effects
    /// looking for a source spell whose ASL holds the name — and if none does, returns whether the
    /// <i>character's</i> ASL holds it (<c>Char.cpp:11414</c>). So an attribute a character
    /// carries innately answers true with no spell involved, which the name does not suggest.
    /// </remarks>
    bool IsAffectedBySpellAttribute(string actor, string attribute);

    /// <summary>
    /// Adds a timed attribute change to the current character
    /// (<c>$MODIFY_CHAR_ATTRIBUTE</c>, <c>GPDLexec.cpp:5459</c>).
    /// </summary>
    /// <param name="attribute">An attribute name — <c>STR</c>, <c>INT</c> and their siblings.</param>
    /// <param name="amount">How much to add; negative subtracts.</param>
    /// <param name="minutes">
    /// How long it lasts. <b>Minutes are the only unit the reference accepts</b> — anything else
    /// logs a warning and adds nothing at all, so the caller has already refused it.
    /// </param>
    /// <param name="text">What the character sheet shows for it.</param>
    /// <param name="source">
    /// A label the effect is later found by. <see cref="RemoveCharacterModification"/> matches
    /// against this, so it is the effect's handle rather than decoration.
    /// </param>
    void ModifyCharacterAttribute(string attribute, int amount, int minutes,
                                  string text, string source);

    /// <summary>
    /// Removes one timed change whose source matches <paramref name="mask"/>
    /// (<c>$REMOVE_CHAR_MODIFICATION</c>).
    /// </summary>
    /// <returns>Whether one was found. <b>At most one goes</b>, however many match.</returns>
    bool RemoveCharacterModification(string mask);

    /// <summary>The stage a quest is at, or zero when the design has no such quest.</summary>
    int QuestStage(string quest);

    /// <summary>Sets a quest's stage (<c>$SET_QUEST</c>, <c>GPDLexec.cpp:5613</c>).</summary>
    void SetQuestStage(string quest, int stage);

    /// <summary>
    /// Gives an actor one bundle of an item (<c>$GIVE_CHAR_ITEM</c>, <c>GPDLexec.cpp:4243</c>).
    /// </summary>
    /// <remarks>
    /// <b>A bundle, not a piece.</b> The reference passes <c>GetItemBundleQty(itemID)</c> as the
    /// quantity, so giving arrows gives the whole bundle the design defined — the same rule the
    /// FRUA importer's carried items follow.
    /// </remarks>
    /// <returns>Whether the item existed and could be carried.</returns>
    bool GiveItem(string actor, string itemId);

    /// <summary>
    /// Takes one bundle of an item back (<c>$TAKE_CHAR_ITEM</c>, <c>GPDLexec.cpp:4274</c>).
    /// </summary>
    /// <remarks>
    /// <b>The first matching item goes, unless the script's own item is among them.</b> The
    /// reference walks the inventory keeping the first match, but prefers one whose key equals the
    /// script context's item key — so a script running <i>from</i> an item takes that copy rather
    /// than an arbitrary duplicate.
    /// </remarks>
    /// <returns>Whether the actor had one.</returns>
    bool TakeItem(string actor, string itemId);

    /// <summary>
    /// What kind of thing an actor is (<c>$GET_CHAR_TYPE</c>, <c>GPDLexec.cpp:4155</c>).
    /// </summary>
    /// <remarks>
    /// <b>Not a number, and not symmetrical.</b> A player character answers the literal
    /// <c>"@PC@"</c> and an NPC <c>"@NPC@"</c> — the at-signs are part of the value — but a
    /// monster answers its own <c>monsterID</c>, so the call is a type test for two of the three
    /// kinds and an identity for the third. Anything unresolved answers empty.
    /// </remarks>
    string CharacterType(string actor);

    /// <summary>
    /// An actor's race (<c>$GET_CHAR_RACE</c>, <c>GPDLexec.cpp:3450</c>).
    /// </summary>
    /// <remarks>
    /// <b>An unresolved actor answers <c>"NoSuchCharacter"</c>, not empty.</b> That string is the
    /// reference's, and a script can test for it — so returning empty would silently turn "who?"
    /// into "no race".
    /// </remarks>
    string CharacterRace(string actor);

    /// <summary>
    /// Changes an actor's race, if the design has one by that name
    /// (<c>$SET_CHAR_RACE</c>).
    /// </summary>
    /// <returns>
    /// Whether the race existed. The reference pushes the name back on success and the empty
    /// string when the design has no such race, so a script can tell the difference.
    /// </returns>
    bool SetCharacterRace(string actor, string race);

    /// <summary>
    /// What the vaults hold (<c>$GET_VAULT_MONEYAVAILABLE</c>, <c>GPDLexec.cpp:4189</c>).
    /// </summary>
    /// <param name="coinType">
    /// <b>Zero means "do not convert"</b> and gives the base-currency total; 1–10 converts into
    /// that denomination. Anything else is not a denomination and gives zero.
    /// </param>
    int VaultMoneyAvailable(int coinType);

    /// <summary>
    /// Writes a level's attribute (<c>$SET_LEVEL_STATS_ASL</c>, <c>GPDLexec.cpp:5522</c>).
    /// </summary>
    /// <param name="level">
    /// The <b>one-based</b> level number, or a negative for "wherever the party is". The reference
    /// takes an empty <i>string</i> to mean the current level and <c>atoi</c>s anything else — so
    /// a caller that passes nothing gets the current level rather than level zero.
    /// </param>
    void SetLevelAsl(int level, string key, string value);

    /// <summary>Reads a level's attribute.</summary>
    /// <inheritdoc cref="SetLevelAsl" path="/param[@name='level']"/>
    string GetLevelAsl(int level, string key);

    /// <summary>Deletes a level's attribute (<c>$DELETE_LEVEL_STATS_ASL</c>).</summary>
    /// <inheritdoc cref="SetLevelAsl" path="/param[@name='level']"/>
    void DeleteLevelAsl(int level, string key);

    /// <summary>
    /// Reads one of a map square's overrides (<c>GetMapOverride</c>,
    /// <c>GlobalData.cpp:2513</c>) — what <c>$GetWall</c> and its four siblings do.
    /// </summary>
    /// <param name="kind">Which of the five layers.</param>
    /// <param name="level">
    /// The <b>one-based</b> level. Outside 1–255 the reference answers
    /// <see cref="GpdlMapOverride.None"/> rather than reading anything.
    /// </param>
    /// <param name="x">Column, <b>wrapped</b> into the level's width — see the remarks.</param>
    /// <param name="y">Row, wrapped into the level's height.</param>
    /// <param name="facing">Which of the square's four sides, wrapped into 0–3.</param>
    /// <returns>
    /// The stored value, or <see cref="GpdlMapOverride.None"/> when the square has no override —
    /// which is also the answer for a level, row or column that does not exist. <b>A script cannot
    /// tell "no override here" from "no such level"</b>; the reference collapses both to 255.
    /// </returns>
    /// <remarks>
    /// <b>Coordinates wrap rather than fail.</b> <c>x</c> and <c>y</c> are taken modulo the level's
    /// size and negatives are folded back up, so <c>-1</c> is the last column and a coordinate past
    /// the edge comes round the other side. This is the map's own toroidal geometry, not a bounds
    /// check, and a script relying on an out-of-range read returning nothing would be wrong.
    /// </remarks>
    int GetMapOverride(GpdlMapOverrideKind kind, int level, int x, int y, int facing);

    /// <summary>
    /// Writes one of a map square's overrides (<c>SetMapOverride</c>,
    /// <c>GlobalData.cpp:2530</c>) — <c>$SetWall</c> and its siblings.
    /// </summary>
    /// <inheritdoc cref="GetMapOverride" path="/param"/>
    /// <param name="value">
    /// What to store. <b>Clamped at 255</b>, which is itself the "no override" marker — so writing
    /// anything above 255 clears the square instead of setting it.
    /// </param>
    /// <remarks>
    /// Storage is allocated as it is written, but <b>only for a real value</b>: clearing a square
    /// that was never set allocates nothing. A level outside 1–255 is ignored silently.
    /// </remarks>
    void SetMapOverride(GpdlMapOverrideKind kind, int level, int x, int y, int facing, int value);

    /// <summary>
    /// The level the party is on, <b>one-based</b> (<c>$GET_GAME_CURRLEVEL</c>).
    /// </summary>
    /// <remarks>
    /// The reference pushes <c>currLevel + 1</c>: the stored index is zero-based and the script
    /// sees the number a designer would write.
    /// </remarks>
    int CurrentLevel { get; }

    /// <summary>
    /// The design's format version (<c>$GET_GAME_VERSION</c>).
    /// </summary>
    /// <remarks>
    /// <b>Formatted to eight decimal places</b> — <c>"%1.8f"</c>. A script comparing it against a
    /// literal is comparing strings, so the trailing zeroes are part of the answer.
    /// </remarks>
    string GameVersion { get; }

    /// <summary>
    /// Removes every spell effect on an actor cast at or below <paramref name="level"/>, and
    /// answers how many went (<c>$CHAR_REMOVEALLSPELLS</c>, <c>GPDLexec.cpp:3959</c>).
    /// </summary>
    /// <remarks>
    /// <b>Level is a ceiling, not a target.</b> The reference loops from the given level down to
    /// one, but each pass already takes everything at or below its argument — so every pass after
    /// the first finds nothing and the loop is a no-op. One sweep at the ceiling is the whole
    /// behaviour.
    /// </remarks>
    int RemoveSpellEffects(string actor, int level);

    /// <summary>
    /// The same sweep, but only over what a dispel can touch
    /// (<c>$CHAR_DISPELMAGIC</c>, <c>GPDLexec.cpp:4012</c>).
    /// </summary>
    /// <remarks>
    /// <b>Two things separate this from <see cref="RemoveSpellEffects"/>.</b> A spell effect is
    /// only taken when its spell is marked <c>CanBeDispelled</c> — so an undispellable curse
    /// survives a dispel and does not survive a remove. And an <i>item</i> special ability is
    /// taken when the level reaches <b>12</b>, whatever spell it came from, which is the only
    /// place that number appears in the family.
    /// </remarks>
    int DispelSpellEffects(string actor, int level);

    /// <summary>
    /// Clears the cursed flag on everything an actor carries
    /// (<c>$CHAR_REMOVEALLITEMCURSE</c>, <c>GPDLexec.cpp:4038</c>).
    /// </summary>
    /// <returns>Whether the actor resolved; the reference pushes false when it does not.</returns>
    bool RemoveItemCurses(string actor);

    /// <summary>
    /// The name of a coin denomination, by its <b>one-based</b> ordinal
    /// (<c>$COINNAME</c>, <c>GPDLexec.cpp:3325</c>).
    /// </summary>
    /// <remarks>
    /// <b>The ordinal is one-based and the table is not.</b> The reference subtracts one before
    /// indexing, so ordinal 1 is the first coin — and it does that subtraction without checking
    /// the lower bound, which is why this port refuses 0 rather than reading behind the array.
    /// </remarks>
    string CoinName(int ordinal);

    /// <summary>
    /// How many base coins one of this denomination is worth (<c>$COINRATE</c>).
    /// </summary>
    /// <remarks>A rate of zero means the design never configured that slot.</remarks>
    double CoinRate(int ordinal);

    /// <summary>
    /// How many coins of a denomination the current character is carrying
    /// (<c>$COINCOUNT</c>, <c>GPDLexec.cpp:3293</c>).
    /// </summary>
    /// <remarks>
    /// <b>Refused during combat.</b> The reference logs an interpreter error and pushes zero,
    /// because the call reads the party's active character and there is no such thing mid-fight.
    /// </remarks>
    int CoinCount(int ordinal);

    /// <summary>Whether an actor is in the party (<c>$InParty</c>, <c>GPDLexec.cpp:4483</c>).</summary>
    /// <remarks>An actor that resolves to nobody is false, not an error.</remarks>
    bool IsInParty(string actor);

    /// <summary>
    /// Moves the party (<c>$SET_PARTY_XY</c>, <c>GPDLexec.cpp:5287</c>).
    /// </summary>
    /// <remarks>
    /// <b>Queued rather than done.</b> The reference posts <c>TASKMSG_SetPartyXY</c> and the move
    /// happens when the task queue next runs — which is why the callers that care test
    /// <c>setPartyXY_x &gt;= 0</c> afterwards to see whether a script moved the party out from
    /// under them.
    /// </remarks>
    void SetPartyXY(int x, int y);

    // ---- combat ---------------------------------------------------------------------------------

    /// <summary>
    /// Whether a fight is running (<c>IsCombatActive</c>).
    /// </summary>
    /// <remarks>
    /// <b>Load-bearing for the stack, not just for the answer.</b> Several combat opcodes take an
    /// early exit when this is false that <i>pushes a result without popping its argument</i> —
    /// see <see cref="NearestTo"/>.
    /// </remarks>
    bool InCombat { get; }

    /// <summary>Which round the fight is on (<c>GetCombatRound</c>).</summary>
    /// <remarks>The editor build answers a hardcoded 3; this is the engine's own count.</remarks>
    int CombatRound { get; }

    /// <summary>
    /// A combatant's state as its display word (<c>CombatantStateText</c>).
    /// </summary>
    string CombatantState(string actor);

    /// <summary>
    /// A combatant's position (<c>$CombatantLocation</c>, <c>GPDLexec.cpp:5887</c>).
    /// </summary>
    /// <param name="axis">
    /// <c>"X"</c> for the column. <b>Anything else is taken as Y</b> — the reference tests only for
    /// <c>"X"</c> and falls through, so a typo'd axis silently answers the other one.
    /// </param>
    /// <returns>−1 when no fight is running or the id names nobody.</returns>
    int CombatantLocation(int combatant, string axis);

    /// <summary>
    /// Reads, sets or adds to a combatant's remaining attacks
    /// (<c>$COMBATANT_AVAILATTACKS</c>, <c>GPDLexec.cpp:6253</c>).
    /// </summary>
    /// <param name="function">0 assigns, 1 adds. <b>Any other value only reads.</b></param>
    /// <returns>The count after the change.</returns>
    int AvailableAttacks(string actor, int function, int value);

    /// <summary>Moves a combatant (<c>$TeleportCombatant</c>).</summary>
    void TeleportCombatant(int combatant, int x, int y);

    /// <summary>
    /// Finds a combatant relative to another (<c>$NEAREST_TO</c> and its two siblings).
    /// </summary>
    /// <returns>The actor string, or the null actor when there is nobody.</returns>
    string NearestTo(string actor, GpdlCombatantQuery query);

    /// <summary>Finds the most or least wounded combatant on a side.</summary>
    string MostDamaged(GpdlDamageQuery query);

    /// <summary>The actor string that means nobody (<c>NULL_ACTOR</c>).</summary>
    string NullActor { get; }

    /// <summary>
    /// An actor's index, as <c>$IndexOf</c> and <c>$MyIndex</c> report it
    /// (<c>m_IndexOf</c>, <c>GPDLexec.cpp:7546</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A string, and not always a number.</b> An actor with no valid instance answers the
    /// literal <c>"Invalid Context"</c> — so a script doing arithmetic on the result gets zero from
    /// <c>atoi</c> rather than an error, and a script comparing it against a number silently fails.
    /// The port keeps the literal because a design can test for it.
    /// </para>
    /// <para>
    /// <b>What the number counts depends on where the game is.</b> Out of combat it is the
    /// character's unique id, not a position in the party — the reference's own comment warns that
    /// "Instance is uniqueCharID for party, not 0..numCharacters". In combat it is the combat
    /// order. A combatant created mid-fight is offset by <c>NewCombatantInstanceOffset</c> (10000)
    /// so it cannot collide with either, and a character the party built during play answers
    /// <c>"-2"</c> whatever its instance is.
    /// </para>
    /// <para>
    /// This is why it is a host call rather than something the VM can work out: an actor string is
    /// opaque here, and only the host knows how to take it apart.
    /// </para>
    /// </remarks>
    string IndexOf(string actor);

    /// <summary>
    /// The drawing state the <c>$Gr*</c> calls share (<c>grc</c>, a single global).
    /// </summary>
    /// <remarks>
    /// <b>One per host, because it is one per engine.</b> The reference keeps a single
    /// <c>GR_CONTROL</c> and every script that draws uses it, so a sheet begun by one script and
    /// finished by another shares the cursor — which is the point.
    /// </remarks>
    GpdlGraphics Graphics { get; }

    /// <summary>
    /// Draws a line of text and answers how wide it was
    /// (<c>GrPrint</c>, <c>UAFWin/CharStatsForm.cpp:1489</c>).
    /// </summary>
    /// <param name="color">A <c>FONT_COLOR_NUM</c> ordinal — see <see cref="GpdlGraphics.Color"/>.</param>
    /// <returns>
    /// The advance width, which is what moves the cursor along. <b>A host that cannot measure text
    /// should answer zero</b> rather than estimate: everything on the line then overprints, which is
    /// at least visibly wrong, where a guessed width produces a sheet that looks plausible and is
    /// misaligned.
    /// </returns>
    int DrawText(string text, int x, int y, int color);

    /// <summary>
    /// What <c>$GET_CHAR_RACE</c> answers for an actor nobody recognises.
    /// </summary>
    /// <remarks>
    /// A literal the reference spells out (<c>GPDLexec.cpp:3460</c>), and one a script can test
    /// for — which is why it is not the empty string.
    /// </remarks>
    public const string NoSuchCharacter = "NoSuchCharacter";

    /// <summary>What <c>$GET_CHAR_TYPE</c> answers for a player character.</summary>
    /// <remarks>The at-signs are part of the value, not punctuation in the source.</remarks>
    public const string PlayerCharacterType = "@PC@";

    /// <summary>And for an NPC. A monster answers its own id instead.</summary>
    public const string NpcType = "@NPC@";

    /// <summary>
    /// The ambient actors a script reads with <c>$AttackerContext()</c> and its siblings.
    /// </summary>
    GpdlScriptContext Context { get; }

    /// <summary>
    /// A named record's special abilities (<c>$GET/SET/DELETE_&lt;record&gt;_SA</c>,
    /// <c>GPDLexec.cpp:2449</c>).
    /// </summary>
    /// <param name="who">
    /// The actor or database id. <b>Not the ambient context</b> — these thirteen calls name their
    /// record, where the nine <c>$SA_&lt;record&gt;_GET</c> lookups read whatever the context
    /// carries.
    /// </param>
    /// <returns>Null when the name reaches nothing, which the caller reports as the sentinel.</returns>
    SpecabList? Abilities(GpdlSaRecord record, string who);

    // ---- the databases --------------------------------------------------------------------------

    /// <summary>
    /// A field off an item's database record (<c>DAT_Item_*</c>).
    /// </summary>
    /// <remarks>
    /// <b>An id the design does not define answers the empty string.</b> The reference clears its
    /// scratch string in <c>m_GetItemData</c> before the lookup (<c>GPDLexec.cpp:1162</c>) — the
    /// guard that <c>GET_CHARACTER_SA</c> forgets — so this family is safe where that one is not.
    /// </remarks>
    string ItemField(string itemId, GpdlItemField field);

    /// <summary>
    /// A character's race height or weight (<c>DAT_Race_Height</c>, <c>Char.cpp:4591</c>).
    /// </summary>
    /// <remarks>
    /// <b>Rolled, not looked up.</b> The race's height is a dice field and this rolls it, so two
    /// calls about the same character give two answers. A script wanting a stable number has to
    /// keep the first one.
    /// </remarks>
    int RaceMeasurement(string actor, bool weight);

    /// <summary>
    /// The level a baseclass reaches at some experience, or the experience a level needs
    /// (<c>DAT_Baseclass_Level</c> and <c>_Experience</c>, <c>GPDLexec.cpp:4081</c>).
    /// </summary>
    int BaseclassProgression(string baseclassId, int value, bool wantExperience);

    /// <summary>
    /// A class's baseclasses, as a <c>$</c>-delimited string (<c>GPDLexec.cpp:4058</c>).
    /// </summary>
    /// <remarks>
    /// <b>The delimiter leads rather than separates.</b> Each name is appended after a <c>$</c>,
    /// so one baseclass is <c>"$fighter"</c> and a class the design does not define is <c>""</c> —
    /// never a bare name. The same convention carries the three numbers of
    /// <see cref="GpdlItemField.DamageSmall"/>.
    /// </remarks>
    string ClassBaseclasses(string classId);

    /// <summary>
    /// Runs a global script once per party member (<c>PARTY::ForEachPartyMember</c>,
    /// <c>Party.cpp:2528</c>).
    /// </summary>
    /// <param name="ability">The special ability the script hangs off.</param>
    /// <param name="script">The script's name within it.</param>
    /// <returns>
    /// <b>Only the last run's answer</b> — and since the loop counts <i>down</i>, that is the
    /// answer for party member zero. Every earlier member's result is overwritten and lost.
    /// </returns>
    string ForEachPartyMember(string ability, string script);

    /// <summary>
    /// Runs a script over everything an actor is carrying (<c>CHARACTER::ForEachPossession</c>,
    /// <c>Char.cpp:11594</c>).
    /// </summary>
    /// <returns>
    /// <b>Every item's answer, concatenated</b> — where
    /// <see cref="ForEachPartyMember"/> keeps only the last. Two walks, two conventions.
    /// </returns>
    string ForEachPossession(string actor, string script);

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

    // ---- auras ----------------------------------------------------------------------------------

    /// <summary>
    /// The combat's auras and its aura reference stack (<c>COMBAT_DATA</c>'s aura members).
    /// </summary>
    /// <remarks>
    /// <b>Thirteen of the fourteen opcodes read <see cref="AuraStore.Current"/> and nothing else.</b>
    /// Outside an aura script it is null, and each of them then takes a different malformed
    /// path — see the arms in <c>GpdlVirtualMachine</c>.
    /// </remarks>
    AuraStore Auras { get; }

    /// <summary>
    /// The combat an aura is placed in, for the create and placement sequences.
    /// </summary>
    IAuraWorld AuraWorld { get; }
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
    /// <inheritdoc/>
    public virtual bool InCombat => false;

    /// <inheritdoc/>
    public virtual int CombatRound => 0;

    /// <inheritdoc/>
    public virtual string CombatantState(string actor) => string.Empty;

    /// <inheritdoc/>
    public virtual int CombatantLocation(int combatant, string axis) => -1;

    /// <inheritdoc/>
    public virtual int AvailableAttacks(string actor, int function, int value) => 0;

    /// <summary>The last <c>$TeleportCombatant</c>, or null.</summary>
    public (int Combatant, int X, int Y)? Teleported { get; private set; }

    /// <inheritdoc/>
    public virtual void TeleportCombatant(int combatant, int x, int y) =>
        Teleported = (combatant, x, y);

    /// <inheritdoc/>
    public virtual string NearestTo(string actor, GpdlCombatantQuery query) => NullActor;

    /// <inheritdoc/>
    public virtual string MostDamaged(GpdlDamageQuery query) => NullActor;

    /// <inheritdoc/>
    public virtual string NullActor => string.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing here can take an actor string apart, so every actor is an invalid context — which
    /// is the reference's answer for one, rather than an invented number.
    /// </remarks>
    public virtual string IndexOf(string actor) => GpdlActorIndex.InvalidContext;

    /// <inheritdoc/>
    public GpdlGraphics Graphics { get; } = new();

    /// <summary>Every call this environment was asked to draw, in order.</summary>
    /// <remarks>
    /// Nothing renders here, so the calls are collected instead — which is what makes the layout
    /// testable without a font or a screen.
    /// </remarks>
    public List<(string Text, int X, int Y, int Color)> Drawn { get; } = [];

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Answers zero, because there is no font to measure with.</b> A sheet drawn against this
    /// environment overprints everything on a line rather than laying it out plausibly and wrongly
    /// — see the interface's remarks. Override to supply a width.
    /// </remarks>
    public virtual int DrawText(string text, int x, int y, int color)
    {
        Drawn.Add((text, x, y, color));
        return 0;
    }

    /// <inheritdoc/>
    public GpdlScriptContext Context { get; } = new();

    /// <summary>Ability lists this environment is holding, by record and name.</summary>
    public Dictionary<(GpdlSaRecord Record, string Who), SpecabList> AbilityLists { get; } = [];

    /// <summary>Each <c>$ForEachPartyMember</c> this environment was asked to run.</summary>
    public List<(string Ability, string Script)> PartyWalks { get; } = [];

    /// <inheritdoc/>
    public virtual string ForEachPartyMember(string ability, string script)
    {
        PartyWalks.Add((ability, script));
        return string.Empty;
    }

    /// <summary>Each <c>$ForEachPossession</c> this environment was asked to run.</summary>
    public List<(string Actor, string Script)> PossessionWalks { get; } = [];

    /// <inheritdoc/>
    public virtual string ForEachPossession(string actor, string script)
    {
        PossessionWalks.Add((actor, script));
        return string.Empty;
    }

    /// <summary>Item fields this environment is holding, by id and field.</summary>
    public Dictionary<(string Item, GpdlItemField Field), string> ItemFields { get; } = [];

    /// <inheritdoc/>
    public virtual string ItemField(string itemId, GpdlItemField field) =>
        ItemFields.TryGetValue((itemId, field), out string? value) ? value : string.Empty;

    /// <inheritdoc/>
    public virtual int RaceMeasurement(string actor, bool weight) => 0;

    /// <inheritdoc/>
    public virtual int BaseclassProgression(string baseclassId, int value, bool wantExperience) =>
        0;

    /// <inheritdoc/>
    public virtual string ClassBaseclasses(string classId) => string.Empty;

    /// <inheritdoc/>
    public virtual SpecabList? Abilities(GpdlSaRecord record, string who) =>
        AbilityLists.TryGetValue((record, who), out var list) ? list : null;

    /// <summary>Party values this environment is holding.</summary>
    public Dictionary<GpdlPartyValue, string> PartyValues { get; } = [];

    /// <inheritdoc/>
    public virtual string GetPartyValue(GpdlPartyValue value) =>
        PartyValues.TryGetValue(value, out string? held) ? held : "0";

    /// <inheritdoc/>
    public virtual void SetPartyValue(GpdlPartyValue value, string setting) =>
        PartyValues[value] = setting;

    /// <inheritdoc/>
    public virtual string PartyLocation => "/1/0/0";

    /// <inheritdoc/>
    public virtual int MoneyAvailable(int coinType) => 0;

    /// <inheritdoc/>
    public virtual string RunCharacterScripts(string actor, string scriptName) => string.Empty;

    /// <inheritdoc/>
    public virtual string RunSpellEffectScripts(string actor, string scriptName) => string.Empty;

    /// <inheritdoc/>
    public virtual string CallGlobalScript(string abilityName, string scriptName) => string.Empty;

    /// <inheritdoc/>
    public virtual string ConfigValue(string token) => string.Empty;

    /// <summary>Spells this environment was asked to teach or unteach.</summary>
    public List<(string Actor, string SpellId, bool Know)> SpellsTaught { get; } = [];

    /// <inheritdoc/>
    public virtual bool KnowSpell(string actor, string spellId, bool know)
    {
        SpellsTaught.Add((actor, spellId, know));
        return false;
    }

    /// <inheritdoc/>
    public virtual string RemoveSpellEffect(string actor, string scriptName) => string.Empty;

    /// <inheritdoc/>
    public virtual void DumpCharacterSpecialAbilities(string actor) =>
        DebugLog.Add($"$DUMP_CHARACTER_SAS({actor})");

    /// <summary>The picture this environment was last asked to show.</summary>
    public string? Picture { get; private set; }

    /// <inheritdoc/>
    public virtual void SmallPicture(string fileName) => Picture = fileName;

    /// <summary>How long this environment was asked to pause, in milliseconds.</summary>
    public List<int> Sleeps { get; } = [];

    /// <inheritdoc/>
    /// <remarks>Recorded rather than performed: a test that really slept would just be slow.</remarks>
    public virtual void Sleep(int milliseconds) => Sleeps.Add(milliseconds);

    /// <inheritdoc/>
    public virtual bool IsAffectedBySpell(string actor, string spellId) => false;

    /// <inheritdoc/>
    public virtual bool IsAffectedBySpellAttribute(string actor, string attribute) => false;

    /// <summary>Timed changes this environment was asked to add, newest last.</summary>
    public List<(string Attribute, int Amount, int Minutes, string Text, string Source)>
        Modifications
    { get; } = [];

    /// <inheritdoc/>
    public virtual void ModifyCharacterAttribute(string attribute, int amount, int minutes,
                                                 string text, string source) =>
        Modifications.Add((attribute, amount, minutes, text, source));

    /// <inheritdoc/>
    public virtual bool RemoveCharacterModification(string mask)
    {
        int index = Modifications.FindIndex(m => GpdlMask.Matches(mask, m.Source));

        if (index < 0)
        {
            return false;
        }

        Modifications.RemoveAt(index);
        return true;
    }

    /// <summary>Quest stages this environment was asked to keep.</summary>
    public Dictionary<string, int> QuestStages { get; } = [];

    /// <inheritdoc/>
    public virtual int QuestStage(string quest) => QuestStages.GetValueOrDefault(quest);

    /// <inheritdoc/>
    public virtual void SetQuestStage(string quest, int stage) => QuestStages[quest] = stage;

    /// <inheritdoc/>
    public virtual bool GiveItem(string actor, string itemId) => false;

    /// <inheritdoc/>
    public virtual bool TakeItem(string actor, string itemId) => false;

    /// <inheritdoc/>
    public virtual string CharacterType(string actor) => string.Empty;

    /// <inheritdoc/>
    public virtual string CharacterRace(string actor) => IGpdlHost.NoSuchCharacter;

    /// <inheritdoc/>
    public virtual bool SetCharacterRace(string actor, string race) => false;

    /// <inheritdoc/>
    public virtual int VaultMoneyAvailable(int coinType) => 0;

    /// <summary>Per-level attributes this environment was asked to keep, by level number.</summary>
    public Dictionary<int, Dictionary<string, string>> LevelAttributes { get; } = [];

    /// <inheritdoc/>
    public virtual void SetLevelAsl(int level, string key, string value)
    {
        if (!LevelAttributes.TryGetValue(level, out var attributes))
        {
            attributes = [];
            LevelAttributes[level] = attributes;
        }

        attributes[key] = value;
    }

    /// <inheritdoc/>
    public virtual string GetLevelAsl(int level, string key) =>
        LevelAttributes.TryGetValue(level, out var attributes)
        && attributes.TryGetValue(key, out string? value)
            ? value
            : string.Empty;

    /// <inheritdoc/>
    public virtual void DeleteLevelAsl(int level, string key)
    {
        if (LevelAttributes.TryGetValue(level, out var attributes))
        {
            attributes.Remove(key);
        }
    }

    /// <summary>
    /// Map overrides this environment was asked to keep, by layer, level, square and side.
    /// </summary>
    /// <remarks>
    /// A dictionary rather than the reference's grown-in-place rows, because without a level
    /// loaded there is no width or height to wrap coordinates into — see
    /// <see cref="GetMapOverride"/>.
    /// </remarks>
    public Dictionary<(GpdlMapOverrideKind Kind, int Level, int X, int Y, int Facing), int>
        MapOverrides { get; } = [];

    /// <summary>
    /// The level range a script may address, matching the reference's <c>MAX_LEVELS</c>.
    /// </summary>
    private static bool IsAddressableLevel(int level) => level is >= 1 and <= 255;

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Coordinates are not wrapped here, and that is a real difference.</b> The reference folds
    /// x and y into the level's width and height, so <c>-1</c> reads the far edge. Nothing is
    /// loaded in this environment, so there is no size to fold into; a script that relies on the
    /// wrap gets <see cref="GpdlMapOverride.None"/> instead. Only facing is wrapped, since four
    /// sides is fixed.
    /// </remarks>
    public virtual int GetMapOverride(GpdlMapOverrideKind kind, int level, int x, int y, int facing)
        => IsAddressableLevel(level)
           && MapOverrides.TryGetValue((kind, level, x, y, WrapFacing(facing)), out int value)
            ? value
            : GpdlMapOverride.None;

    /// <inheritdoc/>
    public virtual void SetMapOverride(
        GpdlMapOverrideKind kind, int level, int x, int y, int facing, int value)
    {
        if (!IsAddressableLevel(level))
        {
            return;
        }

        var square = (kind, level, x, y, WrapFacing(facing));

        // Writing the "none" marker clears the square rather than storing it -- the reference
        // refuses to allocate storage for it, so a square that was never set stays unset.
        if (value >= GpdlMapOverride.None)
        {
            MapOverrides.Remove(square);
            return;
        }

        MapOverrides[square] = value;
    }

    /// <summary>Folds a facing into the four sides a square has, negatives included.</summary>
    private static int WrapFacing(int facing)
    {
        int wrapped = facing % 4;
        return wrapped < 0 ? wrapped + 4 : wrapped;
    }

    /// <inheritdoc/>
    public virtual int CurrentLevel => 1;

    /// <inheritdoc/>
    public virtual string GameVersion => "0.00000000";

    /// <inheritdoc/>
    public virtual int RemoveSpellEffects(string actor, int level) => 0;

    /// <inheritdoc/>
    public virtual int DispelSpellEffects(string actor, int level) => 0;

    /// <inheritdoc/>
    public virtual bool RemoveItemCurses(string actor) => false;

    /// <inheritdoc/>
    public virtual string CoinName(int ordinal) => string.Empty;

    /// <inheritdoc/>
    public virtual double CoinRate(int ordinal) => 0.0;

    /// <inheritdoc/>
    public virtual int CoinCount(int ordinal) => 0;

    /// <inheritdoc/>
    public virtual bool IsInParty(string actor) => false;

    /// <summary>The last <c>$SET_PARTY_XY</c>, or null.</summary>
    public (int X, int Y)? PartyMovedTo { get; private set; }

    /// <inheritdoc/>
    public virtual void SetPartyXY(int x, int y) => PartyMovedTo = (x, y);

    /// <inheritdoc/>
    public virtual void SetCharStat(string actor, GpdlCharStat stat, string value)
    {
        if (!CharacterStats.TryGetValue(actor, out var stats))
        {
            stats = [];
            CharacterStats[actor] = stats;
        }

        stats[stat] = value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A trait nobody has set answers with <see cref="GpdlCharStats.NonMonsterTrait"/> rather than
    /// the empty string: an unhosted character is not a monster, and that is precisely the case
    /// the reference's accessors return a literal for.
    /// </remarks>
    public virtual string GetCharStat(string actor, GpdlCharStat stat)
    {
        if (CharacterStats.TryGetValue(actor, out var stats)
            && stats.TryGetValue(stat, out string? value))
        {
            return value;
        }

        return GpdlCharStats.IsTrait(stat)
            ? GpdlCharStats.NonMonsterTrait(stat)
            : string.Empty;
    }

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
    /// Zero unless a host overrides it, which is what an unhosted script sees.
    /// </remarks>
    public virtual int PartyFacing => 0;

    /// <inheritdoc/>
    /// <remarks>
    /// <b>"0" rather than a refusal</b>, matching the reference's answer when no arrangement is
    /// active — a script calling this outside a placement hook is a design error, not a port gap.
    /// </remarks>
    public virtual string MonsterPlacement(string turtleCode) => "0";

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

    /// <inheritdoc/>
    /// <remarks>
    /// <b>A real store, not a refusal.</b> An aura is self-contained — a mask, an ability list and
    /// ten strings — so the whole family works unhosted except for the parts that need combatants,
    /// which <see cref="AuraWorld"/> supplies.
    /// </remarks>
    public virtual AuraStore Auras { get; } = new(DefaultAuraCells);

    /// <inheritdoc/>
    /// <remarks>
    /// An empty combat: no combatants, so no aura ever fires an enter or exit script. Override to
    /// give a test some.
    /// </remarks>
    public virtual IAuraWorld AuraWorld => emptyCombat;

    /// <summary>
    /// The default mask size, <c>50 × 50</c> — what <c>MAX_TERRAIN_WIDTH</c> and
    /// <c>MAX_TERRAIN_HEIGHT</c> take when the design's config names neither
    /// (<c>Globals.cpp:2861</c>). The reference then clamps both to <c>[25, 500]</c>.
    /// </summary>
    public const int DefaultAuraMapWidth = 50;

    /// <inheritdoc cref="DefaultAuraMapWidth"/>
    public const int DefaultAuraMapHeight = 50;

    private const int DefaultAuraCells = DefaultAuraMapWidth * DefaultAuraMapHeight;

    private readonly EmptyCombat emptyCombat = new();

    private sealed class EmptyCombat : IAuraWorld
    {
        public int MapWidth => DefaultAuraMapWidth;

        public int MapHeight => DefaultAuraMapHeight;

        public int CombatantCount => 0;

        public (int X, int Y, AuraFacing Facing) Combatant(int index) =>
            (-1, -1, AuraFacing.North);

        public (int Width, int Height) CombatantFootprint(int index) => (1, 1);

        /// <summary>
        /// Open ground everywhere, so an unhosted annular aura draws its full wedge with nothing
        /// to stop it. That is a real shape, not a stub — the geometry needs no game.
        /// </summary>
        public AuraObstacle Obstacle(int x, int y) =>
            x >= 0 && y >= 0 && x < DefaultAuraMapWidth && y < DefaultAuraMapHeight
                ? AuraObstacle.None
                : AuraObstacle.OffMap;

        public void RunAuraScript(Aura aura, string scriptName, int combatantIndex)
        {
        }
    }
}

/// <summary>
/// The sixteen creature traits, and what a non-monster answers for each.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fourteen are false and two are true, and the two are the ones that matter.</b> Every trait
/// accessor on <c>CHARACTER</c> tests <c>GetType() == MONSTER_TYPE</c> and returns a literal for
/// anything else (<c>Char.cpp:17853</c> onward). <c>IsMammal</c> and <c>CanBeHeldOrCharmed</c>
/// return <c>TRUE</c> — a player character is a mammal, and can be held or charmed. A host that
/// answered false for all sixteen would make hold-person and charm fail against the entire party,
/// which presents as a spell bug rather than as a missing default.
/// </para>
/// <para>
/// Kept here rather than inside the unhosted environment so the real host answers the same way
/// without restating the table.
/// </para>
/// </remarks>
public static class GpdlCharStats
{
    /// <summary>Whether a stat is one of the sixteen creature traits.</summary>
    public static bool IsTrait(GpdlCharStat stat) => stat is
        GpdlCharStat.IsMammal or GpdlCharStat.IsAnimal or GpdlCharStat.IsSnake or
        GpdlCharStat.IsGiant or GpdlCharStat.IsAlwaysLarge or
        GpdlCharStat.HasDeathImmunity or GpdlCharStat.HasPoisonImmunity or
        GpdlCharStat.HasConfusionImmunity or GpdlCharStat.HasVorpalImmunity or
        GpdlCharStat.HasDwarfArmorClassPenalty or GpdlCharStat.HasDwarfThac0Penalty or
        GpdlCharStat.HasGnomeArmorClassPenalty or GpdlCharStat.HasGnomeThac0Penalty or
        GpdlCharStat.HasRangerDamagePenalty or GpdlCharStat.CanBeHeldOrCharmed or
        GpdlCharStat.AffectedByDispelEvil;

    /// <summary>
    /// What a creature that is not a monster answers for a trait, as GPDL sees it.
    /// </summary>
    /// <remarks>
    /// The reference pushes a <c>BOOL</c> through <c>m_pushInteger1</c>, so a script sees "1" or
    /// "0" — not "true", and not the empty string.
    /// </remarks>
    public static string NonMonsterTrait(GpdlCharStat stat) =>
        stat is GpdlCharStat.IsMammal or GpdlCharStat.CanBeHeldOrCharmed ? "1" : "0";
}

/// <summary>
/// The reference's <c>MatchMask</c> (<c>Char.cpp:12660</c>) — a <b>word</b> matcher, not a glob.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>*</c> matches one whitespace-delimited word, and nothing else is a wildcard.</b> The
/// mask and the data are both walked word by word: a mask word of exactly <c>*</c> skips the
/// data's word, and any other mask word must equal the data's word outright. A mask that runs out
/// matches whatever is left, so <c>"fire"</c> matches <c>"fire spell"</c> — but <c>"fire*"</c>
/// does not match <c>"firestorm"</c>, because that is one word and the mask is not <c>*</c>.
/// </para>
/// <para>
/// <b>Divergence: the reference walks off the end of the string.</b> Its skip loops test the
/// <i>pointer</i> against null rather than the character (<c>while (pData != 0)</c>), so a
/// <c>*</c> word with no trailing space reads past the terminator. This stops at the end of the
/// string instead; there is no value in the bytes beyond it to reproduce.
/// </para>
/// </remarks>
public static class GpdlMask
{
    /// <summary>Whether <paramref name="data"/> matches <paramref name="mask"/>.</summary>
    public static bool Matches(string? mask, string? data)
    {
        string m = mask ?? string.Empty;
        string d = data ?? string.Empty;
        int mi = 0;
        int di = 0;

        while (mi < m.Length)
        {
            while (mi < m.Length && m[mi] == ' ') { mi++; }

            // A mask with nothing left matches whatever the data still holds.
            if (mi >= m.Length) { return true; }

            while (di < d.Length && d[di] == ' ') { di++; }

            if (m[mi] == '*')
            {
                while (di < d.Length && d[di] != ' ') { di++; }
                while (mi < m.Length && m[mi] != ' ') { mi++; }
                continue;
            }

            while (mi < m.Length && m[mi] != ' ')
            {
                if (di >= d.Length || m[mi] != d[di]) { return false; }
                mi++;
                di++;
            }

            // The data's word has to end where the mask's did.
            if (di < d.Length && d[di] != ' ') { return false; }
        }

        return true;
    }
}
