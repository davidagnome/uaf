using UAF.Rules;
using UAF.Scripting;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The engine behind a GPDL script (<c>IGpdlHost</c> against real game state).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GpdlUnhostedEnvironment"/> is the VM's own stand-in, useful for testing the bytecode
/// and nothing else. This is the first host backed by a running game: a script's attribute reads
/// and writes reach the design's global store and the party's own.
/// </para>
/// <para>
/// <b>What is still unhosted.</b> Everything inherited from the base — discourse, <c>$GREP</c>,
/// randomness — plus the roughly 250 character, party and combat sub-opcodes the VM refuses with a
/// citation. This closes the attribute family only, which is the one a design uses to remember
/// things between scripts.
/// </para>
/// </remarks>
public sealed class GameScriptHost(Game game) : GpdlUnhostedEnvironment
{
    private readonly Game game = game ?? throw new ArgumentNullException(nameof(game));

    private AttributeList Store(GpdlAslScope scope) =>
        scope == GpdlAslScope.Party ? game.Party.Attributes : game.Globals;

    /// <inheritdoc/>
    public override string GetAsl(GpdlAslScope scope, string key) =>
        Store(scope).Find(key) ?? string.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// Written with no flags, as the reference does — see <see cref="IGpdlHost.SetAsl"/>. It still
    /// saves; only read-only entries are held back.
    /// </remarks>
    public override void SetAsl(GpdlAslScope scope, string key, string value) =>
        Store(scope).Insert(key, value);

    /// <inheritdoc/>
    public override bool HasAsl(GpdlAslScope scope, string key) =>
        Store(scope).Entry(key) is not null;

    /// <inheritdoc/>
    public override void DeleteAsl(GpdlAslScope scope, string key) =>
        Store(scope).Remove(key);

    /// <summary>
    /// Resolves the actor string a script pushed to one of the party.
    /// </summary>
    /// <remarks>
    /// <b>By character id, which is the unique id the reference settled on.</b> A dated comment
    /// records the change: "almost all functions use the uniqueID of the character rather than the
    /// party index. I decided that the few exceptions should be treated as 'bugs'"
    /// (<c>GPDLexec.cpp:1845</c>). The <i>combat order</i> alternative the same comment mentions is
    /// not resolved here — a script naming a combatant by its place in the fight finds nobody.
    /// <para>
    /// A name that resolves to nobody is not an error here. The reference puts a message box in
    /// front of the player and returns a null character whose store swallows the write
    /// (<c>GPDLexec.cpp:908</c>); a design with a typo'd actor therefore limps rather than stops,
    /// and it does the same here without the dialog.
    /// </para>
    /// </remarks>
    private Character? Resolve(string actor) =>
        game.Party.Members.FirstOrDefault(
            m => string.Equals(m.CharacterId, actor, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(m.Name, actor, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public override string GetCharAsl(string actor, string key) =>
        Resolve(actor)?.Attributes.Find(key) ?? string.Empty;

    /// <inheritdoc/>
    public override void SetCharAsl(string actor, string key, string value) =>
        Resolve(actor)?.Attributes.Insert(key, value);

    /// <inheritdoc/>
    /// <remarks>
    /// <b>An integer stat is formatted plainly, because GPDL's stack holds only strings.</b> The
    /// reference pushes through <c>m_pushInteger1</c>, which does the same conversion — a script
    /// comparing a stat against a literal is comparing text.
    /// </remarks>
    public override string GetCharStat(string actor, GpdlCharStat stat)
    {
        if (Resolve(actor) is not { } character)
        {
            return string.Empty;
        }

        return stat switch
        {
            GpdlCharStat.Name => character.Name,
            GpdlCharStat.HitPoints => Text(character.AdjustedHitPoints),
            GpdlCharStat.MaxHitPoints => Text(character.MaxHitPoints),
            GpdlCharStat.ArmorClass => Text(character.ArmorClass),
            GpdlCharStat.AdjustedArmorClass => Text(character.AdjustedArmorClass),
            GpdlCharStat.Thac0 => Text(character.Thac0),

            // No readied-item model on a character outside combat, so the two bonuses the
            // reference subtracts are zero here -- see Character.AdjustedThac0.
            GpdlCharStat.AdjustedThac0 => Text(character.AdjustedThac0()),
            GpdlCharStat.Experience => Text(character.TotalExperience),
            GpdlCharStat.ReadyToTrain => Text(character.ReadyToTrain ? 1 : 0),
            GpdlCharStat.Gender => Text((int)character.Gender),

            _ => AbilityLayers.Read(character, stat) is { } score ? Text(score) : string.Empty,
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>Only the stats this port keeps as live state are written.</b> A <see cref="Character"/>
    /// reads age, movement, alignment, size, the two combat bonuses and the portrait index off its
    /// immutable record, so a script setting one of those changes nothing here where the reference
    /// would have changed it. The call still yields the empty string, as it does there — the
    /// reference's own setters end in <c>m_pushEmptyString</c> and report nothing either way, so a
    /// script cannot tell the difference from inside.
    /// </para>
    /// <para>
    /// <b>A value that is not a number is ignored rather than written as zero.</b> The reference
    /// pops through <c>m_popInteger1</c>, which is <c>atoi</c> and would give it a zero; refusing
    /// is the one deliberate divergence here, because silently zeroing a character's strength on a
    /// script typo is worse than doing nothing.
    /// </para>
    /// </remarks>
    public override void SetCharStat(string actor, GpdlCharStat stat, string value)
    {
        if (Resolve(actor) is not { } character
            || !int.TryParse(value, System.Globalization.NumberStyles.Integer,
                             System.Globalization.CultureInfo.InvariantCulture, out int number))
        {
            return;
        }

        switch (stat)
        {
            case GpdlCharStat.HitPoints: character.HitPoints = number; break;
            case GpdlCharStat.MaxHitPoints: character.MaxHitPoints = number; break;
            case GpdlCharStat.Morale: character.Morale = number; break;
            case GpdlCharStat.Status: character.Status = (CharacterStatus)number; break;

            default:
                if (AbilityLayers.PermanentScore(stat) is { } ability)
                {
                    character.Abilities = Written(character.Abilities, ability, number);
                }
                break;
        }
    }

    /// <summary>One score replaced, since the scores are an immutable record.</summary>
    private static AbilityScores Written(AbilityScores scores, AbilityScore ability, int value) =>
        ability switch
        {
            AbilityScore.Strength => scores with { Strength = value },
            AbilityScore.StrengthMod => scores with { StrengthMod = value },
            AbilityScore.Intelligence => scores with { Intelligence = value },
            AbilityScore.Wisdom => scores with { Wisdom = value },
            AbilityScore.Dexterity => scores with { Dexterity = value },
            AbilityScore.Constitution => scores with { Constitution = value },
            _ => scores with { Charisma = value },
        };

    private static string Text(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
