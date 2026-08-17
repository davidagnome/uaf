using CommunityToolkit.Mvvm.ComponentModel;
using UAF.Serialization;
using UAFcore;

namespace UAFedit.Globals;

/// <summary>
/// The design's own settings — <c>game.dat</c>'s <c>GLOBAL_STATS</c>.
/// </summary>
/// <remarks>
/// <para>
/// The Avalonia replacement for the reference's Design menu, whose settings were spread over
/// several modal dialogs. What is offered here is the scalar half of <c>GLOBAL_STATS</c>: the
/// design's identity, where a new party starts, what it starts with, the party-size limits and the
/// three named art files. <b>Everything else in the record is carried through untouched</b> — the
/// title and credits sequences, the art slots, the sounds, keys, quests, the level table, the
/// pregenerated characters, the journal and the global events all go back exactly as they were
/// read.
/// </para>
/// <para>
/// <b>It reads <c>game.dat</c> itself rather than taking <see cref="LoadedDesign.Globals"/>.</b>
/// The design's own read stops before the global event list, and a save that wrote the prefix
/// without those events would silently empty them — see <see cref="DesignGlobals"/>.
/// </para>
/// <para>
/// <b>Dirtiness is a comparison, not a flag.</b> <see cref="GlobalStatsPrefix"/> is a record, so
/// the edited prefix is compared against the one that was read and a value typed and typed back
/// leaves the pane clean. The comparison is on the fields this pane owns, because the record's
/// generated equality compares its many list members by reference and would answer "different" for
/// two identical reads.
/// </para>
/// </remarks>
public sealed partial class DesignGlobalsViewModel : ObservableObject
{
    private readonly string root;
    private GameData data;

    /// <summary>Opens the pane over a design, reading its <c>game.dat</c>.</summary>
    public DesignGlobalsViewModel(LoadedDesign design)
        : this((design ?? throw new ArgumentNullException(nameof(design))).Root)
    {
    }

    /// <summary>Opens the pane over a design folder — the seam the tests use.</summary>
    public DesignGlobalsViewModel(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        this.root = root;
        data = DesignGlobals.Read(root);

        Load(data.Global);
    }

    /// <summary>How many global events the file carries, which this pane does not edit.</summary>
    /// <remarks>
    /// Shown so it is visible that they exist and are being preserved. A pane that silently
    /// carried them would look identical to one that dropped them.
    /// </remarks>
    public int GlobalEventCount => data.Events.Count;

    /// <summary>The version the file declares, before a save restamps it.</summary>
    public string Version => data.Global.Version.Value.ToString("0.######");

    [ObservableProperty]
    private string designName = string.Empty;

    [ObservableProperty]
    private int startLevel;

    [ObservableProperty]
    private byte startX;

    [ObservableProperty]
    private byte startY;

    /// <summary>0–3, the compass direction the party faces on a new game.</summary>
    [ObservableProperty]
    private byte startFacing;

    [ObservableProperty]
    private int startTime;

    [ObservableProperty]
    private int startExp;

    [ObservableProperty]
    private int startPlatinum;

    [ObservableProperty]
    private int startGem;

    [ObservableProperty]
    private int startJewelry;

    [ObservableProperty]
    private int minPcs;

    [ObservableProperty]
    private int maxPartyMaxPcs;

    [ObservableProperty]
    private string mapArt = string.Empty;

    [ObservableProperty]
    private string iconBackgroundArt = string.Empty;

    [ObservableProperty]
    private string backgroundArt = string.Empty;

    /// <summary>Whether anything has been changed since the file was read or last saved.</summary>
    public bool IsDirty => !Same(Edited, data.Global);

    /// <summary>The whole file as it now stands — the edited prefix and the untouched events.</summary>
    public GameData EditedData => data with { Global = Edited };

    /// <summary>The prefix with this pane's fields applied over the one that was read.</summary>
    private GlobalStatsPrefix Edited => data.Global with
    {
        DesignName = DesignName,
        StartLevel = StartLevel,
        StartX = StartX,
        StartY = StartY,
        StartFacing = StartFacing,
        StartTime = StartTime,
        StartExp = StartExp,
        StartPlatinum = StartPlatinum,
        StartGem = StartGem,
        StartJewelry = StartJewelry,
        MinPcs = MinPcs,
        MaxPartyMaxPcs = MaxPartyMaxPcs,
        MapArt = MapArt,
        IconBackgroundArt = IconBackgroundArt,
        BackgroundArt = BackgroundArt,
    };

    /// <summary>Writes <c>game.dat</c> back and treats what is on screen as saved.</summary>
    /// <remarks>
    /// The write comes first: a pane marked clean by a save that then threw is an edit the user
    /// has lost without being told.
    /// </remarks>
    public void Save()
    {
        var saving = EditedData;
        DesignGlobals.Write(root, saving);

        data = saving;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Throws away every edit and shows the file again.</summary>
    public void Revert() => Load(data.Global);

    private void Load(GlobalStatsPrefix global)
    {
        DesignName = global.DesignName;
        StartLevel = global.StartLevel;
        StartX = global.StartX;
        StartY = global.StartY;
        StartFacing = global.StartFacing;
        StartTime = global.StartTime;
        StartExp = global.StartExp;
        StartPlatinum = global.StartPlatinum;
        StartGem = global.StartGem;
        StartJewelry = global.StartJewelry;
        MinPcs = global.MinPcs;
        MaxPartyMaxPcs = global.MaxPartyMaxPcs;
        MapArt = global.MapArt;
        IconBackgroundArt = global.IconBackgroundArt;
        BackgroundArt = global.BackgroundArt;

        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>
    /// Compares the fields this pane owns.
    /// </summary>
    /// <remarks>
    /// Not <c>==</c>: <see cref="GlobalStatsPrefix"/> holds a dozen lists, and a record's generated
    /// equality compares those by reference — so two identical reads are never equal and every
    /// pane would open dirty.
    /// </remarks>
    private static bool Same(GlobalStatsPrefix a, GlobalStatsPrefix b) =>
        a.DesignName == b.DesignName
        && a.StartLevel == b.StartLevel
        && a.StartX == b.StartX
        && a.StartY == b.StartY
        && a.StartFacing == b.StartFacing
        && a.StartTime == b.StartTime
        && a.StartExp == b.StartExp
        && a.StartPlatinum == b.StartPlatinum
        && a.StartGem == b.StartGem
        && a.StartJewelry == b.StartJewelry
        && a.MinPcs == b.MinPcs
        && a.MaxPartyMaxPcs == b.MaxPartyMaxPcs
        && a.MapArt == b.MapArt
        && a.IconBackgroundArt == b.IconBackgroundArt
        && a.BackgroundArt == b.BackgroundArt;

    /// <summary>Any edit re-evaluates the dirty flag.</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        ArgumentNullException.ThrowIfNull(e);

        if (e.PropertyName != nameof(IsDirty))
        {
            OnPropertyChanged(nameof(IsDirty));
        }
    }
}
