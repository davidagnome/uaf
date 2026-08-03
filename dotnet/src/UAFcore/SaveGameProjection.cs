using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Turning a game in progress into the <c>.pty</c> record the writer takes, and back.
/// </summary>
/// <remarks>
/// <para>
/// Both ends of the file were finished in Phase 1 — a savegame read from disk writes back byte
/// for byte. This is the piece between: the live party, world, flags, squares, clearances and
/// vaults assembled into a <see cref="SaveGame"/>, and one applied back over a running game.
/// </para>
/// <para>
/// <b>A field the engine does not model is carried through, not zeroed.</b> Every
/// <see cref="Character"/> wraps the <see cref="CharacterRecord"/> it was built from, so writing
/// one back means overwriting the handful of fields play can change and keeping the rest exactly
/// as they were read. The same principle would apply to <see cref="PartyState"/> if it had a
/// record behind it; it does not, which is why the divergences listed on
/// <see cref="From"/> are all there.
/// </para>
/// </remarks>
public static class SaveGameProjection
{
    /// <summary>
    /// State a savegame carries that this engine still does not keep.
    /// </summary>
    /// <remarks>
    /// <b>Empty.</b> Kept as data rather than deleted so the four things that came off it stay
    /// visible — visited squares, event trigger flags, blockages and vaults — and so a future gap
    /// has an obvious place to be declared rather than being found in a diff. The journal was on
    /// this list too and should never have been: it was tracked all along.
    /// </remarks>
    public static readonly string[] Untracked = [];

    /// <summary>Whether a game in progress can be written out, and what stops it.</summary>
    public static bool CanSave(Game game, out string reason)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (Untracked.Length > 0)
        {
            reason = "This port cannot save yet: it does not track " +
                     string.Join(", ", Untracked) + ".";
            return false;
        }

        return SaveGameWriter.CanWrite(From(game), out reason);
    }

    /// <summary>
    /// Assembles the savegame record for a game in progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The event stack is not saved.</b> The reference records it, so a game saved inside a
    /// shop resumes inside the shop. This writes an empty <c>TaskStack</c>, so a loaded game
    /// resumes standing on the square with nothing on screen. A real divergence, and a deliberate
    /// one: the SAVE entry is reachable only from the party menu, which is reachable only from a
    /// training hall event, so refusing to save inside an event would mean never saving at all.
    /// </para>
    /// <para>
    /// <b>The tail is written empty, and empty is correct.</b> Its seven <c>Save</c>/<c>Restore</c>
    /// pairs carry the attributes gameplay <i>changed</i>, with the design supplying the rest on
    /// load — so a port that has not yet mutated a spell's or a monster's attributes writes
    /// nothing there and loses nothing. The one place where "not ported" and "correct" coincide,
    /// and it holds only until something starts changing those lists.
    /// </para>
    /// </remarks>
    public static SaveGame From(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return new SaveGame(
            SaveGameWriter.WrittenVersion,
            PartyOf(game),
            game.TriggerFlags.ToRecords(),
            game.Visited.ToRecords(),
            game.Clearances.ToRecords(),
            [.. game.Party.Members.Select(RecordFor)],
            game.Party.Pooled.ToRecord(),
            game.Party.Journal,
            [.. game.Party.Attributes.Saveable],
            game.World.QuestRecords(),
            game.World.SpecialItemRecords(),
            game.World.KeyRecords(),
            game.Vaults.ToRecords(),
            EmptyTail,
            Body: null!);
    }

    /// <summary><c>LEVEL_STATS_VERSION</c> (<c>GlobalData.h:596</c>).</summary>
    /// <remarks>
    /// The reference stamps this into every level it saves, whatever is in the level — so an
    /// untouched level still declares version 2 and still writes both trailing tables, empty.
    /// Writing 0 instead would be a smaller file the reference reads as a much older save.
    /// </remarks>
    private const int LevelStatsVersion = 2;

    /// <summary>
    /// Nothing after the vaults — see <see cref="From"/> for why that is not a loss.
    /// </summary>
    /// <remarks>
    /// <b>"Empty" is not the same as "absent" in three places here.</b> The level list is exactly
    /// 255 entries whatever the design holds, a <c>READY_ITEMS</c> is exactly 12 slots, and a
    /// <c>MONEY_SACK</c> is exactly 10 coins — all three are compile-time counts the reference
    /// never writes, so a short list silently truncates the record. The writer refuses rather
    /// than allowing it, which is how each of these was caught here rather than in a file.
    /// </remarks>
    private static SaveGameTail EmptyTail { get; } = new(
        ActiveSpells: [], Spells: [], GlobalAttributes: [],
        CombatTreasure: new CombatTreasureData(
            new ItemList([], ReadyItems.Empty),
            new MoneySack(new int[MonsterLeafReaders.MaxCoinTypes], [], [])),
        Levels:
        [
            .. Enumerable.Repeat(
                new SavedLevelStats([], LevelStatsVersion,
                                    new WallOverrides([]), new CellLevelContents([])),
                EventTriggerFlags.MaxLevels),
        ],
        Keys: [], SpecialItems: [], Items: [], Monsters: []);

    /// <summary>
    /// The <c>PARTY</c> scalars.
    /// </summary>
    /// <remarks>
    /// <b>Six fields have no live counterpart and go out as zero</b> — the party's name, its
    /// movement speed, the selected inventory item, the two trade slots and the difficulty
    /// (<c>skillLevel</c>). None is read by anything this port runs, but all six are read by the
    /// reference, so a save written here and loaded there arrives on the lowest difficulty with an
    /// unnamed party. Listing them is cheaper than pretending the projection is total.
    /// </remarks>
    private static PartyState PartyOf(Game game)
    {
        int total = game.Minutes;

        return new PartyState(
            TaskStack: [],
            Days: 1 + (total / 1440),
            Hours: (total / 60) % 24,
            Minutes: total % 60,
            DrinkPoints: 0,
            Name: string.Empty,
            Adventuring: 1,
            AreaView: 0,
            Searching: game.Party.Searching ? 1 : 0,
            Level: (byte)game.LevelIndex,
            Speed: 0,
            PosX: game.X,
            PosY: game.Y,
            PrevPosX: game.X,
            PrevPosY: game.Y,
            Facing: (byte)game.Facing,
            ActiveCharacter: (byte)game.Party.ActiveCharacter,
            ActiveItem: 0,
            TradeItem: 0,
            TradeGiver: 0,
            TradeQuantity: 0,
            SkillLevel: 0,
            CharacterCount: (byte)game.Party.Count,
            MoneyPooled: game.Party.MoneyPooled);
    }

    /// <summary>The total minutes a saved clock stands for — the inverse of the split above.</summary>
    public static int MinutesOf(PartyState party)
    {
        ArgumentNullException.ThrowIfNull(party);
        return (Math.Max(party.Days - 1, 0) * 1440) + (party.Hours * 60) + party.Minutes;
    }

    /// <summary>
    /// One party member as a record: the fields play can change, over the one it was read from.
    /// </summary>
    /// <remarks>
    /// <b>Everything not named here keeps the record's own value.</b> That is what makes a save
    /// from this port loadable by the reference at all — the alignment, the ability scores, the
    /// icon, the spell book and forty other fields survive because nothing touched them, not
    /// because anything projected them.
    /// </remarks>
    private static CharacterRecord RecordFor(Character member) =>
        member.Record with
        {
            HitPoints = member.HitPoints,
            MaxHitPoints = member.MaxHitPoints,
            Status = (int)member.Status,
            Morale = member.Morale,
            Money = member.Purse.ToRecord(),
            Items = new ItemList([.. member.Items], member.Record.Items.Ready),
            Attributes = [.. member.Attributes.Saveable],
            BaseclassStats = [.. member.Baseclasses.Select(b => StatsFor(member, b))],
        };

    /// <summary>
    /// One baseclass's stats, keeping the record's <c>PreDrainLevel</c>.
    /// </summary>
    /// <remarks>
    /// The engine owns three of the five fields. <c>PreDrainLevel</c> is the fourth and belongs to
    /// level drain, which is not ported — so it is read off the original rather than defaulted,
    /// which would restore a drained character to full on the first save.
    /// </remarks>
    private static BaseclassStats StatsFor(Character member, BaseclassProgress progress) =>
        member.Record.BaseclassStats.FirstOrDefault(s => s.BaseclassId == progress.BaseclassId)
            is { } original
            ? original with
            {
                CurrentLevel = progress.CurrentLevel,
                PreviousLevel = progress.PreviousLevel,
                Experience = progress.Experience,
            }
            : new BaseclassStats(progress.BaseclassId, progress.CurrentLevel,
                                 progress.PreviousLevel, 0, progress.Experience);
}
