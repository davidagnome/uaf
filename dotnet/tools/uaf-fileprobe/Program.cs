// uaf-fileprobe -- the C# counterpart to the C++ oracle dumper.
//
// Reads a Dungeon Craft design with UAF.Serialization and emits canonical JSON shaped like
// UAFWinEd's -dumpjson output, so the two can be diffed directly. Phase 1's exit criterion is
// that this output matches the reference for every fixture (docs/PORTING-PLAN.md section 7).
//
// Usage:  uaf-fileprobe <design.dsn|Data-dir> [out.json]

using System.Text.Json;
using System.Text.Json.Nodes;
using UAF.Common;
using UAF.Serialization;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: uaf-fileprobe <design.dsn|Data-dir> [out.json]");
    return 2;
}

string root = args[0];
// Accept either a design folder or its Data subdirectory: fixtures come in both shapes -- a
// re-saved design has Data/, while the manikus default set is a bare Data folder.
string dataDir = Directory.Exists(Path.Combine(root, "Data")) ? Path.Combine(root, "Data") : root;

if (!File.Exists(Path.Combine(dataDir, "game.dat")))
{
    Console.Error.WriteLine($"no game.dat under {dataDir}");
    return 2;
}

JsonObject result = new DesignProbe(dataDir).Run();
string json = result.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

if (args.Length >= 2)
{
    File.WriteAllText(args[1], json + "\n");
    Console.Error.WriteLine($"wrote {args[1]}");
}
else
{
    Console.WriteLine(json);
}
return 0;

/// <summary>Walks a design's files and builds the oracle-shaped JSON model.</summary>
internal sealed class DesignProbe(string dataDir)
{
    private readonly List<string> _diagnostics = [];

    private void Warn(string message) => _diagnostics.Add(message);

    public JsonObject Run()
    {
        var meta = new JsonObject { ["producer"] = "uaf-fileprobe" };
        var root = new JsonObject { ["_meta"] = meta };

        DesignVersion designVersion;
        using (var fs = File.OpenRead(Path.Combine(dataDir, "game.dat")))
        {
            var header = DesignFileHeader.Read(fs, DesignFileKind.GameData);
            designVersion = header.Version;
            meta["designVersion"] = header.Version.Value;
            meta["archiveTier"] = header.HadMagic ? "CompressedMidStream" : "PlainArchive";
            root["globalData"] = header.HadMagic
                ? ReadGlobalDataViaCursor(fs)
                : ReadGlobalData(fs, header);
        }

        root["counts"] = ReadCounts(designVersion);
        root["firstItem"] = ReadFirstItem(designVersion);
        root["firstMonster"] = ReadFirstName("monsters.dat", designVersion);
        root["firstSpell"] = ReadFirstName("spells.dat", designVersion);
        root["level"] = ReadFirstLevel();

        meta["ok"] = _diagnostics.Count == 0;
        meta["diagnostics"] = new JsonArray(
            _diagnostics.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray());
        return root;
    }

    /// <summary>Mirrors GLOBAL_STATS::Serialize's loading branch (GlobalData.cpp:3992).</summary>
    private static JsonObject ReadGlobalData(FileStream fs, DesignFileHeader header)
    {
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);

        double version = ar.ReadDouble();
        string designName = ar.ReadString();
        int startLevel = ar.ReadInt32();
        byte startX = ar.ReadByte(), startY = ar.ReadByte(), startFacing = ar.ReadByte();
        int startTime = ar.ReadInt32(), startExp = ar.ReadInt32(), startExpType = ar.ReadInt32();
        ar.ReadInt32();                                    // retired startEquip slot
        int startPlatinum = ar.ReadInt32(), startGem = ar.ReadInt32(), startJewelry = ar.ReadInt32();
        int dungeonTime = ar.ReadInt32(), dungeonSearch = ar.ReadInt32();
        int wildTime = ar.ReadInt32(), wildSearch = ar.ReadInt32();
        int autoDarkenViewport = ar.ReadInt32(), autoDarkenAmount = ar.ReadInt32();
        int startDarken = ar.ReadInt32(), endDarken = ar.ReadInt32();
        int minPCs = ar.ReadInt32();
        int packed = ar.ReadInt32();
        int flags = ar.ReadInt32();

        string mapArt = ArchiveStringConventions.Decode(ar.ReadString());
        ar.Skip(60);                                       // LOGFONTA blob (version >= 0.830)
        string iconBgArt = ArchiveStringConventions.Decode(ar.ReadString());
        string backgroundArt = ArchiveStringConventions.Decode(ar.ReadString());

        // maxParty_maxPCs is REPAIRED after reading when its high half is zero
        // (GlobalData.cpp:3983): the stored bytes are not the effective value.
        int maxPCs = packed & 0xffff;
        int maxPartySize = (packed >> 16) == 0 ? maxPCs + 2 : packed >> 16;

        return new JsonObject
        {
            ["autoDarkenAmount"] = autoDarkenAmount,       // BOOL-typed but integer-valued
            ["autoDarkenViewport"] = autoDarkenViewport,
            ["backgroundArt"] = backgroundArt,
            ["designName"] = designName,
            ["dungeonSearchTimeDelta"] = dungeonSearch,
            ["dungeonTimeDelta"] = dungeonTime,
            ["endDarken"] = endDarken,
            ["flags"] = flags,
            ["iconBgArt"] = iconBgArt,
            ["mapArt"] = mapArt,
            ["maxPCs"] = maxPCs,
            ["maxPartySize"] = maxPartySize,
            ["minPCs"] = minPCs,
            ["startDarken"] = startDarken,
            ["startExp"] = startExp,
            ["startExpType"] = startExpType,
            ["startFacing"] = startFacing,
            ["startGem"] = startGem,
            ["startJewelry"] = startJewelry,
            ["startLevel"] = startLevel,
            ["startPlatinum"] = startPlatinum,
            ["startTime"] = startTime,
            ["startX"] = startX,
            ["startY"] = startY,
            ["version"] = version,
            ["wildernessSearchTimeDelta"] = wildSearch,
            ["wildernessTimeDelta"] = wildTime,
        };
    }



    /// <summary>
    /// Reads a magic-stamped <c>game.dat</c> through <see cref="GameDataReader"/>, which handles
    /// the mid-stream compression switch (<c>GlobalData.cpp:4336</c>).
    /// </summary>
    private static JsonObject ReadGlobalDataViaCursor(FileStream fs)
    {
        var cursor = GameDataReader.Open(fs);
        return new JsonObject
        {
            ["designName"] = cursor.ReadString(),
            ["version"] = cursor.Version.Value,
            ["framing"] = cursor.Framing.ToString(),
            ["_note"] = "compressed game.dat: only the leading fields are read so far",
        };
    }

    /// <summary>
    /// Opens a database payload, transparently handling tier 2 (plain primitives) and tier 3
    /// (LZW + string interning). Returns null when the file is absent.
    /// </summary>
    private DbCursor? OpenDatabase(string fileName, DesignVersion globalVersion)
    {
        string path = Path.Combine(dataDir, fileName);
        if (!File.Exists(path)) { Warn($"{fileName} missing"); return null; }

        var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database,
                                           DesignFileKind.ItemsFallback(globalVersion));

        if (header.Tier == ArchiveTier.CompressedCar)
        {
            // The compression-type byte sits at 16, written through CAR before compression
            // begins, so CarArchiveReader.Open consumes it.
            fs.Seek(16, SeekOrigin.Begin);
            return new DbCursor(fs, CarArchiveReader.Open(fs), null, header.Version);
        }

        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        return new DbCursor(fs, null, new MfcArchiveReader(fs), header.Version);
    }

    private sealed record DbCursor(FileStream Stream, CarArchiveReader? Car,
                                   MfcArchiveReader? Plain, DesignVersion Version) : IDisposable
    {
        public int ReadInt32() => Car?.ReadInt32() ?? Plain!.ReadInt32();
        public string ReadString() => Car?.ReadString() ?? Plain!.ReadString();
        public void Dispose() => Stream.Dispose();
    }

    private JsonObject ReadCounts(DesignVersion globalVersion)
    {
        var counts = new JsonObject();
        foreach (var (file, key) in new[]
                 {
                     ("items.dat", "items"), ("monsters.dat", "monsters"), ("spells.dat", "spells"),
                 })
        {
            using var db = OpenDatabase(file, globalVersion);
            counts[key] = db is null ? 0 : db.ReadInt32();
        }
        return counts;
    }

    private JsonObject ReadFirstItem(DesignVersion globalVersion)
    {
        var result = new JsonObject();
        using var db = OpenDatabase("items.dat", globalVersion);
        if (db is null || db.ReadInt32() <= 0) return result;

        db.ReadInt32();                                          // preSpellNameKey
        if (db.Version.Value >= 0.999647) db.ReadString();       // spellID -- a STRING, not an int

        result["uniqueName"] = ArchiveStringConventions.Decode(db.ReadString());
        result["idName"] = ArchiveStringConventions.Decode(db.ReadString());
        return result;
    }

    private JsonValue? ReadFirstName(string fileName, DesignVersion globalVersion)
    {
        using var db = OpenDatabase(fileName, globalVersion);
        if (db is null || db.ReadInt32() <= 0) return null;

        // Monsters and spells share a preamble with no spellID field -- unlike items.
        if (DatabaseRecordReader.HasPreSpellNameKey(db.Version)) db.ReadInt32();
        return JsonValue.Create(ArchiveStringConventions.Decode(db.ReadString()));
    }

    private JsonObject ReadFirstLevel()
    {
        var result = new JsonObject();
        string? path = Directory.EnumerateFiles(dataDir, "Level*.lvl").OrderBy(p => p).FirstOrDefault();
        if (path is null) { Warn("no Level*.lvl found"); return result; }

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);

        var (width, height, cells) = LevelReader.ReadAreaMap(ar, header.Version);
        result["file"] = Path.GetFileName(path);
        result["version"] = header.Version.Value;
        result["width"] = width;
        result["height"] = height;
        result["cellCount"] = cells.Length;
        result["zonesUsed"] = cells.Select(c => c.Zone).Distinct().Count();
        result["cellsWithEvents"] = cells.Count(c => c.EventExists);
        return result;
    }
}
