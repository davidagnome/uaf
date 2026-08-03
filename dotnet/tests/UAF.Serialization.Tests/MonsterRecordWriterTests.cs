using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Covers writing <c>MONSTER_DATA</c> — and, through it, the leaf writers — by reading every
/// written record back.
/// </summary>
/// <remarks>
/// The reader is the specification: it was walked against real designs and against the C++ oracle,
/// so agreeing with it is the strongest claim available without a writing oracle. Records are built
/// with named arguments throughout, because a 30-field positional constructor will happily put the
/// armour class where the hit points go and still compile.
/// </remarks>
public class MonsterRecordWriterTests
{
    private static readonly PicRecord Icon = new(
        PicType: 3, FileName: "goblin.pcx", TimeDelay: 120, NumFrames: 4,
        FrameWidth: 64, FrameHeight: 48, Flags: 0x11, MaxLoops: 7,
        Style: 2, UseAlpha: 1, AlphaValue: 0xBEEF, RestartFrame: 2);

    private static ItemList Carrying(params ItemInstance[] items) =>
        new(items, new ReadyItems([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]));

    private static ItemInstance Item(int legacyId = 0) => new(
        Key: 41, ItemId: "Long Sword", LegacyItemId: legacyId, ReadyLocation: 0x1234ABCD,
        Quantity: 2, Identified: 1, Charges: 6, Cursed: 0xEE, Paid: 75);

    private static MoneySack Purse() =>
        new([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], [new GemType(2, 500)], [new GemType(9, 1200)]);

    private static MonsterRecord Monster(
        string name = "Goblin",
        PicRecord? icon = null,
        string legacyIconFile = "",
        IReadOnlyList<AttackDetails>? attacks = null,
        string undeadType = "Skeleton",
        SpecabBlock? specialAbilities = null,
        IReadOnlyList<AslEntry>? attributes = null,
        ItemList? items = null,
        MoneySack? money = null) => new(
            PreSpellNameKey: -1,
            Name: name,
            Icon: icon ?? (legacyIconFile.Length > 0 ? null : Icon),
            LegacyIconFile: legacyIconFile,
            HitSound: "hit.wav",
            MissSound: "miss.wav",
            MoveSound: "move.wav",
            DeathSound: "die.wav",
            Intelligence: 8,
            ArmorClass: 6,
            Movement: 9,
            HitDice: 0.25f,
            UseHitDice: 1,
            HitDiceBonus: 2,
            Thac0: 19,
            Attacks: attacks ?? [new AttackDetails(6, 1, 1, "claws", "Magic Missile", 0, 3, 2)],
            MagicResistance: 15,
            Size: 2,
            ClassId: "Fighter",
            Morale: 50,
            ExperienceValue: 35,
            FormType: 0x01,
            PenaltyType: 0x02,
            ImmunityType: 0x04,
            MiscOptionsType: 0x08,
            UndeadType: undeadType,
            SpecialAbilities: specialAbilities ?? new SpecabBlock([new SpecabPair("k", "v")], [], []),
            Attributes: attributes ?? [new AslEntry("$SYS$Race", 4, "Goblinoid")],
            Items: items ?? Carrying(Item()),
            Money: money ?? Purse());

    private static MemoryStream Written(Action<IArchiveWriteCursor> write)
    {
        var stream = new MemoryStream();
        write(ArchiveWriteCursor.For(new MfcArchiveWriter(stream)));
        stream.Position = 0;
        return stream;
    }

    private static MonsterRecord ReadBack(MemoryStream stream) =>
        MonsterRecordReader.Read(ArchiveCursor.For(new MfcArchiveReader(stream)),
                                 MonsterRecordWriter.WrittenVersion, ArchiveRole.Engine);

    private static MonsterRecord RoundTrip(MonsterRecord monster) =>
        ReadBack(Written(w => MonsterRecordWriter.Write(w, monster)));

    // ---- the whole record ------------------------------------------------------------------------

    [Fact]
    public void Every_scalar_comes_back_as_itself()
    {
        var read = RoundTrip(Monster());

        Assert.Equal(-1, read.PreSpellNameKey);
        Assert.Equal("Goblin", read.Name);
        Assert.Equal(("hit.wav", "miss.wav", "move.wav", "die.wav"),
                     (read.HitSound, read.MissSound, read.MoveSound, read.DeathSound));
        Assert.Equal((8, 6, 9), (read.Intelligence, read.ArmorClass, read.Movement));
        Assert.Equal((1, 2, 19), (read.UseHitDice, read.HitDiceBonus, read.Thac0));
        Assert.Equal((15, 2, "Fighter"), (read.MagicResistance, read.Size, read.ClassId));
        Assert.Equal((50, 35), (read.Morale, read.ExperienceValue));
        Assert.Equal((0x01u, 0x02u, 0x04u, 0x08u),
                     (read.FormType, read.PenaltyType, read.ImmunityType, read.MiscOptionsType));
        Assert.Equal("Skeleton", read.UndeadType);
    }

    [Fact]
    public void The_icon_and_the_attacks_come_back()
    {
        var read = RoundTrip(Monster());

        Assert.Equal(Icon, read.Icon);
        Assert.Equal([new AttackDetails(6, 1, 1, "claws", "Magic Missile", 0, 3, 2)], read.Attacks);
    }

    [Fact]
    public void Hit_dice_is_written_as_a_float()
    {
        // Monster.h:410 declares it float among longs. Same four bytes, so writing an int never
        // desynchronises -- it just gives the monster a nonsense hit-die count. A quarter of a
        // die would read back as roughly 1.05e9.
        Assert.Equal(0.25f, RoundTrip(Monster()).HitDice);
    }

    [Fact]
    public void The_record_continues_past_the_attribute_list()
    {
        // MONSTER_DATA is the only record whose payload runs on after its ASL. A writer modelled
        // on ITEM_DATA stops here, and the reader then takes the next record's key for an
        // item count.
        var read = RoundTrip(Monster());

        Assert.Equal([new AslEntry("$SYS$Race", 4, "Goblinoid")], read.Attributes);
        Assert.Equal([new SpecabPair("k", "v")], read.SpecialAbilities.Pairs);
        Assert.Equal([Item()], read.Items!.Items);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], read.Items!.Ready.Slots);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], read.Money!.Coins);
        Assert.Equal([new GemType(2, 500)], read.Money!.Gems);
        Assert.Equal([new GemType(9, 1200)], read.Money!.Jewelry);
    }

    [Fact]
    public void A_record_ends_exactly_where_the_reader_thinks_it_does()
    {
        // The cheapest whole-record check there is: anything written short or long shows up as a
        // marker that does not come back.
        var stream = Written(w =>
        {
            MonsterRecordWriter.Write(w, Monster());
            w.WriteInt32(0x5EA1);
        });

        var reader = new MfcArchiveReader(stream);
        MonsterRecordReader.Read(ArchiveCursor.For(reader), MonsterRecordWriter.WrittenVersion,
                                 ArchiveRole.Engine);
        Assert.Equal(0x5EA1, reader.ReadInt32());
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void The_cursed_flag_is_one_byte_so_what_follows_stays_aligned()
    {
        // Items.h:325 declares it BYTE between two ints. Four bytes here shift `paid` and every
        // structure after the item list.
        var read = RoundTrip(Monster(items: Carrying(Item())));

        Assert.Equal((byte)0xEE, read.Items!.Items[0].Cursed);
        Assert.Equal(75, read.Items!.Items[0].Paid);
    }

    // ---- the blank-string convention -------------------------------------------------------------

    [Fact]
    public void The_das_strings_go_out_through_the_blank_sentinel()
    {
        var read = RoundTrip(Monster(name: string.Empty));

        Assert.Equal(string.Empty, read.Name);

        // And the sentinel really is on the wire: a zero-length string is a different file, and
        // one the reference reads as a zero-length name rather than as blank.
        var stream = Written(w => MonsterRecordWriter.Write(w, Monster(name: string.Empty)));
        var reader = new MfcArchiveReader(stream);
        reader.ReadInt32();
        Assert.Equal(ArchiveStringConventions.ArchiveBlank, reader.ReadString());
    }

    [Fact]
    public void A_name_that_is_literally_the_sentinel_is_a_fixed_point()
    {
        // It reads as empty and writes back as "*", so the bytes are stable even though the value
        // is not what the design author typed. There is no encoding that could tell them apart.
        Assert.Equal(string.Empty, RoundTrip(Monster(name: "*")).Name);
    }

    [Fact]
    public void The_verbatim_strings_do_not_go_through_it()
    {
        // classID and undeadType are written with the plain operator, not the AS macro. Putting
        // them through it would turn an unset undead type into the literal "*", which then reads
        // back as a category no turning table has.
        var read = RoundTrip(Monster(undeadType: string.Empty));

        Assert.Equal(string.Empty, read.UndeadType);
    }

    // ---- what the reader invents -----------------------------------------------------------------

    [Fact]
    public void A_monster_written_with_no_attacks_reads_back_with_one()
    {
        // Not a writer bug: the reference forces an attack on any monster that loads without one
        // (Monster.cpp:764), so this is what a reference load of the same bytes produces too.
        var read = RoundTrip(Monster(attacks: []));

        Assert.Equal([new AttackDetails(6, 1, 0, MonsterRecordReader.DefaultAttackMessage,
                                        string.Empty, 0, 0, 0)],
                     read.Attacks);
    }

    [Fact]
    public void A_record_from_before_the_item_list_writes_empties_rather_than_refusing()
    {
        // Absent below 0.694 and 0.906, where the reference writes its default-constructed
        // members. Writing empties is what those default members are, not a guess.
        var read = RoundTrip(Monster() with { Items = null, Money = null });

        Assert.Empty(read.Items!.Items);
        Assert.Equal(new int[MonsterLeafReaders.ReadySlotCount], read.Items!.Ready.Slots);
        Assert.Equal(new int[MonsterLeafReaders.MaxCoinTypes], read.Money!.Coins);
        Assert.Empty(read.Money!.Gems);
        Assert.Empty(read.Money!.Jewelry);
    }

    // ---- refusals --------------------------------------------------------------------------------

    [Fact]
    public void A_record_with_only_a_legacy_icon_filename_is_refused()
    {
        var monster = Monster(legacyIconFile: "goblin.pcx");

        Assert.False(MonsterRecordWriter.CanWrite(monster, out string reason));
        Assert.Contains("SetDefaults", reason);
        Assert.Throws<NotSupportedException>(
            () => MonsterRecordWriter.Write(ArchiveWriteCursor.For(new MfcArchiveWriter(new MemoryStream())), monster));
    }

    [Fact]
    public void An_unresolved_legacy_spell_id_is_refused()
    {
        var monster = Monster(attacks: [new AttackDetails(6, 1, 0, "claws", "", 17, 0, 0)]);

        Assert.False(MonsterRecordWriter.CanWrite(monster, out string reason));
        Assert.Contains("spell", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<NotSupportedException>(
            () => MonsterRecordWriter.Write(ArchiveWriteCursor.For(new MfcArchiveWriter(new MemoryStream())), monster));
    }

    [Fact]
    public void An_unresolved_legacy_item_id_is_refused()
    {
        var monster = Monster(items: Carrying(Item(legacyId: 23)));

        Assert.False(MonsterRecordWriter.CanWrite(monster, out _));
        Assert.Throws<NotSupportedException>(
            () => MonsterRecordWriter.Write(ArchiveWriteCursor.For(new MfcArchiveWriter(new MemoryStream())), monster));
    }

    [Fact]
    public void Legacy_special_abilities_are_refused()
    {
        var legacy = new SpecabBlock([], [], [(ushort)3]);

        Assert.False(MonsterRecordWriter.CanWrite(Monster(specialAbilities: legacy), out _));
    }

    [Fact]
    public void An_ordinary_record_is_writable()
    {
        Assert.True(MonsterRecordWriter.CanWrite(Monster(), out string reason));
        Assert.Equal(string.Empty, reason);
    }

    // ---- the fixed-size leaves -------------------------------------------------------------------

    [Fact]
    public void A_short_ready_slot_list_is_rejected_rather_than_truncating_the_record()
    {
        // Twelve slots, compile-time in the reference, so the count is never written. A short list
        // would leave the reader to take the money sack's first coins for equipment.
        var monster = Monster(items: new ItemList([], new ReadyItems([1, 2, 3])));

        Assert.Throws<ArgumentException>(
            () => MonsterRecordWriter.Write(ArchiveWriteCursor.For(new MfcArchiveWriter(new MemoryStream())), monster));
    }

    [Fact]
    public void A_short_coin_list_is_rejected_too()
    {
        var monster = Monster(money: new MoneySack([1, 2, 3], [], []));

        Assert.Throws<ArgumentException>(
            () => MonsterRecordWriter.Write(ArchiveWriteCursor.For(new MfcArchiveWriter(new MemoryStream())), monster));
    }

    // ---- the database ----------------------------------------------------------------------------

    [Fact]
    public void A_database_is_a_count_then_the_records_and_nothing_after()
    {
        var monsters = new[] { Monster(name: "Goblin"), Monster(name: "Kobold") };

        var stream = Written(w => MonsterRecordWriter.WriteDatabase(w, monsters));
        var read = MonsterRecordReader.ReadDatabase(new MfcArchiveReader(stream),
                                                    MonsterRecordWriter.WrittenVersion,
                                                    ArchiveRole.Engine);

        Assert.Equal(["Goblin", "Kobold"], read.Select(m => m.Name));
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void An_empty_database_round_trips()
    {
        var stream = Written(w => MonsterRecordWriter.WriteDatabase(w, []));

        Assert.Empty(MonsterRecordReader.ReadDatabase(new MfcArchiveReader(stream),
                                                      MonsterRecordWriter.WrittenVersion,
                                                      ArchiveRole.Engine));
    }

    [Fact]
    public void One_unwritable_record_stops_the_file_before_a_single_byte_goes_out()
    {
        // Failing part-way leaves a count promising records that are not there, and a reader that
        // runs off the end of the file looking for them.
        var monsters = new[] { Monster(name: "Goblin"), Monster(legacyIconFile: "old.pcx") };

        var stream = new MemoryStream();
        Assert.Throws<NotSupportedException>(
            () => MonsterRecordWriter.WriteDatabase(ArchiveWriteCursor.For(new MfcArchiveWriter(stream)), monsters));
        Assert.Equal(0, stream.Length);
    }
}
