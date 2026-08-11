using UAF.Import.Frua;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// The per-event-type payload readers (<c>addTextEvent</c>, <c>addTeleporterEvent</c> and
/// <c>addStairsEvent</c>, <c>UAFWinEd/UAImport.cpp</c>).
/// </summary>
public class FruaEventPayloadTests
{
    private static string? Heirs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string design = Path.Combine(dir.FullName, "reference", "Unlimited Adventures -ENG",
                                     "DESIGNS", "UA", "HEIRS.DSN");
        return Directory.Exists(design) ? design : null;
    }

    /// <summary>An event whose payload bytes are set at the reference's own offsets.</summary>
    private static FruaEvent Event(byte type, params (int Offset, byte Value)[] payload)
    {
        var record = new byte[FruaEvent.Length];
        record[0] = type;

        foreach (var (offset, value) in payload)
        {
            // Offset 5 is the first payload byte; see FruaEvent's remarks.
            record[4 + (offset - 5)] = value;
        }

        return FruaEvent.Read(record);
    }

    private static void Word(FruaEvent _, byte[] record, int offset, ushort value)
    {
        record[4 + (offset - 5)] = (byte)value;
        record[4 + (offset - 5) + 1] = (byte)(value >> 8);
    }

    // ---- transfers ---------------------------------------------------------------------------

    /// <summary>
    /// The facing is a two-bit field, and 4|8 is west rather than east-or-south.
    /// </summary>
    /// <remarks>
    /// The reference tests 12 before 4 and 8. Testing 4 first would match 12 as well and turn
    /// every west into an east — a transposition that would look like a design bug, not a port
    /// bug, until someone walked through the door.
    /// </remarks>
    [Theory]
    [InlineData(0, FruaTransferFacing.North)]
    [InlineData(4, FruaTransferFacing.East)]
    [InlineData(8, FruaTransferFacing.South)]
    [InlineData(12, FruaTransferFacing.West)]
    public void The_facing_masks_are_tested_widest_first(byte flags, FruaTransferFacing expected)
    {
        var e = Event(34, (8, flags));

        Assert.Equal(expected, FruaTransferEvent.Read(e).Facing);
    }

    /// <summary>The transfer-on-yes bit reads inverted.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(64, false)]
    public void The_transfer_on_yes_bit_is_inverted(byte flags, bool expected)
    {
        Assert.Equal(expected, FruaTransferEvent.Read(Event(34, (8, flags))).TransferOnYes);
    }

    [Fact]
    public void The_destination_reads_x_and_y_the_way_they_are_stored()
    {
        // y at 9, x at 10 -- stored in that order, as everywhere else in this format.
        var e = Event(34, (9, 7), (10, 13));
        var t = FruaTransferEvent.Read(e);

        Assert.Equal(13, t.DestinationX);
        Assert.Equal(7, t.DestinationY);
    }

    /// <summary>
    /// An entry point is used only when bit 1 says so, and 0 folds back to "none".
    /// </summary>
    [Theory]
    [InlineData(0, 5, -1)]    // bit 1 clear: coordinates, not an entry point
    [InlineData(1, 5, 4)]     // stored 5 -> zero-based 4
    [InlineData(1, 1, -1)]    // stored 1 -> 0, which the reference maps back to -1
    [InlineData(1, 0, -1)]    // stored 0 -> -1 already
    public void An_entry_point_of_zero_means_none(byte destFlags, byte stored, int expected)
    {
        var e = Event(34, (13, destFlags), (14, stored));

        Assert.Equal(expected, FruaTransferEvent.Read(e).DestinationEntryPoint);
    }

    [Fact]
    public void The_large_picture_bit_is_split_from_the_flags()
    {
        Assert.True(FruaTransferEvent.Read(Event(34, (7, 9), (8, 128))).PictureIsLarge);
        Assert.False(FruaTransferEvent.Read(Event(34, (7, 9), (8, 0))).PictureIsLarge);

        // And it must not leak into the facing, which reads the low bits of the same byte.
        Assert.Equal(FruaTransferFacing.North,
                     FruaTransferEvent.Read(Event(34, (8, 128))).Facing);
    }

    // ---- text --------------------------------------------------------------------------------

    /// <summary>Five string slots are concatenated into one passage.</summary>
    [Fact]
    public void The_text_is_five_slots_joined()
    {
        var record = new byte[FruaEvent.Length];
        record[0] = 2;
        var e = FruaEvent.Read(record);

        // Slots 1..5 at offsets 9, 11, 13, 15, 17.
        for (int i = 0; i < 5; i++)
        {
            Word(e, record, 9 + (i * 2), (ushort)(i + 1));
        }

        var table = Table(("ONE ", "TWO ", "THREE ", "FOUR ", "FIVE"));
        var text = FruaTextEvent.Read(FruaEvent.Read(record), table);

        Assert.Equal("ONE TWO THREE FOUR FIVE", text.Text);
    }

    /// <summary>Each chunk carries its own highlight bit.</summary>
    [Fact]
    public void A_highlighted_chunk_is_wrapped_at_both_ends()
    {
        var record = new byte[FruaEvent.Length];
        record[0] = 2;
        Word(FruaEvent.Read(record), record, 9, 1);
        Word(FruaEvent.Read(record), record, 11, 2);
        record[4 + (8 - 5)] = 4;    // highlight the FIRST chunk only

        var text = FruaTextEvent.Read(FruaEvent.Read(record), Table(("ONE", "TWO")));

        Assert.Equal("/hONE/hTWO", text.Text);
    }

    /// <summary>Any of five pause bits means wait.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(16, true)]
    [InlineData(8, true)]
    public void Wait_for_return_is_any_of_five_bits(byte control, bool expected)
    {
        var e = Event(2, (5, control));

        Assert.Equal(expected, FruaTextEvent.Read(e, Table()).WaitForReturn);
    }

    [Fact]
    public void Force_backup_is_its_own_bit()
    {
        Assert.True(FruaTextEvent.Read(Event(2, (5, 32)), Table()).ForceBackup);
        Assert.False(FruaTextEvent.Read(Event(2, (5, 1)), Table()).ForceBackup);
    }

    /// <summary>A table holding the given strings in slots 1..n.</summary>
    private static FruaStringTable Table(params (string, string)[] _)
    {
        // Only the slot-resolving behaviour matters here; build a real table so Get() is exercised.
        var level = new byte[FruaLevel.Length];
        return FruaStringTable.Read(level);
    }

    private static FruaStringTable Table((string A, string B, string C, string D, string E) five)
    {
        var level = new byte[FruaLevel.Length];
        PackInto(level, [five.A, five.B, five.C, five.D, five.E]);
        return FruaStringTable.Read(level);
    }

    private static FruaStringTable Table((string A, string B) two)
    {
        var level = new byte[FruaLevel.Length];
        PackInto(level, [two.A, two.B]);
        return FruaStringTable.Read(level);
    }

    /// <summary>Packs strings into a level's string table, six bits per character.</summary>
    private static void PackInto(byte[] level, string[] texts)
    {
        int at = 0;

        for (int slot = 0; slot < texts.Length; slot++)
        {
            var bits = new List<bool>();

            foreach (char c in texts[slot])
            {
                int v = c is >= (char)65 and <= (char)95 ? c & 0x3F : c;
                for (int b = 5; b >= 0; b--)
                {
                    bits.Add((v & (1 << b)) != 0);
                }
            }

            for (int b = 0; b < 6; b++)
            {
                bits.Add(false);
            }

            var packed = new byte[(bits.Count + 7) / 8];
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i])
                {
                    packed[i / 8] |= (byte)(1 << (7 - (i % 8)));
                }
            }

            level[FruaStringTable.LengthsAt + slot] = (byte)packed.Length;
            packed.CopyTo(level, FruaStringTable.StringsAt + at);
            at += packed.Length;
        }
    }

    // ---- combat ------------------------------------------------------------------------------

    [Fact]
    public void A_combat_slot_packs_a_quantity_under_its_flags()
    {
        // Slot 1: 3 monsters, outdoors (32), party surprised (64). Monster index in the next byte.
        var e = Event(1, (9, 3 | 32 | 64), (10, 101));
        var c = FruaCombatEvent.Read(e);

        Assert.Equal(3, c.Monsters[0].Quantity);
        Assert.Equal(101, c.Monsters[0].MonsterIndex);
        Assert.True(c.Outdoors);
        Assert.Equal(FruaSurprise.PartySurprised, c.Surprise);
        Assert.Equal(5, c.Monsters.Count);
    }

    /// <summary>Morale shares the picture byte, below its large-art bit.</summary>
    [Fact]
    public void Morale_shares_the_picture_byte()
    {
        var c = FruaCombatEvent.Read(Event(1, (7, 4), (8, 128 | 60)));

        Assert.Equal(4, c.PictureSlot);
        Assert.True(c.PictureIsLarge);
        Assert.Equal(60, c.MonsterMorale);
    }

    [Theory]
    [InlineData(0, FruaCombatDistance.UpClose)]
    [InlineData(32, FruaCombatDistance.Nearby)]
    [InlineData(64, FruaCombatDistance.FarAway)]
    public void The_distance_reads_from_the_fourth_slots_flags(byte flags,
                                                               FruaCombatDistance expected)
    {
        Assert.Equal(expected, FruaCombatEvent.Read(Event(1, (15, flags))).Distance);
    }

    [Fact]
    public void The_third_slots_flags_carry_three_independent_switches()
    {
        var c = FruaCombatEvent.Read(Event(1, (13, 32 | 64 | 128)));

        Assert.True(c.AutoApproach);
        Assert.True(c.PartyNeverDies);
        Assert.True(c.NoMonsterTreasure);
    }

    /// <summary>
    /// Every shipped combat event names monsters the reference would have discarded.
    /// </summary>
    /// <remarks>
    /// The point of this test is the discrepancy itself: the indices are in the file, this port
    /// reads them, and <c>addCombatEvent</c> throws them away behind six <c>NotImplemented</c>
    /// markers. If a writer is ever added, it must reproduce the reference's silence here or the
    /// byte-identity diff will fail on richer output.
    /// </remarks>
    [Fact]
    public void Shipped_combats_name_monsters_the_reference_discards()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        int slotsWithMonsters = 0;

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            foreach (var e in level.Events.Where(e => e.Type == FruaEventType.Combat))
            {
                var c = FruaCombatEvent.Read(e);

                Assert.Equal(5, c.Monsters.Count);
                Assert.InRange(c.MonsterMorale, 0, 127);

                slotsWithMonsters += c.Monsters.Count(m => m.Quantity > 0);
            }
        }

        Assert.True(slotsWithMonsters > 50,
                    $"only {slotsWithMonsters} populated monster slots across the design");
    }

    // ---- treasure and special items ----------------------------------------------------------

    /// <summary>
    /// The three money words are at 5, 9 and 11 — offset 7 is a hole.
    /// </summary>
    /// <remarks>
    /// Reading them consecutively at 5, 7, 9 would take the jewelry count for gems and leave
    /// jewelry at whatever followed. Only the identified flag at offset 8 reads into that gap.
    /// </remarks>
    [Fact]
    public void The_money_words_skip_offset_seven()
    {
        var record = new byte[FruaEvent.Length];
        record[0] = 3;
        Word(FruaEvent.Read(record), record, 5, 500);    // platinum
        Word(FruaEvent.Read(record), record, 9, 7);      // gems
        Word(FruaEvent.Read(record), record, 11, 2);     // jewelry

        var t = FruaTreasureEvent.Read(FruaEvent.Read(record));

        Assert.Equal(500, t.Platinum);
        Assert.Equal(7, t.Gems);
        Assert.Equal(2, t.Jewelry);
    }

    [Fact]
    public void Treasure_carries_eight_item_slots_with_one_identified_flag()
    {
        var e = Event(3, (8, 128), (13, 20), (14, 0), (15, 44));
        var t = FruaTreasureEvent.Read(e);

        Assert.Equal(8, t.ItemSlots.Count);
        Assert.True(t.ItemsAreIdentified);
        Assert.Equal([(byte)20, (byte)44], t.Items());
    }

    [Fact]
    public void An_unidentified_treasure_clears_the_flag()
    {
        Assert.False(FruaTreasureEvent.Read(Event(3, (8, 0))).ItemsAreIdentified);
    }

    /// <summary>Give is the only zero case; any other flag value takes.</summary>
    [Theory]
    [InlineData(0, FruaSpecialObjectOperation.Give)]
    [InlineData(1, FruaSpecialObjectOperation.Take)]
    [InlineData(64, FruaSpecialObjectOperation.Take)]
    [InlineData(128, FruaSpecialObjectOperation.Give)]   // high bit is the picture, masked off
    public void Only_a_zero_flags_byte_gives(byte flags, FruaSpecialObjectOperation expected)
    {
        Assert.Equal(expected, FruaSpecialItemEvent.Read(Event(38, (8, flags))).Operation);
    }

    [Theory]
    [InlineData(3, FruaObjectKind.Key, 3)]
    [InlineData(12, FruaObjectKind.Item, 4)]
    [InlineData(25, FruaObjectKind.Quest, 5)]
    public void A_special_item_names_a_key_item_or_quest(byte obj, FruaObjectKind kind, int index)
    {
        var s = FruaSpecialItemEvent.Read(Event(38, (9, obj)));

        Assert.Equal(kind, s.ObjectKind);
        Assert.Equal(index, s.ObjectIndex);
    }

    /// <summary>
    /// Every shipped treasure and special-item event decodes into range.
    /// </summary>
    [Fact]
    public void The_shipped_treasures_and_special_items_are_in_range()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        int treasures = 0;
        int specials = 0;

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            foreach (var e in level.Events)
            {
                if (e.Type is FruaEventType.GiveTreasure or FruaEventType.CombatTreasure)
                {
                    var t = FruaTreasureEvent.Read(e);
                    Assert.Equal(8, t.ItemSlots.Count);
                    treasures++;
                }
                else if (e.Type == FruaEventType.SpecialItem)
                {
                    var s = FruaSpecialItemEvent.Read(e);
                    Assert.InRange(s.ObjectIndex, 0, 43);
                    specials++;
                }
            }
        }

        Assert.True(treasures > 20, $"only {treasures} treasure events");
        Assert.True(specials > 20, $"only {specials} special-item events");
    }

    /// <summary>
    /// <c>PickOneCombat</c> is a <c>Combat</c> payload, so it needs no reader of its own.
    /// </summary>
    /// <remarks>
    /// The reference rewrites the type to <c>Combat</c> and calls <c>addCombatEvent</c>
    /// (<c>UAImport.cpp:3999</c>), so <see cref="FruaCombatEvent"/> already covers its 58 events.
    /// </remarks>
    [Fact]
    public void PickOneCombat_reads_as_a_combat()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        int picked = 0;

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            foreach (var e in level.Events.Where(e => e.Type == FruaEventType.PickOneCombat))
            {
                var c = FruaCombatEvent.Read(e);
                Assert.Equal(5, c.Monsters.Count);
                Assert.InRange(c.MonsterMorale, 0, 127);
                picked++;
            }
        }

        Assert.True(picked > 40, $"only {picked} PickOneCombat events; expected ~58");
    }

    // ---- damage and sounds ---------------------------------------------------------------------

    /// <summary>THAC0 is stored inverted, as the monster records store armour class.</summary>
    [Theory]
    [InlineData(0, 60)]
    [InlineData(40, 20)]
    [InlineData(60, 0)]
    public void Thac0_is_stored_as_sixty_minus_the_value(byte stored, int expected)
    {
        Assert.Equal(expected, FruaDamageEvent.Read(Event(4, (13, stored))).Thac0);
    }

    /// <summary>The target ladder tests the combined mask first.</summary>
    [Theory]
    [InlineData(0, FruaDamageTarget.EntireParty)]
    [InlineData(4, FruaDamageTarget.ActiveCharacter)]
    [InlineData(8, FruaDamageTarget.OneAtRandom)]
    [InlineData(12, FruaDamageTarget.ChanceOnEach)]
    public void The_damage_target_masks_are_tested_widest_first(byte flags,
                                                                FruaDamageTarget expected)
    {
        Assert.Equal(expected, FruaDamageEvent.Read(Event(4, (8, flags))).Target);
    }

    /// <summary>So does the save ladder, on the same byte.</summary>
    [Theory]
    [InlineData(0, FruaDamageSave.NoSave)]
    [InlineData(16, FruaDamageSave.SaveForHalf)]
    [InlineData(32, FruaDamageSave.SaveNegates)]
    [InlineData(48, FruaDamageSave.UseThac0)]
    public void The_save_masks_are_tested_widest_first(byte flags, FruaDamageSave expected)
    {
        Assert.Equal(expected, FruaDamageEvent.Read(Event(4, (8, flags))).Save);
    }

    /// <summary>The saving-throw column shares a byte with a four-bit bonus.</summary>
    [Theory]
    [InlineData(0, FruaSpellSave.ParalysisPoisonDeath, 0)]
    [InlineData(16 | 5, FruaSpellSave.PetrifyPolymorph, 5)]
    [InlineData(32 | 3, FruaSpellSave.RodStaffWand, 3)]
    [InlineData(48 | 2, FruaSpellSave.BreathWeapon, 2)]
    [InlineData(64 | 1, FruaSpellSave.Spell, 1)]
    public void The_spell_save_column_and_bonus_share_a_byte(byte stored, FruaSpellSave column,
                                                             int bonus)
    {
        var d = FruaDamageEvent.Read(Event(4, (14, stored)));

        Assert.Equal(column, d.SpellSave);
        Assert.Equal(bonus, d.SaveBonus);
    }

    [Fact]
    public void The_damage_dice_read_in_order()
    {
        var d = FruaDamageEvent.Read(Event(4, (9, 3), (10, 2), (11, 6), (12, 4), (17, 75)));

        Assert.Equal(3, d.Attacks);
        Assert.Equal(2, d.DiceCount);
        Assert.Equal(6, d.DiceSides);
        Assert.Equal(4, d.DamageBonus);
        Assert.Equal(75, d.ChancePerAttack);
    }

    /// <summary>A sound event is ten slots and nothing else.</summary>
    [Fact]
    public void A_sound_event_is_ten_slots()
    {
        var s = FruaSoundEvent.Read(Event(17, (5, 3), (7, 9), (14, 12)));

        Assert.Equal(10, s.SoundSlots.Count);
        Assert.Equal([(byte)3, (byte)9, (byte)12], s.Sounds());
    }

    // ---- quests --------------------------------------------------------------------------------

    /// <summary>The acceptance ladder is widest-first, and has a hole.</summary>
    [Theory]
    [InlineData(0, FruaQuestAccept.Impossible)]
    [InlineData(40, FruaQuestAccept.AutoAccept)]
    [InlineData(32, FruaQuestAccept.ImpossibleAuto)]
    [InlineData(24, FruaQuestAccept.OnYesOrNo)]
    [InlineData(16, FruaQuestAccept.OnNo)]
    [InlineData(8, FruaQuestAccept.OnYes)]
    [InlineData(4, FruaQuestAccept.Unchanged)]   // matches no mask; the reference assigns nothing
    public void The_quest_acceptance_masks_are_tested_widest_first(byte flags,
                                                                   FruaQuestAccept expected)
    {
        Assert.Equal(expected, FruaQuestEvent.Read(Event(35, (8, flags))).Accept);
    }

    /// <summary>The stage is stored zero-based and read one-based.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 5)]
    public void The_quest_stage_is_incremented_on_read(byte stored, int expected)
    {
        Assert.Equal(expected, FruaQuestEvent.Read(Event(35, (10, stored))).Stage);
    }

    [Fact]
    public void The_quest_flags_carry_two_independent_switches()
    {
        var q = FruaQuestEvent.Read(Event(35, (8, 64 | 4)));

        Assert.True(q.CompleteOnAccept);
        Assert.True(q.FailOnRejection);
    }

    /// <summary>Every shipped quest event decodes into range.</summary>
    [Fact]
    public void The_shipped_quest_events_are_in_range()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        int quests = 0;

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            foreach (var e in level.Events.Where(e => e.Type == FruaEventType.QuestStage))
            {
                var q = FruaQuestEvent.Read(e);
                Assert.InRange(q.QuestIndex, 0, 63);
                Assert.InRange(q.Stage, 1, 256);
                quests++;
            }
        }

        Assert.True(quests > 15, $"only {quests} quest events; expected ~21");
    }

    // ---- the real DOS levels -----------------------------------------------------------------

    /// <summary>
    /// Every text event in <c>HEIRS.DSN</c> yields readable text.
    /// </summary>
    /// <remarks>
    /// <b>This joins four layers at once</b> — event record, payload offsets, string slots and the
    /// six-bit decoder — so it fails if any of them has drifted. 443 text events is a lot of
    /// chances to produce noise instead of English.
    /// </remarks>
    [Fact]
    public void Every_shipped_text_event_reads_as_text()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        int withText = 0;

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            foreach (var e in level.Events.Where(e => e.Type == FruaEventType.TextStatement))
            {
                var text = FruaTextEvent.Read(e, level.Strings);

                foreach (char c in text.Text.Replace(FruaTextEvent.HighlightMarker, ""))
                {
                    Assert.InRange(c, (char)32, (char)95);
                }

                if (text.Text.Length > 0)
                {
                    withText++;
                }
            }
        }

        Assert.True(withText > 300, $"only {withText} of the text events carried any text");
    }

    /// <summary>Every transfer event in the design points somewhere sane.</summary>
    [Fact]
    public void Every_shipped_transfer_names_a_reachable_destination()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        var transfers = new[] { FruaEventType.Teleporter, FruaEventType.Stairs,
                                FruaEventType.TransferModule };
        int seen = 0;

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            foreach (var e in level.Events.Where(e => transfers.Contains(e.Type)))
            {
                var t = FruaTransferEvent.Read(e);

                Assert.InRange(t.DestinationX, 0, 255);
                Assert.InRange(t.DestinationY, 0, 255);
                Assert.InRange(t.DestinationEntryPoint, -1, 7);
                seen++;
            }
        }

        Assert.True(seen > 100, $"only {seen} transfer events found; expected ~187");
    }
}
