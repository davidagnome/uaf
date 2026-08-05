namespace UAFcore;

/// <summary>
/// One spell a combatant knows, and how many copies are ready to cast
/// (<c>CHARACTER_SPELL</c>, <c>Spell.h:60</c>).
/// </summary>
/// <param name="spellId">The spell in the design's table.</param>
/// <param name="level">The spell's level, carried here for convenience as the reference does.</param>
public sealed class SpellListEntry(string spellId, int level)
{
    /// <inheritdoc cref="SpellListEntry(string, int)"/>
    public string SpellId { get; } = spellId;

    /// <summary>The spell's level.</summary>
    public int Level { get; } = level;

    /// <summary>How many copies are memorised and ready.</summary>
    public int Memorized { get; set; }

    /// <summary>
    /// How many copies the caster wants memorised (<c>selected</c>, <c>Spell.h:76</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A count, not a flag — despite the comment beside it.</b> The field is declared
    /// <c>int selected; // TRUE if dude will memorize this spell again</c>, and every use reads it
    /// as a quantity: <c>HaveUnmemorized</c> is <c>selected &gt; memorized</c>,
    /// <c>SetMemorized(all)</c> assigns <c>memorized = selected</c>. Only <c>IsSelected</c> treats
    /// it as a flag, and it says <c>selected &gt; 0</c>. This port believed the comment and made
    /// it a <c>bool</c>, which cannot express "I want three of these" — the shape the whole
    /// memorisation clock is built on. The <i>reader</i> had it right all along
    /// (<c>CharacterSpell.Selected</c> is an int).
    /// </para>
    /// <para>
    /// <b>Zero gates spending as well as memorising.</b> <c>SetUnMemorized</c> returns without
    /// doing anything when <c>selected</c> is zero (<c>Spell.cpp:1252</c>), so a spell the caster
    /// has stopped wanting is cast without ever being used up. Odd, but it is what ships.
    /// </para>
    /// </remarks>
    public int Selected { get; set; } = 1;

    /// <summary>Minutes spent so far on the copy currently being memorised (<c>memTime</c>).</summary>
    public int MemorizeTime { get; set; }

    /// <summary>
    /// Whether a copy was memorised on the last tick, for the announcement
    /// (<c>JustMemorized</c>).
    /// </summary>
    /// <remarks>
    /// <b>Cleared by the reader, not by the writer.</b> The reference's announcement loop clears
    /// it as it prints, and <c>IncMemorizedTime</c> clears it again on entry — so a copy finished
    /// and never announced is forgotten on the next tick.
    /// </remarks>
    public bool JustMemorized { get; set; }

    /// <summary>Whether the caster wants more copies than are ready (<c>HaveUnmemorized</c>).</summary>
    public bool HasUnmemorized => Selected > 0 && Selected > Memorized;

    /// <summary>
    /// How long one copy of a spell this level takes to memorise
    /// (<c>GetSpellMemorizeTime</c>, <c>GameRules.cpp:4141</c>).
    /// </summary>
    /// <remarks>
    /// <b>Fifteen minutes a level, flat.</b> A first-level spell is a quarter of an hour and a
    /// ninth-level one is over two — and this is on top of the book's preparation block.
    /// </remarks>
    public static int MemorizeMinutes(int level) => level * 15;

    /// <summary>Whether enough time has been spent on the copy in progress.</summary>
    public bool MemorizeTimeSufficient => MemorizeTime >= MemorizeMinutes(Level);

    /// <summary>
    /// Adds time toward the copy in progress (<c>IncMemorizedTime</c>, <c>Spell.cpp:1189</c>).
    /// </summary>
    /// <param name="all">
    /// Memorise everything wanted at once, ignoring the clock — what a design's "memorize all"
    /// path uses.
    /// </param>
    /// <returns>Whether a copy was finished.</returns>
    public bool AddMemorizeTime(int minutes, bool all = false)
    {
        JustMemorized = false;

        if (Selected <= 0 || Selected <= Memorized)
        {
            return false;
        }

        if (!all)
        {
            MemorizeTime += minutes;
        }

        if (!all && !MemorizeTimeSufficient)
        {
            return false;
        }

        Memorize(all);
        return true;
    }

    /// <summary>
    /// Finishes a copy — or all of them (<c>SetMemorized</c>, <c>Spell.cpp:1231</c>).
    /// </summary>
    public void Memorize(bool all = false)
    {
        if (Selected <= 0 || Selected <= Memorized)
        {
            return;
        }

        Memorized = all ? Selected : Memorized + 1;
        JustMemorized = true;
        MemorizeTime = 0;
    }
}

/// <summary>
/// The spells a combatant carries into a fight (<c>SPELL_LIST</c> through <c>spellBookType</c>).
/// </summary>
/// <remarks>
/// Only the part combat needs: what is known, and what is ready. Learning, scribing and the
/// memorisation clock all live outside a fight and are not ported here.
/// </remarks>
public sealed class SpellList
{
    private readonly List<SpellListEntry> entries = [];

    public IReadOnlyList<SpellListEntry> Entries => entries;

    /// <summary>Adds a spell to the book, or returns the entry already there.</summary>
    public SpellListEntry Add(string spellId, int level, int memorized = 0)
    {
        if (Find(spellId) is { } existing)
        {
            existing.Memorized += memorized;
            return existing;
        }

        var entry = new SpellListEntry(spellId, level) { Memorized = memorized };
        entries.Add(entry);
        return entry;
    }

    /// <summary>The entry for a spell, or null when it is not in the book.</summary>
    public SpellListEntry? Find(string spellId) =>
        entries.FirstOrDefault(e => e.SpellId == spellId);

    /// <summary>
    /// The spells that can be cast right now — what the CAST menu lists
    /// (<c>FillCastSpellListText</c>, <c>Disptext.cpp:417</c>).
    /// </summary>
    public IEnumerable<SpellListEntry> Castable => entries.Where(e => e.Memorized > 0);

    /// <summary>
    /// Spends one memorised copy (<c>DecMemorized</c>, <c>Spell.cpp:1666</c>).
    /// </summary>
    /// <param name="count">
    /// <b>Tested against zero and then ignored.</b> The reference takes a count, returns false when
    /// it is zero, and then decrements by exactly one whatever it was — so asking for five spends
    /// one. Kept as a parameter because callers pass it and the zero test is real.
    /// </param>
    /// <returns>Whether the spell was in the book at all — <i>not</i> whether a copy was spent.</returns>
    /// <remarks>
    /// A spell with no memorised copies left, or one the caster has stopped
    /// <see cref="SpellListEntry.Selected">wanting</see>, still returns true here: the reference's <c>DecMemorized</c> reports only that it found the
    /// spell, and <c>SetUnMemorized</c> swallows both refusals silently.
    /// </remarks>
    /// <summary>
    /// Minutes of preparation the book still needs before any memorising starts
    /// (<c>spellPrepTimeNeeded</c>).
    /// </summary>
    public int PrepTimeNeeded { get; private set; }

    /// <summary>Minutes of preparation done (<c>spellPrepTimeUsed</c>).</summary>
    public int PrepTimeUsed { get; private set; }

    /// <summary>
    /// How long the book must be prepared before a single spell can be memorised
    /// (<c>CalcSpellPrepTime</c>, <c>Spell.cpp:2746</c>; <c>GetSpellPrepTime</c>,
    /// <c>GameRules.cpp:4147</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One block for the whole book, keyed on the highest level still wanted</b> — four hours
    /// for levels one and two, six for three and four, eight for five and six, ten for seven and
    /// eight, twelve above that. It dwarfs the per-spell time: preparing for a single third-level
    /// spell is six hours before the forty-five minutes of memorising it.
    /// </para>
    /// <para>
    /// <b>Level zero and below need none</b>, and <c>highlevel</c> starts at −1, so a book with
    /// nothing outstanding prepares for no time at all.
    /// </para>
    /// </remarks>
    public static int PrepMinutes(int highestLevel) => highestLevel switch
    {
        <= 0 => 0,
        1 or 2 => 4 * 60,
        3 or 4 => 6 * 60,
        5 or 6 => 8 * 60,
        7 or 8 => 10 * 60,
        _ => 12 * 60,
    };

    /// <summary>Recomputes the preparation this book needs and starts the clock over.</summary>
    public int BeginPreparing()
    {
        int highest = entries.Where(e => e.HasUnmemorized)
                             .Select(e => e.Level)
                             .DefaultIfEmpty(-1)
                             .Max();

        PrepTimeNeeded = PrepMinutes(highest);
        PrepTimeUsed = 0;

        return PrepTimeNeeded;
    }

    /// <summary>
    /// Gives the book a slice of time (<c>IncAllMemorizedTime</c>, <c>Spell.cpp:2958</c>).
    /// </summary>
    /// <returns>Whether a copy of some spell was finished.</returns>
    /// <remarks>
    /// <para>
    /// <b>Only one spell is memorised at a time.</b> The list is walked in order and the first
    /// entry still wanting copies takes the whole slice; everything after it waits. The
    /// reference says so in a comment and returns immediately after the first.
    /// </para>
    /// <para>
    /// <b>The preparation block comes first and is consumed whole.</b> Until <c>used</c> passes
    /// <c>needed</c> nothing is memorised at all, and when it does both counters are cleared for
    /// good — a book prepares once per rest, not once per spell.
    /// </para>
    /// <para>
    /// <b>The overshoot arithmetic looks swapped.</b> On the tick that finishes preparing,
    /// <c>delta</c> is how far past <c>needed</c> the clock went — the minutes that should count
    /// toward memorising — and the reference does <c>minuteInc -= delta</c>, keeping the other
    /// part instead. The resting path only ever passes one minute, where the two are equal, so
    /// nothing in the shipped engine can tell. Transcribed as written.
    /// </para>
    /// </remarks>
    public bool AddMemorizeTime(int minutes, bool all = false)
    {
        if (!all)
        {
            PrepTimeUsed += minutes;

            if (PrepTimeUsed <= PrepTimeNeeded)
            {
                return false;
            }

            if (PrepTimeNeeded > 0)
            {
                int delta = PrepTimeUsed - PrepTimeNeeded;
                if (minutes > delta)
                {
                    minutes -= delta;
                }
            }
        }

        PrepTimeNeeded = 0;
        PrepTimeUsed = 0;

        foreach (var entry in entries)
        {
            if (!entry.HasUnmemorized)
            {
                continue;
            }

            bool finished = entry.AddMemorizeTime(minutes, all);
            if (!all)
            {
                return finished;
            }
        }

        return false;
    }

    /// <summary>
    /// How long the whole book would take to fill (<c>CalcRestTime</c>, <c>Spell.cpp:2701</c>).
    /// </summary>
    /// <param name="restTime">
    /// Minutes one copy of a spell takes, by entry. The reference reads a per-spell rest time from
    /// the design rather than reusing <see cref="SpellListEntry.MemorizeMinutes"/>.
    /// </param>
    /// <remarks>
    /// <b>The shortfall is not guarded, so a surplus shortens the estimate.</b> The live loop sums
    /// <c>single * (selected - memorized)</c> over every entry with no test that the first exceeds
    /// the second — the commented-out version above it had exactly that guard. A spell with more
    /// copies memorised than wanted therefore contributes a <i>negative</i> number of minutes.
    /// </remarks>
    public int RestTimeNeeded(Func<SpellListEntry, int> restTime)
    {
        ArgumentNullException.ThrowIfNull(restTime);

        int total = entries.Sum(e => restTime(e) * (e.Selected - e.Memorized));
        return total + BeginPreparing();
    }

    public bool DecrementMemorized(string spellId, int count = 1)
    {
        if (count == 0)
        {
            return false;
        }

        if (Find(spellId) is not { } entry)
        {
            return false;
        }

        if (entry.Selected > 0 && entry.Memorized > 0)
        {
            entry.Memorized--;
        }

        return true;
    }
}
