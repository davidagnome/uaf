using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UAF.Serialization;
using UAFedit.Map;

namespace UAFedit.Levels;

/// <summary>How a zone lets the party map itself (<c>MappingType</c>, <c>Level.h:110</c>).</summary>
public enum ZoneMapping
{
    AreaMapping = 0,
    AutoMapping = 1,
    NoMapping = 2,
}

/// <summary>
/// One of a level's sixteen zones (<c>ZONE</c>, <c>Level.cpp:231</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>CZoneDlg</c> edits one at a time, chosen by a spinner over 1…<c>MAX_ZONES</c>
/// (<c>Zonedlg.cpp:217</c>) whose value is one ahead of the index it reads
/// (<c>zone = m_ZoneNum-1</c>, <c>Zonedlg.cpp:88</c>). The zone byte in a cell is the index, so the
/// spinner's number is a label and nothing else — as with wall slots, the editor counts from one
/// where the data counts from zero.
/// </para>
/// <para>
/// The zone is where several rules that read like level rules actually live: whether magic works,
/// whether the party may rest, how hard undead are to turn, and which combat backdrop a fight
/// here uses.
/// </para>
/// </remarks>
public sealed partial class ZoneViewModel : ObservableObject
{
    internal ZoneViewModel(int index, Zone zone, MapColor color, int cellCount)
    {
        Index = index;
        Zone = zone;
        Color = color;
        CellCount = cellCount;
    }

    /// <summary>The zone's index, which is what a cell's zone byte holds.</summary>
    public int Index { get; }

    /// <summary>What the dialog's spinner shows: <see cref="Index"/> plus one.</summary>
    public int Number => Index + 1;

    /// <summary>The zone as read.</summary>
    public Zone Zone { get; }

    /// <summary>The colour the map's zone mode fills this zone's squares with.</summary>
    /// <remarks><c>GetZoneColor</c> (<c>UAFWinEd.cpp</c>) is the editor palette indexed by zone.</remarks>
    public MapColor Color { get; }

    /// <summary>How many of the level's squares are in this zone.</summary>
    /// <remarks>
    /// The original has no such count, and it is the one thing that distinguishes a zone the author
    /// configured from the fifteen defaults every level ships with. <b>Zone 0 is not "no zone"</b> —
    /// a cleared cell is in zone 0, so on most levels zone 0 holds everything.
    /// </remarks>
    public int CellCount { get; }

    public bool IsUsed => CellCount > 0;

    public string Name => Zone.Name;

    /// <summary>What the party is told on entering the zone.</summary>
    public string Message => Zone.Message;

    /// <inheritdoc cref="ZoneMapping"/>
    public ZoneMapping Mapping => (ZoneMapping)Zone.AllowMap;

    public string MappingText => Mapping switch
    {
        ZoneMapping.AreaMapping => "Area map",
        ZoneMapping.AutoMapping => "Auto map",
        ZoneMapping.NoMapping => "No map",
        _ => "Unknown",
    };

    public bool AllowMagic => Zone.AllowMagic != 0;

    public bool AllowAutoDarken => Zone.AllowAutoDarken != 0;

    /// <summary>Added difficulty when turning undead here, as a percentage.</summary>
    public int AddedTurningDifficulty => Zone.AddedTurningDifficulty;

    /// <summary>The monster a summon spell cast here produces.</summary>
    /// <remarks>
    /// The dialog's own label reads "Summoned Monster / Not used!" (<c>IDD_ZONEDLG</c>), so the
    /// field is still serialized but the engine no longer consults it.
    /// </remarks>
    public string SummonedMonster => Zone.SummonedMonster;

    public string IndoorCombatArt => Zone.IndoorCombatArt;

    public string OutdoorCombatArt => Zone.OutdoorCombatArt;

    /// <summary>Whether the party may camp in this zone.</summary>
    /// <remarks>
    /// Stored the positive way round and shown the negative way round: the checkbox is
    /// "Cant Rest in this zone" over <c>!restEvent.allowResting</c> (<c>Zonedlg.cpp:104</c>).
    /// </remarks>
    public bool AllowResting => Zone.Rest.AllowResting != 0;

    /// <summary>Percentage chance of the rest event firing.</summary>
    public int RestEventChance => Zone.Rest.Chance;

    /// <summary>How often that chance is rolled, in minutes of game time.</summary>
    public int RestEventMinutes => Zone.Rest.EveryMinutes;

    /// <summary>The event id the rest check runs, or 0.</summary>
    public uint RestEvent => Zone.Rest.Event;
}

/// <summary>
/// A level's zone table (<c>ID_VIEW_ZONES</c>, <c>MainFrm.cpp:649</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Sixteen, always.</b> <c>MAX_ZONES</c> (<c>Externs.h:858</c>) is a fixed table and a cell's
/// zone is a byte index into it; the count is written to the file
/// (<c>LevelStructureReaders.ReadZoneData</c>) but no shipped design writes anything but sixteen.
/// A design read as having fewer is honoured here rather than padded, because the reader is what
/// decides how many there are.
/// </para>
/// <para>
/// Read-only. The zone dialog edits art through file pickers, sounds through a sound chooser and a
/// monster through the monster database — none of which exists in this editor yet — so a writable
/// zone editor would be a form of six fields and eight dead buttons. What is here is the table plus
/// the one thing the original never showed: how much of the level is in each zone.
/// </para>
/// </remarks>
public sealed partial class ZonesViewModel : ObservableObject
{
    /// <summary><c>MAX_ZONES</c> (<c>Externs.h:858</c>).</summary>
    public const int MaxZones = LevelStructureReaders.ZonesPerLevel;

    /// <summary>Builds the table over a level, counting cells per zone.</summary>
    /// <param name="current"><c>currZone</c> — the zone the map's zone tool would paint.</param>
    public ZonesViewModel(LevelMapModel model, MapPalette? palette = null, int current = 0)
    {
        ArgumentNullException.ThrowIfNull(model);

        var colors = palette ?? MapPalette.Default;
        var zones = model.Level.Zones.Zones;
        var counts = CountCells(model, zones.Count);

        Zones =
        [
            .. zones.Select((zone, index) =>
                new ZoneViewModel(index, zone, colors.Zone(index), counts[index])),
        ];

        selectedIndex = Math.Clamp(current, 0, Math.Max(Zones.Count - 1, 0));
        AreaViewArt = model.Level.Zones.AreaViewArt;
    }

    /// <summary>The level's zones, in index order.</summary>
    public ObservableCollection<ZoneViewModel> Zones { get; }

    /// <summary>The level-wide area-view art, which sits in the zone block rather than in a zone.</summary>
    /// <remarks>
    /// The dialog's own label says so: "Area View Art for all zones" (<c>IDD_ZONEDLG</c>). It is
    /// written after the sixteen zones (<c>Level.cpp:568</c>) and only from version 0.731.
    /// </remarks>
    public string AreaViewArt { get; }

    /// <summary><c>currZone</c>: the zone the paint tool would apply.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected))]
    private int selectedIndex;

    public ZoneViewModel? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Zones.Count ? Zones[SelectedIndex] : null;

    /// <summary>How many zones the level actually puts squares in.</summary>
    public int UsedZoneCount => Zones.Count(z => z.IsUsed);

    /// <summary>
    /// How many squares are in each zone.
    /// </summary>
    /// <remarks>
    /// A zone byte past the table is dropped rather than throwing. The byte is unconstrained on the
    /// wire and the engine subscripts <c>zones[]</c> with it unchecked, so a corrupt cell is a crash
    /// there and a missing tally here.
    /// </remarks>
    private static int[] CountCells(LevelMapModel model, int zones)
    {
        var counts = new int[Math.Max(zones, 1)];

        for (int y = 0; y < model.Height; y++)
        {
            for (int x = 0; x < model.Width; x++)
            {
                int zone = model.At(x, y).Zone;
                if (zone >= 0 && zone < counts.Length)
                {
                    counts[zone]++;
                }
            }
        }

        return counts;
    }
}
