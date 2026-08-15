namespace UAFcore;

/// <summary>
/// A character's condition (<c>charStatusType</c>, <c>GameRules.h:153</c>).
/// </summary>
/// <remarks>
/// The numbering is serialized into save games, so it is transcribed rather than tidied.
/// <see cref="Unconscious"/> and <see cref="Dying"/> are different: the first is below one hit
/// point and stable, the second is below one and losing another every round.
/// </remarks>
public enum CharacterStatus
{
    Okay = 0,
    Unconscious = 1,
    Dead = 2,
    Fled = 3,
    Petrified = 4,
    Gone = 5,
    Animated = 6,
    TempGone = 7,
    Running = 8,
    Dying = 9,
}

/// <summary>
/// What kind of thing a combatant is (<c>CHAR_TYPE</c> and friends, <c>Char.h:132</c>).
/// </summary>
/// <remarks>
/// The distinction is load-bearing for targeting: a player character on your own side cannot be
/// attacked at all, while a non-pregenerated NPC can be — in the reference that is where the NPC
/// would change sides, though the line doing so is commented out.
/// </remarks>
public enum CombatantKind
{
    Character = 1,
    Npc = 2,
    Monster = 3,
}

/// <summary>
/// One participant in a fight (<c>COMBATANT</c>, <c>Combatant.h:103</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A slice of the original, not the whole of it.</b> <c>Combatant.h</c> declares some 90 members
/// and most of them forward to the underlying <c>CHARACTER</c>; what is here is what the round
/// clock and placement need — identity, position, the turn's resources, and the predicates that
/// decide whether this combatant still has something to do. Spell casting, animation, the
/// targeting queue and the auto-combat "thinking" are not ported.
/// </para>
/// <para>
/// The combatant does not own its position on the grid: <see cref="CombatMap"/> holds occupancy and
/// this holds the coordinate, exactly as the original splits them. Moving means updating both.
/// </para>
/// </remarks>
public sealed class Combatant : ISpellSubject
{
    /// <param name="index">
    /// Its place in the combatant list. This is the value written into the grid's occupancy
    /// layer, so it must match the caller's own ordering.
    /// </param>
    public Combatant(int index, bool isFriendly, CombatantIcon icon, string name = "")
    {
        Index = index;
        IsFriendly = isFriendly;
        Icon = icon;
        Name = name;
    }

    /// <summary>Its place in the combatant list, and its id in the grid's occupancy layer.</summary>
    public int Index { get; }

    /// <summary>
    /// Party and NPCs; everything else is a monster.
    /// </summary>
    /// <remarks>
    /// <b>The side this combatant joined on, which a script can override without changing.</b> See
    /// <see cref="FriendlyOverride"/> and <see cref="IsCurrentlyFriendly"/> — the reference keeps
    /// the two apart so a charm can be undone by clearing the override rather than by remembering
    /// what the original side was.
    /// </remarks>
    public bool IsFriendly { get; }

    /// <summary>
    /// A script's override of which side this combatant is on
    /// (<c>m_adjFriendly</c>, <c>UAFWin/Combatant.h:590</c>).
    /// </summary>
    /// <remarks>
    /// <b>Four states, and "toggle" is one of them.</b> 0 leaves <see cref="IsFriendly"/> alone,
    /// 1 forces friendly, 2 forces hostile, 3 inverts. Because 3 is stored rather than applied, a
    /// charmed monster that changes sides again still reads as inverted — the override is a lens
    /// over the original, not a new value.
    /// </remarks>
    public int FriendlyOverride { get; set; }

    /// <summary>
    /// Which side this combatant is on right now (<c>GetIsFriendly</c>,
    /// <c>UAFWin/Combatant.cpp:11549</c>).
    /// </summary>
    /// <remarks>
    /// <b>This, not <see cref="IsFriendly"/>, is what targeting should ask.</b> Anything reading
    /// the raw field sees the side the combatant started on and ignores every charm in the fight.
    /// </remarks>
    public bool IsCurrentlyFriendly => FriendlyOverride switch
    {
        0 => IsFriendly,
        1 => true,
        2 => false,
        _ => !IsFriendly,
    };

    /// <summary>The footprint, up to 4×4.</summary>
    public CombatantIcon Icon { get; }

    public string Name { get; }

    /// <summary>Top-left square, or −1 when not on the map.</summary>
    public int X { get; set; } = -1;

    /// <inheritdoc cref="X"/>
    public int Y { get; set; } = -1;

    public CharacterStatus Status { get; set; } = CharacterStatus.Okay;

    /// <summary>
    /// Current hit points. Negative means dying or dead — see <see cref="Attack.ApplyDamage"/>.
    /// </summary>
    /// <remarks>
    /// The reference keeps these on the underlying <c>CHARACTER</c> and reaches them through the
    /// combatant. Held here because every combat path that touches them goes through the
    /// combatant anyway, and the character model is not ported.
    /// </remarks>
    public int HitPoints { get; set; }

    /// <inheritdoc cref="HitPoints"/>
    public int MaxHitPoints { get; set; } = int.MaxValue;

    /// <summary>
    /// Whether someone has bandaged this combatant, which stops it bleeding out
    /// (<c>isBandaged</c>).
    /// </summary>
    /// <remarks>
    /// Set once and never cleared during a fight: a bandaged combatant is out of the dying loop
    /// permanently, not for one round.
    /// </remarks>
    public bool IsBandaged { get; set; }

    /// <summary>What this combatant is doing. Only ever set to a value below 11 — see the enum.</summary>
    public CombatantState State { get; set; } = CombatantState.None;

    /// <summary>Rolled initiative, 1..<see cref="CombatRound.MaxInitiative"/>.</summary>
    public int Initiative { get; set; }

    /// <summary>
    /// Movement points <b>spent</b> this round (<c>m_iMovement</c>) — it counts up from zero, not
    /// down from <see cref="MaxMovement"/>.
    /// </summary>
    /// <remarks>
    /// The name suggests an allowance and it is the opposite. <c>StartNewRound</c> zeroes it and
    /// <c>MoveCombatant</c> adds to it (<c>Combatant.cpp:9369</c>), so a combatant that has not
    /// moved reads 0. Reading it as "remaining" inverts every movement test.
    /// </remarks>
    public int Movement { get; set; }

    /// <summary>
    /// How many points may be spent per round (<c>GetAdjMaxMovement</c>).
    /// </summary>
    /// <remarks>
    /// The reference derives this from the character's encumbrance and any spell effects. Held as
    /// a plain value here; <c>UAF.Rules.Encumbrance</c> is where the derivation will come from.
    /// </remarks>
    public int MaxMovement { get; set; } = 12;

    /// <summary>
    /// Diagonal steps taken this round (<c>m_iNumDiagonalMoves</c>), tracked separately because
    /// <b>every second diagonal is free</b> — see <see cref="CombatMovement"/>.
    /// </summary>
    public int DiagonalMoves { get; set; }

    /// <summary>
    /// The eight-way direction of the last step (<c>m_iMoveDir</c>), kept separately from
    /// <see cref="Facing"/>, which only ever flips east or west.
    /// </summary>
    public PathDirection MoveDirection { get; set; } = PathDirection.None;

    /// <summary>Attacks left this round (<c>availAttacks</c>). Fractional by design.</summary>
    public double AvailableAttacks { get; set; }

    /// <summary>
    /// Attacks per round before any are spent (<c>GetNbrAttacks</c>).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="AvailableAttacks"/>, and the free-attack rule wants this one — the
    /// shipped script grants a retreating opponent's attacker its <i>full</i> complement, not what
    /// it has left. Also what <see cref="DamageDice.ForMonster"/> subtracts from to work out which
    /// attack is being made.
    /// </remarks>
    public int TotalAttacks { get; set; } = 1;

    /// <summary>
    /// Set once this combatant has finished its turn. <see cref="IsDone"/> returns it, and the
    /// round's <c>Advance</c> uses that to decide who acts.
    /// </summary>
    public bool TurnIsDone { get; set; }

    /// <summary>The spells this combatant knows and has ready.</summary>
    public SpellList Book { get; } = new();

    /// <summary>What this combatant carries (<c>myItems</c>).</summary>
    public List<UAF.Serialization.ItemInstance> Items { get; } = [];

    /// <summary>What it carries in coin, gems and jewellery, or null.</summary>
    public UAF.Serialization.MoneySack? Money { get; set; }

    /// <summary>
    /// Spends one charge of an item.
    /// </summary>
    /// <remarks>
    /// <see cref="UAF.Serialization.ItemInstance"/> is a record read off the wire, so the charge
    /// count is replaced rather than decremented in place.
    /// </remarks>
    public void SpendCharge(UAF.Serialization.ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);

        int at = Items.IndexOf(item);
        if (at >= 0 && item.Charges > 0)
        {
            Items[at] = item with { Charges = item.Charges - 1 };
        }
    }

    /// <summary>
    /// The spell effects currently on this combatant (<c>CHARACTER::m_spellEffects</c>).
    /// </summary>
    public UAF.Rules.SpellEffectList Effects { get; } = new();

    /// <summary>Percentage magic resistance (<c>GetAdjMagicResistance</c>).</summary>
    public int MagicResistance { get; set; }

    /// <summary>
    /// Hit dice, which is what a <see cref="SpellTargeting.SelectByHitDice"/> spell spends its
    /// budget on (<c>GetNbrHD</c>).
    /// </summary>
    /// <remarks>
    /// Fractional by design: the reference's accessor returns a double, because a monster can have
    /// a half or quarter die.
    /// </remarks>
    public double HitDice { get; set; } = 1;

    /// <summary>Armour class, as a saving throw and an attack both need it.</summary>
    public int ArmorClass { get; set; } = 10;

    /// <summary>To-hit number, likewise.</summary>
    public int Thac0 { get; set; } = 20;

    /// <summary>
    /// The spell this combatant has begun, or null (<c>m_spellIDBeingCast</c>).
    /// </summary>
    public string? SpellBeingCast { get; set; }

    /// <summary>
    /// The spell an item is casting, or null (<c>m_itemSpellIDBeingCast</c>). Set alongside
    /// <see cref="SpellBeingCast"/>, not instead of it.
    /// </summary>
    public string? ItemSpellBeingCast { get; set; }

    /// <summary>
    /// This combatant's entry in the pending-spell list, or <c>-1</c>
    /// (<c>combatant_pendingSpellKey</c>).
    /// </summary>
    /// <remarks>
    /// Held so an interrupted caster can withdraw its own entry without searching. A spell that
    /// resolves at once is never queued, so a combatant can be casting with no key.
    /// </remarks>
    public int PendingSpellKey { get; set; } = -1;

    /// <summary>Whether a begun spell is still waiting on the clock (<c>IsSpellPending</c>).</summary>
    public bool IsSpellPending => State == CombatantState.Casting && PendingSpellKey >= 0;

    /// <summary>
    /// The best turning level across this character's baseclasses — a baseclass's level minus its
    /// <c>m_turnUndeadLevel</c> (<c>GetTurnUndeadLevel</c>).
    /// </summary>
    /// <remarks>
    /// <see cref="TurnUndead.CannotTurn"/> is the sentinel for "does not turn", and is the
    /// default: most combatants are not clerics.
    /// </remarks>
    public int TurnLevel { get; set; } = TurnUndead.CannotTurn;

    /// <summary>
    /// Whether the player may take this combatant off automatic
    /// (<c>GetAdjAllowPlayerControl</c>). A spell can take control away.
    /// </summary>
    public bool AllowPlayerControl { get; set; } = true;

    /// <summary>
    /// Whether this combatant can put its turn off (<c>CanDelay</c>, <c>Combatant.cpp:7673</c>).
    /// </summary>
    /// <remarks>
    /// <b>Delaying is refused at the last initiative slot, not at the last walked one.</b> The test
    /// is <c>m_iInitiative + 1 &lt; INITIATIVE_Never</c>, so a combatant on 21 may still delay to
    /// 22 and one on 22 may not — the whole point being that a delayed turn must still come round
    /// this round.
    /// </remarks>
    public bool CanDelay() => !IsDone() && Initiative + 1 < CombatRound.NeverInitiative;

    /// <summary>
    /// Puts this combatant's turn off until later in the round
    /// (<c>DelayAction</c>, <c>Combatant.cpp:7685</c>).
    /// </summary>
    /// <returns>Whether the delay happened.</returns>
    /// <remarks>
    /// <b>Delaying does not end the turn.</b> Initiative goes up by one, the state clears and the
    /// combatant comes off the queue — but <see cref="TurnIsDone"/> is untouched, so the round's
    /// walk reaches it again at its new slot. That is what separates DELAY from END.
    /// </remarks>
    public bool DelayAction(TurnQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        if (!CanDelay())
        {
            return false;
        }

        Initiative++;
        State = CombatantState.None;

        if (queue.Top == Index)
        {
            queue.Pop();
        }

        return true;
    }

    /// <summary>Who this combatant is attacking, or <see cref="CombatMap.NoDude"/>.</summary>
    public int Target { get; set; } = CombatMap.NoDude;

    /// <summary>The last combatant to attack this one (<c>m_iLastAttacker</c>).</summary>
    public int LastAttacker { get; set; } = CombatMap.NoDude;

    /// <summary>The last combatant this one attacked (<c>m_iLastAttacked</c>).</summary>
    public int LastAttacked { get; set; } = CombatMap.NoDude;

    /// <summary>Whether this combatant moved this round (<c>didMove</c>).</summary>
    public bool DidMove { get; set; }

    /// <summary>Whether a turning attempt has already been spent — once per combat, not per round.</summary>
    public bool HasTurnedUndead { get; set; }

    /// <summary>
    /// Whether this combatant is running (<c>iFleeingFlags</c>, non-zero).
    /// </summary>
    /// <remarks>
    /// A bit field in the reference (<c>FLEEING_FLAGS</c>) recording <i>why</i>; only whether is
    /// used by the AI, so it is a flag here.
    /// </remarks>
    public bool IsFleeing { get; set; }

    /// <summary>
    /// Whether a cleric has turned this undead, which makes it flee its turner
    /// (<c>isTurned</c>).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="IsFleeing"/> in the reference and handled by a near-identical
    /// second block; the behaviour is the same, so the port takes either.
    /// </remarks>
    public bool IsTurned { get; set; }

    /// <summary>Character, NPC or monster. Decides who may be attacked on your own side.</summary>
    public CombatantKind Kind { get; set; } = CombatantKind.Character;

    /// <summary>
    /// Whether the computer runs this combatant (<c>OnAuto</c>). Every monster is auto, and a
    /// party member can be.
    /// </summary>
    /// <remarks>
    /// The reference's <c>OnAuto</c> can consult a script hook; this is the flag only. It matters
    /// for targeting because <b>an auto combatant never attacks its own side</b>.
    /// </remarks>
    public bool IsAuto { get; set; }

    /// <summary>A pre-generated NPC, which cannot be turned on by attacking it.</summary>
    public bool IsPreGenerated { get; set; }

    /// <summary>The round this combatant last attacked in (<c>lastAttackRound</c>).</summary>
    /// <remarks>
    /// <b>Starts at zero</b> (<c>Combatant.cpp:127</c>), not at a sentinel — which matters, because
    /// <c>CheckIdleTime</c> subtracts it from the round number and a sentinel would overflow. Two
    /// rules read it: the fractional-attack limit (<see cref="Targeting.CanAttack"/>) and the idle
    /// detection that ends a fight nobody is fighting (<see cref="CombatRound.IsIdle"/>).
    /// </remarks>
    public int LastAttackRound { get; set; }

    /// <summary>
    /// Whether this combatant can see through invisibility (<c>GetAdjDetectingInvisible</c>).
    /// </summary>
    /// <remarks>
    /// This and the three flags below stand in for special abilities, which are not ported. They
    /// default to off, which makes every target visible — the permissive choice, and the one that
    /// keeps a fight running rather than silently refusing every ranged attack.
    /// </remarks>
    public bool DetectsInvisible { get; set; }

    /// <summary>Invisible to everything (<c>SA_Invisible</c>).</summary>
    public bool IsInvisible { get; set; }

    /// <summary>Invisible to the undead only (<c>SA_InvisibleToUndead</c>).</summary>
    public bool IsInvisibleToUndead { get; set; }

    /// <summary>Invisible to animals only (<c>SA_InvisibleToAnimals</c>).</summary>
    public bool IsInvisibleToAnimals { get; set; }

    /// <summary>Whether this combatant is undead, for the invisibility rules.</summary>
    public bool IsUndead { get; set; }

    /// <summary>
    /// The four trait bitfields a monster carries (<c>Monster.h:60</c>–<c>126</c>).
    /// </summary>
    /// <remarks>
    /// <b>Four unrelated fields, not one.</b> Form, penalty, immunity and misc options are
    /// separate words with overlapping bit values, so they cannot be merged — bit 2 is
    /// <c>FormAnimal</c> in one and <c>CanBeHeldCharmed</c> in another. They reach a script
    /// through <c>$GET_ISMAMMAL</c> and its fifteen siblings; a combatant that is not a monster
    /// leaves them zero and answers the non-monster literals instead
    /// (<c>GpdlCharStats.NonMonsterTrait</c>).
    /// </remarks>
    public uint FormType { get; set; }

    /// <inheritdoc cref="FormType"/>
    public uint PenaltyType { get; set; }

    /// <inheritdoc cref="FormType"/>
    public uint ImmunityType { get; set; }

    /// <inheritdoc cref="FormType"/>
    public uint MiscOptionsType { get; set; }

    /// <summary>Whether this combatant is an animal, for the invisibility rules.</summary>
    public bool IsAnimal { get; set; }

    /// <summary>
    /// The square line of sight is measured from (<c>GetCenterX</c>, <c>Combatant.cpp:10949</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A west-facing combatant measures from one square further left.</b> The reference
    /// subtracts one from the half-width only when facing west, so a 2×2 monster's centre column
    /// depends on which way it looks.
    /// </para>
    /// <para>
    /// <b>The two axes do not agree.</b> <see cref="CenterY"/> subtracts one *unconditionally*
    /// (<c>Combatant.cpp:10967</c>) while this subtracts one only when facing west, so a 2×2
    /// combatant facing north has its centre at <c>(x + 1, y)</c> — the top-right square of its
    /// footprint, not the middle. Transcribed as it stands: line of sight and range are both
    /// measured from here, so straightening it would move every ranged attack.
    /// </para>
    /// </remarks>
    public int CenterX => Icon.Width <= 1
        ? X
        : Facing == UAFcore.Facing.West ? X + (Icon.Width / 2) - 1 : X + (Icon.Width / 2);

    /// <summary>
    /// The square line of sight is measured from (<c>GetCenterY</c>, <c>Combatant.cpp:10964</c>).
    /// </summary>
    /// <remarks>Subtracts one whatever the facing — see <see cref="CenterX"/>.</remarks>
    public int CenterY => Icon.Height <= 1 ? Y : Y + (Icon.Height / 2) - 1;

    /// <summary>Which way this combatant is looking, which shifts <see cref="CenterX"/>.</summary>
    public Facing Facing { get; set; } = Facing.North;

    /// <summary>
    /// Whether a script has declared this combatant unable to act
    /// (<c>m_isCombatReady</c>, <c>Combatant.cpp:6969</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// In the reference this is a tri-state: negative means "ask the scripts", and the
    /// <c>IS_COMBAT_READY</c> hook is run against both the character and the combatant, either of
    /// which can veto by returning a non-empty string. Zero means not ready, one means ready.
    /// </para>
    /// <para>
    /// <b>The script half is not wired up</b> — it needs GPDL running global scripts, the same gap
    /// monster placement worked around. This is the settled answer only, defaulting to ready. A
    /// design whose scripts gate readiness (a sleep or hold effect) will have its combatants act
    /// when they should not.
    /// </para>
    /// </remarks>
    public bool IsCombatReady { get; set; } = true;

    /// <summary>
    /// Whether this combatant is on the map and able to be there
    /// (<c>charOnCombatMap</c>, <c>Combatant.cpp:10463</c>).
    /// </summary>
    /// <remarks>
    /// Note that <see cref="CharacterStatus.Animated"/> and <see cref="CharacterStatus.Running"/>
    /// are <i>not</i> excluded — the test names the statuses that keep a combatant off the map
    /// rather than the ones that allow it, so anything unlisted counts as present.
    /// </remarks>
    public bool IsOnCombatMap(bool unconsciousOk = false, bool petrifiedOk = false)
    {
        if (IsUnconscious && !unconsciousOk)
        {
            return false;
        }

        if (Status == CharacterStatus.Petrified && !petrifiedOk)
        {
            return false;
        }

        return Status is not (CharacterStatus.Fled or CharacterStatus.Gone
                              or CharacterStatus.TempGone or CharacterStatus.Dead);
    }

    /// <summary>
    /// Whether this combatant is out of the fight but not dead
    /// (<c>charUnconscious</c>, <c>Combatant.cpp:10483</c>).
    /// </summary>
    /// <remarks><see cref="CharacterStatus.Dying"/> counts, which is why this is not a plain
    /// equality test.</remarks>
    public bool IsUnconscious =>
        Status is CharacterStatus.Dying or CharacterStatus.Unconscious;

    /// <summary>
    /// Whether this combatant has nothing left to do (<c>IsDone</c>, <c>Combatant.cpp:6951</c>).
    /// </summary>
    /// <param name="freeAttack">
    /// True when asking on behalf of an interrupting attack, which needs a target to be worth
    /// taking.
    /// </param>
    /// <remarks>
    /// <para>
    /// The single most load-bearing predicate in the round: <see cref="CombatRound.Advance"/>
    /// calls it for every combatant it considers, so a wrong answer either skips somebody's turn
    /// or hangs the round on a combatant that can never finish.
    /// </para>
    /// <para>
    /// <b>It mutates.</b> Being off the map, or being a free attacker with no target, sets
    /// <see cref="TurnIsDone"/> rather than just returning true — so asking the question changes
    /// the answer to later ones. Reproduced; the round depends on the latch.
    /// </para>
    /// <para>
    /// Petrified short-circuits <i>before</i> the readiness check, so a petrified combatant is
    /// done regardless of what any script would have said.
    /// </para>
    /// </remarks>
    public bool IsDone(bool freeAttack = false)
    {
        if (Status == CharacterStatus.Petrified)
        {
            return true;
        }

        if (!IsCombatReady)
        {
            return true;
        }

        if (!IsOnCombatMap())
        {
            TurnIsDone = true;
        }

        if (freeAttack && Target == CombatMap.NoDude)
        {
            TurnIsDone = true;
        }

        return TurnIsDone;
    }

    /// <summary>
    /// Ends this combatant's turn and hands the queue on
    /// (<c>EndTurn</c>, <c>Combatant.cpp:6877</c>).
    /// </summary>
    /// <param name="queue">The round's turn queue.</param>
    /// <param name="newState">What to leave this combatant doing. Guarding persists; most do not.</param>
    /// <remarks>
    /// <para>
    /// <b>Only acts when this combatant is at the top of the queue.</b> Calling it for anybody else
    /// sets the state and nothing more — the original guards on <c>qcomb.Top() == self</c>, so an
    /// interrupted combatant cannot end a turn it is not currently taking.
    /// </para>
    /// <para>
    /// The latch condition reads oddly and is transcribed as-is:
    /// <c>ChangeStats() || NumFreeAttacks() || NumGuardAttacks()</c>. So an ordinary turn marks
    /// itself done, and so does an interrupting attacker that still has attacks banked — the one
    /// case that does <i>not</i> latch is a spent interrupter, which is exactly the entry about to
    /// be popped anyway.
    /// </para>
    /// </remarks>
    public void EndTurn(TurnQueue queue, CombatantState newState = CombatantState.None)
    {
        ArgumentNullException.ThrowIfNull(queue);

        State = newState;

        if (queue.Top != Index)
        {
            return;
        }

        if (queue.AffectsStats || queue.FreeAttacks > 0 || queue.GuardAttacks > 0)
        {
            TurnIsDone = true;
        }

        queue.Pop();
    }

    /// <summary>
    /// Resets the per-round state (the body of <c>StartNewRound</c>'s combatant loop,
    /// <c>Combatants.cpp:4553</c>).
    /// </summary>
    /// <param name="attacksThisRound">
    /// What <c>determineNbrAttacks</c> / <c>determineAvailAttacks</c> worked out for this round.
    /// </param>
    /// <param name="continueGuarding">
    /// Whether the <c>GUARDING_START_OF_ROUND</c> hook said to keep guarding. Not wired up — see
    /// <see cref="IsCombatReady"/> for the same gap.
    /// </param>
    /// <param name="isAuto">
    /// Whether this combatant is computer-run (<c>OnAuto</c>). It takes a different branch of the
    /// guarding reset — see the remarks.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Nothing happens unless the combatant can act <i>and</i> has finished its last turn.</b>
    /// The reference gates the whole block on
    /// <c>charCanTakeAction() &amp;&amp; IsDone(...)</c> (<c>:4553</c>), so a combatant still
    /// mid-turn keeps everything it had, and one that is unconscious, dead, fled or petrified is
    /// skipped entirely.
    /// </para>
    /// <para>
    /// <b>Casting skips only the state reset, not the rest.</b> A spell in progress spans the round
    /// boundary, so the state is left alone — but attacks and movement are still recomputed,
    /// because that block sits outside the <c>ICS_Casting</c> check (<c>:4592</c>). Returning early
    /// for a caster would leave it with last round's movement.
    /// </para>
    /// <para>
    /// <b>Guarding persists by two different rules.</b> An auto combatant keeps
    /// <see cref="CombatantState.Guarding"/> outright; a player-run one is moved to
    /// <see cref="CombatantState.ContinueGuarding"/>, and only when the
    /// <c>GUARDING_START_OF_ROUND</c> hook said so.
    /// </para>
    /// <para>
    /// <b>Leftover attacks carry over, capped at this round's own maximum.</b> The reference adds
    /// the previous <c>availAttacks</c> back <i>after</i> recomputing, then clamps to the ceiling
    /// of the new value (<c>:4597</c>) — so an unused half-attack survives but cannot be banked
    /// indefinitely.
    /// </para>
    /// </remarks>
    public void BeginRound(double attacksThisRound, bool continueGuarding = false,
                           bool isAuto = false)
    {
        bool canTakeAction = Status is CharacterStatus.Okay or CharacterStatus.Running
                                       or CharacterStatus.Animated;
        if (!canTakeAction || !IsDone())
        {
            return;
        }

        if (State != CombatantState.Casting)
        {
            TurnIsDone = false;

            if (isAuto)
            {
                if (State != CombatantState.Guarding)
                {
                    State = CombatantState.None;
                }
            }
            else
            {
                State = State == CombatantState.Guarding && continueGuarding
                    ? CombatantState.ContinueGuarding
                    : CombatantState.None;
            }
        }

        double leftover = AvailableAttacks;
        AvailableAttacks = Math.Min(attacksThisRound + leftover, Math.Ceiling(attacksThisRound));

        Movement = 0;
        DiagonalMoves = 0;
    }
}
