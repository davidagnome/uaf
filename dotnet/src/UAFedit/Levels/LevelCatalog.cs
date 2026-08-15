using UAF.Serialization;
using UAFcore;

namespace UAFedit.Levels;

/// <summary>
/// <c>GlobalAreaViewStyle</c> (<c>Externs.h</c>) — which views a level permits.
/// </summary>
/// <remarks>
/// The names the level list shows depend on <see cref="LevelCatalogEntry.IsOverland"/> as well as
/// on the value: the same ordinal is "Area Only" on a dungeon and "Large Only" on a wilderness
/// (<c>SelectLevel.cpp:307</c>). So this is the raw ordinal and
/// <see cref="LevelCatalogEntry.AreaViewStyleText"/> is the label.
/// </remarks>
public enum AreaViewStyle
{
    AnyView = 0,
    OnlyAreaView = 1,
    Only3DView = 2,
}

/// <summary>
/// One row of the level list: a file on disk, the number it claims, and the stats that go with it.
/// </summary>
/// <param name="Position">
/// Where the file sits in <see cref="LoadedDesign.LevelFiles"/> — a sorted directory listing, and
/// <b>not</b> the level's number. Only <see cref="LoadedDesign.Level"/> and
/// <see cref="LoadedDesign.Map"/> take this.
/// </param>
/// <param name="Number">
/// The level number, from the file name. One-based: <c>Level001.lvl</c> is level 1. This is the
/// number the editor's own list shows (<c>SelectLevel.cpp:275</c> prints <c>level+1</c>) and the
/// number a teleport event names.
/// </param>
/// <param name="StatsIndex">
/// <see cref="Number"/> minus one — the key into <see cref="LevelInfo.Levels"/>, and the <c>i</c>
/// every <c>stats[i]</c> in the reference is subscripted by.
/// </param>
/// <param name="StoredNumber">
/// <c>m_level + 1</c> as the file itself records it, or null when the file could not be read.
/// <b>Diagnostic only</b> — see <see cref="AgreesWithFileName"/>.
/// </param>
/// <param name="Stats">The matching <c>LEVEL_STATS</c>, or null when the table has no such row.</param>
public sealed record LevelCatalogEntry(
    int Position, int Number, int StatsIndex, string Path, string FileName,
    int? StoredNumber, LevelStats? Stats, int? Width, int? Height, int? EventCount,
    int? WallSetCount, int? ZoneCount, bool IsReadable)
{
    /// <summary>The level's name from <c>LEVEL_STATS</c>, or empty when it has no stats row.</summary>
    public string Name => Stats?.Name ?? string.Empty;

    /// <summary>
    /// <c>stats[i].used</c>, corrected the way the original's level list corrects it.
    /// </summary>
    /// <remarks>
    /// <c>CSelectLevel::OnInitDialog</c> (<c>SelectLevel.cpp:222</c>) clears <c>used</c> on any
    /// level whose <c>.lvl</c> is missing and then <i>reports the list of them in an error box</i>.
    /// A catalog entry always has a file — see <see cref="LevelCatalog.Orphans"/> for the rows that
    /// do not — so here the flag is only ever read, never cleared.
    /// </remarks>
    public bool IsUsed => Stats?.Used != 0;

    public bool IsOverland => Stats?.Overland != 0;

    /// <summary>
    /// Whether the number in the file name and the number stored inside the file agree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>They need not, and on real data they do not.</b> <c>Case</c>'s <c>Level004.lvl</c> stores
    /// <c>m_level</c> 11, because it was saved as a copy of level 12 and <c>m_level</c> came along.
    /// The file name wins: every path the engine and editor build is
    /// <c>"Level" + %.3i(index+1) + ".lvl"</c> (<c>Level.cpp:3643</c>, <c>:3669</c>), so
    /// <c>LoadLevel(3)</c> reads <c>Level004.lvl</c> and pairs it with <c>stats[3]</c> whatever the
    /// bytes inside say.
    /// </para>
    /// <para>
    /// Keying on <c>m_level</c> instead would pair <c>Case</c>'s <c>Level004.lvl</c> grid with
    /// <c>Level012.lvl</c>'s name, size and entry points. That is why this is surfaced rather than
    /// quietly preferred or quietly ignored.
    /// </para>
    /// </remarks>
    public bool AgreesWithFileName => StoredNumber is null || StoredNumber == Number;

    /// <summary>Whether <see cref="Position"/> happens to equal <see cref="StatsIndex"/>.</summary>
    /// <remarks>
    /// True for every level of a design numbered without holes, which is why conflating the two
    /// survives so long. False from <c>Case</c>'s fifth file onwards.
    /// </remarks>
    public bool PositionMatchesNumber => Position == StatsIndex;

    /// <summary>The size from <c>LEVEL_STATS</c>, which may disagree with the file's own.</summary>
    /// <remarks>
    /// The original's list shows the stats width and height and never opens the file
    /// (<c>SelectLevel.cpp:295</c>), and shows 0 × 0 for an unused level whatever the stats hold.
    /// Both extents are surfaced here because a disagreement is a real corruption and the list is
    /// the only place it would ever be visible.
    /// </remarks>
    public string StatsSize => Stats is null ? string.Empty
                             : !IsUsed ? "0 x 0"
                             : $"{Stats.Width} x {Stats.Height}";

    /// <summary>The extent of the grid actually in the file.</summary>
    public string FileSize => Width is { } w && Height is { } h ? $"{w} x {h}" : string.Empty;

    /// <summary>Whether the stats and the file agree about the level's extent.</summary>
    public bool SizeAgrees =>
        Stats is null || Width is null || Height is null
        || (Stats.Width == Width && Stats.Height == Height);

    /// <inheritdoc cref="AreaViewStyle"/>
    public AreaViewStyle AreaViewStyle => (AreaViewStyle)(Stats?.AreaViewStyle ?? 0);

    /// <summary>The area-view restriction as the original's list words it.</summary>
    /// <remarks><c>SelectLevel.cpp:300-320</c>. The wording changes with the level's kind.</remarks>
    public string AreaViewStyleText => AreaViewStyle switch
    {
        Levels.AreaViewStyle.AnyView => "Any",
        Levels.AreaViewStyle.OnlyAreaView => IsOverland ? "Large Only" : "Area Only",
        Levels.AreaViewStyle.Only3DView => IsOverland ? "Small Only" : "3D Only",
        _ => "Unknown",
    };
}

/// <summary>
/// A <c>LEVEL_STATS</c> row whose <c>.lvl</c> file is not on disk.
/// </summary>
/// <remarks>
/// The reference's own level list finds these, clears their <c>used</c> flag and pops an error box
/// naming them (<c>SelectLevel.cpp:222-262</c>). They are kept as a separate list rather than as
/// catalog entries with a null path so that nothing downstream has to test for a level with no
/// level in it.
/// </remarks>
public sealed record LevelCatalogOrphan(int Number, int StatsIndex, LevelStats Stats)
{
    public string Name => Stats.Name;

    /// <summary>Whether the table claims this level is in use despite having no file.</summary>
    public bool ClaimsUsed => Stats.Used != 0;
}

/// <summary>
/// The design's levels: every <c>.lvl</c> on disk, paired with the right <c>LEVEL_STATS</c> row.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type exists because two different numbers are both called "the level".</b> A design's
/// levels are files named <c>Level001.lvl</c>…<c>Level255.lvl</c> and its <c>LEVEL_INFO</c> is a
/// <i>sparse</i> table keyed by a zero-based index (<c>GlobalData.cpp:3574</c>): the file's number
/// is that index plus one. <see cref="LoadedDesign.LevelFiles"/> is neither — it is a sorted
/// directory listing, so a file's position in it is its number only when the design is numbered
/// without holes.
/// </para>
/// <para>
/// <b>Designs ship holes.</b> <c>Case.dsn</c> has ten files — 001, 002, 003, 004, 011, 012, 013,
/// 016, 018, 255 — so its tenth file is level 255, at position 9, keyed by <c>stats[254]</c>. Using
/// the position as the key there reads the name, size and entry points of an entirely different
/// level, or of none. So every entry carries all three numbers and derives the key from the file
/// name.
/// </para>
/// <para>
/// The reference has no equivalent: it iterates <c>stats[0..MAX_LEVELS)</c> and asks
/// <c>levelExists(i)</c>, which builds the path from <c>i+1</c> (<c>Level.cpp:3665</c>). That is the
/// same pairing, arrived at from the other end.
/// </para>
/// </remarks>
public sealed class LevelCatalog
{
    /// <summary>The file-name prefix every level file carries (<c>Level.cpp:3643</c>).</summary>
    public const string FilePrefix = "Level";

    /// <summary><c>MAX_LEVELS</c> (<c>Externs.h:905</c>) — so numbers run 1…255.</summary>
    public const int MaxLevels = 255;

    private LevelCatalog(IReadOnlyList<LevelCatalogEntry> entries,
                         IReadOnlyList<LevelCatalogOrphan> orphans,
                         IReadOnlyList<string> unnamed,
                         int declaredLevelCount)
    {
        Entries = entries;
        Orphans = orphans;
        Unnamed = unnamed;
        DeclaredLevelCount = declaredLevelCount;
    }

    /// <summary>Every readable level file, in directory order.</summary>
    public IReadOnlyList<LevelCatalogEntry> Entries { get; }

    /// <summary>Stats rows with no file behind them.</summary>
    public IReadOnlyList<LevelCatalogOrphan> Orphans { get; }

    /// <summary>Files in <c>Data/</c> whose names do not parse as a level number.</summary>
    /// <remarks>
    /// Not a theoretical case: an author's <c>Level001.lvl.bak</c> would be globbed by
    /// <see cref="LoadedDesign.LevelFiles"/> (<c>*.lvl</c>) only if it kept the extension, but a
    /// <c>Backup.lvl</c> would. Listed rather than silently dropped so the count in the UI adds up.
    /// </remarks>
    public IReadOnlyList<string> Unnamed { get; }

    /// <summary>
    /// <c>LEVEL_INFO::numLevels</c> as the design declares it.
    /// </summary>
    /// <remarks>
    /// <b>It is a count, not a highest number, and it is not the size of the table.</b>
    /// <c>ReadLevelInfo</c> reads it and then reads a <i>separate</i> count of populated rows
    /// (<c>GlobalData.cpp:3574</c>). <c>CSelectLevel::OnOK</c> recomputes it by counting <c>used</c>
    /// flags before returning, so a design that was last saved by a crash can carry a stale value.
    /// </remarks>
    public int DeclaredLevelCount { get; }

    /// <summary>
    /// The number a level file's name claims, or -1.
    /// </summary>
    /// <remarks>
    /// One-based, because the name is formatted from <c>LevelIndex+1</c>
    /// (<c>Level.cpp:3643</c>). Case-insensitive on the prefix because the reference builds the name
    /// on a case-insensitive filesystem and shipped designs are inconsistent about it.
    /// </remarks>
    public static int NumberFromFileName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string name = System.IO.Path.GetFileNameWithoutExtension(path);

        return name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(name.AsSpan(FilePrefix.Length), out int number)
               && number > 0
            ? number
            : -1;
    }

    /// <summary>The <c>.lvl</c> file name a level number is stored under.</summary>
    /// <remarks><c>"Level" + %.3i + ".lvl"</c> — three digits, so level 255 is <c>Level255.lvl</c>.</remarks>
    public static string FileNameFor(int number) => $"{FilePrefix}{number:000}.lvl";

    /// <summary>
    /// Builds the catalog, reading every level file.
    /// </summary>
    /// <param name="readFiles">
    /// Whether to open each <c>.lvl</c> for its extent, wall table and event count. Off gives a list
    /// built from <c>game.dat</c> alone, which is what the original's list shows and costs nothing;
    /// <c>Case</c>'s ten levels carry 4,244 events between them and reading them all takes real time.
    /// </param>
    public static LevelCatalog Build(LoadedDesign design, bool readFiles = true)
    {
        ArgumentNullException.ThrowIfNull(design);

        var table = design.Globals.Levels;
        var entries = new List<LevelCatalogEntry>();
        var unnamed = new List<string>();
        var claimed = new HashSet<int>();

        var files = design.LevelFiles;
        for (int position = 0; position < files.Count; position++)
        {
            string path = files[position];
            int number = NumberFromFileName(path);

            if (number < 0)
            {
                unnamed.Add(path);
                continue;
            }

            int statsIndex = number - 1;
            claimed.Add(statsIndex);

            LevelStats? stats = null;
            table?.Levels.TryGetValue((uint)statsIndex, out stats);

            // Level() reads the whole file and returns null on an event type this port cannot read;
            // Map() reads only the self-delimiting grid and always succeeds. Asking for the second
            // when the first fails means a level with one unported event still reports its extent.
            var level = readFiles ? design.Level(position) : null;
            var map = readFiles && level is null ? design.Map(position) : null;

            entries.Add(new LevelCatalogEntry(
                Position: position,
                Number: number,
                StatsIndex: statsIndex,
                Path: path,
                FileName: System.IO.Path.GetFileName(path),
                StoredNumber: level is null ? null : level.Level + 1,
                Stats: stats,
                Width: level?.Width ?? map?.Width,
                Height: level?.Height ?? map?.Height,
                EventCount: level?.EventCount,
                WallSetCount: level?.WallSets.Count,
                ZoneCount: level?.Zones.Zones.Count,
                IsReadable: !readFiles || level is not null));
        }

        var orphans = new List<LevelCatalogOrphan>();
        if (table is not null)
        {
            foreach (var (index, stats) in table.Levels.OrderBy(pair => pair.Key))
            {
                if (!claimed.Contains((int)index))
                {
                    orphans.Add(new LevelCatalogOrphan((int)index + 1, (int)index, stats));
                }
            }
        }

        return new LevelCatalog(entries, orphans, unnamed, table?.NumberOfLevels ?? 0);
    }

    /// <summary>The entry for a level number, or null.</summary>
    public LevelCatalogEntry? ByNumber(int number) =>
        Entries.FirstOrDefault(e => e.Number == number);

    /// <summary>The entry at a position in <see cref="LoadedDesign.LevelFiles"/>, or null.</summary>
    public LevelCatalogEntry? ByPosition(int position) =>
        Entries.FirstOrDefault(e => e.Position == position);
}
