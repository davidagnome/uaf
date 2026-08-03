using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// One baseclass a character has levels in (<c>BASECLASS_STATS</c>, <c>Shared/Char.h:238</c>).
/// </summary>
/// <remarks>
/// A multiclass character has several, and each advances on its own experience. The
/// <see cref="PreviousLevel"/> field is not history — see <see cref="Character.GiveExperience"/>.
/// </remarks>
public sealed class BaseclassProgress(string baseclassId, int currentLevel, int previousLevel,
                                      int experience)
{
    public string BaseclassId { get; } = baseclassId;

    public int CurrentLevel { get; set; } = currentLevel;

    /// <summary>The level before a drain, or 0 when the character has not been drained.</summary>
    public int PreviousLevel { get; set; } = previousLevel;

    public int Experience { get; set; } = experience;

    /// <summary>
    /// Adds experience (<c>IncCurExperience</c>, <c>Shared/class.cpp:4828</c>).
    /// </summary>
    /// <returns>The new total, or 0 when the gain was refused.</returns>
    /// <remarks>
    /// <b>A drained baseclass gains nothing.</b> The guard is <c>previousLevel &gt; 0</c>, which
    /// marks a character whose level was drained and not yet restored — so it is frozen until it
    /// is. Nothing clamps the total, so a negative award really does subtract.
    /// </remarks>
    public int Add(int experience)
    {
        if (PreviousLevel > 0)
        {
            return 0;
        }

        return Experience += experience;
    }
}

/// <summary>
/// A party member, with the state that changes during play.
/// </summary>
/// <remarks>
/// <para>
/// Wraps the <see cref="CharacterRecord"/> read off disk rather than replacing it: identity and
/// the scores that do not change are read straight through, while hit points, experience, money
/// and inventory become mutable here. That keeps the record honest as a snapshot of the file and
/// gives the rules somewhere to write.
/// </para>
/// <para>
/// <b>Not all of a character is mutable yet.</b> Ability scores, saving throws, spell books and
/// special abilities are still read from the record, because nothing modifies them until combat
/// and spellcasting exist.
/// </para>
/// </remarks>
public sealed class Character
{
    private readonly List<BaseclassProgress> baseclasses;

    public Character(CharacterRecord record, MoneyRules money)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(money);

        Record = record;
        HitPoints = record.HitPoints;
        Purse = Purse.FromRecord(record.Money, money);
        Items = [.. record.Items.Items];
        Status = (CharacterStatus)record.Status;
        Morale = record.Morale;
        Attributes.Load(record.Attributes);

        baseclasses =
        [
            .. record.BaseclassStats.Select(
                b => new BaseclassProgress(b.BaseclassId, b.CurrentLevel, b.PreviousLevel,
                                           b.Experience)),
        ];
    }

    /// <summary>The record this was built from. Identity and unported fields come from here.</summary>
    public CharacterRecord Record { get; }

    public string Name => Record.Name;

    public string CharacterId => Record.CharacterId;

    public string ClassId => Record.ClassId;

    public string Race => Record.Race;

    public Gender Gender => (Gender)Record.Gender;

    public int MaxHitPoints => Record.MaxHitPoints;

    public int ArmorClass => Record.ArmorClass;

    public bool ReadyToTrain => Record.ReadyToTrain != 0;

    /// <summary>Current hit points, which combat and healing move.</summary>
    public int HitPoints { get; set; }

    /// <summary>
    /// The spell effects currently on this character (<c>m_spellEffects</c>).
    /// </summary>
    /// <remarks>
    /// The same list combat keeps on a <see cref="Combatant"/>, held here so the adjusted
    /// accessors mean something outside a fight — a blessed character walking a corridor has the
    /// armour class the blessing gives.
    /// </remarks>
    public UAF.Rules.SpellEffectList Effects { get; } = new();

    /// <summary>The widest armour class the rules allow (<c>MAX_AC</c>, <c>Char.h:33</c>).</summary>
    /// <remarks>
    /// <b>The bounds are the wrong way round from what the names suggest.</b> Armour class counts
    /// down, so <c>MAX_AC</c> is 10 — the <i>worst</i> — and <c>MIN_AC</c> is −500. A clamp written
    /// as "at least MIN, at most MAX" is right; one written as "at least MAX" inverts the rule.
    /// </remarks>
    public const int WorstArmorClass = 10;

    /// <inheritdoc cref="WorstArmorClass"/>
    public const int BestArmorClass = -500;

    /// <summary>
    /// Armour class with spell effects applied (<c>GetAdjAC</c>, <c>Char.cpp:13198</c>).
    /// </summary>
    public int AdjustedArmorClass =>
        Effects.Apply(ArmorClass, "$CHAR_AC", BestArmorClass, WorstArmorClass);

    /// <summary>
    /// Hit points with spell effects applied (<c>GetAdjHitPoints</c>, <c>Char.cpp:13239</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Floored at −10, then capped at the character's own maximum.</b> Ten below zero is where a
    /// character is finally dead rather than dying, so an effect cannot drain someone past it —
    /// and a healing effect cannot push anyone above their maximum however large it is.
    /// </para>
    /// <para>
    /// <b>Floor then ceiling, in that order, and not <see cref="Math.Clamp(int,int,int)"/>.</b> The
    /// reference is <c>val = max(-10,val); val = min(val, GetMaxHitPoints());</c>
    /// (<c>Char.cpp:13252</c>), so for a character whose <see cref="MaxHitPoints"/> is below
    /// <see cref="DeadAt"/> the maximum simply wins — where <c>Math.Clamp</c> throws
    /// <see cref="ArgumentException"/> because its bounds are crossed. Degenerate data, but the
    /// reference has a value for it and this is read on the path that decides whether a character
    /// is dead. <see cref="Attack.ApplyDamage"/> and
    /// <see cref="EventDamage.GiveCharacterDamage"/> clamp the same way round for the same reason.
    /// </para>
    /// </remarks>
    public int AdjustedHitPoints =>
        Math.Min(Math.Max((int)Effects.Apply(HitPoints, "$CHAR_HITPOINTS"), DeadAt), MaxHitPoints);

    /// <summary>Hit points at which a character is dead rather than dying.</summary>
    public const int DeadAt = -10;

    /// <summary>The character's base to-hit number (<c>GetTHAC0</c>, <c>Char.cpp:6074</c>).</summary>
    public int Thac0 => Record.Thac0;

    /// <summary>
    /// The worst to-hit number the rules allow (<c>MAX_THAC0</c>, <c>Char.h:36</c>).
    /// </summary>
    /// <remarks>
    /// Like armour class, THAC0 counts <b>down</b> — so <c>MAX_THAC0</c> is 20, the worst, and
    /// <c>MIN_THAC0</c> is −500. The same naming trap, in the same header, two lines apart.
    /// </remarks>
    public const int WorstThac0 = 20;

    /// <inheritdoc cref="WorstThac0"/>
    public const int BestThac0 = -500;

    /// <summary>
    /// To-hit number with bonuses and spell effects applied
    /// (<c>GetAdjTHAC0</c>, <c>Char.cpp:13138</c>).
    /// </summary>
    /// <param name="hitBonus">
    /// The character's adjusted hit bonus — strength, mostly. <b>Subtracted</b>, because a lower
    /// THAC0 is better.
    /// </param>
    /// <param name="weaponAttackBonus">
    /// The readied weapon's own attack bonus, also subtracted. Zero when nothing is readied.
    /// </param>
    /// <remarks>
    /// <b>More than a clamp, unlike its neighbours.</b> Where <see cref="AdjustedArmorClass"/> is
    /// base-then-effects-then-clamp, this subtracts two bonuses first — and gets them from the
    /// caller here because the port has no readied-item model on a character outside combat.
    /// <para>
    /// The reference fetches the readied item <i>before</i> testing whether one exists, so
    /// <c>GetItem(NO_READY_ITEM)</c> is called and its result handed to the hit-bonus lookup. Only
    /// the weapon's attack bonus is properly guarded. Nothing observable turns on it.
    /// </para>
    /// </remarks>
    public int AdjustedThac0(int hitBonus = 0, int weaponAttackBonus = 0) =>
        Effects.Apply(Thac0 - hitBonus - weaponAttackBonus, "$CHAR_THAC0",
                      BestThac0, WorstThac0);

    /// <summary>
    /// This character's own attribute store (<c>char_asl</c>), seeded from its record.
    /// </summary>
    /// <remarks>
    /// <b>Its only live use in the engine is <see cref="KnowableSpells"/></b> — everything else it
    /// holds is design data that nothing reads back. It is saved with the character, minus its
    /// read-only entries.
    /// </remarks>
    public AttributeList Attributes { get; } = new();

    /// <summary>
    /// The character's condition (<c>charStatusType</c>). Seeded from the record and changed by
    /// combat.
    /// </summary>
    /// <remarks>
    /// Load-bearing outside combat too: only a character who is
    /// <see cref="CharacterStatus.Okay"/> shares in a fight's experience
    /// (<see cref="CombatAftermath.Distribute"/>).
    /// </remarks>
    public CharacterStatus Status { get; set; }

    /// <summary>
    /// The character's morale (<c>CHARACTER::morale</c>).
    /// </summary>
    /// <remarks>
    /// <b>Settable, unlike the other record-backed stats.</b> An NPC joining the party is assigned
    /// a morale on the way in (<c>ADD_NPC_DATA</c>), so this is live state rather than a view of
    /// the record — which is why it is seeded from the record and then owned here, exactly as
    /// <see cref="Status"/> is.
    /// </remarks>
    public int Morale { get; set; }

    /// <summary>This character's own money, as distinct from the party's pooled purse.</summary>
    public Purse Purse { get; }

    /// <summary>What the character carries.</summary>
    public List<ItemInstance> Items { get; }

    public IReadOnlyList<BaseclassProgress> Baseclasses => baseclasses;

    /// <summary>Total experience across every baseclass.</summary>
    public int TotalExperience => baseclasses.Sum(b => b.Experience);

    public BaseclassProgress? Baseclass(string baseclassId) =>
        baseclasses.FirstOrDefault(
            b => string.Equals(b.BaseclassId, baseclassId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Awards experience, split across the character's baseclasses
    /// (<c>giveCharacterExperience</c>, <c>Shared/Char.cpp:5787</c>).
    /// </summary>
    /// <returns>How much was actually added, which is not the award — see the remarks.</returns>
    /// <remarks>
    /// <para>
    /// <b>The split rounds up, so a multiclass character gains more than the award.</b>
    /// <c>curExp = (exppts + n - 1) / n</c> is ceiling division and each baseclass then receives
    /// that full share: 100 experience across 3 baseclasses is 34 each, 102 in total. Dividing
    /// evenly instead would quietly slow every multiclass character in every design.
    /// </para>
    /// <para>
    /// An award of 0 returns immediately, so it is not the same as awarding nothing to each
    /// baseclass — with the drain guard below, the two can differ.
    /// </para>
    /// <para>
    /// <b>One deviation, and it is in where the count comes from.</b> The reference takes
    /// <c>n</c> from the <i>class definition</i>'s baseclass list (<c>classes.dat</c>) and writes
    /// into the <i>character</i>'s own stats, dropping any share whose baseclass the character
    /// lacks. This port has no reader for <c>classes.dat</c> yet, so it counts the character's own
    /// baseclasses. The two agree whenever a character's stats match its class — which the Phase 1
    /// walk found to hold across the reference designs — and differ only for a character whose
    /// record disagrees with its own class definition.
    /// </para>
    /// </remarks>
    public int GiveExperience(int points)
    {
        if (points == 0 || baseclasses.Count == 0)
        {
            return 0;
        }

        int n = baseclasses.Count;
        int share = (points + n - 1) / n;

        int awarded = 0;
        foreach (var baseclass in baseclasses)
        {
            int before = baseclass.Experience;
            baseclass.Add(share);
            awarded += baseclass.Experience - before;
        }

        return awarded;
    }
}

/// <summary>
/// Who an event's effect reaches (<c>eventPartyAffectType</c>, <c>Shared/GameEvent.h:87</c>).
/// </summary>
/// <remarks>Ordinal values; the field is serialized as an int on the events that carry it.</remarks>
public enum PartyAffect
{
    None = 0,
    EntireParty = 1,
    ActiveCharacter = 2,
    OneAtRandom = 3,
    ChanceOnEach = 4,
}
