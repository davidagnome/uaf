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
/// <b>Events are not carried across yet.</b> A converted level writes with an empty event list,
/// which is a valid <c>.lvl</c> — the cells still name their event indices, so nothing is lost
/// that a later pass cannot fill in.
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
    public static LevelFile Convert(FruaLevel level, int number)
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

        return new LevelFile(
            Version: WrittenVersion,
            Width: (byte)level.Width,
            Height: (byte)level.Height,
            Cells: cells,
            Level: number,

            // Events are a later pass; a level with none is still a valid file.
            EventCount: 0,
            Events: [],
            Entries: [],
            Zones: Zones(level),
            Attributes: [],
            StepEvents: StepEvents(level),
            WallSets: [],
            BackgroundSets: [],
            BlockageKeys: []);
    }

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
    /// <b>An overland level's terrain becomes blockage, not walls.</b> FRUA draws wilderness with
    /// no wall slots at all and stops movement with nibbles 14 and 15; a UAF cell expresses that
    /// as a blockage with no wall, which is what <see cref="FruaMapCell.IsOverlandBlocked"/> is
    /// for.
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
                blockage[slot] = cell.IsOverlandBlocked(facing)
                    ? (byte)FruaBlockage.Blocked
                    : (byte)FruaBlockage.Open;
            }
            else
            {
                walls[slot] = (byte)cell.WallSlot(facing);
                blockage[slot] = (byte)cell.Blockage(facing);
            }
        }

        byte background = (byte)cell.BackdropIndex;

        return new AreaMapCell(
            Background: background,
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
}
