using UAF.Common;

namespace UAF.Serialization;

/// <summary>One monster entry in a combat encounter (<c>GameEvent.cpp:4748</c>).</summary>
public sealed record MonsterEvent(
    int Quantity, int Type, string MonsterId, string CharacterId,
    int Friendly, int MoraleAdjustment,
    int QtyDiceSides, int QtyDiceQty, int QtyBonus, int UseQty,
    MoneySack? Money, ItemList Items);

/// <summary>
/// A <c>COMBAT_EVENT_DATA</c> — the shared event base plus the encounter's own fields.
/// </summary>
public sealed record CombatEvent(
    GameEventBase Base,
    string DeathSound, string MoveSound, string TurnUndeadSound,
    int Distance, int Direction, int Surprise, int AutoApproach,
    int Outdoors, int NoMonsterTreasure, int PartyNeverDies, int NoMagic,
    int MonsterMorale, int Terrain, int RandomMonster, int PartyNoExperience,
    BackgroundSoundData BackgroundSounds,
    IReadOnlyList<MonsterEvent> Monsters);

/// <summary>
/// A <c>BACKGROUND_SOUND_DATA</c> — day and night sound queues plus the hours that switch between
/// them (<c>SoundMgr.cpp:662</c>).
/// </summary>
/// <remarks>
/// <b>Not the same type as <c>BACKGROUND_SOUNDS</c></b>, which is one bare list. This wraps
/// <i>two</i> of those and adds three scalars. The names differ by a single word, and the member
/// is called <c>bgSounds</c> in both places that use them.
/// </remarks>
public sealed record BackgroundSoundData(
    IReadOnlyList<string> Day, IReadOnlyList<string> Night,
    int UseNightMusic, int EndTime, int StartTime);

/// <summary>
/// Reads <c>COMBAT_EVENT_DATA</c> (<c>GameEvent.cpp:6947</c>) and the structures it contains.
/// </summary>
/// <remarks>
/// <para>
/// The first concrete event subclass ported. Its shape is representative: the shared
/// <see cref="GameEventReader"/> base, then a version-gated field list, then a nested collection.
/// </para>
/// <para>
/// <b><c>monsters</c> is read outside the storing/loading branch</b> (<c>GameEvent.cpp:7022</c>),
/// and inside <c>MONSTER_EVENT_DATA</c> the per-element loop is outside its branch too. That is the
/// same trap that hid <c>changeData</c> in <c>SPELL_EFFECTS_DATA</c> — always read past the closing
/// brace.
/// </para>
/// <para>
/// Every gate here is on the global <c>LoadingVersion</c> rather than the <c>version</c> parameter.
/// </para>
/// </remarks>
public static class CombatEventReader
{
    public static CombatEvent Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        string deathSound = ReadDas(ar);
        string moveSound = ReadDas(ar);

        // Defaulted rather than read below 0.750.
        string turnUndeadSound = version >= DesignVersion.V0750 ? ReadDas(ar) : string.Empty;

        int distance = ar.ReadInt32();
        int direction = ar.ReadInt32();
        int surprise = ar.ReadInt32();
        int autoApproach = ar.ReadInt32();
        int outdoors = ar.ReadInt32();
        int noMonsterTreasure = ar.ReadInt32();
        int partyNeverDies = ar.ReadInt32();
        int noMagic = ar.ReadInt32();
        int monsterMorale = ar.ReadInt32();
        int terrain = ar.ReadInt32();

        int randomMonster = version >= DesignVersion.V0690 ? ar.ReadInt32() : 0;
        int partyNoExperience = version >= DesignVersion.V0860 ? ar.ReadInt32() : 0;

        var backgroundSounds = version >= DesignVersion.V0790
            ? ReadBackgroundSoundData(ar)
            : new BackgroundSoundData([], [], 0, 0, 0);

        // Outside the branch -- always read.
        var monsters = ReadMonsterEventData(ar, version, role);

        return new CombatEvent(
            baseEvent, deathSound, moveSound, turnUndeadSound,
            distance, direction, surprise, autoApproach,
            outdoors, noMonsterTreasure, partyNeverDies, noMagic,
            monsterMorale, terrain, randomMonster, partyNoExperience,
            backgroundSounds, monsters);
    }

    /// <summary>
    /// Reads a <c>BACKGROUND_SOUND_DATA</c> (<c>SoundMgr.cpp:662</c>): two sound queues then
    /// three scalars.
    /// </summary>
    /// <remarks>
    /// The nested lists make this look like a single list at a glance, which is exactly how it was
    /// first mis-ported as a bare <c>BACKGROUND_SOUNDS</c> — costing 16 bytes per combat event and
    /// swallowing the monster count that follows.
    /// </remarks>
    public static BackgroundSoundData ReadBackgroundSoundData(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var day = ReadBackgroundSounds(ar);
        var night = ReadBackgroundSounds(ar);

        return new BackgroundSoundData(
            day, night,
            ar.ReadInt32(),                              // UseNightMusic -- BOOL
            ar.ReadInt32(),                              // EndTime, in game minutes
            ar.ReadInt32());                             // StartTime
    }

    /// <summary>
    /// Reads a <c>BACKGROUND_SOUNDS</c> (<c>SoundMgr.cpp:491</c>): a count then that many names.
    /// </summary>
    public static List<string> ReadBackgroundSounds(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var sounds = new List<string>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            sounds.Add(ReadDas(ar));
        }
        return sounds;
    }

    /// <summary>
    /// Reads a <c>MONSTER_EVENT_DATA</c> (<c>GameEvent.cpp:4880</c>): a count, then the entries.
    /// </summary>
    /// <remarks>
    /// The element loop sits outside the storing/loading branch, so only the count is inside it.
    /// </remarks>
    public static List<MonsterEvent> ReadMonsterEventData(IArchiveCursor ar, DesignVersion version,
                                                          ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var monsters = new List<MonsterEvent>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            monsters.Add(ReadMonsterEvent(ar, version, role));
        }
        return monsters;
    }

    /// <summary>
    /// Reads one <c>MONSTER_EVENT</c> (<c>GameEvent.cpp:4748</c>).
    /// </summary>
    /// <remarks>
    /// Ends with a <c>MONEY_SACK</c> and then an <c>ITEM_LIST</c> — the monster's carried
    /// treasure and inventory. The item list is outside the storing/loading branch.
    /// <para>
    /// This only bites on encounters that actually define monsters. DefaultDesign's single combat
    /// event has none, so the omission was invisible until a level with populated encounters.
    /// </para>
    /// </remarks>
    public static MonsterEvent ReadMonsterEvent(IArchiveCursor ar, DesignVersion version,
                                                ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int quantity = ar.ReadInt32();
        int type = ar.ReadInt32();

        bool legacyIds = role == ArchiveRole.Editor && version < DesignVersion.SpellNames;
        string monsterId;
        string characterId = string.Empty;
        if (legacyIds)
        {
            int key = ar.ReadInt32();
            monsterId = key <= 0 ? string.Empty : key.ToString();
        }
        else
        {
            monsterId = ar.ReadString();

            // Another bare-literal gate, and a very tight one -- 0.9984016 sits between
            // VersionSpellIDs and VersionSpellNames.
            if (version.Value > 0.9984016)
            {
                characterId = ar.ReadString();
            }
        }

        int friendly = ar.ReadInt32();
        int moraleAdjustment = version >= DesignVersion.V0690 ? ar.ReadInt32() : 0;

        int qtyDiceSides = 0;
        int qtyDiceQty = 0;
        int qtyBonus = 0;
        int useQty = 0;
        if (version >= DesignVersion.V0910)
        {
            qtyDiceSides = ar.ReadInt32();
            qtyDiceQty = ar.ReadInt32();
            qtyBonus = ar.ReadInt32();
            useQty = ar.ReadInt32();
        }

        MoneySack? money = version >= DesignVersion.V0740
            ? MonsterLeafReaders.ReadMoneySack(ar, version)
            : null;

        // Outside the storing/loading branch (GameEvent.cpp:4816), and ungated. Easy to miss:
        // it sits after the closing brace of the else, past where a grep window usually stops.
        var items = MonsterLeafReaders.ReadItemList(ar, version, role);

        return new MonsterEvent(quantity, type, monsterId, characterId, friendly,
                                moraleAdjustment, qtyDiceSides, qtyDiceQty, qtyBonus, useQty,
                                money, items);
    }

    private static string ReadDas(IArchiveCursor ar) =>
        ArchiveStringConventions.Decode(ar.ReadString());
}
