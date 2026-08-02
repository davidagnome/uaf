namespace UAFcore;

/// <summary>
/// When a spell takes effect relative to the moment it was begun
/// (<c>spellCastingTimeType</c>, <c>GameRules.h:336</c>).
/// </summary>
/// <remarks>
/// The reference's own explanation, beside the arithmetic that uses it
/// (<c>Combatant.cpp:652</c>), is the clearest statement of the model in the codebase:
/// each round has ten initiatives, one round is one minute, ten rounds are one turn, a spell
/// needing whole rounds or turns lands at the <i>end</i> of one, and <b>any hit on the caster
/// during the casting time voids the spell</b>.
/// <para>
/// Out of combat none of this applies — spells activate immediately, because there is no clock to
/// wait on.
/// </para>
/// </remarks>
public enum SpellCastingTime
{
    /// <summary>Takes effect at once, in the caster's own turn.</summary>
    Immediate = 0,

    /// <summary>Casting time is added to the caster's initiative, within this round.</summary>
    Initiative = 1,

    /// <summary>Casting time is a number of rounds; lands at the end of the last one.</summary>
    Rounds = 2,

    /// <summary>Casting time is a number of turns, each ten rounds.</summary>
    Turns = 3,
}

/// <summary>
/// A spell begun but not yet resolved (<c>PENDING_SPELL</c>, <c>Spell.h:1034</c>).
/// </summary>
/// <param name="Key">Identifies this entry so the caster can withdraw it.</param>
/// <param name="Caster">Index of the combatant casting.</param>
/// <param name="SpellId">The spell from the caster's book.</param>
/// <param name="WaitUntil">
/// The clock reading at which it activates. Which clock depends on <paramref name="Timing"/>:
/// an initiative within the round, or a round number.
/// </param>
/// <param name="Timing">
/// How to read <paramref name="WaitUntil"/>. <b>Not necessarily the spell's own casting-time
/// type</b> — scheduling rewrites it when a spell would otherwise land outside the round it was
/// begun in, or when the arithmetic makes it immediate after all.
/// </param>
public readonly record struct PendingSpell(
    int Key, int Caster, string SpellId, int WaitUntil, SpellCastingTime Timing);

/// <summary>
/// The spells in flight (<c>PENDING_SPELL_LIST</c>, <c>Spell.h:1463</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is saved.</b> The reference comments its serializer out with "no need to save
/// pending spells" — a fight cannot be saved mid-round, so the list is always empty at a save
/// point. Combat start and end both clear it (<c>Dgngame.cpp:561</c>).
/// </para>
/// <para>
/// The list is keyed rather than indexed because a caster holds onto its key
/// (<c>combatant_pendingSpellKey</c>) to withdraw its own entry when interrupted, and entries
/// around it come and go.
/// </para>
/// </remarks>
public sealed class PendingSpellList
{
    private readonly List<PendingSpell> spells = [];
    private int nextKey;

    /// <summary>How many spells are in flight.</summary>
    public int Count => spells.Count;

    /// <summary>The spells in flight, in the order they were begun.</summary>
    public IReadOnlyList<PendingSpell> Spells => spells;

    /// <summary>Empties the list, as combat start and end both do.</summary>
    public void Clear()
    {
        spells.Clear();
        nextKey = 0;
    }

    /// <summary>
    /// Works out when a spell lands (the casting-time switch in <c>COMBATANT::CastSpell</c>,
    /// <c>Combatant.cpp:676</c>).
    /// </summary>
    /// <param name="castingTime">The spell's <c>Casting_Time</c>, clamped at zero as the reference does.</param>
    /// <param name="type">The spell's <c>Casting_Time_Type</c>.</param>
    /// <param name="initiative">The caster's initiative this round.</param>
    /// <param name="round">The round now being fought.</param>
    /// <returns>When it lands, and how to read that number.</returns>
    /// <remarks>
    /// <para>
    /// <b>A spell can never wait past the round it was begun in.</b> An initiative-timed spell
    /// whose casting time pushes it beyond the last initiative slot is not deferred to a later
    /// round — it is re-timed to land at the <i>end of this one</i>. The reference's comment at
    /// that branch says so outright: "we certainly don't want to wait many rounds". The commented
    /// line above it, <c>waitUntil += (rnd+1)</c>, is what it used to do.
    /// </para>
    /// <para>
    /// <b>Zero casting time collapses to immediate whatever the type says.</b> All three timed
    /// branches test for the no-wait case and rewrite themselves to
    /// <see cref="SpellCastingTime.Immediate"/>, so a spell declared as taking rounds but given a
    /// casting time of zero resolves in the caster's own turn.
    /// </para>
    /// <para>
    /// <b>The initiative branch's two tests are not exclusive, and that is reproduced.</b> The
    /// reference writes a bare <c>if</c> where the shape of the code wants <c>else if</c>: after an
    /// overlong spell has been re-timed to <c>waitUntil = round</c>, the second test asks whether
    /// <c>waitUntil == initiative</c> — now comparing a round number against an initiative. When
    /// they happen to be equal (round 5, a caster on initiative 5, casting time 19 or more) the
    /// spell that was just deferred to the end of the round is marked immediate instead. It looks
    /// like an accident rather than a rule, but it is what ships and it is what a design was tuned
    /// against, so it is kept.
    /// </para>
    /// </remarks>
    public static (int WaitUntil, SpellCastingTime Timing) Schedule(
        int castingTime, SpellCastingTime type, int initiative, int round)
    {
        castingTime = Math.Max(0, castingTime);

        switch (type)
        {
            case SpellCastingTime.Immediate:
                return (initiative, SpellCastingTime.Immediate);

            case SpellCastingTime.Initiative:
            {
                int wait = initiative + castingTime;
                var timing = SpellCastingTime.Initiative;

                if (wait > CombatRound.NeverInitiative)
                {
                    // Past the end of the initiative order: land at the end of this round instead.
                    timing = SpellCastingTime.Rounds;
                    wait = round;
                }

                // Deliberately not an `else if` — see the remarks. The reference isn't either.
                if (wait == initiative)
                {
                    timing = SpellCastingTime.Immediate;
                }

                return (wait, timing);
            }

            case SpellCastingTime.Rounds:
            {
                int wait = round + castingTime;
                return wait == round
                    ? (initiative, SpellCastingTime.Immediate)
                    : (wait, SpellCastingTime.Rounds);
            }

            case SpellCastingTime.Turns:
            {
                int wait = round + (castingTime * RoundsPerTurn);
                return wait == round
                    ? (initiative, SpellCastingTime.Immediate)
                    : (wait, SpellCastingTime.Turns);
            }

            default:
                // The reference dies here (0xab500) and then carries on as immediate anyway.
                return (initiative, SpellCastingTime.Immediate);
        }
    }

    /// <summary>Ten rounds to a turn, one round to the minute (<c>Combatant.cpp:659</c>).</summary>
    public const int RoundsPerTurn = 10;

    /// <summary>
    /// Begins a spell: schedules it, and queues it unless it resolves at once
    /// (the tail of <c>COMBATANT::CastSpell</c>, <c>Combatant.cpp:722</c>).
    /// </summary>
    /// <returns>
    /// The key to withdraw it by, or <c>-1</c> when the spell is immediate and was not queued.
    /// </returns>
    /// <remarks>
    /// <b>Immediate spells never enter the list.</b> The reference guards the <c>Add</c> with
    /// <c>Casting_Time_Type != sctImmediate</c> and sets the caster's pending key to <c>-1</c>
    /// otherwise, so an immediate spell is resolved by the caster's own turn rather than by the
    /// clock. It tests the <i>spell's declared</i> type, not the rewritten timing — so a
    /// rounds-typed spell with a casting time of zero is still queued, and is then activated by the
    /// very next service call because its rewritten timing came out immediate. One extra hop
    /// through the list, same round.
    /// </remarks>
    public int Begin(int caster, string spellId, int castingTime, SpellCastingTime type,
                     int initiative, int round)
    {
        var (waitUntil, timing) = Schedule(castingTime, type, initiative, round);

        if (type == SpellCastingTime.Immediate)
        {
            return -1;
        }

        int key = nextKey++;
        spells.Add(new PendingSpell(key, caster, spellId, waitUntil, timing));
        return key;
    }

    /// <summary>Withdraws a queued spell, as an interrupted caster does.</summary>
    public bool Remove(int key) => spells.RemoveAll(s => s.Key == key) > 0;

    /// <summary>Withdraws everything a combatant has in flight.</summary>
    public bool RemoveFor(int caster) => spells.RemoveAll(s => s.Caster == caster) > 0;

    /// <summary>
    /// Activates whatever is now due (<c>ProcessTimeSensitiveData</c>, <c>Spell.cpp:7713</c>).
    /// </summary>
    /// <param name="roundInc">
    /// How many rounds have passed since the last service. Nonzero forces initiative-timed spells
    /// through — a spell waiting on an initiative that the round never reached must still land.
    /// </param>
    /// <param name="currentInitiative">How far the initiative walk has got.</param>
    /// <param name="currentRound">The round now being fought.</param>
    /// <param name="activate">Called for each spell coming due, before it leaves the list.</param>
    /// <returns>Whether anything activated.</returns>
    /// <remarks>
    /// <para>
    /// <b>The reference's return value is not "did anything activate".</b> Its <c>castIt</c> is
    /// declared outside the loop and never reset, so what it returns is whatever the <i>last</i>
    /// entry examined decided — and any entry that activates leaves the flag set for every entry
    /// after it, activating those too regardless of their own timing. The caller
    /// (<c>Combatants.cpp:1641</c>) uses the result only to decide whether to look at the turn
    /// queue, which is harmless, but the leaked flag is not. This port resets per entry and
    /// returns a true "anything activated", which is what every caller means.
    /// </para>
    /// <para>
    /// Item-cast spells skip the clock entirely and fire on the first service. Handled by the
    /// caller here, since this port has no item-spell path yet.
    /// </para>
    /// </remarks>
    public bool Service(int roundInc, int currentInitiative, int currentRound,
                        Action<PendingSpell> activate)
    {
        ArgumentNullException.ThrowIfNull(activate);

        // Forward order, as the reference walks it: a spell begun earlier lands earlier, which
        // matters once one spell's effect can change what the next one does.
        var due = spells.Where(s => s.Timing switch
        {
            SpellCastingTime.Immediate => true,
            SpellCastingTime.Initiative => currentInitiative >= s.WaitUntil || roundInc > 0,
            SpellCastingTime.Rounds or SpellCastingTime.Turns => s.WaitUntil <= currentRound,
            _ => false,
        }).ToList();

        foreach (var spell in due)
        {
            spells.Remove(spell);
            activate(spell);
        }

        return due.Count > 0;
    }
}
