using UAF.Scripting;

namespace UAFcore;

/// <summary>
/// Live combat state as the AI script reads it — a <see cref="ForthCombatSummary"/> built from the
/// combatants and the candidate actions (<c>ListCombatants</c>, <c>Combatant.cpp:1385</c>, and the
/// <c>pMe</c>/<c>pHe</c> assignments in <c>ListActions*</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the seam between <see cref="AiActions"/>, which enumerates what a combatant could do,
/// and the Forth VM, which ranks and filters. Nothing here decides anything; it is a projection.
/// </para>
/// <para>
/// <b>Only combatant 0 gets weapons.</b> The reference lists weapons, ammunition and attacks for
/// the active combatant alone — <c>ListWeapons(combatSummary.GetCombatant(0))</c> — so a script
/// reading <c>He W:Damage</c> finds an empty list and every <c>W:</c> word pushes
/// <c>NotWeapon</c>. That is not a defect to fix: the shipped script only ever reads <c>W:</c>
/// under <c>Me</c>, and reproducing the empty list is what keeps a script that strays from
/// silently reading the wrong combatant's gear.
/// </para>
/// </remarks>
public static class AiSummary
{
    /// <summary>
    /// The whole summary for one comparison: everybody in the fight, and two candidate actions.
    /// </summary>
    public static ForthCombatSummary For(Combatant self, IReadOnlyList<Combatant> all,
                                         IReadOnlyList<AiWeapon> weapons,
                                         AiAction a, AiAction b)
    {
        var combatants = Combatants(self, all, weapons);

        return new ForthCombatSummary
        {
            ActionA = Action(a, combatants),
            ActionB = Action(b, combatants),
            Combatants = combatants,
        };
    }

    /// <summary>
    /// Everybody in the fight, <b>the acting combatant first</b>
    /// (<c>ListCombatants</c>, <c>Combatant.cpp:1385</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Position 0 is the actor and the rest keep their original order.</b> The reference writes
    /// <c>k = 0</c> for the active combatant and <c>k = j++</c> for everyone else, so the summary
    /// is a rotation rather than a copy. <c>Shield.Ready!</c> writes to position 0 whichever
    /// combatant the script had selected, which only makes sense under that ordering.
    /// </para>
    /// <para>
    /// <b><c>Index</c> stays the original combat index, not the summary position.</b> The
    /// reference sets <c>CSC.index = i</c> from the loop over all combatants, so the two numbers
    /// differ for everyone but the actor — and it is <c>index</c> that maps an action's target back
    /// to a combatant.
    /// </para>
    /// <para>
    /// <b>Width and height are left at 1.</b> The reference never assigns them either: its
    /// <c>CSC</c> local is cleared once, before the loop, and only <c>isLarge</c> carries size
    /// after that. No script word reads them.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ForthCombatant> Combatants(
        Combatant self, IReadOnlyList<Combatant> all, IReadOnlyList<AiWeapon> weapons)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(weapons);

        var summary = new List<ForthCombatant>(all.Count);
        var others = new List<ForthCombatant>(all.Count);

        foreach (var combatant in all)
        {
            bool isActor = combatant.Index == self.Index;
            var entry = Project(combatant, self, isActor ? weapons : []);

            if (isActor)
            {
                summary.Add(entry);
            }
            else
            {
                others.Add(entry);
            }
        }

        summary.AddRange(others);
        return summary;
    }

    private static ForthCombatant Project(Combatant combatant, Combatant self,
                                          IReadOnlyList<AiWeapon> weapons)
    {
        return new ForthCombatant
        {
            Index = combatant.Index,
            X = combatant.X,
            Y = combatant.Y,
            Fleeing = combatant.IsFleeing ? 1 : 0,

            // friendly = combatant.IsFriendly XOR !self.IsFriendly, which is "on the same side as
            // combatant 0" -- the header's own gloss.
            Friendly = combatant.IsFriendly == self.IsFriendly ? 1 : 0,
            State = (int)ScriptState(combatant),
            AvailableAttacks = (int)combatant.AvailableAttacks,
            IsLarge = IsLarge(combatant) ? 1 : 0,

            // CanMove(FALSE), Combatant.cpp:7187. The monster-no-move config option it also tests
            // is not modelled here.
            CanMove = !combatant.IsDone() && combatant.Movement < combatant.MaxMovement ? 1 : 0,

            // -1 is the reference's own "not computed" sentinel. Computing it needs the
            // AIBaseclass special ability, which this port does not read; no shipped script asks.
            AiBaseclass = -1,
            Weapons = [.. weapons.Select(Project)],

            // The port has no shield model in combat. An empty list makes Shield.Next cycle 0 -> 0
            // and Shield.Ready! store 0, which is what a combatant with no shields would do.
            ShieldCount = 0,
        };
    }

    /// <summary>
    /// A combatant's state as the script's <c>C:S:*</c> constants number it
    /// (<c>ListCombatants</c>'s status switch, <c>Combatant.cpp:1420</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The character's status wins over the combatant's own state, except when it is Okay.</b>
    /// A living combatant reports what it is doing — guarding, casting, moving — and a dead, gone,
    /// fled, dying, unconscious or petrified one reports that instead.
    /// </para>
    /// <para>
    /// <b>The reference then re-applies petrified, dying and dead a second time</b>, reading the
    /// unadjusted status where the switch read the adjusted one. This port has a single
    /// <c>Status</c>, so the two readings coincide and the second pass would change nothing.
    /// </para>
    /// </remarks>
    public static CombatantState ScriptState(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return combatant.Status switch
        {
            CharacterStatus.Okay => combatant.State,
            CharacterStatus.Dead => CombatantState.Dead,
            CharacterStatus.Gone => CombatantState.Gone,
            CharacterStatus.Fled => CombatantState.Fled,
            CharacterStatus.Running => CombatantState.Fleeing,
            CharacterStatus.Dying => CombatantState.Dying,
            CharacterStatus.Unconscious => CombatantState.Unconscious,
            CharacterStatus.Petrified => CombatantState.Petrified,

            // Animated and TempGone reach die(0x21ac) in the reference, which is its way of saying
            // they cannot occur on a combat map.
            _ => throw new NotSupportedException(
                     $"a combatant is {combatant.Status}, which ListCombatants refuses "
                     + "(Combatant.cpp:1446)"),
        };
    }

    /// <summary>
    /// Whether a combatant counts as large, which is what sizes a weapon's damage dice
    /// (<c>isLargeDude</c>, <c>Combatant.cpp:10903</c>).
    /// </summary>
    /// <remarks>
    /// <b>Only the footprint clause is ported.</b> The reference also answers yes to an adjusted
    /// creature size of <c>Large</c> and to the <c>IsAlwaysLarge</c> special ability; neither is
    /// carried onto a live combatant here. A monster drawn on more than one square is still large.
    /// </remarks>
    public static bool IsLarge(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return combatant.Icon.Width > 1 || combatant.Icon.Height > 1;
    }

    /// <summary>
    /// One candidate action, resolved against the projected combatants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>Me</c> is always position 0</b> — the reference assigns
    /// <c>csa.pMe = GetCombatant(activeCombatantIndex)</c> with the active index always 0 at the
    /// <c>THINK</c> call site — and <c>He</c> is the action's target.
    /// </para>
    /// <para>
    /// <b>An action whose target is not on the map falls back to the actor.</b> The reference
    /// cannot reach this, because it only ever builds actions against combatants it has just
    /// listed; here it keeps a stray target from dereferencing nothing.
    /// </para>
    /// </remarks>
    public static ForthAction Action(AiAction action, IReadOnlyList<ForthCombatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(combatants);

        if (combatants.Count == 0)
        {
            throw new ArgumentException("a summary needs at least the acting combatant",
                                        nameof(combatants));
        }

        var me = combatants[0];
        var he = combatants.FirstOrDefault(c => c.Index == action.Target) ?? me;

        return new ForthAction
        {
            ActionType = (int)action.Type,
            TargetOrdinal = IndexOf(combatants, he),
            WeaponOrdinal = action.WeaponOrdinal,
            Distance22 = action.Distance,
            Damage = action.Damage,
            Advance = action.Type == AiActionType.Advance ? 1 : 0,

            // Ammo and attack ordinals, and line of sight, have no reader: none of the twenty-one
            // words exposes the first two, and no shipped script asks for the third.
            HasLineOfSight = 0,
            Me = me,
            He = he,
        };
    }

    private static int IndexOf(IReadOnlyList<ForthCombatant> combatants, ForthCombatant wanted)
    {
        for (int i = 0; i < combatants.Count; i++)
        {
            if (ReferenceEquals(combatants[i], wanted))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// One weapon as the script reads it (<c>ListWeapons</c>, <c>Combatant.cpp:1142</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The damage is the same number for a large target as for a small one</b>, because
    /// <see cref="AiWeapons"/> only reads a weapon's small-target dice
    /// (<c>NbrDiceSm</c>/<c>DmgDiceSm</c>/<c>DmgBonusSm</c>). The reference keeps both pairs and
    /// <c>W:Damage</c> picks by the target's size. That gap is older than this projection and the
    /// transcribed <see cref="MonsterAiScript.Compare"/> shares it exactly — which is what keeps
    /// the two ranking paths comparable. Fixing it means widening <see cref="AiWeapon"/> and
    /// belongs with the damage estimate, not here.
    /// </para>
    /// <para>
    /// <b>Protection, rate of fire, attack bonus and priority are left at zero</b> — the four
    /// <c>W:</c> words that read them are called by no shipped script, and this port carries none
    /// of the four onto an <see cref="AiWeapon"/>.
    /// </para>
    /// </remarks>
    private static ForthWeapon Project(AiWeapon weapon)
    {
        return new ForthWeapon
        {
            Type = (int)weapon.Class,
            Range22 = MonsterAiScript.WeaponRange22(weapon.Range),
            SmallDamageDice = weapon.AverageDamage,
            SmallDamageBonus = weapon.DamageBonus,
            LargeDamageDice = weapon.AverageDamage,
            LargeDamageBonus = weapon.DamageBonus,
        };
    }
}
