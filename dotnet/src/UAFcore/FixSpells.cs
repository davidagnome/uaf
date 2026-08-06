namespace UAFcore;

/// <summary>Which service is doing the fixing (<c>PARTY::FixParty</c>'s <c>environment</c>).</summary>
public enum FixEnvironment
{
    /// <summary>Camp's FIX: the party heals itself out of its own memorised spells.</summary>
    Encamp = 0,

    /// <summary>The temple's FIX: a synthesised bishop casts, and the party spends nothing.</summary>
    Temple = 1,
}

/// <summary>One cast the fix loop decided to make.</summary>
public sealed record FixCast(string SpellId, Character Caster, Character Target);

/// <summary>
/// FIX — healing the party out of a spell book, from camp or from a temple
/// (<c>PARTY::FixParty</c>, <c>Party.cpp:3961</c>, over <c>FIX_SPELL_LIST</c> and
/// <c>FIX_SPELL_ENTRY</c>, <c>:3818</c> and <c>:3681</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>One routine, two environments, and they differ only in who casts.</b> Camp
/// (<c>FixParty(0)</c>, <c>RunEvent.cpp:9296</c>) draws on the party's own memorised spells and
/// spends them. The temple (<c>FixParty(1)</c>, <c>:12878</c>) casts from a synthesised bishop, so
/// the party's own book is untouched and nothing limits how much it can be healed. Everything else
/// — the spell book, the target search, the loop — is shared.
/// </para>
/// <para>
/// <b>The book is the design's global fix spell book</b>, not any character's. A design chooses
/// what FIX is allowed to cast by putting spells in it, and a design that leaves it empty makes
/// both FIX entries do nothing at all.
/// </para>
/// <para>
/// <b>A successful cast does not consume the entry.</b> The loop keeps returning the same spell
/// for as long as it can find a caster and a willing target, so one cure spell heals the whole
/// party one cast at a time. What ends the loop is a spell running out of casters or of willing
/// targets — so <b>the casting itself is the termination condition</b>: healing raises hit points
/// until <see cref="WantsFixing"/> stops saying yes, and in camp each cast also spends a memorised
/// copy until no one has one left. A <paramref name="cast"/> that does neither never terminates.
/// That is the reference's own structure and it is transcribed rather than bounded.
/// </para>
/// <para>
/// <b>The pools are per-spell and are never rebuilt.</b> Each spell keeps its own list of
/// candidate casters and candidate targets, and a candidate rejected once is dropped from that
/// spell's list for the rest of the visit. So a character who was at full health when a cure spell
/// first looked at them cannot be healed by that spell later in the same FIX, however much damage
/// they take from another one meanwhile.
/// </para>
/// </remarks>
public static class FixSpells
{
    /// <summary>One spell in the fix book, with the candidates it has left.</summary>
    private sealed class Entry(string spellId)
    {
        public string SpellId { get; } = spellId;

        /// <summary>Null until first needed — the reference builds these lazily too.</summary>
        public List<Character>? Casters { get; set; }

        public List<Character>? Targets { get; set; }
    }

    /// <summary>
    /// Runs FIX to exhaustion.
    /// </summary>
    /// <param name="fixSpellBook">The design's global fix spell book, in its own order.</param>
    /// <param name="party">Everyone present. Every member is a candidate target, healthy or not.</param>
    /// <param name="random">
    /// Picks an index below the count (<c>randomMT() % n</c>). Injected because every choice this
    /// makes is random and a test that cannot fix them cannot assert an order.
    /// </param>
    /// <param name="wantsFixing">
    /// Whether this character wants this spell. <see cref="WantsFixing"/> is the engine's own
    /// answer and the right thing to pass; the parameter exists because a design's
    /// <c>FIX_CHARACTER</c> script can override it, and the scripting layer is not ported.
    /// </param>
    /// <param name="cast">
    /// Invokes the spell (<c>CHARACTER::CastSpell</c>). <b>Injected for the same reason</b> — the
    /// spell resolution layer is not ported. <b>It must consume the caster's memorised copy in the
    /// <see cref="FixEnvironment.Encamp"/> case</b>, because that is what eventually leaves a
    /// spell with no caster and ends the loop.
    /// </param>
    /// <param name="bishop">
    /// Supplies the temple's caster (<c>Party.cpp:3898</c>), called at most once and only when a
    /// spell actually needs it — so a design with an empty fix book never builds one. The same
    /// synthesised max-level Cleric/Magic User the temple's cast list uses; see
    /// <see cref="TempleSpells"/>. The reference adds it to the design's NPC list and
    /// <b>removes it again</b> when FIX finishes, so it never shows up anywhere a player can see.
    /// Unused in the <see cref="FixEnvironment.Encamp"/> case.
    /// </param>
    /// <returns>The casts made, in the order they were made.</returns>
    public static List<FixCast> Run(
        IReadOnlyList<string> fixSpellBook,
        IReadOnlyList<Character> party,
        FixEnvironment environment,
        Func<int, int> random,
        Func<string, Character, FixEnvironment, bool> wantsFixing,
        Action<Character, string, Character> cast,
        Func<Character?>? bishop = null)
    {
        ArgumentNullException.ThrowIfNull(fixSpellBook);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(wantsFixing);
        ArgumentNullException.ThrowIfNull(cast);

        var spells = new List<Entry>(fixSpellBook.Count);
        foreach (string spellId in fixSpellBook)
        {
            spells.Add(new Entry(spellId));
        }

        var made = new List<FixCast>();
        Character? templeCaster = null;
        bool bishopAsked = false;

        while (spells.Count > 0)
        {
            int index = random(spells.Count);
            var entry = spells[index];

            Character? caster;
            if (environment == FixEnvironment.Encamp)
            {
                caster = RandomCaster(entry, party, random);
            }
            else
            {
                // Built on demand and then kept for the rest of the visit.
                if (!bishopAsked)
                {
                    bishopAsked = true;
                    templeCaster = bishop?.Invoke();
                }

                caster = templeCaster;
            }

            if (caster is not null)
            {
                var target = RandomTarget(entry, party, environment, random, wantsFixing);
                if (target is not null)
                {
                    made.Add(new FixCast(entry.SpellId, caster, target));
                    cast(caster, entry.SpellId, target);
                    continue;                       // the entry stays; it can be cast again
                }
            }

            // No caster or no willing target: this spell is finished. Swapped to the end and
            // dropped, exactly as m_fixSpells[i].Swap(&m_fixSpells[--m_numFixSpells]) does.
            SwapOut(spells, index);
        }

        return made;
    }

    /// <summary>
    /// Picks a caster who has this spell memorised (<c>FIX_SPELL_ENTRY::RandomCaster</c>,
    /// <c>Party.cpp:3719</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The candidate pool is everyone who can cast at all, built once.</b> A member who turns
    /// out not to have this spell memorised is dropped from this spell's pool permanently — so a
    /// caster who memorises it later in the same visit is not reconsidered.
    /// </para>
    /// <para>
    /// <b>A caster who does have it is returned without being removed</b>, so the same character
    /// keeps being chosen until their copies run out. That is what makes casting consume, rather
    /// than the loop, the thing that ends it.
    /// </para>
    /// </remarks>
    private static Character? RandomCaster(Entry entry, IReadOnlyList<Character> party,
                                           Func<int, int> random)
    {
        entry.Casters ??= [.. party.Where(c => SpellPermissions.CanCast(c))];

        while (entry.Casters.Count > 0)
        {
            int i = random(entry.Casters.Count);
            var who = entry.Casters[i];

            if (who.Book.Find(entry.SpellId) is { Memorized: > 0 })
            {
                return who;
            }

            SwapOut(entry.Casters, i);
        }

        return null;
    }

    /// <summary>
    /// Picks someone who wants this spell (<c>FIX_SPELL_ENTRY::RandomTarget</c>,
    /// <c>Party.cpp:3771</c>).
    /// </summary>
    /// <remarks>
    /// <b>Every party member is a candidate, with no filter of any kind</b> — not status, not hit
    /// points, not whether they are even alive. The script is the whole test, and the engine only
    /// tells it whether the character is below their maximum hit points and which service asked.
    /// </remarks>
    private static Character? RandomTarget(Entry entry, IReadOnlyList<Character> party,
                                           FixEnvironment environment, Func<int, int> random,
                                           Func<string, Character, FixEnvironment, bool> wantsFixing)
    {
        entry.Targets ??= [.. party];

        while (entry.Targets.Count > 0)
        {
            int i = random(entry.Targets.Count);
            var who = entry.Targets[i];

            if (wantsFixing(entry.SpellId, who, environment))
            {
                return who;
            }

            SwapOut(entry.Targets, i);
        }

        return null;
    }

    /// <summary>
    /// Drops an element by moving the last one over it (<c>m_x[i] = m_x[--m_num]</c>).
    /// </summary>
    /// <remarks>
    /// <b>Order is not preserved, and that is load-bearing for nothing</b> — every pick is random,
    /// so the shuffle the swap causes is invisible. It is transcribed this way because the
    /// alternative, removing in place, changes which index a subsequent random pick lands on and
    /// would make a seeded test disagree with the reference for no reason.
    /// </remarks>
    private static void SwapOut<T>(List<T> items, int index)
    {
        items[index] = items[^1];
        items.RemoveAt(items.Count - 1);
    }

    /// <summary>
    /// Whether a character wants fixing, when no script says otherwise (<c>Party.cpp:3787</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the engine's answer, not a stand-in for one.</b> The reference pre-loads
    /// <c>hookParameters[0]</c> with <c>"1"</c> or <c>""</c> and then runs the spell's
    /// <c>FIX_CHARACTER</c> scripts — and <c>SPECIAL_ABILITIES::RunScripts</c> with no matching
    /// script calls the callback with <c>CBF_DEFAULT</c> and returns <c>hookParameters[0]</c>
    /// unchanged (<c>Specab.cpp:1955</c>), while <c>ScriptCallback_RunAllScripts</c> never touches
    /// the result at all (<c>:1678</c>). So in a design with no such script the answer <i>is</i>
    /// the pre-loaded value, and the test <c>!ans.IsEmpty() &amp;&amp; ans != "0"</c> comes out as
    /// exactly "below their maximum hit points".
    /// </para>
    /// <para>
    /// <b>A script overrides it rather than adding to it</b>, and where several match, each
    /// overwrites <c>hookParameters[0]</c> in turn, so the <i>last</i> one wins.
    /// </para>
    /// <para>
    /// <b>The pre-loaded value is <c>""</c>, not <c>"0"</c>.</b> Both are refusals here, because
    /// the test rejects the empty string and the literal zero separately — but a script that
    /// echoes what it was handed passes one of them straight back, and only one of the two is what
    /// the engine wrote.
    /// </para>
    /// <para>
    /// <b>Status is not consulted.</b> Not dead, not unconscious, not petrified — only hit points.
    /// A dead character below their maximum is a candidate, and a character at full health who is
    /// petrified is not.
    /// </para>
    /// </remarks>
    public static bool WantsFixing(Character who)
    {
        ArgumentNullException.ThrowIfNull(who);
        return who.HitPoints < who.MaxHitPoints;
    }

    /// <summary>
    /// What the reference hands the target script as <c>hookParameters[5]</c>
    /// (<c>Party.cpp:3786</c>).
    /// </summary>
    /// <remarks>
    /// Nothing in the engine reads it back — it is there for a design's script to branch on, so
    /// that one spell can behave differently in a camp and in a temple.
    /// </remarks>
    public static string Where(FixEnvironment environment) =>
        environment == FixEnvironment.Encamp ? "ENCAMP" : "TEMPLE";
}
