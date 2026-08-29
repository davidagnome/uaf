using UAF.Common;
using UAF.Serialization;

namespace UAF.Import.Frua;

/// <summary>
/// Turns a DOS FRUA level into a UAF <see cref="LevelFile"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of the importer that <c>UAImport.cpp</c> spends its
/// <c>globalData.x = …</c> assignments on: the FRUA model read by
/// <see cref="FruaLevel"/> mapped onto the model <c>UAF.Serialization</c> already writes.
/// </para>
/// <para>
/// <b>Events come across for the types <see cref="FruaEventConverter"/> maps</b>, in file order so
/// that the chain byte an event carries still names the right neighbour. Types not yet mapped are
/// left out rather than stubbed.
/// </para>
/// </remarks>
public static class FruaLevelConverter
{
    /// <summary>
    /// <b>UAF stores walls and blockages north, SOUTH, EAST, west; FRUA stores them north, east,
    /// south, west.</b>
    /// </summary>
    /// <remarks>
    /// The UAF order is the one <c>AREA_MAP_DATA</c> declares and serialises, and every consumer
    /// there permutes with the same <c>{0,2,1,3}</c> table — see <see cref="AreaMapCell.WallAt"/>,
    /// which was settled against 9,708 shared edges of real data. Writing FRUA's order straight
    /// into a UAF cell would swap every east wall with every south one, which is the kind of
    /// mistake that looks like a design bug rather than an importer bug.
    /// </remarks>
    private static readonly FruaFacing[] UafSlotOrder =
        [FruaFacing.North, FruaFacing.South, FruaFacing.East, FruaFacing.West];

    /// <summary>The version a converted level is written as.</summary>
    public static DesignVersion WrittenVersion => LevelFileWriter.WrittenVersion;

    /// <summary>
    /// Converts one level.
    /// </summary>
    /// <param name="level">The FRUA level.</param>
    /// <param name="number">Its one-based number, used as the UAF level index.</param>
    /// <param name="design">
    /// The design the level belongs to, which resolves the keys, items and quests an event's
    /// trigger names. Null leaves those blank; everything else converts the same.
    /// </param>
    public static LevelFile Convert(FruaLevel level, int number, FruaDesign? design = null)
    {
        ArgumentNullException.ThrowIfNull(level);

        var cells = new AreaMapCell[level.Width * level.Height];

        for (int y = 0; y < level.Height; y++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                cells[(y * level.Width) + x] = Cell(level, level.Cell(x, y));
            }
        }

        ApplyOverlandBackground(level, cells);

        var events = Events(level, design);

        return new LevelFile(
            Version: WrittenVersion,
            Width: (byte)level.Width,
            Height: (byte)level.Height,
            Cells: cells,
            Level: number,
            EventCount: events.Count,
            Events: events.Select(e => e.Body!).ToArray(),
            Entries: events,
            Zones: Zones(level),
            Attributes: [],
            StepEvents: StepEvents(level),

            // Placeholder art, which is all the reference assigns -- see FruaArtConverter.
            WallSets: FruaArtConverter.WallSets(level),
            BackgroundSets: FruaArtConverter.Backgrounds(level),
            BlockageKeys: []);
    }

    /// <summary>
    /// The level's events, in record order, skipping the ones not yet mapped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An unmapped type is dropped, not faked.</b> <see cref="FruaEventConverter.Convert"/>
    /// returns null for a type it has no mapping for, and putting a placeholder in its place would
    /// write an event that claims to be something it is not. A cell whose event index no longer
    /// resolves is a visible gap; a wrong event is not.
    /// </para>
    /// <para>
    /// <b>The order is the file's.</b> A FRUA event's chain byte names another event by its record
    /// number, so preserving position is what keeps those references meaningful.
    /// </para>
    /// </remarks>
    private static List<LevelEventEntry> Events(FruaLevel level, FruaDesign? design)
    {
        var entries = new List<LevelEventEntry>();

        // A small town's children are extra events with no FRUA record of their own, so their
        // keys start past the hundred a level can store rather than colliding with one.
        uint nextChild = FruaEvent.PerLevel;
        var generated = new List<LevelEventEntry>();

        for (int i = 0; i < level.Events.Count; i++)
        {
            var source = level.Events[i];

            if (source.Type == FruaEventType.None)
            {
                continue;
            }

            if (FruaEventConverter.Convert(source, (uint)i, level.Strings, design) is not { } body)
            {
                continue;
            }

            if (body is SmallTownEvent town)
            {
                var (wired, children) = FruaEventConverter.SmallTownChildren(
                    source, town, nextChild, level.Strings, design);

                body = wired;
                nextChild += (uint)children.Count;
                generated.AddRange(children.Select(c => new LevelEventEntry(c.Type, c.Body)));
            }

            entries.Add(new LevelEventEntry((EventType)BaseOf(body).EventType, body));
        }

        // The children go after every record-backed event, so a hub's own position -- which its
        // neighbours' chain bytes still refer to -- does not move.
        entries.AddRange(generated);
        return entries;
    }

    /// <summary>
    /// The shared base of a converted event.
    /// </summary>
    /// <remarks>
    /// <c>IGameEvent</c> does not declare the base — each record names it positionally — so this
    /// is the one place that has to know the shape of every event the converter produces. The
    /// throw is deliberate: a new mapping that forgets to come through here fails loudly.
    /// </remarks>
    private static GameEventBase BaseOf(IGameEvent body) => body switch
    {
        TextEvent t => t.Base,
        TreasureEvent t => t.Base,
        DamageEvent d => d.Base,
        SoundEvent s => s.Base,
        QuestEvent q => q.Base,
        GainExperienceEvent g => g.Base,
        YesNoEvent y => y.Base,
        VaultEvent v => v.Base,
        PassTimeEvent p => p.Base,
        ChainEvent c => c.Base,
        CampEvent c => c.Base,
        TransferEvent t => t.Base,
        ShopEvent s => s.Base,
        TempleEvent t => t.Base,
        TrainingHallEvent t => t.Base,
        TavernEvent t => t.Base,
        TavernTalesEvent t => t.Base,
        SmallTownEvent s => s.Base,
        EncounterEvent e => e.Base,
        WhoTriesEvent w => w.Base,
        WhoPaysEvent p => p.Base,
        PasswordEvent p => p.Base,
        AddNpcEvent a => a.Base,
        RemoveNpcEvent r => r.Base,
        NpcSaysEvent n => n.Base,
        SpecialItemEvent s => s.Base,
        UtilitiesEvent u => u.Base,
        CombatEvent c => c.Base,
        GuidedTour g => g.Base,
        QuestionEvent q => q.Base,
        _ => throw new InvalidOperationException(
            $"FruaLevelConverter has no base accessor for {body.GetType().Name}"),
    };

    /// <summary>
    /// The eight zones a FRUA level carries, as UAF's zone table.
    /// </summary>
    /// <remarks>
    /// <b>FRUA has eight zones and one mapping flag for all of them.</b> Its
    /// <c>allowMap</c> byte is read once and applied to every zone, and its turning difficulty
    /// likewise — so all eight come out alike apart from their names and rest events, which is what
    /// the source actually distinguishes.
    /// </remarks>
    private static ZoneData Zones(FruaLevel level)
    {
        var zones = new Zone[level.ZoneNames.Count];

        for (int i = 0; i < zones.Length; i++)
        {
            var rest = level.RestEvents[i];

            zones[i] = new Zone(
                SummonedMonster: string.Empty,
                AddedTurningDifficulty: 0,
                AllowMap: level.AllowMapping ? 1 : 0,
                AllowMagic: 1,
                AllowAutoDarken: 0,
                Message: string.Empty,
                Name: level.ZoneNames[i],
                IndoorCombatArt: string.Empty,
                OutdoorCombatArt: string.Empty,
                Sounds: new BackgroundSoundData([], [], 0, 0, 0),
                CampArt: EmptyPicture,
                TreasurePicture: EmptyPicture,
                Rest: new RestEvent(
                    AllowResting: rest.AllowResting ? 1 : 0,
                    Event: (uint)rest.EventIndex,
                    Chance: rest.Chance,
                    EveryMinutes: rest.EveryMinutes,
                    PreviousMinuteChecked: 0),
                Attributes: []);
        }

        return new ZoneData(zones, string.Empty);
    }

    /// <summary>
    /// FRUA's eight step events, padded to the full table a modern level writes.
    /// </summary>
    /// <remarks>
    /// <b>A 5.24 level stores 255 step-event slots, not 8.</b> FRUA has eight, and the writer
    /// refuses anything short — its table is a fixed array, so a file with fewer would have every
    /// later field read out of the middle of it. The tail is inert: no steps, no event, no zones.
    /// </remarks>
    private static StepEvent[] StepEvents(FruaLevel level)
    {
        var steps = new StepEvent[LevelStructureReaders.MaxStepEvents];

        for (int i = 0; i < steps.Length; i++)
        {
            if (i < level.StepEvents.Count)
            {
                var step = level.StepEvents[i];

                steps[i] = new StepEvent(
                    StepCount: step.StepCount,
                    Event: (uint)step.EventIndex,
                    ZoneMask: step.ZoneMask,
                    Name: string.Empty,
                    Attributes: []);
            }
            else
            {
                steps[i] = new StepEvent(0, 0, 0, string.Empty, []);
            }
        }

        return steps;
    }

    private static PicRecord EmptyPicture { get; } =
        new(PicType: 0, FileName: string.Empty, TimeDelay: 0, NumFrames: 0,
            FrameWidth: 0, FrameHeight: 0, Flags: 0, MaxLoops: 0,
            Style: 0, UseAlpha: 0, AlphaValue: 0, RestartFrame: 0);

    /// <summary>Converts one square.</summary>
    /// <remarks>
    /// <para>
    /// <b>An overland level's terrain becomes the <c>bkgrnd</c> flag, not walls or blockage.</b>
    /// FRUA draws wilderness with no wall slots at all and stops movement with nibbles 14 and 15;
    /// the reference expresses that by leaving <c>wall[]</c> and <c>blockage[]</c> at zero and
    /// setting the <c>bkgrnd</c> flag on the cell an edge points into — which
    /// <see cref="ApplyOverlandBackground"/> does. Writing a blockage here would diverge from the
    /// reference, as the oracle's all-zero blockage on level 1 made clear.
    /// </para>
    /// <para>
    /// <b>The backdrop becomes the cell's single background.</b> FRUA names one of the level's four
    /// backdrops per square; UAF's four per-face backgrounds are a later feature, so all four take
    /// the same value rather than inventing a distinction the source does not have.
    /// </para>
    /// </remarks>
    private static AreaMapCell Cell(FruaLevel level, FruaMapCell cell)
    {
        var walls = new byte[4];
        var blockage = new byte[4];

        for (int slot = 0; slot < 4; slot++)
        {
            var facing = UafSlotOrder[slot];

            if (level.IsOverland)
            {
                walls[slot] = 0;
                blockage[slot] = (byte)FruaBlockage.Open;
            }
            else
            {
                walls[slot] = (byte)cell.WallSlot(facing);
                blockage[slot] = (byte)cell.Blockage(facing);
            }
        }

        byte background = (byte)cell.BackdropIndex;

        return new AreaMapCell(
            Background: 0,
            ShowDistantBackground: false,
            DistantBackgroundInBands: false,
            NorthBg: background,
            EastBg: background,
            SouthBg: background,
            WestBg: background,
            Zone: (byte)cell.Zone,
            EventExists: cell.EventIndex != 0,
            Walls: walls,
            Blockage: blockage);
    }

    /// <summary>
    /// Marks a cell's <c>bkgrnd</c> flag where an overland edge blocks entry into it
    /// (<c>UAImport.cpp</c>'s overland branch).
    /// </summary>
    /// <remarks>
    /// <b>The source walk excludes the outer ring.</b> The reference only checks the edges of
    /// interior cells (<c>x &gt; 0</c>, <c>y &gt; 0</c>, and not the last column/row), so a border
    /// square never propagates but still receives from its interior neighbours. FRUA overland
    /// blockage is only nibbles 14 and 15, which is what
    /// <see cref="FruaMapCell.IsOverlandBlocked"/> tests.
    /// </remarks>
    private static void ApplyOverlandBackground(FruaLevel level, AreaMapCell[] cells)
    {
        if (!level.IsOverland)
        {
            return;
        }

        var blocked = new bool[level.Width * level.Height];

        for (int y = 1; y < level.Height - 1; y++)
        {
            for (int x = 1; x < level.Width - 1; x++)
            {
                var cell = level.Cell(x, y);

                if (cell.IsOverlandBlocked(FruaFacing.North)) blocked[((y - 1) * level.Width) + x] = true;
                if (cell.IsOverlandBlocked(FruaFacing.South)) blocked[((y + 1) * level.Width) + x] = true;
                if (cell.IsOverlandBlocked(FruaFacing.West)) blocked[(y * level.Width) + (x - 1)] = true;
                if (cell.IsOverlandBlocked(FruaFacing.East)) blocked[(y * level.Width) + (x + 1)] = true;
            }
        }

        for (int i = 0; i < cells.Length; i++)
        {
            if (blocked[i])
            {
                cells[i] = cells[i] with { Background = 1 };
            }
        }
    }
}
