using System.Globalization;
using System.Reflection;

namespace UAF.Serialization;

/// <summary>
/// The databases an event's control block can refer to by number below 0.998101.
/// </summary>
/// <param name="Items">For <c>itemID</c>.</param>
/// <param name="Races">For <c>raceID</c>.</param>
/// <param name="Classes">For <c>classBaseclassID.classID</c>.</param>
/// <param name="Spells">For <c>memorizedSpellID</c>.</param>
/// <remarks>
/// <b>There is no table for <c>characterID</c>, and that is why it is the one field this cannot
/// finish.</b> The reference resolves it through <c>globalData.charData</c>, the design's NPC
/// roster; the port reads past that roster rather than into it
/// (<c>LoadedDesign.ReadThroughCharacters</c>), so there is nothing to look a key up in. See
/// <see cref="EventIdUpgrade.Upgrade(EventControl, EventIdTables)"/>.
/// </remarks>
public sealed record EventIdTables(
    IReadOnlyList<ItemRecord> Items,
    IReadOnlyList<RaceRecord> Races,
    IReadOnlyList<ClassRecord> Classes,
    IReadOnlyList<SpellRecord> Spells)
{
    /// <summary>No tables at all — every key resolves to nothing.</summary>
    public static EventIdTables None { get; } = new([], [], [], []);
}

/// <summary>
/// Resolves an event's pre-0.998101 numeric database keys into modern names.
/// </summary>
/// <remarks>
/// <para>
/// Below <see cref="DesignVersion.SpellNames"/> an event's <c>EVENT_CONTROL</c> stores its item,
/// race, class, character and memorised-spell references as numeric keys
/// (<c>GameEvent.cpp:1403–1530</c>). The reader keeps them as their digits, because resolving one
/// needs the database it points at and that is not in hand while a level is being parsed — so
/// <c>"12"</c> in <c>ItemId</c> means <i>item 12</i>, not an item named <c>"12"</c>, and
/// <see cref="EventControl.LegacyIds"/> is what records the difference.
/// </para>
/// <para>
/// <b>Until this runs, a legacy level cannot be written at all.</b>
/// <see cref="GameEventWriter.CanWrite"/> refuses an event still carrying them rather than emit
/// digits into a modern file, where they would name objects that do not exist. That refusal is what
/// makes the reference offer to convert a design's level files on load.
/// </para>
/// <para>
/// <b>All five fields look alike and no two follow the same rule.</b> Writing one helper over the
/// lot was the first attempt here and it was wrong four times out of five — the guards are
/// <c>id &gt;= 0</c> for the item, <b>none at all</b> for the race, <c>&gt; 0</c> for the class and
/// the character, and for the spell a test on the event's <i>trigger</i> rather than on the key.
/// So each is written out separately below with the line it comes from.
/// </para>
/// </remarks>
public static class EventIdUpgrade
{
    /// <summary>
    /// <c>SpellMemorized</c>, the one trigger under which a memorised-spell key means anything
    /// (<c>GameEvent.h:278</c>).
    /// </summary>
    /// <remarks>
    /// The ordinal rather than <c>EventTriggerType</c>: that enum lives in <c>UAFcore</c>, which
    /// depends on this assembly rather than the other way about.
    /// </remarks>
    private const int SpellMemorizedTrigger = 28;

    /// <summary>
    /// The classic class names, in key order (<c>UAFWinEd/Globtext.cpp:466</c>).
    /// </summary>
    /// <remarks>
    /// <b>The class is the only one of the five with a fallback.</b> When no class carries the key,
    /// the reference indexes this table with it and uses the name it finds
    /// (<c>class.cpp:7187</c>) — so an event gating on <c>Fighter</c> in a design whose class
    /// database has been rewritten still says <c>Fighter</c>. It does that unguarded and would run
    /// off the end of the array on a key of 19 or more; this bounds-checks and falls back to
    /// nothing.
    /// </remarks>
    private static readonly string[] ClassText =
    [
        "Fighter", "Cleric", "Ranger", "Paladin", "Magic User", "Thief", "Druid",
        "Cleric/Fighter", "Cleric/Fighter/Magic User", "Cleric/Ranger", "Cleric/Magic User",
        "Cleric/Thief", "Fighter/Magic User", "Fighter/Thief", "Fighter/Magic User/Thief",
        "Magic User/Thief", "Fighter/Druid", "Druid/Magic User", "Fighter/Magic User/Druid",
    ];

    /// <summary>Whether this event still carries numeric keys.</summary>
    public static bool NeedsUpgrade(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        return gameEvent.Base.Control.LegacyIds;
    }

    /// <summary>
    /// The control block with its references resolved, and the legacy marker cleared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A key that names nothing becomes an empty reference, and the marker still clears.</b>
    /// That is what the reference does — it pops a message box and carries on with an empty ID —
    /// and it is the right reading: a key pointing at a record the design does not have is a broken
    /// reference in the <i>design</i>, not a conversion this port has yet to write, and refusing to
    /// save the whole level over one would strand the design in a format nothing can open.
    /// </para>
    /// <para>
    /// <b>The exception is <c>characterID</c>, where the marker stays.</b> A positive character key
    /// cannot be resolved here at all, because the port has no NPC roster to look it up in — that
    /// is a conversion still missing rather than a design defect, and the two must not be confused.
    /// Blanking the field would silently discard the reference; keeping the digits leaves
    /// <see cref="GameEventWriter.CanWrite"/> refusing the event, which is the visible failure this
    /// port prefers. Every other key resolves regardless, so the digits that remain are the whole
    /// of what is unported.
    /// </para>
    /// </remarks>
    public static EventControl Upgrade(EventControl control, EventIdTables tables)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(tables);

        if (!control.LegacyIds)
        {
            return control;
        }

        // GameEvent.cpp:1403 -- "if (id >= 0)". Zero is a real item key here, which it is not on a
        // monster's attack, where -1 is the sentinel and this port tests > 0.
        string item = Convert(control.ItemId, key => key >= 0
            ? Find(tables.Items, key, i => i.Names.PreSpellNameKey, i => i.Names.UniqueName)
            : string.Empty);

        // GameEvent.cpp:1430 -- read as DWORD and passed straight to the finder with no guard at
        // all, so every value including zero is looked up.
        string race = Convert(control.RaceId,
            key => Find(tables.Races, key, r => r.PreSpellNameKey, r => r.Name));

        // GameEvent.cpp:1444 -- "if (temp > 0)", then the ClassText fallback at class.cpp:7187.
        string cls = Convert(control.ClassOrBaseclassId, key =>
        {
            if (key <= 0)
            {
                return string.Empty;
            }

            string named = Find(tables.Classes, key, c => c.PreSpellNameKey, c => c.Name);
            return named.Length > 0 ? named : ClassicClass(key);
        });

        // GameEvent.cpp:1507 -- gated on the event's trigger, not on the key. A key sitting under
        // any other trigger is not a reference and is dropped.
        string spell = Convert(control.MemorizedSpellId, key =>
            control.EventTrigger == SpellMemorizedTrigger
                ? Find(tables.Spells, key, s => s.PreSpellNameKey, s => s.Name)
                : string.Empty);

        // GameEvent.cpp:1466 -- "if (npc > 0)" into charData, which the port does not read. The
        // digits are kept so the writer keeps refusing; see the remarks above.
        bool characterUnresolved = Key(control.CharacterId) is > 0;
        string character = characterUnresolved
            ? control.CharacterId
            : Convert(control.CharacterId, _ => string.Empty);

        return control with
        {
            ItemId = item,
            RaceId = race,
            ClassOrBaseclassId = cls,
            CharacterId = character,
            MemorizedSpellId = spell,
            LegacyIds = characterUnresolved,
        };
    }

    /// <summary>The event with its control block upgraded.</summary>
    /// <remarks>
    /// <para>
    /// <b>Rebuilt through its constructor, because there are sixty-eight of these.</b> Every event
    /// body is a positional record whose first parameter is its <see cref="GameEventBase"/>, so the
    /// replacement is mechanical — but <see cref="IGameEvent"/> exposes the base as a getter, and
    /// <c>with</c> needs the concrete type. Adding a <c>WithBase</c> to the interface would mean the
    /// same line written out once per event type and one more thing for a new event to forget.
    /// </para>
    /// <para>
    /// <b>A body that cannot be rebuilt is returned untouched</b>, still carrying its marker, so it
    /// stays unwritable rather than being silently half-converted.
    /// </para>
    /// </remarks>
    public static IGameEvent Upgrade(IGameEvent gameEvent, EventIdTables tables)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        ArgumentNullException.ThrowIfNull(tables);

        if (!NeedsUpgrade(gameEvent))
        {
            return gameEvent;
        }

        var upgraded = gameEvent.Base with
        {
            Control = Upgrade(gameEvent.Base.Control, tables),
        };

        return Rebuild(gameEvent, upgraded) ?? gameEvent;
    }

    /// <summary>Every event in a level, converted, keeping the chain and the bodies in step.</summary>
    /// <remarks>
    /// <see cref="LevelFile.Events"/> and <see cref="LevelFile.Entries"/> hold the same bodies —
    /// the first is the second with its bodyless tags dropped — so both are rebuilt from one map.
    /// Replacing only one would leave the writer emitting the originals.
    /// </remarks>
    public static LevelFile Upgrade(LevelFile level, EventIdTables tables)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(tables);

        if (!level.Events.Any(NeedsUpgrade))
        {
            return level;
        }

        var map = new Dictionary<IGameEvent, IGameEvent>(ReferenceEqualityComparer.Instance);

        foreach (var body in level.Events)
        {
            map[body] = Upgrade(body, tables);
        }

        return level with
        {
            Events = [.. level.Events.Select(e => map.TryGetValue(e, out var up) ? up : e)],
            Entries = [.. level.Entries.Select(entry =>
                entry.Body is { } body && map.TryGetValue(body, out var up)
                    ? entry with { Body = up }
                    : entry)],
        };
    }

    /// <summary>The classic class name for a key, or nothing when it names none.</summary>
    private static string ClassicClass(int key) =>
        key >= 0 && key < ClassText.Length ? ClassText[key] : string.Empty;

    /// <summary>
    /// Applies a field's rule to the key it holds, or leaves it alone when it holds none.
    /// </summary>
    /// <remarks>
    /// <b>"Not a key" and "a key naming nothing" are different answers and must not collapse into
    /// one.</b> The version gate is per design rather than per field, so a design can carry an
    /// event whose field already holds a name; parsing that as a key and blanking it loses it.
    /// Every rule below therefore runs only once there is a number to run it on.
    /// </remarks>
    private static string Convert(string value, Func<int, string> rule) =>
        Key(value) is { } key ? rule(key) : value;

    /// <summary>The number a legacy field holds, or null when it does not hold one.</summary>
    private static int? Key(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int key)
            ? key
            : null;

    /// <summary>
    /// The name of the record carrying this key, or nothing.
    /// </summary>
    /// <remarks>
    /// The shape of all four of the reference's finders (<c>Items.cpp:6463</c>,
    /// <c>class.cpp:3423</c>, <c>class.cpp:7172</c>, <c>Spell.cpp:10483</c>): a linear search for
    /// the record whose own <c>preSpellNameKey</c> matches, taking the first.
    /// </remarks>
    private static string Find<T>(IReadOnlyList<T> records, int key,
                                  Func<T, int> keyOf, Func<T, string> nameOf)
    {
        foreach (var record in records)
        {
            if (keyOf(record) == key)
            {
                return nameOf(record);
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// A copy of an event body with a different base, or null when its shape is unexpected.
    /// </summary>
    /// <remarks>
    /// Positional records name their properties after their constructor parameters, which is what
    /// makes reading each argument back off the instance safe. The first parameter must be the
    /// base; anything else is not one of these records and is left alone.
    /// </remarks>
    private static IGameEvent? Rebuild(IGameEvent gameEvent, GameEventBase updated)
    {
        var type = gameEvent.GetType();
        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                              .FirstOrDefault(c => c.GetParameters() is [{ } first, ..]
                                                   && first.ParameterType == typeof(GameEventBase));

        if (constructor is null)
        {
            return null;
        }

        var parameters = constructor.GetParameters();
        object?[] arguments = new object?[parameters.Length];
        arguments[0] = updated;

        for (int i = 1; i < parameters.Length; i++)
        {
            if (parameters[i].Name is not { } name
                || type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not { } p)
            {
                return null;
            }

            arguments[i] = p.GetValue(gameEvent);
        }

        return constructor.Invoke(arguments) as IGameEvent;
    }
}
