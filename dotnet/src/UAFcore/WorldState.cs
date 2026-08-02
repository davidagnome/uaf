using UAF.Serialization;

namespace UAFcore;

/// <summary>How far along a quest is (<c>QuestStateType</c>, <c>Shared/GlobalData.h:216</c>).</summary>
public enum QuestState
{
    NotStarted = 0,
    InProgress = 1,
    Complete = 2,
    Failed = 3,
}

/// <summary>
/// The mutable global state a design accumulates: quests, special items and keys.
/// </summary>
/// <remarks>
/// <para>
/// <b>These belong to <c>globalData</c>, not to the party</b>, even though the questions about
/// them are asked through <c>PARTY</c> methods. <c>PARTY::hasSpecialItem</c> and
/// <c>hasSpecialKey</c> (<c>Party.cpp:3275</c>, <c>:3293</c>) consult
/// <c>globalData.specialItemData</c> and <c>globalData.keyData</c> and never touch a character's
/// inventory — so a "special item" is a world flag with a counter, not something anyone carries.
/// Modelling it as party inventory would put it in the savegame's party record instead of its
/// global one and lose it on load.
/// </para>
/// <para>
/// Seeded from the design's own lists, which carry each entry's starting stage, and replaced
/// wholesale when a savegame is loaded.
/// </para>
/// </remarks>
public sealed class WorldState
{
    private readonly Dictionary<int, (QuestState State, int Stage)> quests = [];
    private readonly Dictionary<int, int> specialItems = [];
    private readonly Dictionary<int, int> keys = [];

    /// <summary>Builds the starting world from a design's <c>GLOBAL_STATS</c>.</summary>
    public static WorldState FromDesign(IReadOnlyList<Quest> designQuests,
                                        IReadOnlyList<SpecialObject> designSpecialItems,
                                        IReadOnlyList<SpecialObject> designKeys)
    {
        ArgumentNullException.ThrowIfNull(designQuests);
        ArgumentNullException.ThrowIfNull(designSpecialItems);
        ArgumentNullException.ThrowIfNull(designKeys);

        var world = new WorldState();

        foreach (var quest in designQuests)
        {
            world.quests[quest.Id] = ((QuestState)quest.State, quest.Stage);
        }

        foreach (var item in designSpecialItems)
        {
            world.specialItems[item.Id] = item.Stage;
        }

        foreach (var key in designKeys)
        {
            world.keys[key.Id] = key.Stage;
        }

        return world;
    }

    /// <summary>
    /// Whether a quest has been started at all (<c>QUEST_LIST::IsPresent</c>,
    /// <c>GlobalData.cpp:2097</c>).
    /// </summary>
    /// <remarks>
    /// <b>Present means "not <see cref="QuestState.NotStarted"/>"</b>, so a failed or completed
    /// quest is still present. A quest the design does not define is absent, and every one of
    /// these predicates answers false for it rather than throwing.
    /// </remarks>
    public bool IsQuestPresent(int id) =>
        quests.TryGetValue(id, out var quest) && quest.State != QuestState.NotStarted;

    public bool IsQuestInProgress(int id) => QuestIs(id, QuestState.InProgress);

    public bool IsQuestComplete(int id) => QuestIs(id, QuestState.Complete);

    public bool IsQuestFailed(int id) => QuestIs(id, QuestState.Failed);

    private bool QuestIs(int id, QuestState state) =>
        quests.TryGetValue(id, out var quest) && quest.State == state;

    /// <summary>Whether a quest sits at exactly this stage (<c>StageEqual</c>, <c>:2133</c>).</summary>
    public bool QuestStageEquals(int id, int stage) =>
        quests.TryGetValue(id, out var quest) && quest.Stage == stage;

    public QuestState QuestStateOf(int id) =>
        quests.TryGetValue(id, out var quest) ? quest.State : QuestState.NotStarted;

    public int QuestStageOf(int id) => quests.TryGetValue(id, out var quest) ? quest.Stage : 0;

    public void SetQuest(int id, QuestState state, int stage) => quests[id] = (state, stage);

    /// <summary>Moves a quest to a stage, leaving its state alone.</summary>
    /// <remarks>
    /// State and stage are independent in the reference too — <c>SetStage</c> and
    /// <c>SetComplete</c> are separate calls, and a quest event makes both in turn. Folding them
    /// into one assignment loses whichever the caller did not mean to change.
    /// </remarks>
    public void SetQuestStage(int id, int stage) => quests[id] = (QuestStateOf(id), stage);

    /// <summary>Moves a quest to a state, leaving its stage alone.</summary>
    public void SetQuestState(int id, QuestState state) => quests[id] = (state, QuestStageOf(id));

    /// <summary>
    /// Whether the party has a special item (<c>PARTY::hasSpecialItem</c>, <c>Party.cpp:3275</c>).
    /// </summary>
    /// <remarks>
    /// "Has" is <c>GetStage(item) &gt; 0</c> — the stage doubles as the possession flag, so stage 0
    /// is "not held" rather than "held, at the first stage". An id the design does not define logs
    /// a complaint and answers false.
    /// </remarks>
    public bool HasSpecialItem(int id) => specialItems.TryGetValue(id, out int stage) && stage > 0;

    /// <inheritdoc cref="HasSpecialItem"/>
    public bool HasKey(int id) => keys.TryGetValue(id, out int stage) && stage > 0;

    public int SpecialItemStage(int id) => specialItems.GetValueOrDefault(id);

    public int KeyStage(int id) => keys.GetValueOrDefault(id);

    public void SetSpecialItemStage(int id, int stage) => specialItems[id] = stage;

    public void SetKeyStage(int id, int stage) => keys[id] = stage;

    /// <summary>
    /// Whether the design defines this special item at all — held or not.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="HasSpecialItem"/>, which is "defined <i>and</i> at a stage above
    /// zero". The reference's <c>addSpecialItem</c>/<c>removeSpecialItem</c> both refuse an
    /// undefined id and log "Bogus special item index" rather than creating one
    /// (<c>Party.cpp:3203</c>), so an event naming a deleted item does nothing at all.
    /// </remarks>
    public bool DefinesSpecialItem(int id) => specialItems.ContainsKey(id);

    /// <inheritdoc cref="DefinesSpecialItem"/>
    public bool DefinesKey(int id) => keys.ContainsKey(id);
}
