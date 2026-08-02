namespace UAF.Rules;

/// <summary>
/// Which of the five saving throws a spell is resisted with
/// (<c>spellSaveVersusType</c>, <c>GameRules.h:330</c>).
/// </summary>
/// <remarks>
/// Each names a skill on the target, whose value is the number to roll
/// (<c>Save_Vs_PPDM</c> and its four neighbours, <c>Char.cpp:8326</c>). The design supplies the
/// numbers; nothing here derives them.
/// </remarks>
public enum SaveVersus
{
    /// <summary>Paralysation, poison and death magic.</summary>
    ParalyzePoisonDeathMagic = 0,

    /// <summary>Petrification and polymorph.</summary>
    PetrifyPolymorph = 1,

    RodStaffWand = 2,

    /// <summary>Spells generally — the catch-all.</summary>
    Spell = 3,

    BreathWeapon = 4,
}

/// <summary>
/// What a successful save is worth (<c>spellSaveEffectType</c>, <c>GameRules.h:327</c>).
/// </summary>
public enum SaveResult
{
    /// <summary>Full effect in spite of the save.</summary>
    NoSave = 0,

    /// <summary>No effect at all.</summary>
    SaveNegates = 1,

    /// <summary>Half of a numeric change; anything else is unaffected.</summary>
    SaveForHalf = 2,

    /// <summary>
    /// Resolved as an attack roll against the target's armour class rather than as a save.
    /// </summary>
    UseThac0 = 3,
}

/// <summary>
/// What a saving throw decided.
/// </summary>
/// <param name="Saved">Whether the target made its save (<c>stData.success</c>).</param>
/// <param name="NoEffect">
/// Whether the spell does nothing whatever to this target (<c>noEffectWhatsoever</c>). Distinct
/// from a zero multiplier: it suppresses non-numeric effects too.
/// </param>
/// <param name="Change">
/// What to scale a numeric effect by (<c>changeResult</c>): 1 for full, 0.5 for half, 0 for none.
/// </param>
public readonly record struct SaveOutcome(bool Saved, bool NoEffect, double Change);

/// <summary>
/// Saving throws (<c>DoesSavingThrowSucceed</c> and <c>DidSaveVersus</c>, <c>Char.cpp:11862</c>,
/// <c>:8316</c>).
/// </summary>
/// <remarks>
/// <para>
/// The reference's own summary, in the comment heading <c>DoesSavingThrowSucceed</c>: each save
/// type has a single value that rises with the target's level; roll a d20; a roll below that value
/// is a failed save and the full effect lands; a roll at or above it is a save, and then
/// <see cref="SaveResult"/> says what the save was worth.
/// </para>
/// <para>
/// <b>Rolls are passed in rather than rolled here.</b> Everything in this assembly is a pure rule
/// so a test can pin the dice; the caller owns the roller.
/// </para>
/// </remarks>
public static class SavingThrow
{
    /// <summary>The highest number a save can require (<c>score = min(score, 20)</c>).</summary>
    /// <remarks>
    /// Clamped at the top only. There is no matching floor on the live path — the
    /// <c>max(score, 1)</c> beside it sits inside the commented-out script block — so a save value
    /// of zero or less succeeds on any roll.
    /// </remarks>
    public const int WorstSaveScore = 20;

    /// <summary>
    /// The bonus a target's own protections add to its save roll
    /// (<c>ModifySaveRollAsTarget</c>, <c>Char.cpp:17614</c>).
    /// </summary>
    /// <param name="protectedFromAlignment">
    /// The target has protection from the caster's alignment — <c>SA_ProtFromEvil</c> against an
    /// evil caster, <c>SA_ProtFromGood</c> against a good one. Neither applies to a caster who is
    /// neither, and the two are mutually exclusive because the reference tests them as
    /// <c>if</c>/<c>else if</c>.
    /// </param>
    /// <remarks>
    /// <b>The attacker's half of this is dead.</b> <c>ModifySaveRoll</c>, which would let a caster
    /// worsen its target's roll, is a stub that returns false without touching anything
    /// (<c>Char.cpp:17603</c>).
    /// </remarks>
    public static int RollBonus(bool protectedFromAlignment = false, bool shielded = false,
                                bool displaced = false) =>
        (protectedFromAlignment ? 2 : 0) + (shielded ? 1 : 0) + (displaced ? 2 : 0);

    /// <summary>
    /// Whether the target resisted (<c>DidSaveVersus</c>, <c>Char.cpp:8316</c>).
    /// </summary>
    /// <param name="score">
    /// The number the target must roll, from the skill the <see cref="SaveVersus"/> type names.
    /// Clamped to at most <see cref="WorstSaveScore"/>.
    /// </param>
    /// <param name="roll">The d20.</param>
    /// <param name="rollBonus">From <see cref="RollBonus"/>.</param>
    /// <param name="magicResistance">
    /// The target's percentage resistance. Above zero it is checked first and can end the matter.
    /// </param>
    /// <param name="resistanceRoll">The d100 for magic resistance. Ignored when there is none.</param>
    /// <remarks>
    /// <para>
    /// <b>Magic resistance is checked before the save and counts as one.</b> A target whose d100
    /// comes in at or under its resistance returns saved without ever rolling the d20 — so a
    /// <see cref="SaveResult.SaveForHalf"/> spell still does half damage to a resistant target
    /// rather than none. Resistance is not immunity here.
    /// </para>
    /// <para>
    /// <b>A roll equal to the score saves.</b> The reference's test is <c>roll &lt; score</c> for
    /// failure, so the boundary belongs to the target.
    /// </para>
    /// </remarks>
    public static bool DidSaveVersus(int score, int roll, int rollBonus = 0,
                                     int magicResistance = 0, int resistanceRoll = 101)
    {
        if (magicResistance > 0 && resistanceRoll <= magicResistance)
        {
            return true;
        }

        return roll + rollBonus >= Math.Min(score, WorstSaveScore);
    }

    /// <summary>
    /// Resolves a spell's saving throw against one target
    /// (<c>DoesSavingThrowSucceed</c>, <c>Char.cpp:11862</c>).
    /// </summary>
    /// <param name="result">The spell's <c>Save_Result</c>.</param>
    /// <param name="saved">What <see cref="DidSaveVersus"/> decided.</param>
    /// <remarks>
    /// <b>A failed save always means the full effect, whatever <see cref="SaveResult"/> says.</b>
    /// The switch only runs on a successful save; the failure branch just records it.
    /// <para>
    /// <b>The save is rolled even against your own party.</b> The reference's comment above this
    /// says no save is needed when a spell is cast on yourself or on a willing recipient, and
    /// "party members are always assumed to be willing" — but the guard that implemented it,
    /// <c>if (!friendlyFire || pCaster-&gt;targets.m_area)</c>, is commented out with a dated note
    /// ("Requested by Eric 20121017"). The comment describes an older engine; the code is what
    /// runs.
    /// </para>
    /// </remarks>
    public static SaveOutcome Resolve(SaveResult result, bool saved)
    {
        if (!saved)
        {
            return new SaveOutcome(Saved: false, NoEffect: false, Change: 1.0);
        }

        return result switch
        {
            SaveResult.SaveNegates => new SaveOutcome(true, NoEffect: true, Change: 0.0),
            SaveResult.SaveForHalf => new SaveOutcome(true, NoEffect: false, Change: 0.5),
            _ => new SaveOutcome(true, NoEffect: false, Change: 1.0),
        };
    }

    /// <summary>
    /// The <see cref="SaveResult.UseThac0"/> branch, which is an attack roll rather than a save
    /// (<c>Char.cpp:11898</c>).
    /// </summary>
    /// <param name="roll">The d20, with the saving-throw script's bonus already added.</param>
    /// <param name="casterThac0">The caster's adjusted THAC0.</param>
    /// <param name="targetArmorClass">The target's adjusted armour class.</param>
    /// <remarks>
    /// <para>
    /// <b>The comparison's operands are transposed, and this reproduces it.</b> The reference tests
    /// <c>diceRoll &gt; AC - adjTHAC0</c>. To hit armour class <c>AC</c> with THAC0 <c>T</c> the
    /// roll must reach <c>T - AC</c>, so the subtraction is the wrong way round: with a THAC0 of 18
    /// against armour class 6 the threshold is <c>6 - 18 = -12</c>, which every d20 clears. The
    /// branch that then runs sets <c>noEffectWhatsoever</c> — so <b>a THAC0-resolved spell
    /// essentially never lands</b>, at any competence, against any armour.
    /// </para>
    /// <para>
    /// Kept because it is what ships and a design was balanced against it. See
    /// <see cref="Thac0"/> for the same arithmetic done the right way round, which is what the
    /// ordinary attack path uses.
    /// </para>
    /// </remarks>
    public static SaveOutcome ResolveThac0(int roll, int casterThac0, int targetArmorClass) =>
        roll > targetArmorClass - casterThac0
            ? new SaveOutcome(Saved: true, NoEffect: true, Change: 0.0)
            : new SaveOutcome(Saved: false, NoEffect: false, Change: 1.0);

    /// <summary>
    /// The whole saving throw, both branches (<c>DoesSavingThrowSucceed</c>).
    /// </summary>
    /// <param name="scriptBonus">
    /// From the spell's <c>SavingThrow</c> script. <b>Only the THAC0 branch uses it</b> — the
    /// ordinary branch passes it to <c>DidSaveVersus</c>, whose body never reads it. See the
    /// remarks.
    /// </param>
    /// <remarks>
    /// <b>The script's bonus is silently dropped for four of the five save types.</b>
    /// <c>DidSaveVersus</c> takes a <c>bonus</c> parameter and the live code never touches it; its
    /// only use was inside the deprecated script block, which is commented out
    /// (<c>Char.cpp:8351</c>). So a design that writes a <c>SavingThrow</c> script to grant, say,
    /// +2 against a spell gets nothing unless that spell also uses
    /// <see cref="SaveResult.UseThac0"/>. Reproduced: <paramref name="scriptBonus"/> reaches
    /// <see cref="ResolveThac0"/> and nothing else.
    /// </remarks>
    public static SaveOutcome Resolve(SaveResult result, int score, int roll,
                                      int rollBonus = 0, int magicResistance = 0,
                                      int resistanceRoll = 101, int scriptBonus = 0,
                                      int casterThac0 = 20, int targetArmorClass = 10)
    {
        if (result == SaveResult.UseThac0)
        {
            return ResolveThac0(roll + scriptBonus, casterThac0, targetArmorClass);
        }

        bool saved = DidSaveVersus(score, roll, rollBonus, magicResistance, resistanceRoll);
        return Resolve(result, saved);
    }
}
