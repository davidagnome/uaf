using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Turns a combat event's monster list into combatants
/// (<c>AddCombatants</c> / <c>AddMonstersToCombatants</c>, <c>Combatants.cpp:490</c>, <c>:660</c>).
/// </summary>
/// <remarks>
/// The join between the event reader and the combat machinery: an event names monster ids and
/// quantities, and everything downstream wants <see cref="Combatant"/> objects.
/// </remarks>
public static class EncounterBuilder
{
    /// <summary>
    /// The most combatants an encounter can hold (<c>MAX_COMBATANTS</c>, <c>Combatants.h:29</c>).
    /// </summary>
    public const int MaxCombatants = 100;

    /// <summary>How a monster entry says how many of it there are.</summary>
    /// <remarks><c>MONSTER_EVENT::meUseQty</c> — the alternative is to roll the dice.</remarks>
    public const int UseLiteralQuantity = 0;

    /// <summary>
    /// Builds the encounter's combatant list.
    /// </summary>
    /// <param name="combat">The event.</param>
    /// <param name="party">The party, which goes in first and keeps its order.</param>
    /// <param name="rollDice">
    /// <c>RollDice(sides, times, bonus)</c> — the sum of <paramref name="rollDice"/> rolls plus a
    /// bonus, returning the bonus alone when either count is non-positive
    /// (<c>Globals.cpp:4925</c>).
    /// </param>
    /// <param name="monsterInfo">
    /// The monster record for an id, for icon size and attacks. Null skips the entry.
    /// </param>
    /// <param name="quantityModPercent">
    /// The design's monster-quantity modifier, as a percentage
    /// (<c>GetMonsterQtyMod</c>): +50 makes every group half as large again.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The party goes in first, always.</b> The reference says so in a comment
    /// (<c>Combatants.cpp:492</c>) and everything downstream depends on it: combatant indices are
    /// grid occupancy values, and <see cref="CombatPlacement"/> places the party from the front of
    /// the list.
    /// </para>
    /// <para>
    /// <b>A random encounter picks one entry, not one monster.</b> With the flag set, a single
    /// entry is drawn from the list and its <i>quantity</i> is still rolled — so "random" means
    /// which kind shows up, not how many.
    /// </para>
    /// </remarks>
    /// <param name="iconSize">
    /// The footprint a monster's combat icon measures to. Null gives every monster one square —
    /// see <see cref="CombatIcons.SizeOf"/> for why the record alone cannot answer this.
    /// </param>
    public static IReadOnlyList<Combatant> Build(CombatEvent combat,
                                                 IReadOnlyList<Combatant> party,
                                                 Func<int, int, int, int> rollDice,
                                                 Func<string, MonsterRecord?> monsterInfo,
                                                 double quantityModPercent = 0,
                                                 Func<MonsterRecord, CombatantIcon>? iconSize = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(rollDice);
        ArgumentNullException.ThrowIfNull(monsterInfo);

        var all = new List<Combatant>(party);

        if (combat.Monsters.Count == 0)
        {
            return all;
        }

        // The reference computes this once, before adding anything, so it does not shrink as
        // monsters go in.
        int allowed = MaxCombatants - all.Count - 1;

        var entries = combat.RandomMonster != 0
            ? [PickOne(combat.Monsters, rollDice)]
            : combat.Monsters;

        foreach (var entry in entries)
        {
            int count = QuantityFor(entry, rollDice, quantityModPercent,
                                    limit: combat.RandomMonster != 0 ? allowed : null);

            var record = monsterInfo(entry.MonsterId);
            if (record is null)
            {
                continue;
            }

            for (int i = 0; i < count && all.Count < MaxCombatants; i++)
            {
                var monster = FromMonster(all.Count, entry, record,
                                          iconSize?.Invoke(record)
                                          ?? new CombatantIcon(DefaultIconSide, DefaultIconSide));
                monster.MaxHitPoints = RollHitPoints(record, rollDice);
                monster.HitPoints = monster.MaxHitPoints;
                all.Add(monster);
            }
        }

        return all;
    }

    /// <summary>
    /// Draws one entry from the list (<c>Combatants.cpp:676</c>).
    /// </summary>
    /// <remarks>
    /// <c>RollDice(count, 1, 0)</c> gives 1..count, which is then made zero-based and floored at
    /// zero — the floor matters because a roller returning its bonus for a non-positive side count
    /// would otherwise index at −1.
    /// </remarks>
    private static MonsterEvent PickOne(IReadOnlyList<MonsterEvent> monsters,
                                        Func<int, int, int, int> rollDice) =>
        monsters[Math.Clamp(rollDice(monsters.Count, 1, 0) - 1, 0, monsters.Count - 1)];

    /// <summary>
    /// How many of one entry appear (<c>Combatants.cpp:682</c> and <c>:808</c>).
    /// </summary>
    /// <param name="limit">
    /// The cap, applied only on the random-monster path. <b>The two branches differ here</b>: the
    /// random one clamps to the remaining room <i>before</i> the floor-at-one, and the ordinary one
    /// does not clamp at all — it relies on the add loop running out of room instead.
    /// </param>
    /// <remarks>
    /// The quantity modifier is applied as a proportion of the rolled count and truncated with it,
    /// so a +50% modifier on a roll of 3 gives 4, not 4.5.
    /// </remarks>
    private static int QuantityFor(MonsterEvent entry, Func<int, int, int, int> rollDice,
                                   double quantityModPercent, int? limit)
    {
        int count = entry.UseQty == UseLiteralQuantity
            ? entry.Quantity
            : rollDice(entry.QtyDiceSides, entry.QtyDiceQty, entry.QtyBonus);

        if (quantityModPercent != 0)
        {
            count += (int)(quantityModPercent / 100.0 * count);
        }

        if (limit is { } max && count > max)
        {
            count = max;
        }

        // Floors last, so a cap of zero still yields one monster.
        return count <= 0 ? 1 : count;
    }

    /// <summary>Creates one monster combatant from its record.</summary>
    /// <remarks>
    /// <b>Monsters are always computer-run</b>, and <c>friendly</c> on the entry is an integer
    /// rather than a flag because the editor offers it as a choice — a friendly monster fights on
    /// the party's side.
    /// </remarks>
    private static Combatant FromMonster(int index, MonsterEvent entry, MonsterRecord record,
                                         CombatantIcon icon) =>
        FromMonster(index, entry.Friendly != 0, record, icon);

    /// <summary>
    /// One monster combatant, on whichever side it is told.
    /// </summary>
    /// <remarks>
    /// Split out from the encounter path so a monster can also join a fight already running —
    /// <c>$AddCombatant</c>, which names its side directly rather than reading it off an encounter
    /// entry. <b>It is left unplaced</b> at (−1, −1); see
    /// <see cref="CombatSession.AddMonster"/> for why the reference does not place one either.
    /// </remarks>
    public static Combatant FromMonster(int index, bool isFriendly, MonsterRecord record,
                                        CombatantIcon icon) =>
        new(index, isFriendly, icon, record.Name)
        {
            Kind = CombatantKind.Monster,
            IsAuto = true,
            MaxMovement = record.Movement,
            TotalAttacks = Math.Max(1, record.Attacks.Count),
            AvailableAttacks = Math.Max(1, record.Attacks.Count),
            IsUndead = !string.IsNullOrEmpty(record.UndeadType),

            // The four trait bitfields, which the sixteen $GET_IS*/$GET_HAS* calls read.
            FormType = record.FormType,
            PenaltyType = record.PenaltyType,
            ImmunityType = record.ImmunityType,
            MiscOptionsType = record.MiscOptionsType,
        };

    /// <summary>
    /// Rolls a monster's hit points from its hit dice
    /// (<c>CHARACTER::determineMaxHitPoints</c>'s monster branch, <c>Char.cpp:4941</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>UseHitDice</c> being false means the field holds hit <i>points</i>, not dice</b> —
    /// the value is taken literally with no roll and no bonus. A design can therefore pin a
    /// monster's hit points exactly, and reading the field as dice regardless would reroll them
    /// into something else entirely.
    /// </para>
    /// <para>
    /// <b>Hit dice are d8, and a fractional die scales the sides rather than the count.</b> Under
    /// one hit die the roll is <c>1d(8 × hd)</c> — so a half-die monster rolls 1d4, not half of
    /// 1d8. At one or above it is <c>hd</c>d8 with the count truncated.
    /// </para>
    /// <para>
    /// The result floors at 1: no monster arrives already dead.
    /// </para>
    /// </remarks>
    public static int RollHitPoints(MonsterRecord record, Func<int, int, int, int> rollDice)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(rollDice);

        double hitDice = record.HitDice;

        if (record.UseHitDice == 0)
        {
            // The field is hit points already -- no roll, and the bonus does not apply.
            return Math.Max(1, (int)hitDice);
        }

        int rolled = hitDice < 1.0
            ? rollDice((int)(8.0 * hitDice), 1, record.HitDiceBonus)
            : rollDice(8, (int)hitDice, record.HitDiceBonus);

        return Math.Max(1, rolled);
    }

    /// <summary>
    /// The footprint a monster gets when its icon cannot be measured.
    /// </summary>
    /// <remarks>
    /// <b>The reference never takes this from the monster record.</b> <c>determineIconSize</c>
    /// divides the <i>loaded icon's</i> pixel dimensions by the tile size, so a design's art
    /// decides how much room a monster occupies — see <see cref="CombatIcons.SizeOf"/>. One square
    /// is the safe fallback when the art is missing: too small never refuses a placement that
    /// should have succeeded, whereas too large would.
    /// </remarks>
    private const int DefaultIconSide = 1;
}
