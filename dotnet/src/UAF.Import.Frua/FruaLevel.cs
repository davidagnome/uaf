namespace UAF.Import.Frua;

/// <summary>Which way a party faces when it arrives (<c>0=N, 2=E, 4=S, 6=W</c>).</summary>
/// <remarks>
/// <b>The stored values are even, and the odd ones are not diagonals — they are unreachable.</b>
/// The reference masks with <c>0x7</c> and switches on 0, 2, 4 and 6 with no default, so a byte
/// carrying 1, 3, 5 or 7 leaves the facing at whatever the struct was last cleared to. Reproduced
/// as <see cref="Unknown"/> rather than guessed at.
/// </remarks>
public enum FruaFacing
{
    Unknown = -1,
    North = 0,
    East = 2,
    South = 4,
    West = 6,
}

/// <summary>Where a party can arrive on a level, and facing which way.</summary>
public readonly record struct FruaEntryPoint(int X, int Y, FruaFacing Facing);

/// <summary>
/// A zone's resting rules (the eight rest events).
/// </summary>
/// <param name="EveryMinutes">How often the check comes round.</param>
/// <param name="EventIndex">Which event fires, or 0 for none.</param>
/// <param name="Chance">Percentage chance, the low seven bits.</param>
/// <param name="AllowResting">
/// <b>The high bit means the opposite of what it looks like.</b> The reference reads
/// <c>allowResting = !((b &amp; 0x80) == 0x80)</c> — the bit being <i>set</i> forbids resting.
/// </param>
public readonly record struct FruaRestEvent(int EveryMinutes, int EventIndex, int Chance,
                                            bool AllowResting);

/// <summary>
/// A step-triggered event (the eight step events).
/// </summary>
/// <param name="StepCount">How many steps between triggers.</param>
/// <param name="EventIndex">Which event fires, or 0 for none.</param>
/// <param name="ZoneMask">
/// Which of the eight zones the event may fire in, as a bitmask with bit <i>n</i> set meaning
/// zone <i>n</i> is included.
/// </param>
public readonly record struct FruaStepEvent(int StepCount, int EventIndex, int ZoneMask);

/// <summary>
/// One DOS FRUA level's header — the fixed part of a <c>geo###.dat</c>
/// (<c>ImportGeoDatFile</c>, <c>UAFWinEd/UAImport.cpp:4827</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The first 26 bytes are skipped outright.</b> The reference opens with
/// <c>file.Seek(26, CFile::begin)</c> and never looks at what it passed over, so whatever those
/// bytes hold is not part of the import.
/// </para>
/// <para>
/// <b>This reads the header only.</b> A <c>geo###.dat</c> is 12,962 bytes in every shipped design;
/// the map cells and the event records that fill the rest are a separate slice, and the event half
/// carries its own trap — <c>EventByte</c> indexes <c>pData[FileOffset - 5]</c>, so every offset
/// the reference quotes for an event is five higher than the buffer position.
/// </para>
/// </remarks>
public sealed record FruaLevel(
    int Width,
    int Height,
    string Name,
    bool IsOverland,
    bool AllowMapping,
    IReadOnlyList<int> WallSlots,
    IReadOnlyList<int> BackdropSlots,
    int DungeonCombatArt,
    int WildernessCombatArt,
    IReadOnlyList<FruaEntryPoint> EntryPoints,
    IReadOnlyList<FruaRestEvent> RestEvents,
    IReadOnlyList<FruaStepEvent> StepEvents)
{
    /// <summary>Every shipped level file is exactly this long.</summary>
    public const int Length = 12_962;

    /// <summary>The reference scans <c>geo001.dat</c> through <c>geo040.dat</c>.</summary>
    public const int MaxLevels = 40;

    /// <summary>Where the header starts, the first 26 bytes being skipped.</summary>
    private const int HeaderAt = 26;

    /// <summary>
    /// Reads a level header.
    /// </summary>
    public static FruaLevel Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Length)
        {
            throw new InvalidDataException(
                $"a geo###.dat is {bytes.Length} bytes; every FRUA level has {Length}");
        }

        int[] walls = [bytes[28], bytes[29], bytes[30]];

        var entries = new FruaEntryPoint[8];
        for (int i = 0; i < 8; i++)
        {
            // Four bytes each, not three: the reference reads y, x, facing and then one more it
            // discards, all inside the same loop. Getting this stride wrong slides everything
            // after it -- the level name is the cheapest place to notice.
            int at = 38 + (i * 4);
            entries[i] = new FruaEntryPoint(X: bytes[at + 1], Y: bytes[at],
                                            Facing: Facing(bytes[at + 2]));
        }

        var rests = new FruaRestEvent[8];
        for (int i = 0; i < 8; i++)
        {
            int at = 70 + (i * 4);
            rests[i] = new FruaRestEvent(
                EveryMinutes: bytes[at],
                // bytes[at + 1] is read and discarded -- "unknown byte".
                EventIndex: bytes[at + 2],
                Chance: bytes[at + 3] & 0x7F,
                AllowResting: (bytes[at + 3] & 0x80) != 0x80);
        }

        var steps = new FruaStepEvent[8];
        for (int i = 0; i < 8; i++)
        {
            int at = 102 + (i * 4);

            // A zero mask means every zone; otherwise a set bit EXCLUDES its zone, so the stored
            // byte is inverted into the inclusive mask the rest of the port wants.
            byte excluded = bytes[at + 3];
            int mask = excluded == 0 ? 0xFF : 0xFF & ~excluded;

            steps[i] = new FruaStepEvent(
                StepCount: bytes[at],
                EventIndex: bytes[at + 2],
                ZoneMask: mask);
        }

        // 134..141 are read and discarded before the name.
        return new FruaLevel(
            Width: bytes[27],
            Height: bytes[HeaderAt],
            Name: Text(bytes.Slice(142, 16)),

            // All three wall slots at 255 is how a design says "no walls here" -- the reference
            // turns that into an overland level drawn only in area view.
            IsOverland: walls is [255, 255, 255],
            AllowMapping: bytes[31] != 0,
            WallSlots: walls,
            BackdropSlots: [bytes[32], bytes[33], bytes[34], bytes[35]],
            DungeonCombatArt: bytes[36],
            WildernessCombatArt: bytes[37],
            EntryPoints: entries,
            RestEvents: rests,
            StepEvents: steps);
    }

    /// <summary>Reads level <paramref name="number"/> (one-based), or null when absent.</summary>
    /// <remarks>
    /// <b>Gaps are ordinary.</b> <c>HEIRS.DSN</c> ships 001–013, 015, 017–019, 025 and 033–040 —
    /// the reference simply tests each name for existence and skips what is missing, so a missing
    /// level is not an error.
    /// </remarks>
    public static FruaLevel? ReadFile(string designDirectory, int number)
    {
        ArgumentNullException.ThrowIfNull(designDirectory);

        string? path = FruaFiles.Resolve(designDirectory, $"geo{number:D3}.dat");
        return path is null ? null : Read(File.ReadAllBytes(path));
    }

    /// <summary>Every level a design carries, keyed by its one-based number.</summary>
    public static IReadOnlyDictionary<int, FruaLevel> ReadAll(string designDirectory)
    {
        var levels = new Dictionary<int, FruaLevel>();

        for (int i = 1; i <= MaxLevels; i++)
        {
            if (ReadFile(designDirectory, i) is { } level)
            {
                levels[i] = level;
            }
        }

        return levels;
    }

    private static FruaFacing Facing(byte stored) => (stored & 0x7) switch
    {
        0 => FruaFacing.North,
        2 => FruaFacing.East,
        4 => FruaFacing.South,
        6 => FruaFacing.West,
        _ => FruaFacing.Unknown,
    };

    private static string Text(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return FruaGameData.TextEncoding.GetString(field[..(end < 0 ? field.Length : end)]).Trim();
    }
}
