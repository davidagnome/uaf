namespace UAFcore;

/// <summary>
/// What resolving a spell needs to know about a caster or a target.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reference has one class here and this port has two.</b> Everything a spell touches is a
/// <c>CHARACTER</c> there — a combatant is a <c>COMBATANT</c> holding a pointer back to one, and
/// <c>InvokeSpellOnTarget</c> is a <c>CHARACTER</c> method that both paths reach. Here a
/// <see cref="Combatant"/> is a fight's own object and a <see cref="Character"/> is a party
/// member, and the two do not share a base.
/// </para>
/// <para>
/// This is the four things <see cref="SpellResolution"/> actually reads off either of them, which
/// is what lets the in-combat and out-of-combat paths run the <i>same</i> resolution rather than
/// two that drift apart.
/// </para>
/// </remarks>
public interface ISpellSubject
{
    /// <summary>The effects currently carried (<c>m_spellEffects</c>).</summary>
    UAF.Rules.SpellEffectList Effects { get; }

    /// <summary>Percentage magic resistance (<c>GetAdjMagicResistance</c>).</summary>
    int MagicResistance { get; }

    /// <summary>Armour class, which the <c>UseTHAC0</c> save branch rolls against.</summary>
    int ArmorClass { get; }

    /// <summary>To-hit number, which that same branch rolls with.</summary>
    int Thac0 { get; }
}
