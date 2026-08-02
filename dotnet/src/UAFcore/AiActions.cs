namespace UAFcore;

/// <summary>
/// One weapon a combatant could attack with, as the AI sees it
/// (<c>WEAPON_SUMMARY</c>, <c>CombatSummary.h:60</c>).
/// </summary>
/// <param name="Class">Which decides whether it is a spell item, a ranged weapon or melee.</param>
/// <param name="Range">Reach in squares. Converted to <c>range22</c> where the script wants it.</param>
/// <param name="AverageDamage">How two weapons of a kind are ranked against each other.</param>
/// <param name="HasSpell">
/// Whether a spell-casting item actually names a spell. <b>One that does not is skipped
/// entirely</b> — the reference returns before setting an action type
/// (<c>Combatant.cpp:1559</c>), so a charged wand with no spell yields no action at all rather than
/// a failed one.
/// </param>
public readonly record struct AiWeapon(WeaponClass Class, int Range, int AverageDamage = 0,
                                       bool HasSpell = false);

/// <summary>
/// Enumerating what a computer-run combatant could do this turn
/// (<c>ListActions</c> and its children, <c>Combatant.cpp:1770</c>).
/// </summary>
/// <remarks>
/// <para>
/// One candidate per (target, weapon) pair, plus one per unarmed attack, plus an advance on every
/// target. <see cref="MonsterAiScript"/> then ranks them. The reference builds this into a
/// <c>COMBAT_SUMMARY</c> that the Forth VM reads through; the list is the same either way.
/// </para>
/// <para>
/// <b>Every combatant is considered as a target, including the acting one.</b> The loop runs from
/// zero over all of them; what keeps a combatant from attacking itself is the friendly test, not
/// an identity test — except for unarmed attacks, which do check
/// (<c>Combatant.cpp:1629</c>).
/// </para>
/// </remarks>
public static class AiActions
{
    /// <summary>
    /// Every action worth considering.
    /// </summary>
    /// <param name="unarmedAttacks">
    /// How many natural attacks the combatant has (claws, bite). Each yields its own candidate.
    /// </param>
    /// <param name="canMove">Whether advancing is possible at all (<c>pMe-&gt;canMove</c>).</param>
    /// <param name="judoMeleeOnly">
    /// Suppresses spell items and ranged weapons, leaving melee and unarmed. The reference passes
    /// this when the combatant is somewhere it cannot use them.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>An ordinary weapon is never offered against a friend</b> — <c>if (friendly) return</c>
    /// comes before the ranged/melee split. A spell item passes that point, but the script's own
    /// <c>SpellCasterFilter</c> opens with <c>FGDP?</c>, whose first test is <c>Friendly? ?EXIT</c>,
    /// so it is refused a step later anyway. The two arrive at the same place by different routes,
    /// which is worth knowing before "simplifying" either.
    /// </para>
    /// <para>
    /// <b>A design's <c>CanTargetFriend</c>/<c>CanTargetEnemy</c> flags never reach the AI.</b> The
    /// checks that would have applied them are commented out in both spell branches
    /// (<c>Combatant.cpp:1561</c>, <c>:1578</c>), so a monster's choice of spell target is the
    /// script's business alone.
    /// </para>
    /// <para>
    /// <b>Advancing is offered even on an adjacent target.</b> The <c>distance22 &gt; 8</c> guard
    /// was removed in 2016, with a long comment explaining why: a combatant out of attacks could
    /// not advance on the enemy beside it, so it advanced on a further one, then back, forever.
    /// The engine turns an advance on an adjacent target into a guard.
    /// </para>
    /// </remarks>
    public static List<AiAction> For(Combatant self, IReadOnlyList<Combatant> all,
                                     IReadOnlyList<AiWeapon> weapons, int unarmedAttacks = 0,
                                     bool canMove = true, bool judoMeleeOnly = false,
                                     bool attacksTheDying = MonsterAiScript.AttacksTheDying)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(weapons);

        var actions = new List<AiAction>();

        foreach (var target in all)
        {
            // IsOnCombatMap is a status test only, as charOnCombatMap is -- a combatant can pass
            // it and still have no position, which the reference cannot reach because its summary
            // builder only lists placed combatants. The coordinate check stands in for that.
            if (!target.IsOnCombatMap() || target.X < 0 || target.Y < 0)
            {
                continue;
            }

            bool friendly = target.IsFriendly == self.IsFriendly;
            int distance = MonsterAiScript.DistanceBetween(self, target);

            foreach (var weapon in weapons)
            {
                if (WeaponAction(self, target, weapon, distance, friendly, judoMeleeOnly,
                                 attacksTheDying) is { } action)
                {
                    actions.Add(action);
                }
            }

            // Unarmed attacks, which are the one kind that refuses the acting combatant itself.
            if (target.Index != self.Index)
            {
                for (int i = 0; i < unarmedAttacks; i++)
                {
                    var judo = new AiAction(AiActionType.Judo, target.Index,
                                            Distance: distance);
                    if (MonsterAiScript.Survives(self, target, judo, distance, attacksTheDying))
                    {
                        actions.Add(judo);
                    }
                }
            }

            if (canMove)
            {
                var advance = new AiAction(AiActionType.Advance, target.Index,
                                           Distance: distance);
                if (MonsterAiScript.Survives(self, target, advance, distance, attacksTheDying))
                {
                    actions.Add(advance);
                }
            }
        }

        return actions;
    }

    private static AiAction? WeaponAction(Combatant self, Combatant target, AiWeapon weapon,
                                          int distance, bool friendly, bool judoMeleeOnly,
                                          bool attacksTheDying)
    {
        int range22 = 4 * weapon.Range * weapon.Range;

        AiActionType type;
        switch (weapon.Class)
        {
            case WeaponClass.SpellCaster:
            case WeaponClass.SpellLikeAbility:
                // A spell item with no spell yields nothing at all, and neither kind survives
                // judo-and-melee-only.
                if (judoMeleeOnly || !weapon.HasSpell)
                {
                    return null;
                }

                type = weapon.Class == WeaponClass.SpellCaster
                    ? AiActionType.SpellCaster
                    : AiActionType.SpellLikeAbility;
                break;

            default:
                if (friendly)
                {
                    return null;
                }

                if (MonsterAiScript.IsRangedWeapon(weapon.Range))
                {
                    if (judoMeleeOnly)
                    {
                        return null;
                    }

                    type = AiActionType.RangedWeapon;
                }
                else
                {
                    type = AiActionType.MeleeWeapon;
                }

                break;
        }

        var action = new AiAction(type, target.Index, weapon.Class, weapon.AverageDamage,
                                  distance);

        return MonsterAiScript.Survives(self, target, action, range22, attacksTheDying)
            ? action
            : null;
    }
}
