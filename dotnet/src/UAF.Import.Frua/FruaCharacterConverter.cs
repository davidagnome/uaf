using UAF.Serialization;
using static UAF.Import.Frua.FruaMonsterTraits;

namespace UAF.Import.Frua;

/// <summary>
/// Turns a <c>MONST###.DAT</c> creature into either a UAF monster or a UAF character.
/// </summary>
/// <remarks>
/// <para>
/// <b>One file, two destinations.</b> <c>ImportMonsterToUAF</c> (<c>UAImport.cpp:6965</c>) reads
/// the record and then branches: <c>race == 6 &amp;&amp; combatMode == 1</c> goes to
/// <c>ProcessMonsterCchData</c> and becomes a <c>MONSTER_DATA</c>; everything else goes to
/// <c>ProcessNpcCchData</c> and becomes a <c>CHARACTER</c>. The two share a source struct and
/// almost nothing else — the monster path keeps traits and attack dice, the NPC path keeps
/// abilities, classes and levels. <see cref="FruaCharacter.IsMonster"/> is that test.
/// </para>
/// <para>
/// <b>Carried items need the design's database.</b> Both reference paths call <c>AssignItem</c>,
/// which resolves a FRUA item ordinal against <c>item.dat</c> and multiplies the quantity by that
/// item's bundle size — so both projections take an optional
/// <see cref="FruaItemDatabase"/> and carry nothing without one, which is what a caller converting
/// a creature in isolation gets.
/// </para>
/// </remarks>
public static class FruaCharacterConverter
{
    /// <summary>What a creature with no morale of its own gets.</summary>
    /// <remarks>Both reference paths substitute this for a stored zero.</remarks>
    public const int DefaultMorale = 50;

    /// <summary>Converts a monster.</summary>
    /// <param name="creature">A record for which <see cref="FruaCharacter.IsMonster"/> holds.</param>
    /// <param name="items">The design's item database, for what the creature carries.</param>
    public static MonsterRecord ToMonster(FruaCharacter creature, FruaItemDatabase? items = null)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return new MonsterRecord(
            PreSpellNameKey: -1,
            Name: creature.Name,

            // The editor's default monster icon and hit/miss sounds -- template paths rather than
            // anything FRUA supplies, but not optional: a monster with no PIC_DATA is a pre-0.640
            // shape the writer refuses.
            Icon: FruaArtConverter.MonsterIcon,
            LegacyIconFile: string.Empty,
            HitSound: FruaArtConverter.HitSoundFile,
            MissSound: FruaArtConverter.MissSoundFile,

            // The reference sets neither of these.
            MoveSound: string.Empty,
            DeathSound: string.Empty,

            Intelligence: creature.Abilities.Intelligence,
            ArmorClass: creature.ArmourClass,
            Movement: creature.Move,
            HitDice: (float)creature.HitDice,
            UseHitDice: 1,
            HitDiceBonus: 0,
            Thac0: creature.Thac0,
            Attacks: Attacks(creature),
            MagicResistance: creature.MagicResistance,
            Size: (int)SizeOf(creature),
            ClassId: string.Empty,
            Morale: creature.MoraleValue,
            ExperienceValue: creature.ExperienceValue,
            FormType: (uint)FormOf(creature),
            PenaltyType: (uint)PenaltyOf(creature),
            ImmunityType: (uint)ImmunityOf(creature),
            MiscOptionsType: (uint)MiscOptionsOf(creature),
            UndeadType: UndeadFromName(creature.Name),
            SpecialAbilities: new SpecabBlock([], [], []),
            Attributes: [],
            Items: new ItemList(Carried(creature, items), ReadyItems.Empty),
            Money: null);
    }

    /// <summary><c>NPC_TYPE</c> — what an imported non-monster becomes (<c>Externs.h:966</c>).</summary>
    public const byte NpcType = 2;

    /// <summary>Converts an NPC.</summary>
    /// <param name="creature">
    /// A record for which <see cref="FruaCharacter.IsMonster"/> does not hold — which includes any
    /// creature whose race is not 6, whatever its combat mode.
    /// </param>
    /// <param name="items">The design's item database, for what the creature carries.</param>
    public static CharacterRecord ToCharacter(FruaCharacter creature,
                                              FruaItemDatabase? items = null)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return new CharacterRecord(
            CharacterVersion: 0,
            PreSpellNamesKey: 0,
            Type: NpcType,
            Race: RaceName(creature.Race),
            Gender: creature.Gender,
            ClassId: ClassName(creature.CharClass),
            Alignment: AlignmentOf(creature.Alignment),
            AllowInCombat: 1,
            Status: StatusOf(creature.Status),

            // Unlike a monster's, an NPC's undead type is stored rather than guessed.
            UndeadType: MonsterRecordReader.UndeadTypeName(creature.Undead),

            CreatureSize: (int)SizeOf(creature),
            Name: creature.Name,
            CharacterId: string.Empty,
            Thac0: creature.Thac0,
            Morale: creature.MoraleValue,
            Encumbrance: creature.Encumbrance,
            MaxEncumbrance: 0,
            ArmorClass: creature.ArmourClass,

            // The reference takes the adjusted hit points as current and the rolled maximum as
            // the cap, so an NPC arrives at full health including its constitution bonus.
            HitPoints: creature.AdjustedHitPoints,
            MaxHitPoints: creature.MaxHitPoints,

            NumberOfHitDice: creature.HitDice,
            Age: creature.Age,
            MaxAge: 0,
            Birthday: 0,
            MaxCureDisease: creature.MaxCureDisease,

            // FRUA has one set of damage dice; the engine has a small-target and a large-target
            // pair, and the reference fills both from the one.
            UnarmedDieSmall: creature.DamageDiceSides,
            UnarmedNumberDieSmall: creature.DamageDiceCount,
            UnarmedBonus: creature.DamageBonus,
            UnarmedDieLarge: creature.DamageDiceSides,
            UnarmedNumberDieLarge: creature.DamageDiceCount,

            MaxMovement: creature.Move,
            ReadyToTrain: creature.ReadyToTrain,
            CanTradeItems: 1,
            Abilities: new AbilityScores(
                Strength: creature.Abilities.Strength,
                StrengthMod: creature.Abilities.ExceptionalStrength,
                Intelligence: creature.Abilities.Intelligence,
                Wisdom: creature.Abilities.Wisdom,
                Dexterity: creature.Abilities.Dexterity,
                Constitution: creature.Abilities.Constitution,
                Charisma: creature.Abilities.Charisma),

            OpenDoors: 0,
            OpenMagicDoors: 0,
            BendBarsLiftGates: 0,
            HitBonus: 0,
            DamageBonus: 0,
            MagicResistance: creature.MagicResistance,
            BaseclassStats: Baseclasses(creature),
            SkillAdjustments: [],
            SpellAdjustments: [],

            // An imported NPC is a design's creature, not a pre-generated party member.
            IsPreGenerated: 0,
            CanBeSaved: 1,
            HasLayedOnHandsToday: 0,

            Money: null,
            NumberOfAttacks: (float)creature.AttacksPerRound,
            Icon: null,
            IconIndex: creature.IconId,
            OriginalIndex: creature.MonsterIndex,
            UniquePartyId: creature.UniquePartyId,
            DisableTalkIfDead: 0,
            TalkEvent: 0,
            TalkLabel: string.Empty,
            ExamineEvent: 0,
            ExamineLabel: string.Empty,
            SpellBook: new SpellBook(0, []),
            DetectingInvisible: 0,
            DetectingTraps: 0,
            SpellEffects: [],
            Blockages: [],
            SmallPic: EmptyPicture,
            Items: new ItemList(Carried(creature, items), ReadyItems.Empty),
            SpecialAbilities: new SpecabBlock([], [], []),
            Attributes: []);
    }

    private static PicRecord EmptyPicture { get; } =
        new(PicType: 0, FileName: string.Empty, TimeDelay: 0, NumFrames: 0,
            FrameWidth: 0, FrameHeight: 0, Flags: 0, MaxLoops: 0,
            Style: 0, UseAlpha: 0, AlphaValue: 0, RestartFrame: 0);

    /// <summary>
    /// The six races FRUA has, and a blank for anything else.
    /// </summary>
    /// <remarks>
    /// Race 6 is "monster", which only reaches this path when the combat mode disagrees — the
    /// reference names it <c>Unknown</c> rather than refusing the import.
    /// </remarks>
    private static string RaceName(byte race) => race switch
    {
        0 => "Elf",
        1 => "HalfElf",
        2 => "Dwarf",
        3 => "Gnome",
        4 => "Halfling",
        5 => "Human",
        _ => "Unknown",
    };

    /// <summary>
    /// FRUA's seventeen classes, including the two it never uses.
    /// </summary>
    /// <remarks>
    /// <b>Knight becomes Druid and Monk becomes Fighter.</b> The declaration marks both as "Not
    /// Used", and the reference maps them onto classes the engine actually has rather than
    /// carrying a class no rules table knows.
    /// </remarks>
    private static string ClassName(byte charClass) => charClass switch
    {
        0 => "Cleric",
        1 => "Druid",
        2 => "Fighter",
        3 => "Paladin",
        4 => "Ranger",
        5 => "Magic User",
        6 => "Thief",
        7 => "Fighter",
        8 => "C_F",
        9 => "C_F_MU",
        10 => "C_R",
        11 => "C_MU",
        12 => "C_T",
        13 => "F_MU",
        14 => "F_T",
        15 => "F_MU_T",
        16 => "MU_T",
        _ => "Fighter",
    };

    /// <summary>
    /// FRUA's alignment ordinals, which are not the engine's.
    /// </summary>
    /// <remarks>
    /// <b>FRUA runs law-to-chaos within each moral column; the engine runs good-to-evil within
    /// each ethical one.</b> Passing the byte through unchanged turns lawful-neutral into
    /// chaotic-good, so this is one of the mappings that silently corrupts a design rather than
    /// failing.
    /// </remarks>
    private static int AlignmentOf(byte alignment) => alignment switch
    {
        0 => 0,   // Lawful Good
        1 => 3,   // Lawful Neutral
        2 => 6,   // Lawful Evil
        3 => 1,   // Neutral Good
        4 => 4,   // True Neutral
        5 => 7,   // Neutral Evil
        6 => 2,   // Chaotic Good
        7 => 5,   // Chaotic Neutral
        8 => 8,   // Chaotic Evil
        _ => 4,
    };

    /// <summary>
    /// FRUA's status ordinals, which are likewise not the engine's.
    /// </summary>
    /// <remarks>
    /// Both lists start at Okay and then diverge completely — FRUA's second entry is Animated and
    /// the engine's is Unconscious. FRUA has no Fled; its Running is the nearest thing, and the
    /// reference maps it to the engine's own Running rather than to Fled.
    /// </remarks>
    private static int StatusOf(byte status) => status switch
    {
        0 => 0,   // Okay
        1 => 6,   // Animated
        2 => 7,   // Temporarily gone
        3 => 8,   // Running
        4 => 1,   // Unconscious
        5 => 9,   // Dying
        6 => 2,   // Dead
        7 => 4,   // Petrified
        8 => 5,   // Gone
        _ => 0,
    };

    /// <summary>
    /// The class levels in file order, named as baseclasses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every baseclass gets an entry, including ones at level zero.</b> The reference calls
    /// <c>SetLevels</c> seven times unconditionally, so a pure fighter still carries a cleric
    /// record at level zero. Dropping the empty ones would change what the editor shows.
    /// </para>
    /// <para>
    /// <b>Druid is always zero.</b> FRUA has no druid level to read — its second slot is the
    /// unused knight — so the reference passes literal zeroes, which is why the mapping below
    /// skips slot 1 and appends Druid separately.
    /// </para>
    /// </remarks>
    private static BaseclassStats[] Baseclasses(FruaCharacter creature)
    {
        // File order is cleric, knight, fighter, paladin, ranger, mage, thief.
        (string Name, int Slot)[] order =
        [
            ("Fighter", 2), ("Cleric", 0), ("Ranger", 4),
            ("Paladin", 3), ("MagicUser", 5), ("Thief", 6),
        ];

        var stats = new BaseclassStats[order.Length + 1];

        for (int i = 0; i < order.Length; i++)
        {
            var (name, slot) = order[i];

            stats[i] = new BaseclassStats(
                BaseclassId: name,
                CurrentLevel: creature.ClassLevels[slot],
                PreviousLevel: creature.ClassLevelsPreClassChange[slot],
                PreDrainLevel: creature.ClassLevelsPreDrain[slot],
                Experience: 0);
        }

        stats[^1] = new BaseclassStats("Druid", 0, 0, 0, 0);
        return stats;
    }

    /// <summary>
    /// The sixteen items a creature carries, resolved against the design's database.
    /// </summary>
    /// <remarks>
    /// <b>The quantity is bundles.</b> Both reference paths multiply the stored count by the
    /// item's own bundle size (<c>GetItemBundleQty</c>), so a creature carrying one bundle of
    /// twenty arrows has twenty — not one. Empty slots are zeroes and drop out.
    /// </remarks>
    private static ItemInstance[] Carried(FruaCharacter creature, FruaItemDatabase? items) =>
        creature.ItemsCarried
            .Select((ordinal, i) => FruaItemConverter.Instance(
                ordinal, items, creature.ItemQuantities[i]))
            .OfType<ItemInstance>()
            .ToArray();

    /// <summary>
    /// One attack per pair of attacks-per-two-rounds, all sharing the same damage dice.
    /// </summary>
    /// <remarks>
    /// <b>FRUA has one attack, repeated; UAF has a list of independent ones.</b> The reference
    /// builds the list by copying a single <c>ATTACK_DETAILS</c> — so a creature with three
    /// attacks gets three identical entries, not one entry with a count. A creature that attacks
    /// less than twice per two rounds still gets one attack rather than none.
    /// </remarks>
    private static AttackDetails[] Attacks(FruaCharacter creature)
    {
        int count = Math.Max(creature.AttacksPerTwoRounds / 2, 1);
        var attack = new AttackDetails(
            Sides: creature.DamageDiceSides,
            Nbr: creature.DamageDiceCount,
            Bonus: creature.DamageBonus,
            AttackMessage: "attacks",
            SpellId: string.Empty,
            LegacySpellId: 0,
            SpellClass: 0,
            SpellLevel: 0);

        var attacks = new AttackDetails[count];
        Array.Fill(attacks, attack);
        return attacks;
    }

    /// <summary>
    /// Icon footprints 1&#8211;3 are medium and everything else is large.
    /// </summary>
    /// <remarks>
    /// <b>Nothing imports as <see cref="Size.Small"/>.</b> The engine has three sizes and the
    /// reference's switch only ever produces two, because FRUA's smallest icon is already a
    /// one-square creature. The forced-large flag is a separate bit and lands in
    /// <see cref="Form.Large"/>, not here.
    /// </remarks>
    private static Size SizeOf(FruaCharacter creature) =>
        creature.Size is >= 1 and <= 3 ? Size.Medium : Size.Large;

    private static Form FormOf(FruaCharacter creature)
    {
        var form = Form.None;

        if ((creature.SpecialAbilityFlags & 2) != 0) form |= Form.Mammal;
        if ((creature.SpecialAbilityFlags & 16) != 0) form |= Form.Snake;
        if ((creature.SpecialAbilityFlags & 64) != 0) form |= Form.Animal;
        if ((creature.SpecialAbilityFlags2 & 1) != 0) form |= Form.Giant;
        if (creature.ForcedLarge) form |= Form.Large;

        return form;
    }

    private static Penalty PenaltyOf(FruaCharacter creature)
    {
        var penalty = Penalty.None;

        if ((creature.SpecialAbilityFlags & 4) != 0) penalty |= Penalty.DwarfArmorClass;
        if ((creature.SpecialAbilityFlags & 8) != 0) penalty |= Penalty.RangerDamage;
        if ((creature.SpecialAbilityFlags & 32) != 0) penalty |= Penalty.GnomeArmorClass;
        if ((creature.SpecialAbilityFlags & 128) != 0) penalty |= Penalty.DwarfThac0;
        if ((creature.SpecialAbilityFlags2 & 4) != 0) penalty |= Penalty.GnomeThac0;

        return penalty;
    }

    private static Immunity ImmunityOf(FruaCharacter creature)
    {
        var immunity = Immunity.None;

        if ((creature.SpecialAbilityFlags2 & 8) != 0) immunity |= Immunity.Death;
        if ((creature.SpecialAbilityFlags2 & 16) != 0) immunity |= Immunity.Poison;
        if ((creature.SpecialAbilityFlags2 & 32) != 0) immunity |= Immunity.Vorpal;
        if ((creature.SpecialAbilityFlags2 & 64) != 0) immunity |= Immunity.Confusion;

        return immunity;
    }

    private static MiscOptions MiscOptionsOf(FruaCharacter creature)
    {
        var options = MiscOptions.None;

        if ((creature.SpecialAbilityFlags & 1) != 0) options |= MiscOptions.AffectedByDispelEvil;
        if ((creature.SpecialAbilityFlags2 & 2) != 0) options |= MiscOptions.CanBeHeldCharmed;

        return options;
    }

    /// <summary>
    /// The undead names <c>GuessUndeadStatus</c> looks for, in its order.
    /// </summary>
    /// <remarks>
    /// <b>Order matters and this is the reference's.</b> It is a chain of <c>else if</c>s, so the
    /// first match wins — which is only visible for a name containing two of these words.
    /// </remarks>
    private static readonly string[] UndeadNames =
    [
        "skeleton", "zombie", "ghoul", "shadow", "wight", "ghast",
        "wraith", "mummy", "spectre", "vampire", "ghost", "lich",
    ];

    /// <summary>
    /// The undead type, guessed from the creature's name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>FRUA does not store this for monsters.</b> The struct has an <c>undead</c> byte, but the
    /// monster path never reads it — it calls <c>GuessUndeadStatus</c>
    /// (<c>Monster.cpp:381</c>) instead, which searches the lowercased name for a substring. A
    /// monster called "giant skeleton" is a skeleton; one called "bone golem" is not undead.
    /// </para>
    /// <para>
    /// <b>Divergence: "wight" is capitalised here.</b> The reference writes
    /// <c>undeadType = "wight"</c> where all thirteen of its neighbours are capitalised, so a
    /// wight's undead type does not match the name in <c>UndeadTypeText</c> and the editor's
    /// dropdown cannot show it. Nothing depends on the lowercase spelling.
    /// </para>
    /// </remarks>
    public static string UndeadFromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        string lower = name.ToLowerInvariant();

        foreach (string undead in UndeadNames)
        {
            if (lower.Contains(undead, StringComparison.Ordinal))
            {
                return char.ToUpperInvariant(undead[0]) + undead[1..];
            }
        }

        return string.Empty;
    }
}
