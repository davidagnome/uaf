using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Covers writing <c>CHARACTER</c> and the four leaves that only a character has, by reading every
/// written record back.
/// </summary>
/// <remarks>
/// These carry most of the weight for the money sack, the blockage list, the two adjustment lists
/// and the spell-effect list, because <b>every one of those is empty in every character of both
/// corpus designs</b> — see <see cref="CharacterWriterCorpusTests"/>, which pins that rather than
/// leaving it implied. Records are built with named arguments throughout: a seventy-field
/// positional constructor will put the hit points where the armour class goes and still compile.
/// </remarks>
public class CharacterRecordWriterTests
{
    private static readonly DesignVersion Modern = CharacterRecordWriter.WrittenVersion;

    private static MemoryStream Written(Action<IArchiveWriteCursor> write)
    {
        var stream = new MemoryStream();
        write(ArchiveWriteCursor.For(new MfcArchiveWriter(stream)));
        stream.Position = 0;
        return stream;
    }

    private static IArchiveCursor Reading(MemoryStream stream) =>
        ArchiveCursor.For(new MfcArchiveReader(stream));

    private static PicRecord Art(string file) => new(
        PicType: 3, FileName: file, TimeDelay: 120, NumFrames: 4,
        FrameWidth: 64, FrameHeight: 48, Flags: 0x11, MaxLoops: 7,
        Style: 2, UseAlpha: 1, AlphaValue: 0xBEEF, RestartFrame: 2);

    private static MoneySack Purse() =>
        new([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], [new GemType(2, 500)], [new GemType(9, 1200)]);

    private static ItemList Carrying() => new(
        [new ItemInstance(Key: 41, ItemId: "Long Sword", LegacyItemId: 0,
                          ReadyLocation: 0x1234ABCD, Quantity: 2, Identified: 1, Charges: 6,
                          Cursed: 0xEE, Paid: 75)],
        new ReadyItems([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]));

    private static SpellEffect Effect() => new(
        IndexKey: "$CHAR_HITPOINTS", Flags: 0x2004, ChangeResult: -3.5,
        String2: "src", SourceOfEffect: 7, Parent: 3,
        Scripts: ["s3", "s4", "s5", "s6", "s7", "s8", "s9", "s10", "s11"],
        StopTime: 900, Data: 5,
        ChangeData: new DicePlus(DicePlusReader.TagText, "1d4", "", 0, 0, 0, 0, 0, 0, []));

    private static CharacterRecord Character(
        string name = "Aramil",
        MoneySack? money = null,
        PicRecord? icon = null,
        ItemList? items = null,
        SpellBook? spellBook = null,
        IReadOnlyList<SkillAdjustment>? skills = null,
        IReadOnlyList<SpellAdjustment>? spells = null,
        IReadOnlyList<BlockageData>? blockages = null,
        IReadOnlyList<SpellEffect>? effects = null,
        SpecabBlock? specialAbilities = null) => new(
            CharacterVersion: 0,
            PreSpellNamesKey: 918634,
            Type: 2,
            Race: "Half-Elf",
            Gender: 1,
            ClassId: "Cleric/Fighter",
            Alignment: 4,
            AllowInCombat: 1,
            Status: 0,
            UndeadType: "Skeleton",
            CreatureSize: 1,
            Name: name,
            CharacterId: name,
            Thac0: 18,
            Morale: 50,
            Encumbrance: 272,
            MaxEncumbrance: 3500,
            ArmorClass: 7,
            HitPoints: 27,
            MaxHitPoints: 30,
            NumberOfHitDice: 2.5,
            Age: 28,
            MaxAge: 162,
            Birthday: 346,
            MaxCureDisease: 3,
            UnarmedDieSmall: 2,
            UnarmedNumberDieSmall: 1,
            UnarmedBonus: 0,
            UnarmedDieLarge: 2,
            UnarmedNumberDieLarge: 1,
            MaxMovement: 12,
            ReadyToTrain: 1,
            CanTradeItems: 1,
            Abilities: new AbilityScores(18, 55, 19, 15, 17, 17, 14),
            OpenDoors: 9,
            OpenMagicDoors: 4,
            BendBarsLiftGates: 35,
            HitBonus: 2,
            DamageBonus: 3,
            MagicResistance: 15,
            BaseclassStats: [new BaseclassStats("cleric", 3, 2, 1, 4500),
                             new BaseclassStats("fighter", 4, 0, 0, 8000)],
            SkillAdjustments: skills ?? [],
            SpellAdjustments: spells ?? [],
            IsPreGenerated: 1,
            CanBeSaved: 1,
            HasLayedOnHandsToday: 0,
            Money: money ?? Purse(),
            NumberOfAttacks: 1.5f,
            Icon: icon ?? Art("icon.png"),
            IconIndex: 1,
            OriginalIndex: -1,
            UniquePartyId: 255,
            DisableTalkIfDead: 1,
            TalkEvent: 72,
            TalkLabel: "TALK",
            ExamineEvent: 40,
            ExamineLabel: "EXAMINE",
            SpellBook: spellBook ?? new SpellBook(1, []),
            DetectingInvisible: 0,
            DetectingTraps: 1,
            SpellEffects: effects ?? [],
            Blockages: blockages ?? [],
            SmallPic: Art("small.png"),
            Items: items ?? Carrying(),
            SpecialAbilities: specialAbilities ?? new SpecabBlock([], [], []),
            Attributes: []);

    private static CharacterRecord RoundTrip(CharacterRecord character)
    {
        var stream = Written(w => CharacterRecordWriter.Write(w, character));
        var read = CharacterReader.Read(Reading(stream), Modern, ArchiveRole.Editor);

        // Exact exhaustion: a field written at the wrong width leaves bytes over or runs off the
        // end, and in a record this size that is the only assertion that finds it quickly.
        Assert.Equal(stream.Length, stream.Position);
        return read;
    }

    // ---- the leaves ----------------------------------------------------------------------------

    [Fact]
    public void A_spellbook_round_trips()
    {
        var book = new SpellBook(1, [
            new CharacterSpell("Magic Missile", 2, 1, 0),
            new CharacterSpell("Fireball", 1, 3, 1)]);

        var stream = Written(w => CharacterLeafWriters.WriteSpellBook(w, book));
        var read = MoreEventReaders.ReadSpellBook(Reading(stream), Modern, ArchiveRole.Editor);

        Assert.Equal(book.UseLimits, read.UseLimits);
        Assert.Equal(book.Spells, read.Spells);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void An_empty_spellbook_is_a_limit_and_a_zero_count()
    {
        // Eight bytes, not nothing: the limits int is always on the wire.
        var stream = Written(w => CharacterLeafWriters.WriteSpellBook(w, new SpellBook(0, [])));

        Assert.Equal(8, stream.Length);
    }

    [Fact]
    public void The_blockage_stats_field_keeps_its_word_width()
    {
        // Stats.StatsFull is a WORD of sixteen flags. Written as an int every entry after the
        // first lands two bytes late.
        IReadOnlyList<BlockageData> blockages = [
            new BlockageData(2, 5, 7, 0xBEEF), new BlockageData(3, 1, 1, 0x0001)];

        var stream = Written(w => CharacterLeafWriters.WriteBlockages(w, blockages));
        var cursor = Reading(stream);

        Assert.Equal(2, cursor.ReadInt32());
        Assert.Equal(2, cursor.ReadInt32());
        Assert.Equal(5, cursor.ReadInt32());
        Assert.Equal(7, cursor.ReadInt32());
        Assert.Equal(0xBEEF, cursor.ReadUInt16());
        Assert.Equal(4 + (2 * 14), stream.Length);
    }

    [Fact]
    public void Both_adjustment_lists_open_with_the_same_tag()
    {
        // The reference declares a local called SAVersion in each block and gives both "SA0", so
        // the tag says how a row is laid out and nothing about which list follows.
        var skills = Written(w => CharacterLeafWriters.WriteSkillAdjustments(w, []));
        var spells = Written(w => CharacterLeafWriters.WriteSpellAdjustments(w, []));

        Assert.Equal(CharacterLeafWriters.AdjustmentTag, Reading(skills).ReadString());
        Assert.Equal(CharacterLeafWriters.AdjustmentTag, Reading(spells).ReadString());
        Assert.Equal("BS0", CharacterLeafWriters.BaseclassStatsTag);
    }

    // ---- the record ----------------------------------------------------------------------------

    [Fact]
    public void A_whole_record_round_trips()
    {
        var character = Character();
        var read = RoundTrip(character);

        Assert.Equal(character.PreSpellNamesKey, read.PreSpellNamesKey);
        Assert.Equal(character.Type, read.Type);
        Assert.Equal(character.Race, read.Race);
        Assert.Equal(character.Gender, read.Gender);
        Assert.Equal(character.ClassId, read.ClassId);
        Assert.Equal(character.Alignment, read.Alignment);
        Assert.Equal(character.AllowInCombat, read.AllowInCombat);
        Assert.Equal(character.Status, read.Status);
        Assert.Equal(character.UndeadType, read.UndeadType);
        Assert.Equal(character.CreatureSize, read.CreatureSize);
        Assert.Equal(character.Name, read.Name);
        Assert.Equal(character.CharacterId, read.CharacterId);
        Assert.Equal(character.Thac0, read.Thac0);
        Assert.Equal(character.ArmorClass, read.ArmorClass);
        Assert.Equal(character.HitPoints, read.HitPoints);
        Assert.Equal(character.MaxHitPoints, read.MaxHitPoints);
        Assert.Equal(character.Birthday, read.Birthday);
        Assert.Equal(character.MaxMovement, read.MaxMovement);
        Assert.Equal(character.Abilities, read.Abilities);
        Assert.Equal(character.OpenDoors, read.OpenDoors);
        Assert.Equal(character.OpenMagicDoors, read.OpenMagicDoors);
        Assert.Equal(character.BendBarsLiftGates, read.BendBarsLiftGates);
        Assert.Equal(character.BaseclassStats, read.BaseclassStats);
        Assert.Equal(character.IconIndex, read.IconIndex);
        Assert.Equal(character.OriginalIndex, read.OriginalIndex);
        Assert.Equal(character.UniquePartyId, read.UniquePartyId);
        Assert.Equal(character.TalkEvent, read.TalkEvent);
        Assert.Equal(character.TalkLabel, read.TalkLabel);
        Assert.Equal(character.ExamineEvent, read.ExamineEvent);
        Assert.Equal(character.ExamineLabel, read.ExamineLabel);
        Assert.Equal(character.DetectingTraps, read.DetectingTraps);
        Assert.Equal(character.Icon, read.Icon);
        Assert.Equal(character.SmallPic, read.SmallPic);
        Assert.Equal(character.Items.Items, read.Items.Items);
        Assert.Equal(character.Money!.Coins, read.Money!.Coins);
        Assert.Equal(character.Money.Gems, read.Money.Gems);
        Assert.Equal(character.Money.Jewelry, read.Money.Jewelry);
    }

    [Fact]
    public void The_opener_is_the_constant_whatever_the_record_carried()
    {
        // The reference writes CHARACTER_VERSION unconditionally, and the index a pre-version file
        // held in that slot it discards on load -- so overwriting it loses nothing kept.
        var read = RoundTrip(Character() with { CharacterVersion = 0 });

        Assert.Equal(unchecked((int)CharacterRecordWriter.CharacterVersion), read.CharacterVersion);
    }

    [Fact]
    public void The_two_floating_point_widths_survive()
    {
        // nbrHitDice is a double and NbrAttacks a float. Swapping them costs four bytes and
        // desynchronises everything after.
        var read = RoundTrip(Character() with
        {
            NumberOfHitDice = 2.5,
            NumberOfAttacks = 1.5f,
        });

        Assert.Equal(2.5, read.NumberOfHitDice);
        Assert.Equal(1.5f, read.NumberOfAttacks);
    }

    [Fact]
    public void The_six_single_byte_fields_survive_among_their_int_neighbours()
    {
        var read = RoundTrip(Character() with
        {
            Type = 200, MaxMovement = 250, UniquePartyId = 255,
            OpenDoors = 9, OpenMagicDoors = 4, BendBarsLiftGates = 35,
        });

        Assert.Equal(200, read.Type);
        Assert.Equal(250, read.MaxMovement);
        Assert.Equal(255, read.UniquePartyId);
        Assert.Equal(9, read.OpenDoors);
        Assert.Equal(4, read.OpenMagicDoors);
        Assert.Equal(35, read.BendBarsLiftGates);
    }

    [Fact]
    public void The_ability_scores_go_out_as_ints_not_bytes()
    {
        // 21 bytes of difference in the middle of the record, and a strength modifier of 55 does
        // not even survive the narrow form as a value the reader would question.
        var read = RoundTrip(Character());

        Assert.Equal(new AbilityScores(18, 55, 19, 15, 17, 17, 14), read.Abilities);

        // The bound is the reason: below 0.999702 these are BYTEs.
        Assert.True(CharacterRecordWriter.WrittenVersion.Value >= 0.999702);
    }

    [Fact]
    public void The_structures_the_corpus_never_fills_round_trip_here()
    {
        // Skill and spell adjustments, blockages, spell effects and a non-empty money sack are all
        // absent from every character in both designs, so this is their only coverage.
        var character = Character(
            skills: [new SkillAdjustment("Climb", "ring", 15, -3)],
            spells: [new SpellAdjustment("Magic User", "amulet", 1, 5, 50, 2)],
            blockages: [new BlockageData(2, 5, 7, 0xBEEF)],
            effects: [Effect()]);

        var read = RoundTrip(character);

        Assert.Equal(character.SkillAdjustments, read.SkillAdjustments);
        Assert.Equal(character.SpellAdjustments, read.SpellAdjustments);
        Assert.Equal(character.Blockages, read.Blockages);
        Assert.Single(read.SpellEffects);
        Assert.Equal("$CHAR_HITPOINTS", read.SpellEffects[0].IndexKey);
        Assert.Equal(character.Money!.Gems, read.Money!.Gems);
    }

    [Fact]
    public void A_skill_adjustment_type_is_one_signed_byte()
    {
        // `type` is a char. Written as an int it takes three bytes from the next row's skill id.
        var read = RoundTrip(Character(skills: [
            new SkillAdjustment("Climb", "ring", 15, -3),
            new SkillAdjustment("Hide", "cloak", 5, 2)]));

        Assert.Equal((sbyte)-3, read.SkillAdjustments[0].Type);
        Assert.Equal("Hide", read.SkillAdjustments[1].SkillId);
    }

    // ---- what cannot be written ----------------------------------------------------------------

    [Fact]
    public void A_character_with_no_icon_is_refused()
    {
        Assert.False(CharacterRecordWriter.CanWrite(
            Character() with { Icon = null }, out string reason));
        Assert.Contains("SetDefaults", reason);
    }

    [Fact]
    public void A_character_with_loose_legacy_coins_is_refused()
    {
        // The distinction against a monster's missing sack: there the reference has an empty one
        // to write, here the port has lost the coins it read.
        Assert.False(CharacterRecordWriter.CanWrite(
            Character() with { Money = null }, out string reason));
        Assert.Contains("0.661", reason);
    }

    [Fact]
    public void A_character_holding_an_item_by_legacy_id_is_refused()
    {
        var legacy = new ItemList(
            [new ItemInstance(0, string.Empty, LegacyItemId: 12, 0, 1, 0, 0, 0, 0)],
            new ReadyItems([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]));

        Assert.False(CharacterRecordWriter.CanWrite(Character(items: legacy), out string reason));
        Assert.Contains("ITEM_ID", reason);
    }

    [Fact]
    public void A_legacy_special_ability_block_is_refused()
    {
        var legacy = new SpecabBlock([], [new LegacySpecabSlot("s", "b", "", "", 0, 0, [])], []);

        Assert.False(CharacterRecordWriter.CanWrite(
            Character(specialAbilities: legacy), out string reason));
        Assert.Contains("pre-0.921", reason);
    }

    // ---- the list ------------------------------------------------------------------------------

    [Fact]
    public void A_list_is_a_count_then_the_characters()
    {
        var list = new List<CharacterRecord> { Character("Aramil"), Character("Kloppin") };
        var stream = Written(w => CharacterRecordWriter.WriteList(w, list));

        var read = CharacterReader.ReadList(Reading(stream), Modern, ArchiveRole.Editor);

        Assert.Equal(["Aramil", "Kloppin"], read.Select(c => c.Name));
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void A_list_is_refused_whole_before_a_byte_goes_out()
    {
        var stream = new MemoryStream();
        var cursor = ArchiveWriteCursor.For(new MfcArchiveWriter(stream));

        Assert.Throws<NotSupportedException>(() => CharacterRecordWriter.WriteList(
            cursor, [Character("fine"), Character("bad") with { Icon = null }]));
        Assert.Equal(0, stream.Length);
    }
}
