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
