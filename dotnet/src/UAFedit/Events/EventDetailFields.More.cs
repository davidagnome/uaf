using UAF.Serialization;

namespace UAFedit.Events;

/// <summary>
/// The rest of the per-type tables — everything below Logic Block in corpus frequency, plus the
/// twelve types the two reference designs never use.
/// </summary>
/// <remarks>
/// Split from <see cref="EventDetailFields"/> only for length. The unused types are ported anyway
/// because "unused in two designs" is not "unused": <c>Damage</c> is the standard trap and
/// <c>Encounter</c> the standard wandering monster, and a third design would hit both immediately.
/// </remarks>
public static partial class EventDetailFields
{
    /// <summary><c>COMBAT_EVENT_DATA</c> — <c>IDD_COMBAT</c>, shared with Pick One Combat.</summary>
    /// <remarks>
    /// <b><c>turningMod</c> is not terrain.</b> An earlier revision of the reader named it that;
    /// it is an <c>eventTurnUndeadModType</c> and the widths matched, so nothing broke and nothing
    /// noticed. The monster roster is a dialog of its own (<c>IDD_CHOOSECOMBATMONSTER</c>) and
    /// needs the monster database to offer names, so it is listed and not edited.
    /// </remarks>
    private static EventDetail Combat(CombatEvent combat) => new(
        [
            Field.Choice<CombatEvent>("Distance", EventCatalog.Distance, e => e.Distance,
                (e, v) => e with { Distance = (int)v }),
            Field.Choice<CombatEvent>("Direction", EventCatalog.Direction, e => e.Direction,
                (e, v) => e with { Direction = (int)v }),
            Field.Choice<CombatEvent>("Surprise", EventCatalog.Surprise, e => e.Surprise,
                (e, v) => e with { Surprise = (int)v }),
            Field.Choice<CombatEvent>("Turning modifier", EventCatalog.TurnMod, e => e.TurningMod,
                (e, v) => e with { TurningMod = (int)v }),
            Field.Number<CombatEvent>("Monster morale", e => e.MonsterMorale,
                (e, v) => e with { MonsterMorale = (int)v }),
            Field.Flag<CombatEvent>("Auto approach", e => e.AutoApproach != 0,
                (e, v) => e with { AutoApproach = v ? 1 : 0 }),
            Field.Flag<CombatEvent>("Outdoors", e => e.Outdoors != 0,
                (e, v) => e with { Outdoors = v ? 1 : 0 }),
            Field.Flag<CombatEvent>("No monster treasure", e => e.NoMonsterTreasure != 0,
                (e, v) => e with { NoMonsterTreasure = v ? 1 : 0 }),
            Field.Flag<CombatEvent>("Party never dies", e => e.PartyNeverDies != 0,
                (e, v) => e with { PartyNeverDies = v ? 1 : 0 }),
            Field.Flag<CombatEvent>("No magic", e => e.NoMagic != 0,
                (e, v) => e with { NoMagic = v ? 1 : 0 }),
            Field.Flag<CombatEvent>("Random monster", e => e.RandomMonster != 0,
                (e, v) => e with { RandomMonster = v ? 1 : 0 }),
            Field.Flag<CombatEvent>("No experience", e => e.PartyNoExperience != 0,
                (e, v) => e with { PartyNoExperience = v ? 1 : 0 }),
            Field.Text<CombatEvent>("Death sound", e => e.DeathSound,
                (e, v) => e with { DeathSound = v }),
            Field.Text<CombatEvent>("Move sound", e => e.MoveSound,
                (e, v) => e with { MoveSound = v }),
            Field.Text<CombatEvent>("Turn undead sound", e => e.TurnUndeadSound,
                (e, v) => e with { TurnUndeadSound = v }),
        ],
        [
            new EventFieldGroup("Background sounds",
            [
                Field.Info<CombatEvent>("Day", e => Join(e.BackgroundSounds.Day)),
                Field.Info<CombatEvent>("Night", e => Join(e.BackgroundSounds.Night)),
                Field.Flag<CombatEvent>("Use night music", e => e.BackgroundSounds.UseNightMusic != 0,
                    (e, v) => e with
                    {
                        BackgroundSounds = e.BackgroundSounds with { UseNightMusic = v ? 1 : 0 },
                    }),
                Field.Number<CombatEvent>("Night starts", e => e.BackgroundSounds.StartTime,
                    (e, v) => e with
                    {
                        BackgroundSounds = e.BackgroundSounds with { StartTime = (int)v },
                    }),
                Field.Number<CombatEvent>("Night ends", e => e.BackgroundSounds.EndTime,
                    (e, v) => e with
                    {
                        BackgroundSounds = e.BackgroundSounds with { EndTime = (int)v },
                    }),
            ]),

            ..combat.Monsters.Select((_, i) => new EventFieldGroup($"Monster {i + 1}",
            [
                Field.Info<CombatEvent>("Monster", e => e.Monsters[i].MonsterId),
                Field.Info<CombatEvent>("Character", e => e.Monsters[i].CharacterId),
                Field.Info<CombatEvent>("Quantity", e => e.Monsters[i].UseQty != 0
                    ? e.Monsters[i].Quantity.ToString()
                    : $"{e.Monsters[i].QtyDiceQty}d{e.Monsters[i].QtyDiceSides}"
                      + Signed(e.Monsters[i].QtyBonus)),
                Field.Info<CombatEvent>("Friendly", e => Yes(e.Monsters[i].Friendly)),
                Field.Info<CombatEvent>("Items", e => $"{e.Monsters[i].Items.Items.Count}"),
            ])),
        ]);

    /// <summary>
    /// <c>GIVE_TREASURE_DATA</c> and <c>COMBAT_TREASURE</c> — <c>IDD_COMBATTREASURE</c> and
    /// <c>IDD_COMBAT_TREASURE</c>.
    /// </summary>
    /// <remarks>
    /// One record, two event types, two similarly named dialogs and two distinct wire layouts:
    /// combat treasure is money then items, give treasure adds the silent flag between them. The
    /// flag is therefore meaningless on a combat treasure and is shown anyway — the record carries
    /// it either way and hiding it would imply it was absent.
    /// </remarks>
    private static EventDetail Treasure(TreasureEvent treasure) => new(
        [
            Field.Flag<TreasureEvent>("Give silently to active character",
                e => e.SilentGiveToActiveChar != 0,
                (e, v) => e with { SilentGiveToActiveChar = v ? 1 : 0 }),
        ],
        [
            Money(treasure.Money),
            Items("Items", treasure.Items),
        ]);

    /// <summary><c>SOUND_EVENT</c> — <c>IDD_SOUNDEVENTDLG</c>, a queue of sound names.</summary>
    private static EventDetail Sound(SoundEvent sound) => new(
        [],
        [
            new EventFieldGroup("Sounds", [..sound.Sounds.Select((_, i) =>
                Field.Text<SoundEvent>($"Sound {i + 1}", e => e.Sounds[i],
                    (e, v) => e with { Sounds = Replace(e.Sounds, i, _ => v) }))]),
        ]);

    /// <summary><c>TRAININGHALL</c> — <c>IDD_TRAININGHALL</c>.</summary>
    private static EventDetail TrainingHall(TrainingHallEvent hall) => new(
        [
            Field.Number<TrainingHallEvent>("Cost", e => e.Cost, (e, v) => e with { Cost = (int)v }),
            Field.Flag<TrainingHallEvent>("Force exit", e => e.ForceExit != 0,
                (e, v) => e with { ForceExit = v ? 1 : 0 }),
        ],
        [..hall.Trainable.Select((_, i) => new EventFieldGroup($"Trainable {i + 1}",
        [
            Field.Text<TrainingHallEvent>("Baseclass", e => e.Trainable[i].BaseclassId,
                (e, v) => e with
                {
                    Trainable = Replace(e.Trainable, i, t => t with { BaseclassId = v }),
                }),
            Field.Number<TrainingHallEvent>("Min level", e => e.Trainable[i].MinLevel,
                (e, v) => e with
                {
                    Trainable = Replace(e.Trainable, i, t => t with { MinLevel = (int)v }),
                }),
            Field.Number<TrainingHallEvent>("Max level", e => e.Trainable[i].MaxLevel,
                (e, v) => e with
                {
                    Trainable = Replace(e.Trainable, i, t => t with { MaxLevel = (int)v }),
                }),
            Field.Text<TrainingHallEvent>("Notes", e => e.Trainable[i].Notes,
                (e, v) => e with
                {
                    Trainable = Replace(e.Trainable, i, t => t with { Notes = v }),
                }),
        ]))]);

    /// <summary><c>GAIN_EXP_DATA</c> — <c>IDD_GIVEEXPERIENCE</c>.</summary>
    private static EventDetail GainExperience { get; } = EventDetail.Of(
        Field.Number<GainExperienceEvent>("Experience", e => e.Experience,
            (e, v) => e with { Experience = (int)v }),
        Field.Choice<GainExperienceEvent>("Affects", EventCatalog.AffectsWho, e => e.Who,
            (e, v) => e with { Who = (int)v }),
        Field.Number<GainExperienceEvent>("Chance %", e => e.Chance,
            (e, v) => e with { Chance = (int)v }, 0, 100),
        Field.Text<GainExperienceEvent>("Sound", e => e.Sound, (e, v) => e with { Sound = v }));

    /// <summary><c>CAMP_EVENT_DATA</c> — <c>IDD_CAMPEVENT</c>, a single flag.</summary>
    private static EventDetail Camp { get; } = EventDetail.Of(
        Field.Flag<CampEvent>("Force exit", e => e.ForceExit != 0,
            (e, v) => e with { ForceExit = v ? 1 : 0 }));

    /// <summary><c>TEMPLE</c> — <c>IDD_CHURCH</c>.</summary>
    /// <remarks>
    /// <c>totalDonation</c> is a running total the engine writes back, not a design setting: it
    /// accumulates what the party has given so the donation chain can fire once past
    /// <c>donationTrigger</c>. Editing it is legitimate — designs ship it non-zero — but it is a
    /// saved-state field living in design data.
    /// </remarks>
    private static EventDetail Temple(TempleEvent temple) => new(
        [
            Field.Flag<TempleEvent>("Force exit", e => e.ForceExit != 0,
                (e, v) => e with { ForceExit = v ? 1 : 0 }),
            Field.Flag<TempleEvent>("Allow donations", e => e.AllowDonations != 0,
                (e, v) => e with { AllowDonations = v ? 1 : 0 }),
            Field.Choice<TempleEvent>("Cost factor", EventCatalog.CostFactor, e => e.CostFactor,
                (e, v) => e with { CostFactor = (int)v }),
            Field.Number<TempleEvent>("Max level", e => e.MaxLevel,
                (e, v) => e with { MaxLevel = (int)v }),
            Field.Number<TempleEvent>("Donation trigger", e => e.DonationTrigger,
                (e, v) => e with { DonationTrigger = (int)v }),
            Field.Chain<TempleEvent>("Donation chain", e => e.DonationChain,
                (e, v) => e with { DonationChain = (uint)v }),
            Field.Number<TempleEvent>("Total donated so far", e => e.TotalDonation,
                (e, v) => e with { TotalDonation = (int)v }),
        ],
        [
            new EventFieldGroup("Temple spells",
            [
                Field.Info<TempleEvent>("Use limits",
                    e => Yes(e.TempleSpells.UseLimits)),
                ..temple.TempleSpells.Spells.Select((_, i) =>
                    Field.Info<TempleEvent>($"Spell {i + 1}",
                        e => $"{e.TempleSpells.Spells[i].SpellId} "
                             + $"(level {e.TempleSpells.Spells[i].Level})")),
            ]),
        ]);

    /// <summary><c>ADD_NPC_DATA</c> — <c>IDD_ADDNPC</c>.</summary>
    private static EventDetail AddNpc { get; } = EventDetail.Of(
        Field.Text<AddNpcEvent>("Character", e => e.CharacterId,
            (e, v) => e with { CharacterId = v }),
        Field.Number<AddNpcEvent>("Operation", e => e.Operation,
            (e, v) => e with { Operation = (int)v }),
        Field.Number<AddNpcEvent>("Hit point modifier", e => e.HitPointMod,
            (e, v) => e with { HitPointMod = (int)v }),
        Field.Flag<AddNpcEvent>("Use original", e => e.UseOriginal != 0,
            (e, v) => e with { UseOriginal = v ? 1 : 0 }));

    /// <summary><c>RANDOM_EVENT_DATA</c> — <c>IDD_RANDOMEVENT</c>.</summary>
    /// <remarks>
    /// The chances are <c>BYTE</c>s and the original does not enforce that they sum to 100.
    /// </remarks>
    private static EventDetail Random(RandomEvent random) => new(
        [],
        [..random.Branches.Select((_, i) => new EventFieldGroup($"Branch {i + 1}",
        [
            Field.Number<RandomEvent>("Chance %", e => e.Branches[i].Chance,
                (e, v) => e with
                {
                    Branches = Replace(e.Branches, i, b => b with { Chance = (byte)v }),
                }, 0, 255),
            Field.Chain<RandomEvent>("Chain", e => e.Branches[i].Chain,
                (e, v) => e with
                {
                    Branches = Replace(e.Branches, i, b => b with { Chain = (uint)v }),
                }),
        ]))]);

    /// <summary><c>SHOP</c> — <c>IDD_SHOP</c>.</summary>
    private static EventDetail Shop(ShopEvent shop) => new(
        [
            Field.Flag<ShopEvent>("Force exit", e => e.ForceExit != 0,
                (e, v) => e with { ForceExit = v ? 1 : 0 }),
            Field.Choice<ShopEvent>("Cost factor", EventCatalog.CostFactor, e => e.CostFactor,
                (e, v) => e with { CostFactor = (int)v }),
            Field.Number<ShopEvent>("Buyback %", e => e.BuybackPercentage,
                (e, v) => e with { BuybackPercentage = (int)v }),
            Field.Number<ShopEvent>("Cost to identify", e => e.CostToIdentify,
                (e, v) => e with { CostToIdentify = (int)v }),
            Field.Flag<ShopEvent>("Can identify", e => e.CanIdentify != 0,
                (e, v) => e with { CanIdentify = v ? 1 : 0 }),
            Field.Flag<ShopEvent>("Can appraise gems", e => e.CanAppraiseGems != 0,
                (e, v) => e with { CanAppraiseGems = v ? 1 : 0 }),
            Field.Flag<ShopEvent>("Can appraise jewellery", e => e.CanAppraiseJewels != 0,
                (e, v) => e with { CanAppraiseJewels = v ? 1 : 0 }),
            Field.Flag<ShopEvent>("Buy back only what it sold", e => e.BuyItemsSoldOnly != 0,
                (e, v) => e with { BuyItemsSoldOnly = v ? 1 : 0 }),
        ],
        [Items("Stock", shop.ItemsAvailable)]);

    /// <summary><c>WHO_PAYS_EVENT_DATA</c> — <c>IDD_WHOPAYSDLG</c>.</summary>
    private static EventDetail WhoPays { get; } = new(
        [
            Field.Flag<WhoPaysEvent>("Impossible", e => e.Impossible != 0,
                (e, v) => e with { Impossible = v ? 1 : 0 }),
            Field.Number<WhoPaysEvent>("Platinum", e => e.Platinum,
                (e, v) => e with { Platinum = (int)v }),
            Field.Number<WhoPaysEvent>("Gems", e => e.Gems, (e, v) => e with { Gems = (int)v }),
            Field.Number<WhoPaysEvent>("Jewellery", e => e.Jewels,
                (e, v) => e with { Jewels = (int)v }),
            Field.Number<WhoPaysEvent>("Money type", e => e.MoneyType,
                (e, v) => e with { MoneyType = (int)v }),
            Field.Choice<WhoPaysEvent>("On success", EventCatalog.PasswordAction,
                e => e.SuccessAction, (e, v) => e with { SuccessAction = (int)v }),
            Field.Chain<WhoPaysEvent>("Success chain", e => e.SuccessChain,
                (e, v) => e with { SuccessChain = (uint)v }),
            Field.Choice<WhoPaysEvent>("On failure", EventCatalog.PasswordAction,
                e => e.FailAction, (e, v) => e with { FailAction = (int)v }),
            Field.Chain<WhoPaysEvent>("Fail chain", e => e.FailChain,
                (e, v) => e with { FailChain = (uint)v }),
        ],
        [
            new EventFieldGroup("Success transfer", TransferFields<WhoPaysEvent>(
                e => e.SuccessTransfer, (e, t) => e with { SuccessTransfer = t })),
            new EventFieldGroup("Fail transfer", TransferFields<WhoPaysEvent>(
                e => e.FailTransfer, (e, t) => e with { FailTransfer = t })),
        ]);

    /// <summary><c>TAVERN</c> — <c>IDD_TAVERN</c>.</summary>
    /// <remarks>
    /// Each tale carries a told-count that the engine increments, which is why
    /// <c>eachTaleOnceOnly</c> works at all — the state is in the design file, not the save.
    /// </remarks>
    private static EventDetail Tavern(TavernEvent tavern) => new(
        [
            Field.Flag<TavernEvent>("Force exit", e => e.ForceExit != 0,
                (e, v) => e with { ForceExit = v ? 1 : 0 }),
            Field.Text<TavernEvent>("Barkeep", e => e.Barkeep.ToString(),
                (e, v) => int.TryParse(v, out int b) ? e with { Barkeep = b } : e),
            Field.Number<TavernEvent>("Inflation %", e => e.Inflation,
                (e, v) => e with { Inflation = (int)v }),
            Field.Flag<TavernEvent>("Allow fights", e => e.AllowFights != 0,
                (e, v) => e with { AllowFights = v ? 1 : 0 }),
            Field.Chain<TavernEvent>("Fight chain", e => e.FightChain,
                (e, v) => e with { FightChain = (uint)v }),
            Field.Flag<TavernEvent>("Allow drinks", e => e.AllowDrinks != 0,
                (e, v) => e with { AllowDrinks = v ? 1 : 0 }),
            Field.Number<TavernEvent>("Drink point trigger", e => e.DrinkPointTrigger,
                (e, v) => e with { DrinkPointTrigger = (int)v }),
            Field.Chain<TavernEvent>("Drunk chain", e => e.DrinkChain,
                (e, v) => e with { DrinkChain = (uint)v }),
            Field.Choice<TavernEvent>("Tale order", EventCatalog.TaleOrder, e => e.TaleOrder,
                (e, v) => e with { TaleOrder = (int)v }),
            Field.Flag<TavernEvent>("Each tale once only", e => e.EachTaleOnceOnly != 0,
                (e, v) => e with { EachTaleOnceOnly = v ? 1 : 0 }),
        ],
        [
            new EventFieldGroup("Tales", [..tavern.Tales.Select((_, i) =>
                Field.Paragraph<TavernEvent>($"Tale {i + 1} (told {tavern.Tales[i].Count}x)",
                    e => e.Tales[i].Text,
                    (e, v) => e with { Tales = Replace(e.Tales, i, t => t with { Text = v }) }))]),

            new EventFieldGroup("Drinks", [..tavern.Drinks.SelectMany((_, i) => new[]
            {
                Field.Text<TavernEvent>($"Drink {i + 1}", e => e.Drinks[i].Name,
                    (e, v) => e with { Drinks = Replace(e.Drinks, i, d => d with { Name = v }) }),
                Field.Number<TavernEvent>($"Drink {i + 1} points", e => e.Drinks[i].Points,
                    (e, v) => e with
                    {
                        Drinks = Replace(e.Drinks, i, d => d with { Points = (int)v }),
                    }),
            })]),
        ]);

    /// <summary><c>NPC_SAYS_DATA</c> — <c>IDD_NPCSAYS</c>.</summary>
    private static EventDetail NpcSays { get; } = EventDetail.Of(
        Field.Text<NpcSaysEvent>("Character", e => e.CharacterId,
            (e, v) => e with { CharacterId = v }),
        Field.Choice<NpcSaysEvent>("Distance", EventCatalog.Distance, e => e.Distance,
            (e, v) => e with { Distance = (int)v }),
        Field.Text<NpcSaysEvent>("Sound", e => e.Sound, (e, v) => e with { Sound = v }),
        Field.Flag<NpcSaysEvent>("Must hit RETURN", e => e.MustHitReturn != 0,
            (e, v) => e with { MustHitReturn = v ? 1 : 0 }),
        Field.Flag<NpcSaysEvent>("Highlight", e => e.Highlight != 0,
            (e, v) => e with { Highlight = v ? 1 : 0 }));

    /// <summary><c>REMOVE_NPC_DATA</c> — <c>IDD_REMOVENPC</c>.</summary>
    private static EventDetail RemoveNpc { get; } = EventDetail.Of(
        Field.Text<RemoveNpcEvent>("Character", e => e.CharacterId,
            (e, v) => e with { CharacterId = v }),
        Field.Choice<RemoveNpcEvent>("Distance", EventCatalog.Distance, e => e.Distance,
            (e, v) => e with { Distance = (int)v }));

    /// <summary><c>GIVE_DAMAGE_DATA</c> — <c>IDD_DAMAGE</c>, the standard trap.</summary>
    private static EventDetail Damage { get; } = EventDetail.Of(
        Field.Number<DamageEvent>("Attacks", e => e.NbrAttacks,
            (e, v) => e with { NbrAttacks = (int)v }),
        Field.Number<DamageEvent>("Chance per attack %", e => e.ChancePerAttack,
            (e, v) => e with { ChancePerAttack = (int)v }, 0, 100),
        Field.Number<DamageEvent>("Damage dice", e => e.DmgDiceQty,
            (e, v) => e with { DmgDiceQty = (int)v }),
        Field.Number<DamageEvent>("Damage sides", e => e.DmgDice,
            (e, v) => e with { DmgDice = (int)v }),
        Field.Number<DamageEvent>("Damage bonus", e => e.DmgBonus,
            (e, v) => e with { DmgBonus = (int)v }),
        Field.Number<DamageEvent>("Attack THAC0", e => e.AttackThac0,
            (e, v) => e with { AttackThac0 = (int)v }),
        Field.Number<DamageEvent>("Save bonus", e => e.SaveBonus,
            (e, v) => e with { SaveBonus = (int)v }),
        Field.Choice<DamageEvent>("Save versus", EventCatalog.SaveVersus, e => e.SpellSave,
            (e, v) => e with { SpellSave = (int)v }),
        Field.Choice<DamageEvent>("Save effect", EventCatalog.SaveEffect, e => e.EventSave,
            (e, v) => e with { EventSave = (int)v }),
        Field.Choice<DamageEvent>("Affects", EventCatalog.AffectsWho, e => e.Who,
            (e, v) => e with { Who = (int)v }),
        Field.Choice<DamageEvent>("Distance", EventCatalog.Distance, e => e.Distance,
            (e, v) => e with { Distance = (int)v }));

    /// <summary><c>HEAL_PARTY_DATA</c> — <c>IDD_HEALPARTY</c>.</summary>
    private static EventDetail HealParty { get; } = EventDetail.Of(
        Field.Flag<HealPartyEvent>("Heal hit points", e => e.HealHitPoints != 0,
            (e, v) => e with { HealHitPoints = v ? 1 : 0 }),
        Field.Number<HealPartyEvent>("How much", e => e.HowMuchHp,
            (e, v) => e with { HowMuchHp = (int)v }),
        Field.Choice<HealPartyEvent>("Interpreted as", EventCatalog.LiteralOrPercent,
            e => e.LiteralOrPercent, (e, v) => e with { LiteralOrPercent = (byte)v }),
        Field.Flag<HealPartyEvent>("Restore drained levels", e => e.HealDrain != 0,
            (e, v) => e with { HealDrain = v ? 1 : 0 }),
        Field.Flag<HealPartyEvent>("Remove curses", e => e.HealCurse != 0,
            (e, v) => e with { HealCurse = v ? 1 : 0 }),
        Field.Number<HealPartyEvent>("Chance %", e => e.Chance,
            (e, v) => e with { Chance = (byte)v }, 0, 100),
        Field.Choice<HealPartyEvent>("Affects", EventCatalog.AffectsWho, e => e.Who,
            (e, v) => e with { Who = (int)v }));

    /// <summary><c>TAKE_PARTY_ITEMS_DATA</c> — <c>IDD_TAKEITEMS</c>.</summary>
    /// <remarks>
    /// <c>takeItems</c> is a mask over four independent decisions and each has its own quantity
    /// mode, so the four <c>*SelectFlags</c> are only meaningful when the matching bit is set.
    /// </remarks>
    private static EventDetail TakePartyItems(TakePartyItemsEvent take) => new(
        [
            Field.Flag<TakePartyItemsEvent>("Take inventory", e => (e.TakeItems & 1) != 0,
                (e, v) => e with { TakeItems = (byte)(v ? e.TakeItems | 1 : e.TakeItems & ~1) }),
            Field.Flag<TakePartyItemsEvent>("Take money", e => (e.TakeItems & 2) != 0,
                (e, v) => e with { TakeItems = (byte)(v ? e.TakeItems | 2 : e.TakeItems & ~2) }),
            Field.Flag<TakePartyItemsEvent>("Take gems", e => (e.TakeItems & 4) != 0,
                (e, v) => e with { TakeItems = (byte)(v ? e.TakeItems | 4 : e.TakeItems & ~4) }),
            Field.Flag<TakePartyItemsEvent>("Take jewellery", e => (e.TakeItems & 8) != 0,
                (e, v) => e with { TakeItems = (byte)(v ? e.TakeItems | 8 : e.TakeItems & ~8) }),
            Field.Choice<TakePartyItemsEvent>("Affects", EventCatalog.TakeAffects,
                e => e.TakeAffects, (e, v) => e with { TakeAffects = (int)v }),
            Field.Choice<TakePartyItemsEvent>("Inventory quantity", EventCatalog.TakeQuantity,
                e => e.ItemSelectFlags, (e, v) => e with { ItemSelectFlags = (int)v }),
            Field.Number<TakePartyItemsEvent>("Inventory percent", e => e.ItemPercent,
                (e, v) => e with { ItemPercent = (int)v }, 0, 100),
            Field.Choice<TakePartyItemsEvent>("Money quantity", EventCatalog.TakeQuantity,
                e => e.PlatinumSelectFlags, (e, v) => e with { PlatinumSelectFlags = (int)v }),
            Field.Number<TakePartyItemsEvent>("Money amount", e => e.Platinum,
                (e, v) => e with { Platinum = (int)v }),
            Field.Number<TakePartyItemsEvent>("Money type", e => e.MoneyType,
                (e, v) => e with { MoneyType = (int)v }),
            Field.Choice<TakePartyItemsEvent>("Gems quantity", EventCatalog.TakeQuantity,
                e => e.GemsSelectFlags, (e, v) => e with { GemsSelectFlags = (int)v }),
            Field.Number<TakePartyItemsEvent>("Gems amount", e => e.Gems,
                (e, v) => e with { Gems = (int)v }),
            Field.Choice<TakePartyItemsEvent>("Jewellery quantity", EventCatalog.TakeQuantity,
                e => e.JewelrySelectFlags, (e, v) => e with { JewelrySelectFlags = (int)v }),
            Field.Number<TakePartyItemsEvent>("Jewellery amount", e => e.Jewelry,
                (e, v) => e with { Jewelry = (int)v }),
            Field.Flag<TakePartyItemsEvent>("Store in vault", e => e.StoreItems != 0,
                (e, v) => e with { StoreItems = v ? 1 : 0 }),
            Field.Number<TakePartyItemsEvent>("Which vault", e => e.WhichVault,
                (e, v) => e with { WhichVault = (byte)v }, 0, byte.MaxValue),
            Field.Flag<TakePartyItemsEvent>("Must hit RETURN", e => e.MustHitReturn != 0,
                (e, v) => e with { MustHitReturn = v ? 1 : 0 }),
        ],
        [Items("Named items", take.Items)]);

    /// <summary><c>PASSWORD_DATA</c> — <c>IDD_PASSWORD</c>.</summary>
    private static EventDetail Password { get; } = new(
        [
            Field.Text<PasswordEvent>("Password", e => e.Password,
                (e, v) => e with { Password = v }),
            Field.Number<PasswordEvent>("Tries", e => e.NbrTries,
                (e, v) => e with { NbrTries = (int)v }),
            Field.Choice<PasswordEvent>("On success", EventCatalog.PasswordAction,
                e => e.SuccessAction, (e, v) => e with { SuccessAction = (int)v }),
            Field.Chain<PasswordEvent>("Success chain", e => e.SuccessChain,
                (e, v) => e with { SuccessChain = (uint)v }),
            Field.Choice<PasswordEvent>("On failure", EventCatalog.PasswordAction,
                e => e.FailAction, (e, v) => e with { FailAction = (int)v }),
            Field.Chain<PasswordEvent>("Fail chain", e => e.FailChain,
                (e, v) => e with { FailChain = (uint)v }),
        ],
        [
            new EventFieldGroup("Success transfer", TransferFields<PasswordEvent>(
                e => e.SuccessTransfer, (e, t) => e with { SuccessTransfer = t })),
            new EventFieldGroup("Fail transfer", TransferFields<PasswordEvent>(
                e => e.FailTransfer, (e, t) => e with { FailTransfer = t })),
        ]);

    /// <summary><c>WHO_TRIES_EVENT_DATA</c> — <c>IDD_WHOTRIES</c>.</summary>
    /// <remarks>
    /// <c>compareToDie</c> being clear means <c>compareDie</c> is the target number outright
    /// rather than a die to roll, which inverts what the field means without renaming it.
    /// </remarks>
    private static EventDetail WhoTries(WhoTriesEvent tries) => new(
        [
            Field.Flag<WhoTriesEvent>("Always succeeds", e => e.AlwaysSucceeds != 0,
                (e, v) => e with { AlwaysSucceeds = v ? 1 : 0 }),
            Field.Flag<WhoTriesEvent>("Always fails", e => e.AlwaysFails != 0,
                (e, v) => e with { AlwaysFails = v ? 1 : 0 }),
            Field.Flag<WhoTriesEvent>("Compare to a die roll", e => e.CompareToDie != 0,
                (e, v) => e with { CompareToDie = v ? 1 : 0 }),
            Field.Number<WhoTriesEvent>("Die / target", e => e.CompareDie,
                (e, v) => e with { CompareDie = (int)v }),
            Field.Number<WhoTriesEvent>("Strength bonus", e => e.StrengthBonus,
                (e, v) => e with { StrengthBonus = (byte)v }, 0, byte.MaxValue),
            Field.Number<WhoTriesEvent>("Tries", e => e.NbrTries,
                (e, v) => e with { NbrTries = (int)v }),
            Field.Info<WhoTriesEvent>("Ability checks", e => Checked(e.AbilityChecks)),
            Field.Info<WhoTriesEvent>("Thief skill checks", e => Checked(e.ThiefSkillChecks)),
            Field.Choice<WhoTriesEvent>("On success", EventCatalog.PasswordAction,
                e => e.SuccessAction, (e, v) => e with { SuccessAction = (int)v }),
            Field.Chain<WhoTriesEvent>("Success chain", e => e.SuccessChain,
                (e, v) => e with { SuccessChain = (uint)v }),
            Field.Choice<WhoTriesEvent>("On failure", EventCatalog.PasswordAction,
                e => e.FailAction, (e, v) => e with { FailAction = (int)v }),
            Field.Chain<WhoTriesEvent>("Fail chain", e => e.FailChain,
                (e, v) => e with { FailChain = (uint)v }),
        ],
        [
            new EventFieldGroup("Success transfer", TransferFields<WhoTriesEvent>(
                e => e.SuccessTransfer, (e, t) => e with { SuccessTransfer = t })),
            new EventFieldGroup("Fail transfer", TransferFields<WhoTriesEvent>(
                e => e.FailTransfer, (e, t) => e with { FailTransfer = t })),
        ]);

    /// <summary><c>ENCOUNTER_DATA</c> — <c>IDD_ENCOUNTER</c>.</summary>
    /// <remarks>
    /// <c>allowedUpClose</c> and <c>onlyUpClose</c> are not each other's inverse: an option can be
    /// available at all ranges, only at range, or only up close, which is three states out of two
    /// flags.
    /// </remarks>
    private static EventDetail Encounter(EncounterEvent encounter) => new(
        [
            Field.Choice<EncounterEvent>("Distance", EventCatalog.Distance, e => e.Distance,
                (e, v) => e with { Distance = (int)v }),
            Field.Number<EncounterEvent>("Monster speed", e => e.MonsterSpeed,
                (e, v) => e with { MonsterSpeed = (int)v }),
            Field.Choice<EncounterEvent>("At zero range", EventCatalog.EncounterButton,
                e => e.ZeroRangeResult, (e, v) => e with { ZeroRangeResult = (int)v }),
            Field.Chain<EncounterEvent>("Combat chain", e => e.CombatChain,
                (e, v) => e with { CombatChain = (uint)v }),
            Field.Chain<EncounterEvent>("Talk chain", e => e.TalkChain,
                (e, v) => e with { TalkChain = (uint)v }),
            Field.Chain<EncounterEvent>("Escape chain", e => e.EscapeChain,
                (e, v) => e with { EscapeChain = (uint)v }),
            Field.Number<EncounterEvent>("Buttons", e => e.NumButtons,
                (e, v) => e with { NumButtons = (int)v }, 0, 5),
        ],
        [..encounter.Options.Select((_, i) => new EventFieldGroup($"Button {i + 1}",
        [
            Field.Text<EncounterEvent>("Label", e => e.Options[i].Label,
                (e, v) => e with { Options = Replace(e.Options, i, o => o with { Label = v }) }),
            Field.Flag<EncounterEvent>("Present", e => e.Options[i].Present != 0,
                (e, v) => e with
                {
                    Options = Replace(e.Options, i, o => o with { Present = v ? 1 : 0 }),
                }),
            Field.Choice<EncounterEvent>("Result", EventCatalog.EncounterButton,
                e => e.Options[i].OptionResult,
                (e, v) => e with
                {
                    Options = Replace(e.Options, i, o => o with { OptionResult = (int)v }),
                }),
            Field.Flag<EncounterEvent>("Allowed up close", e => e.Options[i].AllowedUpClose != 0,
                (e, v) => e with
                {
                    Options = Replace(e.Options, i, o => o with { AllowedUpClose = v ? 1 : 0 }),
                }),
            Field.Flag<EncounterEvent>("Only up close", e => e.Options[i].OnlyUpClose != 0,
                (e, v) => e with
                {
                    Options = Replace(e.Options, i, o => o with { OnlyUpClose = v ? 1 : 0 }),
                }),
            Field.Chain<EncounterEvent>("Chain", e => e.Options[i].Chain,
                (e, v) => e with
                {
                    Options = Replace(e.Options, i, o => o with { Chain = (uint)v }),
                }),
        ]))]);

    /// <summary><c>JOURNAL_EVENT</c> — <c>IDD_JOURNALEVENT</c>.</summary>
    /// <remarks>
    /// The entry is a number into the design's journal text, edited elsewhere
    /// (<c>IDD_JOURNALEDITOR</c>) — the event only names it.
    /// </remarks>
    private static EventDetail Journal { get; } = EventDetail.Of(
        Field.Number<JournalEvent>("Journal entry", e => e.Entry,
            (e, v) => e with { Entry = (int)v }));

    /// <summary><c>PLAY_MOVIE_DATA</c> — <c>IDD_PLAYMOVIEFULLSCREEN</c>.</summary>
    private static EventDetail PlayMovie { get; } = EventDetail.Of(
        Field.Text<PlayMovieEvent>("File", e => e.FileName, (e, v) => e with { FileName = v }),
        Field.Number<PlayMovieEvent>("Mode", e => e.Mode, (e, v) => e with { Mode = (int)v }));

    /// <summary><c>VAULT_EVENT_DATA</c> — <c>IDD_VAULTDATADIALOG</c>.</summary>
    private static EventDetail Vault { get; } = EventDetail.Of(
        Field.Number<VaultEvent>("Which vault", e => e.WhichVault,
            (e, v) => e with { WhichVault = (byte)v }, 0, byte.MaxValue),
        Field.Flag<VaultEvent>("Force backup", e => e.ForceBackup != 0,
            (e, v) => e with { ForceBackup = v ? 1 : 0 }));

    /// <summary><c>SMALL_TOWN_DATA</c> — <c>IDD_SMALLTOWN</c>, six chains and nothing else.</summary>
    /// <remarks>
    /// A destination that names no event does not fall back on the town's own chain: picking SHOP
    /// in a town with no shop leaves the player where they were (<c>RunEvent.cpp:4571</c>). So a
    /// zero here is a dead menu entry, not an exit.
    /// </remarks>
    private static EventDetail SmallTown { get; } = EventDetail.Of(
        Field.Chain<SmallTownEvent>("Temple", e => e.TempleChain,
            (e, v) => e with { TempleChain = (uint)v }),
        Field.Chain<SmallTownEvent>("Training hall", e => e.TrainingHallChain,
            (e, v) => e with { TrainingHallChain = (uint)v }),
        Field.Chain<SmallTownEvent>("Shop", e => e.ShopChain,
            (e, v) => e with { ShopChain = (uint)v }),
        Field.Chain<SmallTownEvent>("Inn", e => e.InnChain,
            (e, v) => e with { InnChain = (uint)v }),
        Field.Chain<SmallTownEvent>("Tavern (menu says PUB)", e => e.TavernChain,
            (e, v) => e with { TavernChain = (uint)v }),
        Field.Chain<SmallTownEvent>("Vault", e => e.VaultChain,
            (e, v) => e with { VaultChain = (uint)v }));

    /// <summary><c>TAVERN_TALES</c> — <c>IDD_TAVERNTALES</c>, obsolete but still readable.</summary>
    /// <remarks>
    /// Folded into <see cref="EventType.TavernEvent"/>; a design containing one predates that.
    /// Each tale carries its own flags and its own ASL, which the plain tavern's do not.
    /// </remarks>
    private static EventDetail TavernTales(TavernTalesEvent tales) => new(
        [Field.Info<TavernTalesEvent>("Flags",
            e => EventCatalog.Flags(EventCatalog.TavernTaleFlags, e.Flags))],
        [..tales.Tales.Select((_, i) => new EventFieldGroup($"Tale {i + 1}",
        [
            Field.Paragraph<TavernTalesEvent>("Text", e => e.Tales[i].Text,
                (e, v) => e with { Tales = Replace(e.Tales, i, t => t with { Text = v }) }),
            Field.Info<TavernTalesEvent>("Flags",
                e => EventCatalog.Flags(EventCatalog.TavernTaleFlags, e.Tales[i].Flags)),
            Field.Info<TavernTalesEvent>("Attributes",
                e => e.Tales[i].Attributes.Count.ToString()),
        ]))]);

    /// <summary><c>FLOW_CONTROL_EVENT_DATA</c> — <c>IDD_FlowControl</c>.</summary>
    /// <remarks>
    /// The odd one out: it is a goto/call/return machine over named markers, with a global variable
    /// on the side. Its four enumerations all reserve index 0 for the literal string "illegal", so
    /// a stored 0 is a defect rather than a default.
    /// </remarks>
    private static EventDetail FlowControl { get; } = EventDetail.Of(
        Field.Choice<FlowControlEvent>("Action", EventCatalog.FlowAction, e => e.Action,
            (e, v) => e with { Action = (int)v }),
        Field.Choice<FlowControlEvent>("Condition", EventCatalog.FlowCondition,
            e => e.ActionCondition, (e, v) => e with { ActionCondition = (int)v }),
        Field.Text<FlowControlEvent>("Entry marker", e => e.EntryMarker,
            (e, v) => e with { EntryMarker = v }),
        Field.Text<FlowControlEvent>("Exit marker", e => e.ExitMarker,
            (e, v) => e with { ExitMarker = v }),
        Field.Text<FlowControlEvent>("Destination marker", e => e.DestinationMarker,
            (e, v) => e with { DestinationMarker = v }),
        Field.Chain<FlowControlEvent>("Destination id", e => e.DestinationId,
            (e, v) => e with { DestinationId = (uint)v }),
        Field.Text<FlowControlEvent>("Global variable", e => e.GlobalVariableName,
            (e, v) => e with { GlobalVariableName = v }),
        Field.Text<FlowControlEvent>("Value", e => e.Value, (e, v) => e with { Value = v }),
        Field.Choice<FlowControlEvent>("Value modification", EventCatalog.ValueModification,
            e => e.ValueModification, (e, v) => e with { ValueModification = (int)v }),
        Field.Flag<FlowControlEvent>("Local chain only", e => (e.Flags & 1) != 0,
            (e, v) => e with { Flags = v ? e.Flags | 1u : e.Flags & ~1u }),
        Field.Number<FlowControlEvent>("Version", e => e.Version,
            (e, v) => e with { Version = (int)v }));

    /// <summary>A <c>MONEY_SACK</c> rendered as rows.</summary>
    /// <remarks>
    /// Ten coin slots, always, because <c>MAX_COIN_TYPES</c> is compile-time
    /// (<see cref="MonsterLeafReaders.MaxCoinTypes"/>). The design's own coin names live in the
    /// money data, not here, so the slots are numbered.
    /// </remarks>
    private static EventFieldGroup Money(MoneySack money) => new("Money",
    [
        ..money.Coins.Select((amount, i) =>
            Field.Info($"Coin type {i}", _ => amount.ToString())),
        Field.Info("Gems", _ => money.Gems.Count.ToString()),
        Field.Info("Jewellery", _ => money.Jewelry.Count.ToString()),
    ]);

    /// <summary>An <c>ITEM_LIST</c> rendered as rows.</summary>
    private static EventFieldGroup Items(string label, ItemList items) => new(label,
    [
        ..items.Items.Select((item, i) => Field.Info($"Item {i + 1}",
            _ => $"{item.ItemId} x{item.Quantity}"
                 + (item.Identified != 0 ? ", identified" : string.Empty)
                 + (item.Cursed != 0 ? ", cursed" : string.Empty))),
    ]);

    /// <summary>Which entries of a flag array are set, by index.</summary>
    private static string Checked(IReadOnlyList<int> flags)
    {
        var set = flags.Select((value, index) => (value, index))
                       .Where(pair => pair.value != 0)
                       .Select(pair => pair.index.ToString())
                       .ToList();

        return set.Count > 0 ? string.Join(", ", set) : "none";
    }

    private static string Join(IReadOnlyList<string> values)
    {
        var present = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

        return present.Count > 0 ? string.Join(", ", present) : "none";
    }

    private static string Yes(int flag) => flag != 0 ? "yes" : "no";

    private static string Signed(int bonus) => bonus switch
    {
        0 => string.Empty,
        > 0 => $"+{bonus}",
        _ => bonus.ToString(),
    };
}
