using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>What one cast outside combat did.</summary>
/// <param name="Cast">
/// Whether the spell was cast at all. False only for an id the design has lost — <b>and nothing
/// else</b>: a spell that finds no target still counts as cast, and still spends its memorised copy.
/// </param>
/// <param name="Hits">One entry per party member the spell reached, in target order.</param>
public sealed record CastResult(bool Cast, IReadOnlyList<SpellHit> Hits);

/// <summary>
/// Casting a spell outside combat (<c>CHARACTER::CastSpell</c>, <c>Char.cpp:17021</c>, and
/// <c>CHARACTER::SpellActivate</c>, <c>:16913</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Outside combat every target is a party member.</b> <c>SpellActivate</c> resolves each
/// selected target's <c>uniquePartyID</c> against the party and <i>skips</i> anything it cannot
/// find — so a spell aimed at something that has left the party quietly affects nobody rather than
/// erroring. The reference asserts it is not in combat and that the caster is not a monster; both
/// are structural here rather than checked.
/// </para>
/// <para>
/// <b>The dispatch in between is only a dispatch.</b> The global <c>::SpellActivate</c>
/// (<c>Globals.cpp:4245</c>) is a two-level switch on where the caster came from — party member,
/// NPC, bishop, item, monster — that routes to the right object's own <c>SpellActivate</c> and does
/// nothing else. Out of combat, from a party member or a bishop, every arm lands here.
/// </para>
/// <para>
/// <b>What is <i>not</i> here: the effects reaching the character's attributes.</b>
/// <see cref="SpellResolution"/> adds them to the target's <see cref="UAF.Rules.SpellEffectList"/>,
/// which this port reads back through <c>Adjusted…</c> properties. The reference's
/// <c>AddSpellEffect</c> (<c>Char.cpp:11984</c>) <i>also</i> calls <c>ModifyByDouble</c> to write
/// the change straight onto the attribute — <c>CHAR_HITPOINTS</c> becomes
/// <c>SetHitPoints(GetHitPoints() + modification)</c> (<c>RunTimeIF.cpp:1425</c>) — and reverses it
/// when the effect expires. Whether the two together double-count, and which of them the
/// <c>GetAdj…</c> accessors are meant to see, is the open question below this layer; it decides
/// whether a cure spell moves <see cref="Character.HitPoints"/> or only
/// <see cref="Character.AdjustedHitPoints"/>, and therefore whether FIX's loop terminates.
/// </para>
/// </remarks>
public static class PartyCasting
{
    /// <summary>
    /// Casts a spell from one party member at a chosen set of party members.
    /// </summary>
    /// <param name="caster">Whoever is casting. Their memorised copy is what gets spent.</param>
    /// <param name="party">Everyone present; a target outside it is skipped.</param>
    /// <param name="targets">The chosen targets, which may include the caster.</param>
    /// <param name="spell">The spell's database record, or null for an id the design has lost.</param>
    /// <param name="nextActiveSpellKey">
    /// Issues the next <c>activeSpellList</c> key. Called <b>at most once per cast</b> and only for
    /// a spell that expires.
    /// </param>
    /// <param name="freeOfCharge">
    /// The reference's <c>LayOrCureOrWhatever</c>. True suppresses the decrement, for a cast that
    /// is not coming out of a spell book — laying on hands, and the temple's bishop.
    /// </param>
    /// <param name="castingLevel">
    /// <c>m_spellCastingLevel</c>, which overrides the caster's own level when it is not −1. What
    /// lets an item or a temple cast at a level its holder has not reached.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>A spell the design no longer defines is not cast and costs nothing.</b> The lookup fails
    /// before the decrement, so the copy stays memorised.
    /// </para>
    /// <para>
    /// <b>The memorised copy is spent before anything else happens</b>, and is not refunded by any
    /// later refusal — a target who saves, a target already carrying the spell, or no valid target
    /// at all all leave the caster one copy poorer.
    /// </para>
    /// <para>
    /// <b>One active-spell key for the whole cast, allocated before the target loop.</b> So a spell
    /// that reached four people expires from all four together, rather than each on its own clock.
    /// A spell whose duration is <c>Permanent</c> gets no key at all, because there is nothing to
    /// expire.
    /// </para>
    /// <para>
    /// <b>The cast sound plays whether or not the spell affected anybody</b> — the reference is
    /// explicit that this is deliberate ("regardless of whether spell actually affected anybody"),
    /// and there is no graphical feedback outside combat.
    /// </para>
    /// </remarks>
    public static CastResult Cast(Character caster, IReadOnlyList<Character> party,
                                  IReadOnlyList<Character> targets, SpellRecord? spell,
                                  Func<int, int> dice, Func<int> nextActiveSpellKey,
                                  double elapsedMinutes = 0, bool freeOfCharge = false,
                                  int castingLevel = -1,
                                  Func<Character, int>? saveScoreOf = null,
                                  Func<ISpellSubject, ISpellSubject, bool>? attackSucceeds = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(dice);
        ArgumentNullException.ThrowIfNull(nextActiveSpellKey);

        if (spell is null)
        {
            return new CastResult(false, []);
        }

        if (!freeOfCharge)
        {
            caster.Book.DecrementMemorized(spell.Name);
        }

        int level = castingLevel != -1 ? castingLevel : CasterLevel(caster);

        // Allocated once, before the loop, and only for a spell that expires.
        int activeSpellKey = (SpellDurationRate)spell.DurationRate == SpellDurationRate.Permanent
            ? -1
            : nextActiveSpellKey();

        var hits = new List<SpellHit>(targets.Count);

        foreach (var target in targets)
        {
            int slot = IndexOf(party, target);
            if (slot < 0)
            {
                continue;                       // not in the party -- silently skipped
            }

            hits.Add(SpellResolution.InvokeOn(caster, target, slot, spell, dice, elapsedMinutes,
                                              activeSpellKey,
                                              saveScoreOf?.Invoke(target) ?? DefaultSaveScore,
                                              level, attackSucceeds));
        }

        return new CastResult(true, hits);
    }

    /// <summary>
    /// The save number used when the caller supplies none.
    /// </summary>
    /// <remarks>
    /// Twenty, matching <see cref="SpellResolution"/>: a d20 roll can equal it but not beat it, so
    /// an unsupplied save is one the target essentially always fails. The tables live in
    /// <c>UAF.Rules.SavingThrow</c> and the caller is expected to reach them.
    /// </remarks>
    public const int DefaultSaveScore = 20;

    /// <summary>
    /// The caster's own level, when <c>m_spellCastingLevel</c> does not override it.
    /// </summary>
    /// <remarks>
    /// <b>The highest of the caster's baseclasses, not a total.</b> A dual-class character casts at
    /// the level of whichever class reached furthest, which is what a level-scaled damage
    /// expression multiplies by. A character with no baseclasses at all casts at 1.
    /// </remarks>
    public static int CasterLevel(Character caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        int best = 0;
        foreach (var progress in caster.Baseclasses)
        {
            best = Math.Max(best, progress.CurrentLevel);
        }

        return Math.Max(1, best);
    }

    /// <summary>
    /// Where a target sits in the party (<c>uniquePartyID</c> matched against
    /// <c>party.characters</c>).
    /// </summary>
    /// <remarks>
    /// <b>Matched by identity, where the reference matches by <c>uniquePartyID</c>.</b> The port
    /// holds live <see cref="Character"/> objects rather than ids into a fixed array, so reference
    /// equality answers the same question — and an object that is not in the list is exactly the
    /// case the reference's <c>targIndex &lt; 0</c> skip covers.
    /// </remarks>
    private static int IndexOf(IReadOnlyList<Character> party, Character who)
    {
        for (int i = 0; i < party.Count; i++)
        {
            if (ReferenceEquals(party[i], who))
            {
                return i;
            }
        }

        return -1;
    }
}
