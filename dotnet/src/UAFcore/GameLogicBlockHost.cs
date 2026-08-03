using System.Globalization;
using System.Text;

namespace UAFcore;

/// <summary>
/// The running game, as a logic block's terminals and actions see it.
/// </summary>
/// <remarks>
/// <para>
/// The reference reads six globals — <c>globalData</c>, <c>party</c>, <c>itemData</c>,
/// <c>raceData</c>, <c>classData</c> and <c>grepReg</c>. This is the adapter that names them, and
/// keeping it separate from <see cref="Game"/> is what lets the sixteen input types and twelve
/// action types be tested against a fake instead of a loaded design.
/// </para>
/// <para>
/// <b>Two stores the reference has and this port does not distinguish.</b> <c>global_asl</c> and
/// <c>temp_asl</c> are separate lists in <c>globalData</c>; the port has one
/// <see cref="Game.Globals"/>. They are kept apart here by a key prefix rather than merged,
/// because a design that writes a temporary and reads a global expects to see nothing.
/// </para>
/// </remarks>
/// <param name="questIds">
/// The design's quest names against their ids. A logic block names a quest by its <b>name</b>
/// (<c>questData.GetStage(key)</c>), where every other part of the engine addresses one by the
/// packed id in a <c>QUEST_EVENT_DATA</c> — so this map is the only place the two meet.
/// </param>
public sealed class GameLogicBlockHost(Game game, IReadOnlyDictionary<string, int>? questIds = null)
    : ILogicBlockActionHost
{
    private readonly Game game = game ?? throw new ArgumentNullException(nameof(game));

    private readonly IReadOnlyDictionary<string, int> questIds =
        questIds ?? new Dictionary<string, int>();

    /// <summary>
    /// What a temporary attribute's key is prefixed with inside the shared store.
    /// </summary>
    /// <remarks>
    /// Chosen to be unwritable from a design: an ASL key comes from a logic block parameter or a
    /// script, and neither can produce a leading NUL.
    /// </remarks>
    public const string TempPrefix = "\0temp\0";

    /// <summary>The last regex captures, for <see cref="LogicInput.Wiggle"/>.</summary>
    public IReadOnlyList<string> GrepCaptures { get; set; } = [];

    /// <inheritdoc/>
    public int PartySize => game.Party?.Count ?? 0;

    /// <inheritdoc/>
    public int ActiveCharacter => game.Party?.ActiveCharacter ?? 0;

    /// <inheritdoc/>
    public int Facing => (int)game.Facing;

    /// <inheritdoc/>
    public int CurrentLevel => game.LevelIndex;

    /// <inheritdoc/>
    public string CharacterName(int index) =>
        game.Party is { } party && index >= 0 && index < party.Count
            ? party.Members[index].Name
            : string.Empty;

    /// <inheritdoc/>
    public string Attribute(LogicAslScope scope, int character, string key) =>
        Store(scope, character)?.Find(Key(scope, key)) ?? string.Empty;

    /// <inheritdoc/>
    public void SetAttribute(LogicAslScope scope, int character, string key, string value) =>
        Store(scope, character)?.Insert(Key(scope, key), value);

    /// <inheritdoc/>
    public void RemoveAttribute(LogicAslScope scope, int character, string key) =>
        Store(scope, character)?.Remove(Key(scope, key));

    /// <inheritdoc/>
    /// <remarks>
    /// A design's per-level attributes live in <c>GLOBAL_STATS.levelInfo</c>, which this port reads
    /// but does not keep a mutable copy of — so they are held here, keyed by level, for the
    /// lifetime of the game.
    /// </remarks>
    public string LevelAttribute(int level, string key) =>
        levels.TryGetValue(level, out var store) ? store.Find(key) ?? string.Empty : string.Empty;

    /// <inheritdoc/>
    public void SetLevelAttribute(int level, string key, string value) =>
        LevelStore(level).Insert(key, value);

    /// <inheritdoc/>
    public void RemoveLevelAttribute(int level, string key) => LevelStore(level).Remove(key);

    private readonly Dictionary<int, AttributeList> levels = [];

    private AttributeList LevelStore(int level)
    {
        if (!levels.TryGetValue(level, out var store))
        {
            store = new AttributeList();
            levels[level] = store;
        }
        return store;
    }

    /// <inheritdoc/>
    /// <remarks>A quest the design does not define reads stage 0, as an absent one would.</remarks>
    public int QuestStage(string quest) =>
        questIds.TryGetValue(quest, out int id) ? game.World?.QuestStageOf(id) ?? 0 : 0;

    /// <inheritdoc/>
    public void SetQuestStage(string quest, int stage)
    {
        if (questIds.TryGetValue(quest, out int id))
        {
            game.World?.SetQuestStage(id, stage);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>CHARACTER::getItemList</c> (<c>Char.cpp:10075</c>): <c>{name[index]}</c> per item, with
    /// the index being the character's own position — so a design can tell whose item it found.
    /// </remarks>
    public string ItemList()
    {
        if (game.Party is not { } party)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        for (int i = 0; i < party.Count; i++)
        {
            foreach (var item in party.Members[i].Record.Items.Items)
            {
                text.Append('{').Append(item.ItemId).Append('[')
                    .Append(i.ToString(CultureInfo.InvariantCulture)).Append("]}");
            }
        }
        return text.ToString();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>/&lt;name&gt;/&lt;index&gt;/</c> per NPC — and note the reference builds it by formatting
    /// the accumulated result back into itself, so the leading separator belongs to the first
    /// entry rather than each one.
    /// </remarks>
    public string NpcList()
    {
        if (game.Party is not { } party)
        {
            return string.Empty;
        }

        string result = string.Empty;
        for (int i = 0; i < party.Count; i++)
        {
            if (party.Members[i].Record.Type == EventNpc.NpcType)
            {
                result = $"{result}/{party.Members[i].Name}/{i.ToString(CultureInfo.InvariantCulture)}/";
            }
        }
        return result;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>CHARACTER::getInfo</c> (<c>Char.cpp:10008</c>) wrapped in slashes, for player characters
    /// only. The reference's field list is nineteen semicolon-separated pairs; the ones this port
    /// cannot supply — the adjusted forms that need spell effects and the race/class/alignment
    /// names that need their databases — are written from the raw record instead, which is what a
    /// character with no effects on it would give anyway.
    /// </remarks>
    public string CharInfo()
    {
        if (game.Party is not { } party)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        for (int i = 0; i < party.Count; i++)
        {
            var member = party.Members[i];
            if (member.Record.Type == EventNpc.NpcType)
            {
                continue;
            }

            var record = member.Record;
            text.Append("/name=").Append(record.Name)
                .Append(";pos=").Append(record.UniquePartyId)
                .Append(";THAC0=").Append(record.Thac0)
                .Append(";AC=").Append(record.ArmorClass)
                .Append(";age=").Append(record.Age)
                .Append(";maxage=").Append(record.MaxAge)
                .Append(";HP=").Append(record.HitPoints)
                .Append(";maxHP=").Append(record.MaxHitPoints)
                .Append(";enc=").Append(record.Encumbrance)
                .Append(";maxenc=").Append(record.MaxEncumbrance)
                .Append(";maxmove=").Append(record.MaxMovement)
                .Append(";STR=").Append(record.Abilities.Strength)
                .Append(";INT=").Append(record.Abilities.Intelligence)
                .Append(";WIS=").Append(record.Abilities.Wisdom)
                .Append(";DEX=").Append(record.Abilities.Dexterity)
                .Append(";CON=").Append(record.Abilities.Constitution)
                .Append(";CHA=").Append(record.Abilities.Charisma)
                .Append(";race=").Append(record.Race)
                .Append(";gender=").Append(member.Gender == Gender.Male ? "male" : "female")
                .Append(";class=").Append(record.ClassId)
                .Append(";align=").Append(record.Alignment)
                .Append(";/");
        }
        return text.ToString();
    }

    /// <inheritdoc/>
    public string GrepCapture(int group) =>
        group >= 0 && group < GrepCaptures.Count ? GrepCaptures[group] : string.Empty;

    /// <inheritdoc/>
    public void SetIconIndex(int character, int iconIndex)
    {
        // Nothing consumes a character's icon index yet -- the party roster draws from the
        // record's own -- so this is recorded rather than applied, and the roster will read it
        // when it learns to.
        IconIndexes[character] = iconIndex;
    }

    /// <summary>Icon indices a logic block has set, until the roster reads them.</summary>
    public Dictionary<int, int> IconIndexes { get; } = [];

    private AttributeList? Store(LogicAslScope scope, int character) => scope switch
    {
        LogicAslScope.Global or LogicAslScope.Temp => game.Globals,
        LogicAslScope.Party => game.Party?.Attributes,
        LogicAslScope.Character => game.Party is { } party &&
                                   character >= 0 && character < party.Count
            ? party.Members[character].Attributes
            : null,
        _ => null,
    };

    private static string Key(LogicAslScope scope, string key) =>
        scope == LogicAslScope.Temp ? TempPrefix + key : key;
}
