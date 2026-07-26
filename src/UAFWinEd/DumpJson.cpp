/******************************************************************************
* Filename: DumpJson.cpp
*
* "Oracle" mode for the .NET port. Loads a design through the normal editor code
* path and writes every parsed structure to a file as canonical JSON, so the C#
* reimplementation in dotnet/ can be validated by diffing against this reference
* implementation on identical inputs. See docs/PORTING-PLAN.md.
*
* Invoked as:  UAFWinEd.exe -config <config.txt> -dumpjson <out.json>
* The design is loaded, dumped, and the process exits without creating a window.
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

#include "externs.h"
#include "GlobalData.h"
#include "Items.h"
#include "Monster.h"
#include "Spell.h"
#include "Level.h"

#include <fstream>
#include <string>
#include <json.hpp>

using json = nlohmann::json;

extern const double PRODUCT_VER;
extern const double ENGINE_VER;

// CString -> std::string without transcoding. MBCS build, so this is a byte copy.
static std::string S(const CString& s)
{
  return std::string((LPCSTR)s, s.GetLength());
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
  j["useAreaView"]                = globalData.useAreaView ? true : false;

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

  j["autoDarkenViewport"]         = globalData.AutoDarkenViewport ? true : false;
  j["autoDarkenAmount"]           = globalData.AutoDarkenAmount ? true : false;
  j["startDarken"]                = globalData.StartDarken;
  j["endDarken"]                  = globalData.EndDarken;

  j["mapArt"]                     = S(globalData.m_MapArt);
  j["iconBgArt"]                  = S(globalData.IconBgArt);
  j["backgroundArt"]              = S(globalData.BackgroundArt);
  return j;
}

// Database record counts. This is deliberately a first increment: it proves the
// load -> dump -> golden-file loop end to end and will catch a wrong record count
// (the most common symptom of a mis-parsed container) before any per-record work.
// Per-record dumps get added here as each type is ported.
static json DumpDatabaseCounts(void)
{
  json j;
  j["items"]    = itemData.GetCount();
  j["monsters"] = monsterData.GetCount();
  j["spells"]   = spellData.GetCount();
  return j;
}

bool DumpDesignJson(const CString& outPath)
{
  json root;

  // Provenance: which binary produced this, and from what. Without this a stale
  // golden file is indistinguishable from a current one.
  json meta;
  meta["productVersion"] = D(PRODUCT_VER);
  meta["engineVersion"]  = D(ENGINE_VER);
  meta["designPath"]     = S(rte.DesignDir());
  meta["dataPath"]       = S(rte.DataDir());
  // The design's own format version drives all ~472 version gates, so record it at
  // top level. Note Level.cpp:3340: the editor is NOT reliable for designs in
  // [0.998101, 0.9988], so a golden file in that range is not trustworthy ground truth.
  meta["designVersion"]  = D(globalData.version);
  root["_meta"]          = meta;

  root["globalData"]     = DumpGlobalData();
  root["counts"]         = DumpDatabaseCounts();

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
