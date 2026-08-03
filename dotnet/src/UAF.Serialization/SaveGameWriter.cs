using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes a saved game (<c>.pty</c>) as far as the global vaults — the inverse of
/// <see cref="SaveGameReader"/> (<c>serializeGame</c>, <c>UAFWin/Dgngame.cpp:420</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This does not produce a loadable save, and the boundary is the reader's, not the writer's.</b>
/// A <c>.pty</c> continues past the vaults with an <c>ACTIVE_SPELL_LIST</c> and seven
/// <c>Save</c> calls, and none of those has a reader yet — so there is nothing in hand to write.
/// What this covers is everything <see cref="SaveGameReader"/> covers, which is the whole
/// <c>PARTY</c> record and the four structures after it.
/// </para>
/// <para>
/// The tail is smaller than its description suggests, which is worth recording for whoever takes
/// it: each <c>Save</c>/<c>Restore</c> pair is <b>just the object's ASL</b> through the
/// attribute list's save path — <c>ITEM_DATA::Save</c> is one line, and so are the monster and
/// spell ones. Only <c>GLOBAL_STATS::Save</c> adds anything, a trailing <c>combatTreasure</c>
/// item list, and its <c>Restore</c> reads it back symmetrically. <see cref="AslWriter"/> already
/// has the save path they need.
/// </para>
/// <para>
/// <b>The header is not compressed and the body is.</b> The eight-byte version <c>double</c> goes
/// straight onto the file, then <c>car.Compress(true)</c> (<c>Dgngame.cpp:431</c>) — so a save is
/// the sixth container framing, and the only one where compression starts after a bare scalar
/// rather than after a magic.
/// </para>
/// </remarks>
public static class SaveGameWriter
{
    /// <inheritdoc cref="MonsterRecordWriter.WrittenVersion"/>
    /// <remarks>
    /// 5.24, bound by the <c>PIC_DATA</c> inside every <c>CHARACTER</c> a save carries. The save's
    /// own highest gate is 0.911, where <c>VISIT_DATA</c> stopped storing a single level's bitmap
    /// and started storing all 255 slots.
    /// </remarks>
    public static DesignVersion WrittenVersion => CharacterRecordWriter.WrittenVersion;

    /// <summary>
    /// Whether a save can be written as it stands, and why not when it cannot.
    /// </summary>
    public static bool CanWrite(SaveGame save, out string reason)
    {
        ArgumentNullException.ThrowIfNull(save);

        if (save.Pool is null)
        {
            reason = "the save was read from below 0.661 and carries no pooled MONEY_SACK.";
            return false;
        }

        if (save.Characters.Count > SaveGameReader.MaxPartyMembers)
        {
            reason = $"the save holds {save.Characters.Count} characters; a party writes at most " +
                     $"{SaveGameReader.MaxPartyMembers}.";
            return false;
        }

        if (save.Vaults.Count > SaveGameReader.MaxGlobalVaults)
        {
            reason = $"the save holds {save.Vaults.Count} vaults; the table has " +
                     $"{SaveGameReader.MaxGlobalVaults} slots.";
            return false;
        }

        foreach (var character in save.Characters)
        {
            if (!CharacterRecordWriter.CanWrite(character, out string characterReason))
            {
                reason = characterReason;
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Writes the version stamp and then the compressed body, as far as the vaults.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// When the save holds a shape that cannot go out — see <see cref="CanWrite"/>.
    /// </exception>
    public static void Write(Stream stream, SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(save);

        if (!CanWrite(save, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        // The version goes on the raw file, before compression starts.
        new MfcArchiveWriter(stream).WriteDouble(WrittenVersion.Value);

        using var car = CarArchiveWriter.Open(stream);
        WriteBody(ArchiveWriteCursor.For(car), save);
    }

    /// <summary>Writes the compressed body alone, for a caller that owns the framing.</summary>
    public static void WriteBody(IArchiveWriteCursor ar, SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(save);

        WritePartyState(ar, save.Party);
        WriteEventTriggerData(ar, save.EventFlags);
        WriteVisitData(ar, save.Visited);
        WriteBlockageStatus(ar, save.Blockages);

        ar.WriteInt32(save.Characters.Count);
        foreach (var character in save.Characters)
        {
            CharacterRecordWriter.Write(ar, character);
        }

        MonsterLeafWriters.WriteMoneySack(ar, save.Pool!);
        GlobalStatsTailWriters.WriteJournal(ar, save.Journal);
        AslWriter.Write(ar, WrittenVersion, AslMaps.Party, save.Attributes);

        GlobalTailWriters.WriteQuests(ar, save.Quests);
        GlobalTailWriters.WriteSpecialObjects(ar, save.SpecialItems);
        GlobalTailWriters.WriteSpecialObjects(ar, save.Keys);

        WriteVaults(ar, save.Vaults);
        SaveGameTailWriters.Write(ar, save.Tail);
    }

    /// <summary>Writes <c>PARTY::Serialize</c>'s scalars (<c>Party.cpp:996</c>).</summary>
    /// <remarks>
    /// <b>The record does not begin at its clock fields</b> — a task-state stack comes first, and
    /// transcribing from <c>days</c> (which is where a search lands) writes that stack's worth of
    /// bytes as the time of day. And the widths are not guessable from neighbours: nine
    /// <c>BYTE</c>s are interleaved among the <c>int</c>s, while <c>tradeQty</c> — which reads like
    /// a sibling of <c>tradeItem</c> — is an <c>int</c> declared further down.
    /// </remarks>
    public static void WritePartyState(IArchiveWriteCursor ar, PartyState party)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(party);

        ar.WriteInt32(party.TaskStack.Count);
        foreach (var task in party.TaskStack)
        {
            ar.WriteUInt32(task.Id);
            ar.WriteUInt32(task.Flags);
            ar.WriteByte((byte)task.Data.Count);     // an unsigned char between two unsigned ints
            foreach (uint value in task.Data)
            {
                ar.WriteUInt32(value);               // uints, despite the constant saying bytes
            }
        }

        ar.WriteInt32(party.Days);
        ar.WriteInt32(party.Hours);
        ar.WriteInt32(party.Minutes);
        ar.WriteInt32(party.DrinkPoints);
        ar.WriteString(ArchiveStringConventions.Encode(party.Name));

        ar.WriteInt32(party.Adventuring);
        ar.WriteInt32(party.AreaView);
        ar.WriteInt32(party.Searching);

        ar.WriteByte(party.Level);                   // BYTE
        ar.WriteByte(party.Speed);                   // BYTE
        ar.WriteInt32(party.PosX);
        ar.WriteInt32(party.PosY);
        ar.WriteInt32(party.PrevPosX);
        ar.WriteInt32(party.PrevPosY);

        ar.WriteByte(party.Facing);                  // five BYTEs in a row
        ar.WriteByte(party.ActiveCharacter);
        ar.WriteByte(party.ActiveItem);
        ar.WriteByte(party.TradeItem);
        ar.WriteByte(party.TradeGiver);
        ar.WriteInt32(party.TradeQuantity);          // an int among them
        ar.WriteByte(party.SkillLevel);
        ar.WriteByte(party.CharacterCount);
        ar.WriteInt32(party.MoneyPooled);
    }

    /// <summary>Writes an <c>EVENT_TRIGGER_DATA</c> (<c>Party.cpp:614</c>).</summary>
    /// <remarks>
    /// <c>STEP_COUNTER</c> is a raw struct blit — sixteen <c>unsigned long</c>s, one per zone — so
    /// its 64 bytes go out whole rather than field by field.
    /// </remarks>
    public static void WriteEventTriggerData(IArchiveWriteCursor ar,
                                             IReadOnlyList<LevelFlags> levels)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(levels);

        ar.WriteInt32(levels.Count);
        foreach (var level in levels)
        {
            if (level.StepCounts.Length != StepCounterZones)
            {
                throw new ArgumentException(
                    $"a STEP_COUNTER is {StepCounterZones} zones, not " +
                    $"{level.StepCounts.Length}.", nameof(levels));
            }

            var raw = new byte[StepCounterZones * sizeof(uint)];
            for (int zone = 0; zone < StepCounterZones; zone++)
            {
                BitConverter.TryWriteBytes(raw.AsSpan(zone * sizeof(uint)), level.StepCounts[zone]);
            }
            ar.WriteBytes(raw);

            ar.WriteInt32(level.EventResults.Count);
            foreach (var flags in level.EventResults)
            {
                ar.WriteUInt32(flags.Key);
                ar.WriteInt32(flags.StatusUnused);
                ar.WriteInt32(flags.Result);
            }
        }
    }

    /// <summary><c>MAX_ZONES</c> — the length of a <c>STEP_COUNTER</c>.</summary>
    public const int StepCounterZones = 16;

    /// <summary><c>MAX_LEVELS</c> — how many slots <c>VISIT_DATA</c> always writes.</summary>
    public const int VisitSlots = 255;

    /// <summary>Writes a <c>VISIT_DATA</c> (<c>Party.cpp:4573</c>): the tag, then 255 slots.</summary>
    /// <remarks>
    /// <para>
    /// <b>All 255 slots, however many levels the design has.</b> A one-level design still writes
    /// 254 empty pairs — a fixed 2,040 bytes before any bitmap.
    /// </para>
    /// <para>
    /// <b>A slot's level field is its loop index</b>, not a stored value (<c>:4584</c>), which is
    /// what lets the empty slots be reconstructed: the reader keeps only the visited levels, and
    /// the rest are recoverable because their index is the only thing that was ever written.
    /// </para>
    /// <para>
    /// The tag is the engine's own alignment check — it asserts on it with the comment "make sure
    /// we are located at the correct offset in the data file", which is what makes every field
    /// width in <c>PARTY</c> above a checked claim rather than a hopeful one.
    /// </para>
    /// </remarks>
    public static void WriteVisitData(IArchiveWriteCursor ar, IReadOnlyList<VisitedLevel> visited)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(visited);

        ar.WriteString(ArchiveStringConventions.Encode(SaveGameReader.VisitDataTag));

        var byIndex = visited.ToDictionary(v => v.Level);
        for (int i = 0; i < VisitSlots; i++)
        {
            ar.WriteInt32(i);                        // the index, not a stored field

            if (byIndex.TryGetValue(i, out var level))
            {
                ar.WriteInt32(level.Bitmap.Length);
                ar.WriteBytes(level.Bitmap);
            }
            else
            {
                ar.WriteInt32(0);                    // never entered; no bitmap follows
            }
        }
    }

    /// <summary>Writes the party's <c>BLOCKAGE_STATUS</c>.</summary>
    public static void WriteBlockageStatus(IArchiveWriteCursor ar,
                                           IReadOnlyList<BlockageData> blockages) =>
        CharacterLeafWriters.WriteBlockages(ar, blockages);

    /// <summary>Writes the global vaults (<c>Dgngame.cpp:439</c>).</summary>
    /// <remarks>
    /// <b>The count written is the constant, and so is the number of vaults.</b> The storing
    /// branch writes <c>MAX_GLOBAL_VAULTS</c> and then loops to it, whatever the game holds — and
    /// the loading branch <c>die</c>s on any other count before clamping. A save with fewer
    /// occupied vaults is padded with empty ones, which is exactly what the reference's
    /// default-constructed slots write.
    /// </remarks>
    public static void WriteVaults(IArchiveWriteCursor ar, IReadOnlyList<Vault> vaults)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(vaults);

        ar.WriteInt32(SaveGameReader.MaxGlobalVaults);

        for (int i = 0; i < SaveGameReader.MaxGlobalVaults; i++)
        {
            var vault = i < vaults.Count ? vaults[i] : EmptyVault;
            MonsterLeafWriters.WriteMoneySack(ar, vault.Money);
            MonsterLeafWriters.WriteItemList(ar, vault.Items);
        }
    }

    /// <summary>What an unoccupied vault slot writes as.</summary>
    private static Vault EmptyVault { get; } = new(
        new MoneySack(new int[MonsterLeafReaders.MaxCoinTypes], [], []),
        new ItemList([], new ReadyItems(new int[MonsterLeafReaders.ReadySlotCount])));
}
