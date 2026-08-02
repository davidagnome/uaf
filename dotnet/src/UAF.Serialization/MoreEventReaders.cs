using UAF.Common;

namespace UAF.Serialization;

/// <summary>A <c>SOUND_EVENT</c> — plays a queue of sounds.</summary>
public sealed record SoundEvent(GameEventBase Base, IReadOnlyList<string> Sounds) : IGameEvent;

/// <summary>A <c>GAIN_EXP_DATA</c> — awards experience.</summary>
public sealed record GainExperienceEvent(
    GameEventBase Base, int Experience, string Sound, int Chance, int Who) : IGameEvent;

/// <summary>A <c>FLOW_CONTROL_EVENT_DATA</c> — named markers and global-variable actions.</summary>
public sealed record FlowControlEvent(
    GameEventBase Base, int Version,
    string EntryMarker, string ExitMarker, string DestinationMarker,
    string GlobalVariableName, string Value,
    uint DestinationId, int ValueModification, int ActionCondition, int Action, uint Flags) : IGameEvent;

/// <summary>A <c>WHO_PAYS_EVENT_DATA</c> — a toll, with success and failure transfers.</summary>
public sealed record WhoPaysEvent(
    GameEventBase Base, int Impossible, int Gems, int Jewels, int Platinum,
    uint SuccessChain, int SuccessAction, int FailAction, uint FailChain, int MoneyType,
    TransferData SuccessTransfer, TransferData FailTransfer) : IGameEvent;

/// <summary>A <c>REMOVE_NPC_DATA</c> — removes an NPC from the party.</summary>
public sealed record RemoveNpcEvent(GameEventBase Base, int Distance, string CharacterId) : IGameEvent;

/// <summary>A <c>JOURNAL_EVENT</c> — adds a numbered entry to the party's journal.</summary>
public sealed record JournalEvent(GameEventBase Base, int Entry) : IGameEvent;

/// <summary>A <c>PLAY_MOVIE_DATA</c> — plays a cutscene.</summary>
public sealed record PlayMovieEvent(GameEventBase Base, string FileName, int Mode) : IGameEvent;

/// <summary>A <c>VAULT_EVENT_DATA</c> — opens one of the party's storage vaults.</summary>
public sealed record VaultEvent(GameEventBase Base, int ForceBackup, byte WhichVault) : IGameEvent;

/// <summary>
/// A <c>SMALL_TOWN_DATA</c> — a hub that chains to one of six town services.
/// </summary>
public sealed record SmallTownEvent(
    GameEventBase Base, int Unused, uint TempleChain, uint TrainingHallChain, uint ShopChain,
    uint InnChain, uint TavernChain, uint VaultChain) : IGameEvent;

/// <summary>One tale a tavern can tell, with its own flags and attribute list.</summary>
public sealed record TavernTale(string Text, uint Flags, IReadOnlyList<AslEntry> Attributes);

/// <summary>A <c>TAVERN_TALES</c> — the rumours told in a tavern.</summary>
public sealed record TavernTalesEvent(
    GameEventBase Base, uint Flags, IReadOnlyList<TavernTale> Tales,
    IReadOnlyList<AslEntry> Attributes) : IGameEvent;

/// <summary>A <c>CAMP_EVENT_DATA</c> — lets the party rest.</summary>
public sealed record CampEvent(GameEventBase Base, int ForceExit) : IGameEvent;

/// <summary>One baseclass a training hall will train, and the levels it covers.</summary>
public sealed record TrainableBaseclass(
    string BaseclassId, int MinLevel, int MaxLevel, string Notes);

/// <summary>A <c>TRAININGHALL</c> — levels up characters of listed baseclasses.</summary>
public sealed record TrainingHallEvent(
    GameEventBase Base, int ForceExit, IReadOnlyList<TrainableBaseclass> Trainable, int Cost) : IGameEvent;

/// <summary>A <c>SHOP</c> — buys and sells items, with optional identify/appraise services.</summary>
public sealed record ShopEvent(
    GameEventBase Base, int ForceExit, int CostFactor, int CostToIdentify,
    int BuybackPercentage, int CanIdentify, int CanAppraiseGems, int CanAppraiseJewels,
    int BuyItemsSoldOnly, ItemList ItemsAvailable) : IGameEvent;

/// <summary>One spell in a spell book.</summary>
public sealed record CharacterSpell(string SpellId, int Memorized, int Level, int Selected);

/// <summary>A <c>spellBookType</c> — casting limits plus the spells known.</summary>
public sealed record SpellBook(int UseLimits, IReadOnlyList<CharacterSpell> Spells);

/// <summary>A <c>TEMPLE</c> — healing services and donations.</summary>
public sealed record TempleEvent(
    GameEventBase Base, int ForceExit, int AllowDonations, int CostFactor, int MaxLevel,
    int DonationTrigger, uint DonationChain, SpellBook TempleSpells, int TotalDonation) : IGameEvent;

/// <summary>One tavern tale, and how many times it has been told.</summary>
public sealed record Tale(string Text, int Count);

/// <summary>One drink a tavern serves.</summary>
public sealed record Drink(string Name, int Points);

/// <summary>A <c>TAVERN</c> — tales, drinks and brawls.</summary>
public sealed record TavernEvent(
    GameEventBase Base, int ForceExit, int Inflation, int Barkeep,
    int AllowFights, int AllowDrinks, uint FightChain, uint DrinkChain,
    int DrinkPointTrigger, int TaleOrder, int EachTaleOnceOnly,
    IReadOnlyList<Tale> Tales, IReadOnlyList<Drink> Drinks) : IGameEvent;

/// <summary>An <c>NPC_SAYS_DATA</c> — an NPC speaks to the party.</summary>
public sealed record NpcSaysEvent(
    GameEventBase Base, string CharacterId, int Distance, string Sound,
    int MustHitReturn, int Highlight) : IGameEvent;

/// <summary>Further event subclasses.</summary>
public static class MoreEventReaders
{
    /// <summary>Reads an <c>NPC_SAYS_DATA</c>.</summary>
    public static NpcSaysEvent ReadNpcSays(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        string characterId;
        if (role == ArchiveRole.Editor && version < DesignVersion.SpellNames)
        {
            int key = ar.ReadInt32();
            characterId = key < 0 ? string.Empty : key.ToString();
        }
        else
        {
            characterId = ar.ReadString();
        }

        int distance = ar.ReadInt32();
        string sound = ArchiveStringConventions.Decode(ar.ReadString());
        int mustHitReturn = ar.ReadInt32();
        int highlight = ar.ReadInt32();

        return new NpcSaysEvent(baseEvent, characterId, distance, sound, mustHitReturn, highlight);
    }

    /// <summary>Drinks are a fixed-size array (<c>GameEvent.h:342</c>).</summary>
    public const int MaxDrinks = 5;

    /// <summary>Tales below 0.910 were a fixed ten; above it, a counted list capped at 255.</summary>
    public const int LegacyTaleCount = 10;

    /// <summary>
    /// Reads a <c>TAVERN</c> (<c>GameEvent.cpp:9632</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tale list changes shape at 0.910: below it exactly ten tales are written with no count,
    /// above it a count precedes them. Both the count and the per-tale <c>m_count</c> field arrive
    /// at that same version, so an older tavern is ten bare strings.
    /// </para>
    /// <para>
    /// Drinks are always <see cref="MaxDrinks"/> entries and are read outside the storing/loading
    /// branch.
    /// </para>
    /// </remarks>
    public static TavernEvent ReadTavern(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int forceExit = ar.ReadInt32();
        int inflation = ar.ReadInt32();
        int barkeep = ar.ReadInt32();
        int allowFights = ar.ReadInt32();
        int allowDrinks = ar.ReadInt32();
        uint fightChain = ar.ReadUInt32();
        uint drinkChain = ar.ReadUInt32();
        int drinkPointTrigger = ar.ReadInt32();
        int taleOrder = ar.ReadInt32();

        bool modernTales = version >= DesignVersion.V0910;
        int eachTaleOnceOnly = modernTales ? ar.ReadInt32() : 0;

        int taleCount = modernTales ? ar.ReadInt32() : LegacyTaleCount;
        var tales = new List<Tale>(Math.Max(taleCount, 0));
        for (int i = 0; i < taleCount; i++)
        {
            string text = ArchiveStringConventions.Decode(ar.ReadString());
            tales.Add(new Tale(text, modernTales ? ar.ReadInt32() : 0));
        }

        var drinks = new List<Drink>(MaxDrinks);
        for (int i = 0; i < MaxDrinks; i++)
        {
            drinks.Add(new Drink(
                ArchiveStringConventions.Decode(ar.ReadString()),
                ar.ReadInt32()));
        }

        return new TavernEvent(baseEvent, forceExit, inflation, barkeep, allowFights, allowDrinks,
                               fightChain, drinkChain, drinkPointTrigger, taleOrder,
                               eachTaleOnceOnly, tales, drinks);
    }

    /// <summary>
    /// Reads a <c>spellBookType</c> (<c>Spell.cpp:2325</c>) — spell limits then the spell list.
    /// </summary>
    /// <remarks>
    /// <c>spellLimitsType</c> collapses to a single <c>BOOL</c> at and above
    /// <c>VersionSpellNames</c> (<c>GameRules.cpp:3664</c>). Below that it is a per-baseclass
    /// matrix of <c>BYTE</c>s, which is not ported — no fixture reaches it.
    /// </remarks>
    public static SpellBook ReadSpellBook(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        if (version < DesignVersion.SpellNames)
        {
            throw new NotSupportedException(
                $"spellLimitsType below {DesignVersion.SpellNames} (this is {version}) is a " +
                "per-baseclass BYTE matrix (GameRules.cpp:3614). Not ported: no fixture reaches it.");
        }

        int useLimits = ar.ReadInt32();

        int count = ar.ReadInt32();
        var spells = new List<CharacterSpell>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            // SPELL_ID is a string above VersionSpellNames, a numeric key below it.
            string spellId = ar.ReadString();
            spells.Add(new CharacterSpell(spellId, ar.ReadInt32(), ar.ReadInt32(), ar.ReadInt32()));
        }

        return new SpellBook(useLimits, spells);
    }

    /// <summary>
    /// Reads a <c>TEMPLE</c> (<c>GameEvent.cpp:9948</c>).
    /// </summary>
    /// <remarks>
    /// <c>templeSpells</c> is declared as a <c>spellBookType&amp;</c> — a <b>reference</b> member,
    /// unusual in this codebase — but it serializes inline like any other.
    /// </remarks>
    public static TempleEvent ReadTemple(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int forceExit = ar.ReadInt32();
        int allowDonations = ar.ReadInt32();
        int costFactor = ar.ReadInt32();
        int maxLevel = ar.ReadInt32();
        int donationTrigger = ar.ReadInt32();
        uint donationChain = ar.ReadUInt32();

        var templeSpells = ReadSpellBook(ar, version, role);

        int totalDonation = ar.ReadInt32();

        return new TempleEvent(baseEvent, forceExit, allowDonations, costFactor, maxLevel,
                               donationTrigger, donationChain, templeSpells, totalDonation);
    }

    /// <summary>
    /// Reads a <c>SHOP</c> (<c>GameEvent.cpp:10586</c>).
    /// </summary>
    /// <remarks>
    /// Three separate version gates add fields over time (0.696, 0.740, 0.910), and the item
    /// inventory is read outside the storing/loading branch, so it is present at every version.
    /// </remarks>
    public static ShopEvent ReadShop(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int forceExit = ar.ReadInt32();
        int costFactor = ar.ReadInt32();

        int costToIdentify = 0, buybackPercentage = 0;
        if (version >= DesignVersion.V0696)
        {
            costToIdentify = ar.ReadInt32();
            buybackPercentage = ar.ReadInt32();
        }

        int canIdentify = 0, canAppraiseGems = 0, canAppraiseJewels = 0;
        if (version >= DesignVersion.V0740)
        {
            canIdentify = ar.ReadInt32();
            canAppraiseGems = ar.ReadInt32();
            canAppraiseJewels = ar.ReadInt32();
        }

        int buyItemsSoldOnly = version >= DesignVersion.V0910 ? ar.ReadInt32() : 0;

        var itemsAvailable = MonsterLeafReaders.ReadItemList(ar, version, role);

        return new ShopEvent(baseEvent, forceExit, costFactor, costToIdentify, buybackPercentage,
                             canIdentify, canAppraiseGems, canAppraiseJewels, buyItemsSoldOnly,
                             itemsAvailable);
    }

    /// <summary>
    /// Reads a <c>TRAININGHALL</c> (<c>GameEvent.cpp:10386</c>).
    /// </summary>
    /// <remarks>
    /// The trainable-baseclass list only exists above 0.9984 — another bare-literal gate. Below
    /// that the hall trained a fixed set of seven classes, recorded in commented-out fields.
    /// </remarks>
    public static TrainingHallEvent ReadTrainingHall(IArchiveCursor ar, DesignVersion version,
                                                     ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);
        int forceExit = ar.ReadInt32();

        var trainable = new List<TrainableBaseclass>();
        if (version.Value > 0.9984)
        {
            int count = ar.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                trainable.Add(new TrainableBaseclass(
                    ar.ReadString(),                     // BASECLASS_ID -- a string
                    ar.ReadInt32(),
                    ar.ReadInt32(),
                    ar.ReadString()));
            }
        }

        // After the list, outside the version gate -- the recurring "read past the closing brace"
        // shape. Missing it desynchronised every level containing a training hall.
        int cost = ar.ReadInt32();

        return new TrainingHallEvent(baseEvent, forceExit, trainable, cost);
    }

    /// <summary>Reads a <c>REMOVE_NPC_DATA</c>.</summary>
    public static RemoveNpcEvent ReadRemoveNpc(IArchiveCursor ar, DesignVersion version,
                                               ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);
        int distance = ar.ReadInt32();

        string characterId;
        if (role == ArchiveRole.Editor && version < DesignVersion.SpellNames)
        {
            int key = ar.ReadInt32();
            characterId = key <= 0 ? string.Empty : key.ToString();
        }
        else
        {
            characterId = ar.ReadString();
        }

        return new RemoveNpcEvent(baseEvent, distance, characterId);
    }

    /// <summary>Reads a <c>CAMP_EVENT_DATA</c> — the base plus one flag.</summary>
    public static CampEvent ReadCamp(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);
        return new CampEvent(baseEvent, ar.ReadInt32());
    }

    /// <summary>Reads a <c>SOUND_EVENT</c> (<c>GameEvent.cpp</c>): a count then that many names.</summary>
    /// <remarks>
    /// Structurally identical to <c>BACKGROUND_SOUNDS</c>, but a separate class — do not assume
    /// they can share a reader if either gains a field.
    /// </remarks>
    public static SoundEvent ReadSound(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int count = ar.ReadInt32();
        var sounds = new List<string>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            sounds.Add(ArchiveStringConventions.Decode(ar.ReadString()));
        }
        return new SoundEvent(baseEvent, sounds);
    }

    /// <summary>Reads a <c>GAIN_EXP_DATA</c>.</summary>
    public static GainExperienceEvent ReadGainExperience(IArchiveCursor ar, DesignVersion version,
                                                         ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int experience = ar.ReadInt32();
        string sound = ArchiveStringConventions.Decode(ar.ReadString());
        int chance = ar.ReadInt32();
        int who = ar.ReadInt32();

        return new GainExperienceEvent(baseEvent, experience, sound, chance, who);
    }

    /// <summary>
    /// Reads a <c>FLOW_CONTROL_EVENT_DATA</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opens with its <b>own</b> version int, separate from the design version — one of the few
    /// events that self-versions. The value read is not used to gate anything on the loading path,
    /// but it is on the wire.
    /// </para>
    /// <para>
    /// <c>value</c> is a <c>CString</c> despite the name (<c>GameEvent.h:2xxx</c>), so this record
    /// carries <b>five</b> strings, not four. Reading it as a number costs a counted string.
    /// </para>
    /// </remarks>
    public static FlowControlEvent ReadFlowControl(IArchiveCursor ar, DesignVersion version,
                                                   ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int ownVersion = ar.ReadInt32();

        string entryMarker = ar.ReadString();
        string exitMarker = ar.ReadString();
        string destinationMarker = ar.ReadString();
        string globalVariableName = ar.ReadString();
        string value = ar.ReadString();                  // a string, not a number

        uint destinationId = ar.ReadUInt32();            // the editor discards this after reading
        int valueModification = ar.ReadInt32();
        int actionCondition = ar.ReadInt32();
        int action = ar.ReadInt32();
        uint flags = ar.ReadUInt32();

        return new FlowControlEvent(baseEvent, ownVersion, entryMarker, exitMarker,
                                    destinationMarker, globalVariableName, value,
                                    destinationId, valueModification, actionCondition,
                                    action, flags);
    }

    /// <summary>
    /// Reads a <c>WHO_PAYS_EVENT_DATA</c>.
    /// </summary>
    /// <remarks>
    /// The two <c>TRANSFER_DATA</c> blocks are read outside the storing/loading branch, so they
    /// are present at every version even though <c>moneyType</c> above them is gated at 0.912.
    /// </remarks>
    public static WhoPaysEvent ReadWhoPays(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int impossible = ar.ReadInt32();
        int gems = ar.ReadInt32();
        int jewels = ar.ReadInt32();
        int platinum = ar.ReadInt32();
        uint successChain = ar.ReadUInt32();
        int successAction = ar.ReadInt32();
        int failAction = ar.ReadInt32();
        uint failChain = ar.ReadUInt32();

        int moneyType = version >= DesignVersion.V0912 ? ar.ReadInt32() : 0;

        var successTransfer = SimpleEventReaders.ReadTransferData(ar);
        var failTransfer = SimpleEventReaders.ReadTransferData(ar);

        return new WhoPaysEvent(baseEvent, impossible, gems, jewels, platinum,
                                successChain, successAction, failAction, failChain,
                                moneyType, successTransfer, failTransfer);
    }

    /// <summary>
    /// Reads a <c>JOURNAL_EVENT</c> (<c>GameEvent.cpp:10225</c>) — the base plus one <c>int</c>.
    /// </summary>
    /// <remarks>
    /// The smallest subclass in the format. The entry is an index into the design's journal text,
    /// not the text itself.
    /// </remarks>
    public static JournalEvent ReadJournal(IArchiveCursor ar, DesignVersion version,
                                           ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);
        return new JournalEvent(baseEvent, ar.ReadInt32());
    }

    /// <summary>
    /// Reads a <c>PLAY_MOVIE_DATA</c> (<c>GameEvent.cpp:6544</c>).
    /// </summary>
    /// <remarks>
    /// <c>m_mode</c> arrives at 0.790, so an older design is a filename and nothing else. The
    /// reference also strips the directory from the filename below 3.05 — a read-side fix-up, not
    /// a format difference, and one this port leaves to the caller so the value stays what the
    /// file held.
    /// </remarks>
    public static PlayMovieEvent ReadPlayMovie(IArchiveCursor ar, DesignVersion version,
                                               ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        string fileName = ArchiveStringConventions.Decode(ar.ReadString());
        int mode = version >= DesignVersion.V0790 ? ar.ReadInt32() : 0;

        return new PlayMovieEvent(baseEvent, fileName, mode);
    }

    /// <summary>
    /// Reads a <c>VAULT_EVENT_DATA</c> (<c>GameEvent.cpp:9390</c>).
    /// </summary>
    /// <remarks>
    /// <b><c>WhichVault</c> is a <c>BYTE</c></b>, and it is gated at 0.910 — so the record is five
    /// bytes at and above that version and four below it, never eight.
    /// </remarks>
    public static VaultEvent ReadVault(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int forceBackup = ar.ReadInt32();
        byte whichVault = version >= DesignVersion.V0910 ? ar.ReadByte() : (byte)0;

        return new VaultEvent(baseEvent, forceBackup, whichVault);
    }

    /// <summary>
    /// Reads a <c>SMALL_TOWN_DATA</c> (<c>GameEvent.cpp:10138</c>) — six chains and a hole.
    /// </summary>
    /// <remarks>
    /// <b><c>Unused</c> is serialized.</b> It is a <c>long</c> the reference reads and writes and
    /// nothing consults, so skipping it as its name invites loses four bytes and every chain after
    /// it reads one field early.
    /// </remarks>
    public static SmallTownEvent ReadSmallTown(IArchiveCursor ar, DesignVersion version,
                                               ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        return new SmallTownEvent(
            baseEvent,
            Unused: ar.ReadInt32(),
            TempleChain: ar.ReadUInt32(),
            TrainingHallChain: ar.ReadUInt32(),
            ShopChain: ar.ReadUInt32(),
            InnChain: ar.ReadUInt32(),
            TavernChain: ar.ReadUInt32(),
            VaultChain: ar.ReadUInt32());
    }

    /// <summary>
    /// Reads a <c>TAVERN_TALES</c> (<c>GameEvent.cpp:9526</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two ASL blocks under two different map names</b>, and they are easy to swap: each tale
    /// carries a <c>"TALE"</c> block, and the event ends with a <c>"TAVTALE"</c> one. The names are
    /// the sync markers, so getting them the wrong way round fails loudly rather than silently —
    /// which is the one mercy in this record.
    /// </para>
    /// <para>
    /// The tale's own text is written <b>verbatim</b>, not through the blank convention, unlike
    /// every other body of prose in the event system.
    /// </para>
    /// <para>
    /// The type is obsolete — tales are part of <c>TAVERN</c> now — and <c>CreateNewEvent</c> still
    /// constructs it, so a design converted from FRUA can contain one.
    /// </para>
    /// </remarks>
    public static TavernTalesEvent ReadTavernTales(IArchiveCursor ar, DesignVersion version,
                                                   ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        uint flags = ar.ReadUInt32();

        int count = ar.ReadInt32();
        var tales = new List<TavernTale>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            tales.Add(new TavernTale(
                ar.ReadString(),                         // verbatim, no DAS
                ar.ReadUInt32(),
                AslReader.Read(ar, version, AslMaps.Tale)));
        }

        return new TavernTalesEvent(baseEvent, flags, tales,
                                    AslReader.Read(ar, version, AslMaps.TavernTale));
    }
}
