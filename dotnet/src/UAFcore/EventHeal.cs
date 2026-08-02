using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// How a <c>HEAL_PARTY_DATA</c> turns <c>HowMuchHP</c> into a new hit point total
/// (<c>LiteralOrPercent</c>, <c>Shared/GameEvent.h:3091</c>).
/// </summary>
/// <remarks>
/// <b>Three values, where the field's name and its comment both say two.</b> The header calls it
/// "0=literal,1=percent" and the editor draws three radio buttons for it
/// (<c>LiteralOrPercentText</c>, <c>UAFWinEd/Globtext.cpp:233</c>), so a design can carry any of
/// the three and the header's comment is simply out of date. Anything above 2 abandons the event
/// — see <see cref="EventHeal.Apply"/>.
/// </remarks>
public enum HealAdjust
{
    /// <summary>"Add to Current" — <c>HowMuchHP</c> is a literal number of hit points.</summary>
    AddToCurrent = 0,

    /// <summary>"Add Percent of Max" — that percentage of the character's own maximum, added.</summary>
    AddPercentOfMax = 1,

    /// <summary>
    /// "Set to Percent of Max" — that percentage of the maximum, assigned rather than added, and
    /// never downwards.
    /// </summary>
    SetToPercentOfMax = 2,
}

/// <summary>What a heal event did.</summary>
/// <param name="Healed">How many characters had their hit points written.</param>
/// <param name="HitPointsRestored">
/// The net change in hit points across the party. <b>Can be negative</b> — see
/// <see cref="EventHeal.Apply"/>.
/// </param>
/// <param name="CursesLifted">How many carried items had their cursed flag cleared.</param>
/// <param name="Abandoned">
/// Whether an out-of-range <c>LiteralOrPercent</c> stopped the event part-way.
/// </param>
public readonly record struct HealOutcome(int Healed, int HitPointsRestored, int CursesLifted,
                                          bool Abandoned);

/// <summary>
/// Runs a <c>HEAL_PARTY_DATA</c> (<c>PARTY::HealParty</c>, <c>Shared/Party.cpp:4925</c>, reached
/// from <c>HEAL_PARTY_DATA::OnKeypress</c>, <c>UAFWin/RunEvent.cpp:10235</c>).
/// </summary>
/// <remarks>
/// <para>
/// The event itself is one screen of text and a Return; every observable thing it does happens in
/// <c>PARTY::HealParty</c>, which is what this is. <c>OnInitialEvent</c>
/// (<c>RunEvent.cpp:10216</c>) has a combat branch that would heal without asking, and it is
/// <b>commented out</b> — so a heal event always waits for the keypress, and
/// <c>IsCombatActive()</c> is false everywhere below.
/// </para>
/// <para>
/// <b>Absent from the corpus.</b> None of the six shipped designs contains a single
/// <c>HEAL_PARTY_DATA</c> (see <c>EventTypesAbsentFromCorpusTests</c>), so this is transcription
/// rather than observation — which is the reason for reproducing the reference's awkward parts
/// exactly rather than tidying them.
/// </para>
/// <para>
/// <b>Two things it does not do, both because the reference does not.</b> <c>HealDrain</c> is
/// dead: all four branches of the reference reach <c>WriteDebugString("Heal Drain not coded
/// yet")</c> and nothing else (<c>Party.cpp:4981</c>, <c>:5027</c>, <c>:5073</c>, <c>:5131</c>),
/// so a design that ticks it gets nothing. And <c>HealCurse</c> means the <i>item</i> flag
/// (<see cref="ItemInstance.Cursed"/>), not a curse spell effect — the reference walks
/// <c>myItems</c> and clears <c>cursed</c>, and never touches the spell effect list. So
/// <see cref="UAF.Rules.SpellEffectList"/> is not involved here despite the name.
/// </para>
/// </remarks>
public static class EventHeal
{
    /// <summary>
    /// The <c>HowMuchHP</c> a <c>HEAL_PARTY_DATA</c> still carries when a design predates 0.882
    /// (<c>HEAL_PARTY_DATA::Clear</c>, <c>Shared/GameEvent.cpp:13726</c>).
    /// </summary>
    /// <remarks>
    /// Not zero, which is the whole point — see <see cref="Adjustment(HealPartyEvent)"/>.
    /// </remarks>
    public const int LegacyAmount = 100;

    /// <summary>
    /// The <c>LiteralOrPercent</c> that goes with it (<c>Shared/GameEvent.cpp:13727</c>), making
    /// the pre-0.882 heal event a full heal.
    /// </summary>
    /// <remarks><inheritdoc cref="LegacyAmount" path="/remarks"/></remarks>
    public const HealAdjust LegacyMode = HealAdjust.AddPercentOfMax;

    /// <summary>
    /// The arithmetic a heal event is asking for, resolving the pre-0.882 default.
    /// </summary>
    /// <returns>
    /// The mode, which may be a value <see cref="HealAdjust"/> does not name, and the amount.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A zero pair means a full heal, not a heal of nothing, and this is the trap in the whole
    /// event.</b> <c>HowMuchHP</c> and <c>LiteralOrPercent</c> arrived at design version 0.882 and
    /// the reference's <c>Serialize</c> reads them only at or above it
    /// (<c>GameEvent.cpp:13811</c>). Below the gate the fields keep whatever the constructor left
    /// there — and <c>HEAL_PARTY_DATA::Clear</c> sets them to <b>100</b> and <b>1</b>
    /// (<c>GameEvent.cpp:13726-13727</c>), not to zero. So a pre-0.882 heal event adds 100% of
    /// each character's maximum: the old unconditional full heal, which is what the event meant
    /// before it grew a dial.
    /// </para>
    /// <para>
    /// <see cref="HealPartyEvent"/> writes 0 and 0 for that case instead, because a reader has
    /// nothing to read. So the zero pair is the port's spelling of "absent", and it is restored
    /// here rather than in the reader.
    /// </para>
    /// <para>
    /// <b>The cost is a collision, and it is worth stating plainly.</b> A design at 0.882 or above
    /// can author "add 0 to current" — the editor's <c>HowMuchHP</c> box has no
    /// <c>DDV_MinMaxInt</c> on it (<c>UAFWinEd/HealParty.cpp:70</c>) — and that is indistinguishable
    /// from the legacy default once the record is read. Such an event full-heals here where the
    /// reference would add nothing. Telling them apart needs the design version, which does not
    /// reach this far, and no design in the corpus contains a heal event of either kind.
    /// </para>
    /// </remarks>
    public static (HealAdjust Mode, int Amount) Adjustment(HealPartyEvent heal)
    {
        ArgumentNullException.ThrowIfNull(heal);

        return heal.LiteralOrPercent == 0 && heal.HowMuchHp == 0
            ? (LegacyMode, LegacyAmount)
            : ((HealAdjust)heal.LiteralOrPercent, heal.HowMuchHp);
    }

    /// <summary>
    /// Applies the event to the party (<c>PARTY::HealParty</c>, <c>Shared/Party.cpp:4925</c>).
    /// </summary>
    /// <param name="dice">
    /// A single roll of an <i>n</i>-sided die, 1..n — <c>RollDice(n, 1, 0)</c>, and
    /// <see cref="Game.Dice"/> when the engine calls it, so a test can pin it.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The random member is drawn before the switch, so every heal event consumes a roll.</b>
    /// <c>int rndDude = RollDice(numCharacters, 1) - 1;</c> is the first line of the function
    /// (<c>Party.cpp:4927</c>), above the <c>switch (data.who)</c> that decides whether anything
    /// wants it. An event affecting the entire party still moves the generator on by one, which
    /// matters to anything reproducing a recorded run. The one exception is an empty party:
    /// <c>RollDice</c> returns early for a die of no sides (<c>Globals.cpp:4927</c>) without
    /// touching the generator, so the roll is skipped here too.
    /// </para>
    /// <para>
    /// <b><c>chance</c> is read by <see cref="PartyAffect.ChanceOnEach"/> alone.</b> The other
    /// three affect modes never look at it — the editor greys the box out and forces it to 100 for
    /// them (<c>UAFWinEd/HealParty.cpp:121</c>), but a design need not have been saved by that
    /// editor and the runtime does not check.
    /// </para>
    /// <para>
    /// <b>Under <see cref="PartyAffect.ChanceOnEach"/> each character is rolled for twice</b>, once
    /// in the hit points pass and again in the curse pass (<c>Party.cpp:5081</c>, <c>:5117</c>), so
    /// a character can be healed and stay cursed or the reverse. The passes are sequential and
    /// each is skipped entirely when its flag is clear, so the number of rolls a heal event
    /// consumes depends on which boxes the design ticked.
    /// </para>
    /// <para>
    /// <b>An unknown mode abandons the event outright.</b> The reference's chain of
    /// <c>if</c>/<c>else if</c> on <c>LiteralOrPercent</c> ends in a bare <c>else return</c>
    /// (<c>Party.cpp:5053</c> and its three siblings) which leaves <c>HealParty</c>, not the
    /// arithmetic — so the curse pass is skipped too, and under
    /// <see cref="PartyAffect.EntireParty"/> so is every character after the first. It fires only
    /// when <c>HealHP</c> is set <i>and</i> some character actually reaches the arithmetic, so a
    /// <see cref="PartyAffect.ChanceOnEach"/> event with a chance of 0 lifts curses quite happily
    /// on a mode the reference cannot understand. Three radio buttons is all the editor offers
    /// (<c>DDX_Radio</c>, <c>UAFWinEd/HealParty.cpp:69</c>), so this is unreachable from authored
    /// data and reachable from a corrupt or hand-written record.
    /// </para>
    /// <para>
    /// <b>A heal can kill.</b> Nothing stops <c>HowMuchHP</c> being negative — the editor puts no
    /// validator on the box — and the two adding modes pass the result straight to
    /// <c>SetHitPoints</c>, which will take a character below zero and mark them
    /// <see cref="CharacterStatus.Dead"/>. Only <see cref="HealAdjust.SetToPercentOfMax"/> refuses
    /// to move anyone downwards.
    /// </para>
    /// <para>
    /// <b>One clamp in the reference has nothing to port to.</b> <c>SetHitPoints</c> holds a
    /// healing character to 1 hit point if <c>SA_Diseased</c> is on them
    /// (<c>Shared/Char.cpp:14797</c>). This port has no special-ability model on a character
    /// outside combat, so the clamp is absent rather than approximated; a diseased character is
    /// healed in full here.
    /// </para>
    /// <para>
    /// <b>The curse pass cannot reach treasure picked up during play.</b> It walks each
    /// character's own inventory, which is what the reference has; this port also keeps
    /// <see cref="Party.Carried"/>, a party-level list with no counterpart in the reference, and
    /// that is where a <c>GIVE_TREASURE_DATA</c> pickup lands. A cursed item acquired that way
    /// stays cursed. Widening the walk would be inventing a rule, so it is left as a known gap.
    /// </para>
    /// </remarks>
    public static HealOutcome Apply(HealPartyEvent heal, Party party, Func<int, int> dice)
    {
        ArgumentNullException.ThrowIfNull(heal);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(dice);

        // Party.cpp:4927 -- above the switch, so it is drawn whoever the event affects.
        int random = party.Count > 0 ? dice(party.Count) - 1 : -1;

        bool healHitPoints = heal.HealHitPoints != 0;
        bool healCurse = heal.HealCurse != 0;
        var result = new HealOutcome();

        switch ((PartyAffect)heal.Who)
        {
            case PartyAffect.EntireParty:
                if (healHitPoints)
                {
                    foreach (var member in party.Members)
                    {
                        if (!HealHitPoints(heal, member, ref result))
                        {
                            return result with { Abandoned = true };
                        }
                    }
                }

                if (healCurse)
                {
                    foreach (var member in party.Members)
                    {
                        LiftCurses(member, ref result);
                    }
                }

                break;

            case PartyAffect.ActiveCharacter:
                // The reference indexes characters[activeCharacter] unguarded; Active is null only
                // for a party that could not have run the event at all.
                if (party.Active is { } active)
                {
                    if (healHitPoints && !HealHitPoints(heal, active, ref result))
                    {
                        return result with { Abandoned = true };
                    }

                    if (healCurse)
                    {
                        LiftCurses(active, ref result);
                    }
                }

                break;

            case PartyAffect.OneAtRandom:
                if ((uint)random < (uint)party.Count)
                {
                    var chosen = party.Members[random];

                    if (healHitPoints && !HealHitPoints(heal, chosen, ref result))
                    {
                        return result with { Abandoned = true };
                    }

                    if (healCurse)
                    {
                        LiftCurses(chosen, ref result);
                    }
                }

                break;

            case PartyAffect.ChanceOnEach:
                if (healHitPoints)
                {
                    foreach (var member in party.Members)
                    {
                        if (dice(100) <= heal.Chance
                            && !HealHitPoints(heal, member, ref result))
                        {
                            return result with { Abandoned = true };
                        }
                    }
                }

                if (healCurse)
                {
                    foreach (var member in party.Members)
                    {
                        if (dice(100) <= heal.Chance)
                        {
                            LiftCurses(member, ref result);
                        }
                    }
                }

                break;

            // NoPartyMember returns, and the reference's switch has no default -- an affect value
            // outside the enum falls straight out of it. Both do nothing, after the roll above.
            default:
                break;
        }

        return result;
    }

    /// <summary>
    /// The hit points half, for one character (<c>Party.cpp:4933</c> and its three copies).
    /// </summary>
    /// <returns>False when an unknown mode abandoned the event.</returns>
    /// <remarks>
    /// <b>The two percentage modes truncate at different points, and it shows on a negative
    /// amount.</b> Both compute the share as a <c>double</c>;
    /// <see cref="HealAdjust.AddPercentOfMax"/> adds it to the current total and converts the
    /// <i>sum</i> to <c>int</c>, while <see cref="HealAdjust.SetToPercentOfMax"/> converts the
    /// share on its own. C truncates toward zero, so 10 hit points plus a share of −3.5 is 6 under
    /// the first rule and would be 7 if the share were rounded first.
    /// <para>
    /// <b>The status is only cleared above zero.</b> The reference tests
    /// <c>GetHitPoints() &gt; 0</c> after the write, so a character landing on exactly 0 keeps
    /// whatever status they had — neither revived by this line nor killed by the one inside
    /// <c>SetHitPoints</c>, which tests <c>&lt; 0</c>. And the test is on the value the clamp
    /// settled on, not the value asked for.
    /// </para>
    /// </remarks>
    private static bool HealHitPoints(HealPartyEvent heal, Character who, ref HealOutcome result)
    {
        (var mode, int amount) = Adjustment(heal);
        int current = who.HitPoints;
        int total;

        switch (mode)
        {
            case HealAdjust.AddToCurrent:
                total = current + amount;
                break;

            case HealAdjust.AddPercentOfMax:
                total = (int)(current + (who.MaxHitPoints * (amount * 0.01)));
                break;

            case HealAdjust.SetToPercentOfMax:
                total = (int)(who.MaxHitPoints * (amount * 0.01));
                if (total < current)
                {
                    total = current;                     // "no change", says the reference
                }

                break;

            default:
                return false;                            // the bare `else return`, Party.cpp:5053
        }

        SetHitPoints(who, total);

        if (who.HitPoints > 0)
        {
            who.Status = CharacterStatus.Okay;
        }

        result = result with
        {
            Healed = result.Healed + 1,
            HitPointsRestored = result.HitPointsRestored + (who.HitPoints - current),
        };

        return true;
    }

    /// <summary>
    /// Writes a character's hit points (<c>CHARACTER::SetHitPoints</c>,
    /// <c>Shared/Char.cpp:14787</c>).
    /// </summary>
    /// <remarks>
    /// <b>Clamped to the character's own maximum above and to <see cref="Character.DeadAt"/>
    /// below</b>, the same pair <see cref="Character.AdjustedHitPoints"/> uses — so however large
    /// <c>HowMuchHP</c> is, nobody ends above their maximum, and a heal event cannot be used to
    /// raise one.
    /// <para>
    /// <b>Landing below zero sets the status, and the two ways it can differ.</b> Exactly −10 is
    /// <see cref="CharacterStatus.Dead"/> unconditionally; anything between −9 and −1 is Dead only
    /// if the character was <see cref="CharacterStatus.Okay"/>, leaving an already unconscious or
    /// petrified character as they were. The reference guards the whole block with
    /// <c>!IsCombatActive()</c>, which is always true on this path because the heal event's combat
    /// branch is commented out (<c>RunEvent.cpp:10217</c>).
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
        }
    }

    /// <summary>
    /// The curse half, for one character (<c>Party.cpp:4966</c> and its three copies).
    /// </summary>
    /// <remarks>
    /// A plain walk of the character's own item list clearing every <c>cursed</c> flag. It does
    /// nothing else: the item is not unreadied, dropped or re-identified, and the item
    /// <i>database</i> entry it came from is untouched — only this instance stops being cursed.
    /// </remarks>
    private static void LiftCurses(Character who, ref HealOutcome result)
    {
        int lifted = 0;

        for (int i = 0; i < who.Items.Count; i++)
        {
            if (who.Items[i].Cursed != 0)
            {
                who.Items[i] = who.Items[i] with { Cursed = 0 };
                lifted++;
            }
        }

        result = result with { CursesLifted = result.CursesLifted + lifted };
    }
}
