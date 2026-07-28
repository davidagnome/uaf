# Dungeon Craft → .NET 10 / Avalonia Porting Plan

**Targets:** `UAFWin` → **UAFcore** (game engine + player), `UAFWinEd` → **UAFedit** (design editor)
**Stack:** .NET 10, C#, Avalonia 11.x (editor) + SDL3 (game), cross-platform (Windows / macOS / Linux)
**Status:** Phase 0 complete — reference build green, oracle diffing armed both ways. Phase 1 in progress
**Date:** 2026-07-26

---

## 1. Executive summary

Dungeon Craft is ~398,000 lines of C++ split across four modules, hard-bound to three
Windows-only technologies: **MFC** (UI, containers, strings, serialization), **DirectDraw 7**
(via the vendored CDX library), and **BASS + DirectSound + Video for Windows** (audio/video).
There is **no test coverage**.

The port is not a refactor — it is a reimplementation against a binary file format that must
stay byte-compatible, because existing designs (and the FRUA imports the project exists to
support) are the product. That single constraint drives the entire plan.

The strategy has three parts:

1. **Build an oracle first.** Get the C++ tree building on Windows CI and add a dumper tool that
   emits every parsed structure as JSON. Every C# layer is then validated by diffing against the
   C++ output on the same inputs. Without this, a byte-exact port of a multi-revision
   undocumented format is guesswork.
2. **Translate faithfully where fidelity is the product** — serialization, data model, rules,
   the GPDL and Forth VMs. Keep the code shape, keep the globals, resist the urge to redesign.
3. **Redesign freely where the old shape has no value** — UI (MFC dialogs → Avalonia MVVM),
   rendering (DirectDraw surfaces → managed framebuffer), audio (BASS → managed backend).

Estimated effort: **18–26 engineer-months** for full feature parity. Phasing below is ordered so
that something demonstrable exists after ~4 months, not after 2 years.

---

## 2. What exists today

### 2.1 Inventory

| Module | Files | Lines | Role | Disposition |
|---|---:|---:|---|---|
| `src/Shared` | 80 | 198,279 | Data model, rules, serialization, GPDL, graphics/sound facades | Port (minus vendored) |
| `src/UAFWin` | 50 | 82,446 | Game engine: events, combat, rendering, input | Port → **UAFcore** |
| `src/UAFWinEd` | 271 (136 `.cpp`) | 86,831 | MFC design editor | Port → **UAFedit** |
| `src/cdx` | 62 | 30,143 | CDX — 3rd-party DirectDraw 7 wrapper | **Discard**, replace |
| `src/GPDL` | 1 | 112 | GPDL compiler CLI driver | Port → `gpdlc` tool |
| **Total** | | **397,811** | | |

Vendored code inside `Shared` that gets replaced by NuGet rather than ported: `zlib.h`,
`png_1_0_8.h` / `pngconf*.h`, `regexp.cpp` (4,494 lines), `bass.h`, `Stackwalker.cpp` — roughly
12,000 lines. Combined with discarding CDX, the **actual hand-port surface is ~356,000 lines**.

There are also **104 `.bak` / `.Old` files** in the tree. All `Shared/*.cpp` files are referenced
by at least one project, so the `.bak` files are pure noise — exclude them from any analysis.

### 2.2 Largest single files (the real cost centres)

```
UAFWin/RunEvent.cpp        27,639   event dispatch — the hardest file in the codebase
Shared/Char.cpp            18,635   character model + serialization
Shared/GameEvent.cpp       15,056   116 GameEvent subclasses
Shared/class.cpp           12,400   core containers + the CAR archive
UAFWin/Combatant.cpp       11,694   combat entity
Shared/Spell.cpp           10,506   spell model
UAFWinEd/ItemDB.cpp         9,000   item database editor
UAFWin/Combatants.cpp       8,952   combat resolution loop
Shared/GPDLexec.cpp         8,377   GPDL bytecode VM
UAFWinEd/UAImport.cpp       7,036   DOS FRUA importer
```

### 2.3 Platform coupling

- **MFC, statically linked** (`UseOfMfc=Static`, `CharacterSet=MultiByte`).
  `CString` appears in 250 files, `CWnd` in 251, `CDialog` in 237, `CArray` in 52,
  `CList` in 74, `CMap` in 13, `CArchive` in 46, `CFile` in 30.
- **DirectDraw 7** via CDX (`ddraw.lib`, `dxguid.lib`); `LPDIRECTDRAW*` in 25 files.
- **Audio**: `bass.lib` + `dsound.lib` + `winmm` MIDI; three background threads
  (`SoundQueue`, `MIDIAsyncPlayer`, `BackgroundSoundQueue`) built on `AfxBeginThread`.
- **Video**: `vfw32.lib` / `amstrmid.lib` for `PlayMovie` (12 call sites — a minor feature).
- **Threading**: `Shared/Thread.h` wraps `AfxBeginThread`; `SharedQueue.h` uses raw
  `CRITICAL_SECTION`.
- **Toolset**: `v140_xp` / `v141_xp` — XP-targeting toolsets, unavailable in current Visual Studio.
- **Globals**: `Shared/Externs.h` declares **105 `extern`s**, including the mutable singletons
  `globalData`, `levelData`, `party`, `itemData`, `monsterData`, `rte`, `ede`.

### 2.4 License note

The project is **GPL v2**. BASS is proprietary and not GPL-compatible; linking them is a standing
license problem in the current build. Replacing BASS with a permissively-licensed managed backend
is a license fix as well as a portability fix.

---

## 3. The version scheme (and a dead header that misrepresents it)

The live version constants are defined in **`Shared/Globals.cpp:93–127`**:

```cpp
extern const int    CHARACTER_VERSION  = 0x80000001;  // separate integer scheme
extern const double VersionSpellIDs    = 0.998100;
extern const double VersionSpellNames  = 0.998101;
extern const double VersionSaveIDs     = 0.998914;
extern const double PRODUCT_VER        = 5.29;        // editor
extern const double ENGINE_VER         = 5.29;        // engine
```

**`Shared/ProjectVersion.h` is dead code.** It is wrapped entirely in a `/* … */` block, every
`#include` of it is commented out, and its constants stop at `0.998110`. It is stale by several
years and must not be used as a reference — restoring it would set `PRODUCT_VER` *below* the
version the shipped editor writes, and `Level.cpp:3348` refuses to load designs newer than
`PRODUCT_VER`. **Delete it rather than port it.**

A third location holds the bulk of them: **`Shared/Externs.h:49–191` defines 92 version macros**
spanning `_VERSION_0500_` (0.500) through `_VERSION_529` (5.29), plus derived aliases
(`_ITEM_DB_VERSION_`, `_SPELL_DB_VERSION_`, `_MONSTER_DB_VERSION_`, `_SPECIAL_ABILITIES_VERSION_`
= 0.930, `_CELL_CONTENTS_VERSION` = 5.0).

**Actual scale: 97 version constants and 472 live version-gate comparison sites.**

Four consequences for the port:

1. **The axis is monotonic but spans two orders of magnitude** — `0.500 → 0.930 → 0.998914 →
   5.24 → 5.29`. Ordering comparisons remain valid, but any logic assuming `version < 1.0` —
   range checks, "is this plausible?" validation, formatting with `%4.7f` — is wrong.
   `DesignVersion` must be an opaque comparable value with no assumed range.
2. **Version constants live in three places** with two different mechanisms: `#define` macros in
   `Externs.h`, `extern const double` in `Globals.cpp`, and the dead `ProjectVersion.h`. The port
   should consolidate all live ones into a single `DesignVersion` static class and delete the
   dead header — but must consolidate by *transcription*, not by re-deriving values.
3. **`CHARACTER_VERSION` is a separate integer scheme** (`0x80000001`), unrelated to the double.
   Do not unify them.
4. **At least one gate is undocumented in code.** The comment at `Globals.cpp:113` describes
   behaviour "starting with 0.998915" — the editor offering to back up and upgrade old levels —
   with no corresponding constant. Expect more of these; the oracle is how they get found.

### 3.1 Binary analysis as a recovery technique

`VersionSaveIDs = 0.998914` was independently recovered from the shipped `UAFWinEd.exe` before the
source definition was located, by scanning for IEEE-754 doubles in `0.9 ≤ v < 1.0` whose decimal
expansion terminates within six places, then reading MSVC's per-function constant pools — the pool
at `0x4084a8` holds exactly `{0.998101, 0.998100, 0.998914}`, matching the three constants
referenced by `Monster.cpp:671` / `Items.cpp:2753` / `Spell.cpp:3878`.

This technique works and is worth keeping: where the C++ source is ambiguous about a format
detail, the shipped binaries are an independent oracle. The same scan found version constants
present in the binaries but absent from the source header entirely (`0.998115`, `0.998400`,
`0.998800`, `0.998917`, `0.998918`, `0.999432`, `0.999647`, `0.999680`, `0.999702`, `0.999725`),
which is a standing hint that the shipped binaries post-date parts of the committed source.

> The PE `VERSIONINFO` resources (`5.2.1.0` engine / `5.2.4.0` editor) are marketing strings and
> bear no relation to the internal doubles. Don't conflate them.

### 3.2 Container layout: four framings

Verified against `src/UAFWinEd/DefaultDesign.dsn/Data/` and the read/write paths in
`Level.cpp`, `Items.cpp`, `Spell.cpp`, `Monster.cpp`, and `Char.cpp`.

**Shape 1 — versioned prologue** (`game.dat`, `items.dat`, `monsters.dat`, `spells.dat`, `*.lvl`).
The prologue is written with **raw `CFile::Write`, outside the archive**, and compression is
enabled only afterwards (`Monster.cpp:1300`):

```cpp
__int64 hdr = 0xFABCDEFABCDEFABF;   // little-endian: BF FA DE BC FA DE BC FA
double  ver = PRODUCT_VER;
myFile.Write(&hdr, sizeof(hdr));    // raw — not through CAR
myFile.Write(&ver, sizeof(double)); // raw — not through CAR
CAR ar(&myFile, CArchive::store);
ar.Compress(true);                  // LZW begins here
```

Reading mirrors it, **with a per-type fallback when the magic is absent** (`Level.cpp:2151`):

```cpp
__int64 hdr = 0;
myFile.Read(&hdr, sizeof(hdr));
if (hdr == 0xFABCDEFABCDEFABF) { myFile.Read(&ver, sizeof(double)); }  // payload at 16
else { myFile.SeekToBegin(); ver = _VERSION_0572_; }                   // payload at 0
```

The fallback constant **differs by file type** — `_VERSION_0572_` (0.572) for level/game data,
`_VERSION_0563_` (0.563) for character files (`Char.cpp:6948`). Do not share one constant.

**Shape 2 — self-describing tag** (`ability.dat`, `baseclass.dat`, `classes.dat`, `races.dat`,
`spellgroups.dat`, `traits.dat`). A counted type tag first — `"AbilityV1"`, `"BaseclassV1"`,
`"ClassV1"`, `"RaceV1"`, `"SpGrpV1"`, `"TraitV1"` — read while still uncompressed, then a
compression-type byte, then an LZW stream containing the count and the records
(`class.cpp:3489`):

```cpp
car >> version;                       // a STRING tag -- no magic, no version double
if (version > "RaceV0")               // LEXICOGRAPHIC comparison gates compression
    car.Compress(true);
count = car.ReadCount();              // read from INSIDE the compressed stream
for (count) data.Serialize(car, version);   // records take the STRING version
```

Three things are unique to this framing: the version is a **string**, the compression gate is a
**string comparison** rather than a numeric one, and the `DesignVersion` machinery does not apply
at all — modelling these files with it is a category error.

> **`compressType` is 1 here, not 2.** `CAR::Compress(true)` always *writes* 2
> (`class.cpp:11670`), yet every tagged database on disk carries **1** — an older variant still in
> circulation. It is not cosmetic: the string reader gates its embedded-NUL check on
> `m_compressType > 1` (`class.cpp:11975`), so type-1 streams **intern** NUL-bearing strings that
> type-2 streams skip. Get that wrong and every later string-table index shifts.

Verified by decompressing all six: **6 abilities, 7 baseclasses, 19 classes, 6 races, 15
spellgroups, 43 traits**. The first three are decisive rather than merely plausible — six AD&D
ability scores, and seven baseclasses matching exactly the seven experience fields
`CHARACTER::Serialize` reads (Fighter, Cleric, Ranger, Paladin, MU, Thief, Druid).

**Orthogonal to both: the archive layer is version-selected, in three tiers, with per-file-type
thresholds.** This is the single easiest thing in the format to get wrong. There are **three
distinct loaders** and they agree on nothing:

| | Loader | Unstamped version | → `CAR` at | → LZW at |
|---|---|---|---|---|
| `game.dat` | `loadDesign(LPCSTR)` `Level.cpp:3341` | **read from offset 0** (see below) | **0.998101** | — |
| `*.lvl` | `LoadLevel` `Level.cpp:2151` | literal **0.572** | **0.573** | — |
| databases | `loadData` `Items.cpp:3405` | `min(globalData.version, 0.696)` | **0.697** | **0.930** |

**`game.dat` has no fallback constant at all.** `GetDesignVersion` (`Globals.cpp:3460`) reads the
magic and, when it is absent, seeks back to 0 and reads the `double` there anyway — so for an
unstamped file the container version *is* the payload's own first field, read twice: once to pick
the archive, once by `GLOBAL_STATS::Serialize`.

**And `game.dat` does not use the container model at all.** Unlike the databases, its prologue is
read by the *payload* reader, and compression is switched on **mid-stream**
(`GlobalData.cpp:4336`):

```cpp
car.Serialize((char*)&temp, sizeof(temp));      // GLOBAL_STATS reads the magic ITSELF
if (temp == 0xFABCDEFABCDEFABF) {
    car >> version;                             // version, uncompressed
    car.Compress(true);                         // compression starts HERE
    car >> version;                             // the SAME version again, now compressed
}
else version = (double)temp;                    // no magic: those 8 bytes ARE the version
DAS(car, designName);
```

So a modern `game.dat` is laid out:

```
[0..7]   magic                       uncompressed
[8..15]  version                     uncompressed
[16]     compressType byte           uncompressed
[17..]   version AGAIN, designName, … LZW-compressed
```

This resolves two things that looked like bugs: why `loadDesign` never seeks past the magic (the
payload reader consumes it), and why the version appears twice. Treating the prologue as a plain
container header parses the design name as binary noise — which is precisely how `uaf-fileprobe`
found it, having produced `'P@\x05\x00\x02…'` for every design at or above 2.53 while the
databases in the same folder read perfectly.

> **Correction.** An earlier revision of this document attributed the 0.572 fallback and 0.573
> gate to "level/game data" collectively. That rule belongs to `*.lvl` only. The two disagree
> across all of `[0.573, 0.998101)` — which *includes* `DefaultDesign`'s 0.915025, where a level
> file is already a `CAR` while `game.dat` is still a plain archive. Using one kind for both is a
> latent mis-parse: it happened not to break the `game.dat` reader only because nothing consumed
> the tier on that path.

Two consequences:

1. **"Has the magic" does not imply "is compressed."** `DefaultDesign`'s `items.dat`,
   `monsters.dat`, `spells.dat` and `Level000.lvl` all carry the magic and are version 0.915025 —
   tier 2, so `CAR` *without* compression. Confirmed empirically: the byte after the 16-byte
   prologue is not a compression marker (it reads 0x1d / 0x2c / 0x75 / 0x0a across the four
   files), because `Compress(true)` was never called and so no compression-type byte was written.
2. **The unstamped fallback is not always a constant.** `Level.cpp:2163` uses a literal
   `_VERSION_0572_`, but `Items.cpp:3418` computes `ver = min(globalData.version, _VERSION_0696_)`
   — it depends on already-loaded global state. Load order therefore matters: `game.dat` must be
   read before the databases, or the databases get the wrong version.

Each type's thresholds must be transcribed from its own loader. Do not generalise one file's
constants to another.

**Tier 2 needs no new reader.** Every `CAR` operator opens with
`if (m_compressType == 0) { ar << …; }`, delegating straight to the wrapped `CArchive`
(`class.cpp:11707`, `:11722`, `:11940`). String interning and LZW live only on the `else` branch,
which `Compress(true)` must enable. So tier 2 is **byte-identical to tier 1** at the primitive
level — the same reader serves both, and only the payload offset differs. Only tier 3 requires
the LZW decoder.

Verified: reading `DefaultDesign`'s tier-2 payloads at offset 16 with the plain reader yields
**285 items, 44 monsters, 117 spells** — plausible counts, corroborated by the implied record
sizes (~1900–2200 bytes each). These are the same three values the oracle dumper emits under
`counts.*`, making them the first end-to-end agreement point between the two implementations.

### The LZW layer (tier 3)

`CAR::decompress` (`class.cpp:12215`) is **not** interchangeable with a stock LZW implementation:

- Codes are **13 bits**, packed into fixed **52-byte blocks** — 416 bits, exactly 32 codes per
  block, no remainder and no padding.
- Code **8190** resets the dictionary; code **8191** terminates. The dictionary starts at 256 and
  is never cleared implicitly.
- The dictionary grows unconditionally with no bounds guard; a port must reproduce that rather
  than "fix" it.
- The C++ extracts each code with an unaligned 4-byte read that runs two bytes past the 52-byte
  buffer on the final code of every block — undefined behaviour that works only because other
  members follow it in memory. A port should zero-pad the block instead; the last code starts at
  bit 403 and needs bits 403–415, which live entirely in bytes 50–51, so the padding is never
  consumed and output is identical.

Implemented in `UAF.Serialization/CarLzwDecompressor.cs`.

> **The LZW path is unverified, and no fixture in the repository can verify it.** A repo-wide
> search found exactly one Dungeon Craft design — `src/UAFWinEd/DefaultDesign.dsn` at 0.915025 —
> which is tier 1 or 2 for every file it contains. Everything else that looks like a design is
> DOS FRUA (`reference/example_dsn/SL4-FATH.DSN`, `HEIRS.DSN`, `TUTORIAL.DSN`) and `WebGLBuild`
> is an unrelated Unity build with packed `.unityweb` assets.
>
> **To close this**: open `DefaultDesign` in the shipped `UAFWinEd.exe` and save it. That writes
> at `PRODUCT_VER` (5.29), which is past the 0.930 compression gate, producing a tier-3 fixture
> and simultaneously covering the modern format's `VersionSaveIDs` (0.998914) and spell-name
> branches.

**Partial mitigation.** `CarLzwDecompressorTests` drives the decoder with hand-built code streams
covering bit-packing at all eight shift offsets, block-boundary refill, reset (8190), end (8191),
dictionary expansion, the KwKwK edge case, and truncated input. Expected outputs are derived by
hand-tracing `class.cpp:12215` rather than by running the implementation, so they are not
tautological. This catches transposed shifts and off-by-ones in the 416-bit wrap — but it cannot
prove the C++ *encoder* produces streams in this shape. That still needs a real fixture.

> **Worked example.** `DefaultDesign.dsn/Data/game.dat` begins
> `80 B7 40 82 E2 47 ED 3F 0D 44 65 66 61 75 6C 74 …`. That is *not* a header: the file has no
> magic, so it takes the fallback (`ver = 0.572`), which is `< 0.573`, so it is read with a plain
> `CArchive` starting at offset 0. The leading bytes are the payload's own first two fields —
> `ar << version` (0.915025) then `ar << GetDesignName()` (counted string `0D` + `"Default…"`) —
> exactly as `GLOBAL_STATS::Serialize` writes them (`GlobalData.cpp:3863`). A design that assumed
> "the first 8 bytes are the container version" would silently mis-parse every legacy file.

> Not to be confused: `gamedatSignature` (`Globals.cpp:3449`) is a rolling hash of `game.dat`
> contents (`h = 3h + byte`, seeded 67) used to validate save games against their design in
> `RunEvent.cpp:5598`. It is not a file magic.

---

## 4. The five hard problems

These determine whether the port succeeds. Everything else is volume.

### 4.1 `CAR` reads MFC's private internals — highest risk

`Shared/class.h:474` defines `CAR`, the archive class every data structure serializes through. It
wraps `CArchive` and computes stream position by **casting the `CArchive` object to `int*` and
indexing hardcoded offsets**:

```cpp
#define CArchiveBufSize 7
#define CArchiveBufCur 9
#define CArchiveBufMax 10
const int *par = (int *)&ar;
return GetFile()->GetPosition() - par[CArchiveBufMax] + par[CArchiveBufCur] + m_bufferIndex/8;
```

On top of that, `CAR` layers its own **LZW compression** (`CODES`: 8,192 codes, a 9,973-entry hash
table, `DDATA` prefix/postfix stacks) and a **string-interning table**
(`CMap<CString,LPCSTR,uint,uint> stringIndex` + `m_stringArray`).

**Consequence:** you cannot port this by reading the C++ source. The C++ source describes a
computation over MFC's buffer state; what you need is the resulting *byte stream*. The C# reader
must be written against real files and validated by round-trip.

**Approach:** reimplement as `UAF.Serialization.ArchiveReader/ArchiveWriter` from first principles —
MFC `CArchive` primitives (counted strings: byte count, then `0xFF`+`ushort`, then
`0xFFFF`+`uint`; little-endian scalars), then the LZW layer, then the string table — and prove
each layer against `DefaultDesign.dsn` and the FRUA-imported designs before writing a single data
class.

### 4.2 Byte-exact format with ~97 revisions

There are **97 live version constants** spanning `0.500` to `5.29` (§3) and **472 version-gate
comparison sites**. Every `Serialize` method takes a `double version` and branches on it. Nearly
all of these gates exist to *read* old designs — dropping them drops backward compatibility with a
25-year corpus of community designs, which the compatibility decision (§11) rules out.

**Approach:** port the gates verbatim. Do not "clean them up", do not collapse adjacent versions
that appear to behave identically, and do not renumber. Encode versions as a `DesignVersion`
readonly struct wrapping the `double`, with named static members transcribed from `Externs.h` and
`Globals.cpp`, so comparisons read the same as the original:

```csharp
if (version < DesignVersion.SpellNames) { … }
```

Transcription of 97 constants is a good candidate for generating the C# from the C++ headers
mechanically, then diffing — hand-typing 97 six-decimal doubles is an avoidable source of silent
one-digit errors.

### 4.3 Text encoding

The projects build as `CharacterSet=MultiByte`, so `CString` is `CStringA` and every string in
every data file is single-byte. .NET's default UTF-8 will corrupt any design containing
non-ASCII characters.

**Approach:** all archive string I/O goes through a single configured `Encoding` — start with
Windows-1252 (register `CodePagesEncodingProvider`), verify against real designs, and never let a
raw `Encoding.UTF8` reach the serializer. Round-trip tests must include non-ASCII fixtures.

### 4.4 The engine blocks inside game logic

`RunEvent.cpp`, `Combatants.cpp`, and `FormattedText.cpp` call `theApp.AppSleep(...)` *in the
middle of game logic* — the engine pumps the Windows message loop from deep inside combat
resolution and event handling. This is fundamentally incompatible with an async UI framework's
threading model, and it is the reason a naive port of the engine stalls.

**Approach: a dedicated engine thread.** Run the ported engine loop on its own thread with the
imperative code shape intact. Replace `AppSleep`/message-pumping with blocking reads on a
`Channel<InputEvent>`; the engine publishes frames to the UI thread as immutable render commands
or a completed framebuffer. This makes the port of the 27k-line `RunEvent.cpp` largely mechanical
instead of a rewrite into `async`/`await`, which would touch every line and every call site.

The existing `CProcinp` task system (`TASKLIST`, `TASKSTATE`, `GameEvent::OnIdle`) is already a
cooperative scheduler with save-game-persisted state — the numbered `TASKSTATE` enum values are
serialized into save games and **must not be renumbered**. It ports well onto a dedicated thread.

### 4.5 105 globals

Porting `globalData`, `levelData`, `party`, `rte`, `ede` et al. to injected services up front means
touching every one of the ~356,000 lines being translated, and turns a port into a rewrite.

**Approach:** port them as `static` members of a `Globals` class first — mechanical fidelity, code
reads the same. Once the engine runs and tests exist, progressively fold them into a `GameContext`
instance passed down. Sequencing this *after* correctness is the difference between a port that
ships and one that doesn't.

---

## 5. Target architecture

### 5.1 Solution layout

The legacy C++ tree stays where it is at `src/`; the port lives under `dotnet/` so the two never
collide and the oracle build keeps working unchanged.

```
src/                             (unchanged legacy C++ — the reference implementation)
oracle/                          C++ JSON dumper + golden fixtures
dotnet/
  UAF.sln                        (.NET 10)
  Directory.Build.props          net10.0, nullable, warnings-as-errors
  src/
    UAF.Common/                  strings, containers, RNG, logging, path/RTE model, DesignVersion
    UAF.Serialization/           CArchive + CAR readers/writers, LZW, string table, containers
    UAF.Data/                    GLOBAL_STATS, LEVEL, PARTY, CHARACTER, ITEM, MONSTER, SPELL,
                                 CLASS, RACE, TRAIT, ASL, taglist, Property, PicSlot
    UAF.Rules/                   GameRules, Money, Specab, combat math, character progression
    UAF.Scripting/               GPDL compiler + VM, Forth VM, script host interface
    UAF.Media/                   IRenderTarget, ISurfaceStore, sprites, bitmap fonts,
                                 IAudioBackend, asset loaders (PNG/BMP/WAV/MID)
    UAF.Media.Avalonia/          Avalonia presentation of the framebuffer (editor)
    UAF.Media.Sdl/               SDL3 presentation + input + audio (game)
    UAF.Import.Frua/             DOS FRUA importer: GEO/MONST/STRG/GAME .DAT, GLB, PCX/LBM
    UAFcore/                     engine: GameEvent tree, task scheduler, combat, viewport,
                                 tile rendering, forms                        [was UAFWin]
    UAFcore.App/                 SDL3 host for the player
    UAFedit/                     Avalonia design editor                       [was UAFWinEd]
  tools/
    gen-design-versions.py       generates DesignVersion from the C++ headers
    gpdlc/                       GPDL compiler CLI                            [was src/GPDL]
    uaf-fileprobe/               dumps any .dsn to JSON (C# side of the oracle diff)
  tests/
    UAF.Serialization.Tests/     golden-file round-trip
    UAF.Data.Tests/              oracle diff vs. C++ dumper
    UAF.Rules.Tests/
    UAF.Scripting.Tests/         GPDL/Forth conformance
    UAFcore.Tests/               headless engine, recorded input traces
reference/                       (gitignored) proprietary FRUA game data
.github/workflows/
  oracle-cpp.yml                 MSVC v143 reference build (windows-latest)
  dotnet.yml                     build + test on Linux/Windows/macOS, generator staleness check
```

Scaffolded so far: `UAF.Common`, `UAF.Serialization`, `UAF.Data`, `UAF.Serialization.Tests`, both
workflows, and the version generator. The rest are created as their phase begins — empty projects
are just restore risk.

Dependency direction is strictly downward; `UAF.Data` must not reference `UAF.Media`, and nothing
below `UAFcore` may reference Avalonia. `UAF.Media.Avalonia` is the only Avalonia-aware library —
this keeps the engine headless-testable, which is the backbone of the test strategy.

### 5.2 Naming

| Old | New | Notes |
|---|---|---|
| `UAFWin.exe` | `UAFcore` (lib) + `UAFcore.App` (executable) | splitting the engine from its shell is what makes headless tests possible |
| `UAFWinEd.exe` | `UAFedit` | single Avalonia app |
| `GPDLcomp.exe` | `gpdlc` | CLI, cross-platform |

---

## 6. Dependency replacement matrix

| C++ dependency | .NET replacement | Notes / risk |
|---|---|---|
| `CString` (MBCS) | `string` + explicit `Encoding` at I/O boundaries | §4.3 — do not default to UTF-8 |
| `CArray`, `CList`, `CMap` | `List<T>`, `LinkedList<T>`, `Dictionary<K,V>` | `mCArray`/`mCList` in `Externs.h` add bounds behaviour — replicate it |
| `CArchive` / `CFile` | `UAF.Serialization` (bespoke) | §4.1 — highest risk |
| MFC dialogs / doc-view | Avalonia + CommunityToolkit.Mvvm | 118 dialogs, §7 Phase 5 |
| CDX + DirectDraw 7 | managed framebuffer + Avalonia `WriteableBitmap` | see below |
| BASS + DirectSound | `IAudioBackend` → MiniAudio or SDL3 bindings | also resolves the GPL conflict |
| `winmm` MIDI | MeltySynth (MIT, pure C#) + SoundFont | needs XMI→MID conversion for FRUA `.XMI` |
| Video for Windows | **FFmpeg / libav** (`FFMediaToolkit` or `FFmpeg.AutoGen`) | 12 `PlayMovie` call sites; see §6.1 |
| zlib | `System.IO.Compression` | note: raw deflate vs. zlib header — verify framing |
| libpng 1.0.8 | `SkiaSharp` or `ImageSharp` | 36 `.png` references |
| `regexp.cpp` | `System.Text.RegularExpressions` | verify the dialect matches; it is a Spencer-style engine, not PCRE |
| `AfxBeginThread` / `CThread` | `Task` / dedicated `Thread` | §4.4 |
| `SharedQueue.h` + `CRITICAL_SECTION` | `System.Threading.Channels` | direct conceptual match |
| `Stackwalker.cpp` | `System.Diagnostics.StackTrace` | drop entirely |

**On rendering:** the original is a software blitter — surfaces, source colour keys, palette
manipulation, per-pixel `GetColorAt`. GPU abstractions (MonoGame, Veldrid) fight this rather than
help. **Keep it a software blitter.** Render into a managed `byte[]`/`uint[]` framebuffer with the
same colour-key semantics. This preserves pixel fidelity — the point of a faithful port — keeps
`Drawtile.cpp` and `Viewport.cpp` mechanical to translate, and makes rendering unit-testable by
hashing the framebuffer with no window or GPU involved.

**Split presentation from drawing: SDL3 for the game, Avalonia for the editor.** SDL3 is already a
dependency for audio, is zlib-licensed (GPL-compatible), and is purpose-built for this shape of
program — window and fullscreen management, display-mode enumeration, high-DPI, vsync,
`SDL_Texture` streaming with free GPU scaling, and a unified keyboard/mouse/gamepad event model
that maps almost directly onto the existing `CInput`/`CProcessInput` polling design. It also suits
the dedicated engine thread (§4.4) far better than Avalonia, whose UI-thread and layout system a
real-time loop has to fight.

SDL3 has no widget toolkit, though, so `UAFedit`'s 118 dialogs stay on Avalonia. And critically
**the editor renders game assets too** — tile previews, wall-slot editors, sprite and icon
pickers, the 3D view dialog — so rendering must not live inside SDL or the editor cannot reuse it
and the project ends up with two blitters.

| Layer | Implementation |
|---|---|
| Blitting: colour keys, transparency, palettes, bitmap fonts | Software → managed framebuffer. Platform-agnostic, **shared by both apps** |
| Presentation — `UAFcore` | SDL3 (`SDL_Texture` streaming, fullscreen, vsync, scaling) |
| Presentation — `UAFedit` | Avalonia `WriteableBitmap` in a control |
| Input — `UAFcore` | SDL3 events |
| Input — `UAFedit` | Avalonia |

The framebuffer is the shared contract; only presentation differs. `UAFcore.App` is therefore an
SDL3 host, not an Avalonia one, and `UAF.Media.Avalonia` narrows to the editor's presentation path
with a sibling `UAF.Media.Sdl` for the game.

### 6.2 SDL3 binding: spiked and viable

**Choice: `ppy.SDL3-CS`** (`dotnet/spike/Sdl3Spike`).

Four candidates exist on NuGet — `SDL3-CS` (+ a separate `SDL3-CS.Native`), `ppy.SDL3-CS`,
`Hexa.NET.SDL3`, and `Silk.NET.SDL` (which is SDL**2**, so not a candidate at all). `ppy.SDL3-CS`
wins on the practical criteria: an order of magnitude more downloads, a 2026 build, and it is the
osu! team's fork — a project that actually ships SDL on all three desktop platforms. It also
bundles the native binaries for every RID this project needs (`win-x64`, `win-arm64`,
`osx-arm64`, `osx-x64`, `linux-x64`), so there is no separate native-acquisition step.

The spike deliberately exercises **the port's actual model**, not just `SDL_Init`: a managed
`uint[]` framebuffer with a colour key, written entirely in C#, pushed through a
`SDL_TEXTUREACCESS_STREAMING` texture and presented. That is what the software blitter (§6) will
do, and it is the case a GPU-oriented abstraction would fight.

Verified on macOS arm64, both real and headless:

| | video | audio |
|---|---|---|
| Real drivers | `cocoa` | `coreaudio` |
| Headless (`SDL_VIDEODRIVER=dummy`) | `dummy` | `dummy` |

Windows and Linux are covered by the `SDL3 binding spike` step in the .NET workflow, which forces
the dummy drivers.

**Headless operation matters as much as the rendering.** CI has no display, and `UAF.Media` must
stay testable there. The C++ editor cannot run in CI at all because `OpenDesign` requires a live
DirectX device (§7 Phase 0) — designing the managed media layer so the same trap cannot recur is
a deliberate goal, not a happy accident.

Two API notes for whoever writes `UAF.Media.Sdl`: the binding marshals strings itself
(`SDL_GetError()` returns a `string`), but takes `nint` for pixel data, so framebuffer uploads
need `fixed` + a cast. The project needs `AllowUnsafeBlocks`.

### 6.1 Video: FFmpeg / libav

SDL does not decode video, so `PlayMovie` (`Shared/Movie.cpp`, 12 call sites) needs its own
decoder. **Decision: FFmpeg via libav bindings.**

The deciding factor is *what has to be decoded*. The engine plays `.avi` through Video for
Windows, which means the movies in existing designs are encoded with whatever VfW codec their
author had installed circa 1995–2005 — Cinepak, Indeo 3/4/5, Microsoft Video 1, MJPEG, uncompressed
RGB. No managed-only decoder covers that set. FFmpeg does, and it is realistically the only
option that plays legacy community content rather than just modern files.

- **Binding:** `FFMediaToolkit` (wraps `FFmpeg.AutoGen`) decodes frames to bitmaps directly, which
  suits blitting into the shared framebuffer. Drop to `FFmpeg.AutoGen` if frame-level control or
  exotic pixel formats demand it. Avoid `Xabe.FFmpeg` — it shells out to the `ffmpeg` executable
  rather than linking libav, so it cannot hand back frames in-process.
- **Licensing:** LGPL builds are the default and sit comfortably under this project's GPL v2
  ("or, at your option, any later version"), so either an LGPL or a GPL FFmpeg build is
  compatible. Prefer LGPL to keep distribution simple.
- **Integration:** decode to RGB frames and blit into the same managed framebuffer everything else
  draws into, so movie playback needs no special path in the renderer and stays testable by frame
  hashing. Audio tracks go to the existing `IAudioBackend`.
- **Packaging:** native FFmpeg binaries per RID. This is the heaviest native dependency in the
  project, so make movie support **optional at runtime** — a design without movies must run on a
  build with no FFmpeg present, degrading to a skipped cutscene rather than a startup failure.

Low priority relative to the rest of Phase 3 (12 call sites), but no longer "deferred": the
decision is FFmpeg, and the abstraction should be shaped for it now rather than retrofitted.
>
> This split also avoids repeating the failure that broke the oracle: `OpenDesign` requires a live
> DirectX device, so the editor cannot run headless at all. Keeping the blitter free of any device
> dependency means `UAF.Media` stays testable in CI.

---

## 7. Phased plan

Each phase has an exit criterion that is *verifiable*, not "looks done".

### Phase 0 — Restore buildability and stand up the oracle (3–5 weeks)

The port cannot be validated without a reference implementation that runs.

1. Delete the dead `Shared/ProjectVersion.h` and its commented-out includes (§3), so no future
   reader mistakes it for the version scheme.
2. Retarget `v140_xp` / `v141_xp` → `v143`; fix resulting compile breaks.
3. GitHub Actions `windows-latest` workflow building all four `vcxproj` files (the runner images
   include the MFC/ATL components).
4. Build the JSON dumper. **Implement it as a `-dumpjson` mode inside `UAFWinEd`, not as a
   standalone project.** `Shared/GlobalData.cpp` includes headers from *both* apps
   (`UAFWinEd/UAFWinEd.h`, `UAFWinEd/resource.h`, `UAFWin/Dungeon.h`) plus `Graphics.h` and
   `SoundMgr.h`, so a separate tool would have to untangle that include graph before dumping a
   single byte. The editor project already compiles all of it.

   Three things about the host app are non-obvious, and each cost a CI round-trip:

   - **Bypass `OpenDesign` entirely.** Despite the name it does not load design data, and it
     cannot run headless: after reading `config.txt` it calls `ProcessShellCommand` (creating the
     document and main window) and then requires `GraphicsMgr.IsInitialized()` — a working
     DirectX device, which a CI runner has not got, so it returns `FALSE` before any data is
     touched. The data load normally happens later still, in `CMainFrame::LoadDesign`, which also
     needs a window. None of that is necessary to read a design: resolve paths with
     `rte.DefaultFoldersFromDesign(path)`, read config with `LoadConfigFile(...)`, then load via
     the free functions `loadDesign(name)` (`Level.cpp:3309`) for `game.dat` and the
     `loadData(<DB>, path)` overloads (`Externs.h:483`) for the databases.
   - **A flag and its value must be ONE quoted argument.** `CUAFCommandLineInfo::ParseParam`
     (`Globals.cpp:817`) splits them with `strchr(param, ' ')` *inside* a single token, so the
     invocation is `UAFWinEd.exe "-config <design.dsn>" "-dumpjson <out.json>"`. Passing them
     separately leaves both values empty and the app exits 0 having done nothing. The editor
     launches the engine the same way (`MainFrm.cpp:2648`).
   - **The editor resolves its resources from the EXECUTABLE's directory, not the design's.**
     `EDITOR_ENVIRONMENT::DefaultFoldersFromExecutable` (`Globals.cpp:467`) derives
     `<exeDir>\EditorResources\` and `<exeDir>\TemplateDesign.dsn\` from where the binary sits, so
     a build output folder has none of it. Two separate failures follow: `LoadConfigFile` returns
     FALSE on the missing `MAPART` ("Please re-install Dungeon Craft"), and `saveDesign()` returns
     FALSE when it tries to copy `BASS.DLL` out of the template dir (`Level.cpp:2780`). CI stages
     the layout beside the exe from `src/UAFWinEd/EditorResources` and `DefaultDesign.dsn` rather
     than working around either symptom.
   - **`-config` is mandatory, and every modal path must be suppressed — individually.** A
     `g_headlessMode` flag set by `-dumpjson` handles this, but guarding `MsgBoxError` alone is
     not enough: the load path also reaches `MsgBoxYesNo`, `MsgBoxInfo`, four direct
     `MessageBox(NULL, …)` calls in `Level.cpp`, and one inside `WriteDebugString` itself
     (`Globals.cpp:2216`) — that last one lets a *diagnostic* hang the run it is trying to explain.
     `OpenDesign("")` additionally falls back to `XBrowseForFolder`, a modal picker.

     **The safe default differs per site, so a blanket answer is wrong.** `Level.cpp:3202` offers
     to renumber the design's level files and calls `rename()` on OK — headless must answer
     **Cancel**, because an oracle must never mutate the fixture it reads. `Level.cpp:3279` is
     informational and *aborts the load* unless answered OK — headless must answer **OK**. The
     rule: decline anything that mutates, accept anything that merely informs.

   The mode branches in `CUAFWinEdApp::InitInstance` after `ParseCommandLine`, dumps canonical
   JSON (sorted keys, full-precision doubles, raw MBCS bytes), and returns `FALSE` so the message
   loop never starts. It writes a file **even when loading fails**, with `_meta.ok = false`, so
   that "flag never parsed" and "design failed to load" are distinguishable from outside instead
   of both presenting as a clean exit 0 with no output.
5. Assemble the fixture corpus: `src/UAFWinEd/DefaultDesign.dsn` (complete minimal design —
   `game.dat`, `items.dat`, `spells.dat`, `monsters.dat`, `races.dat`, `classes.dat`,
   `traits.dat`, `ability.dat`, `baseclass.dat`, `spellgroups.dat`, `Level000.lvl`), plus designs
   produced by importing `reference/…/DESIGNS/UA/HEIRS.DSN` and `TUTORIAL.DSN` through the
   existing editor.

**Exit:** `uaf-dump DefaultDesign.dsn` produces stable JSON in CI, committed as golden files.

#### Phase 0 status (verified in CI, run 30190681691)

| Item | Result |
|---|---|
| Retarget to v143 / SDK 10.0 | Done — all four `*_vs2013.vcxproj` |
| Legacy DirectX/multimedia link inputs | **All present** in Windows SDK 10.0.26100 x86: `ddraw.lib`, `dxguid.lib`, `dsound.lib`, `vfw32.lib`, `winmm.lib`, `amstrmid.lib`. The concern that `ddraw.lib` had been dropped was unfounded. |
| Build CDX from source | **Not possible, and not needed.** `CDX_vs2013.vcxproj` compiles `..\PNG\*.c`; the libpng *sources* are not in this repository (only prebuilt `libpng*.lib` and headers under `src/Shared`). `src/Shared/cdx.lib` is prebuilt and committed, and MSVC guarantees binary compatibility from v140 onward, so the oracle links the prebuilt lib and skips the project. |
| MFC availability | Resolved by pinning `windows-2022`, which ships MFC x86 for v143 natively. The first attempt used `windows-latest`, which has moved to VS 2026 / MSBuild 18.x (default toolset v144) and lacks MFC for v143 × Win32 — `MSB8041`. A wildcard `Test-Path` for `afxwin.h` reported a false positive; probe **per toolset and per architecture** (`atlmfc\lib\<arch>\nafxcw.lib`) instead. |
| Build GPDLcomp / UAFWin / UAFWinEd | **All green** after the fixes below. The workflow's build and dump steps are now gating rather than `continue-on-error`. |
| UAFWin / UAFWinEd — compile | Fixed: `Shared/MessageMap.h` declared `std::unordered_map<std::string, std::string>` while including only `<unordered_map>`. Older MSVC headers pulled in `<string>` transitively; v143 does not. `MessageMap.h` is the *only* header in the tree that uses the standard library, so this is a class of one. |
| UAFWin / UAFWinEd — link | `LNK1181: cannot open input file 'vfw32.lib'`. **Root cause: hardcoded `<LibraryPath>` overrides** pointing at a 2015 developer machine — `C:\Development\UAF\DX8SDK\lib`, Windows Kits **8.1**, and SDK `10.0.10240.0`. Because `LibraryPath` *replaces* the default rather than appending, `WindowsTargetPlatformVersion` never got a say. Fixed by deleting the four overrides (2 each in UAFWin and UAFWinEd). GPDLcomp and CDX have no such override, which is exactly why GPDLcomp was the only target that built. |

Also removed: a `PostBuildEvent` in UAFWinEd's Debug configuration copying the exe to
`C:\Users\Shadow\Downloads\...`. It would not fire on a Release build, but it breaks any Debug build.

**A false lead worth recording.** The first theory was that `10.0` resolved to an SDK lacking the
legacy multimedia libs. The per-SDK probe disproved it — every installed SDK from `10.0.17763.0`
upward carries all six libs; only `10.0.10240.0` lacks them, and only the hardcoded override
pointed there. The lesson is that a probe must answer the question *the linker* asks ("is it on the
search path?"), not the one that is easy to ask ("does it exist somewhere?"). The same mistake
produced a false positive on MFC one round earlier.

The SDK pin (`10.0.17763.0`) was kept anyway: it costs nothing, makes the oracle reproducible, and
the probe now fails loudly if that SDK ever disappears from the image.

Three durable lessons for the CI:

1. **Pin the runner image and the SDK.** Image drift moved MFC out from under the build when
   `windows-latest` rolled to VS 2026, and `WindowsTargetPlatformVersion = 10.0` means "newest
   installed", not a version.
2. **`continue-on-error` rewrites a step's `conclusion` to `success`.** Only `outcome` — captured
   in the summary table — reports the truth. This mistake was made once while reading results.
3. **A script that writes `::error::` but does not `exit` non-zero also reports success.** The
   first dump step did exactly that: the summary showed `JSON dump ✅` on a run where the log said
   `no JSON produced` and no artifact was uploaded. Every step script must exit non-zero on
   failure, or the summary becomes decorative.

**GUI-subsystem binaries do not block PowerShell.** `UAFWinEd.exe` is a `WINDOWS` subsystem app,
so `& $exe ...` returns immediately with `$LASTEXITCODE` unset and any following file check races
the process. Use `Start-Process -Wait -PassThru`. This is the reason the first dump attempt
produced nothing despite the build being green.

### Two different formats share the `.dsn` folder convention

A "design" is a **folder whose name ends in `.dsn`/`.DSN`** — for *both* the DOS FRUA format and
the Dungeon Craft format. `Externs.h:1751` (`GetDesignPath`/`GetDataPath`/`GetArtPath`) and the
importer's validation message (`ImportFRUAData.cpp:324`) both assume it. Sniff the contents, never
the extension:

| | DOS FRUA (importer input, Phase 6) | Dungeon Craft (native) |
|---|---|---|
| Layout | flat, 8.3 uppercase names | contains a `Data/` subfolder |
| Marker files | `GAME001.DAT`, `GEO*.DAT`, `MONST*.DAT`, `STRG*.DAT`, `*.TLB`, `SAVE/` | `Data/game.dat`, `items.dat`, `spells.dat`, `Level000.lvl`, `config.txt` |

Available fixtures, **all FRUA except `DefaultDesign`**:

| Fixture | Contents | Use |
|---|---|---|
| `src/UAFWinEd/DefaultDesign.dsn` | Dungeon Craft, **0.915025** | The only DC design. Tier 1/2 only — see the LZW gap below |
| `reference/RUNELORD.DSN` | FRUA — 23 levels, **127 monsters**, 296 `.TLB`, 8 `.XMI`, 2 `.GLB`, both `ITEM.DAT` and `ITEMS.DAT` | Richest importer fixture (Phase 6) |
| `reference/example_dsn/SL4-FATH.DSN` | FRUA — 22 levels, 36 monsters, 118 `.TLB` | Importer; has the mixed-case filenames |
| Steam `HEIRS.DSN` / `TUTORIAL.DSN` | FRUA, small | Importer smoke tests |

> **Beware two unrelated files named `Game.dat`.** `reference/…/GBC/Games/…/Game.dat` belongs to
> **Gold Box Companion**, a third-party tool: it begins with a counted string
> (`0a 14 "Unlimited Adventures"`), has no magic and no version double, and shares nothing with
> Dungeon Craft's `game.dat` but the name.

Filename case is **not** consistent across designs, so it cannot be assumed either way:
`SL4-FATH.DSN` mixes `8X8D1009.TLB` with `8x8d0315.TLB`, while `RUNELORD.DSN` is uppercase
throughout (468 of 469 names). The importer must resolve case-insensitively regardless.

**Filename case is not consistent, and this breaks the port off Windows.** `SL4-FATH.DSN` contains
both `8X8D1009.TLB` and `8x8d0315.TLB`, both `.TLB` and `.tlb`, plus `Back*.tlb`. Windows hides
this; macOS and Linux do not. Any importer code building a name with a fixed-case format string
(`"8X8D%04d.TLB"`) will silently fail to find half the tile libraries. Case-insensitive asset
resolution therefore belongs in **Phase 6, not Phase 7 polish** — and it needs a lookup that
resolves against a case-folded directory index rather than trusting the constructed name.

### Transcribe readers from the LOADING branch, never the storing branch

Every `Serialize` method is `if (ar.IsStoring()) { … } else { … }`, and **the two halves are not
mirror images**. The writer emits only the current format; the reader must handle every historical
one, so the loading branch carries far more version gates.

Worked example — `GLOBAL_STATS::Serialize(CArchive&)`. The storing branch (`GlobalData.cpp:3862`)
reads as a flat sequence ending `AS(m_MapArt)` → `logfont` → `AS(IconBgArt)` →
`AS(BackgroundArt)` → count. Transcribing *that* into a reader produces a garbage count, because
the loading branch (`GlobalData.cpp:3992`) actually does:

```cpp
if (version < 0.830)  { ar >> font; if (version >= 0.681) ar >> fontSize; … }  // no blob at all
else                  { ar.Read(&logfont, sizeof(logfont)); }                  // 60-byte LOGFONTA
if (version < 0.800)  { DAS(ar, TitleBgArt); … }
if (version >= 0.660) { DAS(ar, IconBgArt); DAS(ar, BackgroundArt); … }
if (version >= 0.566 && version < 5.25) { DAS(ar, CreditsBgArt); }             // easily missed
ar >> count;
```

At 0.915025 that means an extra `CreditsBgArt` string the storing branch never hints at, and the
`logfont` is a raw blob only because the version is ≥ 0.830 — an older design stores a font *name*
and size instead, with no blob.

**Two different "versions" are in play, and conflating them mis-parses everything.**

| | Source | Used for |
|---|---|---|
| Container version | the magic prologue, or the per-type unstamped fallback | choosing the archive tier (§3.2) |
| Content version | `ar >> version` — the payload's own first field | every `if (version …)` gate inside `Serialize` |

For `DefaultDesign`'s `game.dat` these differ: the container resolves to **0.572** (no magic →
fallback), which selects the plain `CArchive`; but the first field read *from* the payload is
**0.915025**, and that is what every subsequent gate compares against. Using 0.572 for the content
gates would take the pre-0.830 branch and desynchronise the whole record.

Verified end-to-end against the real file: `m_MapArt = "AreaViewArt.png"`, `LOGFONT` = height 16 /
weight 700 / face `"SYSTEM"` (matching the `FillDefaultFontData("SYSTEM", 16, …)` default),
`IconBgArt = "defib.png"`, `BackgroundArt = "*"` (the `ArchiveBlank` sentinel → empty),
`CreditsBgArt = "Credits.jpg"`, then `SmallPicImport count = 18`.

### The `CArchive` and `CAR` overloads are NOT the same reader

Most data classes define `Serialize(CArchive&, double)` *and* `Serialize(CAR&, double)`. They are
not mechanical duplicates — **their version gates differ**, so the same design version parses
differently depending on which archive tier the file uses. `ITEM_DATA` is the clearest case:

| Field | `Serialize(CArchive&)` `Items.cpp:2341` | `Serialize(CAR&)` `Items.cpp:2677` |
|---|---|---|
| `preSpellNameKey` | `if (ver < 0.576)` → **not read** at 0.915 | `if (ver < VersionSpellNames \|\| ver >= VersionSaveIDs)` → **read** at 0.915 |
| `spellID` | absent | `if (ver >= 0.999647)` |

Transcribing the `CArchive` order and reusing it for tier-2/3 files desynchronises the very first
record. Verified: with `preSpellNameKey` read, item 0 of `DefaultDesign` decodes as
`unique="Arrow"`, `id="Arrow"`, `hit="Hit.wav"`, `miss="Miss.wav"`, `launch="*"` (the sentinel),
`ammoType="Bow"`. Without it, every string is garbage.

**Port each overload separately.** Do not assume one can be derived from the other.

### The build fork is a division of labour, not a format divergence

**Audit result (task #10):** 59 inline `#ifdef` blocks perform archive access inside a shared
`Serialize` body. **Every one is `#ifdef UAFEDITOR`** — there is not a single engine-only inline
read. A further 6 blocks sit outside `Serialize` bodies and are whole-function guards (e.g.
`PARTY::Serialize` exists only in the engine, because save games are engine-only data); those are
not format forks at all.

The asymmetry resolves against one fact in `Level.cpp:3365`, itself engine-only:

```cpp
#ifdef UAFEngine
   else if (DesignVersion < VersionSpellNames)   // 0.998101
   {
     msg = "This game was created with an old editor.  You must load it with "
           "editor version ... or later and save it again.";
     success = FALSE;                            // the engine REFUSES to load it
   }
#endif
```

**The engine refuses any design below 0.998101**, and the editor-only reads are legacy-conversion
fields gated on precisely `version < VersionSpellNames`. So the two builds never read the same
file differently: the editor handles the legacy range and migrates it forward; the engine only
ever sees designs at or above 0.998101, where both agree.

Even the `ITEM_DATA` art case fits. The editor gates `HitArt`/`MissileArt` on
`ver > VersionSpellIDs` (0.998100) while the engine reads them unconditionally — but every version
the engine will accept is above that gate, so both read the art.

Two consequences, and the first is a real simplification:

1. **`UAFcore` needs no legacy paths.** It can require ≥ 0.998101 exactly as the engine does,
   which removes the great majority of the 472 version gates from the engine's reader.
2. **`UAFedit` needs the full range**, 0.500 → 5.29, including the conversion fields. The
   `ArchiveRole` distinction stays, but it separates *legacy-capable* from *modern-only* rather
   than modelling a divergent wire format.

> The earlier framing of this as "the format forks by build" was wrong, and it mattered: it
> implied `UAFcore` and `UAFedit` could disagree about a file both accept, which would have been a
> serious constraint. They cannot.

### Original note: `#ifdef UAFEDITOR` vs `UAFEngine` (superseded by the audit above)

`ITEM_DATA::Serialize(CAR&)` contains:

```cpp
#ifdef UAFEDITOR
    if (ver > VersionSpellIDs)     // 0.998100
#endif
    {
      HitArt.Serialize(ar, ver, "");
      MissileArt.Serialize(ar, ver, "");
    };
```

The **editor** skips this art for designs at or below 0.998100; the **engine** has no gate and
always reads it. At 0.915 the two builds therefore consume different numbers of bytes from the
same file.

This is load-bearing for the port, because `UAFcore` and `UAFedit` share one serialization
library. `UAF.Serialization` must model the build flavour explicitly — an `ArchiveRole`
(`Engine` / `Editor`) threaded through the readers — rather than picking one and hoping. Whether
the divergence is intentional or a latent bug in the reference implementation is a separate
question; the port has to reproduce it either way, and the oracle only ever shows the *editor*
side, since the dumper is built into `UAFWinEd`.

> Not every `#ifdef` forks the format: the `UAFEngine` block immediately after `m_uniqueName`
> only derives `m_commonName` in memory and reads nothing. Check each one for archive access
> before concluding the streams diverge.

### Compressed `CAR` is a different encoding, not just LZW on top

Once `Compress(true)` has run, the stream changes in two ways beyond compression
(`class.cpp:11938`):

| | Plain `CArchive` | Compressed `CAR` |
|---|---|---|
| String prefix | 1-byte count (`AfxWriteStringLength`) | `uint` index, then a **4-byte** length |
| Repeated strings | written out each time | **interned** — index `!= 0` is a back-reference |

So a plain reader cannot read a compressed stream *even after decompressing it*. The table is
1-based (`m_nextIndex` starts at 1, `class.cpp:11603`) and lookups are direct
(`m_stringArray[index]`), leaving slot 0 free as the "new string follows" sentinel. Strings
containing an embedded NUL are **not** interned (`class.cpp:11975`) — interning them anyway
shifts every later index by one.

> **`SPELL_ID` is a `CString`, not an integer** (`Externs.h:1324`). `ar >> spellID` therefore
> goes through the *string* path. The name reads like an identifier and the field sits among
> integers, so it is easy to model as an int — and doing so shifts the record by one field.
>
> This one field was mis-modelled twice: first skipped entirely, then read as an `int`. **Both
> attempts produced printable, plausible-looking output**, because a one-field shift in an
> interned-string stream reads later lengths as indices and vice versa. Only diffing against the
> oracle's reading of identical bytes exposed it. Treat "the strings look readable" as weak
> evidence in compressed streams.

### Type traps found while reading real files

Confirmed against `DefaultDesign.dsn/Data/game.dat` by walking
`GLOBAL_STATS::Serialize(CArchive&)` (`GlobalData.cpp:3862`) field by field:

- **`BOOL` is a 4-byte `int`, and is not always boolean.** `AutoDarkenAmount` is declared `BOOL`
  in `GlobalData.h` but holds **256** in the fixture — it is an integer amount wearing a `BOOL`
  type. Mapping the C++ `BOOL` fields onto C# `bool` would silently destroy the value. Model
  every `BOOL` as `int` at the serialization layer and only interpret higher up.
- **`maxParty_maxPCs` is repaired after reading — the stored bytes are not the effective value.**
  RESOLVED by the oracle. The accessors treat it as `(partySize << 16) | maxPCs`; the fixture's
  raw value is `8`, so `maxPCs = 8` and `partySize = 0`. The loading branch then patches it:
  `if (GetMaxPartySize() == 0) SetMaxPartySize(GetMaxPCs() + 2);` (`GlobalData.cpp:3983`), giving
  the **10** the oracle reports. A reader that stops at the raw bytes gets a working party size of
  zero. Post-read repair like this exists only in the loading branch, and is invisible from the
  file alone — which is exactly the class of bug the oracle diff is for.
- **Empty strings are sentinel-encoded.** The `AS`/`DAS` macros (`Externs.h:1937`) substitute
  `ArchiveBlank` — `"*"` (`Globals.cpp:167`) — for empty strings. `DAS` accepts a literal `"*"`
  *as well as* the configured sentinel, because released builds shipped with `"*"`; dropping that
  leniency turns empty strings into literal asterisks in affected designs.
- **`logfont` is a raw struct blob** — `ar.Write(&logfont, sizeof(logfont))`, i.e. a 60-byte
  `LOGFONTA` written verbatim, not field-by-field. It must be read as fixed-size bytes.
- **Field widths are not uniform: watch for `WORD`.** `PIC_DATA::AlphaValue` is a `WORD`
  (2 bytes) sitting among `int`/`DWORD`/`BOOL` neighbours (`PicData.h`). Reading it as 4 bytes
  shifts every subsequent record by two. Transcribe each field's declared type; do not assume
  4 bytes because the neighbours are.
- **A field's presence is version-gated, and absence must not consume bytes.**
  `PIC_DATA::RestartFrame` appears only at `>= 5.24`, so reading it on a 0.915 design consumes
  four phantom bytes and desynchronises everything after.
- **Some gates compare `globalData.version`, not the `version` parameter.** `PicData.cpp:120`
  tests `globalData.version < 0.930269` inside a method that already takes a `version` argument.
  These can differ — see the container-vs-content distinction above — so transcribe which one each
  site actually reads rather than normalising them.

Worked example, verified: the 18 `SmallPicImport` records in `DefaultDesign`'s `game.dat` read as
`prt_SPic1.png` … `prt_SPic18.png` in sequence, every one 176×211 — matching the
`SmallPic 176 x 211` dimension documented in the design's own `config.txt` — followed by a
plausible `IconPicImport` count of 12. Sequential filenames across 18 records are a strong
integrity signal: any width error breaks them long before the last entry.

**Alignment can be verified without the oracle.** Round decimal values in the payload
(`startTime = 800`, `startExp = 30,000,000`) are strong evidence of correct field alignment — a
one-byte slip yields arbitrary 32-bit noise, not round decimals. Useful for bootstrapping a
reader before golden files exist.

### Assets already in the tree worth knowing about

- **`src/Shared/json.hpp`** — nlohmann/json is already vendored. The dumper should use it; it
  orders object keys deterministically, which is exactly what canonical golden output needs.
- **`src/Shared/cdx.lib` / `cdxd.lib`** — prebuilt, so CDX never needs compiling (§ Phase 0 above).
- **`src/UAFWinEd/DefaultDesign.dsn/`** — a complete minimal design, the primary golden fixture.
- **`upstream/port` branch** — an abandoned JavaScript/protobuf port with test cases
  (`Items.js`, `UAFLib`). Its format analysis may be worth mining before writing
  `UAF.Serialization`.

> If restoring the C++ build proves intractable, the fallback oracle is the committed
> `UAFWin.exe` / `UAFWinEd.exe` driven under Wine plus hand-decoding from hex — an order of
> magnitude slower and far less complete. Phase 0 is worth real effort.

### Phase 1 — Serialization and data model (3–4 months)

`UAF.Common`, `UAF.Serialization`, `UAF.Data`.

- Container sniffing for the three header families (§3.2), including the absent-magic fallback.
- MFC `CArchive` primitive layer; then LZW; then string interning (§4.1).
- `DesignVersion` struct with all 97 constants, generated from the C++ headers (§4.2).
- Port the data classes in dependency order: `Property` → `taglist` → `ASL` → `PicSlot` →
  `Money` → `traits` → `Items` → `Spell` → `Monster` → `class` → `Char` → `Party` → `Level` →
  `GlobalData`.
- Port `ConfigFile.cpp` / `FileParse.cpp` (`config.txt`, `specialAbilities.txt`) and
  `RUNTIME_ENVIRONMENT` — replacing the hardcoded `\` separators and `_MAX_PATH` arrays with
  `Path.Combine` and case-insensitive path resolution for case-sensitive filesystems.

**Exit:** `uaf-fileprobe` (C#) output is byte-identical to `uaf-dump` (C++) JSON for every fixture,
and every `.dat`/`.lvl` file round-trips to an identical byte stream.

*This exit criterion is the single most important gate in the plan. Do not proceed past it.*

#### `ITEM_DATA` field map (`Items.cpp:2749`, the `CAR` overload)

The worked template — now **complete**, and validated end to end (see below).

| Field(s) | Gate | Notes |
|---|---|---|
| ✅ `preSpellNameKey` | `ver < 0.998101 \|\| ver >= 0.998914` | int |
| ✅ `spellID` | `ver >= 0.999647` | **a string** — `SPELL_ID : CString` |
| ✅ `m_uniqueName`, `m_idName`, `HitSound`, `MissSound` | — | `DAS` sentinel applies |
| ✅ `LaunchSound` | `ver >= 0.5691` | |
| ✅ `HitArt`, `MissileArt` | engine always; editor `ver > 0.998100` | two `PIC_DATA` records |
| ✅ `AmmoType` | `ver >= 0.690` | `"None"` normalised to `""` after reading |
| ✅ `Experience`…`Num_Charges` | — | `long`/`BOOL`, 4 bytes each |
| ✅ `Location_Readied` | — | legacy ordinal → base-38 name |
| ✅ `Hands_to_Use`…`Protection_Bonus` | — | `ROF_Per_Round` is a **`double`** among `long`s |
| ✅ `Wpn_Type`, `m_usageFlags` | — | ints |
| ✅ `Usable_by_Class` *or* baseclass list | editor + `ver < 0.998101`, else the list | count then N × `BASECLASS_ID` **strings** |
| ✅ `RangeMax` | — | |
| ✅ `preVersionSpellNames_gsID` + 2 × junk | editor + `ver < 0.998101` | conversion-only, `int` |
| ✅ `m_useEvent` | `ver >= 0.662` | |
| ✅ `ExamineEvent`, `ExamineLabel` | `ver >= 0.800` | |
| ✅ `attackMsg` | `ver >= 0.860` | defaults to `"attacks"` when absent |
| ✅ `Recharge_Rate`, `IsNonLethal`, `HitArt` | `ver >= 0.690` | `HitArt` a **second** time |
| ✅ `CanBeHalvedJoined` | `ver >= 0.881` | defaults **TRUE**, not 0 |
| ✅ `CanBeTradeDropSoldDep` | `ver >= 0.904` | defaults **TRUE**, not 0 |
| ✅ **`specAbs.Serialize`** | — | `SpecabReader` |
| ✅ **`item_asl.Serialize`** | — | `AslReader` |
| ✅ ammo-type list | `ver >= 0.690` | **after** the record loop, not inside it |

##### Specab, as ported

`Specab.cpp` is 2,240 lines but its serialized shape is small, because everything hangs off one
gate (`Specab.cpp:1155`):

```cpp
if (version <= 0.920 && !ar.IsStoring())   // legacy conversion
else  m_specialAbilities.Serialize(ar);    // an A_CStringPAIR_L
```

**The gate is asymmetric.** The legacy branch is conditioned on `!IsStoring()`, so old designs are
*read* in the old shape but always *written* back in the new one. Treating this as a symmetric
format fork would produce files the reference cannot read. Both branches are live in practice:
DefaultDesign is 0.915 and takes the legacy path, while the three compressed designs (2.53, 3.55,
5.28) take the modern one.

The modern form is an `A_CStringPAIR_L` (`ASL.cpp:1848`) — an `int` count then key/value string
pairs, with no map name, no flags byte, and **no** `DAS` decoding. Note it counts with a 32-bit
`int` where its sibling ASL uses a `WORD`, and the legacy branch *does* apply `DAS`. The two
structures live in the same file and are easy to conflate.

The `#ifdef UAFEDITOR` and `#else` halves of the legacy branch read byte-identical streams; they
differ only in what they build afterwards. That is what makes a shared cursor safe here, where it
would not be for ASL.

##### What the full walk found

Wiring both subsystems in made a complete `ITEM_DATA` walk possible, and it immediately exposed a
field that per-record tests could never have caught: **`PIC_DATA` has two overloads that differ**.
The `CAR` one reads a `style` field at 0.900 and above (`PicData.cpp:203`); the `CArchive` one has
that exact line commented out (`PicData.cpp:139`). Four bytes, no marker, and every field after it
still decodes to plausible values — record 0 read perfectly with `style` missed, and the damage
only surfaced 12 bytes later as an impossible message count.

Two lessons are now encoded in the API. `PicDataReader` takes an explicit `PicArchiveVariant`
rather than guessing, since **which overload wrote the bytes is not the same question as whether
the stream is compressed** — a compressType-0 archive still runs the `CAR` code path. And
`SpecabReader` mirrors the reference's `die()` on an out-of-range message count instead of
clamping: that guard is what turned a silent 4-byte drift into an immediate, locatable failure.

##### Validation

| Check | Result |
|---|---|
| All 285 records read to completion | ✅ |
| Both names of all 285 records vs. the C++ oracle | ✅ exact |
| All 25 dumped fields × 8 records vs. the oracle | ✅ exact |
| Stream lands precisely on EOF | ✅ 548,768 / 548,768 |
| `Location_Readied` base-38 conversion | ✅ `QUIVER` = 2,286,454,785 |

The EOF check is worth calling out: it is the cheapest possible whole-file assertion, and it is
what surfaced the ammo-type list appended after the record loop — 22 bytes that a reader stopping
at the last record leaves unconsumed while looking entirely successful.

##### ASL, as ported

The format itself is small (`ASL.cpp:1386`); what took the time was establishing its properties
against real files. Below 0.505 (`_ASL_LEVEL_`) the block is absent entirely — not empty, *absent*,
so nothing is consumed. Otherwise it is a map name, a **WORD** count, then `{key, flags, value}`
triples. The count is 16-bit on both paths: `CAR::operator>>(unsigned short&)` calls
`decompress(&v, 2)` (`class.cpp:11865`).

Four properties are easy to get wrong and each is now pinned by a test:

- **The map name is a sync marker, not a label.** The reference throws on a mismatch
  (`ASL.cpp:1420`). Preserved deliberately: a misaligned reader essentially never reproduces the
  expected literal, so this is the one built-in checkpoint the format offers.
- **The block carries payload, not just metadata.** `MissileArt` was migrated into the attribute
  map rather than given a version gate (`Items.cpp:2627`), so an ASL cannot be read-and-discarded.
- **Entries are hash-ordered.** The container is a `CMapStringToPtr` walked with `GetNextAssoc`.
  The same four global-stats keys appear in one order in the uncompressed DefaultDesign and a
  different one in all three compressed designs. Look up by key, never by index; compare
  round-trips as sets.
- **The compressed encoding is not self-describing.** Keys are written out fresh, but entries
  sharing a value store a string-table index instead — an index counted from the start of the
  stream. The plain encoding of the same block *is* seekable; the compressed one is not.

Two write paths share the one read format: `Serialize` (design files) writes every entry, while
`Save` (savegames, `ASL.cpp:1489`) drops anything flagged `ASLF_READONLY`. That only matters once
the port writes savegames — but note that every `GLOBAL_STATS_ATTRIBUTES` entry is `0x05`
(`READONLY | DESIGN`), so a savegame correctly writes a count of zero there.

Verified against four designs spanning the format's whole life — the uncompressed DefaultDesign
(0.914) plus compressed designs at 2.53, 3.55 and 5.28 — with the plain block decoded
independently in Python before the C# was trusted.

##### Compressed designs walk with no changes

The same `ItemRecordReader` was pointed at the LZW-compressed `items.dat` of three designs — 2.53,
3.55 and 5.28 — and walked all three end to end **without a single code change**. That is the
strongest evidence so far that the version gates are modelled correctly rather than merely fitted
to one file: these take the opposite branch from DefaultDesign at nearly every fork.

| | DefaultDesign | The three compressed designs |
|---|---|---|
| Archive tier | plain / uncompressed `CAR` | `compressType 2` (LZW) |
| `Specab` | legacy conversion (< 0.920) | modern `A_CStringPAIR_L` |
| Usability | `Usable_by_Class` bitmask | `BASECLASS_ID` string list |
| `PIC_DATA` | no `RestartFrame` | `RestartFrame` present (≥ 5.24) |
| Records | 285 | 562 / 551 / 479 |

Each walk decodes its ammo-type list correctly *after* every record and then finds the LZW stream
exhausted at exactly that point. Specab pairs carry real content (`item_WeaponType` = `piercing`),
and `BASECLASS_ID` values decode as names (`assassin`, `fighter`, `paladin`, `ranger`) — reading
those as integers, the `SPELL_ID` mistake, would desynchronise on the first record.

One finding worth recording: a few pairs have an empty key *and* value. That is genuine data. The
two sibling structures disagree here — `A_ASLENTRY_L::Update` refuses an empty key
(`ASL.cpp:1311`), which makes "keys are never empty" a tempting invariant, but
`A_CStringPAIR_L::Serialize` (`ASL.cpp:1875`) inserts whatever is on the wire. A reader that
validates non-empty keys rejects designs the reference loads without complaint.

**What remains open.** `AslReader`'s compressed overload is now genuinely driven — 562 blocks
located with their map names verified, against a live intern table, which cannot happen by
accident. But every one of those blocks has a count of zero, so the key/flags/value loop and the
compressed-only key fixup are still unexercised on real data. The non-empty compressed ASLs live
in `game.dat` (`GLOBAL_STATS_ATTRIBUTES`, four entries), so that last step waits on a `game.dat`
record walk. This is asserted in the tests rather than left implicit, so it stays visible instead
of reading as coverage it is not.

#### Progress

| Layer | State |
|---|---|
| Tier 1 — plain `CArchive` | `game.dat` verified through the picture-import block (offset 1651 of 4343) |
| Tier 2 — `CAR` uncompressed | Three databases verified; counts 285 / 44 / 117 agree with the oracle |
| Tier 3 — `CAR` + LZW | Verified against real encoder output at 2.53, 3.55, 5.28 and 5.29 |
| `GLOBAL_STATS` | Scalars, art strings and both picture-import blocks diffed against the oracle |
| `ITEM_DATA` | **Complete.** 285 uncompressed records match the oracle field by field; 562 / 551 / 479 compressed records at 5.28 / 3.55 / 2.53 walk to exact EOF with no code changes |
| `ASL`, `Specab`, `PIC_DATA` | Ported; exercised at every record of all four designs, on both sides of the 0.920 Specab fork |
| Remaining | ~40 further data classes; `game.dat`'s own record walk (which will close the last compressed-ASL gap) |

The pattern is now established and mechanical: extend the dumper for a type → write the C# reader
→ diff. `ITEM_DATA` is the worked template.

**Fixture coverage** spans 0.915025, 2.53, 3.55, 5.28 and 5.29 — a Dungeon Craft design at each,
with the 5.29 one generated by CI itself (`-savedesign`) and dumped in the same run, so C# and C++
readings of identical bytes can be compared directly. That last property is what caught the
`SPELL_ID` mis-modelling; nothing weaker would have.

### Phase 2 — Rules and scripting VMs (2–3 months, parallelizable with Phase 3)

`UAF.Rules`, `UAF.Scripting`, `tools/gpdlc`.

These are pure computation with no platform coupling — the best-value work in the project and
fully unit-testable.

- GPDL compiler (`GPDLcomp.cpp`, 4,769) and bytecode VM (`GPDLexec.cpp`, 8,377) against
  `GPDLOpCodes.h`. `src/GPDL/language.txt`, `functions.txt`, and `talk.txt` are the spec and
  conformance corpus.
- The Forth VM (`UAFWin/Forth.cpp`, 2,534) used for spell effects.
- `GameRules.cpp` (4,167), `Specab.cpp` (2,240), `Money.cpp` (2,026).

**Exit:** `gpdlc` produces byte-identical bytecode to the C++ compiler for every script in the
fixture corpus; the VM produces identical execution traces.

### Phase 3 — Media layer (1.5–2 months, parallelizable with Phase 2)

`UAF.Media`, `UAF.Media.Avalonia`.

- Software framebuffer with DirectDraw-equivalent blit/colour-key/transparency semantics.
- Surface and sprite stores mirroring `Graphics`/`SurfaceCacheMgr` (`Shared/Graphics.h:94,115`).
- Bitmap font rendering (`CDXBitmapFont.cpp`, `DrawFont` with `FONT_COLOR_NUM` tag handling).
- `IAudioBackend` + MIDI synth; `Channel`-based replacements for the three audio threads.

**Exit:** a test harness renders known tile/sprite/font sequences and the framebuffer hash matches
screenshots captured from the C++ build.

### Phase 4 — UAFcore engine (4–6 months)

- Dedicated engine thread + `Channel`-based input/render marshalling (§4.4).
- `CProcinp` task scheduler with `TASKSTATE` numbering preserved.
- The 116 `GameEvent` subclasses and `RunEvent.cpp` (27,639 lines — budget accordingly).
- Combat: `Combatant.cpp`, `Combatants.cpp`, `path.cpp`.
- Rendering: `Drawtile.cpp`, `Viewport.cpp`, `Screen.cpp`, `Disptext.cpp`, `FormattedText.cpp`.
- Forms: `CharStatsForm`, `SpellForm`, `ItemsForm`, `TextForm`, `RestTimeForm`, `GameMenu`.
- `UAFcore.App`: Avalonia shell, window/fullscreen, input mapping, config.

**Exit (staged):**
- 4a — headless: load `DefaultDesign.dsn`, run a recorded input trace, produce a save game
  byte-identical to one produced by the C++ engine from the same trace.
- 4b — rendered: play `HEIRS.DSN` start to finish on all three platforms.

### Phase 5 — UAFedit (4–5 months; may start once Phase 1 exits)

The 118 dialogs in `UAFWinEd.rc` / 111 `CDialog` subclasses are the bulk of this phase.

**Recommendation: write a one-shot `.rc` → `.axaml` transpiler.** MFC `DIALOG` resources have a
fully deterministic grammar (`LTEXT`, `EDITTEXT`, `PUSHBUTTON`, `COMBOBOX`, `LISTBOX`, `CONTROL`,
with dialog-unit coordinates). Generating `Canvas`-positioned AXAML with DLU→px conversion converts
weeks of manual layout into days of generation plus targeted refinement. Port each dialog's
DDX/DDV and handlers into a `CommunityToolkit.Mvvm` ViewModel.

Sequence: start with a **read-only design inspector** (open a `.dsn`, browse levels/items/
monsters/spells) — this validates Phase 1 end-to-end, gives the editor its shell and navigation,
and grows into the full editor. Then the databases (`ItemDB.cpp` 9,000, `SpellDBDlgEx`,
`MonsterDBDlg`), then the map/level view (`UAFWinEdView.cpp`, `SelectLevel`, `EditWallSlots`),
then the event editor (`EventViewer.cpp` 4,072, `LogicBlock`, `Script`), then cross-reference and
utilities.

**Exit:** open a design, make an edit, save; the resulting files load correctly in *both* UAFcore
and the original C++ `UAFWinEd.exe`.

### Phase 6 — FRUA importer (1–1.5 months; independent after Phase 1)

`UAImport.cpp` (7,036 lines) reads original DOS FRUA data: `GAME001.DAT`, `GEOnnn.DAT`,
`MONSTnnn.DAT`, `STRGnnn.DAT`, `items.dat`. These are exactly the files in
`reference/…/DESIGNS/UA/HEIRS.DSN` and `TUTORIAL.DSN`. Art lives in the `.GLB` archives under
`reference/…/GAME/UA/` and needs PCX/LBM decoding; music is `.XMI`.

Case-insensitive asset resolution is required *here*, not in Phase 7 — see the filename-case note
in §3.2. Build the directory index once per design and resolve every constructed filename through
it.

**Exit:** importing `HEIRS.DSN` and `reference/example_dsn/SL4-FATH.DSN` each produce a design
directory byte-identical to one produced by the C++ importer.

### Phase 7 — Packaging and polish (1–2 months)

- Self-contained `dotnet publish` per RID; macOS `.app` bundle + notarization; Linux AppImage or
  Flatpak; Windows zip/MSIX.
- Case-insensitive asset resolution shim (designs assume Windows path semantics throughout).
- High-DPI, window scaling, gamepad/touch input if desired.
- Migration documentation for existing design authors.

---

## 8. Testing strategy

There is currently zero test coverage — `TestPartySelectDlg.cpp` is a UI dialog, not a test. The
port must not inherit that.

1. **Oracle diffing (Phases 0–2, 6).** The C++ dumper is ground truth. Every data structure,
   compiled script, and imported design is compared field-by-field. This catches the class of bug
   that is otherwise undetectable until a user's 20-year-old design silently corrupts.

   > **The oracle has a known blind spot.** `Level.cpp:3340` warns that the editor "will reliably
   > load designs created with editor version less than 0.998101 or greater than 0.9988" — i.e.
   > the reference implementation itself is *unreliable* for designs in
   > **[`VersionSpellNames` (0.998101), 0.9988]**. Golden fixtures must not be drawn from that
   > window, and a C#/C++ divergence on such a design is not automatically a C# bug. Record the
   > design version alongside every golden file so this is checkable rather than assumed.
2. **Round-trip invariance (Phase 1).** Load → save → byte-compare. Non-negotiable gate.
3. **Golden framebuffers (Phase 3–4).** Hash rendered output against captures from the C++ build.
4. **Recorded input traces (Phase 4).** Capture input sequences from the C++ engine, replay
   headlessly in C#, compare resulting save-game bytes. This is how you test an engine with no
   seams — and it is why `UAFcore` must be a library separate from `UAFcore.App`.
5. **Property tests** over the archive layer: random structures → serialize → deserialize →
   compare, including non-ASCII strings and boundary-length counted strings (254/255/65534/65535).

---

## 9. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| C++ tree cannot be made to build under v143 | Loses the oracle; the whole validation strategy | Phase 0 is scoped generously; committed binaries + PE constant-pool analysis (§3.1) as partial fallback |
| Shipped binaries post-date the committed source (§3.1) | Oracle validates against a *different* implementation than users run | Compare oracle output against files saved by the shipped `.exe`; treat divergence as a finding, not noise |
| `CAR` format not fully recoverable from files alone | Blocks everything | Attack it first, with the C++ build available for instrumentation |
| Undocumented version gates behave differently than the source reads | Silent data corruption | Oracle diffing across a wide design corpus, not just `DefaultDesign` |
| Engine's blocking/re-entrant control flow resists translation | Phase 4 stalls | Dedicated engine thread decided up front (§4.4), not discovered mid-port |
| BASS licensing / audio backend choice | Rework in Phase 3 | Decide before Phase 3 starts; `IAudioBackend` keeps it swappable |
| 118 dialogs ported by hand | Phase 5 doubles | `.rc`→`.axaml` transpiler |
| Scope creep into redesign | Project never ships | Fidelity-first rule (§1); globals stay static until the engine runs (§4.5) |
| Solo-developer bus factor over ~2 years | Abandonment | Phase exits are independently valuable; the data layer alone is a usable library |

---

## 10. Effort and sequencing

| Phase | Effort | Depends on |
|---|---:|---|
| 0 — Build + oracle | 3–5 weeks | — |
| 1 — Serialization + data | 3–4 months | 0 |
| 2 — Rules + scripting | 2–3 months | 1 |
| 3 — Media layer | 1.5–2 months | 0 (parallel with 2) |
| 4 — UAFcore engine | 4–6 months | 1, 2, 3 |
| 5 — UAFedit | 4–5 months | 1 (parallel with 3–4) |
| 6 — FRUA importer | 1–1.5 months | 1 (parallel) |
| 7 — Packaging | 1–2 months | 4, 5 |
| **Serial total** | **18–26 engineer-months** | |

Solo full-time, that is roughly two years. With two developers splitting engine (4) and editor (5)
after Phase 1, calendar time drops to roughly 12–15 months.

**Milestones worth targeting:**
- ~Month 1 — C++ builds in CI, golden JSON committed
- ~Month 5 — every design file round-trips byte-identically in C#
- ~Month 7 — read-only design inspector runs on macOS and Linux (first visible deliverable)
- ~Month 12 — headless engine reproduces C++ save games from recorded traces
- ~Month 18 — playable UAFcore on all three platforms
- ~Month 24 — UAFedit at parity; packaged releases

---

## 11. Immediate next steps

1. Retarget the four `vcxproj` files to `v143` and get a Windows CI build green.
2. Write `oracle/uaf-dump` and commit golden JSON for `DefaultDesign.dsn`.
3. Delete the dead `ProjectVersion.h` (§3).
4. Scaffold the solution (§5.1) and begin `UAF.Serialization` against the golden files, starting
   with the three container families in §3.2.

### Decisions taken

| Decision | Choice | Rationale |
|---|---|---|
| Reference oracle | **GitHub Actions `windows-latest`** | No local Windows needed; host is arm64, where an MSVC VM would be emulated |
| Audio backend | **SDL3 audio + MeltySynth** | Permissive licensing resolves the GPL v2 / BASS conflict; SDL3 is well-tested on arm64. Extended: SDL3 also provides the game's window, presentation and input (see §6) |
| Format compatibility | **Read all versions, write current** | Matches original behaviour; preserves ~25 years of community designs |
| Post-Phase-1 priority | **UAFedit first** | A read-only inspector validates the data layer end-to-end and grows into the editor; visible cross-platform result in ~2 months instead of ~8 |
| Video decoding | **FFmpeg / libav** (LGPL build) | Only realistic way to decode the legacy VfW codecs in existing designs (Cinepak, Indeo, MS-Video1); optional at runtime |
