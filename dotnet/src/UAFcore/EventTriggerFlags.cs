using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Which events have already fired, and how many steps the party has taken in each zone
/// (<c>EVENT_TRIGGER_DATA</c>, <c>Party.h:394</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the state a savegame carries that the port had no live counterpart for.</b> The
/// reader has kept it since Phase 1 and nothing ever set one — which is why <c>OnceOnly</c> did
/// nothing and a once-only event re-fired every time the party stepped on it.
/// </para>
/// <para>
/// <b>An event is marked the moment its trigger test passes</b>, before <c>OnInitialEvent</c>
/// runs (<c>CProcinp.cpp:365</c>). Not when it finishes, not when the player reads it — so an
/// event escaped from, chained away from, or abandoned mid-screen has still happened. That is
/// what makes <c>OnceOnly</c> mean "offered once" rather than "completed once", and it is the
/// difference between a design that can strand a player and one that cannot.
/// </para>
/// </remarks>
public sealed class EventTriggerFlags
{
    /// <summary><c>MAX_LEVELS</c> (<c>Externs.h:905</c>).</summary>
    public const int MaxLevels = 255;

    /// <summary>
    /// The level index global events record against — <c>GLOBAL_ART</c>, which is
    /// <c>MAX_LEVELS</c> (<c>PicSlot.h:309</c>).
    /// </summary>
    /// <remarks>
    /// <b>One past the last real level</b>, so the global list shares the per-level table rather
    /// than having one of its own. <c>CheckLevel</c> grows the array on demand, so nothing has to
    /// reserve the slot in advance — and a design with a level 255 would collide with it.
    /// </remarks>
    public const int GlobalLevel = MaxLevels;

    /// <summary><c>STEP_COUNTER</c>'s zones (<c>Externs.h:858</c>).</summary>
    public const int ZoneCount = 16;

    private readonly Dictionary<int, HashSet<uint>> happened = [];
    private readonly Dictionary<int, uint[]> steps = [];

    /// <summary>Whether an event on a level has already fired.</summary>
    public bool HasHappened(int level, uint eventId) =>
        level >= 0 && happened.TryGetValue(level, out var set) && set.Contains(eventId);

    /// <summary>Records that an event fired (<c>markEventHappened</c>).</summary>
    /// <remarks>
    /// A negative level is ignored rather than throwing — <c>CheckLevel</c> returns FALSE for one
    /// and every caller silently does nothing.
    /// </remarks>
    public void MarkHappened(int level, uint eventId)
    {
        if (level < 0)
        {
            return;
        }

        if (!happened.TryGetValue(level, out var set))
        {
            happened[level] = set = [];
        }
        set.Add(eventId);
    }

    /// <summary>The party's step count in one zone of one level.</summary>
    public uint ZoneSteps(int level, int zone) =>
        level >= 0 && zone >= 0 && zone < ZoneCount && steps.TryGetValue(level, out uint[]? counts)
            ? counts[zone]
            : 0;

    /// <summary>Counts a step in a zone (<c>IncZoneStepCount</c>).</summary>
    public void IncZoneSteps(int level, int zone)
    {
        if (level < 0 || zone < 0 || zone >= ZoneCount)
        {
            return;
        }

        if (!steps.TryGetValue(level, out uint[]? counts))
        {
            steps[level] = counts = new uint[ZoneCount];
        }
        counts[zone]++;
    }

    /// <summary>Which levels have any state, in order — for projecting into a savegame.</summary>
    public IEnumerable<int> Levels => happened.Keys.Union(steps.Keys).Order();

    /// <summary>
    /// The savegame's own shape: one <see cref="LevelFlags"/> per level, dense from zero.
    /// </summary>
    /// <remarks>
    /// <b>Dense, not sparse.</b> <c>EVENT_TRIGGER_DATA</c> is a <c>CArray</c> indexed by level and
    /// <c>CheckLevel</c> grows it with empty entries, so the count written is the highest level
    /// touched plus one and every level below it gets a record whether or not anything happened
    /// there. A sparse projection would read back with the levels shifted.
    /// </remarks>
    public List<LevelFlags> ToRecords()
    {
        int highest = -1;
        foreach (int level in Levels)
        {
            highest = Math.Max(highest, level);
        }

        var records = new List<LevelFlags>(highest + 1);
        for (int level = 0; level <= highest; level++)
        {
            uint[] counts = steps.TryGetValue(level, out uint[]? found)
                ? [.. found]
                : new uint[ZoneCount];

            var results = happened.TryGetValue(level, out var set)
                ? set.Order().Select(id => new TriggerFlags(id, 0, HappenedResult)).ToList()
                : [];

            records.Add(new LevelFlags(counts, results));
        }
        return records;
    }

    /// <summary>Rebuilds the live flags from a savegame's records.</summary>
    public static EventTriggerFlags FromRecords(IReadOnlyList<LevelFlags> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var flags = new EventTriggerFlags();
        for (int level = 0; level < levels.Count; level++)
        {
            var record = levels[level];

            foreach (var result in record.EventResults)
            {
                if (Happened(result))
                {
                    flags.MarkHappened(level, result.Key);
                }
            }

            for (int zone = 0; zone < Math.Min(record.StepCounts.Length, ZoneCount); zone++)
            {
                if (record.StepCounts[zone] != 0)
                {
                    flags.steps.TryAdd(level, new uint[ZoneCount]);
                    flags.steps[level][zone] = record.StepCounts[zone];
                }
            }
        }
        return flags;
    }

    /// <summary>
    /// <c>HasHappenedAtLeastOnce</c> — the second member of <c>eventResultType</c>
    /// (<c>GameEvent.h:306</c>).
    /// </summary>
    /// <remarks>
    /// <c>TRIGGER_FLAGS</c> also carries an <c>eventStatusUnused</c> the engine never reads. It
    /// is written, so a writer has to put something back, and zero is what a cleared record holds.
    /// </remarks>
    public const int HappenedResult = 1;

    /// <summary>
    /// Whether a stored flag means the event fired.
    /// </summary>
    /// <remarks>
    /// <b>Equality, not "non-zero".</b> <c>HasEventHappened</c> tests
    /// <c>eventResult == HasHappenedAtLeastOnce</c> (<c>Party.h:359</c>), so any other value —
    /// which a hand-edited or future save could hold — reads as <i>not</i> happened. Treating the
    /// field as a flag would silently disagree with the reference on exactly those files.
    /// </remarks>
    private static bool Happened(TriggerFlags flags) => flags.Result == HappenedResult;
}
