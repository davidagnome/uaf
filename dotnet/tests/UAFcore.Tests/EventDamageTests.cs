using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The trap event (<c>GIVE_DAMAGE_DATA</c>) — the only thing that attacks the party outside a
/// fight, using its own copy of the combat arithmetic.
/// </summary>
/// <remarks>
/// Most of what is pinned here is the difference between that copy and the real one: a target
/// number clamped to 1–20 instead of collapsing to zero, a save with two independent reasons it
/// cannot be modified, and a save-for-half that can do more damage than a failed save.
/// </remarks>
public class EventDamageTests
{
    /// <summary>
    /// A roller that hands out a scripted sequence and records what it was asked for.
    /// </summary>
    /// <remarks>
    /// It throws when it runs dry rather than improvising, because half of these tests are about
    /// <i>how many</i> dice the reference draws and in what order — an over-consuming port would
    /// otherwise pass silently.
    /// </remarks>
    private sealed class Dice(params int[] rolls)
    {
        private int at;

        /// <summary>The number of sides asked for, in order.</summary>
        public List<int> Asked { get; } = [];

        public int Next(int sides)
        {
            Asked.Add(sides);
            if (at >= rolls.Length)
            {
                throw new InvalidOperationException($"the roller ran dry after {at} rolls");
            }

            return rolls[at++];
        }
    }

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static DamageEvent Event(PartyAffect who = PartyAffect.EntireParty,
                                     int attacks = 1, int chance = 100,
                                     int sides = 6, int qty = 1, int bonus = 0,
                                     SaveResult eventSave = SaveResult.NoSave,
                                     SaveVersus spellSave = SaveVersus.ParalyzePoisonDeathMagic,
                                     int saveBonus = 0, int thac0 = 20,
                                     EncounterDistance distance = EncounterDistance.UpClose) =>
        // DmgDice is the number of SIDES and DmgDiceQty the count -- see the field-order test.
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.Damage, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            NbrAttacks: attacks, ChancePerAttack: chance, DmgDice: sides, DmgDiceQty: qty,
            DmgBonus: bonus, SaveBonus: saveBonus, AttackThac0: thac0,
            EventSave: (int)eventSave, SpellSave: (int)spellSave, Who: (int)who,
            Distance: (int)distance);

    private static Character Hero(string name = "hero", int hitPoints = 20, int maxHitPoints = 20,
                                  int armorClass = 5, int magicResistance = 0,
                                  CharacterStatus status = CharacterStatus.Okay)
    {
        // Named where it matters: the record has seventeen leading scalars and getting armour
        // class or hit points into the wrong slot produces a character that looks fine and reads
        // zero.
        var record = new CharacterRecord(
            CharacterVersion: 0, Type: 0, Race: "human", Gender: 0, ClassId: "fighter",
            Alignment: 0, AllowInCombat: 0, Status: (int)status, UndeadType: "", CreatureSize: 0,
            Name: name, CharacterId: name,
            Thac0: 18, Morale: 50, Encumbrance: 0, MaxEncumbrance: 0, ArmorClass: armorClass,
            HitPoints: hitPoints, MaxHitPoints: maxHitPoints, NumberOfHitDice: 1,
            Age: 0, MaxAge: 0, Birthday: 0, MaxCureDisease: 0,
            UnarmedDieSmall: 0, UnarmedNumberDieSmall: 0, UnarmedBonus: 0,
            UnarmedDieLarge: 0, UnarmedNumberDieLarge: 0,
            MaxMovement: 12, ReadyToTrain: 0, CanTradeItems: 0,
            Abilities: new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            OpenDoors: 0, OpenMagicDoors: 0, BendBarsLiftGates: 0,
            HitBonus: 0, DamageBonus: 0, MagicResistance: magicResistance,
            BaseclassStats: [new BaseclassStats("fighter", 1, 0, 0, 0)],
            SkillAdjustments: [], SpellAdjustments: [],
            IsPreGenerated: 0, CanBeSaved: 0, HasLayedOnHandsToday: 0,
            Money: null, NumberOfAttacks: 1,
            Icon: null, IconIndex: 0, OriginalIndex: 0, UniquePartyId: 0,
            DisableTalkIfDead: 0, TalkEvent: 0, TalkLabel: "",
            ExamineEvent: 0, ExamineLabel: "",
            SpellBook: new UAF.Serialization.SpellBook(0, []),
            DetectingInvisible: 0, DetectingTraps: 0,
            SpellEffects: [], Blockages: [], SmallPic: null!,
            Items: new ItemList([], new ReadyItems([])),
            SpecialAbilities: new SpecabBlock([], [], []), Attributes: []);

        return new Character(record, MoneyRules.Default);
    }

    private static Party Roster(int count, int hitPoints = 20, int maxHitPoints = 20,
                               int armorClass = 5)
    {
        var party = new Party();
        for (int i = 0; i < count; i++)
        {
            party.Add(Hero($"m{i}", hitPoints, maxHitPoints, armorClass));
        }

        return party;
    }

    private static void Bless(Character character, string attribute, double change) =>
        character.Effects.Add(new ActiveSpellEffect(new UAF.Rules.SpellEffect(attribute, change),
                                                    StopTime: null));

    // ---- who is struck -----------------------------------------------------------------------

    [Fact]
    public void The_entire_party_is_struck_once_per_attack_in_roster_order()
    {
        var party = Roster(2);
        var dice = new Dice(3, 1, 4, 1, 5, 1, 6, 1);

        var outcome = EventDamage.Apply(Event(attacks: 2), party, dice.Next);

        // Attack 0 walks the roster, then attack 1 walks it again -- not both attacks on one
        // member before moving on, which would pair the damage rolls differently.
        Assert.Equal(20 - 3 - 5, party.Members[0].HitPoints);
        Assert.Equal(20 - 4 - 6, party.Members[1].HitPoints);
        Assert.Equal(18, outcome.TotalDamage);
        Assert.Equal(4, outcome.Hits.Count);
    }

    [Fact]
    public void Only_the_active_character_is_struck_when_the_event_says_so()
    {
        var party = Roster(3);
        party.ActiveCharacter = 2;
        var dice = new Dice(4, 1);

        EventDamage.Apply(Event(PartyAffect.ActiveCharacter), party, dice.Next);

        Assert.Equal(20, party.Members[0].HitPoints);
        Assert.Equal(20, party.Members[1].HitPoints);
        Assert.Equal(16, party.Members[2].HitPoints);
    }

    [Fact]
    public void One_at_random_rolls_one_based_and_indexes_zero_based()
    {
        var party = Roster(3);
        var dice = new Dice(3, 4, 1);

        EventDamage.Apply(Event(PartyAffect.OneAtRandom), party, dice.Next);

        // RollDice(numCharacters, 1) is 1..3 and the index is [roll - 1]. Transcribing the roll
        // as 0-based would make the last member unreachable.
        Assert.Equal(3, dice.Asked[0]);
        Assert.Equal(16, party.Members[2].HitPoints);
    }

    [Fact]
    public void One_at_random_on_an_empty_party_rolls_nothing_at_all()
    {
        var party = Roster(0);
        var dice = new Dice();

        var outcome = EventDamage.Apply(Event(PartyAffect.OneAtRandom), party, dice.Next);

        // RollDice(0, 1) returns its bonus -- zero -- and the reference then reads
        // characters[-1]. Nothing is rolled on the way there, so the roller stays untouched.
        Assert.Empty(dice.Asked);
        Assert.Empty(outcome.Hits);
    }

    [Fact]
    public void Chance_on_each_rolls_a_hundred_for_every_member_and_skips_the_failures()
    {
        var party = Roster(2);
        var dice = new Dice(50, 4, 1, 51);

        var outcome = EventDamage.Apply(Event(PartyAffect.ChanceOnEach, chance: 50), party,
                                        dice.Next);

        // The d100 is compared with `<=`, so 50 passes on a chance of 50 and 51 does not.
        Assert.Equal([100, 6, 20, 100], dice.Asked);
        Assert.Equal(0, Assert.Single(outcome.Hits).Member);
        Assert.Equal(20, party.Members[1].HitPoints);
    }

    [Fact]
    public void A_chance_of_a_hundred_reaches_everyone_and_a_chance_of_zero_no_one()
    {
        var all = Roster(1);
        EventDamage.Apply(Event(PartyAffect.ChanceOnEach, chance: 100), all,
                          new Dice(100, 4, 1).Next);
        Assert.Equal(16, all.Members[0].HitPoints);

        var none = Roster(1);
        // The roll floor is 1, not 0, so a chance of 0 can never be met.
        var outcome = EventDamage.Apply(Event(PartyAffect.ChanceOnEach, chance: 0), none,
                                        new Dice(1).Next);
        Assert.Empty(outcome.Hits);
    }

    [Fact]
    public void No_party_member_spends_its_attacks_doing_nothing()
    {
        var party = Roster(2);
        var dice = new Dice();

        var outcome = EventDamage.Apply(Event(PartyAffect.None, attacks: 3), party, dice.Next);

        // The loop still runs three times; the switch has an empty case for it.
        Assert.Empty(outcome.Hits);
        Assert.Empty(dice.Asked);
    }

    [Fact]
    public void The_active_character_and_one_at_random_are_capped_at_a_single_attack()
    {
        // The reference assigns nbrAttacks = 1 for these two before the loop, so a design that
        // authors five attacks on one random victim gets one.
        Assert.Equal(1, EventDamage.AttackCount(Event(PartyAffect.ActiveCharacter, attacks: 5)));
        Assert.Equal(1, EventDamage.AttackCount(Event(PartyAffect.OneAtRandom, attacks: 5)));

        // The other three keep their count, including the one that does nothing with it.
        Assert.Equal(5, EventDamage.AttackCount(Event(PartyAffect.EntireParty, attacks: 5)));
        Assert.Equal(5, EventDamage.AttackCount(Event(PartyAffect.ChanceOnEach, attacks: 5)));
        Assert.Equal(5, EventDamage.AttackCount(Event(PartyAffect.None, attacks: 5)));
    }

    [Fact]
    public void An_attack_count_of_zero_or_less_does_nothing()
    {
        var party = Roster(2);

        Assert.Empty(EventDamage.Apply(Event(attacks: 0), party, new Dice().Next).Hits);
        Assert.Empty(EventDamage.Apply(Event(attacks: -3), party, new Dice().Next).Hits);
    }

    [Fact]
    public void The_dead_are_still_attacked_and_still_consume_dice()
    {
        var party = new Party();
        party.Add(Hero("corpse", status: CharacterStatus.Dead));
        party.Add(Hero("living"));
        var dice = new Dice(5, 1, 4, 1);

        var outcome = EventDamage.Apply(Event(), party, dice.Next);

        // Neither the loop nor the outer half of giveCharacterDamage checks status, so a corpse
        // burns a full attack's worth of rolls. Filtering the roster first would hand the living
        // member the corpse's dice and change its damage.
        Assert.Equal([6, 20, 6, 20], dice.Asked);
        Assert.Equal(20, party.Members[0].HitPoints);
        Assert.Equal(16, party.Members[1].HitPoints);
        Assert.Equal(2, outcome.Hits.Count);
    }

    // ---- the damage dice ---------------------------------------------------------------------

    [Fact]
    public void The_damage_fields_are_sides_then_count_not_count_then_sides()
    {
        var party = Roster(1);
        var dice = new Dice(2, 2, 2, 1);

        EventDamage.Apply(Event(sides: 6, qty: 3), party, dice.Next);

        // RollDice(sides, numTimes, bonus): dmgDice is 6 sides and dmgDiceQty is 3 dice, which is
        // the opposite of what the names suggest. Reading it as 6d3 asks the roller for 3 sides.
        Assert.Equal([6, 6, 6, 20], dice.Asked);
        Assert.Equal(14, party.Members[0].HitPoints);
    }

    [Fact]
    public void Zero_sides_or_zero_dice_yields_the_bonus_alone()
    {
        var party = Roster(1);
        var dice = new Dice(1);

        EventDamage.Apply(Event(sides: 0, qty: 5, bonus: 7), party, dice.Next);

        // RollDice returns its bonus untouched when either count is non-positive, so no damage
        // die is drawn -- only the save's d20.
        Assert.Equal([20], dice.Asked);
        Assert.Equal(13, party.Members[0].HitPoints);
    }

    [Fact]
    public void A_damage_event_can_never_heal()
    {
        var party = Roster(1, hitPoints: 10);
        var dice = new Dice(1, 1);

        var outcome = EventDamage.Apply(Event(sides: 4, qty: 1, bonus: -10), party, dice.Next);

        // The gate is `if (result > 0)`, so a net-negative roll is dropped rather than restoring
        // hit points -- even though the function behind the gate clamps upward for exactly that.
        Assert.Equal(10, party.Members[0].HitPoints);
        Assert.Equal(-9, outcome.Hits[0].Rolled);
        Assert.Equal(0, outcome.Hits[0].Damage);
    }

    // ---- the saving throw --------------------------------------------------------------------

    [Fact]
    public void A_save_is_rolled_even_when_the_event_grants_none()
    {
        var party = Roster(1);
        var dice = new Dice(4, 20);

        var outcome = EventDamage.Apply(Event(eventSave: SaveResult.NoSave), party, dice.Next);

        // NoSave means "a successful save changes nothing", not "no save is rolled": the d20 is
        // drawn and its answer discarded, which shifts every subsequent roll in the run.
        Assert.Equal([6, 20], dice.Asked);
        Assert.True(outcome.Hits[0].Saved);
        Assert.Equal(4, outcome.Hits[0].Damage);
    }

    [Fact]
    public void A_character_with_no_saving_throw_skill_always_saves()
    {
        var party = Roster(1);

        var outcome = EventDamage.Apply(Event(eventSave: SaveResult.SaveNegates), party,
                                        new Dice(4, 1).Next);

        // GetAdjSkillValue returns NoSkill (0x80000000) for an undefined skill, and no d20 is
        // below int.MinValue. This port has no skill tables, so that is its default.
        Assert.True(outcome.Hits[0].Saved);
        Assert.Equal(0, outcome.Hits[0].Damage);
    }

    [Fact]
    public void A_supplied_save_score_saves_on_the_boundary_and_fails_below_it()
    {
        var failed = Roster(1);
        EventDamage.Apply(Event(eventSave: SaveResult.SaveNegates), failed, new Dice(4, 13).Next,
                          saveScore: (_, _) => 14);
        Assert.Equal(16, failed.Members[0].HitPoints);

        var saved = Roster(1);
        // The reference's test is `roll < score` for failure, so the boundary belongs to the
        // target.
        EventDamage.Apply(Event(eventSave: SaveResult.SaveNegates), saved, new Dice(4, 14).Next,
                          saveScore: (_, _) => 14);
        Assert.Equal(20, saved.Members[0].HitPoints);
    }

    [Fact]
    public void The_events_save_bonus_field_does_nothing()
    {
        var party = Roster(1);

        EventDamage.Apply(Event(eventSave: SaveResult.SaveNegates, saveBonus: 20), party,
                          new Dice(4, 13).Next, saveScore: (_, _) => 14);

        // saveBonus reaches DidSaveVersus as its `bonus` parameter and the live body never reads
        // it -- the only code that did is commented out. A +20 that saved would be wrong.
        Assert.Equal(16, party.Members[0].HitPoints);
    }

    [Fact]
    public void Save_negates_cancels_the_damage_entirely()
    {
        var party = Roster(1);

        var outcome = EventDamage.Apply(Event(eventSave: SaveResult.SaveNegates), party,
                                        new Dice(6, 20).Next, saveScore: (_, _) => 10);

        Assert.Equal(6, outcome.Hits[0].Rolled);
        Assert.Equal(0, outcome.Hits[0].Damage);
        Assert.Equal(20, party.Members[0].HitPoints);
    }

    [Fact]
    public void Save_for_half_truncates_and_then_floors_at_one()
    {
        var five = Roster(1);
        EventDamage.Apply(Event(eventSave: SaveResult.SaveForHalf), five, new Dice(5, 20).Next,
                          saveScore: (_, _) => 10);
        Assert.Equal(20 - 2, five.Members[0].HitPoints);       // 5 / 2 truncates to 2, not 3

        var one = Roster(1);
        EventDamage.Apply(Event(eventSave: SaveResult.SaveForHalf), one, new Dice(1, 20).Next,
                          saveScore: (_, _) => 10);
        Assert.Equal(20 - 1, one.Members[0].HitPoints);        // 1 / 2 is 0, floored back up to 1
    }

    [Fact]
    public void Saving_for_half_against_a_harmless_event_hurts_where_failing_does_not()
    {
        var saved = Roster(1);
        EventDamage.Apply(Event(eventSave: SaveResult.SaveForHalf, sides: 0, qty: 0), saved,
                          new Dice(20).Next, saveScore: (_, _) => 10);

        // A reference bug, reproduced: max(1, 0/2) is 1, so a successful save turns a zero-damage
        // event into one point of damage. A design can build a trap that only hurts you if you
        // resist it.
        Assert.Equal(19, saved.Members[0].HitPoints);

        var failed = Roster(1);
        EventDamage.Apply(Event(eventSave: SaveResult.SaveForHalf, sides: 0, qty: 0), failed,
                          new Dice(1).Next, saveScore: (_, _) => 10);
        Assert.Equal(20, failed.Members[0].HitPoints);
    }

    [Fact]
    public void An_out_of_range_saving_throw_type_fails_without_rolling()
    {
        var party = Roster(1);
        var dice = new Dice(4);

        var outcome = EventDamage.Apply(
            Event(eventSave: SaveResult.SaveNegates, spellSave: (SaveVersus)9), party, dice.Next,
            saveScore: (_, _) => 1);

        // DidSaveVersus returns FALSE from its opening switch, before magic resistance or the
        // d20. So it is a guaranteed failure that costs nothing to resolve -- with a score of 1
        // the d20 would otherwise have saved.
        Assert.Equal([6], dice.Asked);
        Assert.False(outcome.Hits[0].Saved);
        Assert.Equal(4, outcome.Hits[0].Damage);
    }

    [Fact]
    public void Magic_resistance_is_rolled_first_and_counts_as_a_save()
    {
        var resisted = new Party();
        resisted.Add(Hero(magicResistance: 50));
        var dice = new Dice(4, 30);

        var outcome = EventDamage.Apply(Event(eventSave: SaveResult.SaveNegates), resisted,
                                        dice.Next, saveScore: (_, _) => 21);

        // The d100 comes before the d20 and returns on success, so the d20 is never drawn. A
        // score of 21 would have failed every save.
        Assert.Equal([6, 100], dice.Asked);
        Assert.True(outcome.Hits[0].Saved);
        Assert.Equal(0, outcome.Hits[0].Damage);
    }

    [Fact]
    public void A_failed_resistance_roll_falls_through_to_the_ordinary_save()
    {
        var party = new Party();
        party.Add(Hero(magicResistance: 50));
        var dice = new Dice(4, 80, 3);

        var outcome = EventDamage.Apply(Event(eventSave: SaveResult.SaveNegates), party,
                                        dice.Next, saveScore: (_, _) => 21);

        Assert.Equal([6, 100, 20], dice.Asked);
        Assert.False(outcome.Hits[0].Saved);
        Assert.Equal(4, outcome.Hits[0].Damage);
    }

    // ---- the THAC0 branch --------------------------------------------------------------------

    [Fact]
    public void A_thac0_miss_rolls_no_damage_dice_and_no_save()
    {
        var party = Roster(1, armorClass: 10);
        var dice = new Dice(9);

        var outcome = EventDamage.Apply(Event(eventSave: SaveResult.UseThac0, thac0: 20), party,
                                        dice.Next);

        // Target number is 20 - 10 = 10 and the damage roll sits inside the hit branch, so a miss
        // costs exactly one die. The whole branch rolls no saving throw at any point.
        Assert.Equal([20], dice.Asked);
        Assert.False(outcome.Hits[0].Hit);
        Assert.False(outcome.Hits[0].Saved);
        Assert.Equal(20, party.Members[0].HitPoints);
    }

    [Fact]
    public void A_thac0_hit_equals_the_target_number_and_then_rolls_damage()
    {
        var party = Roster(1, armorClass: 10);
        var dice = new Dice(10, 4);

        var outcome = EventDamage.Apply(Event(eventSave: SaveResult.UseThac0, thac0: 20), party,
                                        dice.Next);

        // `RollDice(20,1) >= need` -- equalling the target hits.
        Assert.Equal([20, 6], dice.Asked);
        Assert.True(outcome.Hits[0].Hit);
        Assert.Equal(16, party.Members[0].HitPoints);
    }

    [Fact]
    public void The_thac0_target_number_is_pinned_between_one_and_twenty()
    {
        // Far better than needed: max(catt, 1) leaves 1, so a natural 1 still hits. ToHit's own
        // floor would have collapsed this to 0 and made every roll hit -- the same, by luck --
        // but the ceiling below is where the two clamps visibly differ.
        var easy = Roster(1, armorClass: 10);
        var hit = EventDamage.Apply(Event(eventSave: SaveResult.UseThac0, thac0: -100), easy,
                                    new Dice(1, 4).Next);
        Assert.True(hit.Hits[0].Hit);

        // Hopeless: 100 - (-50) is 150, pinned to 20, so a natural 20 still lands. A trap can
        // never be made impossible to suffer.
        var hard = Roster(1, armorClass: -50);
        var miss = EventDamage.Apply(Event(eventSave: SaveResult.UseThac0, thac0: 100), hard,
                                     new Dice(19).Next);
        Assert.False(miss.Hits[0].Hit);

        var landed = EventDamage.Apply(Event(eventSave: SaveResult.UseThac0, thac0: 100),
                                       Roster(1, armorClass: -50), new Dice(20, 4).Next);
        Assert.True(landed.Hits[0].Hit);
    }

    [Fact]
    public void The_thac0_branch_ignores_spell_effects_on_armour_class()
    {
        var party = Roster(1, armorClass: 5);
        Bless(party.Members[0], "$CHAR_AC", -10);
        Assert.Equal(-5, party.Members[0].AdjustedArmorClass);

        var outcome = EventDamage.Apply(Event(eventSave: SaveResult.UseThac0, thac0: 15), party,
                                        new Dice(10, 4).Next);

        // GetEffectiveAC is stored value plus readied items; GetAdjAC is stored value plus spell
        // effects, and this branch calls the former. Against the adjusted -5 the target number
        // would be 20 and a 10 would miss.
        Assert.True(outcome.Hits[0].Hit);
        Assert.Equal(16, party.Members[0].HitPoints);
    }

    [Fact]
    public void A_caller_that_knows_the_readied_items_can_supply_the_real_armour_class()
    {
        var party = Roster(1, armorClass: 5);

        var outcome = EventDamage.Apply(Event(eventSave: SaveResult.UseThac0, thac0: 15), party,
                                        new Dice(10).Next, effectiveArmorClass: _ => -6);

        // 15 - (-6) is 21, pinned to 20: the injection point is the only way the port can see
        // readied-item protection, which lives on the design's item records.
        Assert.False(outcome.Hits[0].Hit);
    }

    // ---- applying the damage -----------------------------------------------------------------

    [Theory]
    [InlineData(CharacterStatus.Okay, true)]
    [InlineData(CharacterStatus.Running, true)]
    [InlineData(CharacterStatus.Unconscious, true)]
    [InlineData(CharacterStatus.Animated, true)]
    [InlineData(CharacterStatus.Dying, true)]
    [InlineData(CharacterStatus.Dead, false)]
    [InlineData(CharacterStatus.Fled, false)]
    [InlineData(CharacterStatus.Petrified, false)]
    [InlineData(CharacterStatus.Gone, false)]
    [InlineData(CharacterStatus.TempGone, false)]
    public void Damage_lands_on_five_statuses_and_no_others(CharacterStatus status, bool lands)
    {
        var character = Hero(hitPoints: 12, status: status);

        EventDamage.GiveCharacterDamage(character, 5);

        // Anything outside the five keeps its hit points exactly, already-dead included -- a
        // caller that assumes damage always applies kills things twice.
        Assert.Equal(lands ? 7 : 12, character.HitPoints);
    }

    [Fact]
    public void Hit_points_floor_at_minus_ten_and_death_clears_the_spell_effects()
    {
        var character = Hero(hitPoints: 5);
        Bless(character, "$CHAR_AC", -2);

        EventDamage.GiveCharacterDamage(character, 500);

        Assert.Equal(EventDamage.MinimumHitPoints, character.HitPoints);
        Assert.Equal(CharacterStatus.Dead, character.Status);
        // SetStatus is an inline setter with a side effect: `if (status==Dead)
        // m_spellEffects.RemoveAll()`. Only death does it.
        Assert.Equal(0, character.Effects.Count);
    }

    [Fact]
    public void Zero_hit_points_is_unconscious_and_below_zero_is_dying()
    {
        var unconscious = Hero(hitPoints: 5);
        EventDamage.GiveCharacterDamage(unconscious, 5);
        // The bands are <= -10 dead, < 0 dying, == 0 unconscious. Folding zero into dying would
        // make every knocked-out character bleed out.
        Assert.Equal(CharacterStatus.Unconscious, unconscious.Status);

        var dying = Hero(hitPoints: 5);
        EventDamage.GiveCharacterDamage(dying, 6);
        Assert.Equal(CharacterStatus.Dying, dying.Status);
        Assert.Equal(0, dying.Effects.Count);

        var dead = Hero(hitPoints: 5);
        EventDamage.GiveCharacterDamage(dead, 15);
        Assert.Equal(CharacterStatus.Dead, dead.Status);
    }

    [Fact]
    public void Dead_at_zero_collapses_the_unconscious_and_dying_bands()
    {
        var character = Hero(hitPoints: 5);

        EventDamage.GiveCharacterDamage(character, 5, deadAtZero: true);

        Assert.Equal(CharacterStatus.Dead, character.Status);
        Assert.Equal(0, character.HitPoints);
    }

    [Fact]
    public void Negative_damage_heals_but_cannot_overheal()
    {
        var character = Hero(hitPoints: 5, maxHitPoints: 12);

        // Not reachable from the event, whose gate refuses anything <= 0 -- but the function
        // itself has the ceiling precisely because other callers pass negatives.
        EventDamage.GiveCharacterDamage(character, -100);

        Assert.Equal(12, character.HitPoints);
        Assert.Equal(CharacterStatus.Okay, character.Status);
    }

    [Fact]
    public void The_status_bands_are_read_from_the_adjusted_hit_points()
    {
        var character = Hero(hitPoints: 5, maxHitPoints: 20);
        Bless(character, "$CHAR_HITPOINTS", 10);

        EventDamage.GiveCharacterDamage(character, 5);

        // The stored value took the damage; GetAdjHitPoints is what decides the consequence, so
        // the character is at zero stored hit points and still on its feet.
        Assert.Equal(0, character.HitPoints);
        Assert.Equal(CharacterStatus.Okay, character.Status);
    }

    [Fact]
    public void The_status_gate_reads_the_adjusted_status()
    {
        var character = Hero(hitPoints: 12, status: CharacterStatus.Dead);
        Bless(character, "$CHAR_STATUS", -2);
        Assert.Equal(CharacterStatus.Okay, EventDamage.AdjustedStatus(character));

        EventDamage.GiveCharacterDamage(character, 5);

        // The gate is GetAdjStatus(); the write afterwards goes to the stored field regardless,
        // which is why a dead character can be knocked unconscious.
        Assert.Equal(7, character.HitPoints);
    }

    [Fact]
    public void A_status_adjustment_outside_the_enum_is_discarded_rather_than_clamped()
    {
        var character = Hero(hitPoints: 12, status: CharacterStatus.Dead);
        Bless(character, "$CHAR_STATUS", 100);

        // Unlike every neighbouring GetAdj* accessor, this one reverts to the stored value rather
        // than clamping into range -- so +1 changes everything and +100 changes nothing.
        Assert.Equal(CharacterStatus.Dead, EventDamage.AdjustedStatus(character));
        EventDamage.GiveCharacterDamage(character, 5);
        Assert.Equal(12, character.HitPoints);
    }

    // ---- the outcome -------------------------------------------------------------------------

    [Fact]
    public void The_outcome_totals_the_damage_and_counts_the_deaths()
    {
        var party = Roster(2, hitPoints: 3, maxHitPoints: 3);
        var dice = new Dice(13, 1, 13, 1);

        var outcome = EventDamage.Apply(Event(sides: 20, qty: 1), party, dice.Next);

        Assert.Equal(26, outcome.TotalDamage);
        Assert.Equal(2, outcome.Deaths);
        Assert.All(outcome.Hits, hit => Assert.True(hit.Died));
        // 3 - 13 is -10 exactly, which is where the floor and the death band coincide.
        Assert.All(party.Members, member => Assert.Equal(-10, member.HitPoints));
    }

    [Fact]
    public void A_second_blow_reports_damage_the_corpse_did_not_take()
    {
        var party = Roster(1, hitPoints: 3, maxHitPoints: 3);
        var dice = new Dice(13, 1, 13, 1);

        var outcome = EventDamage.Apply(Event(sides: 20, qty: 1, attacks: 2), party, dice.Next);

        Assert.Equal(1, outcome.Deaths);
        // The reference formats "takes %i damage" inside `if (result > 0)` and only then calls
        // the half that checks status, so the announcement happens for a corpse that takes
        // nothing. Both attacks report 13; only the first moved any hit points.
        Assert.Equal(2, outcome.Hits.Count);
        Assert.Equal(13, outcome.Hits[1].Damage);
        Assert.False(outcome.Hits[1].Died);
        Assert.Equal(-10, party.Members[0].HitPoints);
    }

    [Fact]
    public void The_distance_field_changes_nothing()
    {
        var near = Roster(1);
        var far = Roster(1);

        EventDamage.Apply(Event(distance: EncounterDistance.UpClose), near, new Dice(4, 1).Next);
        EventDamage.Apply(Event(distance: EncounterDistance.FarAway), far, new Dice(4, 1).Next);

        // OnInitialEvent uses it for one thing -- which sprite frame to draw. OnKeypress never
        // reads it, so it is presentation and nothing else.
        Assert.Equal(near.Members[0].HitPoints, far.Members[0].HitPoints);
    }
}
