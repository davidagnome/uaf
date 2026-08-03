using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// The eleven event types no shipped design contains.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are the weakest-verified readers in the port, and the reason is worth stating.</b> The
/// six level-bearing designs in the corpus hold 6,234 events between them and use <b>27</b> of the
/// 44 types; every one of the eleven read here — <c>Damage</c>, <c>EncounterEvent</c>,
/// <c>EnterPassword</c>, <c>HealParty</c>, <c>JournalEvent</c>, <c>PlayMovieEvent</c>,
/// <c>SmallTown</c>, <c>TakePartyItems</c>, <c>TavernTales</c>, <c>Vault</c> and <c>WhoTries</c> —
/// appears exactly zero times. So <see cref="EventWalkTests"/>, which is what actually proves the
/// other readers correct, cannot reach these at all.
/// </para>
/// <para>
/// What stands in for it, given a synthetic fixture can only pin a convention and never discover
/// one:
/// </para>
/// <list type="number">
/// <item>Every field list was cross-checked against the type's <c>Export(JWriter&amp;)</c>, which
/// is a <b>separate</b> description of the same record written independently of <c>Serialize</c>.
/// That is what confirms nothing is missing or invented — and it is how
/// <c>PASSWORD_DATA::matchCase</c> was pinned as exported-but-not-serialized.</item>
/// <item>Every test asserts the stream lands <b>exactly</b> at the end of what it wrote, so a
/// wrong width shows up as a length error rather than a plausible value.</item>
/// <item>The widths come from <c>GameEvent.h</c> and are written here with the same explicit
/// calls the reader reads them with, so a <c>BYTE</c> read as an <c>int</c> fails.</item>
/// </list>
/// <para>
/// A design that uses one of these types is still better evidence than all of the above. Until one
/// turns up, this is transcription checked twice, not observation.
/// </para>
/// </remarks>
public class EventTypesAbsentFromCorpusTests
{
    /// <summary>Modern enough that every version gate in every one of these is open.</summary>
    private static readonly DesignVersion Version = new(5.28);

    private static byte[] Build(EventType type, Action<MfcArchiveWriter> body)
    {
        var stream = new MemoryStream();
        var w = new MfcArchiveWriter(stream);
        WriteBase(w, type);
        body(w);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes the <c>GameEvent</c> preamble every subclass begins with — the control block, two
    /// <c>PIC_DATA</c>, the identity fields, three <c>DAS</c> strings and the event ASL.
    /// </summary>
    private static void WriteBase(MfcArchiveWriter w, EventType type)
    {
        // EVENT_CONTROL (GameEvent.cpp:1567), in the modern name-bearing form.
        w.WriteInt32(0);                                 // eventStatus, unused
        w.WriteInt32(0);                                 // eventResult, unused
        w.WriteInt32(1);                                 // onceOnly
        w.WriteInt32(2);                                 // chainTrigger
        w.WriteInt32(3);                                 // eventTrigger
        w.WriteString("Longsword");                      // itemID
        w.WriteInt32(7);                                 // quest
        w.WriteInt32(50);                                // chance
        w.WriteInt32(1);                                 // facing
        w.WriteString("Human");                          // raceID
        w.WriteString("fighter");                        // classID / baseclassID
        w.WriteString("Aramil");                         // characterID, >= 0.820
        AslWriter.Write(ArchiveWriteCursor.For(w), Version, AslMaps.EventControl, []);
        w.WriteString("$Script");                        // gpdlData, >= 0.880
        w.WriteInt32(0);                                 // gpdlIsBinary
        w.WriteInt32(4);                                 // partyX, >= 0.911
        w.WriteInt32(5);                                 // partyY
        w.WriteString("Bless");                          // memorized spell id
        w.WriteInt32(1);
        w.WriteInt32(2);

        var pic = new PicRecord(0, "art.png", 0, 1, 8, 8, 0, 0, 0, 0, 0, 0);
        PicDataWriter.Write(ArchiveWriteCursor.For(w), pic, PicArchiveVariant.Car);
        PicDataWriter.Write(ArchiveWriteCursor.For(w), pic, PicArchiveVariant.Car);

        w.WriteInt32((int)type);
        w.WriteUInt32(11);                               // id
        w.WriteInt32(12);                                // x
        w.WriteInt32(13);                                // y
        w.WriteUInt32(14);                               // chainEventHappen
        w.WriteUInt32(15);                               // chainEventNotHappen
        w.WriteString("*");                              // text, DAS-blank
        w.WriteString("*");
        w.WriteString("*");
        AslWriter.Write(ArchiveWriteCursor.For(w), Version, AslMaps.EventData, []);
    }

    /// <summary>Reads through the real dispatcher, and insists the stream ends where it should.</summary>
    private static T Read<T>(EventType type, byte[] data) where T : class, IGameEvent
    {
        var stream = new MemoryStream(data);
        var read = EventBodyReader.TryRead(ArchiveCursor.For(new MfcArchiveReader(stream)),
                                           type, Version, ArchiveRole.Editor);

        Assert.NotNull(read);

        // The whole point: a wrong field width leaves the stream short or long, and every event
        // after this one in a real level would then be read from the wrong offset.
        Assert.Equal(data.Length, stream.Position);

        Assert.Equal(11u, read.Base.Id);                 // the base really was consumed
        return Assert.IsType<T>(read);
    }

    // ---- the party-effect trio -------------------------------------------------------------------

    [Fact]
    public void A_damage_event_reads_eleven_ints_with_no_version_fork()
    {
        byte[] data = Build(EventType.Damage, w =>
        {
            foreach (int v in new[] { 3, 75, 6, 2, 1, -2, 17, 1, 2, 3, 4 })
            {
                w.WriteInt32(v);
            }
        });

        var damage = Read<DamageEvent>(EventType.Damage, data);

        Assert.Equal((3, 75), (damage.NbrAttacks, damage.ChancePerAttack));
        Assert.Equal((6, 2, 1), (damage.DmgDice, damage.DmgDiceQty, damage.DmgBonus));
        Assert.Equal((-2, 17), (damage.SaveBonus, damage.AttackThac0));
        Assert.Equal((1, 2, 3, 4),
                     (damage.EventSave, damage.SpellSave, damage.Who, damage.Distance));
    }

    [Fact]
    public void A_heal_event_reads_its_two_bytes_as_bytes()
    {
        byte[] data = Build(EventType.HealParty, w =>
        {
            w.WriteInt32(1);                             // HealHP, a BOOL
            w.WriteInt32(0);                             // HealDrain
            w.WriteInt32(1);                             // HealCurse
            w.WriteByte(80);                             // chance -- BYTE
            w.WriteInt32(2);                             // who
            w.WriteInt32(25);                            // HowMuchHP, >= 0.882
            w.WriteByte(1);                              // LiteralOrPercent -- BYTE
        });

        var heal = Read<HealPartyEvent>(EventType.HealParty, data);

        Assert.Equal((1, 0, 1), (heal.HealHitPoints, heal.HealDrain, heal.HealCurse));
        Assert.Equal((byte)80, heal.Chance);
        Assert.Equal((2, 25), (heal.Who, heal.HowMuchHp));
        Assert.Equal((byte)1, heal.LiteralOrPercent);
    }

    [Fact]
    public void Below_the_gate_a_heal_event_stops_after_who()
    {
        var old = new DesignVersion(0.881);
        var stream = new MemoryStream();
        var w = new MfcArchiveWriter(stream);

        // The base is version-sensitive too, so build the whole thing at the old version.
        WriteOldBase(w, EventType.HealParty, old);
        w.WriteInt32(1);
        w.WriteInt32(0);
        w.WriteInt32(1);
        w.WriteByte(80);
        w.WriteInt32(2);

        byte[] data = stream.ToArray();
        var read = new MemoryStream(data);
        var heal = Assert.IsType<HealPartyEvent>(
            EventBodyReader.TryRead(ArchiveCursor.For(new MfcArchiveReader(read)),
                                    EventType.HealParty, old, ArchiveRole.Engine));

        Assert.Equal(data.Length, read.Position);
        Assert.Equal(0, heal.HowMuchHp);
        Assert.Equal((byte)0, heal.LiteralOrPercent);
    }

    /// <summary>A base for a design old enough to predate the control block's later additions.</summary>
    private static void WriteOldBase(MfcArchiveWriter w, EventType type, DesignVersion version)
    {
        w.WriteInt32(0);
        w.WriteInt32(0);
        w.WriteInt32(1);
        w.WriteInt32(2);
        w.WriteInt32(3);
        w.WriteString("Longsword");
        w.WriteInt32(7);
        w.WriteInt32(50);
        w.WriteInt32(1);
        w.WriteString("Human");
        w.WriteString("fighter");
        w.WriteString("Aramil");                         // >= 0.820
        AslWriter.Write(ArchiveWriteCursor.For(w), version, AslMaps.EventControl, []);
        w.WriteString("$Script");                        // >= 0.880
        w.WriteInt32(0);

        // partyX/partyY AND the memorized spell are one block gated at 0.911, so all five are
        // absent here -- writing the spell alone is what a reader of the declaration would do.

        // Below 0.900 a PIC_DATA has no style, and below 5.24 no RestartFrame.
        var pic = new PicRecord(0, "art.png", 0, 1, 8, 8, 0, 0, 0, 0, 0, 0);
        WriteOldPic(w, pic);
        WriteOldPic(w, pic);

        w.WriteInt32((int)type);
        w.WriteUInt32(11);
        w.WriteInt32(12);
        w.WriteInt32(13);
        w.WriteUInt32(14);
        w.WriteUInt32(15);
        w.WriteString("*");
        w.WriteString("*");
        w.WriteString("*");
        AslWriter.Write(ArchiveWriteCursor.For(w), version, AslMaps.EventData, []);
    }

    private static void WriteOldPic(MfcArchiveWriter w, PicRecord pic)
    {
        w.WriteInt32(pic.PicType);
        w.WriteString(pic.FileName);
        w.WriteInt32(pic.TimeDelay);
        w.WriteInt32(pic.NumFrames);
        w.WriteInt32(pic.FrameWidth);
        w.WriteInt32(pic.FrameHeight);
        w.WriteUInt32(pic.Flags);                        // >= 0.790
        w.WriteUInt32(pic.MaxLoops);                     // >= 0.810
    }

    [Fact]
    public void A_take_items_event_reads_two_bytes_and_then_an_item_list()
    {
        byte[] data = Build(EventType.TakePartyItems, w =>
        {
            w.WriteInt32(1);                             // StoreItems, a BOOL
            w.WriteInt32(0);                             // mustHitReturn
            w.WriteByte(0x0B);                           // takeItems -- BYTE between two BOOLs
            w.WriteInt32(1);                             // takeAffects
            w.WriteInt32(2);                             // itemSelectFlags
            w.WriteInt32(3);                             // platinumSelectFlags
            w.WriteInt32(4);                             // gemsSelectFlags
            w.WriteInt32(5);                             // jewelrySelectFlags
            w.WriteInt32(100);                           // platinum
            w.WriteInt32(2);                             // gems
            w.WriteInt32(1);                             // jewelry
            w.WriteInt32(50);                            // itemPcnt
            w.WriteInt32(6);                             // moneyType
            w.WriteByte(3);                              // WhichVault -- BYTE, >= 0.910

            MonsterLeafWriters.WriteItemList(
                ArchiveWriteCursor.For(w), new ItemList([], new ReadyItems(new int[MonsterLeafReaders.ReadySlotCount])));
        });

        var take = Read<TakePartyItemsEvent>(EventType.TakePartyItems, data);

        Assert.Equal((byte)0x0B, take.TakeItems);
        Assert.Equal((byte)3, take.WhichVault);
        Assert.Equal((100, 2, 1, 50), (take.Platinum, take.Gems, take.Jewelry, take.ItemPercent));
        Assert.Equal(6, take.MoneyType);
        Assert.Empty(take.Items.Items);
    }

    // ---- the two trials --------------------------------------------------------------------------

    [Fact]
    public void A_password_event_reads_its_two_transfer_blocks()
    {
        byte[] data = Build(EventType.EnterPassword, w =>
        {
            w.WriteInt32(3);                             // nbrTries
            w.WriteUInt32(100);                          // successChain
            w.WriteUInt32(200);                          // failChain
            w.WriteInt32(1);                             // successAction
            w.WriteInt32(2);                             // failAction
            w.WriteString("xyzzy");                      // password, through DAS

            // No matchCase here -- it is declared and exported, and never serialized.
            foreach (int v in new[] { 1, 2, 3, 4, 5, 6 }) { w.WriteInt32(v); }
            foreach (int v in new[] { 7, 8, 9, 10, 11, 12 }) { w.WriteInt32(v); }
        });

        var password = Read<PasswordEvent>(EventType.EnterPassword, data);

        Assert.Equal("xyzzy", password.Password);
        Assert.Equal((3, 100u, 200u), (password.NbrTries, password.SuccessChain, password.FailChain));
        Assert.Equal(3, password.SuccessTransfer.DestLevel);
        Assert.Equal(9, password.FailTransfer.DestLevel);
    }

    [Fact]
    public void An_empty_password_round_trips_through_the_blank_sentinel()
    {
        byte[] data = Build(EventType.EnterPassword, w =>
        {
            w.WriteInt32(0);
            w.WriteUInt32(0);
            w.WriteUInt32(0);
            w.WriteInt32(0);
            w.WriteInt32(0);
            w.WriteString(ArchiveStringConventions.ArchiveBlank);
            for (int i = 0; i < 12; i++) { w.WriteInt32(0); }
        });

        Assert.Equal(string.Empty, Read<PasswordEvent>(EventType.EnterPassword, data).Password);
    }

    [Fact]
    public void A_who_tries_event_reads_sixteen_bools_then_a_byte()
    {
        byte[] data = Build(EventType.WhoTries, w =>
        {
            w.WriteInt32(0);                             // alwaysSucceeds
            w.WriteInt32(0);                             // alwaysFails
            foreach (int v in new[] { 1, 0, 0, 1, 0, 0 }) { w.WriteInt32(v); }   // STR..CHA
            foreach (int v in new[] { 0, 1, 0, 0, 0, 0, 0, 0 }) { w.WriteInt32(v); }  // PP..RL
            w.WriteByte(4);                              // strBonus -- BYTE after sixteen BOOLs
            w.WriteInt32(1);                             // compareToDie
            w.WriteInt32(20);                            // compareDie
            w.WriteInt32(2);                             // NbrTries
            w.WriteUInt32(300);                          // successChain
            w.WriteInt32(1);                             // successAction
            w.WriteInt32(2);                             // failAction
            w.WriteUInt32(400);                          // failChain
            for (int i = 0; i < 12; i++) { w.WriteInt32(i); }
        });

        var tries = Read<WhoTriesEvent>(EventType.WhoTries, data);

        Assert.Equal([1, 0, 0, 1, 0, 0], tries.AbilityChecks);
        Assert.Equal([0, 1, 0, 0, 0, 0, 0, 0], tries.ThiefSkillChecks);
        Assert.Equal((byte)4, tries.StrengthBonus);
        Assert.Equal((1, 20, 2), (tries.CompareToDie, tries.CompareDie, tries.NbrTries));
        Assert.Equal((300u, 400u), (tries.SuccessChain, tries.FailChain));
    }

    [Fact]
    public void The_ability_and_thief_lists_are_named_in_wire_order()
    {
        // The names are what a caller will index by, and the order is the one Serialize writes.
        Assert.Equal(["STR", "INT", "WIS", "DEX", "CON", "CHA"], TrialEventReaders.AbilityNames);
        Assert.Equal(["PP", "OL", "FT", "MS", "HS", "HN", "CW", "RL"],
                     TrialEventReaders.ThiefSkillNames);
    }

    // ---- the encounter ---------------------------------------------------------------------------

    [Fact]
    public void An_encounter_reads_all_five_button_slots_whatever_the_count_says()
    {
        byte[] data = Build(EventType.EncounterEvent, w =>
        {
            w.WriteInt32(1);                             // distance
            w.WriteInt32(3);                             // monsterSpeed
            w.WriteInt32(2);                             // zeroRangeResult
            w.WriteUInt32(101);                          // combatChain
            w.WriteUInt32(102);                          // talkChain
            w.WriteUInt32(103);                          // escapeChain

            w.WriteInt32(2);                             // numButtons -- says two, stores five

            for (int i = 0; i < EncounterEventReader.MaxButtons; i++)
            {
                w.WriteString(i < 2 ? $"OPTION{i}" : ArchiveStringConventions.ArchiveBlank);
                w.WriteInt32(i < 2 ? 1 : 0);             // present
                w.WriteInt32(1);                         // allowedUpClose
                w.WriteInt32(i);                         // optionResult
                w.WriteUInt32((uint)(200 + i));          // chain
                w.WriteInt32(i == 1 ? 1 : 0);            // onlyUpClose, >= 0.890
            }
        });

        var encounter = Read<EncounterEvent>(EventType.EncounterEvent, data);

        Assert.Equal(2, encounter.NumButtons);
        Assert.Equal(EncounterEventReader.MaxButtons, encounter.Options.Count);
        Assert.Equal("OPTION0", encounter.Options[0].Label);
        Assert.Equal(string.Empty, encounter.Options[4].Label);
        Assert.Equal(1, encounter.Options[1].OnlyUpClose);
        Assert.Equal(204u, encounter.Options[4].Chain);
        Assert.Equal((101u, 102u, 103u),
                     (encounter.CombatChain, encounter.TalkChain, encounter.EscapeChain));
    }

    // ---- the small ones --------------------------------------------------------------------------

    [Fact]
    public void A_journal_event_is_the_base_and_one_int()
    {
        byte[] data = Build(EventType.JournalEvent, w => w.WriteInt32(42));

        Assert.Equal(42, Read<JournalEvent>(EventType.JournalEvent, data).Entry);
    }

    [Fact]
    public void A_play_movie_event_reads_a_name_then_a_mode()
    {
        byte[] data = Build(EventType.PlayMovieEvent, w =>
        {
            w.WriteString("intro.avi");
            w.WriteInt32(2);
        });

        var movie = Read<PlayMovieEvent>(EventType.PlayMovieEvent, data);

        Assert.Equal("intro.avi", movie.FileName);
        Assert.Equal(2, movie.Mode);
    }

    [Fact]
    public void A_vault_event_is_four_bytes_and_then_one()
    {
        byte[] data = Build(EventType.Vault, w =>
        {
            w.WriteInt32(1);                             // ForceBackup, a BOOL
            w.WriteByte(2);                              // WhichVault -- BYTE, >= 0.910
        });

        var vault = Read<VaultEvent>(EventType.Vault, data);

        Assert.Equal(1, vault.ForceBackup);
        Assert.Equal((byte)2, vault.WhichVault);
    }

    [Fact]
    public void A_small_town_reads_the_unused_field_it_would_rather_skip()
    {
        byte[] data = Build(EventType.SmallTown, w =>
        {
            w.WriteInt32(-1);                            // Unused -- serialized regardless
            foreach (uint chain in new uint[] { 1, 2, 3, 4, 5, 6 }) { w.WriteUInt32(chain); }
        });

        var town = Read<SmallTownEvent>(EventType.SmallTown, data);

        Assert.Equal(-1, town.Unused);
        Assert.Equal((1u, 2u, 3u), (town.TempleChain, town.TrainingHallChain, town.ShopChain));
        Assert.Equal((4u, 5u, 6u), (town.InnChain, town.TavernChain, town.VaultChain));
    }

    [Fact]
    public void Tavern_tales_carry_one_asl_per_tale_and_one_for_the_event()
    {
        byte[] data = Build(EventType.TavernTales, w =>
        {
            w.WriteUInt32(0x7);                          // m_flags
            w.WriteInt32(2);                             // tale count

            w.WriteString("They say the well is cursed.");    // verbatim, NOT through DAS
            w.WriteUInt32(1);
            AslWriter.Write(ArchiveWriteCursor.For(w), Version, AslMaps.Tale, [new AslEntry("seen", 0, "no")]);

            w.WriteString("*");                          // a tale that really is an asterisk
            w.WriteUInt32(2);
            AslWriter.Write(ArchiveWriteCursor.For(w), Version, AslMaps.Tale, []);

            AslWriter.Write(ArchiveWriteCursor.For(w), Version, AslMaps.TavernTale, [new AslEntry("mood", 1, "dour")]);
        });

        var tales = Read<TavernTalesEvent>(EventType.TavernTales, data);

        Assert.Equal(0x7u, tales.Flags);
        Assert.Equal(2, tales.Tales.Count);
        Assert.Equal("They say the well is cursed.", tales.Tales[0].Text);
        Assert.Equal([new AslEntry("seen", 0, "no")], tales.Tales[0].Attributes);

        // Verbatim really means verbatim: the blank convention would have made this empty.
        Assert.Equal("*", tales.Tales[1].Text);

        Assert.Equal([new AslEntry("mood", 1, "dour")], tales.Attributes);
    }

    [Fact]
    public void The_two_tale_map_names_are_not_interchangeable()
    {
        // Each tale's block is "TALE" and the event's is "TAVTALE". Swapping them is the one
        // mistake in this record that announces itself, because the names are sync markers.
        byte[] data = Build(EventType.TavernTales, w =>
        {
            w.WriteUInt32(0);
            w.WriteInt32(1);
            w.WriteString("tale");
            w.WriteUInt32(0);
            AslWriter.Write(ArchiveWriteCursor.For(w), Version, AslMaps.TavernTale, []);      // wrong name here
            AslWriter.Write(ArchiveWriteCursor.For(w), Version, AslMaps.Tale, []);
        });

        var stream = new MemoryStream(data);
        Assert.Throws<InvalidDataException>(
            () => EventBodyReader.TryRead(ArchiveCursor.For(new MfcArchiveReader(stream)),
                                          EventType.TavernTales, Version, ArchiveRole.Editor));
    }

    // ---- the two that have no body at all --------------------------------------------------------

    [Theory]
    [InlineData(EventType.InnEvent)]
    [InlineData(EventType.GPDLEvent)]
    public void The_two_types_the_reference_dies_on_have_no_reader(EventType type)
    {
        // CreateNewEvent (GameEvent.cpp:3888) reaches die(0xab51a) for both -- InnEvent is
        // commented "never" and GPDLEvent is not in the switch at all. Neither can occur in a
        // design the reference could load, so there is no shape to read and no body to skip.
        Assert.True(EventDispatch.ReadsNothing(type));
        Assert.Null(EventBodyReader.TryRead(
            ArchiveCursor.For(new MfcArchiveReader(new MemoryStream([0, 0, 0, 0]))),
            type, Version, ArchiveRole.Editor));
    }

    [Fact]
    public void Every_type_the_reference_constructs_now_has_a_reader()
    {
        // EventDispatch.ClassNames is CreateNewEvent's switch. Anything in it that the dispatcher
        // has no case for stops a level walk dead at the first occurrence, because there is no way
        // to step over a body of unknown length.
        //
        // Fed garbage, a reader that exists throws; only a missing case returns null. That is the
        // distinction being tested, so the throw is the pass.
        var unreadable = EventDispatch.ClassNames.Keys.Where(NoReader).ToList();

        Assert.Empty(unreadable);

        // And the complement: exactly three ordinals have no body, for the two distinct reasons.
        // NoEvent produces no object by design; Inn and GPDL reach die(). Anything else appearing
        // here is a type that has quietly lost its reader.
        var bodyless = Enum.GetValues<EventType>()
            .Where(t => t != EventType.ControlSplash && NoReader(t))
            .ToList();

        Assert.Equal([EventType.NoEvent, EventType.InnEvent, EventType.GPDLEvent], bodyless);
    }

    private static bool NoReader(EventType type)
    {
        try
        {
            var stream = new MemoryStream(new byte[8192]);
            return EventBodyReader.TryRead(ArchiveCursor.For(new MfcArchiveReader(stream)),
                                           type, Version, ArchiveRole.Engine) is null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException
                                       or EndOfStreamException or ArgumentException
                                       or InvalidOperationException or NotSupportedException)
        {
            return false;                                // it dispatched, which is the question
        }
    }
}
