using UAF.Serialization;

namespace UAFcore;

/// <summary>Where a spell is being cast from (<c>CASTING_ENVIRONMENT</c>).</summary>
public enum CastingEnvironment
{
    /// <summary>In a fight.</summary>
    Combat = 0,

    /// <summary>From the camp's MAGIC menu.</summary>
    Camp = 1,

    /// <summary>From the world, without camping.</summary>
    Adventure = 2,
}

/// <summary>Why a non-combat cast did not happen, or that it is under way.</summary>
public enum CastRefusal
{
    /// <summary>Casting.</summary>
    None = 0,

    /// <summary>The caster cannot cast at all (<c>CanCastSpells</c>).</summary>
    CannotCast,

    /// <summary>Not memorised. The reference does nothing at all, silently.</summary>
    NotMemorized,

    /// <summary>An id the design no longer defines.</summary>
    UnknownSpell,

    /// <summary>A combat-only spell, reached outside a fight.</summary>
    CombatOnly,

    /// <summary>Targeting produced nobody, so nothing was cast.</summary>
    NoTargets,

    /// <summary>
    /// The spell names its own targets: the player has to pick them
    /// (<c>TARGET_SELECT_NONCOMBAT_EVENT_DATA</c>).
    /// </summary>
    /// <remarks>
    /// Not a refusal in the reference — it pushes a screen — but it is the same answer to the
    /// caller: nothing has been cast yet.
    /// </remarks>
    NeedsTargets,
}

/// <summary>
/// What a cast still needs before it can resolve.
/// </summary>
/// <param name="Refusal">Why nothing will happen, or <see cref="CastRefusal.None"/>.</param>
/// <param name="NeedsSelection">
/// Whether the player must name the targets. When true, <paramref name="Targets"/> is empty and
/// the caller pushes the picker.
/// </param>
/// <param name="Targets">Who the spell lands on, when no selection is needed.</param>
/// <param name="CasterLevel">
/// The level to cast at, or −1 for "the caster's own" — <c>m_spellCastingLevel</c>.
/// </param>
public sealed record CastPlan(CastRefusal Refusal, bool NeedsSelection,
                              IReadOnlyList<Character> Targets, int CasterLevel);

/// <summary>
/// Choosing and beginning a spell outside combat
/// (<c>CAST_MENU_DATA</c>, <c>RunEvent.cpp:25754</c>, and
/// <c>CAST_NON_COMBAT_SPELL_MENU_DATA</c>, <c>:25924</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two screens, and the second one usually has no screen.</b> The cast menu is a paged spell
/// list with CAST / NEXT / PREV / EXIT. What it pushes is not a menu at all for most spells: the
/// target picker only appears for the three modes that need one, and everything else resolves and
/// pops in <c>OnInitialEvent</c> without the player seeing anything.
/// </para>
/// <para>
/// <b>Every refusal here is silent.</b> Six separate guards pop the screen with nothing but a
/// debug string — a spell that is combat-only, an id the design lost, a caster who cannot cast.
/// The player presses CAST and the screen simply goes away. Reproduced, because a message where
/// the reference has none is a change to the game.
/// </para>
/// </remarks>
public static class NonCombatCast
{
    /// <summary>The cast menu (<c>CastMenuData</c>).</summary>
    public static readonly (string Label, int Shortcut)[] Menu =
        [("CAST", 0), ("NEXT", 0), ("PREV", 0), ("EXIT", 1)];

    /// <summary>
    /// The spells the cast list offers (<c>FillCastSpellListText</c>, <c>Spell.cpp:8912</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only memorised copies, and only spells the environment allows.</b> The two restriction
    /// flags are <i>permissions</i>, not prohibitions, despite the commented-out
    /// <c>NotInCombat</c>/<c>NotInCamp</c> pair above them that read the other way round: a spell
    /// with neither flag set appears nowhere at all.
    /// </para>
    /// <para>
    /// <b>Adventure is filtered by the camp flag, not one of its own.</b> There are three
    /// environments and two flags, and <c>CAST_ENV_ADVENTURE</c> shares <c>InCamp</c> — so a
    /// design cannot let a spell be cast while camping but not while walking around.
    /// </para>
    /// <para>
    /// <b>A spell in the book that the design no longer defines is skipped</b>, with a log line,
    /// rather than shown as a blank row.
    /// </para>
    /// </remarks>
    public static List<SpellListEntry> Castable(SpellList book, CastingEnvironment environment,
                                                Func<string, SpellRecord?> database)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(database);

        var offered = new List<SpellListEntry>();

        foreach (var entry in book.Entries)
        {
            if (entry.Memorized <= 0 || database(entry.SpellId) is not { } spell)
            {
                continue;
            }

            if (Allows(spell, environment))
            {
                offered.Add(entry);
            }
        }

        return offered;
    }

    /// <summary>Whether a spell may be cast in this environment (<c>restrictions</c>).</summary>
    public static bool Allows(SpellRecord spell, CastingEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(spell);

        return environment == CastingEnvironment.Combat
            ? (spell.Restrictions & InCombat) != 0
            : (spell.Restrictions & InCamp) != 0;
    }

    /// <summary><c>IN_CAMP</c> (<c>Spell.h:404</c>).</summary>
    public const int InCamp = 0x01;

    /// <summary><c>IN_COMBAT</c> (<c>Spell.h:405</c>).</summary>
    public const int InCombat = 0x02;

    /// <summary>
    /// Works out who a cast lands on, or that the player has to say
    /// (<c>CAST_NON_COMBAT_SPELL_MENU_DATA::OnInitialEvent</c>).
    /// </summary>
    /// <param name="caster">Whoever pressed CAST.</param>
    /// <param name="party">Everyone present — outside combat the only possible targets.</param>
    /// <param name="spell">The spell's record, or null for an id the design has lost.</param>
    /// <param name="memorized">
    /// Whether the caster has a memorised copy. <c>CAST_MENU_DATA</c> tests this before pushing
    /// anything, so a spell without one does nothing whatsoever — not even a screen.
    /// </param>
    /// <param name="casterLevel">
    /// What the <c>SPELL_CASTER_LEVEL</c> script answered, or −1 for none. Not ported; −1 means
    /// the caster's own level, which is what a design without that hook gets.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The guards run in the reference's order and every one of them is silent.</b> A caster
    /// who cannot cast never even opens the list; the rest pop the cast screen without a word.
    /// </para>
    /// <para>
    /// <b>Outside combat, every area shape becomes the whole party.</b> A fireball cast in camp
    /// hits everyone the party has, because there is no map to centre it on —
    /// <c>NeedSpellTargeting</c> answers no for all five area modes out of combat and the caller's
    /// <c>else</c> adds every party member.
    /// </para>
    /// <para>
    /// <b>Self is the only mode that targets one person without asking.</b> Whole-party and the
    /// area shapes all take everyone; select-by-count, touch and select-by-hit-dice all ask.
    /// </para>
    /// <para>
    /// <b>A cast with no targets is abandoned, not refused.</b> The reference logs "not casting
    /// spell" and pops — and because <c>CastSpell</c> is never reached, the memorised copy is
    /// <i>not</i> spent. That is the one path where pressing CAST costs nothing.
    /// </para>
    /// </remarks>
    public static CastPlan Plan(Character caster, IReadOnlyList<Character> party,
                                SpellRecord? spell, bool memorized, int casterLevel = -1)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(party);

        if (!SpellPermissions.CanCast(caster))
        {
            return Refused(CastRefusal.CannotCast, casterLevel);
        }

        if (!memorized)
        {
            return Refused(CastRefusal.NotMemorized, casterLevel);
        }

        if (spell is null)
        {
            return Refused(CastRefusal.UnknownSpell, casterLevel);
        }

        if (!Allows(spell, CastingEnvironment.Camp))
        {
            return Refused(CastRefusal.CombatOnly, casterLevel);
        }

        var targeting = (SpellTargeting)spell.Targeting;

        if (SpellTargets.NeedsSelection(targeting, inCombat: false))
        {
            return new CastPlan(CastRefusal.None, NeedsSelection: true, [], casterLevel);
        }

        IReadOnlyList<Character> targets = targeting == SpellTargeting.Self
            ? [caster]
            : party;

        return targets.Count > 0
            ? new CastPlan(CastRefusal.None, false, targets, casterLevel)
            : Refused(CastRefusal.NoTargets, casterLevel);
    }

    private static CastPlan Refused(CastRefusal why, int casterLevel) =>
        new(why, false, [], casterLevel);
}
