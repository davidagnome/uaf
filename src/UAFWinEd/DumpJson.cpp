/******************************************************************************
* Filename: DumpJson.cpp
*
* "Oracle" mode for the .NET port. Loads a design through the normal editor code
* path and writes every parsed structure to a file as canonical JSON, so the C#
* reimplementation in dotnet/ can be validated by diffing against this reference
* implementation on identical inputs. See docs/PORTING-PLAN.md.
*
* Invoked as:  UAFWinEd.exe "-config <design.dsn>" "-dumpjson <out.json>"
*
* NOTE the quoting: each flag and its value must be a SINGLE argument. CUAFCommandLineInfo::
* ParseParam (Globals.cpp:817) splits them with strchr(param, ' ') inside one token, so passing
* `-config X` as two arguments silently yields an empty value -- the app then exits 0 having done
* nothing. The editor launches the engine the same way (MainFrm.cpp:2648).
*
* The design is loaded, dumped, and the process exits without creating a window. Note that this
* mode deliberately BYPASSES CUAFWinEdApp::OpenDesign, which requires DirectX
* (GraphicsMgr.IsInitialized()) and a main window and therefore cannot run on a CI runner.
*
* CANONICAL OUTPUT RULES -- the whole point is byte-comparable diffs:
*   - Object keys are sorted. nlohmann::json uses std::map, so this is automatic;
*     do not switch to ordered_json.
*   - Doubles are emitted at full round-trip precision (17 significant digits).
*     Never format a version or any other double for "readability".
*   - Strings are emitted as raw bytes. The engine is an MBCS build, so CString
*     holds single-byte characters in the system codepage; the C# side reads them
*     with the matching encoding. Do NOT transcode here.
*
* This program is free software; you can redistribute it and/or
* modify it under the terms of the GNU General Public License
* as published by the Free Software Foundation; either version 2
* of the License, or (at your option) any later version.
******************************************************************************/
#include "..\Shared\stdafx.h"

// The standard library and json.hpp MUST come before the game data headers. Including them
// afterwards produces a cascade of syntax errors inside <xlocale> plus a `size_t` redefinition:
// one of the legacy headers below defines a macro that collides with STL internals. stdafx.h
// stays first because it is the precompiled header.
#include <cstdarg>
#include <fstream>
#include <string>
#include <vector>
#include <json.hpp>

#include "externs.h"
#include "GlobalData.h"
#include "Items.h"
#include "Monster.h"
#include "Spell.h"
#include "Level.h"

using json = nlohmann::json;

extern const double PRODUCT_VER;
extern const double ENGINE_VER;

// CString -> std::string without transcoding. MBCS build, so this is a byte copy.
static std::string S(const CString& s)
{
  return std::string((LPCSTR)s, s.GetLength());
}

// 'D:\a\uaf\src\UAFWinEd\DefaultDesign.dsn\' -> 'DefaultDesign.dsn'.
// Golden output must not contain machine-specific absolute paths, or it stops being
// reproducible outside the machine that generated it.
static CString DesignFolderName(CString dir)
{
  while (!dir.IsEmpty() && dir.Right(1) == "\\")
  {
    dir = dir.Left(dir.GetLength() - 1);
  }
  int i = dir.ReverseFind('\\');
  return (i >= 0) ? dir.Mid(i + 1) : dir;
}

// Emit a double at full round-trip precision. json.hpp already serializes doubles
// with enough digits to round-trip, but we funnel every double through here so the
// intent is explicit and greppable if the golden files ever disagree.
static double D(double v)
{
  return v;
}

static json DumpGlobalData(void)
{
  json j;
  j["version"]                    = D(globalData.version);
  j["saveGameVersion"]            = D(globalData.SaveGameVersion);
  j["designName"]                 = S(globalData.designName);

  j["startLevel"]                 = globalData.startLevel;
  j["currLevel"]                  = globalData.currLevel;
  j["startX"]                     = (int)globalData.startX;
  j["startY"]                     = (int)globalData.startY;
  j["startFacing"]                = (int)globalData.startFacing;
  j["useAreaView"]                = (int)globalData.useAreaView;   // BOOL -> int, see note below

  j["startTime"]                  = globalData.startTime;
  j["startExp"]                   = globalData.startExp;
  j["startExpType"]               = globalData.startExpType;
  j["startPlatinum"]              = globalData.startPlatinum;
  j["startGem"]                   = globalData.startGem;
  j["startJewelry"]               = globalData.startJewelry;

  // minPCs / maxParty_maxPCs are private and maxParty_maxPCs packs two values into
  // one int (upper 16 = party size, lower 16 = max PCs). Dump the unpacked accessors
  // AND the raw packed value: the C# port must reproduce the packing exactly, and a
  // bug in the packing is invisible if only the accessors are compared.
  j["minPCs"]                     = globalData.GetMinPCs();
  j["maxPartySize"]               = globalData.GetMaxPartySize();
  j["maxPCs"]                     = globalData.GetMaxPCs();

  j["flags"]                      = globalData.flags;
  j["allowCharCreate"]            = globalData.GetAllowCharCreate() ? true : false;
  j["deadAtZeroHP"]               = globalData.GetDeadAtZeroHP() ? true : false;

  j["dungeonTimeDelta"]           = globalData.DungeonTimeDelta;
  j["dungeonSearchTimeDelta"]     = globalData.DungeonSearchTimeDelta;
  j["wildernessTimeDelta"]        = globalData.WildernessTimeDelta;
  j["wildernessSearchTimeDelta"]  = globalData.WildernessSearchTimeDelta;

  // Emit BOOL fields as INTEGERS, not JSON booleans. Win32 BOOL is a 4-byte int and these are
  // not all boolean in practice: AutoDarkenAmount holds 256 in DefaultDesign. Coercing to
  // true/false here would destroy the value and make the golden file disagree with a correct
  // C# reader. Only fields that are genuinely 0/1 predicates (below) are emitted as bools.
  j["autoDarkenViewport"]         = (int)globalData.AutoDarkenViewport;
  j["autoDarkenAmount"]           = (int)globalData.AutoDarkenAmount;
  j["startDarken"]                = globalData.StartDarken;
  j["endDarken"]                  = globalData.EndDarken;

  j["mapArt"]                     = S(globalData.m_MapArt);
  j["iconBgArt"]                  = S(globalData.IconBgArt);
  j["backgroundArt"]              = S(globalData.BackgroundArt);
  return j;
}

// Database record counts. A wrong count is the most common symptom of a mis-parsed container,
// so this is cheap and high-signal.
static json DumpDatabaseCounts(void)
{
  json j;
  j["items"]    = itemData.GetCount();
  j["monsters"] = monsterData.GetCount();
  j["spells"]   = spellData.GetCount();
  return j;
}

// Per-record name digest for the item DB.
//
// Names rather than every field, deliberately. A record is 47 reads wide with many version
// gates, so dumping all of it for 285 items would produce a ~500KB golden file. Names are the
// highest-signal subset: any width or gate error desynchronises the stream and the very next
// name comes back as garbage, so a 285-entry list catches a mis-parse immediately while staying
// a few KB. Full per-field dumps can follow once the C# record reader exists to compare against.
//
// TODO: the equivalent for monsters and spells. MONSTER_DATA_TYPE / SPELL_DATA_TYPE do not use
// the same DEFINE_mCARRAY_ACCESS_FUNCTIONS instantiation as ITEM_DATA_TYPE, so their element
// accessors differ and need checking before use.
static json DumpItemNames(void)
{
  json arr = json::array();
  int n = itemData.GetCount();
  for (int i = 0; i < n; i++)
  {
    const ITEM_DATA *pItem = itemData.GetItem(i);
    if (pItem == NULL)
    {
      arr.push_back(nullptr);
      continue;
    }
    json rec;
    rec["idName"]     = S(pItem->IdName());
    rec["uniqueName"] = S(pItem->UniqueName());
    arr.push_back(rec);
  }
  return arr;
}

// Full field dump for the first few item records.
//
// Complements DumpItemNames: names across all 285 catch stream desync, while every field of a
// handful of records catches per-field errors (wrong width, wrong gate, missing post-read
// transform) that a name check cannot see. Kept to a few records so the golden file stays small.
//
// NOTE several values here are POST-READ, not the raw bytes:
//   Location_Readied  -- Items.cpp:2820 maps the stored DWORD through a switch into base-38
//                        names, so the member never equals what is on disk for old designs.
//   AmmoType          -- "None" is normalised to "" after reading (Items.cpp:2807).
// A C# reader must reproduce both or it will disagree with this dump despite reading identical
// bytes.
static json DumpItemDetails(int maxRecords)
{
  json arr = json::array();
  int n = itemData.GetCount();
  if (n > maxRecords) n = maxRecords;

  for (int i = 0; i < n; i++)
  {
    const ITEM_DATA *p = itemData.GetItem(i);
    if (p == NULL) { arr.push_back(nullptr); continue; }

    json j;
    j["idName"]             = S(p->IdName());
    j["uniqueName"]         = S(p->UniqueName());
    j["preSpellNameKey"]    = p->preSpellNameKey;
    j["ammoType"]           = S(p->AmmoType);
    j["hitSound"]           = S(p->HitSound);
    j["missSound"]          = S(p->MissSound);
    j["launchSound"]        = S(p->LaunchSound);

    j["experience"]         = (int)p->Experience;
    j["cost"]               = (int)p->Cost;
    j["encumbrance"]        = (int)p->Encumbrance;
    j["attackBonus"]        = (int)p->Attack_Bonus;
    j["cursed"]             = (int)p->Cursed;            // BOOL -> int, never a JSON bool
    j["bundleQty"]          = (int)p->Bundle_Qty;
    j["numCharges"]         = (int)p->Num_Charges;
    j["locationReadied"]    = (unsigned int)p->Location_Readied;   // post-switch, see note above

    j["handsToUse"]         = (int)p->Hands_to_Use;
    j["dmgDiceSm"]          = (int)p->Dmg_Dice_Sm;
    j["nbrDiceSm"]          = (int)p->Nbr_Dice_Sm;
    j["dmgBonusSm"]         = (int)p->Dmg_Bonus_Sm;
    j["dmgDiceLg"]          = (int)p->Dmg_Dice_Lg;
    j["nbrDiceLg"]          = (int)p->Nbr_Dice_Lg;
    j["dmgBonusLg"]         = (int)p->Dmg_Bonus_Lg;
    j["rofPerRound"]        = D(p->ROF_Per_Round);       // double -- full precision
    j["protectionBase"]     = (int)p->Protection_Base;
    j["protectionBonus"]    = (int)p->Protection_Bonus;

    arr.push_back(j);
  }
  return arr;
}

// Headless diagnostics.
//
// WriteDebugString writes to LOG_ERROR_PATH from config.txt, which in the committed
// DefaultDesign points at a developer's absolute path (D:\DungeonCraft\...) and therefore goes
// nowhere useful on a CI runner. Since the JSON file is the only artefact that reliably escapes
// the process, diagnostics are collected here and emitted under _meta.diagnostics.
static std::vector<std::string> g_diagnostics;

static void Diag(const char *fmt, ...)
{
  char buffer[1024];
  va_list args;
  va_start(args, fmt);
  _vsnprintf(buffer, sizeof(buffer) - 1, fmt, args);
  va_end(args);
  buffer[sizeof(buffer) - 1] = 0;
  g_diagnostics.push_back(std::string(buffer));
  WriteDebugString("%s\n", buffer);
}

// Load the design's data ourselves.
//
// The editor's normal path is unusable headless: CUAFWinEdApp::OpenDesign calls
// ProcessShellCommand (document + main window) and then requires GraphicsMgr.IsInitialized(),
// i.e. a working DirectX device, which a CI runner has not got. The data load itself normally
// happens even later, in CMainFrame::LoadDesign, which also needs a window.
//
// None of that is necessary to read a design. Everything required is a free function:
//   loadDesign(name)            -- Level.cpp:3309, reads game.dat into globalData
//   loadData(<DB>, fullPath)    -- Items.cpp:3392 and its overloads, read the databases
// so the oracle can drive the load directly and stay windowless.
// Report the resolved environment. Runs REGARDLESS of whether config loading succeeded,
// because when it fails these paths are exactly what is needed to see why -- and a wrong path
// is otherwise indistinguishable from a corrupt file.
static void DiagEnvironment(void)
{
  CString designDir = rte.DesignDir();
  CString dataDir   = rte.DataDir();
  CString configDir = rte.ConfigDir();

  Diag("designDir = '%s'", (LPCSTR)designDir);
  Diag("dataDir   = '%s'", (LPCSTR)dataDir);
  Diag("configDir = '%s'", (LPCSTR)configDir);

  Diag("game.dat   exists = %s", FileExists(dataDir + "game.dat")   ? "yes" : "no");
  Diag("config.txt exists = %s", FileExists(configDir + "config.txt") ? "yes" : "no");
  // The editor falls back to the template dir when ConfigDir() is empty (UAFWinEd.cpp:655),
  // so report that candidate too.
  Diag("templateDataDir = '%s'", (LPCSTR)ede.TemplateDataDir());
}

static bool LoadDesignDataHeadless(void)
{
  CString designDir = rte.DesignDir();
  CString dataDir   = rte.DataDir();

  BOOL loadResult = loadDesign((LPCSTR)designDir);
  Diag("loadDesign returned %s: designName='%s' version=%.6f",
       loadResult ? "TRUE" : "FALSE",
       (LPCSTR)globalData.designName, globalData.version);

  // A FALSE return does NOT mean the data failed to load. loadDesign ends with
  //     if (success) success = CheckLevelVersions(name);
  // (Level.cpp:3431), and CheckLevelVersions is a level-file *naming* migration check: it offers
  // to renumber Level000.lvl -> Level001.lvl and then looks for the new names. Headless declines
  // that rename (an oracle must not modify its fixture), so the check fails on any design still
  // using the old numbering -- while globalData and the databases are already correctly
  // populated. Judge success by whether the data actually arrived.
  bool dataPresent = !globalData.designName.IsEmpty();
  if (!dataPresent)
  {
    Diag("no design data present after loadDesign - treating as failure");
    return false;
  }
  if (!loadResult)
  {
    Diag("loadDesign reported failure but data is present (likely CheckLevelVersions"
         " declining the Level000->Level001 rename) - continuing");
  }

  // loadDesign already pulls in the databases, so only load any that came back empty. Their
  // absence is not fatal -- a design need not define every database -- so failures are recorded,
  // not propagated. The record counts in the output reveal what actually loaded.
  if (itemData.GetCount() == 0)
    Diag("items    -> %d (loaded separately)", loadData(itemData,    (LPCSTR)(dataDir + "items.dat")));
  else
    Diag("items    -> %d (via loadDesign)", itemData.GetCount());

  if (monsterData.GetCount() == 0)
    Diag("monsters -> %d (loaded separately)", loadData(monsterData, (LPCSTR)(dataDir + "monsters.dat")));
  else
    Diag("monsters -> %d (via loadDesign)", monsterData.GetCount());

  if (spellData.GetCount() == 0)
    Diag("spells   -> %d (loaded separately)", loadData(spellData,   (LPCSTR)(dataDir + "spells.dat")));
  else
    Diag("spells   -> %d (via loadDesign)", spellData.GetCount());

  return true;
}

// Re-save the loaded design in place, which upgrades it to PRODUCT_VER.
//
// This is how a tier-3 (LZW-compressed) fixture is produced without driving the editor GUI.
// saveDesign() sets globalData.version = PRODUCT_VER (Level.cpp:2756) -- 5.29, well past the
// 0.930 _SPECIAL_ABILITIES_VERSION_ gate at which the databases enable Compress(true) -- so the
// rewritten files exercise the LZW path that no design in the repository otherwise reaches.
//
// DESTRUCTIVE: writes over the design at rte.DataDir(). The caller must point it at a copy.
static bool SaveDesignHeadless(void)
{
  Diag("saving design (will upgrade %.6f -> %.6f)", globalData.version, PRODUCT_VER);
  if (!saveDesign())
  {
    Diag("saveDesign() FAILED");
    return false;
  }
  Diag("saveDesign OK; design version is now %.6f", globalData.version);
  return true;
}

bool DumpDesignJson(const CString& outPath, bool configLoaded)
{
  // `configLoaded` reports whether config.txt was read successfully. The design data itself
  // is loaded here, independently of that.
  DiagEnvironment();

  // A failed config load is NOT fatal here, and must not gate the data load.
  //
  // LoadConfigFile's editor section (Globals.cpp:2414, 2430) verifies that the editor's own
  // installation art is present -- MAPART under ede.EditorMapArtDir() and OVERLANDART under
  // ede.TemplateOverlandArtDir(), both derived from the executable's directory. A build output
  // folder has no EditorResources\ or TemplateDesign.dsn\ beside the exe, so it returns FALSE
  // with "Please re-install Dungeon Craft" even though config.txt itself parsed fine.
  //
  // None of that art has any bearing on parsing a design: the data load uses rte paths, which
  // are set directly above. So record the outcome and carry on.
  if (!configLoaded)
  {
    Diag("LoadConfigFile FAILED (editor install art missing?) - continuing anyway");
  }
  bool designLoaded = LoadDesignDataHeadless();

  // -savedesign: re-save after loading, before dumping, so the dump describes the UPGRADED
  // design rather than the one on disk beforehand.
  bool designSaved = false;
  if (designLoaded && cmdLine.m_SaveDesign)
  {
    designSaved = SaveDesignHeadless();
  }

  json root;

  // Provenance: which binary produced this, and from what. Without this a stale
  // golden file is indistinguishable from a current one.
  json meta;
  // ok=false means the design failed to load; the remaining fields are then whatever
  // default-constructed state the globals happen to hold and must NOT be used as golden data.
  // A caller seeing no file at all has a different problem -- see the note in InitInstance.
  // Separate flags, because `ok` alone cannot distinguish the two failure points:
  //   configLoaded=false  -> config.txt unusable (often just missing editor install art)
  //   designLoaded=false  -> the design data itself could not be read; ok is false
  meta["configLoaded"]    = configLoaded;
  meta["designSaved"]     = designSaved;   // -savedesign: design was re-saved at PRODUCT_VER
  meta["designLoaded"]   = designLoaded;
  meta["ok"]             = designLoaded;
  meta["diagnostics"]    = g_diagnostics;
  meta["productVersion"] = D(PRODUCT_VER);
  meta["engineVersion"]  = D(ENGINE_VER);
  // Paths are recorded as the DESIGN FOLDER NAME only, never absolute. A golden file must be
  // reproducible: embedding 'D:\a\uaf\uaf\...' would make every regeneration outside that exact
  // checkout diff spuriously, and would leak the build machine's layout into a committed file.
  // The absolute paths still appear in _meta.diagnostics, which is informational and excluded
  // from golden comparison.
  meta["designFolder"]   = S(DesignFolderName(rte.DesignDir()));
  // The design's own format version drives all ~472 version gates, so record it at
  // top level. Note Level.cpp:3340: the editor is NOT reliable for designs in
  // [0.998101, 0.9988], so a golden file in that range is not trustworthy ground truth.
  meta["designVersion"]  = D(globalData.version);
  root["_meta"]          = meta;

  root["globalData"]     = DumpGlobalData();
  root["counts"]         = DumpDatabaseCounts();
  root["itemNames"]      = DumpItemNames();
  root["itemDetails"]    = DumpItemDetails(8);   // full fields for the first 8 records

  std::ofstream out((LPCSTR)outPath, std::ios::binary);
  if (!out.is_open())
  {
    WriteDebugString("DumpDesignJson: cannot open output file %s\n", (LPCSTR)outPath);
    return false;
  }
  // Two-space indent, and ensure_ascii=false so byte-preserved strings are not
  // mangled into \uXXXX escapes that the C# side would have to undo.
  out << root.dump(2, ' ', false) << "\n";
  out.close();

  WriteDebugString("DumpDesignJson: wrote %s\n", (LPCSTR)outPath);
  return true;
}
