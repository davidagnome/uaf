using UAF.Rules;
using UAF.Scripting;

namespace UAFcore;

/// <summary>
/// The three layers a character's ability score is read through
/// (<c>GetPerm…</c> / <c>GetAdj…</c> / <c>GetLimited…</c>, <c>Char.cpp:13610</c> onwards).
/// </summary>
/// <remarks>
/// A rule rather than host plumbing, which is why it is here and not in
/// <see cref="GameScriptHost"/>: GPDL is simply the first caller that needs all three by name.
/// </remarks>
public static class AbilityLayers
{
    /// <summary>
    /// An ability score in one of its three layers
    /// (<c>GetPerm…</c> / <c>GetAdj…</c> / <c>GetLimited…</c>, <c>Char.cpp:13610</c> onwards).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Permanent is stored, adjusted adds spell effects, limited clamps the adjusted one.</b>
    /// The adjusted form is deliberately unbounded — a script asking for it can see a value the
    /// rules would never act on, which is the point of exposing all three.
    /// </para>
    /// <para>
    /// <b>The spell-effect key is the <c>CHAR_ADJUSTED_*</c> one, not <c>CHAR_*</c>.</b> The
    /// reference passes <c>CHAR_ADJUSTED_STR</c> to <c>ApplySpellEffectAdjustments</c>
    /// (<c>Char.cpp:13629</c>) — the commented-out line above it used <c>"$CHAR_STR"</c> and does
    /// not any more, so an effect written against the plain name reaches nothing.
    /// </para>
    /// </remarks>
    public static int? Read(Character character, GpdlCharStat stat)
    {
        (AbilityScore ability, int layer) = stat switch
        {
            GpdlCharStat.PermanentStrength => (AbilityScore.Strength, Permanent),
            GpdlCharStat.AdjustedStrength => (AbilityScore.Strength, Adjusted),
            GpdlCharStat.LimitedStrength => (AbilityScore.Strength, Limited),

            GpdlCharStat.PermanentStrengthMod => (AbilityScore.StrengthMod, Permanent),
            GpdlCharStat.AdjustedStrengthMod => (AbilityScore.StrengthMod, Adjusted),
            GpdlCharStat.LimitedStrengthMod => (AbilityScore.StrengthMod, Limited),

            GpdlCharStat.PermanentIntelligence => (AbilityScore.Intelligence, Permanent),
            GpdlCharStat.AdjustedIntelligence => (AbilityScore.Intelligence, Adjusted),
            GpdlCharStat.LimitedIntelligence => (AbilityScore.Intelligence, Limited),

            GpdlCharStat.PermanentWisdom => (AbilityScore.Wisdom, Permanent),
            GpdlCharStat.AdjustedWisdom => (AbilityScore.Wisdom, Adjusted),
            GpdlCharStat.LimitedWisdom => (AbilityScore.Wisdom, Limited),

            GpdlCharStat.PermanentDexterity => (AbilityScore.Dexterity, Permanent),
            GpdlCharStat.AdjustedDexterity => (AbilityScore.Dexterity, Adjusted),
            GpdlCharStat.LimitedDexterity => (AbilityScore.Dexterity, Limited),

            GpdlCharStat.PermanentConstitution => (AbilityScore.Constitution, Permanent),
            GpdlCharStat.AdjustedConstitution => (AbilityScore.Constitution, Adjusted),
            GpdlCharStat.LimitedConstitution => (AbilityScore.Constitution, Limited),

            GpdlCharStat.PermanentCharisma => (AbilityScore.Charisma, Permanent),
            GpdlCharStat.AdjustedCharisma => (AbilityScore.Charisma, Adjusted),
            GpdlCharStat.LimitedCharisma => (AbilityScore.Charisma, Limited),

            _ => (AbilityScore.Strength, None),
        };

        if (layer == None)
        {
            return null;
        }

        int permanent = Permanently(character, ability);
        if (layer == Permanent)
        {
            return permanent;
        }

        int adjusted = (int)character.Effects.Apply(permanent, EffectKey(ability));

        return layer == Adjusted ? adjusted : AbilityBounds.Limit(adjusted, ability);
    }

    private const int None = 0;
    private const int Permanent = 1;
    private const int Adjusted = 2;
    private const int Limited = 3;

    private static int Permanently(Character character, AbilityScore ability) => ability switch
    {
        AbilityScore.Strength => character.Abilities.Strength,
        AbilityScore.StrengthMod => character.Abilities.StrengthMod,
        AbilityScore.Intelligence => character.Abilities.Intelligence,
        AbilityScore.Wisdom => character.Abilities.Wisdom,
        AbilityScore.Dexterity => character.Abilities.Dexterity,
        AbilityScore.Constitution => character.Abilities.Constitution,
        _ => character.Abilities.Charisma,
    };

    /// <summary>The spell-effect attribute an ability's adjustment accumulates under.</summary>
    private static string EffectKey(AbilityScore ability) => ability switch
    {
        AbilityScore.Strength => "$CHAR_ADJUSTED_STR",
        AbilityScore.StrengthMod => "$CHAR_ADJUSTED_STRMOD",
        AbilityScore.Intelligence => "$CHAR_ADJUSTED_INT",
        AbilityScore.Wisdom => "$CHAR_ADJUSTED_WIS",
        AbilityScore.Dexterity => "$CHAR_ADJUSTED_DEX",
        AbilityScore.Constitution => "$CHAR_ADJUSTED_CON",
        _ => "$CHAR_ADJUSTED_CHA",
    };
}
