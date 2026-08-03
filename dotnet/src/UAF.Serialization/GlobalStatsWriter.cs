using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>GLOBAL_STATS</c> (<c>GlobalData.cpp:4250</c>, the <c>CAR</c> overload) — the record a
/// design's <c>game.dat</c> is, and the one a character list sits inside.
/// </summary>
/// <remarks>
/// <para>
/// The widest record in the format rather than the deepest: a run of scalars, a <c>LOGFONT</c>
/// blit, two picture-import lists, two title sequences, an ASL, eleven art slots, the sound
/// queues, three record lists, the characters, the level table, the money and difficulty
/// configuration, the global event list, the journal and a spellbook.
/// </para>
/// <para>
/// <b>It writes its own version as its first field.</b> No other record does. The loading branch
/// reads eight bytes and decides from them: the magic means "a version follows, then turn
/// compression on, then the version again", anything else means those eight bytes <i>were</i> the
/// version as a <c>double</c> (<c>GlobalData.cpp:4336</c>). The storing branch emits the bare
/// version, so the container header around it is the caller's business — see
/// <see cref="GameDataReader"/> for the reading side of that seam.
/// </para>
/// </remarks>
public static class GlobalStatsWriter
{
    /// <inheritdoc cref="MonsterRecordWriter.WrittenVersion"/>
    /// <remarks>
    /// <b>5.26, and this is the first record whose own gates push past the embedded
    /// <c>PIC_DATA</c>'s 5.24.</b> Two fields force it:
    /// <list type="bullet">
    /// <item><c>creditsData</c> is written unconditionally and read only at <b>5.25</b> and above
    /// — below that a design carries a single <c>CreditsBgArt</c> string instead, so a file
    /// stamped 5.24 would have a whole title sequence read as one string and then run off the
    /// end.</item>
    /// <item><c>CharViewFrameVPArt</c> is gated <c>version &gt;= _VERSION_526 ||
    /// car.IsStoring()</c> (<c>GlobalData.cpp:4500</c>) — the storing side spelled out as
    /// unconditional inside the condition itself. A reader below 5.26 does not consume the slot
    /// this always writes.</item>
    /// </list>
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.V526;

    /// <summary>
    /// Whether the record can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// Most refusals here are inherited — a character, an event or a wall-override table that
    /// cannot go out stops the whole record. Two are its own, and both are absent structures the
    /// reader gates away rather than shapes it converted: a design below 0.642 has no
    /// <c>MONEY_DATA_TYPE</c> and one below 0.697 no difficulty table, and neither has a
    /// default this port can honestly invent — the currency configuration in particular decides
    /// what every coin in the design is worth.
    /// </remarks>
    public static bool CanWrite(GlobalStatsPrefix global, out string reason)
    {
        ArgumentNullException.ThrowIfNull(global);

        if (global.Sounds is null)
        {
            reason = "the design has no GLOBAL_SOUND_DATA; it was read with the record stopped " +
                     "short rather than to its end.";
            return false;
        }

        if (global.Levels is null)
        {
            reason = "the design has no LEVEL_INFO; the record was read with " +
                     "ReadThroughCharacters, which stops before it.";
            return false;
        }

        if (global.Money is null)
        {
            reason = "the design was read from below 0.642 and carries no MONEY_DATA_TYPE. There " +
                     "is no default to invent: the table decides what every coin in the design " +
                     "is worth.";
            return false;
        }

        if (global.Difficulty is null)
        {
            reason = "the design was read from below 0.697 and carries no difficulty table.";
            return false;
        }

        foreach (var character in global.Characters)
        {
            if (!CharacterRecordWriter.CanWrite(character, out string characterReason))
            {
                reason = characterReason;
                return false;
            }
        }

        foreach (var level in global.Levels.Levels.Values)
        {
            if (level.Overrides is { } overrides &&
                !CellContentsWriters.CanWrite(overrides, out string overrideReason))
            {
                reason = $"level '{level.Name}': {overrideReason}";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Writes the record, from its version through to the trailing spellbook.
    /// </summary>
    /// <param name="events">
    /// The design's global event list, in order. It is written where
    /// <c>eventData.Serialize</c> sits (<c>GlobalData.cpp:4556</c>) — the position that made the
    /// event bodies a prerequisite of this record rather than a later concern.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// When the record holds a shape that cannot go out — see <see cref="CanWrite"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b><c>IconBgArt</c> and <c>BackgroundArt</c> are stripped of their directories; <c>MapArt</c>
    /// is not.</b> The reference's <c>StripFilenamePath(MapArt)</c> is commented out
    /// (<c>GlobalData.cpp:4287</c>) two lines above two live ones, which is the only thing that
    /// distinguishes them.
    /// </para>
    /// <para>
    /// <b>An empty face name is filled in before the blit.</b> The reference substitutes
    /// <c>SYSTEM</c> at 16pt when <c>lfFaceName</c> is empty (<c>:4290</c>), so the bytes it writes
    /// are never a zeroed name — and <see cref="LogFont.Parse"/> already applies the same
    /// substitution on the way in, which is what makes the two agree.
    /// </para>
    /// <para>
    /// <b>The picture imports are written as read, where the reference normalises them first.</b>
    /// It forces every small-pic import's <c>picType</c> to <c>SmallPicDib</c> and runs
    /// <c>SetDefaults()</c> over it, and forces every icon import's to <c>IconDib</c>
    /// (<c>:4310</c>). For a file the reference produced those are no-ops — the corpus confirms
    /// every import already carries the right type and frame count — with **one exception worth
    /// stating**: <c>SetDefaults</c> also sets a small pic's <c>RestartFrame</c> to 1, and that
    /// field only reaches the wire at 5.24. A design below it never had one to read, so this
    /// writer emits the 0 it saw where the reference would emit 1. Reproducing that faithfully
    /// would need the runtime viewport dimensions, which this layer does not have.
    /// </para>
    /// </remarks>
    public static void Write(IArchiveWriteCursor ar, GlobalStatsPrefix global,
                             IReadOnlyList<(EventType Type, IGameEvent Body)> events)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(events);

        if (!CanWrite(global, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        ar.WriteDouble(WrittenVersion.Value);
        WriteDas(ar, global.DesignName);

        ar.WriteInt32(global.StartLevel);
        ar.WriteByte(global.StartX);                 // three BYTEs in a row
        ar.WriteByte(global.StartY);
        ar.WriteByte(global.StartFacing);
        ar.WriteInt32(global.StartTime);
        ar.WriteInt32(global.StartExp);
        ar.WriteInt32(global.StartExpType);
        ar.WriteInt32(global.RetiredStartEquip);     // a literal zero in the reference

        ar.WriteInt32(global.StartPlatinum);
        ar.WriteInt32(global.StartGem);
        ar.WriteInt32(global.StartJewelry);

        ar.WriteInt32(global.DungeonTimeDelta);
        ar.WriteInt32(global.DungeonSearchTimeDelta);
        ar.WriteInt32(global.WildernessTimeDelta);
        ar.WriteInt32(global.WildernessSearchTimeDelta);

        ar.WriteInt32(global.AutoDarkenViewport);
        ar.WriteInt32(global.AutoDarkenAmount);
        ar.WriteInt32(global.StartDarken);
        ar.WriteInt32(global.EndDarken);

        ar.WriteInt32(global.MinPcs);
        ar.WriteInt32(global.MaxPartyMaxPcs);        // maxParty in the high word, maxPCs in the low
        ar.WriteInt32(global.Flags);

        WriteDas(ar, global.MapArt);                 // NOT stripped -- see the remarks

        var font = global.Font.FaceName.Length == 0 ? LogFont.Default : global.Font;
        ar.WriteBytes(font.ToBytes());               // a raw struct blit, not field by field

        WriteDas(ar, PicDataWriter.StripFilenamePath(global.IconBackgroundArt));
        WriteDas(ar, PicDataWriter.StripFilenamePath(global.BackgroundArt));

        WritePicImports(ar, global.SmallPicImports);
        WritePicImports(ar, global.IconPicImports);

        GlobalTailWriters.WriteTitleScreens(ar, global.TitleData ?? EmptyTitles);
        GlobalTailWriters.WriteTitleScreens(ar, global.CreditsData ?? EmptyTitles);

        AslWriter.Write(ar, WrittenVersion, AslMaps.GlobalStats, global.Attributes);

        GlobalTailWriters.WriteArtBlock(ar, new GlobalArt(global.Art, global.CursorArt));
        GlobalTailWriters.WriteSounds(ar, global.Sounds!);
        GlobalTailWriters.WriteSpecialObjects(ar, global.Keys);
        GlobalTailWriters.WriteSpecialObjects(ar, global.SpecialItems);
        GlobalTailWriters.WriteQuests(ar, global.Quests);

        CharacterRecordWriter.WriteList(ar, global.Characters);

        GlobalStatsTailWriters.WriteLevelInfo(ar, global.Levels!);
        GlobalStatsTailWriters.WriteMoneyData(ar, global.Money!);
        GlobalStatsTailWriters.WriteDifficulty(ar, global.Difficulty!);

        WriteEventList(ar, events);

        GlobalStatsTailWriters.WriteJournal(ar, global.Journal);
        CharacterLeafWriters.WriteSpellBook(ar, global.FixSpellBook ?? new SpellBook(0, []));
    }

    /// <summary>What an absent title or credits sequence writes as: a timeout and no screens.</summary>
    private static TitleScreenData EmptyTitles { get; } = new(0, []);

    /// <summary>
    /// Writes the global <c>GameEventList</c> — the level tag, a count, then a type-tagged body
    /// each.
    /// </summary>
    /// <remarks>
    /// <b>The level is <c>GLOBAL_ART</c>, not the design's own.</b> The reference assigns
    /// <c>eventData.m_level = GLOBAL_ART</c> immediately before serializing
    /// (<c>GlobalData.cpp:4554</c>), so whatever the list was holding is overwritten on the way
    /// out.
    /// </remarks>
    public const int GlobalArtLevel = 0;

    private static void WriteEventList(IArchiveWriteCursor ar,
                                       IReadOnlyList<(EventType Type, IGameEvent Body)> events)
    {
        foreach ((var type, _) in events)
        {
            if (!EventBodyWriter.CanWrite(type))
            {
                throw new NotSupportedException(
                    $"the global event list holds a {type}, which has no writer. A body has no " +
                    "length prefix, so writing the list without it would corrupt every event " +
                    "after it.");
            }
        }

        ar.WriteInt32(GlobalArtLevel);
        ar.WriteInt32(events.Count);

        foreach ((var type, var body) in events)
        {
            ar.WriteInt32((int)type);
            EventBodyWriter.Write(ar, type, body);
        }
    }

    private static void WritePicImports(IArchiveWriteCursor ar, IReadOnlyList<PicRecord> imports)
    {
        ar.WriteInt32(imports.Count);
        foreach (var import in imports)
        {
            PicDataWriter.Write(ar, import, PicArchiveVariant.Car);
        }
    }

    private static void WriteDas(IArchiveWriteCursor ar, string value) =>
        ar.WriteString(ArchiveStringConventions.Encode(value));
}
