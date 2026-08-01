# Dungeon Craft → .NET 10 / Avalonia Porting Plan

**Targets:** `UAFWin` → **UAFcore** (game engine + player), `UAFWinEd` → **UAFedit** (design editor)
**Stack:** .NET 10, C#, Avalonia 11.x (editor) + SDL3 (game), cross-platform (Windows / macOS / Linux)
**Date:** 2026-08-01

**Status.** Phase 0 complete. Phase 1 complete **for reading** — every design file in the fixture
corpus parses, diffed against the oracle — but **no writer exists**, so its round-trip exit
criterion is not met. Phases 2 and 3 are substantially delivered with named gaps. Phase 4 has a
running engine: it opens a design, walks a level, renders the viewport and executes nine of the 44
event types. Phases 5–7 have not started. 940 tests, green on macOS, Linux and Windows.

> **See also: [SERIALIZATION.md](SERIALIZATION.md)** — the file-format reference. Everything
> established about containers, archive tiers, strings, ASL, special abilities and the type traps
> now lives there. This document covers strategy, sequencing and effort; that one is what you read
> before writing a reader.

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

> **Both compression types are in circulation — a reader must handle either.**
> `CAR::Compress(true)` always *writes* 2 (`class.cpp:11670`), yet `DefaultDesign`'s six databases
> all carry **1**, an older variant. It is not cosmetic: the string reader gates its embedded-NUL
> check on `m_compressType > 1` (`class.cpp:11975`), so type-1 streams **intern** NUL-bearing
> strings that type-2 streams skip. Get that wrong and every later string-table index shifts.
>
> **Correction.** An earlier revision of this note said "every tagged database on disk carries 1".
> That was drawn from `DefaultDesign` alone and is false: `SomethingWild`'s four databases all carry
> **2**. Pinning either value is wrong — the byte is on disk and must be read.
>
> **The version digit varies too, by design and by database.** `DefaultDesign` is `V1` throughout;
> `SomethingWild` ships `AbilityV2` and `RaceV3` beside a `BaseclassV1`. Each loader accepts its own
> range (`RaceV0`…`RaceV3`, `class.cpp:3493`), so a reader must not pin the digit — and **a design
> need not ship all six**: `SomethingWild` has no `spellgroups.dat` or `traits.dat`.

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

`✅` exists today; `—` is created when its phase begins, since empty projects are just restore risk.

```
src/                             (unchanged legacy C++ — the reference implementation)
oracle/                          C++ JSON dumper + golden fixtures            ✅
dotnet/
  UAF.slnx                       (.NET 10)                                    ✅
  Directory.Build.props          net10.0, nullable, warnings-as-errors        ✅
  src/
    UAF.Common/                  DesignVersion, MFC string encoding           ✅
    UAF.Serialization/           CAR/CArchive readers, LZW, string table.     ✅ readers only
                                 Also holds the record model — see below
    UAF.Data/                    config.txt (DesignConfig)                    ✅ see below
    UAF.Rules/                   Money. GameRules, combat math, progression   ✅ money only
    UAF.Scripting/               GPDL compiler + VM. Forth VM                 ✅ GPDL only
    UAF.Media/                   blitter, surfaces, sprites, fonts, audio,    ✅
                                 movie player, PNG loader
    UAF.Media.Sdl/               SDL3 presentation + input + audio + image    ✅
    UAF.Media.Avalonia/          Avalonia presentation (editor)               —
    UAF.Import.Frua/             DOS FRUA importer                            —
    UAFcore/                     engine: events, viewport, party, map         ✅ partial
    UAFcore.App/                 SDL3 host for the player                     —
    UAFedit/                     Avalonia design editor                       —
  tools/
    gen-design-versions.py       generates DesignVersion from the C++ headers ✅
    gpdlc/                       GPDL compiler CLI                            ✅
    uaf-fileprobe/               dumps any .dsn to JSON                       ✅
  spike/Sdl3Spike/               SDL3 binding spike (§6.2)                    ✅
tools/art-oracle.py              regenerates the PNG/legacy-art digests       ✅
  tests/                         Serialization, Media, Rules, Scripting,      ✅ 940 tests
                                 UAFcore
reference/                       (gitignored) design and FRUA data
.github/workflows/
  oracle-cpp.yml                 MSVC v143 reference build (windows-2022)     ✅
  dotnet.yml                     build + test on Linux/Windows/macOS          ✅
```

**Two deviations from the plan above, both deliberate and both worth knowing before you go
looking.** `UAFcore` is currently the executable rather than a library plus `UAFcore.App`; the
split still has to happen before Phase 4b, and the engine is already written to survive it — `Game`
renders into a `Surface` and knows nothing about SDL. And **the record model lives in
`UAF.Serialization`, not `UAF.Data`**: the readers return records directly, and a separate
mirror-image model earned nothing. `UAF.Data` holds `DesignConfig` (`config.txt`) alone. If that
stays true, fold it into `UAF.Common` and drop the project.

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

Four candidates exist on NuGet — `SDL3-CS` (+ a separate `SDL3-CS.Native`, which is
edwardgushchin's), `ppy.SDL3-CS`, `Hexa.NET.SDL3`, and `Silk.NET.SDL` (which is SDL**2**, so not a
candidate at all). `ppy.SDL3-CS` wins on the practical criteria: 522,981 downloads against 60,453
for `SDL3-CS` and 20,962 for `Hexa.NET.SDL3`, and it is the osu! team's fork — a project that
actually ships SDL on all three desktop platforms. It bundles native binaries for 13 RIDs in the
same package, so binding and native cannot skew apart. That last point is not hypothetical:
`SDL3-CS` is at 3.4.12.4 while its separate `SDL3-CS.Native` is still at 3.4.2.

**What `ppy` actually ships is a development snapshot, not a release.** `ppy.SDL3-CS` 2026.722.0
bundles SDL stamped `SDL-3.5.0-a8591d9`, and **there is no SDL 3.5.0 release** — 3.5.0 is the
in-development version `main` carries between releases, and the newest tag is 3.4.12 (2026-07-01).
Commit `a8591d9` is genuine upstream and an ancestor of `main`, but 1,182 commits diverged from
`release-3.4.x`. The package pins it exactly and osu! runs on it, but "SDL 3.4.12" would be a
materially different claim. `SDL3-CS` tracks tagged releases instead, and that is its one real
advantage.

`SDL3-CS` also ships the companion libraries, all tracking **tagged upstream releases**:

| Component | Package | Native version |
|---|---|---|
| Bindings | `SDL3-CS` | SDL 3.4.12 |
| Core natives | `SDL3-CS.{Platform}` | SDL 3.4.12 |
| Image | `SDL3-CS.{Platform}.Image` | SDL_image 3.4.4 |
| Fonts | `SDL3-CS.{Platform}.TTF` | SDL_ttf 3.2.2 |
| Audio | `SDL3-CS.{Platform}.Mixer` | SDL_mixer 3.2.4 |

`{Platform}` is `Windows`, `Linux`, `MacOS`, `Android`, `iOS` or `tvOS`, so natives arrive as
per-platform, per-component references that have to be **guarded by condition in the csproj** —
nine of them for three desktop platforms with image and TTF. That is the real cost, against ppy's
single all-RID package per component.

> An earlier revision claimed `SDL3-CS` had no SDL_image binding at all. That came from probing
> four *guessed* package names, getting four 404s, and reading absence of evidence as evidence of
> absence; the real scheme is `SDL3-CS.{Platform}.Image`. Same failure shape as the serialization
> bugs — a first guess taken as fact instead of read from the source.

So the trade is now: **tagged stable natives plus csproj guards** (`SDL3-CS`) against
**development-branch natives with zero packaging friction and roughly a hundred times the field
usage** (`ppy`). Downloads for the per-platform natives sit at 3–5k, against 522k for
`ppy.SDL3-CS`. Both are zlib/MIT and both offer image and TTF at matching versions.

**Decision: stay on `ppy`.** Switching is a real but bounded cost — the SDL-facing code is
`SdlPresenter`, `SdlAudioDevice`, `SdlInputSource`, `SdlPlatform` and `SdlImageDecoder`, all
binding the same C API under different namespaces and marshalling conventions — but the packaging
friction and the hundredfold difference in field usage outweigh the tagged-release advantage for
now. `ppy.SDL3_ttf-CS` exists at a matching version, so the font rasteriser needs no separate
decision. Revisit if the development-branch dependency causes a concrete problem; this section now
records enough to make that switch without re-deriving it.

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
- **Licensing — this section originally checked only FFmpeg's own licence, which was the wrong
  question.** FFmpeg LGPL builds do sit under GPL v2-or-later. But every C# *binding* is
  **LGPL v3**: `FFmpeg.AutoGen` and `Sdcb.FFmpeg` directly, and `FFMediaToolkit` (MIT itself) by
  depending on the former. Linking any of them makes the combined work GPL **v3**. That is
  permitted by "or any later version", but it removes the v2 option for any build that includes
  video — a decision to take deliberately, not by accident. Second independent reason to keep the
  video decoder in its own optional assembly, loaded only if present. See the Phase 3 progress
  section in §7.
- **Version pinning:** `FFMediaToolkit` pins `FFmpeg.AutoGen` 7.1.1 and so requires FFmpeg 7; it
  fails against FFmpeg 8.
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

> **Open question: the tagged databases may be an exception, and it is not yet resolved.**
> `BASE_CLASS_DATA::Serialize`'s loading branch (`class.cpp:5808`) has:
>
> ```cpp
> #ifdef UAFEDITOR
>       if (intVer >= 4)
> #endif
>       { car.Serialize(THAC0, sizeof(THAC0)); car >> m_spellBonusAbility; /* … */ }
> #ifdef UAFEDITOR
>       else { /* defaults, no archive access */ }
> #endif
> ```
>
> In the **engine** the gate is compiled away, so the block always reads; in the **editor** it reads
> only at `Bcd4` and above. For a record tagged `Bcd2` or `Bcd3` the two builds therefore consume
> **different numbers of bytes from the same file** — which is exactly what the audit below
> concludes cannot happen.
>
> The audit's argument does not cover this. It rests on the engine refusing designs below
> `VersionSpellNames` (0.998101), and **a tagged database has no `DesignVersion` at all** — its
> version is the per-record string tag, on an unrelated axis. The engine's own floor here is a
> different one: it refuses `intVer < 2` outright with "you must install a new one".
>
> **Resolved by reading the per-record tag out of all four designs**, which the container reader
> makes a two-line job:
>
> | Design | container | records | per-record tag |
> |---|---|---:|---|
> | `DefaultDesign` | `BaseclassV1` | 7 | **`Bcd1`** |
> | `SomethingWild` | `BaseclassV1` | 9 | `Bcd5` |
> | `Case` | `BaseclassV1` | 9 | `Bcd5` |
> | `Ambassador's_Letter` | `BaseclassV1` | 17 | `Bcd5` |
>
> **Nothing carries `Bcd2` or `Bcd3`, so the divergence is unreachable and the audit stands** —
> `ArchiveRole` does not need threading through this reader for the reason above. Note the
> container tag is `V1` for all four while the record tags differ: **the two version axes are
> independent**, and neither predicts the other.
>
> **A second, sharper finding: the engine cannot load `DefaultDesign`'s `baseclass.dat` at all.**
> `Bcd1` is below the `intVer < 2` floor, so the engine shows "This module contains an old
> 'baseclass.dat' file. You must install a new one before we can proceed" and calls
> `SignalShutdown()` (`class.cpp:5734`). The primary golden fixture is therefore **editor-only** for
> this database, and anything validating the engine's baseclass or levelling path has to use the
> `reference/` designs instead. This is a second blind spot in `DefaultDesign` of the same kind as
> the item-name one recorded in the items section — it is a minimal design, and minimal turns out
> to mean *old* in places.

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

##### The compressed ASL gap is closed

`GLOBAL_STATS::Serialize(CAR&)` (`GlobalData.cpp:4244`) is now walked through its attribute list on
all three compressed designs. That was the missing coverage: every ASL in `items.dat` has a count
of zero, so those walks proved only that a block could be *located*. `GLOBAL_STATS` carries four
entries, which finally exercises the key/flags/value loop, the compressed-only key fixup, and the
resolution of values held as string-table back-references.

The reader produces exactly what an independent by-hand decode of the raw bytes predicted, down to
the ordering:

```
'GuidedTourVersion'        0x05  '3.56'
'ItemUseEventVersion'      0x05  '3.56'
'RunAsVersion'             0x05  '3.56'
'SpecialItemKeyQtyVersion' 0x05  '3.56'
```

Two things are worth drawing out. All four values are the same string, so only the first is written
literally and the rest are table indices — getting identical text back for all four is proof the
back-references resolved, since a broken table returns a *wrong string* rather than an error. And
the order confirms the earlier hash-order finding independently: the uncompressed DefaultDesign
leads with `RunAsVersion`, every compressed design leads with `GuidedTourVersion`.

Note also that the file declares **5.28** while its attributes say **3.56**. These are not the same
thing and must not be conflated: the container version describes the format, the attributes record
the behaviour version the design wants, which is why the engine consults them separately.

Three details of the prefix were worth getting right. `LOGFONT` is blitted into the archive as a
raw 60-byte struct at 0.830 and above (`GlobalData.cpp:4411`) — this is an MBCS build, so
`LOGFONTA`; assuming the wide variant would be 92 bytes and desynchronise everything after.
`creditsData` exists only at 5.25 and above, and below that the credits art is a single string read
much earlier in the record, so the fixtures cover both branches. And `TITLE_SCREEN_DATA` counts
with a `DWORD` where most neighbouring lists use `int`.

Note this is the **`CAR`** path. The uncompressed DefaultDesign takes
`Serialize(CArchive&)` (`GlobalData.cpp:3855`) instead — a different function with its own field
order. A third overload at `GlobalData.cpp:4960` has an identical signature and is commented out.

#### Progress

| Layer | State |
|---|---|
| Tier 1 — plain `CArchive` | Done. `game.dat` reads to exact exhaustion on the 2.53 and 3.55 designs |
| Tier 2 — `CAR` uncompressed | Done. Three databases; counts 285 / 44 / 117 agree with the oracle |
| Tier 3 — `CAR` + LZW | Done. Verified against real encoder output at 2.53, 3.55, 5.28 and 5.29 |
| `ASL`, `Specab`, `PIC_DATA` | Done. Every record of all four designs, both sides of the 0.920 Specab fork, both ASL encodings including compressed back-references |
| `GLOBAL_STATS` | Done, both overloads. Scalars, art, both picture-import blocks and the ASL diffed against the oracle; tail covers art slots, sounds, key/special-item lists and 171 quests |
| `ITEM_DATA` | Done. 285 uncompressed records match the oracle field by field; 562 / 551 / 479 compressed at 5.28 / 3.55 / 2.53 walk to exact EOF with no code changes |
| `SPELL_DATA` | Done. 117 uncompressed match the oracle by name; 423 / 377 / 318 compressed exhaust exactly |
| `MONSTER_DATA` | Done. 44 uncompressed match the oracle; 171 / 195 / 160 compressed exhaust exactly |
| Events | Done for every fixture. 31 of the 44 design event types have readers; all 22 levels of 4 designs walk end to end — 6,234 events spanning 2.53 → 5.28 |
| `LEVEL` / `ZONE` | Done. All 22 levels of 4 designs read to their last byte — grid, events, zones, step events, wall/background sets, blockage keys |
| `CHARACTER` | Done. The format's largest record; 6 and 23 characters decode on the 2.53 and 3.55 designs, multiclass baseclass counts self-consistent |
| `PARTY` / savegames, `.CHAR`, cell contents | Done — `SaveGameReader`, `CharacterFileReader`, `CellContentsReaders`. (An earlier revision listed all three as remaining; they landed and the row was not updated.) |
| **Writers — none** | **Nothing in `UAF.Serialization` writes.** The exit criterion below is not met and cannot be met until this exists |
| Tagged databases | Framing done for all six. `baseclass.dat` (`Bcd5`) and `classes.dat` (`CL5`) read completely — 57 and 98 records across five designs, all to exact EOF. `ability.dat`, `races.dat`, `spellgroups.dat`, `traits.dat` unread |
| Event readers | 13 types have none: `Damage`, `EncounterEvent`, `EnterPassword`, `GPDLEvent`, `HealParty`, `InnEvent`, `JournalEvent`, `PlayMovieEvent`, `SmallTown`, `TakePartyItems`, `TavernTales`, `Vault`, `WhoTries` |

The pattern is established and mechanical: extend the dumper for a type → write the C# reader
→ diff. `ITEM_DATA` is the worked template.

**Reading is done; writing has not started.** That distinction matters more than the table above
suggests. Every claim here is about parsing bytes the reference produced. The exit criterion is
round-trip byte-identity, which needs `ArchiveWriter` — the LZW *encoder*, the string-interning
table on the write side, and the storing branch of every `Serialize` — none of which exists. That
is the single largest unstarted piece of Phase 1, and Phase 5's exit (the editor saves a design the
C++ editor can load) depends entirely on it.

**Fixture coverage** spans 0.915025, 2.53, 3.55, 5.28 and 5.29 — a Dungeon Craft design at each,
with the 5.29 one generated by CI itself (`-savedesign`) and dumped in the same run, so C# and C++
readings of identical bytes can be compared directly. That last property is what caught the
`SPELL_ID` mis-modelling; nothing weaker would have.

### Phase 2 — Rules and scripting VMs (2–3 months, parallelizable with Phase 3)

`UAF.Rules`, `UAF.Scripting`, `tools/gpdlc`.

These are pure computation with no platform coupling — the best-value work in the project and
fully unit-testable.

- GPDL compiler (`Shared/GPDLcomp.cpp`, 4,769) and bytecode VM (`GPDLexec.cpp`, 8,377) against
  `GPDLOpCodes.h`.
- **Correction:** `src/GPDL/language.txt`, `functions.txt` and `talk.txt` are **not** a usable
  conformance corpus, as this plan previously claimed — all three are stale, in the same way the
  dead `ProjectVersion.h` was. The corpus is now purpose-written scripts under
  `oracle/golden/gpdl/`. Detail in the Phase 2 status section below.
- The Forth VM (`UAFWin/Forth.cpp`, 2,534) used for spell effects.
- `GameRules.cpp` (4,167), `Specab.cpp` (2,240), `Money.cpp` (2,026).

**Exit:** `gpdlc` produces byte-identical bytecode to the C++ compiler for every script in the
fixture corpus; the VM produces identical execution traces.

#### Phase 2 status — GPDL

Delivered: `dotnet/src/UAF.Scripting/` and `dotnet/tools/gpdlc/`, with 201 tests in
`dotnet/tests/UAF.Scripting.Tests/`. Format details and language traps are in
[SERIALIZATION.md §11](SERIALIZATION.md).

| Item | State |
|---|---|
| `GPDLOpCodes.h` — 11 `BINOPS`, 386 `SUBOPS`, 29 `COPS`, 14 `CTX_*` | Transcribed mechanically from the header, not by hand |
| `systemfunctions[]` — 339 rows — and `requiredContexts[]` — 13 | Generated from GPDLcomp.cpp for the same reason |
| Tokeniser (`INPUTFILE`, GPDLcomp.cpp:70–745) | Complete |
| Compiler (`GPDLCOMP`, GPDLcomp.cpp) | Complete for the `talk.bin` path |
| `talk.bin` writer and reader | Complete |
| Assembly listing (`GPDLCOMP::list`) | Complete — the highest-value oracle artefact, since a mismatch names an address and a mnemonic |
| VM control flow, frame protocol, both arithmetic families, string and delimited-string ops | Complete |
| ~250 character / party / combat / special-ability sub-opcodes | **Not ported** — each throws `NotSupportedException` citing its source line |
| `$GREP` / `$WIGGLE` | **Not ported** — needs the vendored Spencer engine (`regexp.cpp`); routed through `IGpdlHost` |
| `CompileScript` embedded-script blob and `BINOP_FETCHTEXT` | **Not ported** — a second, different container (SERIALIZATION.md §11) |
| `RDRCOMP`/`RDREXEC` (GPDLcomp.cpp:4131, GPDLexec.cpp:8249) | **Not ported** — a separate byte-coded expression language that happens to live in the same files |
| Forth VM (`UAFWin/Forth.cpp`) | Not started |

**Exit criterion 1 is not met, and cannot be met from this platform.** Byte-identity needs the
reference `GPDLcomp.exe`, which only the Windows oracle can run. The diff harness is in place —
`GpdlOracleDiffTests` compares bytes and listing lines against `oracle/golden/gpdl/*.{bin,lst}` and
returns early when absent, the same pattern as `OracleDiffTests`. Two cautions for whoever adds the
workflow step: GPDLcomp calls `gets_s` after every error message, so a failing compile **hangs the
runner** unless stdin is redirected from NUL; and it always exits 0 (`GPDL.cpp:111`), so the step
must check the `.bin` exists and is non-empty rather than trusting the exit code.

**The named conformance corpus does not compile — with either compiler.** `src/GPDL/talk.txt` calls
`$GET_CHAR_CHA`, `$SET_CHAR_CHA`, `$Race` and `$Class`; none survive in `systemfunctions[]`, and the
reference compiler rejects the file at `talk.txt:357` exactly as this port does. It is a stale
sample from an earlier revision of the table, not a test suite. `TalkCorpusTests` asserts the
rejection (the message and line number are directly comparable against the reference's stderr, which
makes it the cheapest whole-front-end oracle check available) and then compiles a repaired in-memory
copy — four documented substitutions of same-arity, same-typing functions — which yields 860 code
words, 153 pool entries and 14 public functions. The goldens the oracle produces should therefore be
small purpose-written scripts committed *alongside* their expected output, not `talk.txt`.

Exit criterion 3 is met: `dotnet test dotnet/UAF.slnx` passes on macOS, Linux and Windows.

### Phase 3 — Media layer (1.5–2 months, parallelizable with Phase 2)

`UAF.Media`, `UAF.Media.Avalonia`.

- Software framebuffer with DirectDraw-equivalent blit/colour-key/transparency semantics.
- Surface and sprite stores mirroring `Graphics`/`SurfaceCacheMgr` (`Shared/Graphics.h:94,115`).
- Bitmap font rendering (`CDXBitmapFont.cpp`, `DrawFont` with `FONT_COLOR_NUM` tag handling).
- `IAudioBackend` + MIDI synth; `Channel`-based replacements for the three audio threads.

**Exit:** a test harness renders known tile/sprite/font sequences and the framebuffer hash matches
screenshots captured from the C++ build.

#### Progress

Scaffolded as **three** projects rather than the two named above. `UAF.Media` is the
platform-agnostic half and has **no native dependency at all**; `UAF.Media.Sdl` holds the window,
presentation, input and audio device. `UAF.Media.Avalonia` is still unwritten and belongs with
Phase 5, since its only consumer is the editor.

| Piece | State |
|---|---|
| Framebuffer | `Surface`, `SurfaceRect`, `SurfaceKind`. ARGB8888 only; the original's 15/16/24bpp paths are dropped, since a managed buffer has no reason to carry four blitters |
| Blitter | Opaque, colour-keyed, alpha, keyed-alpha, mirrored, darken, fill. Ported from `cdxsurface.cpp`'s 32bpp cases with `ValidateBlt`'s clipping |
| Surface store | `SurfaceStore`, including the key allocation and the reserved front/back/mouse buffer keys |
| Sprites | `SpriteSheet` (frame grid) + `AnimatedSprite` (the `PIC_DATA` state machine over an injected clock) |
| Input | `VirtualKey` on the Win32 numbering, `InputEvent`, `IInputSource`, `RecordedInputSource`; SDL scancode translation |
| Presentation | `IPresenter` with an SDL3 streaming-texture implementation and a frame-hashing headless one |
| Audio | WAV/MP3 decoders, MeltySynth MIDI, resampler, software mixer, deterministic music queues, `IAudioBackend`, SDL3 device |
| Video | `IVideoDecoder`/`IVideoDecoderFactory` + `MoviePlayer` (timing, letterboxing, skip). **The FFmpeg adapter is not written** — see below |
| Bitmap fonts | **Done.** `FontAtlas` + `BitmapFont` (portable) and `SdlFontRasterizer` over SDL3_ttf, with PT Serif bundled in all four styles as the substitute for Windows' `SYSTEM` raster face |
| Image loaders | **PNG done**, hand-written and verified against libpng on all 1312 reference PNGs. `ImageLoader` sniffs signatures as the C++ did; BMP/PCX/JPG/TGA are recognised but not decoded — see below |

341 tests, green on a machine with no display and no sound card. The SDL tests drive the real
backend on the dummy drivers rather than mocking it.

##### Fonts: what ported cleanly, and what is still open

`CDXBitmapFont` splits neatly in two. Everything after the atlas exists is blits and accumulators
— measure by summing advances, draw by blitting each cell and stepping X — and that half is now
`FontAtlas` + `BitmapFont`, with 18 tests against authored atlases so the layer cannot come to
depend on where glyph pixels came from.

Facts established while porting it:

- **Text is bytes, not UTF-16.** Glyph lookup is by `unsigned char` over 256 cells
  (`CDXBitmapFont.cpp:533`), on the same Windows-1252 assumption the serialization layer records.
  The API takes `ReadOnlySpan<byte>`; the `string` overloads encode first. Casting a `char` to a
  byte would misindex every character above 127 — `Œ` is U+0152 but byte 0x8C.
- **Designs request only four faces.** Parsing the `LOGFONT` rather than skipping it (which the
  reader previously did) shows: `SYSTEM` at height 16 for dc-default and the CI fixture — and
  `SYSTEM` is also the engine's own substitute when a design leaves the face empty
  (`GlobalData.cpp:3901`) — plus `Times New Roman`, `System` and `Garamond` at −13.
- **The original already coped with missing fonts.** It calls `EnumFontFamiliesEx` and warns
  "Cannot find specified font named %s" (`GlobalData.cpp:5846`). Resolving `Garamond` on Linux is
  therefore the same problem the original had on a machine without it, not a new one the port
  introduces.
- **Advance always equals cell width.** Both come from one `GetTextExtentPoint32` result, so the
  font has no kerning and no side bearings.

One deliberate deviation: the original rasterises **eleven separate atlases, one per colour**
(`GlobalData.cpp:5964-5975`), because GDI baked the colour in at `TextOut` time. This port keeps
one atlas and tints at draw time. That is better rather than merely cheaper — GDI's antialiased
edges blend glyph colour toward the background, and since the colour key removes only *exact*
background matches, the original's non-white fonts carry wrong-hued fringes.

**The rasteriser** is `SdlFontRasterizer` over `ppy.SDL3_ttf-CS`. It measures all 256 codepage
bytes, packs them with `FontAtlas.Layout`, and renders each into its cell — the original's
structure, minus the per-glyph clip region it needed (`CDXBitmapFont.cpp:277-282`) to stop
descenders bleeding into neighbouring cells, which cannot happen when each glyph is rendered to
its own surface.

Three things settled while building it:

- **The advance comes from `TTF_GetStringSize`, not from the rendered bitmap's width.** They are
  different numbers, and the original used the former (`GetTextExtentPoint32` reports the advance).
  A space renders to an empty bitmap but has a real advance; taking the surface width would
  collapse every gap between words in the game.
- **Coverage is stored as a grey ramp, not thresholded.** The atlas cannot use its alpha byte —
  `Surface` treats alpha as meaningless and forces it opaque — so glyphs are rendered white on
  black and the grey level *is* the coverage, which `BitmapFont` reads back as a blend weight. An
  earlier revision thresholded it, which does not produce aliased text: it produces text **dilated
  by about a pixel all round**, and it passed every other test.
- **Antialiasing off remains the faithful default**, since `SYSTEM` is a raster face. With coverage
  stored properly the blend degenerates to an exact flat replacement at 1-bit, so the option costs
  the faithful path nothing.

**The substitute face is PT Serif** (SIL OFL 1.1, © ParaType), bundled in all four styles —
regular, bold, italic, bold-italic — rather than synthesising the last three, because designs carry
weight and italic in their `LOGFONT` and SDL_ttf's synthetic italic is a shear, not a set of drawn
letterforms. About 840 KB embedded. It is a text face rather than a display one, which suits what
this engine actually draws: dense UI at 13 and 16 pixels. It is not a metric match and nothing
could be one, since the original's advances came from a bitmap face, so text will not wrap
identically to the C++ build.

**A correction worth keeping.** An earlier revision of this section claimed the bundled face lacked
Windows-1252 `0x80`–`0x9F` and shipped a substitution table to paper over it. There was no gap. The
audit had checked whether Unicode `U+0080`–`U+009F` were present — those are C1 control characters
and no font has them — instead of the codepoints Windows-1252 actually maps that block to,
`U+2018`…`U+2026`. Both the finding and the table it justified were deleted. This is the third
instance in this port of a conclusion drawn from the wrong first lookup rather than from the
source; the other two are recorded in §6.2 and in the type-trap list of `docs/SERIALIZATION.md`.

##### The image decoder, and the SDL3_image question

`PngDecoder` is hand-written over `ZLibStream`: a chunk walk, five row filters and a pixel unpack.
It is diffed against Pillow — which wraps the same libpng the C++ used — across every PNG in the
reference designs, 1312 files, byte-identical. `tools/art-oracle.py` regenerates the digests, and
the digests are committed so the test needs no Python, exactly as the C++ dump works for
serialization.

Three semantics were worth the trouble to get right, none of them guessable from the file format:

- **Alpha is discarded.** `png_set_strip_alpha` plus `PNG_TRANSFORM_STRIP_ALPHA`
  (`cdximagepng.cpp:105,124`), and `tRNS` is never read. Transparency comes from the colour key —
  the top-left pixel — so honouring real alpha would look like an improvement and break every
  keyed sprite.
- **`png_set_bgr` and the bottom-up row writes are not ported.** Both are Windows-DIB plumbing that
  cancel against each other: a 24bpp DIB stores blue first, and `m_IsInverted` defaults to `FALSE`
  (`cdximagebase.cpp:55`) so `biHeight` stays positive, i.e. bottom-up. Porting one gives a flipped
  or colour-swapped image; porting both looks correct in a colour test and is still upside down.
- **Gamma.** Exponent `1 / (file_gamma × screen_gamma)` with `screen_gamma` 2.2 and a default file
  gamma of 0.45455 — chosen by the original author so the common case is the identity. It is: 1302
  of 1312 files are untouched. The 10 that are not are all `SomethingWild` art declaring 0.55531,
  and all truecolour, so the question of whether libpng gamma-corrects a palette in place never
  arises here.

**`SDL3_image` was dismissed for a wrong reason and deserves a second look.** It is fully
cross-platform, and `ppy.SDL3_image-CS` — same publisher as the `ppy.SDL3-CS` already in use, same
version stream — ships natives for all 13 RIDs the core package covers, MIT over zlib. The real
arguments for the managed decoder are narrower than "no native dependency": it keeps `UAF.Media`
headless-testable, and a general-purpose decoder would not reproduce the stripped alpha, the gamma
convention or the 16-bit chop, so the fiddly part of this file would survive anyway.

The legacy formats go to **SDL3_image**, via `ppy.SDL3_image-CS` in `UAF.Media.Sdl` — decided
rather than deferred. `IImageDecoder` is the seam, shaped exactly like `IVideoDecoderFactory`:
optional, probed at construction, and absent by default (`NullImageDecoder`), so a build without
SDL still loads PNG. `SdlImageDecoder` deliberately does *not* claim PNG, because the managed
decoder reproduces the engine's behaviour and is corpus-verified; routing PNG through a
general-purpose decoder would change 1312 files to gain nothing.

Verified against the real files: 5 PCX and 3 BMP match Pillow byte for byte, and the 8 JPEGs are
compared by per-channel mean with a 1.5/255 tolerance, because JPEG's IDCT is specified only to a
precision and two conformant decoders legitimately differ. A whole-image mean would survive a
sheared or vertically flipped decode, so a top-left-quadrant mean is checked as well — that is the
failure a pitch or row-order mistake actually produces.

One accepted gap: SDL3_image has no PSD loader, where the C++ had `cdximagepsd.cpp`. No reference
design ships a PSD.

Interlaced PNG is refused outright rather than guessed at. No file in any shipped design is
interlaced; libpng handled Adam7 transparently, so this is a real gap, left explicit in the same
style as the unported serialization branches.

##### Two findings that change the plan

**`Environment.SetEnvironmentVariable` cannot force SDL headless on macOS or Linux.** .NET keeps its
own copy of the environment on Unix and never calls `setenv`, so a native library's `getenv` does not
see it. It *does* work on Windows — headless that appears to work on one platform of three.
`SdlPlatform.ForceDummyDrivers` uses `SDL_SetHintWithPriority(…, SDL_HINT_OVERRIDE)` instead;
`SDL_HINT_OVERRIDE` is required because SDL consults a real environment variable ahead of a
normal-priority hint. Setting the variables from outside the process, as the workflow does, still
works and takes effect first.

**Every C# FFmpeg binding is LGPL v3, which is a stronger constraint than §6.1 recorded.** §6.1
considered FFmpeg's own licence (LGPL 2.1-or-later, fine under GPL v2) but not the bindings':
`FFmpeg.AutoGen` and `Sdcb.FFmpeg` are both LGPL-3.0, and `FFMediaToolkit` (MIT itself) depends on
the former. Linking any of them makes the combined work GPL v3 — permitted, because this project is
GPL v2 *or later*, but it removes the v2 option for any build that includes it. That is a second,
independent reason to keep video in its own optional assembly, on top of the packaging one.

`FFMediaToolkit` also pins `FFmpeg.AutoGen` 7.1.1, so it loads FFmpeg **7** and fails against
FFmpeg 8. The adapter was left unwritten rather than shipped unverified: it cannot be exercised in
CI (no FFmpeg) and could not be exercised locally either (FFmpeg 8 installed). The abstraction is in
place and its degradation path — `IsAvailable` false, `Start` returns false, the cutscene is
skipped — is tested, which is the behaviour §6.1 actually requires.

##### Notes for whoever continues

- **The alpha argument runs backwards.** CDX blends with `out = ((A * (dst - src)) >> 8) + src`,
  A in 0..256, so A weights the *destination*: 0 draws the source opaquely, 256 leaves the
  destination untouched. `PIC_DATA::AlphaValue` and `BlendAmount` come out of design files in those
  terms, so the port keeps the convention rather than inverting it in two places.
- **A surface's colour key is its top-left pixel** (`CDXSurface::SetColorKey()` with no argument).
  Nothing in the file format records it.
- **`RestartFrame` is 1-based.** `Graphics::SetNextFrame` smuggles it through CDX's unrelated sprite
  `Type` field and subtracts one (`Graphics.cpp:914`).
- **`Graphics::BlitImage`'s `flipY` is a horizontal mirror.** It calls `DrawTransHFlip`, which asks
  DirectDraw for `DDBLTFX_MIRRORLEFTRIGHT`. CDX's software fallback for that blit also has an
  off-by-one that writes one pixel past the right edge (`cdxsurface.cpp:2074`); it only ran when the
  hardware blit failed, so the port follows the DirectDraw behaviour.
- **Colour keys compare 24 bits.** CDX masks the top byte off first, so a key matches on RGB alone.
- **MIDI needs a SoundFont this repository does not ship.** MeltySynth is a synthesiser, not a
  device; with no SoundFont configured (`UAF_SOUNDFONT`), `.mid` entries are skipped the way a
  missing file is. That is the same optional-asset contract movies have.
- **The music queues are `IPcmSource`s, not threads.** The original ran three `CThread`s blocking on
  Win32 events; here a queue advances because the mixer read past the end of the current entry, so a
  playlist can be played through in a test. One deliberate divergence: `SoundQueue::Thread` ends the
  whole queue when a file will not play, silencing everything after it; the port skips the entry and
  reports it.

### Phase 4 — UAFcore engine (4–6 months)

#### What runs today

`UAFcore` opens a design, loads a level, walks a party around it on the torus, renders the viewport
and the party roster, and runs events with chaining. `--dump <path>` renders one frame and exits, so
the whole engine is smoke-testable with no display. 106 tests.

**Nine of the 44 event types execute.** Five through `EventRunner`, which presents and takes an
answer — `TextStatement`, `QuestionButton`, `QuestionList`, `QuestionYesNo`, `NPCSays` — and four
through `Game.ExecuteWithoutInput`, which needs neither: `PassTime`, `Teleporter` (same level),
`GainExperience`, and `GiveTreasure` **in its silent form only**, since the other form opens a
screen that does not exist yet.

Every other type draws `[<name> here -- not implemented]` in the text box rather than being silently
skipped, so a walk through a real design reads as an honest map of what is left.

#### Where to resume, and in what order

The text and menu layers are done, and they were the piece blocking half the content layer. What
remains, in dependency order:

1. ~~**Levelling.**~~ **Done** — see the rules section below. Both databases it was blocked on now
   read completely, and `UAF.Rules.Levelling` turns experience into a level, answers
   ready-to-train and applies a training session. `ability.dat`, `races.dat`, `spellgroups.dat`
   and `traits.dat` remain unread; only the race one is currently missed, and only for a design
   that caps a level by race.
2. **The forms.** The shared engine is **done** — `TEXT_FORM` is ported as `TextForm` in
   `UAF.Media`, with `ItemsForm`, `RestTimeForm` and `CharStatsForm` on top of it (42 tests
   between the four). `SpellForm` is ported too, so **all five forms are done**. `CharStatsForm`'s layout is complete
   but half its values wait on `GameRules.cpp` — see below. The sheet is wired to the treasure
   screen's VIEW entry, so the character screen is reachable in play.
3. **Combat.** `Combatant.cpp` (11,694), `Combatants.cpp` (8,952) and `path.cpp`, with the combat
   math in `UAF.Rules`, which today holds only the currency. Nothing of this is started.
4. **The remaining viewport squares**, 3 and 4.
5. **The engine thread and the `CProcinp` task scheduler** (§4.4). The engine is still a synchronous
   loop; nothing has needed the scheduler yet, but `TASKSTATE` numbering is serialized into save
   games and must be preserved when it lands.
6. **`EVENT_CONTROL`'s remaining pieces**: the chain that lets several events share a cell, and the
   happened/not-happened flags `PARTY` carries — the latter is what makes `OnceOnly` work, and it
   connects to the savegame, which already reads those flags. The trigger conditions themselves are
   ported (`EventTrigger`); the two needing a spellbook or a running GPDL VM report `Unknown`
   rather than guessing.
7. **Screenshots from a Windows C++ build.** `GoldenFrameTests` guards regressions but is *not* an
   oracle — it can only say today matches yesterday. Phases 4 and 5 are most of the remaining work
   and have no equivalent of the serialization dump to diff against.

##### `CLASS_DATA`, as ported

`ClassRecordReader` reads a `CL5` record completely (`class.cpp:7936`): tag, `preSpellNameKey`,
name, a `ReadCount`-framed **baseclass list**, a `Specab` block, a bare-`int`-counted
`HIT_DICE_LEVEL_BONUS` list, a `DICEPLUS`, an `ITEM_LIST` of starting equipment, and
`hitDiceBaseclassID`. Every leaf it needs already existed — `SpecabReader`, `DicePlusReader` and
`MonsterLeafReaders.ReadItemList`.

**The baseclass list is what levelling wanted from this file.** `ci-tier3` yields
`Cleric/Fighter → [cleric, fighter]`, so the experience split can divide by a real count instead of
assuming one.

Three things differ from `BASE_CLASS_DATA` and matter:

- **This record needs the design version passed in.** `BASE_CLASS_DATA` hard-codes 0.930 for its
  `Specab`; `CLASS_DATA` uses `globalData.version` for both its `Specab` and its starting equipment
  (`class.cpp:8043`, `:8134`). So `game.dat` must be read first, and the same file read against the
  wrong version takes the wrong `Specab` branch. The two sibling databases genuinely disagree here.
- **`HIT_DICE_LEVEL_BONUS` serializes `baseclassID` before `ability`** (`class.cpp:7507`) while the
  struct declares `ability` first. Both are strings, so transposing them swaps two plausible
  identifiers and nothing detects it — the same trap as the hit-dice field order in
  `BASE_CLASS_DATA`.
- **The reference discards starting-equipment entries whose item the design does not define**
  (`Items.cpp:1700`). This reader keeps them: dropping records during parsing would make output
  depend on load order.

Verified the same way, by whole-file walks:

| Design | Version | Records | Result |
|---|---|---:|---|
| `SomethingWild` | 3.55 | 20 | exact EOF |
| `Case` | 2.53 | 20 | exact EOF |
| `Ambassador's_Letter` | 2.53 | 19 | exact EOF — custom `Ninja`, `Trooper`, `Hoplit → fighter` |
| `dc-default` | 5.28 | 20 | exact EOF |
| CI-saved 5.29 | 5.29 | 19 | exact EOF — includes the `Cleric/Fighter` multiclass |

98 records, all landing on the last byte, and all read on the first attempt.

> **Two honest gaps.** `DefaultDesign` carries **`CL1`**, which this reader refuses — the same way
> its `baseclass.dat` is `Bcd1`. So the only sub-0.920 design available is exactly the one that
> cannot be read, and **no fixture exercises the legacy `Specab` branch for this file**; that the
> version is load-bearing is established from the source, not demonstrated by data.
>
> Also: every design's last class is **`$$Help`**, a pseudo-class with an empty baseclass list.
> That is genuine data — the record decodes as a normal `CL5` and the file still ends exactly after
> it — and `baseclass.dat` carries the same sentinel. A test asserting "every class has a
> baseclass" fails on it, which is how it was found.

##### `BASE_CLASS_DATA`, as ported

`BaseclassRecordReader` reads a `Bcd5` record **completely**, so a whole file walks and the
experience thresholds levelling needs are available. The field order is below, and the correction
that took two attempts to find is after it.

Verified without an oracle, by decoding to published values: `SomethingWild`'s first baseclass comes
out as `assassin` with Strength 12–19, Intelligence 11–18, Dexterity 12–19, the six standard races,
and thresholds 1501 / 3001 / 6001 / 12001 / 25001 / 50001 / 100001 / 200001 / 300001 — the AD&D
assassin's own tables. `Ambassador's_Letter` yields a custom `ninja` restricted to its invented
`Helmettiger` race, which shows the race list is read as authored strings rather than matched
against a fixed set. A stream drifted by two bytes reproduces none of that.

**Only `Bcd5` needs porting to begin with.** All three `reference/` designs carry it, and
`DefaultDesign`'s `Bcd1` is one the engine refuses outright (above), so there is no fixture for the
older shapes and no engine path that would use them.

The `Bcd5` field order, from `class.cpp:5745` onward, with the editor-only blocks (`intVer < 4`)
skipped as unreachable at 5:

| # | Field | Notes |
|---:|---|---|
| 1 | record tag | `car >> ver` — a **second** tag, per record, distinct from the container's |
| 2 | `m_preSpellNameKey` | `intVer >= 5` only |
| 3 | `m_name` | `"*"` → `""`, the usual sentinel |
| 4 | ability requirements | `ReadCount()` then N × `ABILITY_REQ` |
| 5 | allowed races | `ReadCount()` then N × `RACE_ID` — **strings**, like every other `*_ID` |
| 6 | `m_expLevels` | `mCArray<DWORD, DWORD>` — the experience thresholds levelling needs |
| 7 | `m_allowedAlignments` | `ver >= "Bcd1"`, else defaults to `0x1ff` |
| 8 | `THAC0` | raw blob, `car.Serialize(THAC0, sizeof(THAC0))` |
| 9 | `m_spellBonusAbility` | |
| 10 | bonus spells | `car >> n` — a plain int, **not** `ReadCount()` — then N × `BYTE` |
| 11 | casting info | `car >> n` then N × `CASTING_INFO::Serialize` |

`ABILITY_REQ::Serialize`'s loading branch (`class.cpp:2778`) is: version string (`"ABL1"`;
anything else is rejected with "Unknown ABILITY_LIMITS version"), `m_abilityID` (a **string**),
then `m_min`, `m_minMod`, `m_max`, `m_maxMod`.

Both leaf types are now settled. **`ABILITY_REQ`'s four limits are `short`** (`class.h:988`), not
`int` — reading them wide drifts eight bytes per requirement. And **`car >> CArray<DWORD,DWORD>` is
an `int` count followed by a single bulk `decompress`** of `size * 4` bytes (`class.cpp:12046`), not
`size` separate reads — and that count is a plain `int` where items 4 and 5 use `ReadCount()`, so
this one record genuinely uses both framings.

**Items 7–11 are transcribed but not shipped, and the missing piece is now known.** The widths, all
confirmed from the header: `m_allowedAlignments` is a **`WORD`** (`class.h:1830`), `THAC0` is
`char[40]` (`HIGHEST_CHARACTER_LEVEL`, `Externs.h:199`), `m_spellBonusAbility` is a `CString`, bonus
spells are a bare `int` count then N × `BYTE`, and `CASTING_INFO` (`class.cpp:12372`) is two strings
plus three blitted tables of 40 × 9, 25 and 25 bytes (`MAX_SPELL_LEVEL` = 9,
`HIGHEST_CHARACTER_PRIME` = 25). `CAR::Serialize(char*, n)` is a plain n-byte `decompress`
(`class.cpp:12064`).

A first attempt with exactly those widths still desynchronised, and a byte-level bisect of the 96
bytes after record 0's experience levels showed why: **the record does not end after the casting
info.** A `Specab` block follows (`class.cpp:6136`), gated `ver > "Bcd2"` — the bytes that looked
like record 1's tag are its count, and the string `baseclass_NameSuppress` sitting there is a
special-ability key, not a name.

```cpp
if (ver > "Bcd2")
{
  double version = globalData.version;
  if (intVer >= 5) version = 0.930;   // NOT the tag, and NOT the design version
  m_specAbs.Serialize(car, version, m_name, "baseclasses");
}
```

**Two things about that call are load-bearing.** The version is hard-coded to **0.930** at
`intVer >= 5`, with the source's own explanation: people package old designs with a new
`baseclass.dat`, so the real design version would send `Specab` down the wrong branch. Since 0.930
is above the 0.920 legacy gate, a `Bcd5` record always takes the **modern `A_CStringPAIR_L`** path.
And the map name is `"baseclasses"`, which `SpecabReader` checks as a sync marker.

**Correction: `Specab` was not the end of the record, and "transcription plus one call" was wrong.**
That claim came from reading the bytes where drift appeared rather than reading
`BASE_CLASS_DATA::Serialize` to its end. Items 7–11 plus `Specab` decoded record 0 exactly as the
byte map above predicts, down to `baseclass_NameSuppress = "Y"` — and then record 1's tag came out
as `Dexterity`. Reading on from `class.cpp:6176` shows what follows:

| | Structure | Framing |
|---|---|---|
| 12 | hit dice | 40 × (`sides`, `nbr`, `bonus`), **no count** — and that is the *wire* order, not `DICEDATA`'s declaration order |
| 13 | `m_skills` | bare `int` count, N × `SKILL` (`class.cpp:4879`) — a string and an int |
| 14 | `m_skillAdjustmentsAbility` | `class.cpp:5336` — 2 strings, a `char`, then a blitted **50** bytes (25 × `short`) |
| 15 | `m_skillAdjustmentsBaseclass` | `class.cpp:5371` — same shape, blitted **80** bytes (40 × `short`) |
| 16 | `m_skillAdjustmentsRace` | `class.cpp:5388` — same shape, but a **single `short`**, not a table |
| 17 | `m_skillAdjustmentsScript` | `class.cpp:5405` — **three strings**, no type byte and no table |
| 18 | `m_bonusXP` | `class.cpp:5354` — a string, a `char`, then a blitted **100** bytes (25 × `int`) |

**All of it is now implemented, and `baseclass.dat` reads end to end.** The four adjustment families
are the trap: they look interchangeable in the source and have four different widths, so
transcribing one and reusing it drifts by up to 80 bytes per entry.

Verified by the whole-file assertion, which this format makes both cheap and decisive — a tagged
database has no per-record length, so record *n* is reachable only by having consumed *n−1* exactly:

| Design | Records | Result |
|---|---:|---|
| `SomethingWild` | 9 | exact EOF — `assassin, cleric, druid, fighter, magicUser, paladin, ranger, thief, $$Help` |
| `Case` | 9 | exact EOF — same nine |
| `Ambassador's_Letter` | 17 | exact EOF — including the design's own `ninja`, `randamdi`, `larcener`, `defender` |
| `dc-default` | 9 | exact EOF |
| CI-saved 5.29 | 13 | exact EOF — the seven `LoadUADefaults` names plus six lowercase |

57 records across five designs, every one landing on the last byte.

> **The lesson is the one this document keeps re-learning, and this time the document itself
> misled.** The byte map was right and the transcription from it was right; what was wrong was
> concluding "that is the last piece" from where the drift *appeared* rather than from reading the
> loading branch to its end. A byte map only shows you the structure you already suspect.

> **`baseclass.dat` now has an oracle, and it has been diffed.** `DumpJson.cpp` emits
> `baseclassNames` for every record plus full detail for the first three.
>
> **The dump is not describing the file, and that is a trap worth stating plainly.** The golden
> reports **13** baseclasses for `DefaultDesign` while its `baseclass.dat` holds **7**. Neither is
> wrong: the file's records are `Bcd1`, below the floor at `class.cpp:5731`, so the reference
> refuses them and `LoadUADefaults` supplies 13 built-ins instead. Comparing the dump against the
> file directly would look like a 6-record disagreement and be neither implementation's fault.
>
> Those defaults *are* readable, because the workflow's `-savedesign` pass writes them out at 5.29
> as `Bcd5` — the design kept at `reference/ci-tier3`. Reading it back gives
> `Fighter, Cleric, Ranger, Paladin, Magic User, Thief, Druid` plus six lowercase duplicates, in
> exactly the golden's order, and consumes the file to its last byte. The C++ wrote those records
> and the C# reads them back, which is a real cross-check rather than the reader confirming itself
> — and it is the first thing to have independently corroborated the EOF-walk evidence.

> The reverted attempt is the point of this section. A drifted reader on this file produces
> plausible-looking records, and `baseclass.dat` has **no oracle** — the dumper does not emit it,
> and `DefaultDesign`'s `Bcd1` is a version the engine refuses outright. Shipping it unverified
> would have been the one mistake this port has consistently avoided.

##### The treasure screen, as ported

`GIVE_TREASURE_DATA`'s non-silent path now runs: the "You Have Found Treasure!" message, the item
list through `ItemsForm`, and the six-entry bar — VIEW, TAKE, POOL, SHARE, DETECT, EXIT. TAKE hands
the pile over and EXIT chains; the other four name themselves rather than doing nothing. Verified by
rendering a real treasure event out of `Case.dsn`: `QTY 1 / NAME Battle Axe`, the message, and the
bar with VIEW selected.

- **The runner owns no party, so TAKE is a request rather than an action.** `EventRunner` records
  `TakeRequested` and `Game` acts on it — and it has to be read *before* the runner is reset, which
  is why `UpdateEvent` captures the event first. The silent form never reaches the runner at all;
  `ExecuteWithoutInput` consumes it.
- **The item list needs the item database.** A carried instance names its `m_uniqueName` while
  `m_idName` is what a player should see, so the runner takes a resolver from the host and falls
  back to the raw id rather than showing a blank row.
- **No READY or COST column.** A pile on the floor is neither an inventory nor a shop, and the
  headers are blanked rather than removed so the name column does not move.

**A full-screen event replaces the dungeon view rather than drawing over it**, and getting this
wrong is what first put the item list on top of a corridor wall. The distinction is in the screen
routines: text and question events run under `UpdateAdventureScreen`, which calls `updateViewport`;
the treasure screen runs under `UpdateSmallSprite` (`Screen.cpp:340`), which clears the adventure
background and blits the zone's treasure picture where the viewport was, never touching the viewport
itself. `EventRunner.OwnsScreen` models that, and every form-bearing screen still to come —
character stats, spells, camp — belongs in the second group.

`OnUpdateUI` (`RunEvent.cpp:6694`) greys entries rather than hiding them: TAKE goes when there is
nothing to take, POOL and SHARE are mutually exclusive on whether the money is pooled, and DETECT
needs a caster and a zone allowing magic. Note its `setItemInactive` indices are **one-based**, so
its 2/3/4/5 are TAKE/POOL/SHARE/DETECT. DETECT is disabled unconditionally here rather than offered
and broken, since neither spell memorisation nor zone magic flags are modelled yet.

> **Three things the screenshots showed that the tests did not.** The overlap above, which four
> passing tests said nothing about. That a non-silent treasure event is rarer than expected — of the
> four reference designs only `Case.dsn` has one with items, which is why the silent path was the
> only one exercised for so long. And that `SomethingWild` has a non-silent treasure with **no**
> items, which is what `GoldenFrameTests`' south scene walks onto: once the viewport stopped being
> drawn under an event, that golden was hashing a blank rectangle. It now dismisses the event before
> measuring, because it exists to guard the dungeon view and event presentation is covered
> elsewhere.
>
> **The treasure picture is still not drawn**, so the viewport area is empty rather than showing the
> zone's art. It is zone data rather than event data, and no zone art is loaded yet.

##### The form engine, as ported

`TEXT_FORM` (`UAFWin/TextForm.cpp:49`) becomes `TextForm` in `UAF.Media`: relative layout, column
alignment, tab order and mouse hit-testing over a table of `FormField`s. It is the reason the other
four forms are small — each is an array of field descriptors and code that fills in values, not
drawing code.

- **A field id is not a number.** Its high bits carry `tab`, `white` and `green`, while the
  `x_relative` / `y_relative` values carry `sel`, `end`, `right`, `rightJust` and `autorepeat` —
  two different flag sets in two different places. Comparisons go through `fieldNumMask`
  (`0x30ffffff`), which **keeps the colour bits**: two fields differing only in colour are
  different fields.
- **`autorepeat` is tested with `==`, not `&`** (`TextForm.cpp:58`), which is how it can share bits
  with `end` and `sel` unambiguously. Its row is not a field: `y` is the row count and `x` is how
  many following fields make up a row. Generated rows add `repeatIncr` to their ids once per row
  and stack below the row above.
- **Placement is resolved in table order** — a field positioned against another reads that field's
  already-computed box. The reference asserts; this port throws, because an unplaced anchor puts
  the field at 0,0 and reads as a layout bug rather than a table-ordering one.
- **A click returns the largest field by area, not the first.** Selection boxes overlap the text
  inside them, so first-match returns the label instead of the row.
- **Columns are pushed right until each clears every column before it**, using the widest width and
  space seen. That is what lines a variable-width table up with nobody measuring it in advance.
- **Markup is not interpreted**, exactly as in menus — the original disables font colour tags for
  the duration of a draw.

##### `CharStatsForm`, as ported

The character sheet, and the biggest of the forms at 2,064 lines. **The layout is ported in full;
the population is not** — `showCharStats` is 1,083 of those lines because it *derives* most of the
lower half. Armour class, THAC0, damage, encumbrance and movement all come from `GameRules.cpp`,
which this port has not reached, so those fields are laid out and left blank rather than filled with
plausible numbers. A wrong armour class looks exactly like a right one.

- **Three colour groups in one enum.** `STF_white = TEXT_FORM::white`, then
  `STF_green = TEXT_FORM::green`, then `STF_Str = green + tab` — each re-assignment restarts the
  count, so a field's colour and its tab-stop-ness ride in its id. `ItemsForm` uses the trick once;
  this uses it three times. Note which fields land where: coin *labels* are white while coin
  *amounts* are green, and both the ability labels and their values are green.
- **Up to three experience lines**, one per baseclass a multiclass character advances in, and the
  unused ones still get a placement because the level line hangs off the first.
- **The six ability selection fields are never drawn**, the same as `RestTimeForm` — they name the
  tab stops and the highlight goes on the value beside each. Unlike `ItemsForm`'s row marker,
  nothing has flattened their flags; `showCharStats` simply never gives them text. Three forms, and
  in all three the "selection rectangle" is not one.

`CharacterSheet` takes every field as a string, including the derived ones, so the combat block can
be filled in later without touching the form.

##### The character sheet in play

VIEW on the treasure screen now opens the active character's sheet, built by
`CharacterSheetBuilder` from a `Character` plus the design's baseclasses. Verified by rendering it
out of `Case.dsn`: name, `MALE 28 YEARS`, `TRUE NEUTRAL`, `FIGHTER`, `FIGHTER 4001`, `LEVEL 3`,
`STATUS OKAY`, `HIT POINTS 27/27`, `HALF-ELF`, the five configured coins and the six scores.

**Screen ownership turned out not to be one flag.** The treasure screen keeps the party roster —
`UpdateSmallSprite` calls `displayPartyNames` — and the character screen drops it, since
`UpdateViewCharacterScreen` (`Screen.cpp:620`) draws only the frame, the picture, the menu and the
stats. So `EventRunner` answers two questions, `OwnsScreen` and `CoversRoster`, and every screen to
come has to be asked both. Both answers came from rendering the thing and looking at it: the first
render put the sheet on top of the roster, the second still had the treasure's message showing
through `ARMOR CLASS`.

The reference reaches this screen by pushing `VIEW_CHARACTER_DATA` as its own event, which brings
its own empty text with it. This port draws the sheet in place and dismisses it on the next commit
— the same flow from the player's side, without an event stack to build first — which is why the
text box has to be suppressed explicitly rather than being empty by construction.

**The level shown is derived, not stored.** `HighestLevel` takes the best across the character's
baseclasses using the thresholds read from `baseclass.dat`, falling back to the stored level when
the design's baseclasses cannot be read — the same fallback `IsReadyToTrain` uses.

**Strength carries a percentile**: an 18 with a modifier reads `18/75`, and `18/00` at 100, which is
the top of the exceptional-strength table written as two zeroes rather than as a hundred.

##### `SpellForm`, as ported

The spell list — memorising, casting and shopping all use it. Structurally the closest relative of
`ItemsForm`: column headers, an auto-repeat block of five fields per row, and a side block. What
sits alongside is not money but **spells still available per class**, seven label/value pairs.

- **Two pairs of classes deliberately share a row.** Ranger hangs off `FIGHTERAVAIL+END`, the same
  anchor as paladin, and druid off `CLERICAVAIL+END`, the same as thief — the reference's own
  comment says they were "moved up from bottom to avoid being displayed over border graphics". No
  character is both a paladin and a ranger, or both a thief and a druid, so only one of each pair is
  ever filled and the overlap never shows. Separating them would be tidier and would not match.
- **`RIGHT` right-*aligns*; it does not place.** The seven labels are right-aligned against the
  magic-user label, so the column ends flush however long the class names are — which means the
  shared rows share a *right* edge and start a letter apart. A test asserting they shared a left
  edge failed, which is how this got pinned down.
- The `COST` column is off for memorising and on for shopping, blanked rather than removed for the
  same reason as `ItemsForm`'s: the name column is placed relative to it.

##### `RestTimeForm`, as ported

The rest-time picker — `REST TIME  DD:HH:MM`, three tab stops, `+`/`-` on the selected field. The
smallest of the forms, and a useful contrast with `ItemsForm`: its three `SEL` fields are **not**
inside an auto-repeat block, so they keep their flags rather than being flattened.

**They are still never drawn.** `showRestTime` gives text to the header, the three numbers and the
two colons, and nothing to `RTF_Days`/`RTF_Hours`/`RTF_Minutes`, so those are never placed and have
no box. The enum calls them a "selection rectangle"; in practice they name the tab stops, and the
highlight goes on the number beside each (`RTF_highlight`). Two forms now, two different reasons the
selection field is not a rectangle.

**Incrementing carries and decrementing does not, and that asymmetry is deliberate.** A minute added
at 23:59 advances the day — the minutes case re-checks the hour rollover itself rather than falling
through. Taking a minute off 1 day 00:00 does nothing at all: each field simply refuses at zero
(`RTF_DecrStat`). Making the two symmetric would let a player walk the clock back past the rest they
asked for. Days have no upper bound in either direction.

##### `ItemsForm`, as ported

The inventory / shop / treasure list, and the first form on the new engine: a layout table of four
column headers, a money block of ten label/amount pairs, and an auto-repeat block of five fields per
row. Everything game-specific arrives as formatted strings, so it stays in `UAF.Media` and needs no
item, money or party type.

- **Every field id carries the white colour bit, by an enum trick.** `enum ST_ITEMSFORM` opens
  `STIF_none, STIF_white = TEXT_FORM::white, STIF_READY, …`, so the assignment restarts the count at
  `0x10000000` and *every id after it* inherits that bit. The form is white by default because the
  ids say so, and since `fieldNumMask` keeps colour bits, stripping them would break every relative
  placement in the table.
- **Column headers are blanked, never omitted.** The name column is placed relative to the cost
  column, so a list with no costs still lays that column out — removing the field would move every
  item name.
- **Only the denominations a design configures appear.** Labels come from `moneyData`, not a fixed
  list, which is what lets `Ambassador's_Letter` show three coins and rename them.
- **The row marker is a zero-width placeholder, not a selection box.** This one is worth reading
  twice. The table writes it `ready+SEL / name+SEL`, which reads as "span the four columns" and is
  exactly what `SetText` would do with it — but auto-repeat expansion **overwrites both relative
  values with plain field ids** (`TextForm.cpp:91`), dropping the `SEL` bit before `SetText` ever
  sees it. The marker ends up taking its left from Ready, its top from Name, and no width at all.
  That is why `showItems` builds a separate `InventoryRects` list to hit-test rows with, and why
  this port maps clicks through the row's text fields instead.

> The row-marker finding came from a failing test that asserted the box spanned its row — the
> behaviour the table plainly describes. The port was right and the test was wrong, which is the
> second time in two sessions an assertion encoded intent rather than what the reference does; the
> other was "every class has a baseclass" against `$$Help`.

##### The text layer, as ported

`FormattedText.cpp` splits into four pieces, all of them in `UAF.Media` and none needing SDL:
`FormattedTextScanner` (the `/` markup state machine), `TextFormatter` (wrap and post-process),
`TextDisplayData` (box paging) and `FormattedTextRenderer` (draw a wrapped line, tinting per tag).
`TextBoxMetrics` derives the box from `TEXTBOX` / `TEXTBOX_RECT` / `TextBox_Lines` exactly as
`LoadConfigFile` does. 47 tests, and `Game` now draws its message through the whole path.

What the transcription turned up, none of it inferable from the structures:

- **Only `\r` ends a line — `\n` does nothing.** The wrap loop acts on `FTCR` and explicitly ignores
  `FTNL` ("We only process FTCR", `FormattedText.cpp:1071`). Text with Unix line endings does not
  break at all; it wraps on width alone, and `PostProcessText` is what removes the stray byte.
- **`\n\r` — the reversed pair — kills the engine.** `TestNextChar` produces `FTNLCR` and
  `NextChar`'s dispatch has no case for it, so it reaches `die(0x551b0a)`: `MessageBox` + `abort()`
  (`RunEvent.cpp:148`), in every build, not a debug assert. CRLF is fine. Reproduced as a throw.
- **Declining to rewind means "cut here", not "do not cut".** `Backup`'s `<= 0` guard reads like it
  protects an unbreakable word; it does not. The caller breaks the line either way, so a run with no
  whitespace is hard-cut mid-word at the overflowing character. The guard only stops a line starting
  with a space from rewinding to 0 and wrapping forever.
- **Wrapping is decided one character past the edge**, and a word ending exactly on the boundary
  still wraps.
- **The line count is settled twice from different numbers.** Config computes it against a hardcoded
  16-pixel line; `GetTextBoxCharHeight` then overwrites it from the font's tallest glyph. Only the
  second governs layout. With PT Serif at the requested 16, `SomethingWild`'s six configured lines
  become four.
- **`MultiBoxTextAction`** (`FormattedText.cpp:84`), the `"EWWWCWCW"` decision table the file
  documents at length, is **declared and never referenced anywhere in the tree**. Dead code, like
  `ProjectVersion.h`. Not ported.
- **`StripInvalidChars` reaches exactly one byte.** It tests `*pChar < -127 || *pChar > 255` on a
  *signed* `char`, so the second test never fires and the first matches only 0x80. Every other high
  byte passes through — which is essential, since designs' prose is full of them.
- **`orangeColor` and `brightOrangeColor` are the same RGB** in the shipped default table
  (`GlobalData.cpp:6088`). `/O` and `/T` are distinct tags that resolve identically until a design
  calls `SetFontColor`.
- **`PrevBox` drifts across a `/N`.** It decrements twice before it starts checking for a wait, so a
  box boundary one line above is stepped straight past. On plain text it is the exact inverse of
  `NextBox`.
- Lines keep their markup after wrapping, and each carries a **preamble** re-stating its colour and
  font, so a line renders correctly without replaying the ones above it.

Verified by rendering: wrap at the box width, `/R` and `/G` tinting mid-line, and `/N` ending the
box after its own line with `NextBox` revealing the second page. Colour is a draw-time tint over one
atlas rather than the original's eleven rasterised faces, for the reasons in the Phase 3 notes.

##### The menu layer, as ported

`GameMenu.cpp`'s `CMyMenu` becomes `Menu` (items, selection, shortcuts), `MenuRenderer` (layout and
draw), `MenuInput` (events → selection) and `MenuAnchors` (the config points). 44 tests. Verified by
rendering both shapes a design actually uses: the horizontal bottom bar at `DEFAULT_MENU_HORZ` and a
vertical question list at `DEFAULT_MENU_TEXTBOX`, with a title, a disabled entry and shortcut
letters.

- **Anchors are negative X values, not a field.** `MENU_DATA_TYPE::x` of −1, −2, −3 means
  `DEFAULT_MENU_HORZ`, `_VERT`, `_TEXTBOX`; anything ≥ 0 is absolute (`GameMenu.cpp:1901`). Modelled
  as an enum, because at the call site the sentinel reads as a bug.
- **`DEFAULT_MENU_COMBAT_HORZ` has a real "absent" state.** It is pre-seeded to −1 and tested
  `>= 0`, and when unset the combat menu falls back to the normal horizontal anchor rather than to
  the origin. A plain zero-initialised point would put it in the corner.
- **The item separation gains a space's width plus two — once, and only for a horizontal menu.**
  `initCharSize` guards it, and is set *whether or not the adjustment applied*, so a menu laid out
  vertically first and switched to horizontal never gets the wider gap (`GameMenu.cpp:1690`).
- **An inline title shifts a vertical menu sideways, not down.** The title advances `x` by
  `10 + separation + width` and never touches `y`, so on a column it sits to the *left* of the first
  entry and pushes the whole list right. Confirmed by rendering: the question list's entries start
  at x=147 against a menu anchored at x=20.
- **Menu labels are the one place markup is not interpreted.** `DrawFont` disables font colour tags
  for the duration and restores the setting afterwards (`GameMenu.cpp:1885`), so a label containing
  `/R` draws those two characters.
- **A shortcut letter or a mouse click chooses an entry outright.** Both select it and then push a
  synthetic `VK_RETURN` (`RunEvent.cpp:619`, `:775`) — one keystroke moves *and* confirms.
- **All four arrows drive every menu regardless of orientation.** Up/left step back, down/right
  forward, with no orientation test (`RunEvent.cpp:657`). A bottom bar responds to up and down.
- **`FirstLettersUnique` lets disabled entries collide.** Its outer loop skips them and its inner
  loop does not, so a hidden "Barter" suppresses the shortcut on a visible "Buy" — for the whole
  menu, not just that pair. Real menus hit this: the adventure bar has both "Cast" and "Camp", which
  is what `AttemptToCreateUniqueShortcut` exists to resolve, letter by letter.
- **`activeItem == -314159` means "nothing selected"** and is checked by identity in three places.
  Kept as a sentinel rather than made nullable, since it is compared directly and event flow sets it.
- **Indices are 1-based in `setCurrentItem`, `getMenuItem` and `isItemActive`, 0-based in
  `activeItem`.** The 1-based entry points are named as such here rather than quietly unified; note
  `setCurrentItem`'s guard is `item > 0`, so passing 0 does nothing at all.
- The selection is drawn reverse video. The original gets this from a `HighlightFont` rasterised
  black-on-*white* and drawn opaquely (`GlobalData.cpp:5918`); a tinted atlas has no background
  pixels to carry it, so the port fills the bar and draws the glyphs over it.

##### Events, as executed

`EventRunner` presents an event and takes the answer — `Begin` is `OnInitialEvent`, `Handle` is
`OnKeypress`, `Render` is `OnDraw` — and `EventChain` decides what follows. `Game` drives both.
Running: `TextStatement`, `QuestionButton`, `QuestionList`, `QuestionYesNo`, `NPCSays` (text and a
menu) plus `PassTime` and same-level `Teleporter` (no input at all). 18 tests.

Verified against `SomethingWild`: a real `TextStatement` wraps, pages at its `/N`, and shows the
`EXIT` / `PRESS ENTER TO CONTINUE` bar with both shortcut letters picked out; the `QuestionList` it
chains to renders its five real options at the textbox anchor.

- **`chainTriggerType` is asymmetric under `AlwaysChain`.** The not-happened path chains to
  `chainEventHappen`, not `chainEventNotHappen` (`RunEvent.cpp:910`), so an `Always` event has one
  destination either way and its not-happened id is dead. Reads like a typo; load-bearing.
- **Event id 0 can never be chained to** — both paths guard `> 0`.
- **Chain targets resolve by id, and the target usually sits at no cell.** Both events in the
  worked example above are at `(-1,-1)`: designs use off-map events as subroutines, which is why
  `EventLookup` needed `ById` alongside `FirstAt`.
- **Every one of the five button slots becomes a menu entry, empty or not.** An empty label is
  added as `" "` and disabled, which is what lets the original index straight into
  `buttons[UserResult-1]`. Adding only the non-empty ones picks the wrong option the moment a design
  leaves a gap.
- **A question with no options chains straight through without drawing** — `if (count == 0)
  ChainHappened()`.
- **The fixed menus' shortcut letters are not first letters.** `EXIT` uses index 1 for the `X` and
  `PRESS ENTER TO CONTINUE` index 7 for the `N` of "ENTER" — mnemonic and non-colliding, where
  first-lettering both would give `E` twice and suppress them.
- **The two question forms differ only in placement and flow**: the list is vertical at
  `DEFAULT_MENU_TEXTBOX` with separation 2 and a title; the button row is horizontal at
  `DEFAULT_MENU_HORZ` with separation 7 and none.
- Not run, and each names itself on screen rather than doing nothing: everything needing party
  state, an audio device, or the task queue. `Teleporter` to another level is reported rather than
  moving the party to the right coordinates on the wrong map.

> **A real bug this found: the port was reading the wrong config file.** `LoadedDesign.Open`
> preferred `config640.txt` over `config.txt`, on the reasoning that a design ships one config per
> resolution. It does — but they are **editor templates**: `GameResChange.cpp` copies the chosen one
> *over* `config.txt` (`:99`, `:119`), and the string "config640" appears nowhere in the engine,
> which reads `rte.ConfigDir() + "config.txt"` at both call sites (`Dungeon.cpp:191`,
> `RunEvent.cpp:27063`). So the port was picking up whichever resolution the design was last
> authored at rather than the one it was saved with. For `SomethingWild` the two files disagree on
> `DEFAULT_MENU_TEXTBOX` — 20,328 against 200,328 — which put every question list's options 180px
> to the right and ran the longest one off a 640-wide screen.
>
> This is the fourth instance in this port of a conclusion drawn from a plausible first reading
> rather than from the source, and the third caught by rendering output rather than by a test. The
> full suite was green before and after; only the picture was wrong.

##### Party and world state, as ported

`Party` (roster, flags, pooled money) and `WorldState` (quests, special items, keys) in `UAFcore`,
seeded from `GLOBAL_STATS`. Every trigger condition but two is now answered. 15 tests, plus the
roster drawn from `displayPartyNames`.

- **`FacingDirection` was ported wrong and is corrected.** An earlier revision returned Fire
  unconditionally for both facing forms, documented as "the original ignores the stored facing".
  It does not: only `Any` and `InFront` fire unconditionally (`GameEvent.cpp:918`), and everything
  else is a four-way switch comparing the party's direction against the stored `eventDirType` —
  fifteen named combinations (`N_S`, `N_W_E`, …) compared with `==`, not a bit field. A design
  gating an event on "only from the north" was getting it from all four sides. `Any` is ordinal 0,
  so events that never set the field are unaffected.
- **Three operands are ASL attributes, not fields.** `gender`, `specialItem` and `specialKey` are
  moved into `eventcontrol_asl` as `"Gen"`, `"SpIt"`, `"SpKy"` before writing and pulled back after
  reading (`PreSerialize`/`PostSerialize`, `GameEvent.cpp:1318`). They are read with `atoi`, so a
  missing key is 0 — which for gender means Male, not "unset".
- **`QuestStageEqual` reuses `partyX` as the stage number** (`GameEvent.cpp:1017`). The field is a
  coordinate on every other condition.
- **Special items and keys are world state, not inventory.** `PARTY::hasSpecialItem` and
  `hasSpecialKey` read `globalData`, never a character (`Party.cpp:3275`, `:3293`), and "has" is
  `GetStage(id) > 0` — the stage doubles as the possession flag. Modelling them as party inventory
  would put them in the wrong half of the savegame.
- **The two searching conditions are not mirror images.** `PartySearching` is
  `PartyIsSearching() | looking`; `PartyNotSearching` is the plain negation of `PartyIsSearching()`
  and ignores `looking`. A party that is looking but not searching satisfies **both**.
- **Daylight is `hours >= 6 && hours <= 18`** — inclusive at both ends, so thirteen hours, and
  18:30 is still day.
- **A quest is "present" once it is anything but `NotStarted`** — a failed or completed quest is
  still present.
- **Baseclass is not class.** A multiclass character's baseclasses come from its `BaseclassStats`
  list, so a cleric/fighter answers to "fighter" as a baseclass while its class is "cleric".
- **The roster's columns are fixed pixel offsets** — name at `x`, AC at `x+225`, HP at `x+300`
  (`Disptext.cpp:1069`) — and the line step comes *before* each row, so the header and first name
  never share a line. Status is carried entirely by colour: name blue when ready to train, hit
  points red at zero, yellow below maximum, green at full.
- Still unanswerable: `SpellMemorized` needs a spellbook indexed by class and level, and
  `ExecuteGpdl` needs the VM attached to a running game.

> **The party is seeded from the design's pre-generated characters, which is not how a game
> starts.** The engine builds a party through the add-character flow or restores one from a
> savegame. Taking the first six of `GLOBAL_STATS::Characters` is a stand-in so the conditions and
> the roster have real data to read; it is real data placed by a rule the original does not have,
> and it should be replaced when party creation or savegame loading lands.

##### Rules, as far as the engine has needed them

`UAF.Rules` is created here rather than at scaffolding time, per §5.1's rule that empty projects are
restore risk. First occupant: the currency (`Money.cpp`), as `MoneyRules` (denominations and
conversion) and `Purse` (`MONEY_SACK`, renamed to avoid colliding with the record read off disk).
`GiveTreasure`'s silent path pays into the party purse.

Second: **levelling**, as `Levelling` — `GetLevel`, `GetAllowedLevel`, `IsReadyToTrain`, `Train` and
`CapExperience`, transcribed from `BASE_CLASS_DATA::GetLevel` (`class.cpp:6449`) and
`CHARACTER::GetAllowedLevel` / `IsReadyToTrain` / `getNewCharLevel` (`Char.cpp`). 40 tests.

- **It is per-baseclass, not per-class.** A multiclass advances each baseclass off its own
  experience against its own thresholds, which is why nothing in the API takes a class.
- **The thresholds are the design's, not AD&D's** — there is no hard-coded fallback, which is why
  this could not exist before `baseclass.dat` read.
- **A drained baseclass is entitled to nothing**, not merely to what its experience would buy, and
  the character's other baseclasses are unaffected.
- **A capped character forfeits experience on purpose.** `Char.cpp:5503` steals the surplus so that
  a character held at a level cannot bank an arbitrary total and jump several levels when the cap
  lifts. Omitting it looks harmless and is not.
- **The level cap is only half-implemented.** `GetLevelCap` resolves `MaxLevel$SYS$` through a
  computation that also consults the character's **race**, and `races.dat` has no reader. The
  baseclass side is read from the record's skill list; a design capping a level by race goes
  unenforced. No corpus design does, which is not the same as it being safe.

`LoadedDesign.IsReadyToTrain` now derives the roster's blue name from the thresholds rather than
reading the design author's stored flag, falling back to the flag when `baseclass.dat` cannot be
read — which is the real state for `DefaultDesign`, whose `Bcd1` the reference engine refuses too.

Still nothing of `GameRules.cpp` (4,167 lines) — combat math, saving throws, THAC0. That is Phase
2's other half and it is still ahead.

- **"Base" means two different things in `MONEY_DATA_TYPE`, and they are different coins.**
  `COIN_TYPE::isBase` is a per-denomination flag the defaults set on **platinum**;
  `GetBaseType()` returns `HRType`, the coin with the **highest rate**, which is **copper**
  (`ComputeHighestRate`, `Money.cpp:574`). Every total, price and affordability check goes through
  the latter. Reading the flag instead values a purse a thousand times low — and
  `Ambassador's_Letter` sets the flag on *no* coin at all, so a reader depending on it finds
  nothing.
- **A higher rate is a *less* valuable coin** — rate is "how many of these per base coin", so
  platinum is 1 and copper 1000.
- **The slot mapping is two ranges, not one offset**: platinum–copper subtract 1 (enum 1–5 → 0–4),
  `Coin6`–`Coin10` subtract 7 (enum 12–16 → 5–9), because `BogusItemType` at 11 sits between them.
- **`Convert` truncates to whole coins and hands the remainder back through an out-parameter, in
  the *source* denomination.** 105 copper to silver is 10 silver with 5 *copper* of overflow. A
  caller that ignores it destroys money — and the reference has exactly one place where that
  matters, below.
- **`Subtract`'s change-making has three stages and the third was easy to miss.** Drain the named
  coin, make up the shortfall from the others in reverse slot order (small change before breaking a
  platinum piece), then — critically — an `else if (leftover > 0.0)` branch redistributes the
  change from converting the unspent part back. Omitting that branch loses the change outright:
  paying 3 gold from 1 platinum + 1 gold left the purse empty instead of holding 600 copper of
  value. The author's own commented-out `/*Coins[i]*/` sits on the line that captures it.
  `GiveChange` **assigns** rather than adds to each denomination, which is safe only because the
  change-making already zeroed those slots.
- **Nothing is taken unless the purse covers the whole amount** — the `HaveEnough` guard is first,
  so there is no partial payment.
- **`AutoUpConvert` stops at the first unconfigured slot rather than skipping it.** The loop
  `break`s on a zero rate despite its own comment saying "only include the non-zero coin rates"
  (`Money.cpp:1425`), so a design leaving an early slot empty gets no roll-up at all.
  `Ambassador's_Letter` is exactly that shape — gold, silver and copper configured, platinum and
  electrum absent. Each step also leaves its remainder in the denomination it reached, so
  1050 copper rolls to 1 platinum and **5 silver**, not 1 platinum and 50 copper.
- **`Total()` counts coins only.** Gems and jewellery have to be appraised and sold, so a party
  holding nothing but gems has a total of zero and cannot buy anything.
- **Adding to a denomination a design has not configured silently drops the amount** rather than
  converting or rejecting it.

##### The item database in the engine

`LoadedDesign.Items` opens `items.dat` lazily and `LoadedDesign.Item(id)` resolves a carried
instance to its record. `GiveTreasure` uses both.

- **An item's id is its `m_uniqueName`, not its `m_idName`.** `ITEM_ID ItemID(void) const
  { x = m_uniqueName; }` (`Items.h:701`). The field names invite the opposite reading and this port
  took it. `m_idName` is the fuller display name: `Ambassador's_Letter`'s glaive is
  `UniqueName "Glaive"` / `IdName "Noble Glaive"`, and a carried instance names the former. Keying
  on `IdName` resolves **nothing** and reports every treasure item as missing, with no error saying
  why. **`DefaultDesign` cannot show the difference** — its records set both names the same, which
  is why the §Phase 1 walk of it never exposed this.
- **The unstamped fallback needs `game.dat` read first** — `min(globalData.version, 0.696)`
  (`Items.cpp:3418`), so load order is load-bearing rather than incidental.
- The database is loaded lazily and a failure yields null rather than throwing: a design can be
  walked around without it, and the engine's own behaviour when a database is absent is to carry on.

Verified across the whole corpus rather than a sample: **124 of 124** item references in the three
reference designs resolve — 19 from treasure events, 105 carried by pre-generated characters. A
sample would have proved little, since most records set both names identically and only the ones
that differ can fail.

##### Mutable characters and experience

`Character` wraps a `CharacterRecord`: identity and the unchanging scores read straight through,
while hit points, per-baseclass experience, money and inventory become mutable. The record stays an
honest snapshot of the file, which a test pins directly.

- **The experience split rounds up, so a multiclass character gains more than the award.**
  `curExp = (exppts + n - 1) / n` and each baseclass receives that *full* share
  (`Char.cpp:5798`) — 100 across three baseclasses is 34 each, 102 in total. An even division
  would quietly slow every multiclass character in every design.
- **A drained baseclass gains nothing.** `IncCurExperience` refuses when `previousLevel > 0`
  (`class.cpp:4828`), the level-drain marker; the character's other baseclasses still gain. Nothing
  clamps the total, so a negative award really does subtract.
- **`GAIN_EXP_DATA`'s random mode rolls 1..count and indexes count−1** (`RunEvent.cpp:10178`) —
  a 0-based roll never picks the last member.
- **One deviation, recorded rather than hidden.** The reference takes the split's `n` from the
  *class definition* (`classes.dat`, no reader yet) and writes into the *character*'s own stats,
  dropping any share whose baseclass the character lacks. This port counts the character's own
  baseclasses. The two agree wherever a character's stats match its class, which the Phase 1 walk
  found to hold; they differ only for a record that disagrees with its own class.
- ~~**Ready-to-train is left as the record has it.**~~ **Now derived** from `baseclass.dat`'s
  thresholds via `LoadedDesign.IsReadyToTrain`; the roster's blue name is this engine's answer.
- Items land in a party-level list rather than a character's inventory, and `HasItem` searches
  both — otherwise a design gating on "do you have the key" would never fire after a pickup.

Corroborated against `SomethingWild`'s six pre-generated fighters: 2001 XP at level 2, 8001 at 4,
35001 at 6 — each sitting just past the classic AD&D fighter thresholds (2000 / 8000 / 32000),
which is independent evidence that both the reader and this model are right.

> **The money path is not verified against real design data, and no fixture can verify it.** Every
> treasure carrying coins in the three reference designs takes the pick-up screen; the only two
> silent ones (`Ambassador's_Letter`, level 2) carry items and no money. What *was* checked against
> real designs is the currency configuration — all three parse, and the two `base` notions and the
> empty-slot case above are findings from that data, not from the source alone. The arithmetic
> itself rests on unit tests derived from `Money.cpp`.

##### A caution that is worth more than any of the above

Porting this viewport and savegame code produced **eight wrong claims from a careful reading** —
two constants (`MAX_PARTY_MEMBERS` is 12 not 8; `BlockageStats.StatsFull` is a `WORD` not a
`DWORD`), three about the wall-format system, an index-minus-one that passed its first test only
because that design's first three wall sets name the same file, and two package-availability claims
drawn from guessed names rather than checked ones.

**Every one was caught by rendering output or probing real files. None by reasoning, and several
survived a full green test suite.**

The loop that works: read the loading branch *from its start* (not from where a search lands), run
it against real data, look at the result, and narrow by ruling out layers — resolution, then art,
then the blit. Two habits follow from it. Do not classify a routine by its length: `RenderSquare12`
looks hard at 107 lines and is three plain passes, while squares 13 and 14 read as occlusion cases
and are single passes behind a layout gate. And when a test passes on the first try against real
data, check what would have failed — twice here, nothing would have.

`UAFcore` is an executable. It opens a design, loads a level, walks a party around it and draws the
screen; `Game` holds the state machine and renders into a managed `Surface` knowing nothing about
SDL, so the whole engine runs headless from a `RecordedInputSource` into a `HeadlessPresenter`.
That is the thing the C++ engine cannot do — its equivalent path needs a live DirectX device before
it reads a record, which is why it has no automated tests. `--dump <path>` renders one frame and
exits, so the executable itself is smoke-testable with no display.

**The viewport is the deep water, and most of what makes it hard is undocumented.** What follows is
what cost real time to establish; none of it is inferable from the structures.

- **A level is a torus.** Both the viewport (`Viewport.cpp:4105`) and movement (`Party.cpp:1735`)
  take coordinates modulo the map extent. A map has no edges — only walls. An early version of
  `Game.Step` invented a boundary and reported "the map ends here".
- **`ViewMap` slots 13 and 14 are deliberately unwrapped**, with the original's own comment that
  "it is significant if they exceed the map boundaries". The occlusion tests ask
  `!validCoords(viewMap[13])`; on a torus that could never be true and far slivers would vanish
  wherever a corridor crossed an edge.
- **Wall index 0 means "no wall", and the table is indexed directly.** `WallSets[wallSlot]` with no
  adjustment, guarded by an early return for 0 — because the table *is* the full 192-entry
  `MAX_WALLSETS` array with slot 0 present and unused. An index-minus-one is the natural guess and
  is wrong; it survived its first test because that design's entries 1–3 all name the same file.
- **There are two kinds of wall format and only one is numbered.** The default is built from
  *unsuffixed* config keys (`A_WALL_RECT`, `VIEWPORT_COORD_0`) at index 0; the alternates come from
  suffixed keys and are appended after it. `GetFormat` skips index 0 while searching and falls back
  to it.
- **A format is selected by the wall sheet's own dimensions**, not by anything about the party.
  That is how a design drops in third-party wall packs — `SomethingWild`'s bands are labelled "from
  Kevin's Tavern demo" and "from Kevin's New Walls", and its art is 1500×375, which matches band 5.
- **`DRAW_A_WALL` is 1, not 0** (`GetWidth` does `Type--`), and **the blit is 1:1** — the
  destination takes the *source* rectangle's size, so nothing scales at draw time.
- **Slot rect heights are not uniform.** The default format's are 112×134, 48×58, 32×211 — real
  per-distance sizes. The alternates are all 211 tall with the foreshortening painted in. A
  renderer must read both dimensions rather than assume either convention.

**Squares ported: 0, 1, 2 and 5–14. Remaining: 3 and 4**, which carry 24–35 conditionals apiece.
Square 3 is likely square 2's mirror, but square 0/1 already disproved that assumption once — they
are *not* mirrors of each other — so it has to be read rather than assumed.

**Do not classify these routines by length.** Square 12 looks hard at 107 lines and is three plain
passes — it draws the 112-wide front wall, so skipping it means dead ends never render. Squares 13
and 14 read as occlusion squares and are single passes behind a layout gate (`if (WallCount == 5)
return;`), which is also why `DistantWallCount` decides between 13 and 15 viewport coordinates:
those coordinates exist exactly when those squares do.

**The diagnostic loop that worked**, after several rounds of inference that did not: render the
frame and look at it; when something is missing, probe the resolution chain (indices, art
filenames, rects) to rule that layer out; then crop the source rectangles out of the sheet to rule
out the art; what remains is the blit. That sequence found the front-face-only skip which discarded
square 9 — the corridor's side walls — while every unit test passed.


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

The four steps that stood here — retarget the `vcxproj` files, write the dumper, delete
`ProjectVersion.h`, scaffold the solution — are all done. What is next, in the order the
dependencies actually run:

1. **Finish the tagged database record bodies**, starting with `baseclass.dat`'s items 7–11 (the
   `Specab` tail is identified, §Phase 4) and then `classes.dat`, which has no reader at all.
   Levelling is blocked on both, and levelling blocks the training hall, the temple and combat.
2. **Close the two verification gaps that need only the Windows oracle**, since both are cheap and
   both currently make a green suite mean less than it looks:
   - commit GPDL reference bytecode for `oracle/golden/gpdl/*.txt`, which is Phase 2's exit
     criterion and is the only thing `GpdlOracleDiffTests` is waiting for;
   - dump `baseclass.dat` from the C++ side so the reader above has an oracle at all.
3. **Start `ArchiveWriter`.** Nothing writes today, so Phase 1's round-trip criterion is unmet and
   Phase 5 cannot begin — an editor that cannot save is not an editor. This is also the last part
   of the format still wholly unexplored: the LZW *encoder* and the write side of string interning.
4. **The forms layer**, which is what actually unblocks the ten event types that parse and cannot
   run.

Phases 5, 6 and 7 have not started and nothing in them is blocked by anything but Phase 1's writer.

### Decisions taken

| Decision | Choice | Rationale |
|---|---|---|
| Reference oracle | **GitHub Actions `windows-latest`** | No local Windows needed; host is arm64, where an MSVC VM would be emulated |
| Audio backend | **SDL3 audio + MeltySynth** | Permissive licensing resolves the GPL v2 / BASS conflict; SDL3 is well-tested on arm64. Extended: SDL3 also provides the game's window, presentation and input (see §6) |
| Format compatibility | **Read all versions, write current** | Matches original behaviour; preserves ~25 years of community designs |
| Post-Phase-1 priority | **UAFedit first** | A read-only inspector validates the data layer end-to-end and grows into the editor; visible cross-platform result in ~2 months instead of ~8 |
| Video decoding | **FFmpeg / libav** (LGPL build) | Only realistic way to decode the legacy VfW codecs in existing designs (Cinepak, Indeo, MS-Video1); optional at runtime, and in its own assembly because the C# bindings are LGPL v3 (§6.1 correction) |
