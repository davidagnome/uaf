namespace UAF.Import.Frua;

/// <summary>
/// A <see cref="FruaEventType.TextStatement"/>'s payload
/// (<c>addTextEvent</c>, <c>UAFWinEd/UAImport.cpp:3540</c>).
/// </summary>
/// <remarks>
/// <b>The most common event by a wide margin</b> — 443 of the 1,040 in <c>HEIRS.DSN</c>.
/// </remarks>
/// <param name="Text">
/// The five string slots joined, with highlight markers already inserted.
/// </param>
/// <param name="ForceBackup">Whether the party is pushed back a square first.</param>
/// <param name="WaitForReturn">Whether the text waits on a keypress.</param>
/// <param name="PictureSlot">Which art slot is shown, 0 for none.</param>
/// <param name="PictureIsLarge">The high bit of the flags byte, which picks the large art.</param>
/// <param name="SoundSlot">Which sound plays, 0 for none.</param>
public sealed record FruaTextEvent(
    string Text, bool ForceBackup, bool WaitForReturn,
    byte PictureSlot, bool PictureIsLarge, byte SoundSlot)
{
    /// <summary>
    /// The marker the reference wraps a highlighted chunk in, at both ends.
    /// </summary>
    /// <remarks>
    /// Not an escape the format defines — it is the engine's own inline markup, inserted during
    /// import. A chunk with its bit set comes out as <c>/h…/h</c>.
    /// </remarks>
    public const string HighlightMarker = "/h";

    /// <summary>
    /// Reads the payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The text is five separate strings concatenated, not one.</b> Slots at offsets 9, 11, 13,
    /// 15 and 17 are looked up in turn and joined. Each string is capped at 228 characters by the
    /// decoder, so five is how FRUA expresses a long passage — and any of them may be absent, in
    /// which case it contributes nothing.
    /// </para>
    /// <para>
    /// <b>Each chunk has its own highlight bit</b>, in the flags byte at offset 8: 4, 8, 16, 32
    /// and 64 for chunks one to five. A set bit wraps that chunk in <c>/h</c> at both ends, so
    /// highlighting is per-chunk rather than per-event.
    /// </para>
    /// <para>
    /// <b><c>WaitForReturn</c> is any of five bits, not one.</b> The reference ORs masks 1, 2, 4,
    /// 8 and 16 of the byte at offset 5 — so any pause style at all means "wait".
    /// </para>
    /// </remarks>
    public static FruaTextEvent Read(FruaEvent e, FruaStringTable strings)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(strings);

        byte control = e.Byte(5);
        byte flags = e.Byte(8);
        bool large = (flags & 128) != 0;
        flags &= 127;

        var text = new System.Text.StringBuilder();

        foreach (var (at, bit) in new[] { (9, 4), (11, 8), (13, 16), (15, 32), (17, 64) })
        {
            string chunk = strings.Get(e.Word(at)) ?? string.Empty;
            bool highlight = (flags & bit) == bit;

            if (highlight)
            {
                text.Append(HighlightMarker);
            }

            text.Append(chunk);

            if (highlight)
            {
                text.Append(HighlightMarker);
            }
        }

        return new FruaTextEvent(
            Text: text.ToString(),
            ForceBackup: (control & 32) == 32,
            WaitForReturn: (control & (1 | 2 | 4 | 8 | 16)) != 0,
            PictureSlot: e.Byte(7),
            PictureIsLarge: large,
            SoundSlot: e.Byte(19));
    }
}

/// <summary>How close the monsters start.</summary>
public enum FruaCombatDistance
{
    UpClose,
    Nearby,
    FarAway,
}

/// <summary>Who, if anyone, is surprised.</summary>
public enum FruaSurprise
{
    Neither,
    PartySurprised,
    MonsterSurprised,
}

/// <summary>One of a combat event's five monster slots.</summary>
/// <param name="Quantity">How many, in the low five bits of the slot's flag byte.</param>
/// <param name="MonsterIndex">
/// Which <c>MONST###.DAT</c> record, from the byte after the flags.
/// <b>The reference reads this and throws it away</b> — see
/// <see cref="FruaCombatEvent.MonstersAreNotImported"/>.
/// </param>
public readonly record struct FruaCombatMonster(int Quantity, byte MonsterIndex);

/// <summary>
/// A <see cref="FruaEventType.Combat"/>'s payload
/// (<c>addCombatEvent</c>, <c>UAFWinEd/UAImport.cpp:2284</c>).
/// </summary>
/// <remarks>
/// Five monster slots at bytes 9, 11, 13, 15 and 17, each packing a quantity in its low five bits
/// and a different set of flags in its top three; the monster index follows in the even byte after.
/// </remarks>
public sealed record FruaCombatEvent(
    ushort TextSlot, byte PictureSlot, bool PictureIsLarge, int MonsterMorale,
    bool Outdoors, FruaSurprise Surprise, bool AutoApproach, bool PartyNeverDies,
    bool NoMonsterTreasure, bool NoMagic, FruaCombatDistance Distance,
    IReadOnlyList<FruaCombatMonster> Monsters)
{
    /// <summary>
    /// <b>The reference importer does not import combat monsters at all.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every one of the five <c>monster.monster = GetMonsterKey(...)</c> assignments in
    /// <c>addCombatEvent</c> is commented out and replaced by a <c>NotImplemented(...)</c> marker
    /// — six of the fourteen such markers in <c>UAImport.cpp</c>, with the rest clustered on NPCs.
    /// So a design imported by the reference gets combat events carrying quantities, morale,
    /// surprise and distance, and <b>no monsters</b>.
    /// </para>
    /// <para>
    /// <b>This port reads the indices anyway</b>, because they are in the file and a reader that
    /// discarded them would be throwing away data the format has. It matters for the byte-identity
    /// exit criterion, though: an importer that <i>writes</i> those monsters out would produce a
    /// richer design than the reference and fail the diff. The gap belongs in the writer, not here.
    /// </para>
    /// <para>
    /// <c>NotImplemented</c> shows a message box, deduplicated per code. Every import call site
    /// passes <c>loopForever: false</c>, and <c>MsgBoxInfo</c> honours <c>g_headlessMode</c>, so
    /// under <c>-importfrua</c> these are silent. Without that flag they would be fourteen modal
    /// dialogs.
    /// </para>
    /// </remarks>
    public const string MonstersAreNotImported =
        "addCombatEvent's monster assignments are commented out behind NotImplemented markers";

    /// <summary>Reads the payload.</summary>
    public static FruaCombatEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte pic = e.Byte(8);
        byte first = e.Byte(9);
        byte third = e.Byte(13);
        byte fourth = e.Byte(15);

        var monsters = new FruaCombatMonster[5];
        for (int i = 0; i < 5; i++)
        {
            int at = 9 + (i * 2);
            monsters[i] = new FruaCombatMonster(e.Byte(at) & 0x1F, e.Byte(at + 1));
        }

        return new FruaCombatEvent(
            TextSlot: e.Word(5),
            PictureSlot: e.Byte(7),
            PictureIsLarge: (pic & 128) != 0,

            // Morale shares the picture byte, below its high bit.
            MonsterMorale: pic & 0x7F,
            Outdoors: (first & 32) == 32,
            Surprise: (first & 64) == 64 ? FruaSurprise.PartySurprised
                    : (first & 128) == 128 ? FruaSurprise.MonsterSurprised
                    : FruaSurprise.Neither,
            AutoApproach: (third & 32) == 32,
            PartyNeverDies: (third & 64) == 64,
            NoMonsterTreasure: (third & 128) == 128,
            NoMagic: (fourth & 128) == 128,
            Distance: (fourth & 32) == 32 ? FruaCombatDistance.Nearby
                    : (fourth & 64) == 64 ? FruaCombatDistance.FarAway
                    : FruaCombatDistance.UpClose,
            Monsters: monsters);
    }
}

/// <summary>
/// A treasure payload — <see cref="FruaEventType.GiveTreasure"/> and
/// <see cref="FruaEventType.CombatTreasure"/> share it
/// (<c>addTreasureEvent</c>, <c>UAFWinEd/UAImport.cpp:2439</c>).
/// </summary>
/// <param name="Platinum">Coins, as a count.</param>
/// <param name="Gems">
/// <b>A count of gems, not a value.</b> The reference loops <c>AddGem()</c> that many times, so
/// each is worth whatever the design's gem table says.
/// </param>
/// <param name="Jewelry">Likewise a count.</param>
/// <param name="ItemSlots">
/// Eight item-database indices at offsets 13–20; 0 means an empty slot.
/// </param>
/// <param name="ItemsAreIdentified">
/// Whether the party receives the items identified. One flag for all eight — the reference passes
/// the same <c>id</c> to every <c>AssignItem</c> call.
/// </param>
public sealed record FruaTreasureEvent(
    ushort Platinum, ushort Gems, ushort Jewelry,
    IReadOnlyList<byte> ItemSlots, bool ItemsAreIdentified)
{
    /// <summary>How many item slots a treasure carries.</summary>
    public const int ItemSlotCount = 8;

    /// <summary>Reads the payload.</summary>
    /// <remarks>
    /// <b>Offset 7 is skipped.</b> The three money words sit at 5, 9 and 11 — not 5, 7 and 9 — so
    /// there is a two-byte hole after the platinum that only the identified flag at offset 8 reads
    /// into. Reading the words consecutively would take jewelry for gems.
    /// </remarks>
    public static FruaTreasureEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var slots = new byte[ItemSlotCount];
        for (int i = 0; i < ItemSlotCount; i++)
        {
            slots[i] = e.Byte(13 + i);
        }

        return new FruaTreasureEvent(
            Platinum: e.Word(5),
            Gems: e.Word(9),
            Jewelry: e.Word(11),
            ItemSlots: slots,
            ItemsAreIdentified: (e.Byte(8) & 128) == 128);
    }

    /// <summary>The non-empty item slots.</summary>
    public IEnumerable<byte> Items() => ItemSlots.Where(s => s != 0);
}

/// <summary>What a special-item event does with the object it names.</summary>
public enum FruaSpecialObjectOperation
{
    Give,
    Take,
}

/// <summary>
/// A <see cref="FruaEventType.SpecialItem"/>'s payload
/// (<c>addSpecialItemEvent</c>, <c>UAFWinEd/UAImport.cpp:3888</c>).
/// </summary>
/// <param name="TextSlot">The message shown, 0 for none.</param>
/// <param name="Operation">Whether the object is given or taken.</param>
/// <param name="ObjectKind">Whether the byte names a key, an item or a quest.</param>
/// <param name="ObjectIndex">Its zero-based index within that kind.</param>
public sealed record FruaSpecialItemEvent(
    ushort TextSlot, byte PictureSlot, bool PictureIsLarge,
    FruaSpecialObjectOperation Operation,
    FruaObjectKind ObjectKind, int ObjectIndex)
{
    /// <summary>Reads the payload.</summary>
    /// <remarks>
    /// <b>Give is the <i>only</i> zero case; every other flag value takes.</b> The reference tests
    /// <c>temp == 0</c> after masking off the high bit, rather than testing a specific bit — so a
    /// flags byte with any low bit set means take, whatever that bit was meant for.
    /// </remarks>
    public static FruaSpecialItemEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);
        bool large = (flags & 128) != 0;
        flags &= 127;

        byte obj = e.Byte(9);

        return new FruaSpecialItemEvent(
            TextSlot: e.Word(5),
            PictureSlot: e.Byte(7),
            PictureIsLarge: large,
            Operation: flags == 0 ? FruaSpecialObjectOperation.Give
                                  : FruaSpecialObjectOperation.Take,
            ObjectKind: FruaEvent.ObjectKind(obj),
            ObjectIndex: FruaEvent.ObjectIndex(obj));
    }
}

/// <summary>Who a damage event falls on.</summary>
public enum FruaDamageTarget
{
    EntireParty,
    ActiveCharacter,
    OneAtRandom,
    ChanceOnEach,
}

/// <summary>Whether a saving throw applies, and what it does.</summary>
public enum FruaDamageSave
{
    NoSave,
    SaveForHalf,
    SaveNegates,
    UseThac0,
}

/// <summary>Which saving-throw column the save is rolled against.</summary>
public enum FruaSpellSave
{
    ParalysisPoisonDeath,
    PetrifyPolymorph,
    RodStaffWand,
    BreathWeapon,
    Spell,
}

/// <summary>
/// A <see cref="FruaEventType.Damage"/>'s payload
/// (<c>addGiveDamageEvent</c>, <c>UAFWinEd/UAImport.cpp:2471</c>).
/// </summary>
public sealed record FruaDamageEvent(
    ushort TextSlot, byte PictureSlot, bool PictureIsLarge,
    FruaDamageTarget Target, FruaDamageSave Save, FruaSpellSave SpellSave, int SaveBonus,
    byte Attacks, byte DiceCount, byte DiceSides, byte DamageBonus,
    int Thac0, FruaCombatDistance Distance, byte ChancePerAttack)
{
    /// <summary>Reads the payload.</summary>
    /// <remarks>
    /// <para>
    /// <b>THAC0 is stored as <c>60 - value</c></b>, the same inversion the monster records use for
    /// armour class. The two are the same convention, applied in different files.
    /// </para>
    /// <para>
    /// <b>Three mask ladders here are order-dependent, and each tests a combined value first</b> —
    /// 12 before 4 and 8 for the target, 48 before 16 and 32 for the save, and 48 again before 16,
    /// 32 and 64 for the saving-throw column. Reversing any of them makes the combined case
    /// unreachable.
    /// </para>
    /// </remarks>
    public static FruaDamageEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);
        bool large = (flags & 128) != 0;
        flags &= 127;

        byte save = e.Byte(14);
        byte distance = e.Byte(15);

        return new FruaDamageEvent(
            TextSlot: e.Word(5),
            PictureSlot: e.Byte(7),
            PictureIsLarge: large,
            Target: (flags & 12) == 12 ? FruaDamageTarget.ChanceOnEach
                  : (flags & 4) == 4 ? FruaDamageTarget.ActiveCharacter
                  : (flags & 8) == 8 ? FruaDamageTarget.OneAtRandom
                  : FruaDamageTarget.EntireParty,
            Save: (flags & 48) == 48 ? FruaDamageSave.UseThac0
                : (flags & 16) == 16 ? FruaDamageSave.SaveForHalf
                : (flags & 32) == 32 ? FruaDamageSave.SaveNegates
                : FruaDamageSave.NoSave,
            SpellSave: (save & 48) == 48 ? FruaSpellSave.BreathWeapon
                     : (save & 16) == 16 ? FruaSpellSave.PetrifyPolymorph
                     : (save & 32) == 32 ? FruaSpellSave.RodStaffWand
                     : (save & 64) == 64 ? FruaSpellSave.Spell
                     : FruaSpellSave.ParalysisPoisonDeath,
            SaveBonus: save & 0x0F,
            Attacks: e.Byte(9),
            DiceCount: e.Byte(10),
            DiceSides: e.Byte(11),
            DamageBonus: e.Byte(12),
            Thac0: 60 - e.Byte(13),
            Distance: (distance & 32) == 32 ? FruaCombatDistance.Nearby
                    : (distance & 64) == 64 ? FruaCombatDistance.FarAway
                    : FruaCombatDistance.UpClose,
            ChancePerAttack: e.Byte(17));
    }
}

/// <summary>
/// A <see cref="FruaEventType.Sounds"/>'s payload
/// (<c>addSoundEvent</c>, <c>UAFWinEd/UAImport.cpp:1980</c>).
/// </summary>
/// <remarks>
/// <b>Ten slots and nothing else</b> — no text, no picture, no flags. The reference unrolls the
/// same three lines ten times over offsets 5 to 14, appending each non-zero slot in order.
/// </remarks>
public sealed record FruaSoundEvent(IReadOnlyList<byte> SoundSlots)
{
    /// <summary>How many sound slots the event carries.</summary>
    public const int SlotCount = 10;

    /// <summary>Reads the payload.</summary>
    public static FruaSoundEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var slots = new byte[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            slots[i] = e.Byte(5 + i);
        }

        return new FruaSoundEvent(slots);
    }

    /// <summary>The slots that name a sound, in order.</summary>
    public IEnumerable<byte> Sounds() => SoundSlots.Where(s => s != 0);
}

/// <summary>How a quest is taken on.</summary>
public enum FruaQuestAccept
{
    /// <summary>The flags byte is zero — the quest cannot be accepted at all.</summary>
    Impossible,
    OnYes,
    OnNo,
    OnYesOrNo,
    ImpossibleAuto,
    AutoAccept,

    /// <summary>Non-zero flags matching none of the masks. The reference leaves the field alone.</summary>
    Unchanged,
}

/// <summary>
/// A <see cref="FruaEventType.QuestStage"/>'s payload
/// (<c>addQuestEvent</c>, <c>UAFWinEd/UAImport.cpp:2081</c>).
/// </summary>
public sealed record FruaQuestEvent(
    ushort TextSlot, byte PictureSlot, bool PictureIsLarge,
    FruaQuestAccept Accept, bool CompleteOnAccept, bool FailOnRejection,
    int QuestIndex, int Stage)
{
    /// <summary>Reads the payload.</summary>
    /// <remarks>
    /// <para>
    /// <b>The acceptance ladder tests overlapping masks widest-first, and has a hole.</b> Zero is
    /// impossible; then 40 (32|8), 32, 24 (16|8), 16, 8 in that order. A non-zero value matching
    /// none of them — 4 alone, say — falls out of every branch and the reference leaves the field
    /// at whatever the event was constructed with. Reported as <see cref="FruaQuestAccept.Unchanged"/>
    /// rather than guessed at.
    /// </para>
    /// <para>
    /// <b>The stage is stored zero-based and read one-based</b> — the reference adds one — which is
    /// the opposite direction to the level and entry-point counters, which are stored one-based and
    /// decremented.
    /// </para>
    /// <para>
    /// <b>The quest byte is always read as a quest</b>, through <c>SetQuestTypeAndID(QUEST_FLAG,
    /// ...)</c>, even though <c>GetObjectKey</c> would classify a value under 20 as a key or an
    /// item. A design storing a low number here gets a quest with that object's index.
    /// </para>
    /// </remarks>
    public static FruaQuestEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte raw = e.Byte(8);
        byte flags = (byte)(raw & 127);

        return new FruaQuestEvent(
            TextSlot: e.Word(5),
            PictureSlot: e.Byte(7),
            PictureIsLarge: (raw & 128) != 0,
            Accept: flags == 0 ? FruaQuestAccept.Impossible
                  : (flags & 40) == 40 ? FruaQuestAccept.AutoAccept
                  : (flags & 32) == 32 ? FruaQuestAccept.ImpossibleAuto
                  : (flags & 24) == 24 ? FruaQuestAccept.OnYesOrNo
                  : (flags & 16) == 16 ? FruaQuestAccept.OnNo
                  : (flags & 8) == 8 ? FruaQuestAccept.OnYes
                  : FruaQuestAccept.Unchanged,
            CompleteOnAccept: (flags & 64) == 64,
            FailOnRejection: (flags & 4) == 4,
            QuestIndex: FruaEvent.ObjectIndex(e.Byte(9)),

            // Stored zero-based; the reference adds one.
            Stage: e.Byte(10) + 1);
    }
}

/// <summary>
/// What a town service charges, as a multiplier on the base price
/// (<c>ConvertCostModifier</c>, <c>UAFWinEd/UAImport.cpp:1240</c>).
/// </summary>
/// <remarks>
/// <b>FRUA stores a modifier, not a price.</b> The values run 0–19 as a scale from free through
/// hundredfold, with <see cref="Normal"/> at 10 in the middle. Anything above 19 falls to
/// <see cref="Free"/>, because the reference initialises to <c>Free</c> and its switch has no
/// default.
/// </remarks>
public enum FruaCostFactor
{
    Free = 0,
    Div100 = 1,
    Div50 = 2,
    Div20 = 3,
    Div10 = 4,
    Div5 = 5,
    Div4 = 6,
    Div3 = 7,
    Div2 = 8,
    Div1_5 = 9,
    Normal = 10,
    Mult1_5 = 11,
    Mult2 = 12,
    Mult3 = 13,
    Mult4 = 14,
    Mult5 = 15,
    Mult10 = 16,
    Mult20 = 17,
    Mult50 = 18,
    Mult100 = 19,
}

/// <summary>Turning a stored byte into a cost factor.</summary>
public static class FruaCost
{
    /// <summary>
    /// The factor for a stored byte; out-of-range values are <see cref="FruaCostFactor.Free"/>.
    /// </summary>
    public static FruaCostFactor Factor(byte stored) =>
        Enum.IsDefined((FruaCostFactor)stored) ? (FruaCostFactor)stored : FruaCostFactor.Free;

    /// <summary>The multiplier a factor applies to a base price.</summary>
    public static double Multiplier(FruaCostFactor factor) => factor switch
    {
        FruaCostFactor.Free => 0,
        FruaCostFactor.Div100 => 1.0 / 100,
        FruaCostFactor.Div50 => 1.0 / 50,
        FruaCostFactor.Div20 => 1.0 / 20,
        FruaCostFactor.Div10 => 1.0 / 10,
        FruaCostFactor.Div5 => 1.0 / 5,
        FruaCostFactor.Div4 => 1.0 / 4,
        FruaCostFactor.Div3 => 1.0 / 3,
        FruaCostFactor.Div2 => 1.0 / 2,
        FruaCostFactor.Div1_5 => 1.0 / 1.5,
        FruaCostFactor.Normal => 1,
        FruaCostFactor.Mult1_5 => 1.5,
        FruaCostFactor.Mult2 => 2,
        FruaCostFactor.Mult3 => 3,
        FruaCostFactor.Mult4 => 4,
        FruaCostFactor.Mult5 => 5,
        FruaCostFactor.Mult10 => 10,
        FruaCostFactor.Mult20 => 20,
        FruaCostFactor.Mult50 => 50,
        _ => 100,
    };
}

/// <summary>
/// A <see cref="FruaEventType.Temple"/>'s payload
/// (<c>addTempleEvent</c>, <c>UAFWinEd/UAImport.cpp:3760</c>).
/// </summary>
/// <param name="DonationTrigger">
/// A dword at offset 9 — the donation amount that fires the temple's trigger.
/// </param>
public sealed record FruaTempleEvent(
    ushort TextSlot, ushort SecondTextSlot, byte PictureSlot, bool PictureIsLarge,
    FruaCostFactor CostFactor, bool ForceExit, bool AllowDonations, uint DonationTrigger)
{
    /// <summary>Reads the payload.</summary>
    /// <remarks>
    /// <b>The two text slots are at 14 and 16, not 5 and 7.</b> A temple's message sits after the
    /// donation dword rather than at the front, which is where every other event with text keeps
    /// it — offset 5 here is a spell-limit byte the reference has commented out.
    /// </remarks>
    public static FruaTempleEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);

        return new FruaTempleEvent(
            TextSlot: e.Word(14),
            SecondTextSlot: e.Word(16),
            PictureSlot: e.Byte(7),
            PictureIsLarge: (flags & 128) != 0,
            CostFactor: FruaCost.Factor(e.Byte(6)),
            ForceExit: ((flags & 127) & 4) == 4,
            AllowDonations: ((flags & 127) & 8) == 8,
            DonationTrigger: e.Dword(9));
    }
}

/// <summary>
/// A <see cref="FruaEventType.TrainingHall"/>'s payload
/// (<c>addTrainingHallEvent</c>, <c>UAFWinEd/UAImport.cpp:3923</c>).
/// </summary>
public sealed record FruaTrainingHallEvent(
    ushort TextSlot, byte PictureSlot, bool PictureIsLarge,
    FruaCostFactor CostFactor, bool ForceExit, byte ClassFlags)
{
    /// <summary>The base price the cost factor is applied to.</summary>
    public const int BaseCost = 1000;

    /// <summary>
    /// <b>Which classes a hall trains is not imported.</b>
    /// </summary>
    /// <remarks>
    /// The six <c>data->TrainMagicUser = HasMask(temp, 1)</c> assignments are commented out behind
    /// a <c>NotImplemented(0xea31b)</c> marker — one of the fourteen in <c>UAImport.cpp</c>. So a
    /// training hall imported by the reference trains whatever the event class was constructed
    /// with, and the flags byte at offset 9 is read into a local and discarded.
    /// <para>
    /// <see cref="ClassFlags"/> carries that byte anyway, since it is in the file: bit 1 magic
    /// user, 2 cleric, 4 thief, 8 fighter, 16 paladin, 32 ranger. As with the combat monsters,
    /// a <i>writer</i> aiming at byte-identity would have to discard it again.
    /// </para>
    /// </remarks>
    public const string ClassesAreNotImported =
        "addTrainingHallEvent's per-class flags are commented out behind NotImplemented(0xea31b)";

    /// <summary>The cost after the factor is applied to <see cref="BaseCost"/>.</summary>
    public double Cost => FruaCost.Multiplier(CostFactor) * BaseCost;

    /// <summary>Reads the payload.</summary>
    public static FruaTrainingHallEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);

        return new FruaTrainingHallEvent(
            TextSlot: e.Word(5),
            PictureSlot: e.Byte(7),
            PictureIsLarge: (flags & 128) != 0,

            // The cost modifier is at 10, after the discarded class flags at 9.
            CostFactor: FruaCost.Factor(e.Byte(10)),
            ForceExit: ((flags & 127) & 4) == 4,
            ClassFlags: e.Byte(9));
    }
}

/// <summary>What a utilities event does with its amount.</summary>
public enum FruaMathOperation
{
    /// <summary>Low two bits of 0 — the reference's switch has no case, so nothing is assigned.</summary>
    None = 0,
    StoredIn = 1,
    AddedTo = 2,
    SubtractedFrom = 3,
}

/// <summary>How many of the checked objects the party must hold.</summary>
public enum FruaItemCheck
{
    None,
    AllItems,
    AtLeastOneItem,
}

/// <summary>
/// A <see cref="FruaEventType.Utilities"/>'s payload
/// (<c>addUtilitiesEvent</c>, <c>UAFWinEd/UAImport.cpp:2018</c>).
/// </summary>
/// <param name="CheckedObjects">
/// Four object bytes at offsets 8–11, each naming a key, item or quest to test for.
/// </param>
public sealed record FruaUtilitiesEvent(
    FruaMathOperation Operation, FruaItemCheck ItemCheck, bool EndPlay,
    FruaObjectKind MathObjectKind, int MathObjectIndex, byte MathAmount,
    IReadOnlyList<byte> CheckedObjects)
{
    /// <summary>How many objects the event tests for.</summary>
    public const int CheckedObjectCount = 4;

    /// <summary>Reads the payload.</summary>
    /// <remarks>
    /// <b>A low-two-bits value of 0 leaves the operation unset.</b> The reference switches on
    /// <c>op &amp; 3</c> with cases for 1, 2 and 3 and no default, so zero assigns nothing —
    /// reported as <see cref="FruaMathOperation.None"/> rather than guessed at.
    /// </remarks>
    public static FruaUtilitiesEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte op = e.Byte(5);
        byte mathObject = e.Byte(6);

        var checks = new byte[CheckedObjectCount];
        for (int i = 0; i < CheckedObjectCount; i++)
        {
            checks[i] = e.Byte(8 + i);
        }

        return new FruaUtilitiesEvent(
            Operation: (FruaMathOperation)(op & 3),
            ItemCheck: (op & 4) == 4 ? FruaItemCheck.AllItems
                     : (op & 8) == 8 ? FruaItemCheck.AtLeastOneItem
                     : FruaItemCheck.None,
            EndPlay: (op & 16) == 16,
            MathObjectKind: FruaEvent.ObjectKind(mathObject),
            MathObjectIndex: FruaEvent.ObjectIndex(mathObject),
            MathAmount: e.Byte(7),
            CheckedObjects: checks);
    }
}

/// <summary>
/// A <see cref="FruaEventType.GainExperience"/>'s payload
/// (<c>addGiveExpEvent</c>, <c>UAFWinEd/UAImport.cpp:2151</c>).
/// </summary>
public sealed record FruaGainExperienceEvent(
    ushort TextSlot, byte PictureSlot, bool PictureIsLarge,
    bool ActiveCharacterOnly, uint Experience, byte SoundSlot)
{
    /// <summary>
    /// <b>The chance is hard-coded to 100, not read.</b> The reference assigns
    /// <c>data->chance = 100</c> after everything else, so the event always fires.
    /// </summary>
    public const int Chance = 100;

    /// <summary>Reads the payload.</summary>
    public static FruaGainExperienceEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);

        return new FruaGainExperienceEvent(
            TextSlot: e.Word(5),
            PictureSlot: e.Byte(7),
            PictureIsLarge: (flags & 128) != 0,

            // Only two possibilities here, unlike the damage event's four.
            ActiveCharacterOnly: ((flags & 127) & 4) == 4,
            Experience: e.Dword(9),
            SoundSlot: e.Byte(13));
    }
}

/// <summary>What follows an answer to a yes/no question.</summary>
public enum FruaChainAction
{
    DoNothing,
    ReturnToQuestion,
    BackupOneStep,
}

/// <summary>
/// A <see cref="FruaEventType.QuestionYesNo"/>'s payload
/// (<c>addQYesNoEvent</c>, <c>UAFWinEd/UAImport.cpp:3846</c>).
/// </summary>
public sealed record FruaQuestionYesNoEvent(
    ushort TextSlot, ushort YesTextSlot, ushort NoTextSlot,
    byte PictureSlot, bool PictureIsLarge,
    FruaChainAction OnYes, FruaChainAction OnNo)
{
    /// <summary>Reads the payload.</summary>
    /// <remarks>
    /// <b>Zero flags means both answers do nothing; otherwise each answer has its own pair of
    /// bits</b> — 4 and 32 for yes, 8 and 16 for no. A non-zero byte setting neither of an answer's
    /// bits leaves that answer's action unassigned in the reference, which is reported here as
    /// <see cref="FruaChainAction.DoNothing"/> since that is what the field was cleared to.
    /// </remarks>
    public static FruaQuestionYesNoEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte raw = e.Byte(8);
        byte flags = (byte)(raw & 127);

        return new FruaQuestionYesNoEvent(
            TextSlot: e.Word(5),
            YesTextSlot: e.Word(11),
            NoTextSlot: e.Word(13),
            PictureSlot: e.Byte(7),
            PictureIsLarge: (raw & 128) != 0,
            OnYes: flags == 0 ? FruaChainAction.DoNothing
                 : (flags & 4) == 4 ? FruaChainAction.ReturnToQuestion
                 : (flags & 32) == 32 ? FruaChainAction.BackupOneStep
                 : FruaChainAction.DoNothing,
            OnNo: flags == 0 ? FruaChainAction.DoNothing
                : (flags & 8) == 8 ? FruaChainAction.ReturnToQuestion
                : (flags & 16) == 16 ? FruaChainAction.BackupOneStep
                : FruaChainAction.DoNothing);
    }
}

/// <summary>
/// A <see cref="FruaEventType.Vault"/>'s payload
/// (<c>addVaultEvent</c>, <c>UAFWinEd/UAImport.cpp:2136</c>).
/// </summary>
/// <remarks>Text, a picture and one flag — the smallest payload in the format.</remarks>
public sealed record FruaVaultEvent(
    ushort TextSlot, byte PictureSlot, bool PictureIsLarge, bool ForceBackup)
{
    /// <summary>Reads the payload.</summary>
    public static FruaVaultEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);

        return new FruaVaultEvent(
            TextSlot: e.Word(5),
            PictureSlot: e.Byte(7),
            PictureIsLarge: (flags & 128) != 0,
            ForceBackup: ((flags & 127) & 4) == 4);
    }
}

/// <summary>
/// A <see cref="FruaEventType.PassTime"/>'s payload
/// (<c>addPassTimeEvent</c>, <c>UAFWinEd/UAImport.cpp:3909</c>).
/// </summary>
/// <remarks>
/// <b>Three of its fields are hard-coded, not read.</b> The reference sets <c>AllowStop</c>,
/// <c>PassSilent</c> and <c>SetTime</c> to <c>FALSE</c> after reading the duration, so an imported
/// pass-time event can never stop early, is never silent, and never sets an absolute time.
/// </remarks>
public sealed record FruaPassTimeEvent(ushort TextSlot, byte Days, byte Hours, byte Minutes)
{
    /// <summary>Reads the payload.</summary>
    public static FruaPassTimeEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        return new FruaPassTimeEvent(
            TextSlot: e.Word(5),
            Days: e.Byte(9),
            Hours: e.Byte(10),
            Minutes: e.Byte(11));
    }
}

/// <summary>One move of a guided tour.</summary>
public enum FruaTourStep
{
    Pause = 0,
    Left = 1,
    Right = 2,
    Forward = 3,
}

/// <summary>
/// A <see cref="FruaEventType.GuidedTour"/>'s payload
/// (<c>addTourEvent</c>, <c>UAFWinEd/UAImport.cpp:3612</c>).
/// </summary>
public sealed record FruaGuidedTourEvent(
    byte StartX, byte StartY, FruaFacing Facing,
    bool UseStartLocation, bool ExecuteEvent, IReadOnlyList<FruaTourStep> Steps)
{
    /// <summary>The most steps a tour can hold.</summary>
    public const int MaxSteps = 24;

    /// <summary>Reads the payload.</summary>
    /// <remarks>
    /// <para>
    /// <b>Four steps to a byte, two bits each</b>, over the six bytes at offsets 9–14 — which is
    /// exactly the 24 of <c>MAX_TOUR_STEPS</c>. The reference spells all four out as separate mask
    /// ladders (3/2/1, then 12/8/4, then 48/32/16, then 192/128/64), but every one reduces to the
    /// two-bit value itself: 3 forward, 2 right, 1 left, 0 pause.
    /// </para>
    /// <para>
    /// <b>The step count at offset 7 truncates the list</b>, so a tour can store more steps than it
    /// walks. Steps past it are not read.
    /// </para>
    /// <para>
    /// <b>The facing ladder is the transfer family's, widest-first</b> — 12 before 4 and 8 — on
    /// the same byte that carries the two flags at 16 and 32.
    /// </para>
    /// </remarks>
    public static FruaGuidedTourEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);
        int wanted = Math.Min((int)e.Byte(7), MaxSteps);
        var steps = new List<FruaTourStep>(wanted);

        for (int at = 9; at <= 14 && steps.Count < wanted; at++)
        {
            byte packed = e.Byte(at);

            for (int field = 0; field < 4 && steps.Count < wanted; field++)
            {
                steps.Add((FruaTourStep)((packed >> (field * 2)) & 3));
            }
        }

        return new FruaGuidedTourEvent(
            StartX: e.Byte(6),
            StartY: e.Byte(5),
            Facing: (flags & 12) == 12 ? FruaFacing.West
                  : (flags & 4) == 4 ? FruaFacing.East
                  : (flags & 8) == 8 ? FruaFacing.South
                  : FruaFacing.North,
            UseStartLocation: (flags & 16) == 16,
            ExecuteEvent: (flags & 32) == 32,
            Steps: steps);
    }
}

/// <summary>
/// A <see cref="FruaEventType.Tavern"/>'s payload
/// (<c>addTavernEvent</c>, <c>UAFWinEd/UAImport.cpp:3700</c>).
/// </summary>
public sealed record FruaTavernEvent(
    ushort TextSlot, byte PictureSlot, bool PictureIsLarge,
    bool AllowFights, bool AllowDrinks, bool TalesInRandomOrder,
    IReadOnlyList<ushort> TaleSlots)
{
    /// <summary>How many tales a tavern holds.</summary>
    public const int TaleCount = 4;

    /// <summary>Reads the payload.</summary>
    public static FruaTavernEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte raw = e.Byte(8);
        byte flags = (byte)(raw & 127);

        var tales = new ushort[TaleCount];
        for (int i = 0; i < TaleCount; i++)
        {
            tales[i] = e.Word(9 + (i * 2));
        }

        return new FruaTavernEvent(
            TextSlot: e.Word(5),
            PictureSlot: e.Byte(7),
            PictureIsLarge: (raw & 128) != 0,
            AllowFights: (flags & 16) == 16,
            AllowDrinks: (flags & 32) == 32,
            TalesInRandomOrder: (flags & 8) == 8,
            TaleSlots: tales);
    }
}

/// <summary>
/// A <see cref="FruaEventType.QuestionButton"/>'s payload
/// (<c>addQButtonEvent</c>, <c>UAFWinEd/UAImport.cpp:3806</c>).
/// </summary>
public sealed record FruaQuestionButtonEvent(
    ushort TextSlot, ushort LabelSlot, byte PictureSlot, bool PictureIsLarge,
    IReadOnlyList<FruaChainAction> ButtonActions)
{
    /// <summary>How many buttons the event always has.</summary>
    /// <remarks>
    /// <b>All five are marked present unconditionally</b> — the reference sets
    /// <c>numListButtons = 5</c> and every <c>present = TRUE</c> before reading anything, so a
    /// design cannot offer fewer. An empty label is what makes one look absent.
    /// </remarks>
    public const int ButtonCount = 5;

    /// <summary>The character separating the labels in the label string.</summary>
    /// <remarks>
    /// <b>All five labels come from ONE string, caret-delimited</b> — the reference walks it with
    /// <c>strchr(buffer, '^')</c> pairs. So the labels share the 228-character budget of a single
    /// six-bit string between them.
    /// </remarks>
    public const char LabelSeparator = '^';

    /// <summary>Reads the payload.</summary>
    public static FruaQuestionButtonEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte raw = e.Byte(8);
        byte flags = (byte)(raw & 127);

        // One bit per button: 4, 8, 16, 32, 64.
        var actions = new FruaChainAction[ButtonCount];
        for (int i = 0; i < ButtonCount; i++)
        {
            int bit = 4 << i;
            actions[i] = (flags & bit) == bit
                ? FruaChainAction.ReturnToQuestion
                : FruaChainAction.DoNothing;
        }

        return new FruaQuestionButtonEvent(
            TextSlot: e.Word(5),
            LabelSlot: e.Word(9),
            PictureSlot: e.Byte(7),
            PictureIsLarge: (raw & 128) != 0,
            ButtonActions: actions);
    }

    /// <summary>
    /// Splits a label string into its five labels.
    /// </summary>
    /// <remarks>
    /// The reference reads pairs of carets, so a string of <c>^one^two^</c> yields "one" then
    /// "two"; anything before the first caret is ignored, and missing labels come out empty.
    /// </remarks>
    public static IReadOnlyList<string> Labels(string? labelString)
    {
        var labels = new string[ButtonCount];
        Array.Fill(labels, string.Empty);

        if (string.IsNullOrEmpty(labelString))
        {
            return labels;
        }

        // Everything before the first separator is not a label.
        var parts = labelString.Split(LabelSeparator);
        for (int i = 1; i < parts.Length && i - 1 < ButtonCount; i++)
        {
            labels[i - 1] = parts[i];
        }

        return labels;
    }
}

/// <summary>Where a transfer event sends the party facing.</summary>
public enum FruaTransferFacing
{
    North,
    East,
    South,
    West,
}

/// <summary>
/// The payload shared by <see cref="FruaEventType.Teleporter"/>,
/// <see cref="FruaEventType.Stairs"/> and <see cref="FruaEventType.TransferModule"/>
/// (<c>addTeleporterEvent</c>/<c>addStairsEvent</c>, <c>UAFWinEd/UAImport.cpp:3657</c>).
/// </summary>
/// <remarks>
/// <b>The three readers are the same code three times over.</b> They differ only in which event
/// class they cast to, so one reader serves all three here — 187 of <c>HEIRS.DSN</c>'s events
/// between them.
/// </remarks>
public sealed record FruaTransferEvent(
    ushort TextSlot, ushort ConfirmTextSlot,
    byte PictureSlot, bool PictureIsLarge,
    bool AskYesNo, bool TransferOnYes,
    FruaTransferFacing Facing,
    byte DestinationX, byte DestinationY,
    int DestinationEntryPoint, bool ExecuteDestinationEvent)
{
    /// <summary>Reads the payload.</summary>
    /// <remarks>
    /// <para>
    /// <b>The facing is a two-bit field tested as masks, and the order matters.</b> The reference
    /// asks for 12 first — both bits — then 4, then 8, falling through to north. So 4|8 is west,
    /// 4 alone east, 8 alone south. Testing 4 before 12 would turn every west into an east.
    /// </para>
    /// <para>
    /// <b><c>transferOnYes</c> is inverted</b>: the reference reads <c>((temp &amp; 64) == 0)</c>,
    /// so the bit being <i>set</i> means transfer on <i>no</i>.
    /// </para>
    /// <para>
    /// <b>An entry point of 0 becomes -1, i.e. none.</b> The stored value is decremented to make
    /// it zero-based, and the reference then maps the result 0 back to -1 — so entry point 1 and
    /// entry point 0 both mean "use the coordinates instead".
    /// </para>
    /// </remarks>
    public static FruaTransferEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);
        bool large = (flags & 128) != 0;
        flags &= 127;

        byte destination = e.Byte(13);
        int entryPoint = -1;

        if ((destination & 1) == 1)
        {
            entryPoint = e.Byte(14) - 1;
            if (entryPoint == 0)
            {
                entryPoint = -1;
            }
        }

        return new FruaTransferEvent(
            TextSlot: e.Word(5),
            ConfirmTextSlot: e.Word(11),
            PictureSlot: e.Byte(7),
            PictureIsLarge: large,
            AskYesNo: (flags & 32) == 32,
            TransferOnYes: (flags & 64) == 0,
            Facing: FacingOf(flags),
            DestinationX: e.Byte(10),
            DestinationY: e.Byte(9),
            DestinationEntryPoint: entryPoint,
            ExecuteDestinationEvent: (destination & 4) == 4);
    }

    private static FruaTransferFacing FacingOf(byte flags)
    {
        // 12 first: it is both bits, and either of the narrower tests would match it.
        if ((flags & 12) == 12) { return FruaTransferFacing.West; }
        if ((flags & 4) == 4) { return FruaTransferFacing.East; }
        if ((flags & 8) == 8) { return FruaTransferFacing.South; }
        return FruaTransferFacing.North;
    }
}
