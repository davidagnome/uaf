namespace UAF.Serialization;

/// <summary>
/// Writes <c>COMBAT_EVENT_DATA</c> (<c>GameEvent.cpp:6947</c>) and the structures it contains —
/// the inverse of <see cref="CombatEventReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// The last event type standing between the port and a complete level: 57 occurrences across the
/// two designs that ship levels, and the one holdout in Case.dsn's 575-event Level001.
/// </para>
/// <para>
/// <b>Three lists sit outside the storing branch</b>, which is this event's whole character:
/// <c>monsters</c> after the encounter's fields (<c>:7022</c>), and inside each monster its
/// <c>items</c> after the money (<c>:4816</c>). The second is past where a grep window usually
/// stops — the same trap that hid <c>changeData</c> in <c>SPELL_EFFECTS_DATA</c>.
/// </para>
/// <para>
/// <b>The three sounds are stripped of their directories on the way out</b>, and here the
/// reference does it inside the storing branch rather than in a <c>PreSerialize</c>.
/// </para>
/// </remarks>
public static class CombatEventWriter
{
    /// <summary>
    /// Whether a combat event can be written, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// Two refusals, both of them shapes already met elsewhere. A monster read from below 0.740 has
    /// <b>no money sack at all</b> — and unlike a monster <i>record</i>, where the reference writes
    /// its default-constructed one, here the sack is simply absent from this port's model, so an
    /// empty one is the honest thing to refuse over rather than invent. And a monster carrying an
    /// item by its pre-0.998101 numeric id cannot be named — see
    /// <see cref="MonsterRecordWriter.CanWrite"/>.
    /// </remarks>
    public static bool CanWrite(CombatEvent combat, out string reason)
    {
        ArgumentNullException.ThrowIfNull(combat);

        if (!GameEventWriter.CanWrite(combat.Base, out reason))
        {
            return false;
        }

        foreach (var monster in combat.Monsters)
        {
            if (monster.Money is null)
            {
                reason = $"Combat event {combat.Base.Id} holds '{monster.MonsterId}', read from a " +
                         "design below 0.740 where a monster entry carries no MONEY_SACK. This " +
                         "port has none to write and inventing an empty one would give the " +
                         "encounter treasure it never had.";
                return false;
            }

            if (monster.Items.Items.Any(i => i.LegacyItemId != 0))
            {
                reason = $"Combat event {combat.Base.Id} holds '{monster.MonsterId}' carrying an " +
                         "item by its pre-0.998101 numeric id, which resolves against the item " +
                         "database. Writing an empty ITEM_ID would leave it holding nothing.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes one <c>COMBAT_EVENT_DATA</c>.</summary>
    /// <exception cref="NotSupportedException">
    /// When the event holds a legacy shape — see <see cref="CanWrite"/>.
    /// </exception>
    public static void Write(IArchiveWriteCursor ar, CombatEvent combat)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(combat);

        if (!CanWrite(combat, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        GameEventWriter.Write(ar, combat.Base);

        WriteDas(ar, PicDataWriter.StripFilenamePath(combat.DeathSound));
        WriteDas(ar, PicDataWriter.StripFilenamePath(combat.MoveSound));
        WriteDas(ar, PicDataWriter.StripFilenamePath(combat.TurnUndeadSound));

        ar.WriteInt32(combat.Distance);
        ar.WriteInt32(combat.Direction);
        ar.WriteInt32(combat.Surprise);
        ar.WriteInt32(combat.AutoApproach);
        ar.WriteInt32(combat.Outdoors);
        ar.WriteInt32(combat.NoMonsterTreasure);
        ar.WriteInt32(combat.PartyNeverDies);
        ar.WriteInt32(combat.NoMagic);
        ar.WriteInt32(combat.MonsterMorale);
        ar.WriteInt32(combat.TurningMod);            // eventTurnUndeadModType, not terrain
        ar.WriteInt32(combat.RandomMonster);
        ar.WriteInt32(combat.PartyNoExperience);

        WriteBackgroundSoundData(ar, combat.BackgroundSounds);

        // Outside the branch -- always written.
        WriteMonsterEventData(ar, combat.Monsters);
    }

    /// <summary>Writes a <c>BACKGROUND_SOUND_DATA</c> (<c>SoundMgr.cpp:662</c>).</summary>
    /// <remarks>
    /// <b>Two sound queues, then three scalars</b> — not one queue.
    /// <c>BACKGROUND_SOUND_DATA</c> and <c>BACKGROUND_SOUNDS</c> differ by a single word and the
    /// member is called <c>bgSounds</c> in both places; writing the bare list costs 16 bytes and
    /// the reader takes the monster count out of the middle of it.
    /// </remarks>
    public static void WriteBackgroundSoundData(IArchiveWriteCursor ar, BackgroundSoundData sounds)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(sounds);

        WriteBackgroundSounds(ar, sounds.Day);
        WriteBackgroundSounds(ar, sounds.Night);

        ar.WriteInt32(sounds.UseNightMusic);
        ar.WriteInt32(sounds.EndTime);
        ar.WriteInt32(sounds.StartTime);
    }

    /// <summary>Writes a <c>BACKGROUND_SOUNDS</c> (<c>SoundMgr.cpp:491</c>): a count then names.</summary>
    public static void WriteBackgroundSounds(IArchiveWriteCursor ar, IReadOnlyList<string> sounds)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(sounds);

        ar.WriteInt32(sounds.Count);
        foreach (string sound in sounds)
        {
            WriteDas(ar, sound);
        }
    }

    /// <summary>Writes a <c>MONSTER_EVENT_DATA</c> (<c>GameEvent.cpp:4880</c>).</summary>
    public static void WriteMonsterEventData(IArchiveWriteCursor ar,
                                             IReadOnlyList<MonsterEvent> monsters)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(monsters);

        ar.WriteInt32(monsters.Count);
        foreach (var monster in monsters)
        {
            WriteMonsterEvent(ar, monster);
        }
    }

    /// <summary>Writes one <c>MONSTER_EVENT</c> (<c>GameEvent.cpp:4748</c>).</summary>
    /// <remarks>
    /// <b><c>characterID</c> is written unconditionally</b> where the reader admits it only above
    /// 0.9984016 — a bare-literal gate sitting between <c>VersionSpellIDs</c> and
    /// <c>VersionSpellNames</c>, and one of the tightest in the format. The item list at the end is
    /// outside the storing branch.
    /// </remarks>
    public static void WriteMonsterEvent(IArchiveWriteCursor ar, MonsterEvent monster)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(monster);

        ar.WriteInt32(monster.Quantity);
        ar.WriteInt32(monster.Type);
        ar.WriteString(monster.MonsterId);           // verbatim: a MONSTER_ID
        ar.WriteString(monster.CharacterId);
        ar.WriteInt32(monster.Friendly);
        ar.WriteInt32(monster.MoraleAdjustment);

        ar.WriteInt32(monster.QtyDiceSides);
        ar.WriteInt32(monster.QtyDiceQty);
        ar.WriteInt32(monster.QtyBonus);
        ar.WriteInt32(monster.UseQty);

        MonsterLeafWriters.WriteMoneySack(ar, monster.Money!);

        // Outside the branch, and easy to miss: it sits after the closing brace of the else.
        MonsterLeafWriters.WriteItemList(ar, monster.Items);
    }

    private static void WriteDas(IArchiveWriteCursor ar, string value) =>
        ar.WriteString(ArchiveStringConventions.Encode(value));
}
