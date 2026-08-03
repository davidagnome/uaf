using System.Text;
using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes the structures that follow <c>GLOBAL_STATS</c>'s attribute list — the inverses of
/// <see cref="GlobalTailReaders"/>.
/// </summary>
public static class GlobalTailWriters
{
    /// <summary>
    /// How many named art slots the storing branch writes: the eight unconditional ones plus
    /// <c>CharViewFrameVPArt</c>, <c>CombatPetrifiedIconArt</c> and <c>CombatDeathArt</c>.
    /// </summary>
    /// <remarks>
    /// <b>All eleven, unconditionally.</b> Two of them are gated on the way in — one at 0.930204
    /// and one whose test is literally <c>version &gt;= _VERSION_526 || car.IsStoring()</c>
    /// (<c>GlobalData.cpp:4500</c>). That second gate is the clearest statement of the rule
    /// anywhere in the codebase: the storing side is spelled out as unconditional in the condition
    /// itself, and it is why <see cref="GlobalStatsWriter.WrittenVersion"/> cannot be 5.24.
    /// </remarks>
    public static int ArtSlotCount => GlobalTailReaders.ArtSlotNames.Length;

    /// <summary>Writes a <c>PicDataType</c> (<c>PicSlot.cpp:900</c>): a type and a name.</summary>
    public static void WritePicSlot(IArchiveWriteCursor ar, PicDataSlot slot)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(slot);

        ar.WriteInt32(slot.PicType);
        WriteDas(ar, slot.Name);
    }

    /// <summary>Writes the art block that follows the global ASL.</summary>
    /// <remarks>
    /// The cursor is a whole <c>PIC_DATA</c> rather than a slot, and it comes last.
    /// </remarks>
    public static void WriteArtBlock(IArchiveWriteCursor ar, GlobalArt art)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(art);

        if (art.Slots.Count != ArtSlotCount)
        {
            throw new ArgumentException(
                $"the global art block writes exactly {ArtSlotCount} slots, not " +
                $"{art.Slots.Count}. The reader returns them all whatever the version, with the " +
                "ones a design predates left empty, so a short list means it was built by hand.",
                nameof(art));
        }

        foreach (var slot in art.Slots)
        {
            WritePicSlot(ar, slot);
        }

        PicDataWriter.Write(ar, art.Cursor ?? PicDataWriter.Empty, PicArchiveVariant.Car);
    }

    /// <summary>Writes a <c>GLOBAL_SOUND_DATA</c> (<c>GlobalData.cpp:1025</c>).</summary>
    /// <remarks>
    /// Five names then three queues. All three queues go out whatever the version, where the
    /// reader admits the intro as a bare string below 0.710, the credits only from 5.25 and the
    /// camp only from 0.910.
    /// </remarks>
    public static void WriteSounds(IArchiveWriteCursor ar, GlobalSounds sounds)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(sounds);

        WriteDas(ar, sounds.CharHit);
        WriteDas(ar, sounds.CharMiss);
        WriteDas(ar, sounds.PartyBump);
        WriteDas(ar, sounds.PartyStep);
        WriteDas(ar, sounds.DeathMusic);

        CombatEventWriter.WriteBackgroundSounds(ar, sounds.IntroMusic);
        CombatEventWriter.WriteBackgroundSounds(ar, sounds.CreditsMusic);
        CombatEventWriter.WriteBackgroundSounds(ar, sounds.CampMusic);
    }

    /// <summary>Writes a <c>SPECIAL_OBJECT_LIST</c>: a count then that many objects.</summary>
    /// <remarks><c>stage</c> is a <c>WORD</c>, not an <c>int</c>.</remarks>
    public static void WriteSpecialObjects(IArchiveWriteCursor ar,
                                           IReadOnlyList<SpecialObject> objects)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(objects);

        ar.WriteInt32(objects.Count);
        foreach (var obj in objects)
        {
            WriteDas(ar, obj.Name);
            ar.WriteInt32(obj.Id);
            ar.WriteUInt16(obj.Stage);               // WORD
            ar.WriteUInt32(obj.ExamineEvent);
            WriteDas(ar, obj.ExamineLabel);
            ar.WriteInt32(obj.CanBeDropped);

            AslWriter.Write(ar, GlobalStatsWriter.WrittenVersion, AslMaps.SpecialObjectData,
                            obj.Attributes);
        }
    }

    /// <summary>Writes a <c>QUEST_LIST</c>: a count then that many quests.</summary>
    public static void WriteQuests(IArchiveWriteCursor ar, IReadOnlyList<Quest> quests)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(quests);

        ar.WriteInt32(quests.Count);
        foreach (var quest in quests)
        {
            WriteDas(ar, quest.Name);
            ar.WriteInt32(quest.State);
            ar.WriteUInt16(quest.Stage);             // WORD again
            ar.WriteInt32(quest.Id);

            AslWriter.Write(ar, GlobalStatsWriter.WrittenVersion, AslMaps.QuestData,
                            quest.Attributes);
        }
    }

    /// <summary>Writes a <c>TITLE_SCREEN_DATA</c> (<c>GlobalData.cpp:373</c>).</summary>
    /// <remarks>The count is a <c>DWORD</c>, not an <c>int</c> — same width, opposite sign.</remarks>
    public static void WriteTitleScreens(IArchiveWriteCursor ar, TitleScreenData titles)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(titles);

        ar.WriteUInt32(titles.Timeout);
        ar.WriteUInt32((uint)titles.Titles.Count);

        foreach (var title in titles.Titles)
        {
            WriteDas(ar, title.BackgroundArt);
            ar.WriteInt32(title.UseTrans);
            ar.WriteInt32(title.UseBlend);
            ar.WriteUInt32(title.DisplayBy);
        }
    }

    internal static void WriteDas(IArchiveWriteCursor ar, string value) =>
        ar.WriteString(ArchiveStringConventions.Encode(value));

    /// <summary>
    /// Writes a raw <c>char</c> buffer of a fixed size, NUL-padded — the shape a gem or coin name
    /// uses.
    /// </summary>
    /// <remarks>
    /// The member is <c>char name[MAX_NAME + 1]</c> and the reference loops over <c>MAX_NAME</c>
    /// <i>characters</i>, so this is that many single bytes rather than a counted string. The
    /// terminator slot is never on the wire.
    /// </remarks>
    internal static void WriteRawName(IArchiveWriteCursor ar, string name, int length)
    {
        byte[] encoded = Encoding.Latin1.GetBytes(name);
        if (encoded.Length > length)
        {
            throw new ArgumentException(
                $"'{name}' is {encoded.Length} bytes; the buffer holds {length}.", nameof(name));
        }

        var buffer = new byte[length];
        encoded.CopyTo(buffer, 0);
        ar.WriteBytes(buffer);
    }
}

/// <summary>
/// Writes the <c>GLOBAL_STATS</c> structures that follow the character list — the inverses of
/// <see cref="GlobalStatsTailReaders"/>.
/// </summary>
public static class GlobalStatsTailWriters
{
    /// <summary>Writes a <c>LEVEL_STATS</c> (<c>GlobalData.cpp:3183</c>).</summary>
    /// <remarks>
    /// <c>height</c> and <c>width</c> are <c>BYTE</c>s, as they are in the level files themselves,
    /// and the eight entry points are a fixed table with no count — each a Win32 <c>POINT</c>, so
    /// two <c>LONG</c>s.
    /// </remarks>
    public static void WriteLevelStats(IArchiveWriteCursor ar, LevelStats level)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(level);

        if (level.EntryPoints.Count != GlobalStatsTailReaders.MaxEntryPoints)
        {
            throw new ArgumentException(
                $"a LEVEL_STATS writes exactly {GlobalStatsTailReaders.MaxEntryPoints} entry " +
                $"points, not {level.EntryPoints.Count}. The count is compile-time in the " +
                "reference and never written.", nameof(level));
        }

        ar.WriteByte(level.Height);                  // BYTE
        ar.WriteByte(level.Width);
        ar.WriteInt32(level.Used);
        ar.WriteInt32(level.Overland);
        ar.WriteInt32(level.AreaViewStyle);
        GlobalTailWriters.WriteDas(ar, level.Name);

        foreach (var point in level.EntryPoints)
        {
            ar.WriteInt32(point.X);
            ar.WriteInt32(point.Y);
        }

        GlobalTailWriters.WriteDas(ar, level.StepSound);
        GlobalTailWriters.WriteDas(ar, level.BumpSound);

        // Spelled out inline in the reference rather than calling BACKGROUND_SOUND_DATA::Serialize,
        // but the layout is identical.
        CombatEventWriter.WriteBackgroundSoundData(
            ar, level.Sounds ?? new BackgroundSoundData([], [], 0, 0, 0));

        CellContentsWriters.WriteWallOverrides(
            ar, level.Overrides ?? new WallOverrides([]));
        CellContentsWriters.WriteCellContents(
            ar, level.Contents ?? new CellLevelContents([]));

        AslWriter.Write(ar, GlobalStatsWriter.WrittenVersion, AslMaps.LevelStats,
                        level.Attributes);
    }

    /// <summary>Writes a <c>LEVEL_INFO</c> (<c>GlobalData.cpp:3574</c>).</summary>
    /// <remarks>
    /// <b>Two counts that are not the same number.</b> The first is how many levels the design
    /// declares, the second how many entries follow — and each entry carries its own index, so the
    /// indices need not be contiguous.
    /// </remarks>
    public static void WriteLevelInfo(IArchiveWriteCursor ar, LevelInfo levels)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(levels);

        ar.WriteInt32(levels.NumberOfLevels);
        ar.WriteInt32(levels.Levels.Count);

        foreach ((uint index, var stats) in levels.Levels.OrderBy(l => l.Key))
        {
            ar.WriteUInt32(index);
            WriteLevelStats(ar, stats);
        }
    }

    /// <summary>Writes a <c>GEM_CONFIG</c> (<c>Money.cpp:349</c>).</summary>
    public static void WriteGemConfig(IArchiveWriteCursor ar, GemConfig gem)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(gem);

        ar.WriteInt32(gem.MinValue);
        ar.WriteInt32(gem.MaxValue);
        GlobalTailWriters.WriteRawName(ar, gem.Name, GlobalStatsTailReaders.GemNameLength);
    }

    /// <summary>Writes a <c>COIN_TYPE</c> (<c>Money.cpp:185</c>).</summary>
    /// <remarks><c>rate</c> is a <c>double</c>; the name is a raw buffer, not a counted string.</remarks>
    public static void WriteCoinType(IArchiveWriteCursor ar, CoinType coin)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(coin);

        ar.WriteDouble(coin.Rate);
        ar.WriteInt32(coin.IsBase);
        GlobalTailWriters.WriteRawName(ar, coin.Name, GlobalStatsTailReaders.CoinNameLength);
    }

    /// <summary>Writes a <c>MONEY_DATA_TYPE</c> (<c>Money.cpp:969</c>).</summary>
    /// <remarks>
    /// The ten <c>COIN_TYPE</c> records sit outside the storing branch, so they are written at
    /// every version. They are full records here — <c>MONEY_SACK</c>'s <c>Coins[]</c> of the same
    /// name is a plain <c>int</c> array.
    /// </remarks>
    public static void WriteMoneyData(IArchiveWriteCursor ar, MoneyData money)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(money);

        if (money.Coins.Count != MonsterLeafReaders.MaxCoinTypes)
        {
            throw new ArgumentException(
                $"a MONEY_DATA_TYPE writes exactly {MonsterLeafReaders.MaxCoinTypes} coin types, " +
                $"not {money.Coins.Count}.", nameof(money));
        }

        ar.WriteInt32(money.Weight);
        ar.WriteInt32(money.HighestRate);
        ar.WriteInt32(money.HighestRateType);
        ar.WriteInt32(money.DefaultType);

        WriteGemConfig(ar, money.Gems ?? new GemConfig(0, 0, string.Empty));
        WriteGemConfig(ar, money.Jewelry ?? new GemConfig(0, 0, string.Empty));

        foreach (var coin in money.Coins)
        {
            WriteCoinType(ar, coin);
        }
    }

    /// <summary>Writes a <c>DIFFICULTY_LEVEL_DATA</c> (<c>GlobalData.cpp:849</c>).</summary>
    /// <remarks>
    /// Only <c>m_defaultLvl</c> — a <c>BYTE</c> — is inside the storing branch; the five level
    /// records follow it from outside. Each level's four amount fields are <c>char</c>, so four
    /// bytes where a uniform reading would write sixteen.
    /// </remarks>
    public static void WriteDifficulty(IArchiveWriteCursor ar, DifficultyData difficulty)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(difficulty);

        if (difficulty.Levels.Count != GlobalStatsTailReaders.DifficultyLevels)
        {
            throw new ArgumentException(
                $"there are exactly {GlobalStatsTailReaders.DifficultyLevels} difficulty levels, " +
                $"not {difficulty.Levels.Count}.", nameof(difficulty));
        }

        ar.WriteByte(difficulty.DefaultLevel);       // BYTE

        foreach (var level in difficulty.Levels)
        {
            GlobalTailWriters.WriteDas(ar, level.Name);
            ar.WriteInt32(level.ModifyHitDice);
            ar.WriteInt32(level.ModifyQuantity);
            ar.WriteInt32(level.ModifyMonsterExp);
            ar.WriteInt32(level.ModifyAllExp);
            ar.WriteByte((byte)level.HitDiceAmount);     // char, not int
            ar.WriteByte((byte)level.QuantityAmount);
            ar.WriteByte((byte)level.MonsterExpAmount);
            ar.WriteByte((byte)level.AllExpAmount);
        }
    }

    /// <summary>Writes a <c>JOURNAL_DATA</c> (<c>Party.h:186</c>): a count then the entries.</summary>
    public static void WriteJournal(IArchiveWriteCursor ar, IReadOnlyList<JournalEntry> journal)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(journal);

        ar.WriteInt32(journal.Count);
        foreach (var entry in journal)
        {
            ar.WriteInt32(entry.Entry);
            ar.WriteInt32(entry.OriginalEntry);
            GlobalTailWriters.WriteDas(ar, entry.Text);
        }
    }
}
