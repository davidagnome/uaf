namespace UAF.Import.Frua;

/// <summary>
/// Translates the reader's FRUA enums into the engine's ordinals.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reader's enums model FRUA's own numbering; the engine's are its own.</b> Where the two
/// agree, a cast would work — and for three of the five here it very nearly does, which is exactly
/// what makes casting dangerous. Each translation below is derived from what the reference
/// <i>assigns</i>, not from what FRUA stores, because the assignment is the only place the two
/// numberings are ever put side by side.
/// </para>
/// <para>
/// The failure mode is silent: an off-by-one on <see cref="PartyAffect"/> turns a whole-party
/// effect into a single-character one, and the swapped pairs below turn a save that halves damage
/// into one that negates it. Nothing throws, nothing fails to load, and the design plays wrong.
/// </para>
/// </remarks>
public static class FruaEventEnums
{
    /// <summary>
    /// Who an effect reaches, as <c>eventPartyAffectType</c>.
    /// </summary>
    /// <remarks>
    /// <b>Off by one throughout.</b> The engine's list opens with <c>NoPartyMember</c>, which FRUA
    /// has no way to express, so its <c>EntireParty</c> is 1 where the reader's is 0. Casting
    /// straight through makes every whole-party damage event affect nobody.
    /// </remarks>
    public static int PartyAffect(FruaDamageTarget target) => target switch
    {
        FruaDamageTarget.EntireParty => 1,
        FruaDamageTarget.ActiveCharacter => 2,
        FruaDamageTarget.OneAtRandom => 3,
        FruaDamageTarget.ChanceOnEach => 4,
        _ => 1,
    };

    /// <summary>Whole party or active character only, as <c>eventPartyAffectType</c>.</summary>
    public static int PartyAffect(bool activeCharacterOnly) =>
        activeCharacterOnly ? 2 : 1;

    /// <summary>
    /// What a successful save does, as <c>spellSaveEffectType</c> (<c>GameRules.h:327</c>).
    /// </summary>
    /// <remarks>
    /// <b><c>SaveNegates</c> and <c>SaveForHalf</c> are the other way round in the engine.</b> Its
    /// order is <c>NoSave</c>, <c>SaveNegates</c>, <c>SaveForHalf</c>, <c>UseTHAC0</c>; the
    /// reader's follows FRUA's flag order instead. Only the two middle values are affected, so a
    /// spot-check on "no save" or "use THAC0" would find nothing wrong.
    /// </remarks>
    public static int SaveEffect(FruaDamageSave save) => save switch
    {
        FruaDamageSave.NoSave => 0,
        FruaDamageSave.SaveNegates => 1,
        FruaDamageSave.SaveForHalf => 2,
        FruaDamageSave.UseThac0 => 3,
        _ => 0,
    };

    /// <summary>
    /// Which saving-throw column, as <c>spellSaveVersusType</c> (<c>GameRules.h:330</c>).
    /// </summary>
    /// <remarks>
    /// <b>Breath and spell are swapped.</b> The engine's order is <c>ParPoiDM</c>,
    /// <c>PetPoly</c>, <c>RodStaffWand</c>, <c>Sp</c>, <c>BreathWeapon</c> — spell fourth and
    /// breath fifth. FRUA stores its five saving throws in the other order, which is the order the
    /// reader's enum follows, so the first three agree and the last two do not.
    /// </remarks>
    public static int SpellSaveVersus(FruaSpellSave save) => save switch
    {
        FruaSpellSave.ParalysisPoisonDeath => 0,
        FruaSpellSave.PetrifyPolymorph => 1,
        FruaSpellSave.RodStaffWand => 2,
        FruaSpellSave.Spell => 3,
        FruaSpellSave.BreathWeapon => 4,
        _ => 0,
    };

    /// <summary>
    /// Engagement range, as <c>eventDistType</c> (<c>GameEvent.h:63</c>).
    /// </summary>
    /// <remarks>
    /// The one enum here whose values <i>do</i> line up: <c>UpClose</c>, <c>Nearby</c>,
    /// <c>FarAway</c>. It is translated anyway so that the agreement is asserted rather than
    /// assumed — the engine's list continues with three internal-only values, and a future value
    /// inserted among the first three would otherwise go unnoticed.
    /// </remarks>
    public static int Distance(FruaCombatDistance distance) => distance switch
    {
        FruaCombatDistance.UpClose => 0,
        FruaCombatDistance.Nearby => 1,
        FruaCombatDistance.FarAway => 2,
        _ => 0,
    };

    /// <summary>Who is surprised, as <c>eventSurpriseType</c> — which does line up.</summary>
    public static int Surprise(FruaSurprise surprise) => surprise switch
    {
        FruaSurprise.Neither => 0,
        FruaSurprise.PartySurprised => 1,
        FruaSurprise.MonsterSurprised => 2,
        _ => 0,
    };

    /// <summary>
    /// What a question's button does after its chained event, as
    /// <c>labelPostChainOptionsType</c> (<c>GameEvent.h:319</c>).
    /// </summary>
    public static int PostChainAction(FruaChainAction action) => action switch
    {
        FruaChainAction.DoNothing => 0,
        FruaChainAction.ReturnToQuestion => 1,
        FruaChainAction.BackupOneStep => 2,
        _ => 0,
    };

    /// <summary>
    /// What an encounter button does, as <c>encounterButtonResultType</c>
    /// (<c>GameEvent.h:311</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every value differs.</b> <see cref="FruaEncounterResult"/>'s members are named for the
    /// engine's constants but numbered as FRUA <i>stores</i> them, and the two orders share no
    /// value at all: FRUA leads with <c>DecreaseRange</c> and ends with <c>NoResult</c>, the
    /// engine does the reverse, and the three combat results are permuted between. The reference
    /// spells the whole switch out five times over, once per button.
    /// </para>
    /// <para>
    /// <b>Stored 7 has no case.</b> The reference's switch covers 0–6, so a stored 7 leaves the
    /// button at whatever it was initialised with — not a value this port can reproduce. It
    /// resolves to <c>NoResult</c> here, the same refusal made for
    /// <see cref="FruaEventControlConverter.TriggerClassName"/>'s missing class.
    /// </para>
    /// </remarks>
    public static int EncounterResult(FruaEncounterResult result) => result switch
    {
        FruaEncounterResult.NoResult => 0,
        FruaEncounterResult.DecreaseRange => 1,
        FruaEncounterResult.CombatNoSurprise => 2,
        FruaEncounterResult.CombatSlowPartySurprised => 3,
        FruaEncounterResult.CombatSlowMonsterSurprised => 4,
        FruaEncounterResult.Talk => 5,
        FruaEncounterResult.EscapeIfFastPartyElseCombat => 6,
        _ => 0,
    };

    /// <summary>
    /// A price multiplier, as <c>costFactorType</c> (<c>Externs.h:842</c>).
    /// </summary>
    /// <remarks>
    /// The twenty values line up exactly, <c>Free</c> through <c>Mult100</c>. Translated anyway so
    /// the agreement is asserted rather than assumed — a cost factor is the difference between a
    /// free temple and one charging a hundredfold.
    /// </remarks>
    public static int CostFactor(FruaCostFactor factor) => (int)factor;

    /// <summary>The number of <c>costFactorType</c> values, for bounds-checking a cast.</summary>
    public const int CostFactorCount = 20;
}
