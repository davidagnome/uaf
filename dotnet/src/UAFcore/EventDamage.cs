using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// One character's share of one attack from a <c>GIVE_DAMAGE_DATA</c>.
/// </summary>
/// <param name="Member">The victim's index into <see cref="Party.Members"/>.</param>
/// <param name="Attack">
/// Which pass of the <c>nbrAttacks</c> loop produced this, zero-based. A character can appear
/// several times.
/// </param>
/// <param name="Hit">
/// The <see cref="SaveResult.UseThac0"/> branch's attack roll. <b>Always true on every other
/// branch</b>, which does not roll to hit at all — the save is what modulates it.
/// </param>
/// <param name="Saved">
/// Whether the saving throw succeeded. Always false on the <see cref="SaveResult.UseThac0"/>
/// branch, which rolls no save.
/// </param>
/// <param name="Rolled">
/// What the damage dice plus bonus produced, before the save was applied. Zero when a
/// <see cref="SaveResult.UseThac0"/> attack missed, because a miss never rolls damage. Can be
/// negative — see <see cref="EventDamage"/>.
/// </param>
/// <param name="Damage">
/// The number the reference announces as "takes %i damage" (<c>Char.cpp:8229</c>). <b>Not
/// necessarily the hit points lost</b>: the message is formatted outside the status gate, so a
/// corpse is reported as taking damage it did not take, and the floor at −10 absorbs the rest of an
/// overkill.
/// </param>
/// <param name="HitPoints">The victim's hit points afterwards.</param>
/// <param name="Status">The victim's status afterwards.</param>
/// <param name="Died">Whether this blow is what made the victim dead.</param>
public readonly record struct DamageHit(int Member, int Attack, bool Hit, bool Saved, int Rolled,
                                        int Damage, int HitPoints, CharacterStatus Status,
                                        bool Died);

/// <summary>Everything a Damage event did.</summary>
/// <param name="Hits">
/// One entry per attack that was actually rolled, in the order the reference rolls them.
/// <b>A <see cref="PartyAffect.ChanceOnEach"/> character who failed the chance roll is absent</b>
/// rather than present with zero damage, because the reference does nothing at all for them.
/// </param>
/// <param name="TotalDamage">
/// The sum of <see cref="DamageHit.Damage"/> — what the reference announces, which is not the same
/// as hit points lost. Compare <see cref="DamageHit.HitPoints"/> for that.
/// </param>
/// <param name="Deaths">How many characters this event killed.</param>
public sealed record DamageOutcome(IReadOnlyList<DamageHit> Hits, int TotalDamage, int Deaths);

/// <summary>
/// Runs a <c>GIVE_DAMAGE_DATA</c> — the trap, the falling rock, the gout of flame
/// (<c>GIVE_DAMAGE_DATA::OnKeypress</c>, <c>RunEvent.cpp:10006</c>, and the
/// <c>CHARACTER::giveCharacterDamage</c> it calls, <c>Char.cpp:8177</c>).
/// </summary>
/// <remarks>
/// <para>
/// It is the only event that attacks the party outside combat, and it does so through its own
/// private copy of the combat rules rather than through the combat ones. That copy is not the same
/// arithmetic, and the differences are the whole content of this file.
/// </para>
/// <para>
/// <b>Two branches that share nothing but the damage dice.</b> When <c>eventSave</c> is
/// <see cref="SaveResult.UseThac0"/> the event rolls a d20 to hit and rolls damage only if it
/// lands; otherwise it rolls damage first and then rolls a saving throw that can halve or cancel
/// it. The dice are drawn in that order, which matters to anything reproducing a run.
/// </para>
/// <para>
/// <b>The event's <c>saveBonus</c> field does nothing whatsoever.</b> It is handed to
/// <c>CHARACTER::DidSaveVersus</c> as its <c>bonus</c> parameter (<c>Char.cpp:8316</c>) and the
/// live body of that function never reads it — the only code that did is inside the deprecated
/// script block, commented out at <c>Char.cpp:8351</c>. The editor offers the field, designs set
/// it, and it has no effect. Reproduced: <see cref="Apply"/> ignores <c>SaveBonus</c> entirely.
/// </para>
/// <para>
/// <b>Nor do the target's own save bonuses apply.</b> <c>DidSaveVersus</c> only calls
/// <c>ModifySaveRollAsTarget</c> — the protection-from-evil, shield and displacement bonuses of
/// <see cref="SavingThrow.RollBonus"/> — when it is given a non-null attacker, and this event
/// passes <c>NULL</c> (<c>RunEvent.cpp:10037</c> and its three siblings). So the save against a
/// trap is the target's bare skill against a bare d20, unmodifiable from either side.
/// </para>
/// <para>
/// <b><c>distance</c> is presentation only.</b> <c>OnInitialEvent</c> (<c>RunEvent.cpp:9989</c>)
/// uses it for one thing, <c>currPic.SetFrame(party.DetermineSpriteDistance(distance))</c> — which
/// sprite frame to draw. Nothing in <c>OnKeypress</c> reads it, so it changes no outcome and this
/// takes no account of it.
/// </para>
/// <para>
/// <b>The player picks the victim on the <see cref="PartyAffect.ActiveCharacter"/> setting.</b>
/// <c>OnKeypress</c> begins <c>if (TABParty(key)) return;</c>, and <c>TABParty</c>
/// (<c>RunEvent.cpp:792</c>) advances <c>party.activeCharacter</c> and redraws. So the screen sits
/// there until Return is pressed, and TAB until then chooses who takes the hit. That is input, not
/// rules, so it belongs to the caller — but a caller that runs this without offering the choice has
/// dropped a real mechanic.
/// </para>
/// <para>
/// <b>Two things the reference does here that this port cannot.</b> The event ends with
/// <c>party.setPartyAdventureState()</c> (<c>Party.cpp:2947</c>), which re-reads the current dungeon
/// level and raises the <c>adventuring</c> flag; <see cref="Party"/> models neither field, so it is
/// not reproduced. And <c>SetCharContext</c>/<c>SetTargetContext</c> bracket each blow to give
/// scripts an actor to read — but no script runs anywhere on this path, the only candidate being
/// the commented-out save-hook block, so the brackets are dead weight and are not ported.
/// </para>
/// </remarks>
public static class EventDamage
{
    /// <summary>
    /// The floor hit points cannot go below (<c>Char.cpp:8258</c>), matching
    /// <see cref="Character.DeadAt"/>.
    /// </summary>
    public const int MinimumHitPoints = Character.DeadAt;

    /// <summary>The best and worst numbers the THAC0 branch will ever ask for.</summary>
    /// <remarks>
    /// <b>Not <see cref="ToHit"/>'s clamp.</b> <c>need = max(catt, 1); need = min(need, 20)</c>
    /// (<c>Char.cpp:8194</c>) pins the target number inside 1–20, where the ordinary attack path
    /// collapses anything below <c>MIN_THAC0</c> to zero. The consequence is that a Damage event's
    /// THAC0 attack <b>always has at least a 1-in-20 chance of missing and at least a 1-in-20
    /// chance of landing</b>, no matter how the design sets <c>attackTHAC0</c> or how well armoured
    /// the party is. A trap cannot be made to always hit, or never to.
    /// </remarks>
    public const int BestTargetNumber = 1;

    /// <inheritdoc cref="BestTargetNumber"/>
    public const int WorstTargetNumber = 20;

    /// <summary><c>NUM_CHAR_STATUS_TYPES</c> (<c>GameRules.h:90</c>).</summary>
    public const int CharacterStatusTypes = 10;

    /// <summary>
    /// The saving-throw score of a character who has no such skill defined
    /// (<c>NoSkill</c>, <c>class.h:1051</c>).
    /// </summary>
    /// <remarks>
    /// <c>0x80000000</c>, which is <see cref="int.MinValue"/>. <c>CHARACTER::GetAdjSkillValue</c>
    /// returns it for a skill the character's race and baseclasses never mention
    /// (<c>Char.cpp:1019</c>), and <c>DidSaveVersus</c> then does <c>score = min(score, 20)</c> —
    /// which leaves it alone — and fails the save on <c>roll &lt; score</c>. No d20 is less than
    /// <see cref="int.MinValue"/>, so <b>a character with no saving-throw skill always saves</b>.
    /// That is the reference's behaviour, not a fallback invented here, and it is what this port
    /// does when no <c>saveScore</c> is supplied.
    /// </remarks>
    public const int NoSkillScore = int.MinValue;

    /// <summary>
    /// How many times the event's <c>who</c> loop runs
    /// (<c>RunEvent.cpp:10011</c>).
    /// </summary>
    /// <remarks>
    /// <b>Two of the five targeting modes silently discard <c>nbrAttacks</c>.</b> The reference
    /// opens with <c>if ((who == ActiveChar)||(who==OneAtRandom)) nbrAttacks=1;</c> — and note that
    /// it <i>assigns to the event's own field</i>, so a design that authors five attacks against
    /// one random character gets one, and the stored event is quietly rewritten in the process.
    /// The rewrite is unobservable because the same assignment happens on every run and the engine
    /// never writes events back to disk; the lost four attacks are not.
    /// <para>
    /// <see cref="PartyAffect.EntireParty"/> and <see cref="PartyAffect.ChanceOnEach"/> keep their
    /// count. <see cref="PartyAffect.None"/> keeps it too and spends it doing nothing.
    /// </para>
    /// </remarks>
    public static int AttackCount(DamageEvent damage)
    {
        ArgumentNullException.ThrowIfNull(damage);

        return (PartyAffect)damage.Who is PartyAffect.ActiveCharacter or PartyAffect.OneAtRandom
            ? 1
            : damage.NbrAttacks;
    }

    /// <summary>
    /// Applies the event to the party.
    /// </summary>
    /// <param name="damage">The event, as read off disk.</param>
    /// <param name="party">The roster, and whose turn it is.</param>
    /// <param name="dice">A roller: given sides, returns 1..sides.</param>
    /// <param name="saveScore">
    /// The number a character must roll on the named saving throw
    /// (<c>GetAdjSkillValue(Save_Vs_…)</c>). Null means every character behaves as one with no such
    /// skill — see <see cref="NoSkillScore"/> for why that means they always save. The port has no
    /// reader for the skill tables, so there is nothing to derive it from here.
    /// </param>
    /// <param name="effectiveArmorClass">
    /// The armour class the THAC0 branch attacks (<c>GetEffectiveAC</c>, <c>Char.cpp:13220</c>).
    /// Null uses the character's stored value alone — see the remarks.
    /// </param>
    /// <param name="deadAtZero">
    /// The design's <c>deadAtZeroHP</c> flag (<c>GlobalData.h:866</c>), which collapses the
    /// unconscious and dying bands into plain death.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The THAC0 branch reads a different armour class from every other rule in the engine.</b>
    /// It calls <c>GetEffectiveAC</c> — stored value plus readied-item protection, clamped — where
    /// <see cref="Character.AdjustedArmorClass"/> is <c>GetAdjAC</c>, stored value plus <i>spell
    /// effects</i>. Nothing combines the two. So <b>a magically shielded character gets no benefit
    /// against a trap</b>, while a character in plate does. This port has no readied-item
    /// protection model on <see cref="Character"/> — the protection values live on the design's item
    /// records, which a character does not carry — so the default is the stored value alone and
    /// <paramref name="effectiveArmorClass"/> is where a caller that has those records supplies the
    /// real thing. Substituting <see cref="Character.AdjustedArmorClass"/> would be the wrong
    /// accessor, not a better one.
    /// </para>
    /// <para>
    /// <b>Everybody is attacked, including the dead.</b> Neither the loop nor
    /// <c>giveCharacterDamage</c>'s outer half checks status; only the inner half does
    /// (<see cref="GiveCharacterDamage"/>), and by then the dice are spent. A corpse in the roster
    /// therefore consumes a full attack's worth of rolls and takes nothing, which shifts every
    /// subsequent roll in the run. Filtering the roster first would produce different numbers for
    /// the living.
    /// </para>
    /// <para>
    /// <b>Damage can never heal.</b> The gate is <c>if (result &gt; 0)</c>
    /// (<c>Char.cpp:8223</c>), so a large negative <c>dmgBonus</c> produces nothing rather than
    /// hit points — even though the function it guards clamps upward at the maximum precisely
    /// because negative damage is meaningful elsewhere.
    /// </para>
    /// <para>
    /// <b>One deviation, and it is a bounds check.</b> The
    /// <see cref="PartyAffect.ActiveCharacter"/> branch guards only
    /// <c>if (party.activeCharacter &gt;= 0)</c> (<c>RunEvent.cpp:10045</c>) with no upper bound,
    /// and <see cref="PartyAffect.OneAtRandom"/> indexes <c>party.characters[i-1]</c> where
    /// <c>i = RollDice(party.numCharacters, 1)</c> — which is <b>0 for an empty party</b>, because
    /// <c>RollDice</c> returns its bonus when asked for zero sides (<c>Globals.cpp:4927</c>), so
    /// the reference reads <c>characters[-1]</c>. Both are out-of-bounds reads that C# cannot
    /// perform; this skips instead. Nothing else here departs from the reference.
    /// </para>
    /// </remarks>
    public static DamageOutcome Apply(DamageEvent damage, Party party, Func<int, int> dice,
                                      Func<Character, SaveVersus, int>? saveScore = null,
                                      Func<Character, int>? effectiveArmorClass = null,
                                      bool deadAtZero = false)
    {
        ArgumentNullException.ThrowIfNull(damage);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(dice);

        var hits = new List<DamageHit>();
        int total = 0;
        int deaths = 0;
        int attacks = AttackCount(damage);

        for (int attack = 0; attack < attacks; attack++)
        {
            switch ((PartyAffect)damage.Who)
            {
                case PartyAffect.None:
                    break;

                case PartyAffect.EntireParty:
                    for (int i = 0; i < party.Count; i++)
                    {
                        Strike(i, attack);
                    }

                    break;

                case PartyAffect.ActiveCharacter:
                    if (party.ActiveCharacter >= 0 && party.ActiveCharacter < party.Count)
                    {
                        Strike(party.ActiveCharacter, attack);
                    }

                    break;

                case PartyAffect.OneAtRandom:
                    // RollDice(numCharacters, 1) is 1..count, indexed at [roll - 1] -- the roll is
                    // one-based and the index is not. An empty party rolls 0; see the remarks.
                    int chosen = DiceExpression.Roll(1, party.Count, dice);
                    if (chosen >= 1)
                    {
                        Strike(chosen - 1, attack);
                    }

                    break;

                case PartyAffect.ChanceOnEach:
                    for (int i = 0; i < party.Count; i++)
                    {
                        // RollDice(100,1) is 1..100 against `<=`, so 100 always fires and 0 never
                        // does.
                        if (dice(100) <= damage.ChancePerAttack)
                        {
                            Strike(i, attack);
                        }
                    }

                    break;
            }
        }

        return new DamageOutcome(hits, total, deaths);

        void Strike(int member, int pass)
        {
            var target = party.Members[member];
            var before = target.Status;

            bool hit = true;
            bool saved = false;
            int rolled = 0;
            int result = 0;

            if ((SaveResult)damage.EventSave == SaveResult.UseThac0)
            {
                int need = Math.Clamp(ToHit.TargetNumber(damage.AttackThac0, ArmorClassOf(target)),
                                      BestTargetNumber, WorstTargetNumber);

                hit = ToHit.Hits(dice(20), need);
                if (hit)
                {
                    // A miss rolls no damage at all, so it costs fewer dice than a hit.
                    rolled = RollDamage();
                    result = rolled;
                }
            }
            else
            {
                // Damage first, save second, always in that order -- and the save is rolled even
                // for NoSave, whose whole meaning is that the answer is discarded.
                rolled = RollDamage();
                result = rolled;
                saved = DidSave(target);

                if (saved)
                {
                    result = (SaveResult)damage.EventSave switch
                    {
                        SaveResult.NoSave => result,
                        SaveResult.SaveNegates => 0,

                        // Truncating division, then a floor of 1. See the class remarks: this is
                        // the branch where saving can cost more than failing.
                        SaveResult.SaveForHalf => Math.Max(1, result / 2),

                        // UseThac0 cannot reach here; anything outside the enum can, and is
                        // negated.
                        _ => 0,
                    };
                }
            }

            int applied = 0;
            if (result > 0)
            {
                applied = result;
                GiveCharacterDamage(target, result, deadAtZero);
            }

            total += applied;
            bool died = before != CharacterStatus.Dead && target.Status == CharacterStatus.Dead;
            if (died)
            {
                deaths++;
            }

            hits.Add(new DamageHit(member, pass, hit, saved, rolled, applied, target.HitPoints,
                                   target.Status, died));
        }

        // RollDice(sides, numTimes, bonus): dmgDice is the number of SIDES and dmgDiceQty the
        // number of dice, which is the opposite of what the field names suggest. Either being
        // zero or less yields the bonus alone rather than nothing (Globals.cpp:4927).
        int RollDamage() =>
            DiceExpression.Roll(damage.DmgDiceQty, damage.DmgDice, dice) + damage.DmgBonus;

        int ArmorClassOf(Character target) =>
            effectiveArmorClass?.Invoke(target) ?? ArmorClass.Clamp(target.ArmorClass);

        bool DidSave(Character target)
        {
            // An out-of-range spellSave returns FALSE from the very first switch, before any dice
            // are drawn (Char.cpp:8331) -- so it is not "no save", it is a guaranteed failure that
            // also costs nothing to resolve.
            var versus = (SaveVersus)damage.SpellSave;
            if (versus is < SaveVersus.ParalyzePoisonDeathMagic or > SaveVersus.BreathWeapon)
            {
                return false;
            }

            // Magic resistance is rolled first and, on success, returns before the d20 exists.
            // Rolling both up front would consume a die the reference never draws.
            int resistance = target.Effects.Apply(target.Record.MagicResistance,
                                                  "$CHAR_MAGICRESIST", 0, 100);
            if (resistance > 0 && dice(100) <= resistance)
            {
                return true;
            }

            return SavingThrow.DidSaveVersus(saveScore?.Invoke(target, versus) ?? NoSkillScore,
                                             dice(20));
        }
    }

    /// <summary>
    /// Applies damage to a character and settles what it did to them
    /// (<c>CHARACTER::giveCharacterDamage(int)</c>, <c>Char.cpp:8245</c>).
    /// </summary>
    /// <returns>The character's hit points afterwards.</returns>
    /// <remarks>
    /// <para>
    /// <b>The same reference function as <see cref="Attack.ApplyDamage"/>, and this is not a
    /// duplicate of it.</b> That one takes a <see cref="Combatant"/>; this takes a
    /// <see cref="Character"/>, which is what the event path actually holds, and a
    /// <see cref="Character"/> carries the two things the reference reads here that a combatant in
    /// this port does not: a spell-effect list and a maximum. Three differences follow, all of them
    /// the reference's.
    /// </para>
    /// <para>
    /// <b>1. The gate reads the <i>adjusted</i> status.</b> <c>charStatusType stype =
    /// GetAdjStatus()</c> (<c>Char.cpp:8248</c>, and the accessor at <c>:13936</c>) runs the
    /// character's <c>$CHAR_STATUS</c> spell effects over the stored value and reverts to the stored
    /// one only if the result falls outside the enum. So a spell that moves a character's apparent
    /// status decides whether damage lands, and the write afterwards goes to the stored field
    /// regardless.
    /// </para>
    /// <para>
    /// <b>2. The bands are read off the <i>adjusted</i> hit points.</b> <c>HP =
    /// GetAdjHitPoints()</c> (<c>Char.cpp:8264</c>) — so a character carrying a <c>$CHAR_HITPOINTS</c>
    /// effect can be driven to zero stored hit points and stay conscious, or be knocked out while
    /// stored hit points are positive. The stored value is what took the damage; the adjusted value
    /// is what decides the consequence.
    /// </para>
    /// <para>
    /// <b>3. Dying clears the character's spell effects.</b> <c>SetStatus</c> is
    /// <c>{ status=val; if (status==Dead) m_spellEffects.RemoveAll(); }</c> (<c>Char.h:907</c>) — an
    /// inline setter with a side effect, easy to read past. Only <c>Dead</c> does it; going
    /// unconscious or dying leaves everything in place.
    /// </para>
    /// <para>
    /// <b>Damage only lands on five statuses</b> — okay, running, unconscious, animated, dying.
    /// Anything else, already-dead included, takes nothing and keeps its hit points. <b>The floor
    /// is applied before the ceiling</b>, so a <c>maxHitPoints</c> below −10 wins over the floor;
    /// and <b>zero is unconscious, not dying</b> (<c>Char.cpp:8272</c>), the bands being
    /// <c>&lt;= −10</c> dead,
    /// <c>&lt; 0</c> dying, <c>== 0</c> unconscious.
    /// </para>
    /// </remarks>
    public static int GiveCharacterDamage(Character character, int damage, bool deadAtZero = false)
    {
        ArgumentNullException.ThrowIfNull(character);

        if (AdjustedStatus(character) is not (CharacterStatus.Okay or CharacterStatus.Running
                                              or CharacterStatus.Unconscious
                                              or CharacterStatus.Animated or CharacterStatus.Dying))
        {
            return character.HitPoints;
        }

        // Sequential, not Math.Clamp: the reference floors then ceilings, so a maximum below the
        // floor would win rather than raise. Not demonstrable from here -- Character's own
        // AdjustedHitPoints uses Math.Clamp(value, DeadAt, MaxHitPoints) and throws on such a
        // character before this can return -- but transcribed the way the reference has it.
        int points = character.HitPoints - damage;
        if (points < MinimumHitPoints)
        {
            points = MinimumHitPoints;
        }

        if (points > character.MaxHitPoints)
        {
            points = character.MaxHitPoints;
        }

        character.HitPoints = points;

        int adjusted = character.AdjustedHitPoints;

        if (deadAtZero)
        {
            if (adjusted <= 0)
            {
                Kill(character);
            }

            return character.HitPoints;
        }

        if (adjusted <= MinimumHitPoints)
        {
            Kill(character);
        }
        else if (adjusted < 0)
        {
            character.Status = CharacterStatus.Dying;
        }
        else if (adjusted == 0)
        {
            character.Status = CharacterStatus.Unconscious;
        }

        return character.HitPoints;
    }

    /// <summary>
    /// The character's status with spell effects applied
    /// (<c>CHARACTER::GetAdjStatus</c>, <c>Char.cpp:13936</c>).
    /// </summary>
    /// <remarks>
    /// <b>It reverts rather than clamps.</b> Unlike every neighbouring <c>GetAdj*</c> accessor,
    /// which pins the adjusted value inside a legal range, this one throws the adjustment away
    /// entirely and returns the stored status when the result falls outside the enum — so an effect
    /// of +100 on <c>$CHAR_STATUS</c> changes nothing at all, where +1 changes everything.
    /// </remarks>
    public static CharacterStatus AdjustedStatus(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);

        int value = (int)character.Effects.Apply((int)character.Status, "$CHAR_STATUS");

        return value < 0 || value >= CharacterStatusTypes
            ? character.Status
            : (CharacterStatus)value;
    }

    /// <summary><c>SetStatus(Dead)</c> (<c>Char.h:907</c>), side effect and all.</summary>
    private static void Kill(Character character)
    {
        character.Status = CharacterStatus.Dead;
        character.Effects.Clear();
    }
}
