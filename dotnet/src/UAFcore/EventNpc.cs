using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>Why an <c>ADD_NPC_DATA</c> did or did not seat its NPC.</summary>
/// <remarks>
/// The reference has no such value — <c>PARTY::addNPCToParty</c> returns a bare <c>BOOL</c> and the
/// caller re-asks <c>characterID.IsValidNPC()</c> to tell the two failures apart
/// (<c>RunEvent.cpp:10839</c>). This names them instead, and there is no member for the reference's
/// third exit because it cannot be reached — see <see cref="EventNpc.Add"/>.
/// </remarks>
public enum AddNpcResult
{
    /// <summary>The NPC joined.</summary>
    Joined,

    /// <summary>
    /// The design's character list has no NPC under that id, so nothing happened and the player is
    /// told nothing.
    /// </summary>
    NoSuchNpc,

    /// <summary>
    /// The roster was already at the design's cap. This is the one failure the reference reports,
    /// as <c>miscError = NPCPartyLimitReached</c>.
    /// </summary>
    PartyFull,
}

/// <summary>What an <c>ADD_NPC_DATA</c> did.</summary>
/// <param name="Result">Which of the three exits was taken.</param>
/// <param name="Joined">The new party member, or null when none was seated.</param>
/// <param name="MoraleModifier">
/// The charisma-derived morale adjustment that was applied, or 0 when nothing was seated. Reported
/// because it is drawn from the <i>existing</i> roster and is otherwise invisible.
/// </param>
public readonly record struct AddNpcOutcome(AddNpcResult Result, Character? Joined,
                                            int MoraleModifier);

/// <summary>What a <c>REMOVE_NPC_DATA</c> did.</summary>
/// <param name="Removed">Whether a matching NPC was found.</param>
/// <param name="Index">The roster slot it was removed from, or −1.</param>
/// <param name="Member">The member that left, or null.</param>
public readonly record struct RemoveNpcOutcome(bool Removed, int Index, Character? Member);

/// <summary>
/// Runs the NPC pair — <c>ADD_NPC_DATA::OnKeypress</c> (<c>UAFWin/RunEvent.cpp:10821</c>) and
/// <c>REMOVE_NPC_DATA::OnKeypress</c> (<c>:10870</c>) — whose whole effect is one call each into
/// <c>PARTY::addNPCToParty</c> (<c>Shared/Party.cpp:2552</c>) and
/// <c>PARTY::removeNPCFromParty</c> (<c>Shared/Party.cpp:2644</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Four events in the whole corpus</b> — three adds and one remove. Two of the adds are
/// SomethingWild seating <c>Uril Kabo</c> and <c>Meuronna</c>, the remove is the same design
/// dismissing <c>Meuronna</c>, and the third add is Ambassador's Letter event 1105, whose
/// <c>characterID</c> is the <b>empty string</b>. That last one can never seat anybody and never
/// says so — see <see cref="AddNpcResult.NoSuchNpc"/> — and it is the reason the empty id is
/// treated as data here rather than as a caller error.
/// </para>
/// <para>
/// All four carry <c>distance = FarAway</c>, and all three adds carry
/// <c>hitPointMod = 100, useOriginal = 1</c>. So every branch below except the plain one is
/// transcription from the source rather than anything the corpus exercises.
/// </para>
/// <para>
/// <b><see cref="AddNpcEvent.Operation"/> is not an operation.</b> The field is
/// <c>eventDistType distance</c> (<c>GameEvent.h:1370</c>), the same field
/// <see cref="RemoveNpcEvent.Distance"/> names honestly, and it is read once by
/// <c>OnInitialEvent</c> to pick a sprite frame (<c>RunEvent.cpp:10813</c>). Nothing in either
/// event's effect looks at it, so nothing here does either.
/// </para>
/// <para>
/// <b>Neither event rolls anything</b>, so there is no dice function to inject. The only number
/// either derives is <see cref="MoraleModifier"/>, and that is a table lookup.
/// </para>
/// <para>
/// <b>Both chain unconditionally.</b> <c>ChainHappened()</c> is the last line of both
/// <c>OnKeypress</c> bodies (<c>RunEvent.cpp:10843</c>, <c>:10884</c>) and sits outside every
/// failure branch, so a party that was too full to take the NPC follows exactly the same chain as
/// one that took them. There is no not-happen path.
/// </para>
/// <para>
/// <b>Neither event is wired into this port's runner yet.</b> <c>EventRunner.Begin</c> has no case
/// for either type, so both currently land in its unsupported branch; what is here is the effect,
/// ready for that wiring.
/// </para>
/// </remarks>
public static class EventNpc
{
    /// <summary><c>NPC_TYPE</c> (<c>Shared/Externs.h:966</c>).</summary>
    public const byte NpcType = (byte)CombatantKind.Npc;

    /// <summary>
    /// <c>IN_PARTY_TYPE</c> (<c>Shared/Externs.h:969</c>) — the high bit <c>CHARACTER::GetType</c>
    /// masks off.
    /// </summary>
    /// <remarks>
    /// <c>type</c> is one byte holding a kind in its low bits and a membership flag in its top one
    /// (<c>Char.h:985</c>), so a raw comparison against <see cref="NpcType"/> misses any record
    /// saved while its subject was in a party.
    /// </remarks>
    public const byte InPartyFlag = 128;

    /// <summary><c>CHARACTER::GetType</c> (<c>Shared/Char.h:985</c>) — the kind, without the flag.</summary>
    public static byte KindOf(CharacterRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return (byte)(record.Type & ~InPartyFlag);
    }

    /// <inheritdoc cref="KindOf(CharacterRecord)"/>
    public static byte KindOf(Character member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return KindOf(member.Record);
    }

    /// <summary>
    /// How many members the party may hold (<c>GetMaxPartyMembers</c>,
    /// <c>Shared/Globals.cpp:4968</c>).
    /// </summary>
    /// <param name="maxPartyMaxPcs">
    /// <see cref="GlobalStatsPrefix.MaxPartyMaxPcs"/> as the design carries it.
    /// </param>
    /// <remarks>
    /// <b>Two numbers packed into one int, and the one that matters here is the top half.</b>
    /// <c>maxParty_maxPCs</c> holds the party size in its upper 16 bits and the player-character
    /// count in its lower (<c>GlobalData.h:854</c>), and only the former bounds the roster. The
    /// corpus makes the distinction vivid: SomethingWild stores <c>0x00080006</c> — eight seats, six
    /// of them for player characters, leaving room for exactly the two NPCs it adds — while
    /// Ambassador's Letter stores <c>0x00020001</c>, a two-seat party. Reading the field whole gives
    /// 524294, which after the <c>min</c> is just <see cref="Party.MaxMembers"/> and would let any
    /// design seat twelve.
    /// </remarks>
    public static int MaxPartyMembers(int maxPartyMaxPcs) =>
        Math.Min(Party.MaxMembers, maxPartyMaxPcs >> 16);

    /// <summary>
    /// Whether the party has room for one more (<c>PARTY::CanAddNPC</c>,
    /// <c>Shared/Party.cpp:4851</c>).
    /// </summary>
    /// <remarks>
    /// Counts <i>everybody</i>, not just NPCs — the reference compares <c>numCharacters</c>, so
    /// player characters occupy the same seats.
    /// </remarks>
    public static bool CanAddNpc(Party party, int maxPartyMembers)
    {
        ArgumentNullException.ThrowIfNull(party);

        return party.Count < maxPartyMembers;
    }

    /// <summary>
    /// Whether the design's character list holds an NPC under this id
    /// (<c>CHAR_LIST::HaveNPC</c>, <c>Shared/Char.cpp:9776</c>).
    /// </summary>
    /// <param name="findCharacter">
    /// <c>CHAR_LIST::LocateCharacter</c> (<c>Char.cpp:9482</c>) — a linear scan of the design's
    /// characters returning the one whose <c>characterID</c> matches, or null.
    /// </param>
    /// <remarks>
    /// <b>Two conditions, and the second is the interesting one.</b> The record must exist
    /// <i>and</i> its kind must be <see cref="NpcType"/>; a <c>CHAR_TYPE</c> record under the same
    /// id is not addable. Every character in every corpus design is stored as an NPC, so the gate
    /// never bites there — but it is what stops a design's player-character templates being seated
    /// by an event.
    /// <para>
    /// <b>The reference's lookup is case-sensitive and this one is the caller's.</b>
    /// <c>CHARACTER_ID</c> derives from <c>CString</c> (<c>Externs.h:1409</c>) and inherits its
    /// <c>operator==</c>, which is <c>strcmp</c>. A resolver that folds case — as this port's other
    /// id lookups do — is therefore looser than the reference. The roster side below does the
    /// comparison itself and keeps the reference's rule.
    /// </para>
    /// </remarks>
    public static bool HaveNpc(string characterId, Func<string, CharacterRecord?> findCharacter)
    {
        ArgumentNullException.ThrowIfNull(findCharacter);

        return findCharacter(characterId ?? string.Empty) is { } record && KindOf(record) == NpcType;
    }

    /// <summary>
    /// Whether an NPC with this id is already in the party (<c>PARTY::isNPCinParty</c>,
    /// <c>Shared/Party.cpp:2674</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not the same question as <see cref="Party.HasCharacter"/>.</b> That one is the
    /// <c>PartyHasNPC</c> trigger condition's spelling in this port and it drops the
    /// <see cref="NpcType"/> gate and folds case; the reference's <c>PartyHasNPC</c> is literally
    /// <c>isNPCinParty</c> (<c>Party.cpp:1273</c>), so the two ought to agree and do not. This is
    /// the faithful one.
    /// </para>
    /// <para>
    /// <b>An empty id is a real query, not a no-op</b>, and it matches any NPC-kinded member whose
    /// own id is empty. In the reference no such member exists — a player character is
    /// <c>CHAR_TYPE</c>, and a joined NPC was found by a non-empty id — so the empty case is inert
    /// there. It is not inert here; see the remarks on <see cref="Remove"/>.
    /// </para>
    /// </remarks>
    public static bool IsNpcInParty(Party party, string characterId)
    {
        ArgumentNullException.ThrowIfNull(party);

        return IndexOfNpc(party, characterId) >= 0;
    }

    /// <summary>The first roster slot holding an NPC with this id, or −1.</summary>
    private static int IndexOfNpc(Party party, string characterId)
    {
        for (int i = 0; i < party.Count; i++)
        {
            var member = party.Members[i];

            // CString::operator== is strcmp, so this comparison is case-sensitive.
            if (KindOf(member) == NpcType
                && string.Equals(member.CharacterId, characterId ?? string.Empty,
                                 StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The extra gate an add event carries (<c>ADD_NPC_DATA::EventShouldTrigger</c>,
    /// <c>Shared/GameEvent.cpp:10800</c>), on top of <see cref="EventTrigger"/>.
    /// </summary>
    /// <remarks>
    /// <b>The party-full test is asked twice and answered differently.</b> An event that fails it
    /// here never runs at all, so the player sees nothing; one that passes it here and fails it
    /// again in <see cref="Add"/> — because the roster filled up between the two, which nothing in
    /// a single event can do — would draw its text first. In practice this makes the
    /// <see cref="AddNpcResult.PartyFull"/> outcome unreachable through the normal trigger path,
    /// which is worth knowing before assuming its error message is ever shown.
    /// </remarks>
    public static bool ShouldTriggerAdd(AddNpcEvent add, Party party, int maxPartyMembers)
    {
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(party);

        return !IsNpcInParty(party, add.CharacterId) && CanAddNpc(party, maxPartyMembers);
    }

    /// <summary>
    /// The extra gate a remove event carries (<c>REMOVE_NPC_DATA::EventShouldTrigger</c>,
    /// <c>Shared/GameEvent.cpp:10815</c>).
    /// </summary>
    /// <remarks>
    /// The mirror of the first half of <see cref="ShouldTriggerAdd"/>, and it means
    /// <see cref="Remove"/>'s "no matching NPC" path is likewise unreachable through the trigger.
    /// </remarks>
    public static bool ShouldTriggerRemove(RemoveNpcEvent remove, Party party)
    {
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(party);

        return IsNpcInParty(party, remove.CharacterId);
    }

    /// <summary>
    /// The morale adjustment the party's best charisma buys a joining NPC
    /// (<c>Shared/Party.cpp:2574</c>).
    /// </summary>
    /// <remarks>
    /// <b>A hand-written table with a hole in the middle and nothing above 18.</b> 3–8 are
    /// penalties from −30 to −5, 14–18 are bonuses from +5 to +40, and 9–13 fall through the
    /// <c>default</c> to nothing — which is the intent. What is almost certainly not the intent is
    /// that the switch is on discrete values rather than ranges, so a charisma of <b>19 or more
    /// scores zero</b>, the same as an average one; a design granting an exceptional score through
    /// a spell effect gets no bonus at all. Below 3 is likewise zero.
    /// </remarks>
    public static int MoraleModifier(int highestCharisma) => highestCharisma switch
    {
        3 => -30,
        4 => -25,
        5 => -20,
        6 => -15,
        7 => -10,
        8 => -5,
        14 => 5,
        15 => 15,
        16 => 20,
        17 => 30,
        18 => 40,
        _ => 0,                                          // 9..13, and everything off the table
    };

    /// <summary>
    /// The best adjusted charisma in the party, as the morale table is indexed by
    /// (<c>Shared/Party.cpp:2568</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The score is put through a <c>BYTE</c> on the way, and that is a live bug.</b> The
    /// reference writes <c>BYTE pc_cha = characters[i].GetAdjCha();</c> where <c>GetAdjCha</c>
    /// returns an unclamped <c>int</c> (<c>Char.cpp:13788</c>) — it is <c>GetLimitedCha</c> that
    /// bounds it, and this does not call it. So a drain effect taking a character to −1 charisma
    /// makes <c>pc_cha</c> 255, the highest possible, and hands the joining NPC the
    /// <c>default</c> of nothing rather than the −30 the raw score asks for; a buff to 256 reads as
    /// 0. Reproduced, because the truncation is what decides which table row is used.
    /// </para>
    /// <para>
    /// <b>The joining NPC's own charisma is not counted.</b> The loop runs before the new member is
    /// placed, so an empty party contributes a best of 0 and the modifier is 0.
    /// </para>
    /// </remarks>
    public static int HighestCharisma(Party party)
    {
        ArgumentNullException.ThrowIfNull(party);

        int highest = 0;
        foreach (var member in party.Members)
        {
            byte score = unchecked((byte)EventWhoTries.Adjusted(member, Ability.Charisma));
            if (score > highest)
            {
                highest = score;
            }
        }

        return highest;
    }

    /// <summary>
    /// Seats the NPC (<c>PARTY::addNPCToParty</c>, <c>Shared/Party.cpp:2552</c>).
    /// </summary>
    /// <param name="findCharacter">
    /// The design's character lookup — <c>CHAR_LIST::LocateCharacter</c>. Called twice on the
    /// success path, exactly as the reference calls <c>HaveNPC</c> and then
    /// <c>FetchCharacter</c>.
    /// </param>
    /// <param name="money">
    /// The design's currency, needed to build the member's purse. The reference copies the record
    /// wholesale and has no such parameter.
    /// </param>
    /// <param name="maxPartyMembers">
    /// From <see cref="MaxPartyMembers(int)"/>. The reference reads a global.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The reference's third exit is dead.</b> After <c>HaveNPC</c> passes and
    /// <c>CanAddNPC</c> passes, it calls <c>FetchCharacter</c> and has an <c>else</c> branch
    /// logging "Failed GetCharacterData" (<c>Party.cpp:2625</c>). Both functions resolve the id
    /// through the same <c>LocateCharacter</c> and <c>HaveNPC</c> is the stricter of the two, so
    /// the fetch cannot fail once it has passed. The branch is unreachable, and its caller's
    /// reaction to it — <c>miscError = NPCPartyLimitReached</c> at <c>RunEvent.cpp:10840</c>, which
    /// would blame a full party for a failed read — is unreachable with it.
    /// </para>
    /// <para>
    /// <b><c>hitPointMod</c> is a percentage of the NPC's own maximum, assigned rather than
    /// added</b>, and it is not bounded anywhere: the editor puts no validator on the box, so 0 and
    /// negative values are authorable. 0 seats an unconscious NPC at 0 hit points. A negative one
    /// goes through <c>SetHitPoints</c>, which floors at −10 and marks the character
    /// <see cref="CharacterStatus.Dead"/> on the way — <b>and <c>CHARACTER::SetStatus</c> empties
    /// the spell effect list when it is handed Dead</b> (<c>Char.h:907</c>). The next line then
    /// overwrites the status with <see cref="CharacterStatus.Unconscious"/>, so the death is
    /// invisible and the lost effects are permanent. An NPC can therefore join at −10 hit points
    /// and merely unconscious, which no other path in the engine produces.
    /// </para>
    /// <para>
    /// <b>The status test is on the <i>adjusted</i> total and the boundary is exclusive.</b>
    /// <c>GetAdjHitPoints() &gt; 0</c>, so exactly 0 is Unconscious, and a spell effect on
    /// <c>$CHAR_HITPOINTS</c> can flip the answer either way. Only those two statuses are
    /// reachable here; a joining NPC is never Dead, Petrified or anything else, whatever the design
    /// record said.
    /// </para>
    /// <para>
    /// <b>Morale is written from its own adjusted value, so effects are baked into the base.</b>
    /// <c>SetMorale(GetAdjMorale() + MoraleMod)</c> reads through the spell effects and writes the
    /// result to the permanent field, where the next read applies the same effects again. And
    /// because it runs <i>after</i> the status write, an NPC whose effects were just cleared by the
    /// transient Dead above has none left to bake. Both ends clamp to 0..100
    /// (<c>Char.cpp:14111</c>, <c>Char.h:839</c>).
    /// </para>
    /// <para>
    /// <b><c>useOriginal = 0</c> asks for a saved copy of the NPC, and the reference can never find
    /// one.</b> It calls <c>serializeCharacter(FALSE, name)</c>, whose load branch for an
    /// <c>NPC_TYPE</c> character builds the path as save-folder plus the literal <c>"DCNPC_"</c>
    /// and <b>never appends the name or the extension</b> (<c>Char.cpp:6889-6893</c>) — while the
    /// save branch four hundred lines later writes <c>DCNPC_&lt;name&gt;.chr</c>
    /// (<c>Char.cpp:6992</c>). The open fails, the copy is discarded, and the design's original is
    /// used. So the observable behaviour of <c>useOriginal = 0</c> is the same as
    /// <c>useOriginal = 1</c>, which is what this does. <b>Named as a gap rather than claimed as
    /// equivalence:</b> this port has no character save layer at all, so if the reference's path
    /// were fixed the two would diverge, and a save folder containing a file called exactly
    /// <c>DCNPC_</c> would diverge today. All three corpus adds carry <c>useOriginal = 1</c>.
    /// </para>
    /// <para>
    /// <b>Three of the reference's writes have nowhere to land.</b> <c>uniquePartyID</c> is assigned
    /// from <c>GetNextUniquePartyID</c> (<c>Party.cpp:2489</c>) and is live in the original —
    /// targeting and script actor identity index by it — but nothing in this port reads it, so it
    /// is not fabricated. <c>SetPartyMember()</c> sets a flag this port does not model.
    /// <c>SetType(NPC_TYPE)</c> is the one that works out: <see cref="HaveNpc"/> has already
    /// required the record's kind to be exactly that, so the member's
    /// <see cref="Character.Record"/> already carries the value the reference would write.
    /// </para>
    /// <para>
    /// <b>And one that is a script call.</b> <c>RunJoinPartyMemorizeScripts</c>
    /// (<c>Party.cpp:2517</c>) fires the new member's <c>JOIN_PARTY</c> character scripts. This
    /// port's scripting host is not attached to party membership, so the hook is absent.
    /// </para>
    /// <para>
    /// <b>This port's party is a stand-in, and it matters to the first test.</b>
    /// <c>Game</c> seeds the roster from the design's own character records, which are all stored
    /// as <see cref="NpcType"/> — the reference builds a party from <c>CHAR_TYPE</c> characters
    /// made in character generation, none of which are in the design's list. So a stand-in member
    /// looks exactly like an already-joined NPC to <see cref="IsNpcInParty"/>, and an add event
    /// naming someone who happens to be a seeded member is suppressed by
    /// <see cref="ShouldTriggerAdd"/> where the reference would seat them. Nothing here works
    /// around that; it is a property of how the roster is built.
    /// </para>
    /// </remarks>
    public static AddNpcOutcome Add(AddNpcEvent add, Party party,
                                    Func<string, CharacterRecord?> findCharacter,
                                    MoneyRules money, int maxPartyMembers)
    {
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(findCharacter);
        ArgumentNullException.ThrowIfNull(money);

        if (!HaveNpc(add.CharacterId, findCharacter))
        {
            return new AddNpcOutcome(AddNpcResult.NoSuchNpc, null, 0);
        }

        if (!CanAddNpc(party, maxPartyMembers))
        {
            return new AddNpcOutcome(AddNpcResult.PartyFull, null, 0);
        }

        // Party.cpp:2566 -- drawn from the roster as it stands, before the newcomer is placed.
        int moraleModifier = MoraleModifier(HighestCharisma(party));

        double percent = (double)add.HitPointMod / (double)100;

        // Party.cpp:2595. HaveNPC has already resolved this id, so the null is unreachable; it is
        // the reference's dead "Failed GetCharacterData" branch and returns the same false.
        if (findCharacter(add.CharacterId ?? string.Empty) is not { } record)
        {
            return new AddNpcOutcome(AddNpcResult.NoSuchNpc, null, 0);
        }

        // useOriginal == 0 would reload a saved copy here. The reference's load path for an NPC
        // cannot find one -- see the remarks -- so the original is used either way.
        var joined = new Character(record, money);

        SetHitPoints(joined, (int)((double)joined.MaxHitPoints * percent));

        joined.Status = joined.AdjustedHitPoints > 0
            ? CharacterStatus.Okay
            : CharacterStatus.Unconscious;

        joined.Morale = AdjustedMorale(joined) + moraleModifier;

        party.Add(joined);

        return new AddNpcOutcome(AddNpcResult.Joined, joined, moraleModifier);
    }

    /// <summary>
    /// Dismisses the NPC (<c>PARTY::removeNPCFromParty</c>, <c>Shared/Party.cpp:2644</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The first match only.</b> The loop's condition is <c>(i &lt; numCharacters) &amp;&amp;
    /// (!found)</c>, so it stops the moment it removes someone; a party holding the same NPC twice
    /// — which nothing prevents, since <c>addNPCToParty</c> does not check — loses one copy per
    /// event.
    /// </para>
    /// <para>
    /// <b>Not finding anyone is not an error.</b> The reference writes a debug line and returns,
    /// and the event chains regardless.
    /// </para>
    /// <para>
    /// <b>The kind gate is doing real work in the reference and less here.</b> Only an
    /// <see cref="NpcType"/> member can be dismissed, which in the original protects every player
    /// character. This port's stand-in roster is built from design records that are all stored as
    /// NPCs, so a remove event naming a seeded member would dismiss them — the same stand-in
    /// mismatch described on <see cref="Add"/>, seen from the other side. An event with an empty
    /// <c>characterID</c> would likewise match any seeded member whose own id is empty; no corpus
    /// design has one, and the single corpus remove names <c>Meuronna</c>.
    /// </para>
    /// <para>
    /// <b>What removal does besides removing is not ported.</b> <c>removeCharacter</c> writes the
    /// departing character to <c>DCNPC_&lt;name&gt;.chr</c> in the save folder first
    /// (<c>Party.cpp:2061</c>, through <c>Char.cpp:6992</c>), gated on the record's
    /// <c>CanBeSaved</c> flag — four of the six characters in the corpus's Case design have it
    /// clear. This port has no character save layer, so that write is absent; nothing reads those
    /// files today either, because the only reader is the <c>useOriginal = 0</c> path above and it
    /// is looking at the wrong filename.
    /// </para>
    /// <para>
    /// <b>The <c>AddToTemps</c> argument is dead.</b> <c>removeNPCFromParty</c> passes FALSE, and
    /// even a TRUE would be discarded: both arms of the <c>if (GetType() == CHAR_TYPE)</c> at
    /// <c>Party.cpp:2052</c> set it to FALSE, the NPC-specific condition that once distinguished
    /// them being commented out at <c>:2059</c>. So no removed character is ever added to the temp
    /// list.
    /// </para>
    /// </remarks>
    public static RemoveNpcOutcome Remove(RemoveNpcEvent remove, Party party)
    {
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(party);

        int index = IndexOfNpc(party, remove.CharacterId);
        if (index < 0)
        {
            return new RemoveNpcOutcome(false, -1, null);
        }

        var member = party.Members[index];
        party.RemoveAt(index);

        return new RemoveNpcOutcome(true, index, member);
    }

    /// <summary>
    /// Morale with spell effects applied (<c>CHARACTER::GetAdjMorale</c>,
    /// <c>Shared/Char.cpp:14101</c>).
    /// </summary>
    /// <remarks>
    /// Clamped to 0..100 on the way out, the same window <see cref="Character.Morale"/> clamps to on
    /// the way in. A private here rather than a fourth <c>Adjusted*</c> on
    /// <see cref="Character"/>, because this is its only caller in the port — morale is read for a
    /// decision in exactly one other place in the reference, the end-of-combat restore at
    /// <c>Combatant.cpp:10556</c>, which is not ported.
    /// </remarks>
    private static int AdjustedMorale(Character who) =>
        who.Effects.Apply(who.Morale, "$CHAR_MORALE", 0, 100);

    /// <summary>
    /// Writes a character's hit points (<c>CHARACTER::SetHitPoints</c>,
    /// <c>Shared/Char.cpp:14787</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same function <see cref="EventHeal"/> transcribes, with one line it leaves out:
    /// <c>SetStatus(Dead)</c> also empties the spell effect list (<c>Char.h:907</c>), and on this
    /// path that clearing outlives the status it came with. It is invisible today because
    /// <see cref="Character"/>'s constructor does not load <c>SpellEffects</c> off the record, so a
    /// character built here starts with none — but it is what the reference does and a record
    /// carrying effects would show it.
    /// </para>
    /// <para>
    /// <b>The diseased clamp is absent</b>, as it is in <see cref="EventHeal"/>: the reference holds
    /// a healing character to 1 hit point under <c>SA_Diseased</c> (<c>Char.cpp:14800</c>) and this
    /// port has no special-ability model outside combat. It could only bite on a
    /// <c>hitPointMod</c> that raised the NPC above the hit points its own record carries.
    /// </para>
    /// </remarks>
    private static void SetHitPoints(Character who, int value)
    {
        who.HitPoints = value;

        if (who.HitPoints > who.MaxHitPoints)
        {
            who.HitPoints = who.MaxHitPoints;
        }
        else if (who.HitPoints < Character.DeadAt)
        {
            who.HitPoints = Character.DeadAt;
        }

        if (who.HitPoints < 0
            && (who.HitPoints == Character.DeadAt || who.Status == CharacterStatus.Okay))
        {
            who.Status = CharacterStatus.Dead;
            who.Effects.Clear();                         // CHARACTER::SetStatus, Char.h:907
        }
    }
}
