using UAF.Data;
using UAF.Media;
using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// A design opened from disk: its records, its layout config, and the art and fonts drawn from it.
/// </summary>
/// <remarks>
/// <para>
/// The engine's equivalent of <c>OpenDesign</c>, minus the part that made the original untestable.
/// The C++ version cannot run headless because it needs a live DirectX device before it will read
/// a single record (docs/PORTING-PLAN.md §7 Phase 0); here loading is pure file I/O and the
/// presenter is a separate concern entirely.
/// </para>
/// <para>
/// Art is resolved leniently, because designs genuinely ship with slots naming files that are not
/// present — the original warned and carried on, and a missing wall texture must not stop a design
/// loading.
/// </para>
/// </remarks>
public sealed class LoadedDesign : IDisposable
{
    private readonly Dictionary<string, Surface?> artCache = [];
    private readonly ImageLoader loader;
    private readonly IFontRasterizer? rasterizer;
    private readonly Dictionary<int, BitmapFont> fonts = [];

    /// <summary>Which reader the databases use. See <see cref="Open"/>.</summary>
    private readonly ArchiveRole role;

    private LoadedDesign(string root, GlobalStatsPrefix globals, DesignConfig config,
                         ImageLoader loader, IFontRasterizer? rasterizer, ArchiveRole role)
    {
        Root = root;
        Globals = globals;
        Config = config;
        this.loader = loader;
        this.rasterizer = rasterizer;
        this.role = role;
    }

    public string Root { get; }

    private List<SpecialAbility>? specialAbilities;

    /// <summary>
    /// The design's special abilities — where its GPDL scripts live.
    /// </summary>
    /// <remarks>
    /// A design without a <c>specialAbilities.txt</c> is ordinary: it overrides no hook and every
    /// one falls back on the built-in defaults. See <see cref="SpecialAbilitiesFile"/>.
    /// </remarks>
    public IReadOnlyList<SpecialAbility> SpecialAbilities =>
        specialAbilities ??= SpecialAbilitiesFile.Load(
            Path.Combine(Root, "Data", "specialAbilities.txt"));

    private ForthAiScript? aiScript;

    private bool aiScriptTried;

    /// <summary>
    /// The design's compiled <c>AI_Script.BLK</c>, or null when it has none that builds.
    /// </summary>
    /// <remarks>
    /// <b>Compiled once and reused, as the reference does.</b> <c>ExpandKernel</c> guards itself
    /// with a <c>static bool finished</c>, so the dictionary is built on the first fight and every
    /// later one runs against it. Null is the ordinary case in the sense that matters: every
    /// shipped design carries the stock script, which <see cref="MonsterAiScript"/> already is, so
    /// only a design that edited its own gets a different answer from going through here.
    /// </remarks>
    public ForthAiScript? AiScript
    {
        get
        {
            if (!aiScriptTried)
            {
                aiScriptTried = true;
                aiScript = ForthAiScript.Load(Path.Combine(Root, "Data"));
            }

            return aiScript;
        }
    }

    public GlobalStatsPrefix Globals { get; }

    public DesignConfig Config { get; }

    public string Name => Globals.DesignName;

    /// <summary>
    /// Opens a design directory — the one containing <c>Data/</c> and <c>Resources/</c>.
    /// </summary>
    /// <param name="rasterizer">
    /// Optional. Without one the design still loads and still draws; it simply has no text, which
    /// is the same degradation contract the video and image decoders use.
    /// </param>
    /// <param name="role">
    /// Which of the two readers to use for the databases.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The role is not a detail, and the default is the strict one.</b> The two differ only in
    /// what they accept below 0.998101, and the engine refuses such a design outright
    /// (<c>Level.cpp:3365</c>) where the editor is expected to open it and upgrade it. Defaulting
    /// to <see cref="ArchiveRole.Engine"/> keeps that distinction: an editor asks for
    /// <see cref="ArchiveRole.Editor"/> deliberately.
    /// </para>
    /// <para>
    /// <b>It applies to the databases only.</b> <c>GLOBAL_STATS</c> and the level files are read
    /// as the editor either way — the design's identity and its maps have to be legible before
    /// anything can decide whether to refuse it.
    /// </para>
    /// </remarks>
    public static LoadedDesign Open(string root, IImageDecoder? extraDecoder = null,
                                    IFontRasterizer? rasterizer = null,
                                    ArchiveRole role = ArchiveRole.Engine)
    {
        ArgumentNullException.ThrowIfNull(root);

        string data = Path.Combine(root, "Data");
        if (!Directory.Exists(data))
        {
            throw new DirectoryNotFoundException($"no Data/ directory under '{root}'");
        }

        using var stream = File.OpenRead(Path.Combine(data, "game.dat"));
        var cursor = GameDataReader.Open(stream);
        // Past the character list, not stopping at it. ReadThroughCharacters leaves LEVEL_INFO
        // unread, which makes Globals.Levels null -- and a null level table silently disables
        // every script path that falls through to the design's own level data: the map-override
        // family and the design's shipped level attributes both read as empty. Passing no event
        // reader still stops before the global event list, so this reads the level table, the
        // money data and the difficulty settings and no further.
        var globals = GlobalStatsReader.Read(cursor.Body, cursor.Version, ArchiveRole.Editor,
                                             null, pics: cursor.PicVariant);

        // The engine reads config.txt and nothing else -- rte.ConfigDir() + "config.txt" at both
        // call sites (Dungeon.cpp:191, RunEvent.cpp:27063), and "config640" appears nowhere in the
        // engine at all.
        //
        // An earlier revision here preferred config640.txt on the reasoning that a design ships one
        // config per resolution. It does, but they are EDITOR templates: GameResChange.cpp copies
        // the chosen one OVER config.txt (:99, :119), so by the time the engine runs there is one
        // file and its name is config.txt. Reading config640.txt directly picks up whichever
        // resolution the design was last authored at rather than the one it was saved with -- and
        // for SomethingWild the two disagree on DEFAULT_MENU_TEXTBOX (20,328 against 200,328),
        // which put every question list's options 180px right of where they belong.
        string config = Path.Combine(data, "config.txt");

        return new LoadedDesign(root, globals,
                                File.Exists(config) ? DesignConfig.Load(config)
                                                    : DesignConfig.Parse([]),
                                new ImageLoader(extraDecoder), rasterizer, role);
    }

    /// <summary>
    /// Loads art by the filename a design record gave, or returns null when it cannot be found or
    /// decoded.
    /// </summary>
    /// <remarks>
    /// Cached by name including the failures, so a design naming a missing file does not retry the
    /// lookup on every frame.
    /// </remarks>
    public Surface? Art(string fileName, SurfaceKind kind = SurfaceKind.Common)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string key = $"{fileName}|{kind}";
        if (artCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Surface? surface = null;
        string path = Path.Combine(Root, "Resources", fileName);
        if (File.Exists(path))
        {
            try
            {
                surface = loader.LoadSurface(path, kind);
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException)
            {
                surface = null;
            }
        }

        artCache[key] = surface;
        return surface;
    }

    /// <summary>
    /// A font at the given pixel height, rasterised once and reused.
    /// </summary>
    /// <remarks>
    /// The design's requested face (<see cref="LogFont.FaceName"/>) is not honoured yet: the
    /// bundled PT Serif is used at the design's requested height and weight. Resolving a named
    /// system face is a policy question the original also had — it warned "Cannot find specified
    /// font named %s" and carried on.
    /// </remarks>
    public BitmapFont? Font(int pixelHeight)
    {
        if (rasterizer is null || !rasterizer.IsAvailable)
        {
            return null;
        }

        if (fonts.TryGetValue(pixelHeight, out var cached))
        {
            return cached;
        }

        var font = new BitmapFont(rasterizer.Rasterize(
            EmbeddedFonts.PtSerif(bold: Globals.Font.IsBold, italic: Globals.Font.Italic),
            new FontRasterOptions(pixelHeight, Antialias: true)));

        fonts[pixelHeight] = font;
        return font;
    }

    /// <summary>The height the design's <c>LOGFONT</c> asks for.</summary>
    public int RequestedFontHeight => Math.Clamp(Globals.Font.PointSizeHint, 8, 48);

    /// <summary>The level files present, in name order.</summary>
    private ItemDatabase? items;
    private bool itemsLoaded;

    /// <summary>
    /// The design's item database, or null when <c>items.dat</c> is missing or unreadable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Loaded on first use rather than at open, because a design can be walked around without it
    /// and a failure here must not stop the level loading — the engine's own behaviour when a
    /// database is absent is to carry on with an empty one.
    /// </para>
    /// <para>
    /// <b>The unstamped fallback depends on <c>game.dat</c> having been read first</b>:
    /// <c>ItemsFallback</c> is <c>min(globalData.version, 0.696)</c> (<c>Items.cpp:3418</c>), so
    /// the load order is not arbitrary. It holds here because <see cref="Globals"/> is read in the
    /// constructor.
    /// </para>
    /// </remarks>
    public ItemDatabase? Items
    {
        get
        {
            if (itemsLoaded)
            {
                return items;
            }

            itemsLoaded = true;
            items = LoadItems();

            // A pre-0.998101 design says which classes may use an item as a bitmask, and the
            // conversion to baseclass names needs classes.dat -- which is why the reference
            // defers it to a pass over the whole database once everything is loaded
            // (Items.cpp:6422) rather than doing it in the reader. Same reason here.
            //
            // Deliberately AFTER the assignment: reading Classes can come back here for Items,
            // and a half-built database is better reached than an infinite recursion.
            if (items is { } read && read.Items.Any(ItemUsabilityUpgrade.NeedsUpgrade))
            {
                items = ItemUsabilityUpgrade.Upgrade(
                    read, [.. Classes?.Values ?? []]);
            }

            return items;
        }
    }

    private ItemDatabase? LoadItems()
    {
        string path = Path.Combine(Root, "Data", "items.dat");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var header = DesignFileHeader.Read(stream, DesignFileKind.Database,
                                               DesignFileKind.ItemsFallback(Globals.Version));

            if (header.Tier == ArchiveTier.CompressedCar)
            {
                // The compression-type byte sits at 16 and CarArchiveReader.Open consumes it, so
                // the seek is to the magic's end rather than to PayloadOffset.
                stream.Seek(16, SeekOrigin.Begin);
                return ItemRecordReader.ReadDatabase(CarArchiveReader.Open(stream),
                                                     header.Version, role);
            }

            stream.Seek(header.PayloadOffset, SeekOrigin.Begin);
            return ItemRecordReader.ReadDatabase(new MfcArchiveReader(stream),
                                                 header.Version, role);
        }
        catch (Exception e) when (e is IOException or InvalidDataException
                                       or EndOfStreamException or InvalidOperationException)
        {
            // A database this port cannot yet read is a real state -- the engine still runs, and
            // items simply cannot change hands. Throwing here would take the whole design down.
            return null;
        }
    }

    private List<MonsterRecord>? monsters;
    private bool monstersLoaded;

    /// <summary>
    /// The monster database, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// Same framing and the same fallback as <see cref="Items"/> — both are ordinary databases and
    /// both depend on <c>game.dat</c> having been read first for their unstamped version.
    /// </remarks>
    public IReadOnlyList<MonsterRecord>? Monsters
    {
        get
        {
            if (monstersLoaded)
            {
                return monsters;
            }

            monstersLoaded = true;
            monsters = LoadMonsters();
            return monsters;
        }
    }

    /// <summary>
    /// A monster by its unique name, or null.
    /// </summary>
    /// <remarks>
    /// Matched case-insensitively on <see cref="MonsterRecord.Name"/>, which is what a combat
    /// event's <c>monsterID</c> names. As with items, several records can share a name and the
    /// first match wins.
    /// </remarks>
    public MonsterRecord? Monster(string monsterId) =>
        string.IsNullOrEmpty(monsterId)
            ? null
            : Monsters?.FirstOrDefault(
                m => string.Equals(m.Name, monsterId, StringComparison.OrdinalIgnoreCase));

    private List<SpellRecord>? spells;
    private bool spellsLoaded;

    /// <summary>
    /// The spell database, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// Same framing and fallback as <see cref="Monsters"/>. Combat needs it for casting times; a
    /// design whose spells cannot be read still fights, with every spell resolving immediately.
    /// </remarks>
    public IReadOnlyList<SpellRecord>? Spells
    {
        get
        {
            if (spellsLoaded)
            {
                return spells;
            }

            spellsLoaded = true;
            spells = LoadSpells();
            return spells;
        }
    }

    /// <summary>A spell by its unique name, or null. Matched as monsters and items are.</summary>
    public SpellRecord? Spell(string spellId) =>
        string.IsNullOrEmpty(spellId)
            ? null
            : Spells?.FirstOrDefault(
                s => string.Equals(s.Name, spellId, StringComparison.OrdinalIgnoreCase));

    private List<SpellRecord>? LoadSpells()
    {
        string path = Path.Combine(Root, "Data", "spells.dat");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var header = DesignFileHeader.Read(stream, DesignFileKind.Database,
                                               DesignFileKind.ItemsFallback(Globals.Version));

            if (header.Tier == ArchiveTier.CompressedCar)
            {
                stream.Seek(16, SeekOrigin.Begin);
                return SpellRecordReader.ReadDatabase(CarArchiveReader.Open(stream),
                                                      header.Version, role);
            }

            stream.Seek(header.PayloadOffset, SeekOrigin.Begin);
            return SpellRecordReader.ReadDatabase(new MfcArchiveReader(stream),
                                                  header.Version, role);
        }
        catch (Exception e) when (e is IOException or InvalidDataException
                                       or EndOfStreamException or InvalidOperationException)
        {
            return null;
        }
    }

    private List<MonsterRecord>? LoadMonsters()
    {
        string path = Path.Combine(Root, "Data", "monsters.dat");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var header = DesignFileHeader.Read(stream, DesignFileKind.Database,
                                               DesignFileKind.ItemsFallback(Globals.Version));

            if (header.Tier == ArchiveTier.CompressedCar)
            {
                stream.Seek(16, SeekOrigin.Begin);
                return MonsterRecordReader.ReadDatabase(CarArchiveReader.Open(stream),
                                                        header.Version, role);
            }

            stream.Seek(header.PayloadOffset, SeekOrigin.Begin);
            return MonsterRecordReader.ReadDatabase(new MfcArchiveReader(stream),
                                                    header.Version, role);
        }
        catch (Exception e) when (e is IOException or InvalidDataException
                                       or EndOfStreamException or InvalidOperationException)
        {
            // A database this port cannot yet read is a real state: combat simply finds no
            // monsters, rather than the design failing to open.
            return null;
        }
    }

    private Dictionary<string, BaseclassRecord>? baseclasses;
    private bool baseclassesLoaded;

    /// <summary>
    /// The design's baseclasses by name, or null when <c>baseclass.dat</c> is missing or is a
    /// shape this port does not read.
    /// </summary>
    /// <remarks>
    /// Lazy and failure-tolerant for the same reason as <see cref="Items"/>. Null is a real state
    /// worth expecting here: <c>DefaultDesign</c>'s records are <c>Bcd1</c>, which the reference
    /// engine itself refuses outright (<c>class.cpp:5731</c>), so a design the port cannot level
    /// is not necessarily a design the port has got wrong.
    /// </remarks>
    public IReadOnlyDictionary<string, BaseclassRecord>? Baseclasses
    {
        get
        {
            if (baseclassesLoaded)
            {
                return baseclasses;
            }

            baseclassesLoaded = true;
            baseclasses = LoadBaseclasses();
            return baseclasses;
        }
    }

    private Dictionary<string, BaseclassRecord>? LoadBaseclasses()
    {
        string path = Path.Combine(Root, "Data",
                                   TaggedDatabaseReader.FileName(TaggedDatabase.Baseclass));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var header = TaggedDatabaseReader.Read(path, TaggedDatabase.Baseclass, out var body,
                                                   out var stream);
            using (stream)
            {
                var map = new Dictionary<string, BaseclassRecord>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in BaseclassRecordReader.ReadAll(body, header.Count))
                {
                    map[record.Name] = record;
                }
                return map;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException
                                       or EndOfStreamException or InvalidOperationException)
        {
            return null;
        }
    }

    private Dictionary<string, RaceRecord>? races;
    private bool racesLoaded;

    /// <summary>
    /// The design's races by name, or null when <c>races.dat</c> is missing or is a shape this port
    /// does not read.
    /// </summary>
    /// <remarks>
    /// Null is a real state: <c>DefaultDesign</c> ships <c>RaceV1</c>, the one container shape where
    /// the editor and the engine read different streams, which this port refuses rather than guess
    /// at. A design whose races cannot be read still levels — it simply has no race-imposed cap.
    /// </remarks>
    public IReadOnlyDictionary<string, RaceRecord>? Races
    {
        get
        {
            if (racesLoaded)
            {
                return races;
            }

            racesLoaded = true;
            races = LoadRaces();
            return races;
        }
    }

    private Dictionary<string, RaceRecord>? LoadRaces()
    {
        string path = Path.Combine(Root, "Data",
                                   TaggedDatabaseReader.FileName(TaggedDatabase.Race));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var header = TaggedDatabaseReader.Read(path, TaggedDatabase.Race, out var body,
                                                   out var stream);
            using (stream)
            {
                var map = new Dictionary<string, RaceRecord>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in RaceRecordReader.ReadAll(body, header.Count, header.Tag,
                                                                Globals.Version))
                {
                    map[record.Name] = record;
                }
                return map;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException
                                       or EndOfStreamException or InvalidOperationException)
        {
            return null;
        }
    }

    private Dictionary<string, AbilityRecord>? abilities;
    private bool abilitiesLoaded;

    /// <summary>The design's abilities, by name — or null when the database cannot be read.</summary>
    /// <remarks>
    /// The dice a new character's scores are rolled from. Unread by this port until the character
    /// generator needed them — see §ability.dat.
    /// </remarks>
    public IReadOnlyDictionary<string, AbilityRecord>? Abilities
    {
        get
        {
            if (abilitiesLoaded)
            {
                return abilities;
            }

            abilitiesLoaded = true;
            abilities = LoadAbilities();
            return abilities;
        }
    }

    private Dictionary<string, AbilityRecord>? LoadAbilities()
    {
        string path = Path.Combine(Root, "Data",
                                   TaggedDatabaseReader.FileName(TaggedDatabase.Ability));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var header = TaggedDatabaseReader.Read(path, TaggedDatabase.Ability, out var body,
                                                   out var stream);
            using (stream)
            {
                var map = new Dictionary<string, AbilityRecord>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in AbilityRecordReader.ReadAll(body, header.Count,
                                                                   Globals.Version))
                {
                    map[record.Name] = record;
                }
                return map;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException
                                       or EndOfStreamException or InvalidOperationException)
        {
            return null;
        }
    }

    private Dictionary<string, ClassRecord>? classes;
    private bool classesLoaded;

    /// <summary>The design's classes, by name — or null when the database cannot be read.</summary>
    /// <remarks>
    /// A <i>class</i> is a bundle of one or more baseclasses; the character generator offers
    /// these, and the rules elsewhere work in baseclasses. Both tables are needed to answer which
    /// classes a race may take.
    /// </remarks>
    public IReadOnlyDictionary<string, ClassRecord>? Classes
    {
        get
        {
            if (classesLoaded)
            {
                return classes;
            }

            classesLoaded = true;
            classes = LoadClasses();
            return classes;
        }
    }

    private Dictionary<string, ClassRecord>? LoadClasses()
    {
        string path = Path.Combine(Root, "Data",
                                   TaggedDatabaseReader.FileName(TaggedDatabase.Class));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var header = TaggedDatabaseReader.Read(path, TaggedDatabase.Class, out var body,
                                                   out var stream);
            using (stream)
            {
                var map = new Dictionary<string, ClassRecord>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in ClassRecordReader.ReadAll(body, header.Count,
                                                                 Globals.Version))
                {
                    map[record.Name] = record;
                }
                return map;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException
                                       or EndOfStreamException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The level ceiling a character faces in one baseclass, from its baseclass and its race.
    /// </summary>
    /// <remarks>
    /// The reference resolves <c>MaxLevel$SYS$</c> through a <c>SKILL_COMPUTATION</c> built with
    /// <c>minimize = true</c>, so the <b>smaller</b> of the two wins
    /// (<c>Char.cpp:GetLevelCap</c>, <c>class.cpp:5215</c>). A missing database simply contributes
    /// no cap.
    /// </remarks>
    public int LevelCap(Character character, BaseclassRecord baseclass)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(baseclass);

        int fromBaseclass = Levelling.GetLevelCapFromSkills(
            baseclass.Skills.Select(s => (s.SkillId, s.Value)));

        int fromRace = Levelling.NoLevelCap;
        if (Races is { } known && known.TryGetValue(character.Race, out var race))
        {
            fromRace = Levelling.GetLevelCapFromSkills(race.Skills.Select(s => (s.SkillId, s.Value)));
        }

        return Levelling.CombineLevelCaps(fromBaseclass, fromRace);
    }

    /// <summary>
    /// Whether a character has earned a level it has not taken, in any of its baseclasses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CHARACTER::IsReadyToTrain</c> (<c>Char.cpp</c>) asks this per baseclass and stops at the
    /// first that qualifies. Replaces reading the flag off the character record, which was the
    /// design author's answer rather than this engine's.
    /// </para>
    /// <para>
    /// <b>Falls back to the stored flag when the baseclasses cannot be read</b>, since a design
    /// whose <c>baseclass.dat</c> this port refuses still has to draw its roster.
    /// </para>
    /// </remarks>
    public bool IsReadyToTrain(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);

        if (Baseclasses is not { } known)
        {
            return character.ReadyToTrain;
        }

        foreach (var progress in character.Baseclasses)
        {
            if (!known.TryGetValue(progress.BaseclassId, out var baseclass))
            {
                continue;   // the reference logs "unknown baseclass" and moves on
            }

            if (Levelling.IsReadyToTrain(baseclass.ExperienceLevels, (uint)progress.Experience,
                                         progress.CurrentLevel, progress.PreviousLevel,
                                         LevelCap(character, baseclass)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Looks an item up by the id a record or a carried instance names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An item's id is its <c>m_uniqueName</c>, not its <c>m_idName</c></b> —
    /// <c>ITEM_ID ItemID(void) const { x = m_uniqueName; }</c> (<c>Items.h:701</c>). The names
    /// invite the opposite reading and this port took it: <c>m_idName</c> is the fuller display
    /// name, so <c>Ambassador's_Letter</c>'s glaive is <c>UniqueName "Glaive"</c> with
    /// <c>IdName "Noble Glaive"</c>, and a carried instance names the former. Keying on
    /// <c>IdName</c> resolves nothing and reports every treasure item as missing, with no error to
    /// say why. <c>DefaultDesign</c> cannot show the difference — its records set both names the
    /// same.
    /// </para>
    /// <para>
    /// Several records can share a name; the reference's <c>GetItem</c> walks the list and takes
    /// the first match, so this does too.
    /// </para>
    /// </remarks>
    public ItemRecord? Item(string itemId) =>
        string.IsNullOrEmpty(itemId)
            ? null
            : Items?.Items.FirstOrDefault(
                i => string.Equals(i.Names.UniqueName, itemId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> LevelFiles =>
        Directory.Exists(Path.Combine(Root, "Data"))
            ? Directory.GetFiles(Path.Combine(Root, "Data"), "*.lvl")
                       .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    /// <summary>
    /// Reads a whole level by index, wall sets included, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// The wall and background tables sit after the event list, so this needs a reader for every
    /// event type in the file. <see cref="EventBodyReader"/> aborts on one it does not know, and a
    /// level containing such an event comes back null rather than partial — the alternative is a
    /// wall table read out of the middle of an event body.
    /// </remarks>
    /// <summary>
    /// The file holding the level with this index, or null when the design has no such level.
    /// </summary>
    /// <param name="levelIndex">
    /// The level's own index — <c>Game.LevelIndex</c>, the key into <c>LEVEL_INFO</c>. The file is
    /// named for <paramref name="levelIndex"/> <b>plus one</b>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>A level's number is not its position in the directory listing, and conflating the two is
    /// a real bug on a real design.</b> A level file is named for its index plus one
    /// (<c>Shared/Level.cpp:3643</c>), and a design may skip numbers: <c>Case.dsn</c> ships ten
    /// levels numbered 001-004, 011-013, 016, 018 and 255. Its last file sits at <i>position</i>
    /// nine and is <i>level</i> 255, so anything that walked to level 255 and then read position
    /// 255 — or read position nine — would get the wrong level or none.
    /// </para>
    /// <para>
    /// The two coincide exactly when a design is numbered from 1 with no gaps, which every design
    /// used to test this port except <c>Case</c> happens to be. That is why the confusion survived:
    /// see <see cref="Level"/>, which takes a position and is what the editor's own lists want.
    /// </para>
    /// </remarks>
    public string? LevelFileFor(int levelIndex)
    {
        if (levelIndex < 0)
        {
            return null;
        }

        // Case-insensitively, because a design authored on Windows may disagree with itself about
        // the case of its own filenames -- the same reason asset resolution is case-insensitive.
        string wanted = $"Level{levelIndex + 1:000}.lvl";

        return LevelFiles.FirstOrDefault(
            f => string.Equals(Path.GetFileName(f), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The level index of the file at a position in <see cref="LevelFiles"/>, or null when its
    /// name does not follow the convention.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="LevelFileFor"/>, and the bridge anything enumerating the listing
    /// needs before it can talk to the engine: <c>Game</c> takes an index, and a loop over the
    /// listing has a position. On <c>Case.dsn</c> position 9 is index 254.
    /// </remarks>
    public int? LevelIndexAt(int position)
    {
        var files = LevelFiles;
        if (position < 0 || position >= files.Count)
        {
            return null;
        }

        string name = Path.GetFileNameWithoutExtension(files[position]);

        return name.StartsWith("Level", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(name.AsSpan(5), out int number)
               && number > 0
            ? number - 1
            : null;
    }

    /// <summary>The whole level with this index, or null. See <see cref="LevelFileFor"/>.</summary>
    public LevelFile? LevelNumbered(int levelIndex) =>
        LevelFileFor(levelIndex) is { } path ? ReadLevel(path) : null;

    /// <summary>The map of the level with this index, or null. See <see cref="LevelFileFor"/>.</summary>
    public Map? MapNumbered(int levelIndex) =>
        LevelFileFor(levelIndex) is { } path ? ReadMap(path) : null;

    public LevelFile? Level(int index)
    {
        var files = LevelFiles;
        if (index < 0 || index >= files.Count)
        {
            return null;
        }

        return ReadLevel(files[index]);
    }

    /// <summary>Reads one <c>.lvl</c> whole, or null when it will not decode.</summary>
    private static LevelFile? ReadLevel(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return LevelFileReader.Read(stream, ArchiveRole.Editor,
                (ar, type, version) => EventBodyReader.TryRead(ar, type, version, ArchiveRole.Editor));
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException
                                    or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a level's map by index into <see cref="LevelFiles"/>, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Only the grid is read, which always succeeds: the cells are fixed-size and sit before the
    /// event list. <see cref="Level"/> reads everything but can fail on an unported event type, so
    /// movement uses this and wall rendering uses that.
    /// </remarks>
    public Map? Map(int index)
    {
        var files = LevelFiles;
        if (index < 0 || index >= files.Count)
        {
            return null;
        }

        return ReadMap(files[index]);
    }

    /// <summary>Reads one <c>.lvl</c>'s grid, or null when it will not decode.</summary>
    private static Map? ReadMap(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var (_, width, height, cells) = LevelFileReader.ReadAreaMapOnly(stream);
            return new Map(width, height, cells);
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException
                                    or EndOfStreamException)
        {
            return null;
        }
    }

    public void Dispose() => (rasterizer as IDisposable)?.Dispose();
}
