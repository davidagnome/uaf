namespace UAFcore;

/// <summary>
/// What a computer-run combatant is considering doing (<c>ACTION_TYPE</c>,
/// <c>CombatSummary.h:134</c>).
/// </summary>
/// <remarks>The numbering is what the AI script's <c>A:T:</c> constants hold.</remarks>
public enum AiActionType
{
    Unknown = 0,
    SpellCaster = 1,
    Advance = 2,
    RangedWeapon = 3,
    MeleeWeapon = 4,

    /// <summary>Hands and feet — the reference's own comment.</summary>
    Judo = 5,

    SpellLikeAbility = 6,
}

/// <summary>
/// One thing a combatant could do this turn (<c>COMBAT_SUMMARY_ACTION</c>,
/// <c>CombatSummary.h:145</c>).
/// </summary>
/// <param name="Type">What kind of action.</param>
/// <param name="Target">The combatant it is aimed at.</param>
/// <param name="WeaponType">The <see cref="WeaponClass"/> of the weapon it would use.</param>
/// <param name="Damage">Average damage, which is how two actions of a kind are ranked.</param>
/// <param name="Distance">
/// <b>Not a number of squares.</b> This is <c>distance22</c> — <c>4 × (dx² + dy²)</c> between the
/// nearest edges of the two footprints (<c>Distance22</c>, <c>Combatant.cpp:1674</c>) — and it is
/// what the script's <c>C:Distance</c> pushes (<c>Forth.cpp:2149</c>). Every threshold in the
/// script is in these units. Use <see cref="MonsterAiScript.DistanceBetween"/> to compute it.
/// </param>
/// <param name="WeaponOrdinal">
/// Which of the actor's weapons this action would use — <b>one-based, with 0 meaning none</b>
/// (<c>weaponOrd</c>).
/// </param>
/// <remarks>
/// <b><paramref name="WeaponType"/> and <paramref name="WeaponOrdinal"/> are the same fact stored
/// twice</b>, and deliberately. The transcribed <see cref="MonsterAiScript.Compare"/> reads the
/// type directly, where the script reaches it as <c>W:Type</c> — the weapon at
/// <paramref name="WeaponOrdinal"/> of the <i>selected combatant</i>. The ordinal is what the Forth
/// path needs; without it every <c>W:</c> word pushes <c>NotWeapon</c> and the script's first two
/// tests, which are the ones that put spell items ahead of everything, can never fire.
/// </remarks>
public readonly record struct AiAction(
    AiActionType Type, int Target, WeaponClass WeaponType = WeaponClass.NotWeapon,
    int Damage = 0, int Distance = 0, int WeaponOrdinal = 0);

/// <summary>
/// The monster AI's priority ordering — what the shipped <c>AI_Script.BLK</c> decides
/// (<c>RunTHINK</c>, <c>Forth.cpp:2510</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the shipped script's decision function, not the Forth VM that runs it.</b> The
/// reference evaluates a 143-line Forth program through a 2,500-line indirect-threaded interpreter,
/// called as a <i>comparator</i>: <c>THINK</c> is handed two candidate actions and returns
/// A minus B, positive meaning A is preferred, and the caller heap-sorts the candidate list with it
/// (<c>Combatant.cpp:2251</c>). What is ported here is the ordering that program expresses.
/// </para>
/// <para>
/// <b>Why that is a reasonable substitution, and where it is not.</b> <c>AI_Script.BLK</c> ships in
/// every design's <c>Data</c> folder and <c>ExpandKernel</c> <c>die()</c>s without it — so it is
/// engine data with a version rather than design content. Across the four reference designs there
/// are exactly two versions, and they differ by <b>one line</b>: 1.01 (October 2014) adds
/// <c>Dying?</c> to the do-not-attack filter, which 0.999785 (August 2014) lacks. Both are
/// reproduced, selectable by <see cref="AttacksTheDying"/>. <b>A design that edited its own script
/// would not be honoured</b>, and that needs the VM.
/// </para>
/// <para>
/// The script's own comments give the order, and they are worth quoting because they are the
/// specification: spell-caster items first ("used first if the monster has them"), then spell-like
/// abilities ("Dragon Breath, Medusa Gase"), then ranged weapons by average damage, then melee, then
/// unarmed, then advancing on the nearest enemy — "the only action left is to guard".
/// </para>
/// </remarks>
public static class MonsterAiScript
{
    /// <summary>
    /// Whether a dying target may still be attacked.
    /// </summary>
    /// <remarks>
    /// The one difference between the two shipped scripts. Version 1.01 lists <c>Dying?</c> in
    /// <c>FGDP?</c> and in <c>AdvanceFilter</c>; 0.999785 does not, so an older design's monsters
    /// keep hitting a combatant who is bleeding out. Defaults to the newer behaviour.
    /// </remarks>
    public const bool AttacksTheDying = false;

    /// <summary>
    /// Whether an action's target is worth attacking (<c>FGDP?</c> — "Friendly Gone Dead Dying or
    /// Petrified Targets should not be attacked").
    /// </summary>
    /// <remarks>
    /// <b>Friendly is tested first and exits early</b> — <c>Friendly? ?EXIT</c> — so a friendly
    /// target is refused whatever its condition. The rest are OR-ed together.
    /// </remarks>
    public static bool IsWorthAttacking(Combatant attacker, Combatant target,
                                        bool attacksTheDying = AttacksTheDying)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        if (target.IsFriendly == attacker.IsFriendly)
        {
            return false;
        }

        return target.Status switch
        {
            CharacterStatus.Gone or CharacterStatus.Dead or CharacterStatus.Petrified => false,
            CharacterStatus.Dying => attacksTheDying,
            _ => true,
        };
    }

    /// <summary>
    /// The reference's distance measure: <c>4 × (dx² + dy²)</c> between the nearest edges of two
    /// footprints (<c>Distance22</c>, <c>Combatant.cpp:1674</c>).
    /// </summary>
    /// <remarks>
    /// <b>Doubled and then squared</b>, which is what the trailing "22" in the name means. The
    /// doubling buys a half-square of resolution without floating point, and the squaring avoids a
    /// square root — but it means <b>every threshold in the AI script is in these units and none of
    /// them are square counts</b>. Reading them as squares inverts two of the three range rules.
    /// </remarks>
    public static int DistanceBetween(Combatant a, Combatant b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        int dx = Gap(a.X, a.Icon.Width, b.X, b.Icon.Width);
        int dy = Gap(a.Y, a.Icon.Height, b.Y, b.Icon.Height);
        return 4 * ((dx * dx) + (dy * dy));
    }

    /// <summary>The gap between two spans on one axis, zero when they overlap.</summary>
    private static int Gap(int aStart, int aSize, int bStart, int bSize)
    {
        int aEnd = aStart + aSize - 1;
        int bEnd = bStart + bSize - 1;

        if (aEnd < bStart)
        {
            return bStart - aEnd;
        }

        return bEnd < aStart ? aStart - bEnd : 0;
    }

    /// <summary>
    /// Closer than a ranged weapon will shoot (<c>TooNear?</c>: <c>C:Distance 5 &lt;</c>).
    /// </summary>
    /// <remarks>
    /// <b>This is adjacency, not "within five squares".</b> In <c>distance22</c> units the test is
    /// <c>4d² &lt; 5</c>, so it only holds at <c>d ≤ 1</c>. A monster with a bow refuses to shoot
    /// somebody standing next to it and nothing else.
    /// </remarks>
    public const int RangedMinimumDistance = 5;

    /// <summary>
    /// Beyond a judo attack's reach (<c>NotAdjacent?</c>: <c>C:Distance 8 &gt;</c>).
    /// </summary>
    /// <remarks>
    /// <b>Also adjacency</b>, and here the script's own name for the test says so. <c>4d² &gt; 8</c>
    /// holds from <c>d ≥ 2</c>, so unarmed attacks reach exactly the adjacent squares — including
    /// the diagonals, since <c>d</c> is the axis gap and a diagonal neighbour has a gap of one on
    /// both axes, giving <c>8</c>, which the test does not exceed.
    /// </remarks>
    public const int JudoMaximumDistance = 8;

    /// <summary>
    /// A weapon's reach in the units the filters compare against
    /// (<c>GetWeaponRange</c>, <c>Combatant.cpp:1130</c>).
    /// </summary>
    /// <remarks>
    /// <b>This is <c>(2r + 1)²</c>, which is not the transform
    /// <see cref="DistanceBetween"/> uses.</b> A distance is <c>(2d)²</c>; a range is
    /// <c>(2r + 1)²</c> — half a square longer before squaring. The two are nonetheless compared
    /// directly (<c>TooFar?</c> is <c>W:Range &lt; C:Distance</c>), and the half-square is what
    /// makes a reach of <c>r</c> cover a distance of exactly <c>r</c>: <c>(2r+1)² &lt; (2d)²</c>
    /// reduces to <c>r &lt; d − ½</c>, which for whole squares is <c>r &lt; d</c>.
    /// <para>
    /// <b>A reach above 90 becomes 32767 rather than its square</b>, which is effectively
    /// unlimited and is not what the formula would have given (32761). The clamp comes first.
    /// </para>
    /// </remarks>
    public static int WeaponRange22(int range)
    {
        if (range > UnlimitedRangeAbove)
        {
            return UnlimitedRange;
        }

        int scaled = (2 * range) + 1;
        return scaled * scaled;
    }

    /// <inheritdoc cref="WeaponRange22"/>
    public const int UnlimitedRangeAbove = 90;

    /// <inheritdoc cref="WeaponRange22"/>
    public const int UnlimitedRange = 32767;

    /// <summary>
    /// The smallest <c>range22</c> that counts as a ranged weapon
    /// (<c>pWeapon-&gt;range22 &gt; 9</c>, <c>Combatant.cpp:1592</c>).
    /// </summary>
    /// <remarks>
    /// Nine is <c>(2·1 + 1)²</c> — exactly a reach of one — so the test excludes it and a reach-1
    /// weapon is melee while anything longer is ranged. The split is made on the <i>weapon</i>,
    /// not on how far the target happens to be.
    /// </remarks>
    public const int RangedWeaponThreshold = 9;

    /// <summary>Whether a weapon of this reach is treated as ranged rather than melee.</summary>
    public static bool IsRangedWeapon(int range) => WeaponRange22(range) > RangedWeaponThreshold;

    /// <summary>
    /// Whether an action survives its filter (the five <c>*Filter</c> words).
    /// </summary>
    /// <param name="weaponRange22">
    /// The weapon's reach in <c>distance22</c> units, for the too-far test — the script compares
    /// <c>W:Range</c> against <c>C:Distance</c> directly, so both are in the same units.
    /// </param>
    /// <remarks>
    /// Each action type has its own filter and they are not the same: a ranged weapon is refused
    /// both when the target is out of range and when it is <i>adjacent</i>; judo is refused beyond
    /// adjacency; advancing skips the range tests entirely and only checks the target's condition.
    /// </remarks>
    public static bool Survives(Combatant attacker, Combatant target, AiAction action,
                                int weaponRange22, bool attacksTheDying = AttacksTheDying)
    {
        if (action.Type == AiActionType.Advance)
        {
            return IsWorthAttacking(attacker, target, attacksTheDying);
        }

        if (!IsWorthAttacking(attacker, target, attacksTheDying))
        {
            return false;
        }

        return action.Type switch
        {
            AiActionType.Judo => action.Distance <= JudoMaximumDistance,
            AiActionType.RangedWeapon =>
                action.Distance <= weaponRange22 && action.Distance >= RangedMinimumDistance,
            _ => action.Distance <= weaponRange22,
        };
    }

    /// <summary>
    /// Ranks two candidate actions (<c>THINK</c>).
    /// </summary>
    /// <returns>
    /// Positive when <paramref name="a"/> is preferred, negative when <paramref name="b"/> is,
    /// zero when the script cannot tell them apart.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Eight tests in order, each exiting as soon as it has an opinion (<c>?EXIT</c>). Reproduced
    /// as written, including the two places where the script's comparison is by <i>weapon</i> type
    /// rather than action type — the spell-caster and spell-like tests read <c>W:Type</c>, while
    /// everything after them reads <c>A:Type</c>.
    /// </para>
    /// <para>
    /// <b>The melee test is a subtraction of two booleans, not a three-way compare.</b> The script
    /// writes <c>B A:Type A:T:MeleeWeapon = A A:Type A:T:MeleeWeapon = -</c>, which is
    /// <c>isMelee(B) − isMelee(A)</c> — the operands the other way round from every neighbouring
    /// test, and it works because Forth's <c>=</c> yields −1 for true. Getting the sign wrong here
    /// makes monsters prefer <i>not</i> to use a weapon.
    /// </para>
    /// </remarks>
    public static int Compare(AiAction a, AiAction b)
    {
        // Spell-caster items first, then spell-like abilities. Both compare weapon types.
        int result = Rank(a.WeaponType == WeaponClass.SpellCaster,
                          b.WeaponType == WeaponClass.SpellCaster);
        if (result != 0)
        {
            return result;
        }

        result = Rank(a.WeaponType == WeaponClass.SpellLikeAbility,
                      b.WeaponType == WeaponClass.SpellLikeAbility);
        if (result != 0)
        {
            return result;
        }

        // Ranged weapons: preferred outright, and between two of them the higher average damage.
        bool rangedA = a.Type == AiActionType.RangedWeapon;
        bool rangedB = b.Type == AiActionType.RangedWeapon;
        if (rangedA && rangedB)
        {
            result = a.Damage - b.Damage;
            if (result != 0)
            {
                return result;
            }
        }
        else if (rangedA || rangedB)
        {
            return rangedA ? 1 : -1;
        }

        // Melee: a boolean subtraction in the script, and -1 is true there, so isMelee(a) wins.
        bool meleeA = a.Type == AiActionType.MeleeWeapon;
        bool meleeB = b.Type == AiActionType.MeleeWeapon;
        if (meleeA != meleeB)
        {
            return meleeA ? 1 : -1;
        }

        if (meleeA && meleeB)
        {
            result = a.Damage - b.Damage;
            if (result != 0)
            {
                return result;
            }
        }

        result = Rank(a.Type == AiActionType.Judo, b.Type == AiActionType.Judo);
        if (result != 0)
        {
            return result;
        }

        result = Rank(a.Type == AiActionType.Advance, b.Type == AiActionType.Advance);
        if (result != 0)
        {
            return result;
        }

        // Between two advances, the closer target. Both must be advances -- the script exits with
        // zero the moment either is not.
        if (a.Type == AiActionType.Advance && b.Type == AiActionType.Advance)
        {
            return b.Distance - a.Distance;
        }

        // "The only action left is to guard."
        return 0;
    }

    /// <summary>
    /// The shape every one of the script's category tests takes: having the trait beats not having
    /// it, and two that agree are indistinguishable.
    /// </summary>
    private static int Rank(bool a, bool b) => a == b ? 0 : a ? 1 : -1;

    /// <summary>
    /// Puts a best action first (the tree insertion around <c>RunTHINK</c>,
    /// <c>Combatant.cpp:2237</c>–<c>:2255</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The reference builds a heap and reads only its root</b> — it is not a sort, whatever an
    /// earlier revision of this comment said. Sorting here is deliberate all the same: the engine
    /// walks past a choice whose target has gone, and after a heap build the second element is not
    /// the second-best, so a sorted list is what makes that walk mean anything.
    /// </para>
    /// <para>
    /// <b>Where the comparator has no opinion the two disagree, and neither is wrong.</b> Two
    /// spell-caster actions of equal damage against different targets score 0 — every one of
    /// <c>THINK</c>'s tests reads the action or weapon type, never the target — so a heap and a sort
    /// stop at different ones. <c>ForthAiEquivalenceTests</c> measures this against the real script:
    /// every pair ranks the same way, and each path's head is unbeaten, but the heads themselves
    /// need not be the same action.
    /// </para>
    /// </remarks>
    public static List<AiAction> Rank(IEnumerable<AiAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var ranked = actions.ToList();
        ranked.Sort((a, b) => Compare(b, a));
        return ranked;
    }
}
