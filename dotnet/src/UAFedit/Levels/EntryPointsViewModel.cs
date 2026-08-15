using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using UAF.Serialization;
using UAFcore;

namespace UAFedit.Levels;

/// <summary>
/// One of a level's eight entry points: where the party lands, and which way it faces.
/// </summary>
/// <remarks>
/// <para>
/// The row <c>CEntryPointDlg</c> spells out eight times over in <c>DoDataExchange</c>
/// (<c>EntryPointDlg.cpp:70</c>) — sixteen <c>DDX_Text</c> calls and sixteen <c>DDV_MinMaxInt</c>
/// ones against <c>m_x1</c>…<c>m_y8</c>.
/// </para>
/// <para>
/// <b>The facing is not in the entry-point table.</b> <c>POINT</c> is two <c>LONG</c>s and that is
/// all that is serialized (<c>GlobalData.cpp:3199</c>, with the facing's write commented out and
/// annotated "stored as attribute"). <c>PreSerialize</c> copies each facing into the level ASL under
/// <c>EPFace_<i>i</i></c> and <c>PostSerialize</c> reads it back out and deletes the key
/// (<c>GlobalData.cpp:3384</c>, <c>:3418</c>) — so on disk the facings live in
/// <c>LEVEL_STATS_ATTRIBUTES</c>, and a reader that only looks at the <c>POINT</c>s finds every
/// entry point facing north.
/// </para>
/// </remarks>
public sealed partial class EntryPointViewModel : ObservableObject
{
    private readonly Action? changed;

    internal EntryPointViewModel(int index, EntryPoint point, Facing facing, Action? changed)
    {
        Index = index;
        this.changed = changed;
        x = point.X;
        y = point.Y;
        this.facing = facing;
    }

    /// <summary>The four facings, for a picker to bind to.</summary>
    public static IReadOnlyList<Facing> Facings { get; } = Enum.GetValues<Facing>();

    /// <summary>Zero-based slot. The dialog labels these 1…8 (<c>EntryPointDlg</c> IDC_EPX1…8).</summary>
    public int Index { get; }

    /// <summary>What the dialog's row label shows.</summary>
    public int Number => Index + 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefault))]
    [NotifyPropertyChangedFor(nameof(Coordinates))]
    private int x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefault))]
    [NotifyPropertyChangedFor(nameof(Coordinates))]
    private int y;

    [ObservableProperty]
    private Facing facing;

    /// <summary>The pair as text, for a list that has no room for two columns.</summary>
    public string Coordinates => $"{X}, {Y}";

    /// <summary>
    /// Whether the slot still holds what <c>LEVEL_STATS::Clear</c> put there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The cleared value is (0, <i>i</i>), not (0, 0)</b> — <c>entryPoints[i].x=0;
    /// entryPoints[i].y=i;</c> (<c>GlobalData.cpp:3059</c>). So an untouched level's eight entry
    /// points are a column down the west edge, one per row, and all eight are real coordinates that
    /// the map will happily draw markers on. Every one of <c>Case</c>'s ten levels is in exactly
    /// this state.
    /// </para>
    /// <para>
    /// There is no "unset" to distinguish it from: a design that genuinely wants the party to arrive
    /// at (0, 3) writes the identical bytes. This is a hint for the UI, not a fact about the data,
    /// which is why nothing here treats a default slot as absent.
    /// </para>
    /// </remarks>
    public bool IsDefault => X == 0 && Y == Index;

    partial void OnXChanged(int value) => changed?.Invoke();

    partial void OnYChanged(int value) => changed?.Invoke();

    partial void OnFacingChanged(Facing value) => changed?.Invoke();
}

/// <summary>
/// A level's eight entry points, editable (<c>ID_VIEW_ENTRYPOINTS</c>, <c>MainFrm.cpp:1543</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>CEntryPointDlg</c> takes a <c>LEVEL_STATS&amp;</c>, copies the eight points out, and copies
/// them back through <c>GetData</c> on OK. This does the same with an immutable
/// <see cref="LevelStats"/>: <see cref="Apply"/> returns a new record rather than mutating one, so
/// nothing is committed until a caller takes the result.
/// </para>
/// <para>
/// <b>The dialog's validation bounds are one short of the level's extent and are read from the
/// stats, not the file</b> — <c>m_MaxX.Format("%i", data.area_width-1)</c>
/// (<c>EntryPointDlg.cpp:66</c>). On a level whose stats and grid disagree that lets the author
/// enter a coordinate off the actual map, and forbids one that is on it. <see cref="MaxX"/> follows
/// the reference; <see cref="Validate"/> is what a caller uses to find out.
/// </para>
/// <para>
/// <b>An out-of-range entry point is not clamped anywhere.</b> The teleport path reads
/// <c>stats[destLevel].entryPoints[destEP]</c> straight into the party's position
/// (<c>GameEvent.cpp:14654</c>), so the dialog's <c>DDV_MinMaxInt</c> is the only thing standing
/// between a typo and the party landing outside the grid.
/// </para>
/// </remarks>
public sealed partial class EntryPointsViewModel : ObservableObject
{
    /// <summary><c>MAX_ENTRY_POINTS</c> (<c>Externs.h:904</c>).</summary>
    /// <remarks>
    /// Taken from the reader rather than restated: the table is a compile-time eight and the writer
    /// refuses a <see cref="LevelStats"/> carrying any other number
    /// (<c>GlobalTailWriters.cs:188</c>), so a second literal here could only ever be wrong.
    /// </remarks>
    public const int Count = GlobalStatsTailReaders.MaxEntryPoints;

    /// <summary>The ASL key each facing is parked under while the stats are on disk.</summary>
    /// <remarks><c>key.Format("EPFace_%i", i)</c> (<c>GlobalData.cpp:3383</c>).</remarks>
    public const string FacingKeyPrefix = "EPFace_";

    private readonly LevelStats? stats;

    /// <summary>Builds the editor over a level's stats. A null stats row gives eight defaults.</summary>
    /// <param name="current">
    /// <c>currEntryPoint</c>, the editor-wide "which entry point am I placing" radio
    /// (<c>MainFrm.cpp:1543</c> passes it in and <c>GetData</c> writes it back). It is global in the
    /// reference rather than per level, so it survives a level change.
    /// </param>
    public EntryPointsViewModel(LevelStats? stats, int current = 0)
    {
        this.stats = stats;

        var points = stats?.EntryPoints;
        var facings = FacingsFrom(stats);

        Points =
        [
            .. Enumerable.Range(0, Count).Select(i => new EntryPointViewModel(
                i,
                points is not null && i < points.Count ? points[i] : new EntryPoint(0, i),
                facings[i],
                MarkDirty)),
        ];

        selectedIndex = Math.Clamp(current, 0, Count - 1);

        // The reference's bounds, exactly: the stats extent less one, and 50 when there are no
        // stats at all (m_MaxX's AFX_DATA_INIT value, EntryPointDlg.cpp:56).
        MaxX = (stats?.Width ?? 51) - 1;
        MaxY = (stats?.Height ?? 51) - 1;
    }

    /// <summary>The eight slots, always eight of them.</summary>
    public ObservableCollection<EntryPointViewModel> Points { get; }

    /// <summary>Largest legal X, as the dialog computes it.</summary>
    public int MaxX { get; }

    /// <summary>Largest legal Y, as the dialog computes it.</summary>
    public int MaxY { get; }

    /// <summary>Which slot the map's entry-point tool would place.</summary>
    [ObservableProperty]
    private int selectedIndex;

    /// <summary>Whether anything has been edited since the stats were read.</summary>
    [ObservableProperty]
    private bool isDirty;

    /// <summary>The slot the radio column has selected.</summary>
    public EntryPointViewModel Selected => Points[Math.Clamp(SelectedIndex, 0, Count - 1)];

    /// <summary>
    /// Every slot whose coordinates fall outside the level, as human-readable complaints.
    /// </summary>
    /// <remarks>
    /// Empty on every shipped design read so far — which is the point of running it, since a
    /// non-empty result means either a corrupt table or the stats/grid size disagreement above.
    /// </remarks>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        foreach (var point in Points)
        {
            if (point.X < 0 || point.X > MaxX)
            {
                problems.Add($"Entry point {point.Number}: X {point.X} is outside 0–{MaxX}");
            }

            if (point.Y < 0 || point.Y > MaxY)
            {
                problems.Add($"Entry point {point.Number}: Y {point.Y} is outside 0–{MaxY}");
            }
        }

        return problems;
    }

    /// <summary>
    /// The stats row with the edited entry points and facings written back into it.
    /// </summary>
    /// <remarks>
    /// The facings go back into the ASL under <see cref="FacingKeyPrefix"/> because that is where
    /// the writer expects to find them; the eight <c>POINT</c>s go into the fixed table. Any other
    /// attribute the level carries is preserved untouched — a level ASL holds design-authored keys
    /// as well as these eight, and dropping them would silently delete author data.
    /// </remarks>
    public LevelStats Apply(LevelStats target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var attributes = target.Attributes
            .Where(a => !IsFacingKey(a.Key))
            .ToList();

        foreach (var point in Points)
        {
            // The reference stores every facing, including the zero the default carries, because
            // PreSerialize writes all eight unconditionally.
            attributes.Add(new AslEntry(
                FacingKeyPrefix + point.Index.ToString(CultureInfo.InvariantCulture),
                Flags: 0,
                ((int)point.Facing).ToString(CultureInfo.InvariantCulture)));
        }

        return target with
        {
            EntryPoints = [.. Points.Select(p => new EntryPoint(p.X, p.Y))],
            Attributes = attributes,
        };
    }

    /// <summary>Discards edits and reloads from the stats row this was built over.</summary>
    public void Revert()
    {
        var points = stats?.EntryPoints;
        var facings = FacingsFrom(stats);

        for (int i = 0; i < Count; i++)
        {
            var point = points is not null && i < points.Count ? points[i] : new EntryPoint(0, i);
            Points[i].X = point.X;
            Points[i].Y = point.Y;
            Points[i].Facing = facings[i];
        }

        IsDirty = false;
    }

    private void MarkDirty() => IsDirty = true;

    private static bool IsFacingKey(string key) =>
        key.StartsWith(FacingKeyPrefix, StringComparison.Ordinal);

    /// <summary>The eight facings, dug out of the level ASL.</summary>
    private static Facing[] FacingsFrom(LevelStats? stats)
    {
        var facings = new Facing[Count];

        if (stats is null)
        {
            return facings;
        }

        foreach (var entry in stats.Attributes)
        {
            if (!IsFacingKey(entry.Key)
                || !int.TryParse(entry.Key.AsSpan(FacingKeyPrefix.Length), out int index)
                || index < 0 || index >= Count)
            {
                continue;
            }

            // The value is stored through StoreIntAsASL, which formats a plain integer; anything
            // else is a design that hand-edited the key, and north is the reference's fallback
            // because RetrieveIntFromASL leaves temp untouched on a miss.
            if (int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                             out int facing))
            {
                facings[index] = (Facing)(facing & 3);
            }
        }

        return facings;
    }
}
