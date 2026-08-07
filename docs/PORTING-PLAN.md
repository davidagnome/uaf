# Dungeon Craft → .NET 10 / Avalonia Porting Plan

**Targets:** `UAFWin` → **UAFcore** (game engine + player), `UAFWinEd` → **UAFedit** (design editor)
**Stack:** .NET 10, C#, Avalonia 11.x (editor) + SDL3 (game), cross-platform (Windows / macOS / Linux)
**Date:** 2026-08-03

**Status.** Phase 0 complete. **Phase 1 complete** — for reading and for writing. Every design
file in the fixture corpus parses, diffed against the oracle, and **every file kind the format has
now round-trips**: the byte layer, every shared leaf, both halves of the `CAR` write path, and all
six record types — monsters, items, spells, characters, `GLOBAL_STATS` and levels. All three
shipped databases are reproduced **byte for byte**; `.chr`, `.lvl`, `game.dat` and `.pty` are all
written whole; **30 of the 44 event types write, covering every one of the 4,705 events in every
shipped level**. The remaining 14 event types appear in no shipped design at all.
Phases 2 and 3 are substantially delivered with named gaps. Phase 4 has a
running engine: it opens a design, walks a level, renders the viewport, reads **all 44**
event types and executes thirty-four of them, presents the treasure and character screens, and sets up a combat encounter with the
party and monsters placed, and **a combat that plays itself to a conclusion** — round clock, AI,
pathing, movement, attacks, the dying clock and attacks of opportunity — with spell durations and
stacking under it, and **combat: walking onto a combat event starts a fight that runs to a
verdict, drawn on screen with real icons, and a player who can move, aim, attack, guard, bandage
and cast** — spells run the full casting clock, saving throw, area geometry and effect
application. Phases 5–7 have not started.
**3,499 tests, green on macOS, Linux and Windows; both CI workflows green.**

### Where to pick up

This document is long. A new session wanting the short path in should read **§11**, which is kept
current, and then the one "as ported" section under §7 Phase 4 for whatever it is about to touch —
those sections carry the findings that cost real time, and they are written to be read before the
code rather than after it.

The single most useful habit this port has: **render the thing and look at it.** Six separate
defects this codebase has shipped were invisible to a green suite and obvious in one frame — an
item list drawn over a corridor wall, a character sheet on top of the party roster, a treasure
message showing through `ARMOR CLASS`, `ARMOR CLASS7` with no gap, a crossbow collecting a
strength bonus it should never get, and **an endless corridor east that should have been a wall
two squares ahead**. `UAFcore --dump <design> <out>` writes one frame and exits.

The habit's corollary, learned the expensive way on that last one: **a synthetic fixture can only
pin a convention, never discover it.** Three tests asserted the wall order was right, against a
fixture written from the same misreading as the code. Where a fact is recoverable from real data,
assert it there.

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
  tests/                         Serialization, Media, Rules, Scripting,      ✅ 1,146 tests
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
| Writers | Byte layer, `CAR` write path and every shared leaf done. **`MONSTER_DATA`, `ITEM_DATA`, `SPELL_DATA` and `CHARACTER` write**, and all three of `ci-tier3`'s databases are reproduced byte for byte. `LEVEL` and the savegames do not |
| Tagged databases | Framing done for all six. `baseclass.dat` (`Bcd5`) and `classes.dat` (`CL5`) read completely — 57 and 98 records across five designs, all to exact EOF. `ability.dat`, `races.dat`, `spellgroups.dat`, `traits.dat` unread |
| Event readers | 13 types have none: `Damage`, `EncounterEvent`, `EnterPassword`, `GPDLEvent`, `HealParty`, `InnEvent`, `JournalEvent`, `PlayMovieEvent`, `SmallTown`, `TakePartyItems`, `TavernTales`, `Vault`, `WhoTries` |

The pattern is established and mechanical: extend the dumper for a type → write the C# reader
→ diff. `ITEM_DATA` is the worked template.

**Reading is done; writing is half done.** That distinction matters more than the table above
suggests, because every reading claim is about parsing bytes the reference produced. The exit
criterion is round-trip byte-identity, and the machinery it needs now exists — the LZW *encoder*,
the string-interning table on the write side, and the storing branch of `MONSTER_DATA`,
`ITEM_DATA`, `SPELL_DATA` and `CHARACTER`; the first three reproduce `ci-tier3`'s databases byte
for byte, a `.chr` file is written whole, and 30 of the 44 event bodies write — every event the corpus has. What is left is the
event tail, then `GLOBAL_STATS` and `LEVEL` — which both need it — and the rest of the savegames.
Phase 5's exit (the editor saves a design the C++ editor can load) depends entirely on those.

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
and the party roster, runs events with chaining, and presents the treasure and character screens.
`--dump <path>` renders one frame and exits, so the whole engine is smoke-testable with no display.
115 tests.

**Nine of the 44 event types execute.** Five through `EventRunner`, which presents and takes an
answer — `TextStatement`, `QuestionButton`, `QuestionList`, `QuestionYesNo`, `NPCSays` — and four
through `Game.ExecuteWithoutInput`, which needs neither: `PassTime`, `Teleporter` (same level),
`GainExperience`, and `GiveTreasure` **in its silent form only**, since the other form opens a
screen that used to not exist. The treasure screen now runs, so its non-silent form does too.

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
2. ~~**The forms.**~~ **Done** — all five, on the shared `TextForm` engine, and the character
   sheet is complete and reachable from the treasure screen's VIEW entry.
3. **Combat — in progress; see §11.** **Encounter setup is done**: `CombatMap`,
   `CombatMapGenerator`, `CombatPlacement` (party formations), `TurtlePlacement` +
   `MonsterArrangement` + `MonsterApproach` (monsters), and `CombatSetup` over the lot.
   `CombatPathFinder` ports `path.cpp`, `CombatRound` + `TurnQueue` the round clock, and
   `Combatant` the entity, `Targeting` who may hit whom, `CombatMovement` the walk, `Attack` the
   swing, `MonsterAi` the choice, `CombatUpkeep` the dying clock and `OpportunityAttacks` the
   interruptions — **a fight plays itself to a conclusion**. What remains is the spell layer and
   the Forth VM.
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

##### The wall array is stored N,S,E,W — a bug this port shipped for months

**Found while porting the combat map, which reads all four faces of every cell.** `AREA_MAP_DATA`
declares `BYTE wall[4]; // North, south, east, west` (`Level.h:87`) — and `blockage[4]` the same
way — so **east and south are transposed relative to compass order**. Every consumer in the
original permutes with `{0,2,1,3}`: `walls(int dir)` and `blockages(int dir)` (`Level.cpp:932`,
`:945`), `IsWallAt` (`Drawtile.cpp:1819`), and three explicit switches in `RunEvent.cpp`
(`:5171`, `:5420`, `:14678`). Backgrounds are the exception — `northBG`…`westBG` really are in
compass order, which is why `backgrounds(dir)` has a *different* table. That near-miss is what
makes the trap convincing: one struct, two orders, three accessors.

`WallResolver.IndexAt` and `Map.Blockage` were indexing with `Facing` directly, so **every east
and south wall in the engine was the other one**. Now `AreaMapCell.WallAt(dir)` /
`BlockageAt(dir)` hold the permutation, and both callers go through it.

Three independent confirmations, because the source alone had already been misread once:

| Evidence | Result |
|---|---|
| Shared-edge agreement on `SomethingWild` — a cell's east face against its east neighbour's west | **9,708 / 9,708** permuted vs 78.88% by facing |
| Same on `Case` / `Ambassador's_Letter` | 74.98% vs 27.68%, 92.03% vs 65.73% |
| `GoldenFrameTests` | exactly the four **East and South** scenes moved; North and West untouched |

Rendering it settled which was right. Facing east from `SomethingWild`'s start at (1,2), the old
build drew an endless corridor with a door in the right-hand wall; the fixed one draws a wall two
squares ahead with a door in it. The data says (1,2) has no east wall and (2,2) has east wall slot
3 — and the level is 10×10, so the corridor could not have run to the horizon.

> **The lesson is about the test, not the code.** `WallResolverTests` had a synthetic 4×4 map
> whose fixture comment read "North=1, East=2, South=0, West=3" over the array `[1,2,0,3]` — the
> same wrong order the resolver used. Three tests asserted against it and all three passed. **A
> synthetic fixture can only pin a convention, never discover it**, and when the author of the
> fixture and the author of the code share a misreading it locks the bug in. The real-data test
> that would have caught it now lives in `LevelReaderTests`, and it is two lines: read a shared
> edge from both sides and check they agree.

##### The combat map, as ported

`CombatMap` is the terrain grid (`terrain[][]`) plus the `Drawtile.cpp` free functions that read
and write it; `CombatMapGenerator` is `GenerateIndoorCombatMap`; `CombatTerrainExpander` is
`ConvertTempMapToCombatTerrain`. The tile tables are **generated** by
`dotnet/tools/gen-combat-tiles.py`, checked in CI — 340 integers whose only structure is their
position, and two of the five columns (`invisible`, `passable`) drive line-of-sight and movement
rather than drawing, so a transposed column is a gameplay bug rather than a cosmetic one.

**The combat map is the dungeon rotated 45°.** Each level cell becomes an 8×7 block in a temp
grid, each row starting one column left of the row above; the shear is what turns rectilinear
corridors into the diagonal ones combat is fought on. Three passes: stamp the four faces, reduce
each wall square to a junction type from its neighbours, then sample a 50×50 window and expand
each junction into one to five terrain tiles.

Five things that are not guessable from the shape of the code:

- **`getTerrainWallType`'s four "compass" neighbours are diagonal.** North is `(x−1, y−1)` and
  south is `(x+1, y+1)`; only east and west are orthogonal. The grid is already rotated, so
  reading north as `(x, y−1)` yields a map that looks plausible and has every junction wrong.
- **The `#ifndef diagonalMap` block — about 60 lines clamping the source window to the level
  bounds — is dead, and provably so.** `diagonalMap` is defined at `Drawtile.cpp:27`, and the
  block reads `areaMapEndY`, whose only declaration is *commented out* at `:2402`. It would not
  compile. The combat map is a torus with no clamping, which matches the rest of the engine.
- **Of the two `findEmptyCell` definitions, the first is live.** `newMonsterArrangement` is
  defined at `Combatants.h:67`, which `Drawtile.cpp` includes at line 38 — before both
  definitions at `:3841` and `:4016`.
- **`partyCountX/Y` is computed before the diagonal shift**, not after. Reordering those two
  statements moves the party half a map east.
- **An empty terrain square is impassable, not open.** `HaveMovability` rejects `cell < 1`, so a
  map is unwalkable until the hole-filling pass puts floor tiles down. `FillHoles` is not optional.

**Two bugs in the original are reproduced deliberately, and one is not.**

- *Reproduced.* An open **south** door punches its gap at row `ty` — the cell's *north* edge —
  using a loop variable that has escaped with the value 8, so the two cleared squares land 19 and
  20 columns right of the cell (`Drawtile.cpp:2853`). The block computes the correct `y` on the
  line above and never uses it. So an open south door holes a *neighbour's* north wall and leaves
  its own shut. It is deterministic and in bounds, and designs have been played against these maps
  for 25 years, so it stays — commented where it happens.
- *Not reproduced.* `GetDoorAt` builds its permutation as **`{0,2,1,4}`** (`Drawtile.cpp:1887`) —
  a 4 where every other site has 3. Both arrays are `[4]`, so asking for a **west door** reads one
  past the end of each: the slot comes from the first byte of `blockage[0]` and the blockage from
  beyond the struct entirely, into the next cell. That is undefined behaviour with no defined
  result to port, so the C# uses 3. West doors in the original are arbitrary — drawn or not
  depending on neighbouring bytes.

**No oracle exists for any of this** — the C++ editor cannot run headless (§7 Phase 0), so there
is nothing to diff against. The tests assert structural properties a drifted generator cannot
satisfy by accident (a level with no walls yields an entirely open map; an all-*open* level with
wall art on every face yields the same, since `IsWallAt` is `slot > 0 && blockage != Open`; the
party always lands somewhere it can stand, over every cell of a real level). And the map was
printed as ASCII and looked at — it comes out as diamond rooms joined by diagonal corridors, with
the party in the corner formed by cell (1,2)'s north and west walls, which is what the level says.

##### Party placement, as ported

`CombatPlacement.PlaceParty` is `determineInitCombatPos` plus `getNextCharCombatPos`
(`Combatants.cpp:2424`, `:4046`). Each member has a **preferred square from a formation table**,
and takes the first free square found by a square spiral outward from it; members are placed in
marching order and occupy the grid as they land, so a later one routes around an earlier one.

**The formation tables are authored data, not geometry**, and they are generated by
`dotnet/tools/gen-party-arrangements.py` — 624 characters each, written in the C++ as ~50 adjacent
string literals with two commented-out earlier versions interleaved, one of them *after* the
terminating semicolon. Two characters per member give a `(dx, dy)`; the layout is four direction
blocks of 156, each holding twelve runs for party sizes 1–12, run *n* being 2*n* long.

- **`Decode` is not a sign convention.** `'A'` and `'a'` are **both zero** (`Combatants.cpp:2016`);
  upper case counts up, lower case counts down starting at `'b'` = −1. Reading `'a'` as −1 shifts
  every negative offset by one and skews the formation without breaking it.
- **The spiral is stranger than it looks and was transcribed, not rewritten.** `dir` and `i` are
  initialised *once* outside the ring loop rather than per ring, and the 90° rotation reuses `i` as
  its swap temporary. The initial `dir = 3` / `i = -1` is the whole reason ring 0 tests exactly one
  square — its inner condition is `i < 0`. Confirmed by running it and printing the visit order:
  225 distinct squares, rings of 1, 8, 16, … 56, each ring starting at its top-left and going
  clockwise. `getNextCharCombatPos` also declares a `searchOrder` array it never reads; not ported.
- **Unplaced is `x = -1`, not an exception or a removal.** The original leaves the combatant in the
  array and later passes test `x < 0` to skip it, so the index correspondence the round order
  depends on survives.
- **`CombatantCount` must cover the party before anyone is placed.** Occupancy reads reject an
  index at or above it *and clear the square* — the self-healing read noted above — so leaving it
  low makes each member invisible to the next and stacks the whole party on one square.

Verified by printing it: facing north, a party of six lands in two ranks of three immediately south
of the origin; facing east, in a column to its west. Both match the tables decoded by hand, and on
an open map the spiral never displaces anybody, which is the intended behaviour rather than a
coincidence.

> **The `PartyArrangement` hook is not wired up.** A design can replace the whole table at runtime
> if its global script returns one of exactly the right length (`Combatants.cpp:2489`), and
> `m_iPartyOriginX/Y` likewise comes from a `PartyOrigin<direction>` hook offset that defaults to
> zero. Both need the GPDL VM running global scripts. `PartyArrangements.For` takes the table as an
> argument and `PlaceParty` takes the origin, so wiring them up later is a call-site change.

##### Monster placement, as ported

Monsters are **not** placed by the code that places the party. Under `newMonsterArrangement`
(defined, `Combatants.h:67`) that branch of `determineInitCombatPos` is commented out entirely
(`Combatants.cpp:2197`); monsters go through `MonsterPlacementCallback` — a **turtle-graphics
interpreter** whose program is a string of single-character commands. `TurtlePlacement` is that
interpreter, `MonsterArrangement` its state, `MonsterApproach` the direction cursor, and
`CombatSetup` the orchestration.

**The program is design data.** It arrives from the design's own `CombatPlacement` special ability
via GPDL — `$MonsterPlacement("16FbPV500E")` — and the six shipped variants differ only in how far
forward the turtle steps first: 0 for up close, 9/10 nearby, 16/17 far away, the larger of each
pair when the party faces south or west. `CombatSetup` takes the program as a parameter and
defaults to the built-ins, which is faithful rather than a shortcut: the C++ carries the same
strings in a `defaultGlobalScripts` table (`Specab.cpp:2081`) for designs that define none.

**The turtle does not work in map squares.** Its position is party-relative *and sheared*:
`MoveTurtleY` shifts the column by the same delta so `x − y` is held constant
(`Combatants.cpp:2743`), and the east/west placement limits compare against `x − y` rather than
`x` (`:2698`). That is because the combat map is the dungeon rotated 45°, so the axis across a
corridor is the diagonal. Treating any of it as ordinary coordinates puts monsters on the diagonal
— and the four direction tables agree: "forward" for a northern approach is `(−1,−1)`, not `(0,−1)`.

Verified by running real encounters and printing them. With the party at the map centre (25,25),
the four `Any` directions each step their own forward vector and plant:

| Distance | Program | North | East | South | West |
|---|---|---|---|---|---|
| Up close | `bPV500E` | adjacent | adjacent | adjacent | adjacent |
| Nearby | `9FbPV500E` | (16,16) | (34,25) | (34,34) | (16,25) |
| Far away | `16FbPV500E` | (9,9) | (41,25) | (41,41) | (9,25) |

Exactly 9 and 16 steps out along each direction's own forward vector, which is the decisive check
that the shear and the direction tables are both right.

Three further things:

- **`V` (line of sight) needs `IsLineOfSight`, which is not a Bresenham line.** It is an
  octant-decomposed DDA that tests the cells on **both** sides of the line
  (`Drawtile.cpp:3417`), so a sight line slipping diagonally between two walls is blocked. Ported
  as `LineOfSight`. Note its wall test bounds the tile index with `cell < CurrentTileCount` where
  `HaveVisibility` rejects on `cell > CurrentTileCount` — the last tile of each table is
  transparent to one and opaque to the other. Unreachable, because both tables' last tiles are
  disabled, but they are not interchangeable.
- **`E`'s hand-rolled visited-set index is correct**, which is worth recording because it does not
  look it: a flat `(2R+1)²` array advanced across three nested loops, per-column stride `2R+1` and
  per-ring rewind `(2r+2)(2R+1)+1`. Working it through, the rewind lands exactly on the next ring's
  top-left and the walk stays in bounds. Ported with a relative index instead, which is provably
  the same and does not need the derivation.
- **The approach cycles are transcribed, not derived.** `N_S_E` runs N→E→S→N while `N_W_E` runs
  W→N→E→W. No naming rule gives you both.

**Two original bugs are reproduced, and both are in commands no shipped program uses.** `d` computes
its `dy` from `partyPositions[j].x` — the same field as `dx` — so it measures to a reflected point
(`Combatants.cpp:2934`). And the four bounding-box jumps `w n p s` are not the symmetric set their
names suggest: `n` for a northern approach calls `MoveTurtleX(partyMaxY)`, setting a column from a
row. There is no observed behaviour to check a correction against, so inventing one would be
guessing. `WithinSight`'s `placeX > 0` guard (rather than `>= 0`) is reproduced for the same reason.

> **Both of the passes that follow placement are now in.** `CombatSetup` deletes any monster with
> no path to the party (`Combatants.cpp:255`) and retries the encounter at a shorter distance when
> nothing could be placed (`the for(;;)` at `:214`). The reachability walk uses a **1×1** footprint
> rather than the monster's own — "1x1 good enough to let party reach" (`:238`) — because the
> question is whether the two sides can meet at all, not whether that particular monster fits.

##### Combat pathing, as ported

`CombatPathFinder` is `CPathFinder::GeneratePath` (`path.cpp:566`) — a cost-ordered best-first
search over the eight neighbours of each square, stopping as soon as a square inside the
destination rectangle is queued.

**It is not the A\* implementation.** `path.cpp` contains two `CPathFinder` classes; the first —
the `_asNode` / open-list / closed-list A\* at `:88–441` — is behind `#ifdef OLDPATH`, and
`OLDPATH` is **commented out** at `path.h:97` and defined nowhere in the tree. So the ~350 lines
that look like the real pathfinder are dead. The live one's own comment explains why it replaced
them: the old one "took cpu time proportional to the fourth power of the distance".

Details that decide the shape of a walk, rather than merely its length:

- **Diagonals cost 15 against 10.** `GetCost` is `5·d² + 5` over the *squared* Euclidean distance
  (`path.cpp:47`), so the ratio is 1.5 rather than √2 ≈ 1.414 — diagonals are slightly less
  attractive than true geometry would make them, but still beat two orthogonal steps, so a walk
  across open ground is Chebyshev-optimal.
- **The neighbour order is orthogonal-first and load-bearing.** Equal-cost ties break on which
  neighbour was queued first, so reordering `dX`/`dY` changes which of several shortest routes
  gets walked. The file keeps the previous ordering in a comment marked "Old method
  compatibility"; that one is not live.
- **Two deliberate "more random-looking walks" rules, both deterministic.** `CostSort` jumps a
  node ahead of its equals only on odd queue indices (`i & 1`), and an equal-cost rival takes over
  as parent only on odd slots (`path.cpp:773`). They look like noise and are not optional — they
  pick between equal-length routes.
- **`CostSort` is not a sort.** It walks one node forward past a run of equal-or-greater cost,
  swapping with the *first* node of each run. The queue stays grouped by cost without ever being
  fully ordered.
- **Arrival is tested on the node just added, not the node being examined**, so the search stops
  one expansion earlier than a textbook Dijkstra.

> **`IDTYPE` is a `WORD`, and node IDs are `x·rows + y`.** A combat map above 256×256 overflows it,
> and `config.txt` allows up to 500×500 (§ the combat map). The port uses `int`, because
> reproducing a 16-bit truncation would mean deliberately corrupting paths on large maps with no
> observable behaviour to match. Nothing else in the port depends on the width.

**Verified against an independent flood fill**, which is the strongest check available with no
oracle: a plain 8-way BFS over the same passability must agree about which squares are reachable.
Across 100 combat maps generated from a real level, **6,400 sampled targets, 4,970 reachable, zero
disagreements** — and a seeded 30×30 maze runs the same comparison over all 900 squares in CI.
Routes were also printed and looked at: a straight line due east, and a 14-step diagonal weave
between the map's diagonal walls that is exactly Chebyshev-optimal.

**`ComputeDistanceFromParty` is deliberately not ported.** It is a BFS filling
`monsterArrangement.distanceFromParty`, and **nothing reads that array** — it is allocated, filled
and freed, and a repo-wide search finds no consumer. Its one bug is therefore inert, but worth
recording because it is invisible on a skim: the neighbour test reads `terrain[y][x].cell` — the
**parent's** square, not the child `X,Y` it has just computed (`Combatants.cpp:2283`). Since the
parent is by construction passable, every neighbour passes, so the fill would mark the whole map
reachable and the "inaccessible" branch is unreachable code. An earlier note in this document
listed it as a prerequisite for monster placement; it is not one.

##### The round clock, as ported

`CombatRound` is the round half of `COMBAT_DATA` — which round it is, whose turn, and what the
engine should be doing — and `TurnQueue` is `QueuedCombatantData`. **Deliberately no combatant
model**: `COMBATANT::HandleCurrState` (`Combatant.cpp:3903`) needs attack resolution, movement and
the GPDL script hooks, and building the clock first is what makes those testable when they arrive.

- **The turn queue is a stack, not a queue.** `Push` adds at the *head* and `Top` reads it, so an
  interrupting free or guarding attack goes in front of whoever was acting and the interrupted
  combatant resumes when it pops. That is the entire interrupt mechanism, and the reason the
  reference uses a list rather than an index.
- **A round is never queued up in advance.** `getNextCombatant` (`Combatants.cpp:1610`) drains the
  queue of anyone finished, and only when it is empty walks the initiative order — **1 to 22** —
  pulling one combatant at a time. That is what lets a spell resolving mid-round insert somebody.
- **`RestartInterruptedTurn = !StartOfTurn` on the displaced head** (`Combatant.h:737`), so a
  combatant interrupted *before* it ever acted comes back as a fresh turn rather than a resumed
  one. `IsStartOfTurn` ORs the two, so both count as starting.
- **`PushTail` does not set a start of turn and `Push` does.** A combatant that reaches the top
  because others popped off never announces itself. The asymmetry is in the original.
- **A last round runs to completion.** `StartNewRound` tests `m_bLastRound` as its *first* act
  (`:4520`), so the round marked last still happens and the *next* rollover ends the fight.
- **Round 0 with initiative 1 is the starting state**, and `m_bStartingNewCombatRound` starts true,
  so the first thing any encounter does is roll over into round 1 — "Characters start with
  initiative=0 so we will need to start a new round before we can begin" (`:203`).

**Two enums that the headers insist must match, and do not.** `individualCombatantState`
(`Combatant.h:30`) carries `ICS_Unconscious` at 13; `overallCombatState` (`Combatants.h:34`) has no
equivalent, so from 13 onward the two are **off by one** — and `GetCombatState` casts straight
across (`:6604`), which would turn `Unconscious` into `Dead`, `Dead` into `Gone` and `Gone` into
`NewCombatant`. It is latent, not live: every value from 11 up is marked "Not used as an
ICS_STATE...only for script", and a repo-wide search confirms none of them is ever assigned. Both
are transcribed as they stand — inserting the missing member would renumber everything after it,
and the numbering reaches save games (§4.4).

> **A dead state-name table, not worth carrying over — but mind which one.**
> `CombatantsStateText` (**plural**, `Combatants.cpp:95`) is declared with
> `NUM_COMBATANTS_STATES` = 17 against a 23-value enum, and its entries desynchronise from index 9
> on — slot 9 reads `"OCS_CombatRoundDelay"`, a state that does not exist at all. It is **never
> read**, so the mismatch is inert. Its near-namesake `CombatantStateText` (**singular**,
> `Combatant.cpp:62`) is a different table, is live, and is correct; see the combatant section
> below. Grepping for one and concluding about the other is a mistake this document has already
> made once.

##### The combatant, as ported

`Combatant` is a **slice** of `COMBATANT` (`Combatant.h:103`), not the whole of it. The original
declares some 90 members and most forward to the underlying `CHARACTER`; what is here is what the
round clock and placement need — identity, position, the turn's resources, and the predicates that
decide whether a combatant still has something to do. Spell casting, animation, the targeting queue
and the auto-combat "thinking" are not ported. **With it, a round now runs end to end**: everybody
acts once in initiative order and the round rolls over, driven by real entities rather than
callbacks.

`CombatSetup` was unified onto it — the placeholder record it used to take is gone, and setup now
writes each combatant's square back onto the entity.

- **`IsDone` mutates, and the round depends on it.** Being off the map, or being a free attacker
  with no target, *sets* `turnIsDone` rather than merely returning true (`Combatant.cpp:7016`), so
  asking the question changes the answer to later ones. The latch is what stops a fled combatant
  being offered turns for the rest of the round.
- **The free-attack latch fires before the return, not after.** `IsDone(freeAttack: true)` with no
  target reports done on the *first* call. A test that assumed otherwise is what found this.
- **Petrified short-circuits ahead of the readiness check**, so a petrified combatant is done
  whatever a script would have said.
- **`EndTurn` only acts for the combatant at the top of the queue** (`qcomb.Top() == self`,
  `:6882`) — an interrupted combatant cannot end a turn it is not currently taking. Its latch
  condition is `ChangeStats() || NumFreeAttacks() || NumGuardAttacks()`, so the one case that does
  *not* latch is a spent interrupter, which is the entry about to be popped anyway.
- **The start-of-round reset is gated on `charCanTakeAction() && IsDone()`** (`Combatants.cpp:4553`),
  so a combatant mid-turn keeps what it had and an unconscious one is skipped entirely.
- **Casting skips only the state reset.** The attacks-and-movement block sits *outside* the
  `ICS_Casting` check (`:4592`), so returning early for a caster would leave it with last round's
  movement. This is the kind of nesting that a skim gets backwards.
- **Guarding persists by two different rules** — an auto combatant keeps `Guarding` outright, a
  player-run one moves to `ContinueGuarding` and only when the hook said so.
- **Unused attacks carry over, capped at this round's own ceiling**: new + leftover, clamped to
  `ceil(new)` (`:4597`). Half an attack survives a round; it cannot be hoarded.

> **The `IS_COMBAT_READY` script hook is the one part left stubbed**, exactly as predicted. It runs
> against both the character and the combatant and either can veto (`Combatant.cpp:6981`); both need
> GPDL global scripts. `IsCombatReady` is the settled answer only, defaulting to ready, so a design
> whose scripts gate readiness — a sleep or hold effect — will have its combatants act when they
> should not.

> **There are two state-name tables and only the plural one is dead.** `CombatantStateText`
> (**singular**, `Combatant.cpp:62`, 12 entries) is live and correct — it is returned to scripts by
> a GPDL sub-opcode (`GPDLexec.cpp:5282`), not merely traced. Its 12 entries cover exactly the
> states a combatant can actually be in, so the four enum values past its end are unreachable. The
> dead one is `CombatantsStateText` (plural), covered above. An earlier revision of this document
> grepped for the plural name and drew a conclusion about both.

##### Targeting, as ported

`Targeting` is `GetCurrTarget`, `IsValidTarget` and `canAttack`; `WeaponRange` is
`WpnCanAttackAtRange` (`Items.cpp:223`). `canAttack` returns a named `AttackRefusal` rather than
the reference's bare `BOOL` — the order of its tests is preserved and the first refusal is the one
reported, so naming them makes each test state which rule it is exercising.

- **`IsValidTarget` is entirely a script hook, and it can only refuse.** The reference runs
  `IS_VALID_TARGET` on the *target* and treats a leading `'N'` as a veto; an empty result — which
  is what a design with no such script gives — leaves the answer at valid (`Combatants.cpp:1349`).
  So with GPDL unported **every target is valid, and that is faithful** rather than a stand-in.
  The reference caches per attacker in `targetValidity`, so the script runs once per target.
- **The ranged weapon classes have a *minimum* range of 2, not just a maximum.** A bow, crossbow,
  sling or thrown weapon cannot be used on an adjacent enemy at all. The hand classes have no
  minimum, which is exactly what lets `HandThrow` cover both — the header's table at `Items.h:52`
  spells this out and is worth reading before assuming anything from the names.
- **Natural attacks bypass the range table entirely.** With no readied weapon the reference
  refuses any distance above 1 outright (`Combatant.cpp:9100`); claws and fists never consult an
  item range.
- **A fractional attack cannot be spent in consecutive rounds** — `availAttacks < 1.0 &&
  currentRound - lastAttackRound <= 1` refuses (`:8961`), so half an attack banked from last round
  waits a round before it can be swung.
- **Same-side targeting has three different answers.** An auto combatant never turns on its own
  side; a player cannot strike a party *character*; a non-pregenerated **NPC** is the one same-side
  target that is allowed — and in the reference that is where the NPC would change sides, though
  the line doing so is commented out with a note saying it does not belong there.
- **Invisibility only protects at a distance.** The whole block is gated on `dis > 1`, so an
  adjacent attacker finds an invisible target regardless, and the selective variants
  (`SA_InvisibleToUndead`, `SA_InvisibleToAnimals`) depend on what the *attacker* is.
- **`GetCurrTarget` distinguishes asking from acting.** With `updateTarget` false a target that has
  left the map is *returned anyway* rather than cleared (`:4183`), because the animation code wants
  to know who it was without the side effect.

> **`GetCenterX` and `GetCenterY` do not agree, and both feed line of sight.** X subtracts one from
> the half-width **only when facing west** (`Combatant.cpp:10952`); Y subtracts one
> **unconditionally** (`:10967`). So a 2×2 combatant facing north measures from `(x + 1, y)` — the
> top-right of its footprint, not the middle. A first pass here wrote the symmetric version and
> reading the second function caught it. Transcribed as it stands: range and line of sight are both
> measured from here, so straightening it would move every ranged attack.

> **Special abilities stand in as flags.** `DetectsInvisible`, `IsInvisible`, `IsUndead` and the
> rest are plain booleans on the combatant because `SPECIAL_ABILITIES` is not ported. They default
> to off, which makes every target visible — the permissive choice, and the one that keeps a fight
> running rather than silently refusing every ranged attack.

##### Movement, as ported

`CombatMovement` is `MoveCombatant` and `TakeNextStep` (`Combatant.cpp:9293`, `:4026`), plus the
`GetDir` / `GetDist` helpers from `path.h`. It is what finally joins the pathfinder to the
combatant: `CombatPathFinder` produces a route and `TakeNextStep` walks it down to empty.

- **Every second diagonal is free.** A diagonal nominally costs 2, but `m_iNumDiagonalMoves` is
  incremented first and the cost drops to 1 whenever the count lands even (`:9316`) — so diagonals
  run 2, 1, 2, 1… and average 1.5. That is the AD&D rule made integral, and it is **the same 1.5
  the pathfinder charges** (15 against 10, §the pathing section), so the search and the walk agree
  on what a route costs. The counter moves whether or not the step is taken, so a refused diagonal
  still shifts which of the next ones is free.
- **`m_iMovement` counts points *spent*, not remaining.** It starts at zero each round and adds
  up. The name says the opposite, and reading it as an allowance inverts every movement test. The
  affordability check is `spent < max - (cost - 1)`, i.e. `spent + cost <= max`.
- **Stepping off the map is fleeing, not a failed move.** The reference's `else` arm sets the
  status to fled, bumps the side's flee counter and ends the turn (`:9440`). A caller that treats
  an off-map destination as an error removes the only way out of a fight.
- **Facing only ever becomes east or west.** The icon is a sprite that mirrors horizontally, so a
  north or south step leaves the facing unchanged — the reference's `default:` arm says so
  outright. The full eight-way direction goes to `m_iMoveDir` instead.
- **Walking into somebody attacks them.** The blocking combatant is looked up before the wall test,
  and if `canAttack` allows it the step becomes an attack — the combatant does not move.
- **The occupancy and obstacle tests disagree on purpose.** The wall check that follows passes
  `CheckOccupants = FALSE` (`:9362`), because the combatant just found in that square would
  otherwise block its own attack.

> **Three things in `MoveCombatant` are not ported and all three are hooks or effects**: the
> `ON_STEP` script, the lingering-spell check on the square moved into, and
> `CheckOpponentFreeAttack` — the attack of opportunity a retreating combatant grants, which sets
> the mover's state back to `None` so its turn resumes after the interruption. The turn queue
> already models that interruption (§the round clock); what is missing is the rule deciding when
> one is owed.

##### The attack, as ported

`DamageDice` is `GetDamageDice` (`Combatant.cpp:8379`), `Attack.Resolve` the swing, and
`Attack.ApplyDamage` is `CHARACTER::giveCharacterDamage` (`Char.cpp:8245`). The arithmetic already
lived in `UAF.Rules` — `ToHit`, `Thac0`, `ArmorClass` — so this is wiring plus the dice selection.

**Dice come from the caller.** The reference rolls through the engine's shared generator; passing a
roller in keeps resolution deterministic and `UAFcore` free of a global.

Four quirks in the damage dice, none of them derivable:

- **A weapon's to-hit bonus is added to its damage as well.** `Attack_Bonus` goes into the damage
  bonus alongside the size-specific one (`:8407`), so a +1 sword both lands more often and hits
  harder — one field doing two jobs, and the same field `ToHit.TargetNumber` subtracts.
- **Unarmed damage drops the unarmed bonus against large targets.** Small uses
  `unarmedBonus + GetAdjDmgBonus()`; large uses `GetAdjDmgBonus()` alone (`:8469`). No comment, no
  obvious reason.
- **A monster's own attack takes no adjusted damage bonus at all** (`:8462`) — unlike every other
  branch, including the unarmed fallback the same monster uses when it defines no attacks.
- **Which attack a monster is making is inferred, not tracked**: the index is
  `totalAttacks − availAttacks`, so a three-attack monster rolls its profiles in order as its
  allowance drains. The reference clamps the index twice because `availAttacks` is a `double`.

**Ammunition is two separate questions.** `WpnConsumesAmmoAtRange` asks whether a quiver empties;
`WpnConsumesSelfAsAmmo` whether the weapon itself is spent. They disagree for the spell classes
(self yes, ammo no) and for bows (ammo yes, self no), which reads as a contradiction until you
notice a wand has no quiver. A thrown weapon only costs anything **beyond range 1**, so stabbing
with a dagger you could have thrown does not lose it.

> **`giveCharacterDamage` was got wrong four ways on the first pass** and is worth stating
> carefully.
> 1. **Damage only lands on five statuses** — okay, running, unconscious, animated, dying.
>    Anything else, already-dead included, takes nothing and keeps its hit points.
> 2. **Hit points clamp at −10 going down and at the maximum going up.** The floor is applied
>    *before* the status test, so "dead" and "at the floor" are the same state; the ceiling means
>    negative damage heals but cannot overheal.
> 3. **Zero is unconscious, not dying.** The bands are `<= −10` dead, `< 0` dying, `== 0`
>    unconscious (`:8271`) — the dying band is −1 to −9. Folding zero into dying would make every
>    knocked-out character bleed out.
> 4. **There is no non-lethal branch here.** The flag rides on the damage and is consumed
>    elsewhere, so the port carries it through without acting on it.
>
> The first draft invented an AD&D-shaped rule from memory and cited a header that only declares
> the enum. Reading the function is what produced all four.

##### The monster AI, as ported

`MonsterAi.Think` is `COMBATANT::Think` (`Combatant.cpp:2080`) — **the unscripted half**. It returns
an `AiPlan` rather than assigning state and readying weapons as it goes; separating the decision
from its execution is what lets a test assert the choice instead of its consequences.

The decision order is the reference's and it matters:

1. **Fleeing or turned beats everything**, checked before any target is considered.
2. **Acquire a target** — keep the current one if still attackable, else the nearest enemy
   **with line of sight**, else the nearest enemy at all.
3. **Attack from here** if reach allows.
4. **Otherwise walk toward** whichever target can actually be pathed to, trying them in order.
5. **Guard** if none of the above.

- **Line of sight is preferred, not required.** The reference makes two passes and its own comment
  explains why: targets are ordered by distance, and the nearest may be on the far side of a wall,
  so the shortest straight line is not the shortest walk.
- **The walk's destination is the target's footprint expanded by one on every side**
  (`x-1, y-1` through `x+width, y+height`, `:2719`), so a route ends *beside* the target. Asking
  for the target's own square would never path — the target is standing in it.
- **Standing on any map edge while fleeing means leaving the fight**, not walking to the edge. The
  test fires before any pathing.
- **`CanMove` is not just "has movement left"** — a combatant whose turn is done cannot move
  either, and a monster can be pinned by the `Monster_NoMove` debug setting.
- The reference has the flee block **twice**, once for `iFleeingFlags` and once for `isTurned`,
  differing only in a trace message. Ported once.

> **The scripted branch is not ported, and it is a separate large piece.** When a design supplies
> an AI script, `Think` builds a `COMBAT_SUMMARY` of every combatant, weapon, attack and reachable
> cell, enumerates candidate actions, and ranks them by running a **Forth** program (`RunTHINK`,
> `:2251`) — a partial-order insertion into a binary tree so the best action bubbles up. That needs
> the Forth VM, which is not started (§11). Every design without a custom AI takes the path that
> *is* ported.

**Verified by running a fight and watching it.** Four heroes and four orcs, all on auto, on a real
combat map generated from `SomethingWild`: they close over round 1, trade blows from round 2, and
the fight is decided in six rounds with four heroes standing. Nothing drives it but the ported
pieces — setup, round clock, AI, pathing, movement, attack. The hit-point bands showed up correctly
along the way, an orc at 0 reading `Unconscious` and one at −4 reading `Dying`, which is the
`giveCharacterDamage` correction above holding under real play rather than only in its own test.

##### Dying, bandaging and morale, as ported

`CombatUpkeep` holds the two passes that run at the head of every round, plus bandaging. **Only one
of the three does anything, and finding that out was most of the work.**

**`CheckDyingCombatants` is live and small**: every combatant whose status is `Dying` and who is
not bandaged takes one point (`Combatants.cpp:4697`). That is what gives the −1..−9 band its
meaning — without it a combatant knocked below zero stays there forever. Nine rounds and it reaches
−10 and dies.

**`Bandage` is the only escape, and it stabilises rather than heals**: the worst-hurt dying
combatant is set to **zero hit points and unconscious**, and `isBandaged` is set once and never
cleared for the rest of the fight (`Combatants.cpp:1271`). Exactly one combatant per action; ties
go to the later one, because the comparison is `<=`.

> The reference seeds its search with combatant 0 whether or not it is dying, then compares every
> candidate against it. It works because a dying combatant is below zero and a healthy one is not,
> so the first real candidate always displaces the seed. The port takes the minimum over dying
> combatants only, which agrees on every reachable case.

> **`CheckMorale` does nothing, deliberately, and the reason is quoted in the source.** The
> function computes a modifier from allies fled and slain and from being outnumbered three to one —
> and discards it, because the `SetMorale(GetMorale() - mod)` that would apply it is commented out.
> The decision is hard-coded:
>
> ```cpp
> //int cur_morale = GetAdjMorale();
> Flee = FALSE; //(RollDice(100, 1, 0) > cur_morale);
> ```
>
> Directly above sits a quoted email from the designer dated 2018-11-02 — "I would like the Morale
> value to not autochange" — so this is a removal, not an unfinished feature. Everything downstream
> is unreachable: **both** sites that set `fleeBecauseImpossible` are inside `if (Flee)`, and the
> block that would put a combatant into the running state is already excluded by an early return at
> the top of the same function. Morale is still loaded and stored from a monster's record, but
> `GetAdjMorale` appears **only in commented-out code** — nothing reads it for a decision.
>
> Nothing is ported because there is nothing to port. `CombatUpkeep.CheckMorale` is an empty method
> carrying this note, so the next reader does not spend an afternoon transcribing dead arithmetic.
> The live routes into fleeing are elsewhere: walking off the map, and an AI script setting the
> flag.

This makes **four** things now found dead by looking for consumers rather than by an `#ifdef`:
`ComputeDistanceFromParty`, `CombatantsStateText`, the morale computation, and the flee decision it
feeds.

##### Attacks of opportunity, as ported

`OpportunityAttacks` is `CheckOpponentFreeAttack` (`Combatant.cpp:10060`), called from the middle of
a step. It compares who was adjacent before the move with who will be adjacent after, and grants a
**free attack** to anyone the mover retreated from and a **guard attack** to anyone it walked up
to. Both interrupt through the turn queue, which already modelled exactly this —
`Push` with `affectStats: false`.

> **Read the pseudo-code specification at `Combatant.cpp:9990` first.** A quoted email lays out the
> whole guarding/free-attack scheme as a set of named script hooks — `Guarding-CanGuard`,
> `Guarding-Guard`, `FreeAttack-CanFreeAttack` and the rest — with their bodies in outline. It is
> the clearest statement of intent anywhere in this codebase and it is a comment.

- **The rules live in the design's scripts, not the engine.** The C++ asks
  `FreeAttack-CanFreeAttack` and `Guarding-CanGuardAttack` and does nothing unless they return an
  affirmative. **The polarity is the opposite of `IS_VALID_TARGET`**, where silence means yes:
  here silence means no attack at all. The port transcribes the scripts every reference design
  ships, so the defaults are the designs' rather than invented.
- **A guard attack requires the attacker to actually be guarding**, and to have attacks left.
  Exactly one is granted, always.
- **A free attack grants the attacker's *whole* complement.** The shipped script returns hook
  parameter 8, which is `GetNbrAttacks()` — total, not remaining. That is why `Combatant` now
  carries `TotalAttacks` separately.
- **A ranged weapon earns nothing**, which is the one rule both scripts agree on.
- **A casting combatant never gets one** — breaking off to swing would lose the spell.
- **Guard attacks are queued first so free attacks resolve first.** The queue is a stack, and the
  reference's own comment explains the ordering.
- **The mover is rewound to its old square while the attacks resolve**, with its destination parked
  on the queue entry (`SetXY`) so the step can finish afterwards. Its state is set back to `None`,
  so it does not resume mid-move.
- The reference passes `FreeAttackDistance` — a distance function that **always returns 1**
  (`:10047`) — into `canAttack`, so the weapon's range test always sees an adjacent target. Reach
  is decided by the adjacency scan, not by the weapon.

The adjacency scan runs `-1` to `width` and `-1` to `height` inclusive, so it covers the footprint
*and* the ring around it: a 1×1 combatant checks a 3×3 block.

##### Spell durations and stacking, as ported

`SpellDuration` and `SpellEffectList` are the half of the spell layer `SpellEffects` deliberately
left out: not how an effect changes a number, but **how long it lasts and whether it lands at all**.
This was blocked on the round clock and is now unblocked.

**One combat round is one game minute**, and that is the whole bridge. `StartNewRound` calls
`party.incrementClock(1)`, and that parameter is minutes (`Party.cpp:1422`) — so a three-round
spell and a three-minute spell are the same thing, and every duration is stored as an
elapsed-minute reading to compare against.

- **Every timed rate has a floor of one minute**, applied *after* the unit conversion. The comments
  on the three cases disagree about what the floor is called — "1 minute min" on rounds, "1 round
  min" on hours and days — which is one value under two names.
- **A stop time of zero means expire immediately.** It is the first test in `IsReadyToExpire`, so
  "no duration" and "already over" are the same state.
- **The two expiry paths disagree by one.** A script effect expires at `elapsed >= stop`, a spell
  effect at `elapsed > stop` (`Spell.cpp:983` against `:1000`). The same duration lasts a minute
  longer as a spell than as a script. Both are reachable and neither is obviously intended, so both
  are transcribed.

Three rules decide whether an effect lands (`Char.cpp:11989`), and their order matters:

1. **A negated effect never lands** — the `EFFECT_NONE` flag means a saving throw stopped it. The
   check carries a dated comment naming the person who reported effects landing *despite* a
   successful save.
2. **A non-cumulative effect refuses to stack, and the incumbent wins.** If anything already
   modifies that attribute the *new* effect is dropped — so a second casting of the same buff is
   simply wasted rather than replacing the first.
3. **`RemoveAll` clears the attribute first, sparing intrinsic character abilities**, which cannot
   be removed because they are part of what the character is.

Rules 2 and 3 are not exclusive — an effect flagged both cumulative and remove-all clears the
attribute and then adds itself, which is how a spell that overrides rather than stacks is written.
But rule 2 runs first, so a *non*-cumulative remove-all never gets to clear anything.

> **`Permanent` has no case in the reference at all.** The duration switch handles rounds, hours,
> days and the two counted rates, then falls to `default: die()` — leaving the stop time at its
> constructed value of zero, which means *expire immediately*. So a permanent spell effect lasts no
> time at all. `StopTimeFor` returns null for it rather than inventing a value the reference never
> computes, and `SpellDuration.PermanentExpiresImmediately` records what actually happens, as a
> named constant rather than a buried comment — otherwise the next reader will assume it is a
> porting mistake.

> **`ByDamageTaken` and `ByNumberOfAttacks` are declared, authored and unsupported.** The duration
> switch stores their count raw where a time belongs, and `IsReadyToExpire` reaches its error path
> for both (`Spell.cpp:991`). A design can select them in the editor and they will not work.

##### The combat screen, as ported

`CombatRenderer` is `displayCombatWalls` (`Drawtile.cpp:3085`) plus the coordinate helpers around
it. **Combat is now visible** — a real encounter on a real map draws with the design's own tiles.

Unlike the dungeon viewport there is no perspective and no slot geometry: every terrain square is
one 48×48 tile cut from the sheet at the coordinates the generated tile table gives, so the whole
renderer is a double loop and a blit.

- **The art comes from the zone, not the design.** Each zone names an indoor and an outdoor combat
  sheet (`ZoneRecord.IndoorCombatArt`) and the engine picks on whether the encounter is outdoors
  (`Dgngame.cpp:1126`). `SomethingWild`'s zone 0 names `combat_Dungeon.png` / `combat_Wilderness.png`
  and both ship.
- **The reference draws two squares beyond the view on every side** — its loops run `start - 2` to
  `start + tiles + 2` — so a part-scrolled edge has something under it rather than a gap. That is
  what the clip rectangle is for, and it is why a test asserts every pixel of the area gets painted
  under a scroll.
- **The dying are drawn before the living**, which is why the grid keeps two occupancy layers:
  `TERRAIN_CELL`'s own comment says "dying dude is drawn before regular dude", so a combatant
  standing on a corpse hides it rather than the other way round.

**Verified by rendering it and looking**, which is the habit this document keeps recommending and
the reason this step was worth doing before the Forth VM. Two frames from `SomethingWild`: an open
stretch at level (5,5) that comes out as unbroken floor — confirmed against the terrain indices,
which really are all tile 23 there — and level (1,2), which draws a horizontal stone wall across
the top and a diagonal corridor wall down the left, matching the `6`/`11` pair and the `4`/`14`/`5`
staircase in the generated terrain exactly.

> **The engine half is still missing.** `EventBodyReader` already dispatches `EventType.Combat` to
> `CombatEventReader`, so a level's events do produce `CombatEvent` objects — but `EventRunner`
> falls through to its unsupported arm for them, so walking onto one still prints
> `[Combat here -- not implemented]`. What is needed is combat state in `Game`: build the encounter
> from the event's monster list, run the loop the scratch harness already proves, and present it.
> Every piece below that is done.

##### Building an encounter, as ported

`EncounterBuilder` is `AddCombatants` / `AddMonstersToCombatants` (`Combatants.cpp:490`, `:660`) —
the join between the event reader and the combat machinery, turning a `CombatEvent`'s monster ids
and quantities into `Combatant`s.

- **The party goes in first, always.** The reference says so in a comment and everything downstream
  depends on it: combatant indices *are* grid occupancy values, and `CombatPlacement` takes the
  party from the front of the list.
- **A random encounter picks one *entry*, not one monster.** With the flag set a single entry is
  drawn and its quantity is still rolled, so "random" means which kind shows up rather than how
  many.
- **The two quantity branches differ.** The random one clamps to the remaining room *before*
  flooring at one; the ordinary one does not clamp at all and relies on the add loop running out of
  room. Both then floor at one, so a cap of zero still yields a monster.
- The quantity modifier is a percentage of the rolled count, truncated with it — +50% on a roll of
  3 gives 4.
- `RollDice(sides, times, bonus)` returns **the bonus alone** when either count is non-positive
  (`Globals.cpp:4925`), which is why the random pick clamps its index at zero.

> **Monster footprints are all 1×1 for now, and that is a stand-in.** `determineIconSize` divides
> the *loaded icon's* pixel dimensions by the tile size — so a design's art decides how much room a
> monster takes and the monster record alone cannot answer it. One square is the safe default: too
> small never refuses a placement that should have succeeded, whereas too large would.

##### The combat menu and cursor, as ported

`CombatMenu` is `COMBAT_EVENT_DATA::OnUpdateUI` (`RunEvent.cpp:19533`) and `AimCursor` is
`GetNextAim` / `GetPrevAim` (`Combatants.cpp:1363`). **The first thing in this port a player drives
rather than watches** — everything before it has been computer-run.

The fifteen commands are MOVE, AIM, USE, CAST, TURN, GUARD, QUICK, DELAY, BANDAGE, VIEW, SPEED,
WIN, READY, END and a design-supplied special whose default label is SWEEP.

- **`READY` is disabled unconditionally** (`:19584`), ahead of every conditional rule. Not a stub —
  the reference simply never enables it.
- **`WIN` is editor-only.** It forces a victory, so it is gated on `EditorMode()` rather than on
  anything about the fight.
- **Casting is refused for two separate reasons** — the caster cannot, or the *zone* forbids magic
  — tested one after the other against the same entry. A design can have a no-magic zone that
  silently disables spellcasting for everyone standing in it.
- **A computer-run combatant gets the whole menu greyed**, and so does a turn with no current
  combatant. The one exception is a moving auto combatant, whose title becomes the move readout and
  whose menu is left alone.
- **Only enemies are cycled by the cursor.** Party members and anything friendly are skipped, so
  the cursor cannot be walked onto your own side even though the map has no such restriction — and
  when nothing is targetable it **comes home to the acting combatant** rather than staying put or
  reporting failure.

> **`CombatCommand` is one-based and `Menu.SetItemEnabled` is zero-based.** The enum keeps the
> reference's numbering so it can be checked against `RunEvent.cpp:19566` directly, and the
> conversion lives in one named place rather than being absorbed silently. Passing a command
> straight to the menu disables its *neighbour* — the same off-by-one the treasure screen's indices
> set, and this document's note about that is what made it obvious to look for here.

##### The combat session, as ported

`CombatSession` owns one fight end to end: `EncounterBuilder` makes the combatants, `CombatSetup`
places them, `CombatRound` orders the turns, `MonsterAi` decides for the computer-run ones,
`CombatMenu` offers the player its commands, and `CombatRenderer` draws it. **A fight now runs from
a real `CombatEvent` to a verdict**, with or without a player in it.

**The reference has no object like this.** Its combat lives in `COMBAT_DATA`, a global, driven by
the `CProcinp` task scheduler through `RunEvent.cpp`'s state machine. The engine here is still a
synchronous loop (§7 Phase 4 item 5), so the session owns the fight directly and the scheduler
stays unported — keeping it out of `Game` is also what lets a fight be driven in a test with no
loaded design.

Only four commands do anything: MOVE walks to the cursor, AIM attacks what it lands on, GUARD and
END finish the turn. **The rest report that they are not implemented rather than silently ending
the turn** — a command that appears to work and does nothing is worse than one that says so.
Casting is greyed at the menu, since the casting half of the spell layer is unported.

> **`CheckIdleTime` is not a per-round activity flag, and an earlier revision of this port invented
> one.** The real rule (`Combatants.cpp:4480`) takes the smallest `currentRound − lastAttackRound`
> across **every** combatant and calls the fight off only when even that exceeds twenty — so a
> single combatant still swinging holds the whole encounter open. `CombatRound.RecordActivity` has
> been replaced by `IsIdle` / `CheckIdleTime`, which is what the reference does.
>
> Two consequences worth stating, because a test got both wrong first time. **The rule keys on
> attacking, not on hitting**: `Attack.Resolve` stamps `lastAttackRound` whether or not the blow
> lands, so two sides swinging and missing forever are *not* idle and the fight does not end. And
> `lastAttackRound` **starts at zero**, not at a sentinel — a sentinel would overflow the
> subtraction.

##### Combat reachable from the engine, as ported

`Game` now routes a `CombatEvent` to a `CombatSession` instead of `EventRunner`'s unsupported arm.
**Walking onto a combat event starts a fight** — verified against `SomethingWild`'s level 1, whose
first encounter is a Tiger: six party members from the design placed in formation, the Tiger read
out of `monsters.dat`, and a fight that runs to a conclusion and chains on.

- **Combat owns the viewport**, like a full-screen event: `Render` draws the map instead of the
  dungeon view rather than compositing over it. Same distinction the treasure screen makes.
- **The terrain sheet comes off the zone the party is standing in**, not the design.
- `LoadedDesign.Monsters` / `Monster(id)` were added alongside `Items`, with the same framing and
  the same unstamped-version fallback.

> **Wiring it up immediately exposed two gaps that every isolated test had missed** — which is the
> argument for doing integration before more breadth.
>
> 1. **Monsters arrived with zero hit points and died on the spot.** `EncounterBuilder` never
>    rolled hit dice. The rule (`Char.cpp:4941`) is d8-based with two traps: `UseHitDice` being
>    **false means the field holds hit *points***, taken literally with no roll and no bonus; and a
>    **fractional hit die scales the sides, not the count** — under one die the roll is
>    `1d(8 × hd)`, so a half-die monster rolls 1d4.
> 2. **Monsters never acted at all.** `Initiative` was left at zero and the round's walk runs 1 to
>    22, so no monster was ever reached. `UAF.Rules.Initiative` had been ported long before and was
>    simply never *called* — `DetermineCombatInitiative` rolls it fresh every round, and surprise
>    is cleared after the first (`Combatants.cpp:1500`).
>
> The first fight ran 21 rounds with only the party acting and ended on the idle rule. Both are now
> covered by tests that name the symptom.

##### Combat icons, as ported

`CombatIcons` is `determineIconSize` and the measuring half of `LoadCombatIcon`
(`Combatant.cpp:8579`, `:8492`). **Combatants now draw**, and the 1×1 stand-in in
`EncounterBuilder` is gone.

- **A footprint is measured off the art, never read from the record.** Nothing in `MONSTER_DATA`
  says how many squares a monster occupies — the engine divides the loaded sprite's pixel
  dimensions by the tile size. That is why `EncounterBuilder` could not size a monster alone, and
  why the art has to be resolved *before* placement rather than at draw time.
- **A sheet holds poses side by side, two per icon** — ready and attacking. Width is
  `(sheetWidth / 48) / 2 / (frames / 2)`, height `sheetHeight / 48`, each flooring at one. **The
  divisions are separate integer steps**: collapsing them to `w / n` changes the answer for sheets
  that are not exact multiples.
- **There is no upper clamp.** A comment in `Drawtile.cpp` says icons are at most 4×4 and nothing
  enforces it — `SomethingWild`'s Red Dragon measures **8×4** off a 768×192 sheet. Real footprints
  from that design: Orc 1×1, Tiger 2×1, Hill Giant 1×2.
- **The `cm_` / `cn_` prefix is a fallback, not the normal path.** `LoadCombatIcon` calls
  `LoadPicSurfaces("")` first and only uses the prefix when falling back to the default monster
  icon. No reference design ships a `cm_`-prefixed file.
- The `iconIndex` rewind is transcribed with its own oddity: the bounds test compares
  `offset + width·48` against `imageWidth − width`, subtracting a *square count* from a *pixel
  count* (`:8615`). It is off by nearly a whole tile; reproducing it keeps the same frames
  reachable.

**Looked at, as always.** A real encounter on `SomethingWild` level 1 now renders the party's own
icons and the Tiger at 2×1 beside them, with the roster and "Tiger hits Human Fighter for 3." in
the message line. The terrain came out flat where an earlier frame was textured — checked rather
than assumed, and it is correct: that zone names `combat_DungeonStreet.png`, a dirt street rather
than flagged stone, and its 123 wall squares lie outside the visible window.

##### The cursor, and an alignment bug it exposed

`CombatRenderer.DrawCursor` is `displayCursor` (`Combatants.cpp:4733`). Alpha-blended at
`aval` = 100 — the reference's comment beside the blit calls that "50%"; it is 100/255, nearer 39%,
so **the value wins over the comment**. Two modes: a single square, or `coverFullIcon`, which
highlights **every square of the combatant under it** so a player can see the whole of a large
monster being targeted. A cursor that would overhang the view is **dropped whole**, not clipped.

> **That whole-or-nothing rule is what surfaced a real defect.** `CombatRenderer` defaulted its
> origin to the C++'s `CombatScreenX/Y` of (14, 16) — but the reference draws combat on its own
> full screen, while this port draws it in the **dungeon viewport**, which `SomethingWild` puts at
> (48, 54). Every square was therefore drawn 34 pixels up and left of where it belonged, *clipped
> rather than aligned*, which looked almost right in a frame and was not. The cursor, computed at
> screen x 254 against a view ending at 224, fell outside and vanished — and that is what made the
> misalignment visible at all. `CombatSession.ViewArea` now derives both the origin and the
> visible-tile counts from wherever the caller is drawing; the old hard-coded 10×8 was wrong too,
> since that viewport fits about 3×4.

**The view is now centred on the party when combat opens**, as the reference does
(`PlaceCursorOnCurrentDude`). Without it the first frame looked at the map's corner — a screenful
of empty floor a long way from the fight.

##### The combat golden frame, as added

`CombatGoldenFrameTests` hashes the viewport for a real encounter at three points in a
deterministic fight, the same shape as `GoldenFrameTests` does for the dungeon view. A regression
guard, not an oracle.

> **Two of its own assertions were wrong before the code was.** The colour-variety floor was set at
> 200 by analogy with the dungeon guard and failed on a perfectly correct frame: this zone's floor
> tile is a **flat colour**, so terrain alone gives exactly *one*, and all the variety comes from
> the combatant icons. And the three sampled step counts collapsed to two distinct frames, because
> past a certain point the fight is simply waiting on the player and the screen legitimately stops
> changing. Both were fixed by measuring what the frames actually contain rather than by tuning the
> numbers until they passed.

##### What the AI is told it can attack with, as ported

`ListWeapons`, `ListAttacks` and `GetWeaponRange` (`Combatant.cpp:1142`, `:1308`, `:1089`).
`UAFcore/AiWeapons.cs`. **The scripted AI now runs in a real fight** — `CombatSession` hands
`MonsterAi` the acting combatant's weapons, and the ordering (§the monster AI's priority ordering)
picks the action.

Two lists, kept separate because they behave differently: carried **weapons**, which are items
readied in the weapon hand, and natural **attacks**, which a monster's record supplies.

> **A weapon's reach and a combatant's distance use different transforms, and are compared
> directly.** A distance is `(2d)²` (§the monster AI's priority ordering); a reach is
> **`(2r + 1)²`** (`Combatant.cpp:1135`) — half a square longer before squaring. `TooFar?` compares
> them anyway, and the half-square is exactly what makes a reach of `r` cover a distance of `r`:
> `(2r+1)² < (2d)²` reduces to `r < d − ½`, which for whole squares is `r < d`. Using one transform
> for both happens to give the same answers for integer ranges, which is how it survives casual
> checking. **A reach above 90 becomes 32767** rather than its square — the clamp comes first, so
> the formula's 32761 is never produced.

> **The natural-attack damage estimate has its dice operands transposed.** `ListAttacks` computes
> `5 × ((1 + nbr) × sides + 2 × bonus)`; `ListWeapons` computes `5 × ((1 + sides) × nbr)`. The
> `1 +` lands on the die *count* instead of the die *size*. The bonus term is right in both — the
> outer 5 turns `2 × bonus` into `10 × bonus`. Only the dice are swapped, and the effect is
> systematic: `1d8` and `3d2` both average 4.5, and the estimate calls them **80 and 40**. The AI
> overrates few-but-large dice and underrates many-but-small ones — a dragon's bite against a
> swarm's nibbles. Nothing else reads the number, so it only ever changes which natural attack the
> AI prefers. Reproduced.

> **A weapon firing ammunition takes its damage dice from the ammunition, not from itself.** The
> estimate is `ammo dice + ammo bonus + weapon bonus` (`ListActionsByAmmo`, `Combatant.cpp:1503`) —
> the weapon's own dice are dropped entirely. That is the tabletop convention (the arrow does the
> damage, the bow adds its bonus), and the AI's ranking of two bows depends on it, so the weapon's
> dice and bonus have to be kept apart rather than pre-summed.
>
> **One candidate per kind of ammunition**, so a bow with two sorts of arrow is two actions and the
> script picks the better. **A weapon that names an ammunition type and has none to hand yields
> nothing at all** — an archer out of arrows is offered no ranged action rather than a weak one. A
> weapon with no ammunition type (a sling, a thrown dagger) uses its own dice.

Only items in the weapon-hand slot count as weapons; **ammunition is taken wherever it is carried**,
with no readied-slot test, and its quantity comes from the carried stack rather than the database.
The reference additionally asks `CanReady` — class restrictions, curses, hands free — which this
port does not model yet, so every weapon-hand item is taken.

##### Enumerating the AI's candidate actions, as ported

`ListActions` and its three children (`Combatant.cpp:1770`, `:1522`, `:1619`, `:1649`) — building
the list that §the monster AI's priority ordering ranks. `UAFcore/AiActions.cs`, and
`MonsterAi.Think` now takes the combatant's weapons and uses the ranked plan when given them.

One candidate per (target, weapon) pair, one per unarmed attack, and an advance on every target.

- **Every combatant is considered as a target, including the acting one.** The loop runs from zero
  over all of them; what stops a combatant attacking itself is the *friendly* test, not an identity
  test — **except for unarmed attacks**, which do check explicitly (`:1629`).
- **An ordinary weapon is never offered against a friend** (`if (friendly) return`, before the
  ranged/melee split). A spell item gets past that point, but the script's `SpellCasterFilter`
  opens with `FGDP?`, whose first test is `Friendly? ?EXIT` — so it is refused a step later anyway.
  The two arrive at the same place by different routes.
- **A spell item that names no spell yields no action at all**, rather than a failed one: the
  reference returns before setting an action type.
- **Advancing is offered even on an adjacent target.** The `distance22 > 8` guard was removed in
  2016 with a long comment explaining why — a combatant out of attacks could not advance on the
  enemy beside it, so it advanced on a further one, then back, forever. The engine turns an advance
  on an adjacent target into a guard, which is what this port does too.

> **A design's `CanTargetFriend`/`CanTargetEnemy` flags never reach the AI.** The checks that would
> have applied them are commented out in both spell branches (`:1561`, `:1578`), so a monster's
> choice of spell target is the script's business alone.

`MonsterAi.Think` keeps its older, simpler rule when no weapon list is supplied — every existing
caller still works — and follows the script when one is. The remaining gap is the caller: nothing
yet builds an `AiWeapon` list from a monster's readied items, so combat still runs the simple path.

##### The monster AI's priority ordering, as ported

What the shipped `AI_Script.BLK` decides (`RunTHINK`, `Forth.cpp:2510`).
`UAFcore/MonsterAiScript.cs`. **This is the script's decision function, not the Forth VM that runs
it** — see the trade-off below before reaching for either.

`THINK` is a **comparator**, not a planner. It is handed two candidate actions and returns A minus
B, positive meaning A is preferred; the caller heap-sorts the candidate list with it
(`Combatant.cpp:2240`). The script's own comments are the specification, and the order is:
spell-caster items ("used first if the monster has them"), spell-like abilities ("Dragon Breath,
Medusa Gase"), ranged weapons by average damage, melee likewise, unarmed, advancing on the nearest
enemy — "the only action left is to guard".

> **Every distance in the script is `distance22`, and none of them are square counts.** `C:Distance`
> pushes that field (`Forth.cpp:2149`), which is `4 × (dx² + dy²)` between the **nearest edges** of
> the two footprints (`Distance22`, `Combatant.cpp:1674`) — doubled and then squared, which is what
> the trailing "22" means. Reading the thresholds as squares inverts two of the three range rules:
> `TooNear? … 5 <` is `4d² < 5`, which holds only at `d ≤ 1`, and `NotAdjacent? … 8 >` is
> `4d² > 8`, which holds from `d ≥ 2`. Both are adjacency tests, not five- and eight-square reaches.
> This port got them wrong first time and the arithmetic caught it, not a test.

Filters run first, per action type, and they are not the same:

- `FGDP?` — "Friendly Gone Dead Dying or Petrified Targets should not be attacked". **Friendly is
  tested first and exits early**, so an ally is refused whatever its condition. An *unconscious*
  target is not in the list at all and is still attacked.
- **A ranged weapon refuses an adjacent target** as well as a distant one. That is the whole of
  `TooNear?` — a monster with a bow will not shoot somebody standing next to it, and will shoot
  anything two squares out.
- **Judo reaches exactly the adjacent squares**, diagonals included: a diagonal neighbour has a gap
  of one on both axes, giving `distance22 = 8`, which `> 8` does not exceed.
- **The ranged/melee split is made on the weapon, not the target**: `range22 > 9` is `4r² > 9`, so
  reach 1 is melee and reach 2 or more is ranged.
- **Advancing skips the range tests entirely** and checks only the target's condition, which is the
  whole point of it.

> **The melee test is a subtraction of two booleans, with the operands the other way round from
> every neighbouring test.** The script writes
> `B A:Type A:T:MeleeWeapon = A A:Type A:T:MeleeWeapon = -`, which is `isMelee(B) − isMelee(A)` —
> and it comes out right only because Forth's `=` yields **−1** for true. Read as C, the sign is
> backwards and monsters prefer *not* to use a weapon.

> **Two of the eight tests compare weapon type, the rest compare action type.** The spell-caster
> and spell-like tests read `W:Type`; everything after them reads `A:Type`. Using one throughout
> silently changes which actions are preferred.

**The trade-off, stated plainly.** The reference evaluates a 143-line Forth program through a
2,534-line indirect-threaded interpreter (`Forth.cpp`). Porting the interpreter is a subsystem;
porting what the program decides is a table. Two facts make the second reasonable:

1. `AI_Script.BLK` **ships in every design's `Data` folder**, and `ExpandKernel` calls `die()` when
   it is missing (`Forth.cpp:2333`) — so it is engine data with a version, not design content.
2. Across the four reference designs there are exactly **two versions, differing by one line**:
   1.01 (October 2014) adds `Dying?` to the do-not-attack filter; 0.999785 (August 2014) lacks it.
   Both are reproduced, selectable by `AttacksTheDying`.

**What this does not do**: honour a design that edited its own script. That needs the VM, and the
VM is still worth building — but it is a smaller prize than "the scripted AI is unported" suggested,
and this is why.

##### Driving a design through a fight, as tested

`GameCombatTests` — the first tests that walk a real design through a whole encounter: start, rounds,
verdict, spoils, and the engine handed back. Everything else about combat is unit-tested; this
covers the **sequencing through `Game.Update`**, which is where the ordering bugs have been.

Three things a future reader will hit trying to write one of these:

- **Most combat events sit at (−1, −1).** They are chained to, not stepped on, so a party cannot be
  walked onto one. `Game.StartEvent` is public for this — events are reached by chains and, later,
  by scripts, not only by walking. It had been reached by reflection before
  (`BindingFlags.NonPublic`) in the golden-frame test, which is the same need answered worse.
- **Party members are player-run, so every turn of theirs must be driven to END.** Pressing Return
  blindly re-selects MOVE, which fails with nowhere to go and never ends the turn — the fight then
  makes no progress at all and the test times out looking like a hang.
- **A fight where nobody ever hits does not end.** Rolling a 1 for everything does not produce a
  stalemate: the idle rule keys on *attacking*, not on hitting, so two sides swinging and missing
  are never idle. A test written to assert termination under those dice is asserting the opposite
  of the rule.

##### The treasure a fight leaves, as ported

The treasure half of the combat results screen (`RunEvent.cpp:19795`) — the fallen's possessions now
reach the party through the ordinary treasure screen. `CombatAftermath.Merge` and
`IsWorthShowing`, plus the wiring in `Game`.

The reference builds a `GIVE_TREASURE_DATA`, fills it from the dead, and **pushes it ahead of the
combat event's own exit** so the chain is followed *after* the screen rather than instead of it.
This port synthesises the same event from the spoils (§the combat aftermath) and holds the chain
across it.

- **Coins add per denomination; gems and jewellery pile up**, being individual objects. A monster
  record need not carry every denomination, so a short coin list is treated as zeroes beyond its
  end rather than as an error.
- **An empty pile is not offered at all.** The reference deletes the event rather than pushing an
  empty screen, so a fight against penniless monsters shows nothing and exits straight to the
  chain.
- The synthesised event borrows the combat event's own base, so its picture, text and control block
  are the design's own.

> **The borrowed base brings the combat event's chain fields with it**, which is exactly what must
> *not* be followed when the screen closes — doing so would run the same destination twice. The
> destination held back when the screen was raised wins instead.
>
> **Three ways that hold could go wrong, all closed.** `StartEvent` can decline a screen outright
> (no font, nothing can be presented), leaving nothing to release the chain — so it is followed
> directly. It can finish one immediately, in which case `Apply` has already released it. And the
> screen can simply be *running*, where the hold must survive until it closes. The first draft of
> this port handled the first two and got the third backwards, clearing the hold and following the
> chain while the screen was still open.

`CombatAftermath.TreasureScreen` decides whether a screen is raised at all, and lives with the rest
of the aftermath rather than in `Game` — the decision is aftermath logic, and putting it there makes
it directly testable rather than reachable only by driving a whole fight.

##### The archive writer's first layers, as ported

`MfcArchiveWriter` — the exact inverse of `MfcArchiveReader` — with `AslWriter` and
`SpecabWriter` on top of it. **The first pieces of the writer Phase 1 has been missing.** Everything above it has a reader and no counterpart, which is why the
round-trip exit criterion is unmet and why Phase 5 cannot start.

The tests read back everything the writer writes. The reader is the specification: it was diffed
against the C++ oracle, so agreeing with it is the strongest claim available without regenerating
goldens. What they do **not** prove is that a design file round-trips — that needs the record
writers on top of this.

> **The two variable-width encodings are different schemes, and both escape on `0xFFFF`.** A string
> length has *three* tiers — a byte, escaping to a word, escaping to a dword — while a collection
> count has *two* and no byte form at all. So a count of 3 costs two bytes where a string length of
> 3 costs one. Using one for the other produces a stream that reads back plausibly for small values
> and desynchronises for large ones, which is the worst way for a format bug to behave.

- **The tier boundaries are exclusive.** A string length of exactly 255 does not fit the byte tier,
  because 255 *is* the escape; likewise 0xFFFF for the word tier.
- **A string's length is in bytes, not characters.** Windows-1252 makes those the same for
  everything it can encode, but a character it cannot becomes a single `?`, so the count has to come
  from the encoded bytes rather than from the string.

**The first record-level writer on top of it is `AslWriter`**, chosen because ASL is the leaf every
other record ends with — nothing above it can be written until this can. All three write paths are
there: `Serialize` (everything, what a design file holds), `Save` (skipping read-only, what a
savegame holds) and the 32-bit-count `DeSerialize` form that `races.dat` uses.

> **The savegame count must be of the filtered set, not the whole one.** The reference walks the
> list twice for exactly this reason and asserts the two agree afterwards. Counting everything and
> writing some produces a file that reads back cleanly with silently missing attributes — which is
> the failure that assert exists to catch.

> **Only the uncompressed path can be written.** The compressed one applies a key fixup on read
> that is **not invertible**: it maps every character below `0x20` up by `0x20`, so a key read as
> `'%'` could have been written as `'%'` or as `0x05`. A compressed writer cannot be derived from
> the reader; it needs the pre-fixup key, which only the producing code knows.

**`SpecabWriter` is the second leaf** — every record writes its special abilities immediately
before its ASL, so both are needed to write any record at all.

> **There is only one write path, whatever the version.** The reference's legacy branch is gated on
> `version <= 0.920 && !ar.IsStoring()` — *reading only* — so an old design is read in the old shape
> and written back in the new one. Mirroring the reader's fork produces files the reference cannot
> read. The legacy branch is additionally inside `#ifdef UAFEDITOR`, so the engine never reads it
> either: writing the modern `A_CStringPAIR_L` unconditionally is not a simplification, it is the
> only behaviour the format has.

> **A block still in the legacy shape is refused rather than written empty.** The reference converts
> legacy slots to modern pairs as it reads (`Specab.cpp:1196`); this port keeps them unconverted,
> so there is no honest modern form to write yet. Emitting an empty block instead would produce a
> file that reads back cleanly with every special ability silently gone — so `Write` throws and
> `CanWrite` lets a caller find out before it has started a file.

Two contrasts with the sibling ASL block, which is easy to conflate since both live in `ASL.cpp`:
**the count is a 32-bit `int` where ASL uses a `WORD`**, and **the strings are verbatim** where
ASL's legacy path wraps them in the `DAS` blank convention. There is no map name and no flags byte,
so a desynchronised stream has nothing here to announce itself with — but a legacy reader handed a
modern block does run off the end rather than quietly finding nothing.

> **Where the real blocks are is not where you would look.** Round-tripping against shipped designs
> found that **every monster carries an ASL block** — 195 of 195 in `SomethingWild`, 44 of 44 in
> `ci-tier3` — while **no item in any design does**, and only four spells in one. The first draft of
> the corpus test used items and passed by finding nothing; the guard assertion beside it,
> asserting the corpus is non-empty, is what caught that and is why it stays.

##### The event layer, as ported

**Every one of the 44 event types now has a reader.** The eleven that were missing —
`Damage`, `EncounterEvent`, `EnterPassword`, `HealParty`, `JournalEvent`, `PlayMovieEvent`,
`SmallTown`, `TakePartyItems`, `TavernTales`, `Vault` and `WhoTries` — are in
`EncounterEventReader`, `PartyEffectEventReaders`, `TrialEventReaders` and `MoreEventReaders`.
Only three ordinals have no body, for two distinct reasons: `NoEvent` produces no object by
design, and `InnEvent` and `GPDLEvent` reach `die(0xab51a)` in `CreateNewEvent` — the first
commented "never", the second not in the switch at all — so neither can occur in a design the
reference could load.

> **These eleven are the weakest-verified readers in the port, and the reason is structural.** The
> six level-bearing designs in the corpus hold 6,234 events and use **27** of the 44 types.
> Every one of the eleven appears **zero** times. `EventWalkTests` — the marker-counting drift
> detector that is what actually proves the other readers — cannot reach them at all.

What stands in for a corpus, given a synthetic fixture can only pin a convention and never
discover one:

- **Each field list was cross-checked against the type's `Export(JWriter&)`**, a *separate*
  description of the same record written independently of `Serialize`. That is what confirms
  nothing is missing or invented, and it is how `PASSWORD_DATA::matchCase` was pinned as
  exported-but-not-serialized — a field a reader written from the class declaration would insert
  four bytes for.
- **Every test asserts the stream lands exactly at the end of what it wrote**, so a wrong width
  fails as a length error rather than as a plausible value.

The traps found, all of the same family — **a `BYTE` among 4-byte `BOOL`s**, which is what the
declarations interleave and `Serialize` hides: `HEAL_PARTY_DATA.chance` and `LiteralOrPercent`,
`TAKE_PARTY_ITEMS_DATA.takeItems` and `WhichVault`, `VAULT_EVENT_DATA.WhichVault`,
`WHO_TRIES_EVENT_DATA.strBonus` (immediately after *sixteen* consecutive `BOOL`s). And two fields
that are serialized despite their names: `SMALL_TOWN_DATA.Unused`, and the eight thief-skill flags
in `WHO_TRIES_EVENT_DATA` that the storing branch writes as literal `FALSE` and the loading branch
still reads.

**On the engine half, seven more types execute**, bringing it to sixteen of 44:

> **`CHAIN_EVENT` replaces itself with its target rather than chaining to it**
> (`RunEvent.cpp:10974`). Its own `chainEventHappen` is never consulted, and a target the level
> does not contain **ends the run** — the reference pops the event rather than falling through.
> Both halves of that are easy to get wrong in the direction of "chain normally", which would send
> a design down a path it never authored. 165 of them in `Case.dsn` alone.

> **A random event's chances need not sum to 100.** The die is sized to whatever they add up to,
> so a design using 1/2/3 gets sixths; normalising to a percentage would change the outcome of
> every such design. A branch counts only if its chance is above zero **and** its target exists,
> and the dead branch's weight leaves the total rather than vanishing into a dead end — so the
> survivors keep their relative odds. The boundary belongs to the earlier branch: with 30 and 70,
> a roll of 30 takes the first.

> **`FLOW_CONTROL_EVENT_DATA` is the design's `if` statement**, and the most common unexecuted
> type there was — 314 across the corpus. It modifies a global attribute and then branches on the
> result, and the order matters: **the modification happens first and is not conditional**, so a
> design using flow control purely as a counter still counts even with the action set to `NONE`.
> `Game.Globals` is the store, which is the one the scripting host and the savegame already read.

> **Increment and decrement do nothing at all to a variable that does not exist.** The reference
> breaks out before the insert, so there is no implicit "starts at zero" — only `SET` creates one,
> and it writes flags of `0` where the other two write `MODIFIED`. A design that increments an
> unset counter gets no counter, which is worth knowing before concluding its logic is broken.

> **Only `ACTION_NONE` is distinguished.** `GOTO`, `CALL`, `RETURN` and `POP` all take the same
> branch, so the call stack the last three imply was never built. Reproduced deliberately: a design
> using `CALL` today gets a `GOTO`, and inventing a stack would change what it does.

The comparison is textual throughout: increment reads with `atoi` semantics — leading digits, zero
for anything else — writes back with `%d`, then compares the design's value *string* against that
numeral, so `"007"` never equals an incremented `"7"`. `int.TryParse` is not a substitute for
`atoi`; it rejects `"12 apples"`, which a design may well contain.

> **Special items and keys are global, not carried**, which is why they live on `WorldState` next
> to quests rather than on a character. **Possession is a stage, and the stage doubles as the
> flag** — giving sets stage 1, taking sets stage 0, and `HasSpecialItem` asks whether the stage is
> above zero. The reference guards a give with `if (!hasSpecialItem(...))`, which is what stops a
> re-give from rewinding an item that has progressed past stage 1.

> **An id the design does not define is skipped, not created** ("Bogus special item index",
> `Party.cpp:3203`), so an event left pointing at a deleted item is silent rather than resurrecting
> it. `WorldState.DefinesSpecialItem` is the distinction — "defined" is not "held".

The list is applied on **Return, not on arrival**, so a run abandoned before the keypress leaves
the party without the item. `ForceExit` is not ported: the reference posts
`TASKMSG_MovePartyBackward` and this port has no task queue to post to, so the party stays standing
on the event.

> **A quest event does not necessarily touch a quest.** The packed `m_quest` carries a type in its
> top bits, and it can be `ITEM_FLAG` or `KEY_FLAG` instead — so this is the *second* way a design
> hands out plot tokens, sharing `WorldState` with the special-item event. **The state calls do not
> follow the type**: `SetStage` respects it while `SetComplete` and `SetFailed` always land on the
> quest store. That asymmetry is the reference's and is reproduced.

> **`QA_OnNo` takes the quest when the player answers No.** It reads like a mistake and is not —
> a design uses it for a question phrased as a refusal. Collapsing it into `QA_OnYes` would invert
> every such event. Note also that `QA_Impossible` still *asks*, even though the answer changes
> nothing; only the two `…Auto` forms skip the question.

> **"In progress" is only set at stage 1**, and only when the event is not completing the quest. A
> design that advances straight to stage 3 never starts the quest — worth knowing before concluding
> a quest tracker is broken.

> **An unreachable branch is not the same as no branch.** When the accept or reject chain names no
> event, the two automatic operations fall back on the ordinary chain while the rest *end the run*
> — the reference pushes a do-nothing event, which amounts to the same thing. An automatic quest
> event has no branch to name, which is why the two differ.

> **`UTILITIES_EVENT_DATA` is how a design does sums**, and it draws nothing —
> `OnInitialEvent` clears the menu and `OnIdle` does the work. Every special item, key and quest
> carries a `stage`, and this reads, writes and compares them in three switchable parts:
> arithmetic on one token, a check across a list, and an award to a third.

> **Adding to a quest is a different operation from adding to an item.** Items and keys get a plain
> add clamped to 65535; a quest goes through `IncStage`, which clamps to `QUEST_COMPLETED_STAGE`
> (0xFDE8), **refuses to act on an already-complete quest**, and re-derives the quest's *state*
> from the stage it lands on. The reference's own comment says why — "cannot add to a quest and
> make it fail", failure being the sentinel one above completion. **Subtraction has no such
> guard**: all three stores take the plain path, so subtracting can drop a quest out of completion
> without touching its state.

> **An empty item list never activates**, under either check — "all of nothing" would be vacuously
> true and the reference writes `activate = FALSE` explicitly. But a list of nothing but *blank*
> entries does pass `AllItems`, because a negative index is skipped rather than failed and the list
> is non-empty. And a quest counts as held on its **state** where an item counts on its **stage**:
> two different questions in one loop.

> **The award is not symmetric either.** Items and keys are incremented; a result quest is set to a
> literal 1, so awarding the same quest twice does not advance it. `endPlay` pushes `EXIT_DATA` —
> the only route a design has to ending the game.

> **A guided tour's blank slots are skipped, not terminators.** The step array is a fixed size and
> the reference advances past every `TStep_NoMove` *before* testing whether the tour is over, so
> `TourOver()` reduces to "ran off the end of the array". Treating a blank as the end would
> truncate any tour with a hole in it — and a design that leaves a gap while editing gets one.

> **An out-of-range starting square abandons the event outright** — no steps, no chain. Returning
> "did not happen" instead would send the design down its not-happened branch, a route it never
> wrote. This is why the tour is handled beside `CHAIN_EVENT` rather than through
> `ExecuteWithoutInput`, whose `bool` answer always ends in a chain.

> **The tour runs to its end in one go rather than a step at a time**, and that is a real
> difference. The reference drives it from `TASKTIMER_GuidedTour`, so the player watches the party
> walk and a `Pause` holds its caption on screen; this port has no scheduler to hang that timer on
> (§4.4), so only the last caption survives. Where the party ends up, which way it faces and
> whether the destination's event fires are all correct — the animation is not there. The walk also
> passes *through* squares without setting off what is on them, which is the reference's
> `movePartyForward(0)`, and only fires an event at the end if the tour asks.

**Four more types were ported in parallel by subagents**, each delivering a pure static plus tests
in the same shape as `Quests` and `Utilities`, with all wiring done centrally afterwards so the
conflict surface stayed at zero. All four now execute.

> **There is no character-selection screen in this format, and looking for one was a wrong turn.**
> `GameEvent::TABParty` (`RunEvent.cpp:792`) is the *first line of every event's* `OnKeypress`:
> TAB advances `party.activeCharacter`, wrapping at the end, and returns before the menu ever sees
> the key — so TAB can never also move a selection. The event then reads whoever is active through
> `GetActiveChar`. "Who tries" and "who pays" are answered by the same roster the player has been
> looking at all along, which is why both events wire in a few lines rather than needing a screen
> built first.

What that exercise established, beyond the code:

> **`ChainOrQuit` is not `QUEST_EVENT_DATA`'s branch rule**, and both of these events use it
> (`RunEvent.cpp:931`). It falls back on the ordinary chain for a chain id of zero **and** for one
> naming a missing event, where a quest pushes a do-nothing event and ends the run. Nothing about
> a toll or an ability check can end a run.

> **`WHO_TRIES` is almost entirely dead by construction.** The storing branch writes a literal
> `FALSE` for the eight thief flags and for `compareToDie`, and `0` for `compareDie` — so the
> target number is always 0, every ability comparison becomes `score < 0`, and `NbrTries` never
> fires a retry. The one reachable failure is the strength percentile against `strBonus`.

> **`WHO_PAYS`'s `moneyType == 0` means Platinum, not "no coin".** The field arrived at 0.912 and
> `Clear`'s default stands below it. `ItemClass 0` is `Item` and `MoneyRules.IndexOf` aborts on it,
> so passing the zero through kills every pre-0.912 toll. Gems are **counted, never valued** —
> two 1gp stones pay a two-gem toll and one 100,000gp stone does not — and removal takes from the
> head of the list, oldest first.

> **`GIVE_DAMAGE_DATA`'s saving throw cannot be modified from either side.** `saveBonus` is passed
> and never read (`Char.cpp:8316`), and `ModifySaveRollAsTarget` is guarded on a non-null attacker
> which the event never supplies — so protection-from-evil and friends give nothing against a trap.
> Save-for-half's `max(1, result)` also means a zero-damage trap hurts those who *save* and spares
> those who fail.

> **`HEAL_PARTY_DATA` runs on 100/1 below 0.882, not on zero** — `Clear` sets `HowMuchHP=100;
> LiteralOrPercent=1;`, the old unconditional full heal. This corrected a wrong premise in the
> reader's own remarks. `HealDrain` is entirely dead, and `HealCurse` is the *item* flag rather
> than a spell effect.

**`JournalEvent` and `SoundEvent` execute but are both half-connected**, and the gaps are named
rather than hidden:

> **The `^` token expander is not ported.** `PreProcessText` (`FormattedText.cpp:823`) expands
> `^D` and `^a`–`^z` in journal text; the port passes identity, so a design using them records the
> raw token. `EventJournal.Apply` takes the expander as a *required* callback so the gap is at the
> call site rather than buried. Note for whoever ports it: it looks `^a`–`^z` up in `global_asl`
> while its own header comment says temp ASL, and `^10`–`^12` parse as two-digit slots where
> `^13`+ does not.

> **Nothing reads the journal back.** The reference renders it through
> `DISPLAY_PARTY_JOURNAL_DATA` (`RunEvent.cpp:27604`); that screen is unported, so entries
> accumulate and nothing shows them. And `HaveGlobalJournalEntryAlready` — which exists precisely
> to answer "already collected?" — is **called from nowhere in the reference**, so a
> non-once-only event appends the same text every pass. Reproduced.

> **The journal lives on `Party`, not `WorldState`.** The reference serializes it *inside*
> `PARTY::Serialize`, above the quest and special-item records — a line `SaveGameReader` already
> draws. Putting it with the other accumulated global state would misfile it in the savegame.

> **`SoundEvent` is computed and discarded**: `IAudioBackend` exists and carries the exact
> `StopQueue`/`QueueSound`/`PlayQueue` triple, but nothing outside the media tests constructs one,
> and there is no sound-file resolver to match `Art()`. Wiring it is a three-case adapter. Two
> things to preserve when someone does: **an empty sound list is a silence command**, not a no-op —
> the stop still runs — and these go to the *foreground* queue, so a design's `.mid` sounds layer
> **over** the level music rather than replacing it.

**`TakePartyItems` and the NPC pair were salvaged from two agent runs cut off mid-write.** Both
drafts were transcription-complete and test-free; their claims were spot-checked against the C++
before the tests were written around them, and both checks held.

> **`moneyType == 0` means the opposite here from what it means on `WHO_PAYS`.** The sibling toll
> gated the field at 0.912 and so restores `PlatinumType` for a stored zero; **this event has no
> gate on `moneyType` at all** — both serializers read it unconditionally and only `WhichVault`,
> two lines later, is gated (`GameEvent.cpp:8368`). So a zero is `itemType`, which the reference
> dies on, and inventing a platinum default would silently rewrite what the design authored.

> **A percentage of money is a percentage of the *base-converted* amount.** `qty` is
> `ConvertToBase(platinum, moneyType)` before any of the four quantity rules see it
> (`Party.cpp:2393`), so "take 50%" authored in gold is multiplied by the exchange rate and empties
> the purse. It behaves as authored only when `moneyType` *is* the base coin. Reproduced.

> **"The whole party" charges each member in full, not a share** — six members and a 100-gold take
> is 600 gold. The reference's own comment at `Party.cpp:2414` says "take equally from all party
> members", describing something it does not do. And **two of the four quantity rules do nothing at
> all for inventory**: random and percent fall into a commented "not used for items".

> **The NPC morale table is indexed on discrete values, not ranges.** 3–8 are penalties, 14–18
> bonuses, 9–13 a deliberate hole — and **19 or more scores zero**, the same as an average
> charisma, so a score raised by a spell effect earns nothing. Both events additionally gate on the
> record's *kind*, so a design character left at the player-character type is invisible to them.

Two gaps in the port itself had to be filled for these: `Character.Morale` (settable, since a
joining NPC is assigned one) and `Party.RemoveAt`, which pulls the active index back when the
member it pointed at leaves — that index is what TAB cycles and what every "who tries" event reads.

**`LOGIC_BLOCK_DATA`'s gate network is ported and deliberately left unwired.** A logic block is a
design's circuit diagram: five inputs (A, B, D, F, G) feed seven gates (C, E, H, I, J, K, L)
through six optional inverters, and the last gate decides the branch and which of two actions run.

> **Truth is emptiness, and this is the load-bearing fact.** There is no boolean anywhere in the
> network — a gate yields `"1"` or `""`, arithmetic gates yield their digits, and the result is
> `w[11] == "" ? 0 : 1`. So an arithmetic gate that computes **`"0"` is true**. A port that
> reached for `bool` would invert every design that does arithmetic.

> **Transcribe the calls, not the diagram.** `ProcessLogicBlock` carries an ASCII schematic in its
> comments that does **not** agree with the calls beneath it: gate C takes (B, A) where gate H
> takes (F, A), and gate L combines the two *inverted* outputs `w15`/`w17` rather than the gates
> themselves. Gate L is also the one gate with no inverter, which is why the serialized negation
> run is six bytes and not seven.

> **The arithmetic gates call GPDL's own `LongAdd`/`LongSubtract`/`LongMultiply`/`LongDivide`**,
> already ported as `GpdlLongArithmetic` — so they are arbitrary precision, not `int`. And
> `LBagreater` is `LongSubtract(side, top)` tested for a leading `-`, so its operands read the
> opposite way from what it computes.

> **An unreachable conditional target stops the run**, and so does a conditional branch whose own
> flag is clear — neither falls back on the ordinary chain, unlike `WHO_TRIES`. Only
> `m_NoChain == 1` uses the event's own chain at all.

**Why it is not wired.** `ProcessLBInput` (220 lines, sixteen input types — ASLs at four scopes,
character info, quest stages, item lists, `$RunTimeIf`, and both GPDL forms) is not ported, so
every input would read empty. A block whose inputs all read false does not fail visibly: it
computes a result and **takes a branch**, sending the design down a route its author did not write.
That is worse than drawing `[LogicBlock here -- not implemented]`, which is what it still does.
Wiring it needs the input layer first; the network beneath is finished and tested.

**`COMBAT_TREASURE` was executing wrongly, and finding out was worth more than the fix.** It reads
into the same `TreasureEvent` record as `GIVE_TREASURE_DATA`, so the runner matched it on type and
opened the pickup screen — in the middle of setting up a fight.

> **The two events behave nothing alike.** `COMBAT_TREASURE::OnInitialEvent`
> (`RunEvent.cpp:9591`) presents *nothing*: it resets the menu and **appends** its items and money
> to `globalData.combatTreasure`, a pile the combat results screen later hands over, adding each
> item's `Experience` to the party's share before clearing it (`:19688`, `:19842`). It appends
> rather than replaces, which is how a design gives a multi-stage encounter one shared reward.

The staging is ported as `Game.StagedCombatTreasure`; **nothing consumes it**, because the results
screen is not ported — a missing reader rather than a defect in what is staged, and said so in the
remarks. The lesson generalised, and **the audit it prompted found a second bug**.

Both other shared-record pairings came back clean: `PickOneCombat` is one of four ordinals
`GameEvent.cpp:4180` marks `used = FALSE` — "these events cannot be created or used" — so it cannot
occur in a design at all; and `TRANSFER_EVENT_DATA`'s runner never branches on whether it is a
stair, a teleporter or a module transfer. But reading that runner to prove it turned up this:

> **`destEP` has three meanings and only one of them is "use destX and destY".** At **zero or
> above** it is an index into the destination level's *entry-point table* and the stored
> coordinates are ignored entirely (`Party.cpp:3495`). At **−3** the three fields are *arguments*
> to `RunGlobalScript("TeleporterDestinations", …)`, which resolves the real destination at
> runtime (`RunEvent.cpp:975`). Only otherwise are they the square to arrive on. This port read
> them literally in all three cases, so a design using entry points teleported the party to
> whatever those fields happened to hold — silently, and to the wrong square.

Entry points are now resolved from `GLOBAL_STATS`'s per-level table, which the port already read
and nothing consulted; the scripted form is **refused with a message**, since the port has no
run-a-global-script-by-name bridge — the same gap `WHO_TRIES`'s `Attempt` hook needs.

**`specialAbilities.txt` now parses, which is the file the port kept wanting.** Every hook this
session has had to decline — `WHO_TRIES`'s `Attempt` veto, scripted teleporter destinations,
combat placement, and two of the logic block's sixteen input types — resolves through
`RunGlobalScript` (`Specab.cpp:2097`), which looks its GPDL source up here by ability name and then
by script name. Shipped designs carry a great deal of it: **1,131 abilities across three files**,
and `SomethingWild` defines `$EVENT_WhoTries_Attempt` outright.

> **The comment marker is `\\`, not `//`.** `IsComment` (`ItemDB.cpp:3116`) tests for two
> backslashes. The file's own header block uses `//` and survives only because it sits before the
> first `\(BEGIN)` and the loader enumerates objects from 1. A `//` line *inside* an object is
> data, not a comment.

> **An object missing its `\(END)` is still loaded.** Every `\(BEGIN)` starts a new object number
> and each object's lines are decoded on their own, so the closer is optional in practice — and
> the editor's own `DefaultDesign` relies on it, shipping **182 openers against 181 closers**.
> Requiring the closer silently loses one of its abilities, which is how this was found.

> **Continuation lines join with CRLF and lose their leading `-`.** What comes out is GPDL source
> the compiler sees with real newlines in it, so joining with spaces would merge a trailing `//`
> comment into the statement after it.

The bracketing of a name gives its kind — `[script]`, `(variable)`, `<table>`, or a bare constant —
and the split is a plain `Find('=')` with no escape handling, unlike the general config splitter.

**`RunGlobalScript` now runs** (`UAFcore.GlobalScripts`), so the bridge is complete: look the
source up by ability and script name, wrap it in `$PUBLIC $FUNC SA(){ … } SA ;`, compile it once,
execute it and take the result from hook parameter 0.

> **There is exactly one built-in default script** — `CombatPlacement`/`PlaceMonsterFar`. Not a
> table that grew; a single entry. `TeleporterDestinations` and the `WHO_TRIES` `Attempt` veto have
> no default at all, so they exist only in designs that author them.

> **The default is consulted per *ability*, not per script.** The reference reaches its defaults
> only in the `pSpecAb == NULL` branch, so a design that defines `CombatPlacement` without
> `PlaceMonsterFar` *loses* the built-in rather than inheriting it.

> **`$SET_HOOK_PARAM` is a swap, not a setter.** It pushes the slot's previous contents back
> (`GPDLexec.cpp:3213`), so a script written as though it returned nothing leaves a value on the
> stack. Its read guards only the upper bound where the write guards both, so a negative index
> reads off the front of the array in the reference; C# returns empty instead.

> **`#` belongs to the numeric comparison operators, not to integer literals.** `>=#` is the
> numeric form of `>=`; `$GET_HOOK_PARAM(#5)` is a syntax error and `$GET_HOOK_PARAM(5)` is
> correct. Reading the one built-in default script — which contains `>=#2` — the other way round
> costs a compile error that names only the `#`.

Compilation is cached, failures included, matching the reference's `SPECAB_SCRIPTERROR` flag: a
broken script pays the error once rather than once per invocation. **The reference caches by
overwriting the source with the bytecode**; this port caches beside it, which keeps the source
readable and behaves identically.

**The first caller is wired: scripted teleporter destinations.** `LoadedDesign.SpecialAbilities`
loads the file, `Game.Scripts` compiles from it, and a transfer whose `destEP` is −3 now asks the
design where it goes.

> **The script *name* carries the arguments.** There is no parameter passing — the name is
> `/level+1/x/y` and a design authors one script per source square, so fifty scripted teleporters
> means fifty named scripts inside one ability and the lookup is an exact string match.

> **The level is one-based in both directions.** The reference formats `destLevel + 1` into the
> name and subtracts one from what it parses back, so a script written against the level number a
> designer sees is correct.

> **The answer must parse completely or nothing happens.** `sscanf(...) == 3` is the test, so a
> two-number answer changes nothing rather than partially applying — though trailing text after
> the third number is ignored, because `sscanf` stops once it has its three.

One deliberate divergence: when no script matches, the reference logs "Cannot find
TeleporterDestination" and then **transfers using the unresolved fields anyway** — to a square the
design never named. This port refuses and says so.

**The second caller is `WHO_TRIES`'s attempt veto**, and unlike the teleporter it is exercised by
real data — `SomethingWild` authors `$EVENT_WhoTries_Attempt`.

> **It can take a success away and never give one.** The whole block sits inside `if (!failed)`,
> so a check the character already failed never reaches a script. A design cannot use it to
> implement an ability the engine does not know about.

> **The scripts are not independent votes.** They share one `HOOK_PARAMETERS` block, constructed
> outside the loop, and slot 0 is read once afterwards — so a later script writing anything other
> than `"N"` **clears an earlier script's veto**. The last writer wins.

> **The hook is named by the *event*, not the design.** The event's own ASL carries an `Attempt`
> entry listing which scripts to run; the ability they live in is always
> `$EVENT_WhoTries_Attempt`. Two such events in one design can therefore run different subsets of
> the same library.

**The third caller is combat placement**, which closes the "global script hooks" gap for
`CombatPlacement`: `$GET_PARTY_FACING` and `$MonsterPlacement` are implemented, a design's own
script runs once per side, and without one the built-in program is used directly.

> **Three hooks, one per encounter distance — and only `PlaceMonsterFar` has a built-in default.**
> `PlaceMonsterClose` and `PlaceMonsterNear` exist solely in the `CombatPlacement` ability shipped
> designs carry, so a design with no `specialAbilities.txt` has *no* script for an up-close
> encounter and the reference places nothing at all. This port falls back on
> `TurtlePlacement.Default` for all three — the same programs the shipped ability produces —
> because an empty battlefield is a worse answer than the right one arrived at differently. That
> is a deliberate divergence, not an oversight.

> **The script runs once per side, not once per fight.** The reference resets the turtle and calls
> the hook inside its direction loop, so a design's script sees a freshly-reset arrangement each
> time and `$MonsterPlacement` plants monsters as it goes.

> **`$MonsterPlacement` outside a placement answers `"0"`** rather than refusing — the reference
> guards on `monsterArrangement.active` and logs. A script calling it at the wrong time is a
> design error, not a port gap.

Reading the `WHO_TRIES` entry needed the **self-delimiting list convention** (`SUBSTRINGS`, `ASL.cpp:242`),
now `UAF.Common.Substrings`:

> **The first character of the value is the delimiter** — there is no fixed separator and no
> escaping. That is what lets one list nest inside another, the outer picking a character the
> inner does not use, and it is why these look like paths without being paths. `HeadAndTail`
> strips the head's delimiter and **leaves the tail's**, so a tail can be split again with no
> bookkeeping — and a caller wanting the tail's text has to drop the character by hand, which is
> what the reference's `Right(len - 1)` is doing.

A chain-depth cap was added with them. **It is not a rule from the reference**, which has no limit
and simply hangs if a design chains an event to itself; chains of chains are ordinary, so a cycle
is an easy mistake to make and a hang tells the author nothing.

##### `ability.dat` — the seventh database, and the ability roll

> **A database was named and never opened.** `TaggedDatabaseReader.FileName` has listed
> `ability.dat` since the tagged framing was ported, and `TaggedDatabaseCorpusTests` even asserts
> its container tag and record count — but nothing read a record out of it. The framing was
> tested, the contents were not, and the gap stayed invisible until the character generator needed
> the dice a strength score is rolled from. **All seven databases now read.**

`ABILITY_DATA` is small — one tag, a name, an abbreviation, a `DICEPLUS` and a specab block — and
the reader was right first time. **The test was wrong**: it fed the reader a made-up
`DesignVersion` instead of the design's own, and the stream desynced on the second record. A
tagged header carries a record tag and a count and *no version*, so the version has to come off
`game.dat` beside the database — and it decides both the specab gate and whether an editor stream
carries the pre-`VersionSpellNames` numeric key.

> **Removing the specab read made it worse, which is what identified the cause.** Without it the
> second record's *tag* came back as a space rather than `Abd0`; with it, the tag was fine and the
> name desynced. Two different wrong answers from one wrong version, and neither was a defect in
> the transcription.

The roll itself is `rollSkillDie`:

> **Best of three, always.** Every score is rolled three times and the largest kept — which is why
> new characters come out so uniformly good, and why lowering an ability's dice moves the average
> far less than it looks like it should.

> **Two eras, both live.** Below 0.870 the dice are a hard-coded 3d6 and the ability database is
> not consulted at all; from 0.870 each ability carries its own `DICEPLUS`. A design old enough
> gets 3d6 whatever its `ability.dat` says.

> **An attempt that does not roll counts as zero, not as a skip.** `RollAbility` answering false
> leaves that try at 0 and it still competes for the maximum — so an ability whose dice never roll
> produces a score of 0 rather than a refusal.

> **Exceptional strength needs *exactly* 18.** The test is equality, so racial or magical strength
> above 18 skips the percentile rather than maximising it. And the dice come from the **class**,
> not the rules: the commented-out predecessor restricted the bonus to fighters, rangers and
> paladins, while the live code rolls whatever `strengthBonusDice` the class carries — so the
> restriction is data now, and a class with no dice gets nothing.

> **A class minimum only ever raises a score.** A character who rolls below what their class
> demands is given the minimum rather than re-rolled or refused, which makes picking a demanding
> class a way to guarantee good scores.

##### What a new character starts with

The deterministic half of `generateNewCharacter`: money, equipment, baseclass rows and birthday.

> **`StartPlatinum` is not platinum.** The amount goes in at `money.GetDefaultType()` — the
> design's own base denomination — so a design whose currency is copper starts its characters with
> that many copper pieces. The field name is a leftover from when the denominations were fixed,
> and it is in the savegame under that name too.

> **Starting equipment is copied off the class wholesale.** No per-baseclass contribution and no
> merging: a fighter/mage gets its class record's list and nothing from either baseclass beneath
> it.

> **Every baseclass row starts at level 1 with no experience.** A character who begins above first
> level does so by being awarded the design's starting experience and then *levelled*, not by
> being created at that level — which is why `getNewCharLevel` runs immediately afterwards.

> **The two age clamps never look at each other.** The race's roll is floored at the design's
> `START_AGE` — but only when that is positive — and then capped at the character's maximum age,
> so a race whose maximum is below the design's minimum produces a character born at its own
> limit.

> **One of `generateNewCharacter`'s two paths is dead code that would crash.** It handles
> `START_EXP_VALUE` and then calls `die("Not Needed?")` for the "start experience is a minimum
> level" case, so a design configured that way takes down the reference. Only the live path is
> ported.

##### Rolling a character against a real design — and a measurement that was wrong

The generator's rules finally meet a design's own tables. Two things showed up the moment they
did, and neither was visible in any unit test, because both are properties of real data.

> ~~**A `DICEPLUS`'s text carries its own bounds** as `min|<expression>|max`.~~ **Half right, and
> the wrong half mattered.** The bars are real, but they are the operators `|<` and `>|`, and what
> follows the closing one is an expression rather than an integer — see §the re-measurement. The
> reading here happened to give the same answers for these four designs and would not have for a
> race-dependent ceiling.

> **Ability dice reference races, and the names are quoted when they need to be.**
> `2d6+6+(Race_Halfling*-1)+("Race_Half-Orc"*1)` — a `Race_<name>` symbol is 1 for that race and 0
> otherwise, exactly like `Male`, and a name containing a hyphen is quoted because the tokeniser
> would otherwise stop at it.

> **My earlier measurement was partial and I stated its conclusion as fact.** The probe covered
> races and classes and reported "the only identifier in the entire corpus is `Male`" — from 288
> expressions, which made it look settled. Abilities were never sampled. **A confident number from
> an incomplete sample is worse than no number**, and the fix is not a bigger probe but naming
> which records were read: the fields carrying these expressions have not all been enumerated, so
> the evaluator now makes no coverage claim at all.

> **`SomethingWild`'s own data is corrupt here, and now I know why.** The race prefix repeats up
> to forty-one times — `Race_Race_Race_…_Halfling`. The *editor* rewrites every expression on
> load through `EncodeOldDicePlusText` (`class.cpp:2309`, called at `:2591`, `#ifdef UAFEDITOR`),
> prefixing bare race names with `Race_`. Its identifier scanner is `ISALPHANUM`
> (`class.cpp:2307`) — letters and digits, **not** the underscore — so on the next pass
> `Race_Dwarf` scans as `Race`, `_`, `Dwarf`, and `Dwarf` gets prefixed again. **One `Race_` per
> editor session.** `"Race_Half-Orc"` is untouched in the same files because `Half-Orc` is not one
> of the six names the encoder knows.
>
> The engine never re-encodes, and `LookupRefKey` (`class.cpp:900`) checks only that a name begins
> `Race_` — it never consults the race database — so the accumulated name is a well-formed race
> test that matches nobody and scores 0. **These designs' racial ability adjustments stopped
> applying long ago, in the reference too.** That the port must resolve such a name to 0 rather
> than refuse it is load-bearing: refusing would make every ability in the design roll nothing.

One bug I introduced and caught: allowing `-` inside a bare identifier so `Race_Half-Orc` would
parse unquoted. It would have read `Male-1` as a single identifier. The quoted branch is the
correct home for that, and bare identifiers are letters, digits and underscore again.

##### CREATE CHARACTER, all ten steps

The wizard runs from race to a written `.chr`. **Eight of the party menu's twelve entries do
something at this point** — see §MODIFY for the two that followed.

> **`CanBeSaved` and `IsPreGenerated` are what make a character the player's.** A pre-generated
> NPC appears on the roster and refuses to serialize — `serializeCharacter` returns FALSE when
> `CanBeSaved` is clear — so a generated character has to declare itself the opposite of one.

> **Creating and joining are separate acts.** CREATE writes the file and nothing else; ADD is what
> seats a character. That is why a design's roster shows characters the party has never met, and
> why making one does not put it in the party.

> **The icon and the portrait are file names, not art.** The record holds a `PIC_DATA` whose only
> field the generator fills is its filename; the surfaces are loaded on demand by whoever draws it.

> **The save prompt opens on NO**, like every other irreversible prompt in this port.

`NewCharacter.Blank` is **the only place the port constructs a `CharacterRecord` positionally** —
sixty-odd fields in an order that means nothing to a reader, so it happens once and everything
else uses `with`, the same shape `SaveGameProjection` uses to write a live character back over the
record it came from.

**Still stubbed at the seam:** the assembled record takes zeroed ability scores, one hit point and
no age. Every one of those rules is ported and tested — `AbilityRoll`, `NewCharacterHitPoints`,
`NewCharacter.Roll` — and nothing yet calls them at the point of assembly, because the roller
needs the ability database threaded to the call site and the ages need the race record. That is
plumbing, and it is the next thing.

##### MODIFY, which is not what the plan said it was

This plan said, for several rounds, that MODIFY was **"the same wizard re-entered over an existing
character"**. It is not. `MAIN_MENU_DATA::OnKeypress` case 3 (`RunEvent.cpp:2038`) is three lines:
take the active character, and push `CHOOSESTATS_MENU_DATA(false)`. **MODIFY is the stats screen
and nothing else** — no questions, no re-picking a race.

> **The flag it is pushed with buys nothing.** `CHOOSESTATS_MENU_DATA(false)` means "use the
> existing character", and its handler is
> `if (m_CreateNewChar) { generateNewCharacter(...) } else { generateNewCharacter(...) }`
> (`RunEvent.cpp:4064`). **Both branches are the same call**; only the string in the debug log
> differs. So MODIFY's REROLL regenerates a party member from scratch — keeping its race, gender
> and class, discarding everything else — exactly as creation does.

Which means the screen the generator was skipping and the screen MODIFY needs are one screen, and
porting it finished both. `UAFcore/StatsScreen.cs`, `UAF.Rules/AbilityLimits.cs`,
`UAF.Rules/StatAdjustment.cs`. **Ten of the party menu's twelve entries do something at this
point** — CHANGE CLASS is the twelfth, in the section below.

> **`AllowModifyStats` is a global initialised to `true` and assigned nowhere else**
> (`Globals.cpp:588`). It guards the TAB and up/down handling, so in a shipped build the guard is
> permanently open and the only thing it changes is the screen's title.

**It is point-buy starting from nothing.** `availPoints` opens at zero on every visit and no code
path adds to it except a decrease, so a player can reshape a character but never improve one.

> **The two directions are not symmetric, and the reason is a limit mismatch.** The increase
> charges `*avail += orig - final` — what it actually achieved — while the decrease credits
> exactly one point, its `if (orig != final)` guard commented out. That looks arbitrary until you
> notice the guard and the clamp read different tables: `STF_IncrStat` refuses at the **class**
> maximum, then `UpdateStats` clamps against the **race**'s as well, and the race check runs
> first. Where a race is stricter, the press is allowed, the score comes straight back, and
> nothing is charged. Where a race's *minimum* is above the class's, the decrease credits a point
> for a score that did not move — which a player can farm.

> **The class's limits are the tightest of its baseclasses** — greatest minimum, least maximum,
> ties broken by the larger modifier at *both* ends (`CLASS_DATA::GetAbilityLimits`,
> `class.cpp:7698`).

> **A class with no baseclasses caps every score at 15.** The running maximum starts at 9999 as a
> sentinel and, with nothing to lower it, goes straight into `ASSEMBLEABILITYLIMITS`, which masks
> each field to a byte: `9999 & 0xff` is 15 — below what a 3d6 roll produces. An *unknown* class
> is worse: `GetAbilityLimits` returns the literal `1`, which unpacks to a maximum of zero, so no
> score can rise at all.

> **Every adjustment recomputes the hit points and sets the current total to the new maximum**
> (`CharStatsForm.cpp:2012`). A wounded party member that MODIFY touches ends the screen at full
> health.

> **The exceptional-strength percentile is rolled once per visit and cached.** Walking strength
> down off 18 and back up returns the same percentile rather than a fresh chance at a better one —
> the cache is a static cleared by `CHOOSESTATS_initial`. A roll of zero or less is stored as
> zero, and since the cache is *tested* by being zero, a class with no strength dice re-rolls
> nothing on every press.

> **`KC_PLUS` and `KC_MINUS` are `VK_ADD` and `VK_SUBTRACT`** (`Getinput.cpp:566`) — the numeric
> keypad, not the OEM keys on the number row, which the mapper passes through as `KC_NUM`. Reading
> the constant names rather than the mapping puts the shortcut on a key that does nothing.

> **This is one of the few screens whose `OnKeypress` does not open with `TABParty(key)`**
> (`RunEvent.cpp:4049`). TAB moves the ability highlight here and never reaches the party, which
> is the opposite of every other screen in the port.

Two things the end-to-end test found that the unit tests could not:

> **The party menu's horizontal handler was stealing the stats screen's keys.** `PartyMenuOpen`
> stays set while the menu's pushed screens are up, and the guard excluded the roster, the slots
> and the confirmations — and the generator, via `Creating is null`. MODIFY arrives with no
> generator behind it, so <kbd>Right</kbd> tabbed the party instead of moving to ACCEPT. Every
> screen the party menu pushes has to be named in that guard.

> **A live character had nowhere to keep an ability score.** `Character` mirrored the record's hit
> points but not its abilities, and `SaveGameProjection`'s comment said in as many words that the
> scores "survive because nothing touched them". Once MODIFY can touch them that stops being true,
> and a save would have silently discarded the change.

**One divergence, stated:** during creation a stat change re-rolls the hit dice, because the
hit-point seed is not yet stored on a character that has not been assembled. The reference re-runs
`DetermineNewCharMaxHitPoints(hitpointSeed)` against the character's own seed, so its hit points
are a function of the ability scores alone. The scores are right; the total moves more than it
should.

##### CHANGE CLASS, which the engine deliberately refuses to decide

`CreateChangeClassList` (`Char.cpp:7646`) filters every class in the design through
`CanChangeToClass` — and **every rule that function once had is commented out**, with the reason
written above it. The designer asked for "the engine to have no part in this decision"; the
alignment check and the minimum-15-in-your-prime-ability checks are all `/* … */`.
`UAFcore/ClassChange.cs`.

> **Two script hooks decide, and they are asked of different classes.** `CanChangeFromClass` runs
> on the class being left with the target's id in `hookParameters[5]`; `CanChangeToClass` runs on
> the class being joined with the origin's. Both must answer `Y`.

> **Silence is a refusal, and that is the shipped behaviour.** `hookParameters[0]` starts empty and
> is emptied again between the two calls, so a class with no such script fails `!= 'Y'`. **A design
> that has not written these hooks has a permanently dark CHANGE CLASS entry — in the reference as
> much as here.** `ClassChange.NoScripts` is therefore not a placeholder standing in for a rule; it
> *is* the rule until the scripting phase lands, and the seam for when it does.

Everything around the hook is deterministic and ported: not a monster, the race's
`m_canChangeClass` flag set, a race the design actually has (a missing one refuses rather than
permits), not already dual-classed, and never the character's own class.

> **`IsDualClass` is a sweep for a non-zero `previousLevel`** (`Char.cpp:7454`) — which is exactly
> the field changing class sets, so the change is one-way by construction rather than by a flag.

`HumanChangeClass` (`Char.cpp:7770`) is the mutation:

- **Every existing baseclass drops to level 0 and keeps its old level as its previous one.**
  Nothing is removed — the old row is what makes the character dual-classed.
- **Experience is not reset.** Only rows added by the change start at zero.
- **Every carried item is unreadied**, whether or not the new class could use it.
- Two more hooks, `CHANGE_CLASS_FROM` and `CHANGE_CLASS_TO`, bracket it.

> **The duplicate-baseclass check reads the wrong index.** The inner search does
> `PeekBaseclassStats(i)` where `j` is plainly meant, so it compares one arbitrary row instead of
> searching them all — and indexes past the end as soon as the new class has more baseclasses than
> the character has rows. There is no defined behaviour to transcribe, so the port searches
> properly and says so.

##### A silent bug the type constants were hiding

Porting the monster check turned up that `Game` had `NpcType = 1`, documented as `NPC_TYPE`.
**`CHAR_TYPE` is 1; `NPC_TYPE` is 2** (`Externs.h:965`). DELETE built its filename from that
constant, so it looked for every player character at `DCNPC_<name>.chr` and every NPC at
`<name>.chr` — **both misses silent**, because the delete failed, was caught, and reported a file
that had never been there.

> **And the comparison was raw where it had to be masked.** `type` holds a kind in its low bits and
> an in-party flag in its top one, and `CHARACTER::GetType` masks the flag off before comparing
> (`Char.h:985`). `EventNpc.KindOf` already did this correctly elsewhere in the port; this call
> site did not use it. So even with the right constant, every record saved while its subject was in
> the party — which is every record DELETE sees — would have missed.

Two constants, one line, no test. `CharacterFileNameTests` now covers it.

##### The art and spell screens on screen — and three bugs the end-to-end test found

The generator now runs as a wizard from race to the save prompt. Wiring the last four steps into
the runner turned up three defects that no unit test could have seen, because each needed the
*sequence*.

> **The art pickers were rules with no screen.** `ArtPicker` and the wizard's `Pick` transitions
> were built and tested a few rounds back, and the runner never showed them — so the wizard
> closed at the icon step. The earlier note here said eight of ten steps ran; it was the rules for
> eight, and the screens for six. Corrected.

> **`Typing` survived the name step and swallowed every movement key.** `HandleTyping` takes any
> key that is not Return, so once the name screen set it, the art and spell screens after it could
> not move their menus. On the art screen this was invisible — the cursor already sits on the only
> entry that does anything — and total on the spell screen, which pages. The fix is one line, and
> the reason it hid is that **a screen that swallows input looks identical to a screen whose
> input does nothing.**

> **The art screen left the cursor on a darkened entry.** `NEXT` is the first entry and the cursor
> starts there; with one picture both paging entries darken, leaving the cursor on an entry that
> does nothing. It now homes onto `SELECT`.

The screens themselves:

> **Neither spell screen has an EXIT.** Both menus are three entries — `SELECT/NEXT/PREV` and
> `LEARN/NEXT/PREV` — so there is no way out but to keep picking until the acquisition rules say
> every level is finished. They differ in the verb and the message and nothing else, which is why
> one screen serves both.

> **A failed attempt still consumes the spell.** The reference sets
> `m_spellAvailabilityList[i].learned = success` either way, so there is no second go at a spell
> that was missed — which is what makes `Num`, "how many he must try", a bound at all.

##### A character's casting ability — and two fields that were named backwards

`UpdateSpellAbilityForBaseclass` folds each baseclass's casting tables into a per-school ability.
It is what `CanKnowSpell` consults, so it decides what the spell screens can offer at all.

> **Two `CASTING_INFO` fields were called `Bonus` and `Penalty` and are neither.** They are
> `m_maxSpellLevelsByPrime` and `m_maxSpellsByPrime` — the highest spell level castable, and how
> many spells may be known, each indexed by the prime ability's score. Nothing had used them yet,
> so nothing was wrong; the names would have made the first use wrong. Renamed, along with
> `Name` → `SchoolId` and `AbilityId` → `PrimeAbility`.

> **The maximum spell level is *assigned*, not maximised — and the spell *count* beside it is
> maximised.** The line above is the commented-out `if (maxSpellLevel > …)`, replaced in 2017 by a
> bare assignment from the by-prime table. So a character whose two baseclasses cast from the same
> school gets **the later baseclass's level ceiling and the better of the two spell counts**, from
> adjacent lines. The order that decides it is the order of `BASECLASS_STATS`.

> **The old code's `maxSpellLevel` is still computed and goes nowhere.** The scan for the highest
> non-zero entry in the level's spell-limit row survived the replacement; its result is never read.

> **The base count updates on `>=`, not `>`.** A baseclass matching the count already recorded
> still takes the slot, handing the "contributing level" to whichever is folded in later — and
> that level is what tells a level-up how many new spells to grant.

> **Bonus spells are triples of (threshold, bonus, level) and accumulate.** Two triples naming the
> same spell level stack; one naming a level above the school's maximum is skipped rather than
> clamped.

> **A school the character has no ability in is a refusal, not a zero.** `CanKnowSpell`'s lookup
> returning -1 answers FALSE before the level is compared.

##### Which spells a character is offered

`CreateSpellAvailabilityList` — four filters in order, feeding the acquisition rules below.

> **A spell must name a baseclass the character's class actually has.** The check walks the
> spell's `allowedBaseclasses` looking for one the class contains, and `continue`s if it finds
> none — so a spell allowing an empty list is offered to nobody, however scribable it is.

> **The `KNOWABLE_SPELLS` hook runs on the class first and the spell second, and an empty reply
> from both means 100.** So the absence of scripting is *more* generous than a scripted design and
> never less — every offered spell is certain. Same shape as `IS_BASECLASS_ALLOWED`: the scripting
> phase will add refusals this port currently grants.

> **A probability of zero removes the spell rather than offering it as impossible.**
> `if (probability != 0)` guards the add, so a hook can hide a spell as well as make it unlikely.

> **The reference reads an unparsed reply as an indeterminate number.** `probability` is an
> uninitialised local and the reply goes through `sscanf(result, "%d", …)`, which leaves it
> untouched when the text is not a number — a hook answering "yes" gives whatever was on the
> stack. The port treats an unparsable answer as certainty, the only defined behaviour available.

> **The maximum spell level is taken from what survived the filters**, not from what the database
> holds — a level 9 spell the character cannot know does not raise the ceiling the acquisition
> loop sweeps.

> **`CanKnowSpell` is injected, not reached for.** It asks the character's `spellAbility` for a
> per-school maximum level, which `UpdateSpellAbility` derives — a chain this port has not built.
> A caller supplies the answer, as `Training` takes two functions rather than a whole design, so
> the rest of the rule can be exercised now. **`UpdateSpellAbility` is what stands between here
> and a finished CREATE.**

##### Learning spells at creation — a two-pass round robin

The rules behind `INITIAL_MU_SPELLS_MENU_DATA`: how many spells a new character may take, which
are free, and when the screen is finished. `AreWeDone` states its own rule in a comment, and the
comment is accurate — which is rare enough here to be worth saying.

> **Every new character sees this screen, whatever their class.** The reference computes
> `PickMUSpells` from the class, comments the line out, and assigns `knowSpellsAtCreation = TRUE`
> unconditionally — so the `else` branch that skips to the save prompt is dead code and a fighter
> is offered a spell list.

> **`MNMC` is four counts and only two are limits.** `Certain` is a free allowance and `Num` is an
> obligation to *try* — a character who fails every roll still leaves the level, because `Num`
> counts attempts and not successes. `Min` and `Max` are the actual bounds.

> **The free allowance counts successes, not attempts.** The test is `numAcquired < certain`, so a
> failed roll leaves the allowance intact.

> **Index 0 of the state array is not a spell level — it is the totals.** The loop starts at 1 and
> `m_acquireStates[0].mnmc` carries the global floor and ceiling across every level at once. A
> reader treating the array as levels 0..n counts the totals as a level and finishes early.

> **A level with nothing on offer is out of the reckoning** — neither short of its minimum nor of
> its maximum, so it never holds the loop open.

The two passes have different goals, and the second one behaves in a way I got wrong first time:

> **Pass 0 fills every level to its `Max`; passes 1 onwards bring short levels up to `Min`.** So
> `Min` is not a floor checked at the end — it is the target of a second sweep.

> **A later pass leaves the level showing *even when it is the short one*.** In the
> `oneLevelNotMin` branch nothing ever clears `FinishedThisLevel`, so a top-up pass does not sit
> on a level until it is satisfied: it moves on immediately and comes back around. **The sweep is
> a round robin**, which is the only reason "pass 1 → n" is plural. I wrote it as stay-until-
> satisfied, and the test I had written from the reference's structure caught it — then the
> *corrected* code failed a second test I had written from my wrong model, which is how the round
> robin surfaced at all.

> **Exactly one branch holds a level open in a later pass**: every level at its minimum, the
> global floor still unmet, and this level with room.

> **`AllLevels` always implies `ThisLevel`.** The reference sets it as a postcondition, so the two
> flags can never disagree and a caller checking only `ThisLevel` still advances off the last one.

##### The art pickers, and a search that does not search

The generator's last two screens. **One screen, two directories** — both scan a fixed naming
series, show one picture at a time, and offer `NEXT`, `PREV`, `SELECT`; only the folder, the
pattern and the destination field differ.

> **`FindImageWithValidExt` does not look for valid extensions — not in the engine.** It reads as
> though it tries every image format for a root name, and under `UAFEngine` the function is
> `if (FileExists(fullName)) return TRUE; return FALSE;` — the search is behind `#ifndef
> UAFEngine` (`Globals.cpp:3714`). So at play time the series is literal `.png`, and **a design
> that shipped its portraits as `.pcx` shows the designer everything and the player nothing.**

> **The series is scanned by name, not enumerated.** The engine asks for `prt_SPic1` through
> `prt_SPic50` one at a time, so a portrait called anything else is invisible however well formed
> it is — and a gap in the numbering is skipped rather than ending the scan.

> **Both directions wrap.** `NEXT` off the end returns to the first and `PREV` off the front goes
> to the last — the opposite of the roster and the inventory, which stop. It is a carousel because
> there is only ever one picture on screen.

> **One picture darkens both paging entries, and SELECT never darkens.** The test is
> `numSmallPics <= 1`, so a design with no portraits at all still asks the player to press SELECT
> over an empty screen, and the character is made without one.

##### A new character's hit points, and two ways they differ from a trained one's

`DetermineNewCharMaxHitPoints` is **not** the formula `DetermineCharMaxHitPoints` uses when a
character levels up. The training path rolls only the levels just gained and *adds* to the
existing maximum; this one rolls every level from 1 and replaces it. Same character, two
functions, and they disagree twice.

> **The per-level constant is only added when the baseclass rolls no dice.** The line is
> `HP += (numDice>0) ? ran.Roll(sides, numDice, bonus) : 0 + constant;` — and `?:` binds looser
> than `+`, so it parses as `… : (0 + constant)`. The training path writes
> `RollDice(…) + constant` and gets it right. A baseclass with dice therefore **loses its constant
> at creation and gains it on every level-up afterwards**. Transcribed as written: a design's hit
> points are balanced against what the engine does, not against what it meant.

> **The comment says "take the average" and the code takes the sum.** Twenty lines are given over
> to explaining that a multi-class character's baseclasses should be averaged, `numBaseclass` is
> counted for it, and `maxHP` and `specificHP` are computed alongside — then the result is
> `max(1, totalHP)` and none of the three is ever read. A fighter/mage gets both baseclasses' hit
> points in full.

> **There is a private random generator, and it is there on purpose.** `LITTLE_RAN` is seeded from
> the character's `hitpointSeed` before every attempt, so re-rolling ability scores changes only
> the bonus and never the dice. The comment above it explains why: the computation is "complex and
> non-linear because of multiple baseclasses", so replaying it deterministically is the only way
> to isolate what an ability change is worth.

> **Its `z` half is initialised, guarded against zero, and never read.** The two-word generator the
> commented-out line above describes was replaced by a one-word one and the setup stayed.

##### What a dice expression actually contains

**`DICEPLUS::Roll` is a client of the GPDL toolchain, not a small VM of its own.** `RDRCOMP` and
`RDREXEC` live in `GPDLcomp.h` and `GPDLexec.h` — ~~**13,146 lines** of compiler and interpreter
between them, so this belongs in **priority 3**, not in character creation.~~ **That line count is
the files', not the feature's.** `RDRCOMP` and `RDREXEC` are about 250 lines between them and are
self-contained; the other thirteen thousand are `GPDLCOMP`, the *script* compiler, which a dice
field never touches. Reading them first would have been cheaper than the two measurements it took
to get here. See §the re-measurement.

There is no shortcut through `decodeNdM`, either: it decodes a single token, and the compiler has
already split `3d6+2` into `3d6`, `+`, `2`, so even that needs the expression layer.

So before building anything, I counted what designs actually write. **288 dice expressions across
the corpus's races and classes, 74 distinct:**

| shape | share | what it needs |
| --- | --- | --- |
| empty | 19.1% | nothing — zero |
| a constant | 17.7% | nothing |
| a bare `NdM` | 13.2% | a dice roll |
| arithmetic over those | 25.0% | `+ − * /`, parentheses |
| contains an identifier | 25.0% | a name lookup |

> ~~**Every identifier in the entire corpus is `Male`.**~~ **That was wrong, and the way it was
> wrong is worth keeping.** The probe behind it read races and classes — the records I happened to
> be working on — and not abilities or spells. It had sampled **288 of 8,880 expressions**, 3% of
> the corpus, and reported a settled-looking conclusion. Every ability in `SomethingWild`
> references races; every spell field that scales writes `level`. A partial sample gave a
> confident, false, and *measured-looking* answer, and the number 288 is what made it feel
> settled. The complete count and the vocabulary it actually shows are in §the re-measurement.

`DiceFormula` was that evaluator: a hand-rolled recursive-descent parser over the subset I had
measured. **It has since been replaced by a transcription, and the section below records what the
re-measurement found.**

##### The re-measurement, and what it cost to guess

Enumerating every field that carries a `DICEPLUS` — an ability's roll, a class's strength bonus, a
race's weight, height, age, maximum age and movement, and a spell's parameters, effect duration
and effect change data — gives **8,880 expressions across all four designs**, not 288. The first
sample had missed abilities and all of spells; it had been 3% of the corpus.

| field | expressions | empty | distinct |
| --- | --- | --- | --- |
| `spell.Parameter` | 6,786 | 4,030 | 69 |
| `spell.Duration` | 1,131 | 353 | 4 |
| `spell.Effect` | 651 | 0 | 60 |
| `class.StrengthBonus` | 78 | 40 | 2 |
| `race.*` (five fields) | 210 | 15 | 78 |
| `ability.Roll` | 24 | 0 | 20 |

**The complete identifier vocabulary is three things:** `Male`, `Race_<name>`, and `level` in
either case. Spells are where `level` lives, which is why sampling races and classes could not
have found it.

**And the grammar was wrong in a way no symbol count would have caught.** `|<` and `>|` are
*operators* in `CoperDef` (`GPDLcomp.cpp:4100`), at the table's lowest priority — not a
`min|<expr>|max` bracket syntax. `3|<3d6>|18` is `3 |< 3d6 >| 18`: floor, then ceiling, each
taking everything to its right. The corpus writes `3|<3d6>|19+(Race_Elf*1)`, where **the ceiling is
itself an expression** — a racial ability maximum. Reading the bars as delimiters parses the `19`
and silently drops the rest of the cap.

So rather than guess again, `DiceFormula` is now a transcription of `RDRCOMP::CompileExpression`
(`GPDLcomp.cpp:4517`) and `RDREXEC::InterpretExpression` (`GPDLexec.cpp:8249`): the tokeniser, the
operator table with its priorities, the shunting-yard loop, and a postfix interpreter over an
integer stack. **Those two functions are ~250 lines, not 13,146** — the rest of `GPDL*` is the
*script* compiler, `GPDLCOMP`, which a dice field never touches. The earlier line count was the
file's, not the feature's.

> **The arithmetic is integer.** `InterpretExpression`'s stack is `int stk[40]`; `DICEPLUS::Roll`
> widens to a double only at the end. `level/2` at level three is 1.

> **`|<` keeps the larger operand and `>|` the smaller**, and equal priorities associate left
> (the drain condition is `>=`), so `3|<3d6>|18` clamps low first and high second.

> **An unrecognised character ends the expression silently.** The loop does
> `if (tokenType == CTKN_NONE) break;` with no error — so `1.5*level` compiles to just `1`. The
> same character where a *term* was expected is an error, so `.5*level` fails to compile and the
> roll returns nothing. **Both forms are in the shipped corpus and they do not mean the same
> thing.**

> **Division and remainder by zero give zero**, tested by the interpreter rather than performed.
> My hand-rolled evaluator refused instead — a defensible-looking choice that was simply not what
> the reference does.

> **`1d0` rolls nothing.** `RollDice` returns the bonus when either the sides or the count is not
> positive. `SomethingWild`'s spell effects contain one.

> **An empty expression "did not roll"; it did not roll zero.** `Compile` fails on an empty string
> and `Roll` returns FALSE with its result left at 0 — the same answer `AbilityRoll` treats as a
> zero-scoring attempt. Just under half the corpus's dice fields are empty.

The measurement itself is now a **test**, not a probe: `DiceCorpusTests` walks all ten fields in
every design, asserts every expression either evaluates or is empty, asserts that the only refusals
are the ones the reference also refuses, and — the guard against repeating the original mistake —
**asserts that all ten field kinds are present in the sample**, so a database that silently fails
to open can no longer look like a field with no expressions.

##### Typed text — and `EnterPassword` running

Three things were waiting on text entry. Two now have it: `ENTER_PASSWORD` runs, and the
generator takes a name.

> **Two screens share one behaviour and disagree about every rule.** Both accumulate characters,
> both delete on **Backspace or Left**, both commit on Return — and they differ on punctuation,
> leading spaces, refused characters and length. The behaviour is shared and the disagreements are
> a `TextEntryRules`, rather than two nearly-identical screens that drift apart.

> **The name screen refuses `?` and `/` with no comment.** A character is saved as
> `<name>.chr`, so both would make a file name that cannot be written or that matches the wrong
> things — the rule is filename safety and nothing in the reference says so.

> **The two screens refuse a slash for different reasons**, which is the only way to tell them
> apart: the name screen takes punctuation and then singles out those two by hand; the password
> screen takes no punctuation at all, so a slash never reaches a refusal list. A hyphen
> distinguishes them.

> **Left deletes; it does not move a cursor.** There is no cursor — the reference draws the text
> as menu items and has nowhere to put one — so a player expecting to move back and insert erases
> instead.

> **A password screen has no menu, so there is no way out but to answer.** Every other event
> answers with one; this takes raw characters until Return, and Escape does nothing.

> **An empty answer passes a `TypedInPswd` check.** `strstr(password, "")` returns the password
> rather than null, so pressing Return without typing anything succeeds — for the very mode a
> designer would reach for to accept a partial answer. The other two reject it.

> **An unknown match mode is treated as exact.** The reference puts the exact case under
> `default` rather than its own label, so a corrupt value falls into the strictest behaviour
> rather than the loosest.

> **`MtchCri` and `MtchCse` are ASL attributes, not fields** — written by `PreSerialize` and
> deleted again after. A reader that took only the record's members would lose the match mode and
> the case rule entirely. The port already had them, in the event's attribute list, for the same
> reason the race's `AllowedClass` was already there.

> **Only the try that exhausts `nbrTries` takes the failure chain**, so the screen is a retry loop
> with a counter, not a single question. And a design asking for zero tries still gets one — the
> count is compared *after* the first answer.

**The generator now runs to the name step**, and skipping the one between is not the hole it
looks like:

> **`CHOOSESTATS_MENU_DATA` is a re-roll screen, not the thing that makes the stats.** The
> character was already generated at the alignment step; that screen's item 2 is literally "don't
> re-roll". Skipping it means the player keeps the first roll — a real divergence, and a small
> one, where stopping at it would strand the wizard one step short of the name.

##### The character generator's spine, and four of its ten steps

CREATE CHARACTER runs as far as alignment. **The first wizard in the port** — every screen before
this answered a question and finished; this pushes ten in sequence, each writing into one shared
character.

> **The step order is a dependency chain, not a preference.** Race narrows the classes on offer,
> and so does gender — `ALLOWED_CLASSES::Restrictions` takes both — so class cannot come first.
> Nothing here is reorderable, which is why the steps are an enum driven from one place rather
> than a chain of calls.

> **There is no going back.** Every picker's EXIT sets `m_AbortCharCreation` and unwinds the whole
> thing; none of them steps back one. A player who picks the wrong race starts again.

> **All four pickers are one screen with four sources**: a horizontal `SELECT / NEXT / PREV / EXIT`
> menu over a vertical list, `HMenuVItemsKeyboardAction` — the inventory's key split, third
> appearance.

> **The character is rolled at *alignment*, not at the stats step.** `ALIGNMENT_MENU_DATA` calls
> `generateNewCharacter` the moment the alignment is chosen, and **aborts the whole creation if
> the result has zero hit points**. The step named for stats comes after the character already
> exists.

The class filter is the substance, and it is two gates in a specific order:

> **The first gate is usually wide open.** `RACE_DATA::IsAllowedClass` returns true outright when
> the race carries no `AllowedClass` attribute — or one that is not a legal delimited string — and
> that is the *first* test. Most designs write no such attribute, so for most races the second
> gate never runs and every class is offered, multi-classes included.

> **An empty list is the opposite of an absent one.** Empty is legal and contains nothing, so
> every class falls through to the second gate — which is the only way the baseclass table ever
> gets a say. Absent and empty are opposite answers from adjacent lines, and it is the *empty*
> case that turns the filter on.

> **The second gate never admits a multi-class.** "Allowed if we have a single Base Class and the
> Base Class allows this race, *or* the race explicitly allows this class". So once a race writes
> a list, a multi-class needs naming — both its baseclasses permitting the race counts for
> nothing. And naming one class does not hide the others: they still pass on their own
> baseclasses, so a designer writing the list to mean "only these" would be surprised.

I had this backwards while writing it — the doc comment said an empty list allowed nothing and an
absent one allowed everything, which reversed which case does the filtering. Three tests failed on
the first run and the reference settled it.

> **`AllowedClass` is a `DELIMITED_STRING`, which is length-prefixed rather than separated.**
> `5.Dwarf3.Elf` is two elements — so an element may contain a full stop or a digit, which is the
> point when it holds names a designer typed. Ported as `DelimitedString`; nothing in the port had
> needed the format before.

Six of the ten steps do not run: stats, name, icon, small picture and the two spell screens each
want machinery the port has not built — an ability roller, **text entry** (which `EnterPassword`
also waits on), and an art picker. The generator stops and names the step it reached rather than
producing half a character.

##### ADD, REMOVE and DELETE — and a page size that was never a constant

Three of the party menu's five character entries run. Six of its twelve now do.

> **`ITEMS_PER_PAGE` is design configuration, and the port had it as a hardcoded 8.** The reference
> reads it from `config.txt` and falls back to **14** (`Globals.cpp:2778`). Every paged list in the
> port — treasure, inventory, and now the roster — was six rows short, and the comment beside the
> constant named the very token it was not reading. No shipped design sets it, so nothing in the
> corpus would ever have contradicted the 8; what caught it was needing the same value for a third
> screen and going to look up what it meant.

> **The paging entries are part of the roster, not a control strip beside it.** `<--- PREV` takes
> the first line when there is a page behind, `NEXT --->` the last-but-one when there is a page
> ahead, and `EXIT` is always last — so how many characters fit depends on which of those are
> showing. That is the exact opposite of the inventory, where paging is a fixed menu of commands
> next to a fixed list. Two screens, two conventions, one page size.

> **Stepping back never leaves a single character behind.** `first -= Items_Per_Page - 2` can land
> on exactly 1, which would draw a `PREV` line for one character; the reference special-cases it
> to zero rather than allow it.

> **Nothing is applied until EXIT.** Selecting a name toggles a `*` and redraws; leaving adds every
> marked character and removes every unmarked one, in one pass. So unstarring someone already in
> the party is a second way to drop them — REMOVE CHARACTER is not the only one.

> **The roster is sorted, and the reason is in the comment.** A bubble sort by name, case
> insensitive, "so that their order will not depend on the operating system that supplied the file
> names". Directory enumeration order is not stable across platforms, and a roster that reorders
> itself between machines is one a player cannot learn.

> **A saved NPC's file is `DCNPC_<name>.chr`.** The roster shows the character's name, not the
> file's — and `purgeCharacter` reassembles the same prefix when deleting, which is why DELETE has
> to know the character's type.

> **The yes/no answer comes back through the trade slot.** `party.tradeItem == 1` means yes
> (`RunEvent.cpp:2408`) — the item-trading register doubles as the answer register, which is why
> `tradeItem` is in the saved `PARTY` record at all. The port keeps the question as state on the
> runner instead, and the confirmation opens on **NO**.

##### Saving works

A game in progress writes to a slot and loads back. `SaveGameProjection` assembles the record,
`Game.LoadFrom` applies one, and the slot screens are wired to real files — so the port is
playable across sessions rather than only within one.

> **A field the engine does not model is carried through, not zeroed.** Every `Character` wraps
> the `CharacterRecord` it was built from, so writing one back means overwriting the eight fields
> play can change and keeping the other forty exactly as read. The alignment, ability scores, icon
> and spell book survive because nothing touched them — not because anything projected them. The
> same trick was needed for quests: `WorldState` keeps only id → state, so it now holds the
> design's records too and writes state and stage over them. Building a quest from live state
> alone would have saved one with no name.

> **Three "empty" values are not absences, and the writer caught all three.** A `LEVEL_STATS` list
> is exactly 255 entries, a `READY_ITEMS` exactly 12 slots, a `MONEY_SACK` exactly 10 coins —
> compile-time counts the reference never writes, so a short list silently truncates the record.
> Phase 1's writers refuse rather than allow it, and each of these surfaced as a clear exception
> at the writer rather than as a file that reads back wrong. That is the guard rails paying for
> themselves a year after they were built.

> **`LEVEL_STATS_VERSION` is stamped on every level, touched or not.** The reference always writes
> 2 and always writes both trailing tables, empty. Writing 0 would be a smaller file that the
> reference reads as a much older save.

Two deliberate divergences, both documented at the call site:

> **The event stack is not saved.** The reference records it, so a game saved inside a shop
> resumes inside the shop; this resumes standing on the square with nothing on screen. Deliberate,
> because SAVE is reachable only from the party menu, which is reachable only from a training hall
> event — refusing to save inside an event would mean never saving at all.

> **Six `PARTY` scalars have no live counterpart and go out as zero** — the party's name, its
> speed, the selected inventory item, the two trade slots and the difficulty. Nothing this port
> runs reads them; the reference reads all six, so a save written here and loaded there arrives on
> the lowest difficulty with an unnamed party. Listed rather than glossed.

**The tail is written empty, and empty is correct.** Its seven `Save`/`Restore` pairs carry the
attributes gameplay *changed*, with the design supplying the rest on load — so a port that has not
yet mutated a spell's or a monster's attributes writes nothing and loses nothing. The one place
where "not ported" and "correct" coincide, and it holds only until something starts changing
those lists.

~~**Not done: loading does not reload the level.**~~ **Done** — see §the level load, below.

##### HEAL, and the one price scale the whole game shares

`TEMPLE`'s heal states (`RunEvent.cpp:12859`) and `ApplyCostFactor` (`Globals.cpp:971`).
`UAF.Rules/CostFactor.cs`, `UAFcore/TempleSpells.cs`. **Both of the temple's own entries now run.**

> **One scale prices everything.** `costFactorType` is twenty steps from a hundredth to a hundred
> with `Normal` in the middle, and the same function serves the temple's spells, a shop's items
> (`Items.cpp:2191`) and `Char.cpp:6695`.

> **`Free` is the only way to pay nothing.** It is answered before the arithmetic; every other
> factor truncates to an integer and then **floors at one**, so a one-coin spell at a hundredth
> still costs a coin. A temple meaning to give something away has to say `Free` rather than
> dividing enough.

> **It truncates rather than rounds** — three halved is one.

> **The list is the temple's own memorised spells, not the party's.** And the character holding
> them is *synthesised*: `TASK_TempleCast`'s first run builds a "TempleBishop" — a maximum-level
> cleric and magic-user with a chaotic-neutral alignment and a gender of `Bishop` — and keeps it in
> the design's NPC list, so that any spell the temple carries can actually be cast. A spell with no
> memorised copy is not offered; the temple is out of it until it memorises again.

**Not ported, and named:** the casting itself, which goes through the ordinary spell machinery —
the same layer FIX waits on. HEAL's FIX entry is `party.FixParty(1)`, the same call camp's FIX
makes with a different environment, so the two arrive together.

##### The temple's donation

`TEMPLE`'s donate states (`RunEvent.cpp:12717`–`:12830`). `UAFcore/Donation.cs`.

> **The amount is typed into a <i>menu</i>, not a text field.** Each digit is its own menu entry
> with a blank one on the end, and the total is the entries concatenated through <c>atoi</c>.
> Backspace deletes the last entry.

> **Too much snaps to the maximum rather than being refused.** A digit that would take the amount
> past what the party can pay — or wrap it negative — clears the whole entry and replaces it with
> the maximum. A player mashing digits ends up offering everything they have, which is presumably
> the point.

> **Pooled money is the party's to give; otherwise it comes out of one purse.** The ceiling is
> <c>party.GetPoolGoldValue()</c> or <c>poolCharacterGold()</c> depending on
> <c>party.moneyPooled</c>.

> **The running total belongs to the temple, not the party.** It is saved with the event, so a
> party giving a little on each of several visits still crosses the threshold.

> **The trigger is only tested on the way out</b>, and crossing it mid-visit does nothing. On exit,
> a total at or past <c>donationTrigger</c> resets the total and <i>replaces</i> the event with
> <c>donationChain</c> — where an ordinary exit follows the event's own <c>ChainEventHappen</c>.
> Both paths chain; the trigger decides which.

> **A trigger of zero fires on every visit.** The total starts at zero and the test is <c>&gt;=</c>,
> so a design that leaves the field unset chains every time the party walks out — donation or not.

##### APPRAISE, and a value the party can never roll

`APPRAISE_SELECT_DATA` (`RunEvent.cpp:26679`), `APPRAISE_EVALUATE_DATA` (`:26793`) and
`GEM_CONFIG::GetAValue` (`Money.cpp:309`). `UAFcore/Appraisal.cs`, and the two screens on the
runner. Reached from HEAL and from DONATE — **the same screen hangs off both of the temple's
entries**, which is why the port wires it to `HealMenu` index 6 and `DonateMenu` index 3 rather
than owning a menu of its own.

> **The maximum is never rolled.** `GetAValue` rolls `|max − min|` sides and offsets by `min − 1`,
> which spans `min` to `max − 1`. A design writing 10 to 100 gets 10 to 99. Transcribed rather than
> corrected — every price in every shipped design was balanced against this arithmetic, and a gem
> that suddenly hits its stated maximum is a different economy.

> **A range of nothing is the maximum.** `sides <= 0` returns `maxValue` outright, which is how a
> design pins a fixed value: set both ends the same. The guard is `<=` rather than `==` because the
> width is an absolute value, so it can only ever be zero — the branch is there for a negative that
> cannot arrive.

> **The two entries are renamed to the design's own words.** Not "GEMS" and "JEWELRY": the labels
> come from the design's gem and jewellery type names, so a design calling them STONES and TRINKETS
> says so on the bar.

> **Each entry needs the service and the purse to agree.** A shop that appraises gems still darkens
> the entry for a party carrying none, and a party with a pocketful of them cannot appraise at a
> service that does not offer it.

> **The piece leaves the purse before it is valued.** Choosing a kind removes one immediately and
> only then rolls what it was worth. There is no way back to an unappraised gem, and KEEP does not
> put it back.

> **Both outcomes spend it; they differ in what replaces it.** SELL adds the value in coins to the
> active character. KEEP creates a *carried item* named for the design's gem or jewellery type,
> worth the appraisal — so a kept gem stops being money and starts being inventory, and from then
> on it weighs something.

###### A correction: only the shop can refuse a kind

I had the enable test wrong. `APPRAISE_SELECT_DATA`'s constructor takes `apprGems` and
`apprJewels` and **both default to `TRUE`** (`GameEvent.h:4590`). The temple pushes the screen
without them — twice, at `RunEvent.cpp:12743` and `:12894` — so **a temple appraises both kinds
whatever its design says**. Only the shop passes `canApprGems` / `canApprJewels`
(`RunEvent.cpp:10986`), so it is the one service that can darken an entry outright.

What I wrote in the APPRAISE round asked the *host* whether a kind was offered and had it answer
"the design has a config for it" — which is a different question with a different answer, and made
a temple in a design with no jewellery config refuse jewellery. The offer is now a parameter of the
screen and the host reports only the design's name and the count. Caught while porting BUY, because
the shop is where the flags live.

##### BUY, and the error a shop shows for the wrong reason

`BUY_SHOP_ITEMS_DATA` (`RunEvent.cpp:11085`), `CHARACTER::buyItem` (`Char.cpp:6670`),
`getItemEncumbrance` (`Items.cpp:602`) and `MONEY_SACK::GetTotalWeight` (`Money.cpp:2362`).
`UAFcore/Shopping.cs`, and the shelf on the runner. **The shop now runs BUY and APPRAISE**, which
is both of the entries it has that are only arithmetic.

> **The shelf is the inventory screen with the columns swapped** — COST on, READY off, where a pack
> shows the reverse. One list widget, two presentations.

> **The reference identifies the shop's whole stock on open**, walking `itemsAvail` and writing
> `identified = TRUE` into the *event* rather than a copy — "shops disclose full name". So an
> unidentified item a designer put on a shelf is identified from the first time a player opens the
> door and stays that way for the session. Nothing here needs it: the port's rows take every name
> from the database and never read the flag.

> **An item's stated encumbrance is for the whole bundle.** A quiver of 20 arrows weighing 2
> divides to 0.1 each and the quantity multiplies back up — so the database field means something
> different for a bundled item than for a single one. The division is floating-point and the result
> truncates, so **part of a bundle can weigh nothing**: nine arrows out of that quiver are free to
> carry and the tenth costs a whole unit.

> **An empty purse weighs one unit.** `GetTotalWeight` floors the division at 1 without ever asking
> whether there was anything to divide, so 0/100 is 1. Every character in a design that gives coins
> a weight carries a unit of nothing. Coins, gems and jewellery are counted as one pile — a gem
> weighs exactly as much as a copper piece, and its appraisal has nothing to do with it.

> **The first weight test asks about one of them, not about the bundle** —
> `getItemEncumbrance(itemID, 1)`, which for a bundled item is usually a fraction that truncates
> away. Then `addCharacterItem` weighs it again properly, sets `TooMuchWeight`, and returns FALSE —
> and `buyItem`'s `else` **overwrites that with `MaxItemsReached`**. So a purchase refused because
> the party cannot carry it tells the player they are holding too many things. Reproduced: it is
> the only way a shop reports weight on a bundle, and `MAX_ITEMS` is 0x00FFFFFF, so that message
> can never be about item count.

> **Nothing stacks.** `addItem` calls `AddItem(newItem, FALSE)` — auto-join off — so buying the
> same dagger ten times leaves ten rows, each with its own key and its own paid price. JOIN on the
> inventory menu is what merges them, by hand.

> **The price paid is remembered on the item.** `paid` is what the shop charged after its cost
> factor, not the database price, and it is what a buyback is computed from later.

> **The shelf does not shrink as things are bought.** A shop's stock is a list of what it offers,
> not a count of what it has.

> **BUY darkens on the price of the row the cursor is on, re-tested every frame** — so the entry
> lights and darkens as the player moves down a shelf they can only half afford. And on the shop's
> own menu it darkens for an active character who is not `Okay`, which TAB can change under the
> player's feet.

**Not ported, and named:** SELL and the buyback percentage, `costToIdentify` and `canIdentify`, and
`buyItemsSoldOnly` — the shop's other half, which lives on the inventory screen's SELL entry rather
than on the shop's own menu.

##### FIX, and the script default that turns out to be the rule

`PARTY::FixParty` (`Party.cpp:3961`) over `FIX_SPELL_LIST` (`:3818`) and `FIX_SPELL_ENTRY`
(`:3681`). `UAFcore/FixSpells.cs`. Reached from camp (`FixParty(0)`, `RunEvent.cpp:9296`) and from
the temple's heal menu (`FixParty(1)`, `:12878`) — **one routine, two environments, differing only
in who casts.** Camp draws on the party's own memorised spells and spends them; the temple casts
from the synthesised bishop, so the party's book is untouched and nothing limits how much it can be
healed.

> **The book is the design's global fix spell book**, not any character's. A design chooses what
> FIX may cast by putting spells in it, and one that leaves it empty makes both entries do nothing.

> **I expected the target test to be unportable, and it is not.** `RandomTarget` pre-loads
> `hookParameters[0]` with `"1"` or `""` on whether the character is below their maximum hit
> points, then runs the spell's `FIX_CHARACTER` scripts. With no such script,
> `SPECIAL_ABILITIES::RunScripts` calls the callback with `CBF_DEFAULT` and returns
> `hookParameters[0]` *unchanged* (`Specab.cpp:1955`), and `ScriptCallback_RunAllScripts` never
> touches the result at all (`:1678`). So the engine's own answer, in every design without that
> hook, is exactly **"below their maximum hit points"** — and a script overrides it rather than
> supplying it. That is the opposite of `ClassChange`, where an absent script means no.

> **Status is not consulted.** Only hit points. A dead character below their maximum is a
> candidate; a petrified one at full health is not.

> **A successful cast does not consume the entry.** The loop keeps returning the same spell for as
> long as it can find a caster and a willing target, so one cure spell heals the whole party one
> cast at a time. **The casting is the termination condition** — healing until nobody wants it, and
> in camp spending a memorised copy each time until no one has one left.

> **The pools are per-spell and never rebuilt.** Each spell keeps its own candidate casters and
> candidate targets, and a candidate rejected once is dropped from that spell's list for the rest
> of the visit. A character who was at full health when a cure spell first looked at them cannot be
> healed by it later in the same FIX, however much damage they take meanwhile.

> **Every party member is a target candidate, with no filter at all** — including whoever is
> casting, so a lone cleric heals themselves.

> **Neither menu entry pushes a screen or says anything.** Both are a bare call in the middle of a
> menu switch, so the player is left looking at the menu they pressed it on and the only feedback
> is the hit points on the status line.

**Wired but held back, deliberately.** `FixSpells.Run` is complete and tested and both entries
reach it, but `Game`'s callback returns nothing: the loop is ended *by* the casting, so handing it
a cast that resolves nothing would spin forever on the first hurt character. One line switches it
on when spells resolve, and the line is in the source. This is the same layer the temple's own
casting waits on.

##### Casting outside combat, and one class where the reference has none

`CHARACTER::CastSpell` (`Char.cpp:17021`) and `CHARACTER::SpellActivate` (`:16913`).
`UAFcore/PartyCasting.cs`, plus `UAFcore/SpellSubject.cs`.

**First: what was already there.** `SpellResolution` — `InvokeSpellOnTarget`, the part that decides
what actually happens to a target — has been ported since the combat work, along with the saving
throw and the effect roll. The gap was only the path *into* it from outside a fight. Grepping
before porting saved the whole of it a second time.

> **Outside combat every target is a party member.** `SpellActivate` matches each selected target's
> `uniquePartyID` against the party and **skips** anything it cannot find, so a spell aimed at
> something that has left the party affects nobody rather than erroring.

> **The global `::SpellActivate` is only a dispatcher** (`Globals.cpp:4245`) — a two-level switch on
> where the caster came from that routes to the right object's own method and does nothing else.

> **The memorised copy is spent first and never refunded.** A target who saves, a target already
> carrying the spell, or no valid target at all each leave the caster one copy poorer. Only an id
> the design has lost escapes the charge, because the lookup fails before the decrement.

> **One active-spell key for the whole cast, allocated before the target loop** — so a spell that
> reached four people expires from all four together rather than each on its own clock. It is spent
> even when the cast reaches nobody. A `Permanent` spell takes no key at all.

> **`LayOrCureOrWhatever` suppresses the decrement**, which is how laying on hands and the temple's
> bishop cast without a spell book behind them.

> **The cast sound plays whether or not the spell affected anybody** — the reference says so in a
> comment, and there is no graphical feedback outside combat at all.

###### One class there, two here

The reference resolves every spell against a `CHARACTER`; a `COMBATANT` holds a pointer back to
one, so `InvokeSpellOnTarget` serves both paths by construction. This port has a `Combatant` that
belongs to a fight and a `Character` that belongs to the party, and they share no base. Rather than
copy the resolution — the mistake this port has made twice and caught twice — `ISpellSubject` names
the four things resolution actually reads off either of them, and `SpellResolution.InvokeOn` takes
that. The old `Invoke(Combatant, Combatant, …)` stays as a one-line delegation, so no combat caller
or test changed.

###### The open question underneath: do effects double-count?

Found while reading, not resolved in that round — see the next section, which settles it.

##### The permanent branch, and why FIX runs now

`CHARACTER::AddSpellEffect` (`Char.cpp:11984`). `UAFcore/PermanentEffects.cs`, plus a correction in
`UAF.Rules/SpellEffectList.cs`.

**The answer to the open question: `AddSpellEffect` has two branches and they are exclusive.**
`isPerm = (pSdata->Duration_Rate == Permanent)`.

> **A permanent spell never reaches the effect list at all.** The `isPerm` arm reads the attribute,
> applies the change and writes it back with `SetDataXXX` (`:12256`), storing nothing — there is
> nothing to expire. So the "double count" I suspected is not one: the eager `ModifyByDouble` and
> the stored entry belong to the *non-permanent* arm, and only that arm is walked by
> `ApplySpellEffectAdjustments`.

> **This is what makes healing work.** A cure spell is permanent, so it moves the character's
> *stored* hit points rather than layering an adjustment over them. That is precisely what
> `FixSpells.WantsFixing` reads, so the FIX loop ends on its own.

> **Virtual traits are the exception.** An attribute with no character field behind it has nowhere
> to be written, so a permanent effect on one is stored anyway (`:12283`). In this port that is any
> attribute `PermanentEffects` does not recognise — including armour class, THAC0 and magic
> resistance, which `Character` reads off its immutable record. Observably identical for every
> reader, since all three are read through their adjusted form; a *saved* game would differ, and
> that is called out in the source.

> **Nothing clamps a cure on the way in.** The write goes through `SetHitPoints`, and the target
> test only asks whether the stored value is below the maximum — so the last cast of a FIX run
> leaves the character *above* it, and `AdjustedHitPoints` is what caps the number anyone reads.

**Still open, and narrower than before:** in the non-permanent arm the change really is applied
eagerly *and* stored, and `ApplySpellEffectAdjustments` walks the stored list with no filter
(`Char.cpp:13062` — the flag test that would have excluded once-only effects is commented out, with
its comment still there). That does look like a genuine double count for temporary effects. It is
not transcribed here: it would change every buff in combat on the strength of a reading I cannot
run, and nothing currently depends on it.

###### A correction: remove-all is an instruction, not an effect

`SpellEffectList.Add`'s remove-all branch cleared the attribute and then added the new effect. The
reference's ends in `return TRUE` (`Char.cpp:12054`) without ever reaching the add — so the flag
means "strip this attribute" and leaves nothing behind carrying the new change. Two tests encoded
the wrong behaviour and now encode the right one. Found by reading `AddSpellEffect` properly for
the branch above.

**FIX is switched on.** Both entries now run for real: camp casts out of the party's memorised
spells and spends them, the temple casts from the bishop and spends nothing, and the loop
terminates because the healing moves the value the target test reads. Proven end to end rather than
argued — three tests drive `FixSpells.Run` through the actual `PartyCasting.Cast`.

##### The cast list, and one field that means four things

`CAST_MENU_DATA` (`RunEvent.cpp:25754`), `CAST_NON_COMBAT_SPELL_MENU_DATA` (`:25924`),
`FillCastSpellListText` (`Spell.cpp:8912`) and the four target accessors (`:4787`–`:4905`).
`UAFcore/NonCombatCast.cs`, `UAFcore/SpellParameters.cs`, and the cast screen on the runner.
**MAGIC's CAST and the temple's CAST both open it.**

> **One dice field means different things to different spells.** `P1` is the target *count* for a
> spell that picks units and the area *width* for one that covers ground; `P2` is the height,
> except in a circle where it is the width again — which is what makes a circle a circle, and what
> frees `P1` to be its count. Four accessors read six fields through four switch tables and the
> targeting mode is the only key.

> **The field names are fossils and will actively mislead.** They are called `P1`…`P6` *because*
> they were renamed away from what they meant, and the header still carries `//Was NumTargets` on
> `P1` and `//Was TargetRange` on `P2`. Neither is true: the range comes from **`P3`**. This
> port's own reader repeats those stale comments where it names the fields.

> **The restriction flags are permissions, not prohibitions** — despite a commented-out
> `NotInCombat`/`NotInCamp` pair sitting directly above them that reads the other way round. A
> spell with neither flag set can be cast nowhere at all.

> **Three environments, two flags.** `CAST_ENV_ADVENTURE` is filtered by the *camp* flag, so a
> design cannot allow a spell while camping but forbid it while walking around.

> **Every refusal is silent.** Six guards pop the pushed screen with nothing but a debug string —
> a combat-only spell, a lost id, a caster who cannot cast. The player presses CAST and the screen
> goes away. Reproduced; a message where the reference has none is a change to the game.

> **Outside combat every area shape becomes the whole party.** `NeedSpellTargeting` answers no for
> all five, and the caller's `else` adds every party member — so a fireball cast in camp hits
> everyone the party has. Only self, and only self, targets one person without asking.

> **A cast with no targets costs nothing.** That branch never reaches `CastSpell`, so the
> memorised copy is not spent — the one path where pressing CAST is free.

**Not ported, and named:** the `SPELL_CASTER_LEVEL` script. (The target picker was the next round
— see below.)

##### The target picker, which is a party cursor with two menu entries

`TARGET_SELECT_NONCOMBAT_EVENT_DATA` (`RunEvent.cpp:25408`) and `STD_AddTarget`
(`Spell.cpp:7503`). The screen on the runner; `SpellTargetSelection` already held every rule it
drives. **The three picking modes now cast**, so every non-combat spell in a design resolves.

> **There is no target list.** The screen is two menu entries — `CAST SPELL ON?` and `EXIT` — and
> the target is whichever party member the cursor happens to be on. `HMenuVPartyKeyboardAction`
> gives the menu the horizontal keys and the party the vertical ones, the mirror of the party
> menu's split, because this menu is horizontal.

> **Choosing targets really moves the active character.** The picker walks
> `party.activeCharacter` and reads `GetActiveChar` as the selection, which is why
> `CAST_NON_COMBAT_SPELL_MENU_DATA` saves it into `tempActive` before pushing this and restores it
> on every exit path.

> **What is still wanted goes on the menu's title, not in the text box** — `menu.setTitle` on the
> menu already up, re-called after each pick rather than rebuilding the screen.

> **The last target closes the picker by itself.** No confirmation: `AllTargetsChosen` pops
> immediately, so a one-target spell is aimed with a single press.

> **EXIT casts at whatever has been chosen rather than abandoning.** The picker just pops, and the
> screen underneath casts if it has any targets — so leaving a three-target spell after one pick
> casts it at one. The *combat* picker asks before abandoning an empty selection; this one never
> asks at all.

> **The same member cannot be chosen twice** — `STD_AddTarget` refuses a duplicate, logs it, and
> leaves the menu up looking unchanged.

> **A target that exactly spends the hit-dice budget lands and ends the selection**; only one that
> would exceed it is refused.

##### Running a record's own scripts, and a callback that is entirely dead code

`SPECIAL_ABILITIES::RunScripts` (`Specab.cpp:1876`) and its callbacks (`:1678` onwards).
`UAFcore/SpecabScripts.cs`. **This is the shape every named hook in the engine goes through** — a
record carries ability names, each named ability may define a script under the hook's name, the
walk runs the ones that do and hands each answer to a callback. `FIX_CHARACTER` and
`CanCastSpells` now run for real.

> **The answer lives in hook parameter 0 and is both input and output.** The caller seeds it and
> reads it back; each script that runs overwrites it. A walk that runs no scripts leaves the seed
> untouched — which is the mechanism behind every "script-backed default" in this plan, including
> the `FIX_CHARACTER` finding two sections up.

> **`ScriptCallback_RunAllScripts` is entirely dead code.** It opens with an unconditional
> `return CBR_CONTINUE;`, and everything after it — a Y/N accumulator and an `ENDOFSCRIPTS` arm
> that would blank the result and stop — is unreachable. Fifteen call sites name it. What it
> actually does is: every script runs, nothing stops the walk, nothing rewrites the answer, and
> the last script wins.

> **`ScriptCallback_LookForChar` is the one that differs, and the difference is the ending.** It
> stops at the first answer containing one of the wanted characters and **trims the answer to that
> single character** — so a script replying in a sentence still satisfies a `result[0] == 'N'`
> test. An exhausted search **blanks** the result, where run-all leaves it, which is what lets
> `DOES_SPELL_ATTACK_SUCCEED` chain to the next source on an empty answer. And "no scripts at all"
> is a third outcome again: the seed survives.

> **`FindOneOf` scans the whole answer, so an ordinary word is read as a verdict.** A script
> answering `MAYBE` is taken as `Y`. Found by writing a test fixture that meant to match nothing
> and matched.

> **Abilities past `MAX_SPEC_AB` are skipped, not a stopping point** — the reference's `continue`
> keeps scanning, so which are dropped depends on the order the record lists them.

**Where this leaves Priority 3.** The harness is now ported; the remaining gap is the opcodes the
scripts themselves use — 83 of ~387 sub-opcodes were implemented in the VM when this was written.
A design whose hooks stay inside them works today.

##### The ability scores, which a script sees three ways

`GET_CHAR_PERM_*` / `ADJ_*` / `LIMITED_*` (`GPDLexec.cpp:3696`–`:3717`), `LimitAb`
(`Char.cpp:13599`) and the bounds at `Globals.cpp:506`. `UAF.Rules/AbilityBounds.cs` and
`UAFcore/AbilityLayers.cs`. **Twenty-one sub-opcodes**, taking the VM from 83 to 104.

> **A character carries three versions of every score and they are not interchangeable.**
> Permanent is what the record stores; adjusted is that plus spell effects and is **unbounded**;
> limited is the adjusted one clamped. GPDL exposes all three by name, so a script can see the raw
> sum a clamp would have hidden — that is the point of having three rather than a bug.

> **The spell-effect key is `CHAR_ADJUSTED_STR`, not `CHAR_STR`.** The commented-out line directly
> above it used `"$CHAR_STR"` and does not any more, so an effect written against the plain name
> reaches nothing at all.

> **Every score shares one range except the strength percentile** — five run 3 to 25, the
> percentile 0 to 100, because it is a percentage and not a score. It is a separate score with its
> own three layers, not a part of strength.

> **`P1` of the parameter fossils has a cousin here**: `UAF.Rules.AbilityScore` is deliberately
> *not* `UAFcore.Ability`. The latter is the design's wire ordinal for a WHO_TRIES check and has
> six members; this one adds the percentile. Two lists that look alike and answer different
> questions.

##### The rest of the character block, and writing back

The remaining `GET_CHAR_*` (`GPDLexec.cpp:3691`–`:3753`) and the whole `SET_CHAR_*` family
(`:5417`). **Forty-four more sub-opcodes, taking the VM from 104 to 148** — the character block is
now complete apart from the per-baseclass calls that take an argument.

> **A setter pops the value before the actor.** `m_SetCharInt` is
> `m_popInteger1(); Dude(msg)->f(...)` — actor pushed first, value second. Getting it backwards
> writes the stat onto a character named "9" and leaves the real one untouched, silently.

> **Every setter yields the empty string.** They end in `m_pushEmptyString`, so the call is an
> expression with no value — and the compiler depends on it leaving exactly one thing on the stack.

> **`SET_CHAR_SEX` and `SET_CHAR_GENDER` are the same call**, two names for one `SetGender`, kept
> because designs were authored against either.

> **`GET_CHAR_FLOAT` formats to eight decimal places.** Hit dice and number of attacks come back as
> `"1.00000000"`, not `"1"` — so a script comparing either against a plain literal never matches.

> **`SUBOP_SET_CHAR_MAGICRESIST`'s diagnostic string is misspelled** — `"$SET_CHAR_<AGICRESIST()"`,
> an `M` that became a `<`. Cosmetic, and only visible in an error message, but it is the sort of
> thing a transcription should not silently tidy.

**Two deliberate divergences, both named in the source.** A `Character` here reads age, movement,
alignment, size, the two combat bonuses and the portrait index off its immutable record, so a
script setting one of those changes nothing where the reference would have changed it — the call
still yields the empty string, so a script cannot tell from inside. And a non-numeric value is
*ignored* rather than written as zero: the reference pops through `atoi` and would zero the stat,
and silently zeroing a character's strength on a script typo is worse than doing nothing.

##### The party block, and a setter that pushes nothing

`GET/SET_PARTY_*` (`GPDLexec.cpp:5551` onward), `$PARTYSIZE` (`:5074`), `$InParty` (`:4483`) and
`$GET_PARTY_MONEYAVAILABLE` (`:4215`). **Seventeen more, taking the VM from 148 to 165.**

> **`SET_PARTY_FACING` pushes nothing, where every sibling setter pushes the empty string.** The
> commented-out line above it shows it used to be `m_setPartyValue(PARTY_FACING)`, which went
> through `m_SetLiteralInt` and ended in `m_pushEmptyString`; inlining it lost that. So it consumes
> a stack slot and produces none, and a script using it is unbalanced. Transcribed — a design
> tested against the reference was tested against that.

> **`ACTIVECHAR` is read and written in different units.** Reading gives the active member's
> `uniquePartyID`; writing takes an *index* and wraps it with `% numCharacters`. A script cannot
> round-trip it, and feeding a read straight back into the write lands somewhere arbitrary.

> **The clock fields are not clamped on write.** `SET_LITERAL_INT` assigns straight through, so a
> script may set hours to 99 and the party's clock holds it. `SET_PARTY_FACING` is the only one
> that clamps.

> **`GET_PARTY_LOCATION` is a string, and its level is one-based** — `"/level+1/x/y"`, where every
> other level reference in the engine counts from zero.

> **`MONEYAVAILABLE`'s out-of-range argument answers zero, not the total.** 0 means the raw sum in
> the base coin and 1–10 name a denomination; anything else falls through to `m_Integer2 = 0`.

> **`SET_PARTY_XY` is queued, not done.** It posts `TASKMSG_SetPartyXY` and the move happens when
> the task queue next runs, which is why callers test `setPartyXY_x >= 0` afterwards to find out
> whether a script moved the party out from under them.

**One divergence, named in the source.** This port keeps the clock as a single minute count where
the reference keeps three independent ints, so an out-of-range hour folds into the day here rather
than being held. A script that writes 99 hours and reads them back sees 99 in the reference and 3
here.

**A stale test found in passing.** `An_unported_subop_throws_with_a_citation` used `$PARTYSIZE` as
its example of something unported — which this round ported, so the test started failing for the
right reason. It now points at `$GET_CHAR_EFFAC`, and says so.

##### The combat queries, and an imbalance in the other direction

`GetCombatRound` (`GPDLexec.cpp:5858`), `GetCombatantState` (`:5277`), `CombatantLocation`
(`:5887`), `COMBATANT_AVAILATTACKS` (`:6253`), `TeleportCombatant` (`:5868`) and the seven
selectors (`:4578`, `:4849`, `:4904`, `:4944`). **Twelve more, taking the VM from 165 to 177.**

> **Out of combat, `NEAREST_TO` pushes without popping.** The early exit runs
> `m_pushString2(); break;` — *before* the `m_popString1()` two lines below it — so the call leaves
> its argument on the stack and adds a result: one deeper than it found it.
> `NEAREST_ENEMY_TO` and `LAST_ATTACKER_OF` do the same. This is the mirror of
> `SET_PARTY_FACING`'s missing push, and worse for being **conditional**: the same script is
> balanced inside a fight and not outside one. The four damage selectors take no argument, so
> their identical early exit is harmless.

> **Any axis but `"X"` is taken as Y.** `CombatantLocation` tests for the one string and falls
> through, so a typo'd axis silently answers the other one. It answers −1 for no fight or an id
> that names nobody.

> **`COMBATANT_AVAILATTACKS` is a read, an assign and an add in one call**, chosen by a trailing
> function argument: 0 assigns, 1 adds, and **any other value only reads**.

> **`GetCombatRound` has a hardcoded 3 in the editor build.** Not reachable from the engine, but it
> is what the `#else` answers, and a reader diffing the two builds will meet it.

###### The actor type is enforced in both directions

Writing the tests turned up a language rule worth recording: `systemfunctions[]`'s type flags are
checked on the **return** *and* on each **parameter**. A selector returns an *actor*, so `$RETURN`
refuses it outright; and an actor-typed parameter refuses a string literal, so
`$GetCombatantState("hero")` does not compile either. An actor can therefore only come from another
call — and the only actor producers that need no actor themselves are the four damage selectors.
Every combat script in a design is built up from one of those four or from a context call.

##### The script context, which is a stack that inherits nothing

`$AttackerContext`, `$TargetContext`, `$CombatantContext` (`GPDLexec.cpp:5683`),
`$MonsterTypeContext` (`:4744`) and the `SCRIPT_CONTEXT` they read (`Specab.h:817`,
`Specab.cpp:324`). `UAF.Scripting/GpdlScriptContext.cs`. **Four more, 177 → 181** — and the piece
that makes the other combat calls usable at all.

> **The context is a stack, and its frames are RAII.** Constructing a `SCRIPT_CONTEXT` pushes it
> onto the global `pScriptContext`; the destructor pops it. Every hook in the engine declares one
> on the stack, so the declaration *is* the scope. Modelled here as a `using`.

> **A new frame inherits nothing.** The constructor nulls every field rather than copying the frame
> below, so a script that pushes a context and asks for an attacker its caller had set gets
> nothing. That is why the hooks set the same two or three contexts over and over — it looks like
> redundancy and is not.

> **`$MonsterTypeContext` is not actor-typed.** It pushes the monster's database id, so its type
> flag is 0 and it can be returned like any other string; the other three are actors and cannot.
> Four calls that look like one family are two.

> **A missing context is an error box and then the empty string.** The reference alerts and carries
> on. There is no dialog here, so the complaints are collected instead — a script reaching for a
> context nobody set is broken in a way silently answering `""` would hide.

**Why this mattered before the rest of combat.** The actor type is enforced in both directions
(§the actor type), so an actor can only come from a call. Before this round the only actor
producers were the four damage selectors; now a script can name its attacker, its target and
itself, which is what every real hook does. `CanCastSpells` and `FIX_CHARACTER` set theirs.

##### Backing the combat calls with a real fight — and two selectors that do not do what they say

`GetNearestTo` and its neighbours (`Combatants.cpp:7500`–`:7700`), behind `GameScriptHost`'s
combat members. `UAFcore/CombatSelectors.cs`. No new sub-opcodes: this is the half that turns last
round's plumbing into behaviour.

> **`GetNearestTo` never excludes the combatant it was asked about, so it always answers that
> one.** The loop has no `i != self` guard, the distance from anyone to themselves is zero, and
> the comparison is strictly `<` — nothing can beat it. The function is useless as written.
> Transcribed, because a design's script was written against what it does rather than what it is
> called.

> **"Enemy" means *not friendly* in absolute terms, not "the other side from you".**
> `GetNearestEnemyTo` filters on `!GetIsFriendly()` with no reference to the asker, so a monster
> asking for its nearest enemy is handed the nearest *monster* — and, by the rule above, itself.
> Only a party member gets the answer the name promises.

> **"Most damaged" means lowest hit points, not most damage taken.** The comparison is on
> `GetAdjHitPoints` alone with no reference to the maximum, so a goblin at full health with four
> hit points is "more damaged" than a fighter on 60 of 100. **The first of a tie wins** in both
> directions, since the comparisons are strict.

> **Nothing filters on being alive.** A combatant on negative hit points is still the most damaged
> candidate on its side.

**Not ported, and named:** `LAST_ATTACKER_OF`. The port keeps no per-combatant record of who struck
last, and inventing one would be a rule rather than a transcription; it answers the null actor,
which is what the reference answers out of combat anyway.

**A combatant's actor string is its list index here**, where the reference packs a source flag and
an instance into `ActorType`. There is no fight-independent combatant identity in this port, so the
index is the identity — and it means only what it means while that fight is running.

##### What a script can learn about the ability running it

`$SA_NAME`, `$SA_PARAM_GET/SET`, `$SA_SOURCE_TYPE/NAME`, `$SA_REMOVE` (`GPDLexec.cpp:3215`, and
`SA_Name`/`SA_Param` at `:1957`). **Six more, 181 → 187**, and the reason `SpecabScripts` now
carries the specab pair rather than just its name.

> **The ability is a key/value pair, and the value is the parameter.** `$SA_NAME()` and
> `$SA_PARAM_GET()` are the two halves of the specab entry that triggered the script — so one
> script shared by three abilities can tell which of them is running it and what each was
> configured with. That is what makes a design's "Regeneration 3" and "Regeneration 5" one script.

> **A missing ability answers a sentinel, not an empty string.** `NO_SUCH_SA` is `-?-?-`, five
> characters a design compares against — which is how a script distinguishes "no such ability" from
> "the parameter is blank". Both are reachable and they mean different things.

> **`$SA_PARAM_SET` yields what it was given.** It pushes the same value back, where every
> character and party setter pushes the empty string — so this one setter is usable as an
> expression, and the asymmetry is not an accident of inlining like `SET_PARTY_FACING`'s was.

> **The source-type words are the wire format.** `"EVENT TRIGGER"` keeps its space and an
> unrecognised type is `"Unknown"` with that capitalisation, because a design compares
> `$SA_SOURCE_TYPE()` against the literals.

> **`SA_Param`'s "NULL SA List" complaint is a `static bool`** — logged once per process, not once
> per call. A design with a broken lookup in a loop gets one line and then silence.

**Not ported, and named:** the `SA_<record>_GET` lookups and `GET/SET/DELETE_<record>_SA`, which
read another record's ability list rather than the running one — they need a specab store the port
can address by record, and `$SA_REMOVE` records its request rather than writing for the same
reason.

##### What opening a rest does — and a claim I got wrong

Wiring memorisation into the resting cycle meant reading `REST_MENU_DATA::OnInitialEvent`
properly, and it does two things I had not ported.

> **The rest screen opens already filled in.** Its duration is seeded from `party.CalcRestTime()`
> — how long the party needs to memorise everything selected — so a player who just wants their
> spells back presses REST and nothing else. This port opened at zero.

> **And it wakes the unconscious.** `party.BeginResting()` runs at the end of the same function:
> an unconscious character is set to one hit point and `Okay`. Woken, not healed — which is
> exactly what the day's auto-heal needs, since that skips the unconscious.

**Which corrects something I stated two sections above.** I wrote that `BeginResting` is "never
called from anywhere in the source". It is called, once, from the line above. The grep behind the
claim was piped through `head -8` and the call site was the ninth line. **A truncated search read
as a complete one** — the same shape as the dice-expression probe that sampled 3% of the corpus and
reported a settled answer. The lesson repeats: a search that could be complete and a search that
*is* complete look identical in the output.

The memorisation itself:

> **A minute at a time, unlike the auto-heal.** The resting branch loops `inc` times over the whole
> party, so memorisation gets every minute of a coarse step — where the day's hit point is granted
> at most once per cycle. A forty-five minute step finishes three first-level spells.

> **It gates on `CanCastSpells`, not `CanMemorizeSpells(1)`.** The header documents circumstance 1
> as "Resting; should character memorize spells" and **nothing ever asks it** — the only call in
> the engine is `CanMemorizeSpells(0)` on the magic menu.

> **Only the last announcement survives.** A minute that finishes a copy sets the paused text; a
> minute that finishes nothing *clears* it — and the clearing is inside the per-character loop, so
> one caster finishing nothing wipes what another just set. A long step that ends quietly shows
> nothing at all, however many copies it finished on the way.

##### The MEMORIZE screen, and a live character's spell book

`MEMORIZE_MENU_DATA` (`RunEvent.cpp:25101`, `:25208`) over the working list. Camp reaches it
through MAGIC, so **two of MAGIC's six entries now run**.

> **The screen and the entry that opens it are gated on different predicates.** MAGIC darkens
> MEMORIZE on `CanMemorizeSpells(0)`; the screen's own `OnInitialEvent` checks `CanCastSpells()`
> and pops straight back out. A character who may memorise but may not cast presses a live entry
> and nothing happens — the refusal is the screen failing to appear, not a message.

> **A caster with no castable spells is left with one way out.** An empty list darkens the other
> five entries and the reference returns early, so EXIT is all there is.

**A live character had no spell book.** Until now the only `SpellList` in the port belonged to a
`Combatant` — built for a fight and thrown away with it — so nothing carried a caster's selections
between rests. `Character.Book` is seeded from the record and projected back, for the same reason
the ability scores are: MEMORIZE moves it.

> That is the hole the previous round named — "nothing populates it from a record yet" — and it is
> why making `selected` a count had broken nothing. It would have broken here.

One bug of my own, caught by pushing a test one assertion further: **pressing a live MEMORIZE entry
that then refuses to open left neither screen active.** I closed the hub before the pushed screen
had opened. The reference gets this free — its screen pops itself and lands back on the magic menu
still sitting underneath — and the port now closes the hub only once the new screen is really up.
The test had passed while asserting only that the screen was absent; asserting where the player
actually ended up is what found it.

##### The memorise screen's working list

`FillMemorizeSpellListText` (`Spell.cpp:8735`) and the two count functions (`:9662`, `:9708`).
`UAFcore/MemorizeList.cs`. The rules behind MAGIC's MEMORIZE.

> **A slot belongs to a school <i>and</i> a level, and every spell at that pair shares it.**
> Selecting one level-three wizard spell takes a slot from every other level-three wizard spell on
> the screen. The count comes from the school ability's base plus bonus at that level, then through
> any script adjustment matching the school — or the wildcard <c>*</c> — and covering the level:
> <c>available = available * percent / 100 + bonus</c>.

> **A spell whose school gives no slots is absent, not greyed.** The row is built and then dropped
> unless <c>available &gt; 0</c>, so a caster does not see the spell listed as unavailable — it is
> simply not there.

> **What is already selected has already been paid for.** A second pass subtracts every row's
> <c>selected</c> from the <c>available</c> of every row at the same school and level, itself
> included, so the number left really is the slots still free.

> **Nothing bounds the counts; the menu does.** <c>IncreaseSpellSelectedCount</c> adds and
> subtracts unconditionally — the only thing stopping a caster overcommitting is <c>OnUpdateUI</c>
> darkening SELECT at zero. The guard is kept where the reference puts it.

> **UNSELECT only goes down to what is memorised.** A copy already in the caster's head is dropped
> with FORGET, which decrements the memorised count and **does not return the slot** — the
> reference's function carries the comment "Now we need to decrease the available counts for all
> spells of this school and level" and then returns without doing it. Correctly, as it turns out:
> <c>selected</c> still holds the slot, so the copy is simply memorised again. The slot comes back
> by unselecting, not by forgetting.

> **The screen edits a copy and EXIT is the commit.** Nothing reaches the character until the
> player leaves, and there is no cancel — escape *is* EXIT.

##### The memorisation clock — and a comment the port believed

The rules behind MAGIC's MEMORIZE and REST's spell recovery (`Spell.cpp:1189`–`:1256`, `:2701`,
`:2958`; `GameRules.cpp:4141`). `UAFcore/SpellList.cs`.

> **`selected` is a count, not a flag, and its own comment says otherwise.** The field is declared
> `int selected; // TRUE if dude will memorize this spell again`, and every use reads it as a
> quantity — `HaveUnmemorized` is `selected > memorized`, `SetMemorized(all)` assigns
> `memorized = selected`. Only `IsSelected` treats it as a flag, and even that says
> `selected > 0`. **This port believed the comment and made it a `bool`**, which cannot express
> "I want three of these" — the shape the whole clock is built on. The *reader* had it right the
> whole time: `CharacterSpell.Selected` has been an int since Phase 1. The live model had drifted
> from the record it loads.
>
> Nothing broke, because `SpellList` is only reachable from `Combatant.Book` and nothing populates
> it from a record yet. It would have broken the moment anything did.

> **Fifteen minutes a level to memorise a spell** — and a preparation block first, for the whole
> book, keyed on the highest level still wanted: four hours for levels one and two, rising to
> twelve above eight. Preparing for a single third-level spell is **six hours before the
> forty-five minutes of memorising it**.

> **The book prepares once per rest, not once per spell.** When the preparation clock passes, both
> counters are cleared for good and every remaining copy memorises back to back.

> **Only one spell memorises at a time.** The list is walked in order and the first entry still
> wanting copies takes the whole slice; everything after it waits.

> **`JustMemorized` is cleared by the reader.** The announcement loop clears it as it prints and
> `IncMemorizedTime` clears it again on entry — so a copy finished and never announced is
> forgotten on the next tick.

Two things transcribed as written rather than repaired:

> **The preparation overshoot looks swapped.** On the tick that finishes preparing, `delta` is how
> far past `needed` the clock went — the minutes that ought to count toward memorising — and the
> reference does `minuteInc -= delta`, keeping the other part. The resting path only ever passes
> one minute, where the two are equal, so nothing in the shipped engine can tell.

> **`CalcRestTime`'s shortfall is unguarded, so a surplus shortens the estimate.** The live loop
> sums `single * (selected - memorized)` with no test that the first exceeds the second — the
> commented-out version directly above it had exactly that guard. A spell with more copies
> memorised than wanted contributes a *negative* number of minutes.

##### MAGIC — a hub whose defaults run the other way

`MAGIC_MENU_DATA` (`RunEvent.cpp:26468`, `:26578`) — camp's **tenth of twelve**. Six entries, three
separate gating rules. `UAFcore/SpellPermissions.cs`.

> **Both spell permissions default to <i>yes</i>, and that is the opposite of CHANGE CLASS.**
> `CanMemorizeSpells` literally seeds `"YYYYY"` as its "innitial assumption" and scripts can only
> narrow it; `CanCastSpells` looks for a script answering `"N"` and finds none. So a design with no
> scripts casts and memorises freely — where the same design can never change class, because that
> gate starts from an empty answer only a script can fill in. Two script hooks, opposite failure
> modes, and only one of them is a refusal.

> **The class-level cast hook never applies.** The combatant's and character's answers are checked
> with `.IsEmpty()`; the class's is `if (!pClass->RunClassScripts(...))` — a `CString` through
> `LPCTSTR`, whose buffer is never null, so the negation is always false and the branch is dead. A
> design putting its casting rule on the class finds it silently ignored.

> **SCRIBE has no fixed name and no fixed meaning.** `CAN_SCRIBE_OR_WHATEVER` runs on the character
> and its class and answers with the menu text *and* a shortcut index; an empty answer darkens the
> entry. The reference's own constant is spelled `SCRIBE_OR_WHATEVER` — with no script there is
> nothing to call it and nothing for it to do, so it is dark in every shipped design.

> **In combat, MEMORIZE, SCRIBE and REST go dark and the character predicates are not consulted at
> all** — the whole `else` branch that reads them is skipped, so a character who could not
> otherwise cast still gets a live CAST entry in a fight.

> **The hub's own no-magic rule is unreachable from camp.** It would darken CAST, MEMORIZE, SCRIBE
> and REST — but camp darkens its MAGIC entry on the same flag first, so the branch can only be
> reached from a magic menu pushed by combat. Found by a test that could not walk its cursor onto
> the entry it wanted.

REST is reached from here as well as from camp, so the rest screen grew a parent the way the
inventory, the slot screens and the confirmation already had one. CAST, MEMORIZE and DISPLAY are
each a screen of their own and are named rather than run.

##### Time passing, and two things that never happen

`PARTY::ProcessTimeSensitiveData` (`Party.cpp:4052`) — the function `OnCycle` calls "basically all
of the time". `UAF.Rules/RestClock.cs` and `UAFcore/PartyTime.cs`. This is what REST was waiting
on, and it is where memorisation and FIX will hang too.

> **Only unbroken rest counts, and the break costs everything.** Any cycle where the party is not
> resting sets the tally to **zero**, not down — twenty-three hours of sleep interrupted for a
> minute is worth nothing. Over a day the tally is *reduced* by a day rather than cleared, so two
> unbroken days really is two hit points.

> **At most one hit point per cycle, however long the cycle.** The reference tests
> `if (minutesRested >= 1440)` once and subtracts once — not a loop — so a rest stepping a
> fortnight heals a single point and carries thirteen days forward. With the delta ladder
> shortening as a rest runs down, healing is roughly per cycle rather than per day.

> **One hit point a day is the live rule.** The generous version — three points for a full day's
> rest, one otherwise, granted every 24 hours whether resting or not — is the `OLD_AUTO_HEAL`
> branch and is compiled out. The comment beside it records the decision: "According to Eric and
> Tom we should add a hit point for every 1440 minutes of unbroken rest."

> **Spell effects expire on every cycle, not only while resting** — a blessing wears off while the
> party walks.

**And two things in this function never happen at all.**

> ~~**Resting does not wake an unconscious character.**~~ **Half right, and the wrong half was a
> truncated grep.** The block inside this function (`:4175`) really is unreachable — it sits inside
> `if (lastUpdateTime != -1)` and is gated on `if (resting && (lastUpdateTime == -1))`. But
> `PARTY::BeginResting` (`:4018`) is **not** uncalled: `REST_MENU_DATA::OnInitialEvent` calls it
> (`RunEvent.cpp:22812`), so opening the rest screen does wake the unconscious. See §what opening
> a rest does.

> **The poison tick is commented out**, so a poisoned character loses nothing over time. Nothing
> to port.

**Not ported, and named:** spell *memorisation*, which needs `IncAllMemorizedTime` and the
per-character spell list; drink points; and the background-music day/night switch. The new-day
resets — item charges and lay-on-hands — have a place in the code and no rule behind them yet,
because neither is modelled on the live character.

##### REST — and a form that had been sitting there for rounds

`REST_MENU_DATA` (`RunEvent.cpp:22652`, `:22815`) — camp's **ninth of twelve**. Two states: setting
a duration, then spending it.

> **The two directions of the editor are not inverses.** Incrementing carries — and because the
> minutes case re-tests the hours, one press at 23:59 advances the day. Decrementing does not
> borrow: each field refuses at zero, so one day flat cannot be counted down through its minutes.

> **ADD and SUB act on whatever the cursor last passed over.** The reference re-syncs the form to
> the menu after *every* keypress (`:22673`), so walking from HOURS rightward to ADD crosses MINS
> and takes the selection with it — pressing ADD there adds a minute. Only the field immediately
> before ADD is reachable that way; anything else has to be adjusted with `+` and `−`. A real
> wart, faithfully reproduced, and the thing that made three of my first tests wrong.

> **Time passes faster the more of it is left** (`GetMinuteDelta`, `:10485`) — a fortnight a cycle
> at the top of the ladder, a minute at the bottom, so a sixty-day rest does not take an hour of
> real time. **Its top four rungs are commented out**, which is why anything past sixty days steps
> a fortnight rather than a proportion of what remains. **Its final guard cannot fire**: every live
> rung returns a delta no larger than the threshold that selected it, so the remainder is never
> below it. It exists for the rungs that were removed.

> **The clock advances a minute at a time even though the delta is coarse**, because the zone's
> rest-event counter is per minute and a rest can be interrupted part-way through a step. A
> fifteen-minute step gives the zone fifteen chances, not one.

> **The zone's counter resets when it is checked, not when it fires.** A roll that misses still
> starts the interval again.

**This is the first screen that needed a cycle.** `GameEvent::OnCycle` runs "regardless of whether
the player provides input", and the port's loop was purely input-driven — so `Game.Cycle` is new,
and the main loop now calls it every frame. Only REST uses it; the reference also drives
spell-effect expiry and the auto-heal timer through it, by way of
`PARTY::ProcessTimeSensitiveData`. **That function is what REST's healing and spell memorisation
hang off, and it is not ported** — so a rest passes time and can be interrupted, and nobody heals.

> **`RestTimeForm` already existed, built and tested, wired to nothing.** The art-picker situation
> again: I wrote the carrying increment and the refusing decrement a second time, in a second
> place, and found out only because the `RestField` enum collided on a build. The duplicate is
> gone; `RestDuration` keeps only what a *running* rest needs — how much is left, and how fast to
> spend it. **Grepping for the screen's name before porting its rules would have cost nothing.**

Three more encamp gates turned up while reading `OnUpdateUI` (`:9197`):

> **MAGIC follows the zone's `AllowMagic`.** **FIX is dark in a no-rest zone whatever pushed the
> camp; REST is dark only when the camp came from the world** rather than from an event — so an
> event that camps the party can rest them somewhere they could not have chosen to. Only the
> event-pushed path exists in this port, so the flag feeding that rule is a property rather than a
> constant, to stay visible when the other arrives.

##### ALTER, and the marching order

`ALTER_GAME_MENU_DATA` (`RunEvent.cpp:22353`) is a hub of nine over one character; three of them
run — ORDER, DROP and EXIT — which takes camp to **eight of twelve**. The other six are settings
screens and the two art pickers, named rather than run.

> **ORDER and DROP are dark below two characters** (`:22423`). There is no order to alter with
> one, and dropping the last member would leave no party.

`ALTER_ORDER_MENU_DATA` (`:22581`) is the smallest screen in the game: one EXIT entry, and all the
work is in two arrow keys.

> **The ends wrap by rotating rather than refusing.** `DecCharacterOrder` on the front character
> shifts everyone else forward and drops it at the back; `IncCharacterOrder` on the back one does
> the reverse (`Party.cpp:4857`, `:4891`). A player holding a key cycles the party instead of
> jamming against slot one.

> **The active index follows the character it moved**, not the slot it left — which is what lets
> the next press keep moving the same one.

> **DROP is the party menu's REMOVE, asked from somewhere else.** Same question, same confirmation,
> same opening-on-NO. What differs is only where answering returns to, which is why the
> confirmation grew a parent the way the inventory and the slot screens already had one.

One ordering bug, caught by its own test: **a pushed screen has to answer before the screen it
sits on.** DROP's confirmation was reaching `ChooseAlter` instead of `AnswerConfirm`, because the
ALTER branch had gone in ahead of the confirmation's rather than after it — so YES did nothing at
all. That rule is what the rest of the dispatch already follows; this was the one branch that
broke it.

##### The camp screen's journal — and a rule that was dark by omission

`DISPLAY_PARTY_JOURNAL_DATA` (`RunEvent.cpp:27570`) and `FormatJournalText`
(`FormattedText.cpp:1201`). `UAFcore/JournalScreen.cs`, plus six methods on `TextDisplayData`.
**Seven of camp's twelve entries now run** — SAVE, LOAD, VIEW, TALK, JOURNAL, ZAP and EXIT.

> **The journal pages by lines, not by entries.** Every entry is concatenated into one passage and
> then wrapped, so a long entry spans boxes and a box can hold the end of one and the start of the
> next.

> **The separator carries a colour reset.** `"\b\n\n"` — the `\b` is the journal's own tag
> (`CheckJournalColorTag`, `:343`), clearing whatever colour the previous entry left set, and it is
> stripped before drawing so it costs no width.

> **Empty entries are skipped but still counted.** The separator goes on while
> `count < jdata.GetCount()`, where `count` only advances for entries that had text and
> `GetCount()` is the whole list — so a journal ending in empty entries puts a separator after its
> last real one.

> **The journal gets six paging methods of its own** (`:581`–`:645`) rather than the box methods
> with a bigger count, and the difference is real: `NextBox` reads the lines it steps over and
> stops early at a `/N`; the journal's never does, because what it shows is many entries
> concatenated rather than one authored passage.

> **It opens on the last box.** A player opening the journal wants what just happened.

> **`LastJournalBox` has a bug that only bites on an empty journal.** It floors the line at zero
> and then re-tests `currLine >= numLines`, which with no lines at all is `0 >= 0` — putting the
> line back to `-20`. Nothing else reaches it and what it would do is read before the start of the
> list, so this port stops at the floor.

**And porting the encamp menu's enable rules found TALK had never been wired at all.** `Game` set
no talk callback, so the entry was dark by omission; now it is dark by rule, and live when the rule
says so. Its three conditions (`RunEvent.cpp:9215`):

> **The label is not decoration.** `changeMenuItem(8, dude.TalkLabel)` *renames* the entry and then
> re-derives the first-letter shortcuts, so a character's own word appears on the bar — and a
> character with an event but no label would leave a nameless entry, which is why an empty label
> darkens it. The two are one rule.

> **`DisableTalkIfDead` is a third condition**, applied after the other two against a status that
> is not `Okay`.

> **The dispatch's `DO_NOTHING` fallback is unreachable from the keyboard** once `OnUpdateUI` runs,
> since a dark entry cannot be selected. It is kept anyway — a mouse click or a shortcut could
> disagree with the enable pass, and then the screen has to stay up.

One test had to change for a reason worth recording: `EventCampTests`' menu helper **counted
keypresses**, which is the exact trap already recorded two sections above. It worked until entries
started darkening, because a dark entry is skipped and N presses stop advancing N places. It steps
until it arrives now, and asserts it did.

##### The level load, and three bugs in the transfer beside it

`LoadLevel` (`Level.cpp:2210`) was the loose end saving left behind, and it was one extraction
away: every level-dependent thing — the map, the zones, the event lookup, the wall resolver and
the wall sets — was built once in `Game`'s constructor and never rebuilt. Pulling it into a
`LoadLevel(index)` the constructor also calls closed the load path and the teleporter's
cross-level branch together.

> **It does not move the party.** Every caller in the reference stashes the square before calling
> and puts it back after, so where the party ends up is always the caller's decision — a
> savegame's stored square, a teleporter's destination — never the level's own idea of a start.

> **A failure leaves the game on the level it was already on.** `LoadLevel` assigns
> `globalData.currLevel` only inside its success branch, and its callers set
> `miscError = LevelLoadError` rather than proceeding.

> **The destination has to be copied before the load.** "This data gets wiped when the new level
> is loaded" (`Party.cpp:3483`) — the `TRANSFER_DATA` lives in an event on the level being left,
> which the load frees. A record parameter makes the copy for free here, but the hazard is real and
> the reason is worth keeping.

> **Entry points come from the level just loaded**, not the one being left — the reference says so
> in a comment, and reading the old table would place the party by coordinates that mean something
> else entirely.

Reading `TeleportParty` properly to get the cross-level case turned up **two live bugs in the
same-level case the port already had**, neither of which any test covered:

> **A facing of 4 means "unchanged", and this port masked it to two bits.** `if (df == 4) df =
> facing` (`Party.cpp:3520`); `4 & 3` is 0, which is north. A teleporter meaning to leave the
> party looking the way it came was spinning it — silently, and only for the designs that use the
> sentinel.

> **`destEP == -2` means "the square you are already on".** The port read the stored coordinates
> instead. The reference honours this only in its same-level branch; since "stay here" across a
> level change names a square on a different map, this port honours it either way rather than
> reproducing a gap that no design can sensibly rely on.

> **An off-map destination is refused rather than arrived at.** The reference bounds-checks too,
> but only *after* loading the new level — leaving the party on it at the old level's coordinates.
> Its own comment questions this: `// reload old level?`. The port refuses before moving anyone.

##### Blockages and vaults — the last two, and one that was never missing

**The journal was never missing.** `Party.Journal` has been live since the journal event was
ported, filled by `EventJournal.Apply`, and is already the type the savegame's field takes. Naming
it on the untracked list two rounds ago was an error, corrected here rather than quietly dropped —
the list was written from the savegame record's field names without checking each against the
engine, which is exactly the shortcut it was created to prevent.

> **`BLOCKAGE_STATUS` is a list of *clearances*, not of blockages.** The class name says otherwise
> and so does the comment beside the struct — "1 of these saved for each blockage removed" is the
> only line that gets it right. Every accessor reads the other way: `IsSecret` returns **TRUE**
> for a cell that is not in the list, because "not found means party has not cleared secret bit
> for this spot yet" (`Char.cpp:574`). An empty list is a dungeon where nothing has been opened. A
> port that read it as "these are the walls in the way" would have the entire map inverted and
> every secret door already found.

> **Every bit starts at 1 and is zeroed on clearing.** A new entry is created as `0xFFFF` and then
> one bit is cleared, so a record's presence means only that *something* about that cell has been
> dealt with — not that the cell is open.

> **The bit groups are ordered North, South, East, West. The facings are North, East, South,
> West.** `Char.h:53` against `Externs.h:1039`: transposed for East and South, and nothing in
> either declaration hints at the other. Indexing the flags by the facing value means a secret
> door found to the east opens one to the south and stays shut — a bug that would look like a
> design error for as long as anyone cared to look.

> **A vault is global and numbered, not per-level.** A `VAULT_EVENT_DATA` carries only a
> `WhichVault` index, so two vault events naming the same number are two doors onto one store —
> which is how a design hands a party its belongings back in a different town. Fifteen of them,
> and the savegame writes every slot, so an empty vault is a record rather than an absence.

`Purse.ToRecord` was added as the inverse of `Purse.FromRecord` — the projection needs it for
characters and vaults alike, and **it writes all ten coin slots** because `MONEY_SACK` blits a
fixed array; emitting only the active denominations would shift everything after them.

**With these, every piece of live state a savegame carries is tracked.** `SaveGameProjection`
still refuses, but the reason has changed from "the state is not kept" to "the file cannot yet be
assembled" — and the second costs no gameplay work to fix.

##### Visited squares

The second of the five, and the one an automap will want the moment it exists: a bit per square, a
bitmap per level, allocated only for levels the party has actually entered.

> **The bitmap's bounds are the format's, not the level's.** Every one is 100 × 100
> (`MAX_AREA_WIDTH` × `MAX_AREA_HEIGHT`) whatever size the level is, because `SetVisited`
> allocates a fixed `TAG_LIST_2D` without asking the level how big it is. The row stride is
> therefore always 100 — and getting that wrong wraps rows into each other, which looks like a
> working automap with ghosts on it.

> **A square off the edge of the map reads as *visited* — but only on a level that has been
> entered.** `TAG_LIST_2D::Get` returns 1 outside its bounds ("outside boundaries is tagged"),
> which is what keeps the border from drawing as unexplored. But `IsVisited` checks for a missing
> bitmap *first* and returns false. So the identical query answers differently depending on
> whether the level has ever been walked, and both answers are the reference's.

> **`SetVisited` allocates before it range-checks.** A level whose only recorded step was off the
> map still ends up in the savegame with an empty bitmap rather than no entry at all.

> **Level 255 can hold trigger flags and can never hold a visited square.** `VISIT_DATA` is a
> fixed `TAG_LIST_2D*[MAX_LEVELS]` tested with `level >= MAX_LEVELS`, while `EVENT_TRIGGER_DATA`
> is a `CArray` that grows. That difference is exactly what lets global events record at
> `GLOBAL_ART` — and it means the two structures disagree about how many levels exist.

> **A bitmap is one byte longer than the squares need.** `(w*h >> 3) + 1` = 1251, and the `+1` is
> unconditional, so a writer that computed a tight size would be one short of what the reader
> expects.

> **These records are sparse where the trigger flags are dense.** `VISIT_DATA` writes a
> (level, count) pair per slot and a bitmap only where the count is non-zero, so the level number
> travels with the record and nothing is positional. The opposite convention from
> `EVENT_TRIGGER_DATA`, in the same file, written by the same function.

Marked in three places, matching the reference: the starting square (`setPartyLevelState`), each
square the party arrives on (`UpdatePartyMovementData` — the arrival, not the departure), and a
teleport destination.

##### Event trigger flags — and `OnceOnly` finally meaning something

The first of the five things a save could not carry, and the one that was doing visible damage
before anyone tried to save: **`OnceOnly` was read and never consulted**, so a once-only event
re-fired every time the party stepped on its square. The reader has kept the flags since Phase 1;
nothing ever set one.

> **An event is marked the moment its trigger test passes — before it draws anything.**
> `MakeSureEventIsReady` calls `markEventHappened` between `OnTestTrigger()` returning true and
> `OnInitialEvent()` being called (`CProcinp.cpp:365`). Not on completion. So an event the player
> escapes from, chains away from, or abandons mid-screen has still *happened*, and `OnceOnly`
> means "offered once", not "completed once". That is the difference between a design that can
> strand a player and one that cannot, and it is a one-line difference in where the call sits.

> **A spent once-only event is not a *suppressed* one.** The reference drops out of
> `OnTestTrigger` before `EventShouldTrigger` is reached, so it gets **no not-happened chain**
> either — it is a cell with nothing on it. A port that folded this into the ordinary suppression
> path would fire the not-happened chain every subsequent step, which is worse than the bug it
> replaced.

> **Global events are recorded one past the last level.** `GLOBAL_ART` is `MAX_LEVELS` (255), so
> the global event list shares the per-level flag table rather than having one of its own, and
> `CheckLevel` grows the array to reach it. A design with a level 255 would collide.

> **`HasEventHappened` is an equality test, not a flag test.** It asks
> `eventResult == HasHappenedAtLeastOnce`, so any other value reads as *not* happened. Treating
> the field as "non-zero means yes" would agree on every file the engine wrote and disagree on
> any it did not.

> **The projection has to be dense.** `EVENT_TRIGGER_DATA` is a `CArray` indexed by level that
> `CheckLevel` grows with empty entries, so a flag on level 3 writes four records. A sparse
> projection reads back with every level shifted by the gaps.

Zone step counters (`STEP_COUNTER`, sixteen per level) live in the same record and are tracked
alongside, since they cost one array and would otherwise be a second visit to the same structure.

##### The save and load screens — and the half of saving that is missing

Both screens run: the ten slots, the wording, the disabling, and the way out. They are the first
two of the party menu's remaining nine, and were taken first because their **file format is
already finished** — which turned out to be the more interesting finding.

> **Save and load are one menu, twice.** `SaveMenuData` and `LoadMenuData` are two
> `MENU_DATA_TYPE`s pointing at a single `SaveGameMenu` array (`GameMenu.cpp:1113`), so the slot
> letters cannot drift apart between the screens and neither can gain an entry without the other.

> **How many saves a player may keep is a fact about a menu table.** `MAX_SAVE_GAME_SLOTS` is
> `#define`d as `SaveGameMenuItems-1` (`GameMenu.h:296`) — the constant is derived from the array,
> not the other way round.

> **Only the load screen darkens anything.** Saving over an occupied slot is offered without
> comment; there is no "are you sure". Loading from an empty one is refused, so `OnUpdateUI`
> turns each fileless slot off and the line above the menu changes to "THERE ARE NO SAVED GAMES
> AVAILABLE" when none of them has one.

> **Both screens pop unconditionally.** A failed save returns to the menu exactly as a successful
> one does — `miscError` is set and nothing looks at it here — so there is no retry loop and a
> player who picks a slot always lands back where they came from.

**Saving itself is refused, deliberately.** This is worth stating plainly because the surrounding
work makes it easy to assume otherwise:

> **Both ends now meet in the middle.** `SaveGameReader` and `SaveGameWriter` round-trip a `.pty`
> byte for byte — Phase 1's exit criterion — and `SaveGameProjection` turns a *game in progress*
> into one. **Saving works**; see §saving works.

> **A lossy save would have been worse than none**, which is why this refused for four rounds
> while the missing state was built: a `.pty` with an empty visited map is a perfectly valid file
> that reads back into a party that has forgotten where it has been and will re-fire every event
> it resolved. Invisible until much later, and indistinguishable from a design bug. The
> `Untracked` list is kept — now empty — so a future gap has a declared place rather than being
> found in a diff.

The slot screens read the real folder — `Saves` beside the design, which is what `rte.SaveDir`
resolves to — so the occupied slots a player sees are the ones the reference wrote.

##### The party menu, and training

The training hall's YES now opens something. It turns out not to be a training screen at all:

> **The training hall has no inner screen of its own — it pushes the game's top-level menu.**
> `TRAININGHALL::OnKeypress` case 1 is `PushEvent(new MAIN_MENU_DATA(this))`, the same twelve-entry
> screen the game opens at startup, with the hall as its parent. The entire difference is what
> lights up: TRAIN and CHANGE CLASS are dark unless a training hall pushed it, and BEGIN
> ADVENTURING pops back to the hall instead of loading the starting level. So the screen behind
> the hall is a *shared* one — which changes its priority, since save, load and the character
> screens all hang off the same menu.

> **Two twelve-entry tables sit side by side in the source and only one is live.** One is
> commented "original order" and leads with CREATE and DELETE; the live one leads with ADD and
> REMOVE. The branch numbers in `OnKeypress` happen to agree for the four entries that matter,
> so reading the wrong list looks fine right up until it does not.

> **The keys are split the opposite way from the inventory.** `VMenuHPartyKeyboardAction` gives
> the menu the vertical keys and the party the horizontal ones; the inventory's
> `HMenuVInventoryKeyboardAction` does exactly the reverse. Two screens, two conventions, named
> almost identically.

Three of the twelve run — VIEW, TRAIN, and the two exits. The rest are character creation, the
save and load screens and the class change, each a screen rather than a command. **CHANGE CLASS
is dark** rather than guessed at: it depends on `CreateChangeClassList`, which is not ported.

The enable rules are the substance of the screen, and TRAIN's is three conditions rather than one:
ready to train, able to pay, **and** holding a baseclass this particular hall teaches. They are
recomputed on every pass, because TAB changes who is standing at the counter.

> **The player gets no reason, ever.** The reference shows the same dark entry for "you lack the
> experience" and "this hall does not teach your class". The port keeps a
> <code>TrainingRefusal</code> so the two are distinguishable in code and in tests, and still shows
> what the reference shows.

**Training itself** (`CHARACTER::TrainCharacter`) is ported: the fee, the levelling, the hit
points and the announcement.

> **One level per visit, however much experience is banked.** `TrainCharacter` passes a
> `maxLevelGain` of literally 1, so a character sitting on four levels' worth must visit four
> times and pay four times. The entitlement is deferred, not lost.

> **Hit points are rolled, not tabled — one roll per level crossed.** The gain comes from the
> baseclass's own per-level dice, so training twice from the same save gives different results,
> and a two-level jump rolls twice at each level's own dice rather than once. That is why the
> roller is a parameter rather than a static call, and it is what makes the rule testable at all.

> **The hall's advertised level range is decoration.** `LocateTrainableBaseclass` matches on the
> baseclass id alone and no caller looks further, so a hall listing "levels 1 to 3" trains a level
> 9 character just the same. The fields are read, written and never consulted.

> **Training heals.** `hitPoints` is set to the new maximum outright — not topped up by the gain,
> set. A character who walks in on 3 of 10 walks out on the full new total.

Not ported, and named: the **constitution bonus** on each roll (`DetermineHitDiceBonus` needs the
adjusted ability scores, so a tough fighter is currently short by it), the thief-skill and
spell-ability recalculations, and the initial magic-user spell pick — which the reference itself
disables with a hard `PickSpells = FALSE` two lines after computing it.

> **`Training` takes two functions, not a `LoadedDesign`.** The rules need a baseclass table and a
> level cap; `LoadedDesign` needs a design on disk to exist at all. A rule that cannot run without
> loading a game is a rule nobody checks, so the dependency is inverted and the eighteen tests
> here run on fixtures.

##### The ready rules, and two conversion tables that are not the same table

READY now puts an item where its own database record says, and refuses what the reference
refuses. Getting there turned up a constant that had been quietly wrong since the inventory was
first read.

> **`NOTRDY` is a packed word, not zero — and zero is the weapon hand.** `Inventory.NotReady` was
> `0`. The reference's `NotReady` is `BASE38('N','O','T','R','D','Y')`, and a stored `0` converts
> to `WeaponHand`. Every carried item in every shipped savegame is worn, and two of them are
> stored as a bare `0`, so the port was reading two readied weapons as empty hands. Nothing in the
> file looks wrong, no round-trip notices — the bytes are preserved perfectly either way — and no
> unit test written against the port's own assumptions could have caught it. What caught it was
> reading the constant's definition while porting a different function.

> **A carried item and a database record convert by *different tables*.**
> `itemReadiedLocation::Synonym` (`Items.cpp:727`) converts a carried `ITEM`'s slot;
> `Items.cpp:2495` converts an `ITEM_DATA` record's. They agree on nine of eleven ordinals. They
> disagree on **3** — `Hands` in the database, `AmmoQuiver` for a carried item — and the carried
> table runs to 16 rather than 10, reaching `CANNOT`, `PACK` and five body parts no item record
> can name. `Hands` does not appear in the carried table at all. Crossing the two swaps gauntlets
> for quivers and has no other symptom. `ReadiedLocation.Convert` and `ReadiedLocation.Synonym`
> are now separate, and a test walks both tables against each other so the divergence has to stay
> deliberate.

> **The carried conversion has no version gate.** The database's is gated; this one runs on every
> load at every version, which is why a 2.81 save and a 3.65 save can be read the same way.

The refusals (`ITEM_LIST::CanReadyItem`, `Items.cpp:1460`) are ported whole: money, an empty
stack, an item the design no longer defines, more than two hands, a `CANNOT` slot, an occupied
slot, a two-hander wanting a full hand, and a hand already holding a two-hander. Each has its own
`ReadyRefusal` value where the reference collapses most of them onto `UnknownError` and shows
nothing — the difference between "that is a gem" and "your hands are full" is the entire content
of the message, and a screen that says neither is indistinguishable from a broken one.

> **An item already worn is never refused.** The reference tests that second, before any of the
> hand rules, which is what lets a two-hander be put down again despite the rules that stopped its
> neighbour going on.

> **The reference asks "is this slot taken?" two different ways, three lines apart.**
> `GetReadiedCount` matches on the *database record's* slot; `GetReadiedItem` matches on the
> *carried item's own*. They diverge exactly for an item the engine placed somewhere its record
> does not name — which the engine itself can do. Both are ported as they are, under names that
> say which is which (`ReadiedCount`, `WornIn`), because they answer different questions.

Two deliberate divergences, both flagged in the source:

> **An item whose slot is `CANNOT` is refused rather than worn.** `itemUsesRdySlot` returns false
> for it, and the reference then skips the whole slot check and readies it anyway — at a location
> named `CANNOT`. Refused here.

> **A carried item whose record is gone is skipped, not dereferenced.** `GetReadiedCount`
> dereferences the lookup without checking, so a design that dropped an item its savegame still
> carries crashes the reference. Skipping gives the same count, alive.

Not ported: the class check (`IsUsableByClass`) needs the baseclass tables, so for now any class
may wear anything; and the twelve `ReadyWeaponScript`-family hooks, which are where an item's
special abilities switch on and off.

##### The inventory, on screen

`ITEMS` in a shop or a vault now opens the inventory instead of naming it, and closing it returns
to the service underneath. The first **nested** screen the runner has — and the nesting is what
made it interesting:

> **The inventory replaces its parent's menu; the character sheet draws over one.** The reference
> gets the return for free by pushing an event and popping it, so it never has to think about what
> was underneath. This runner presents one event at a time, so closing the inventory has to
> *rebuild* the parent's menu by hand — which is why it remembers which service pushed it rather
> than assuming.

> **Paging lives in the runner, not in `ItemsForm`.** The form lays out a fixed number of rows and
> has no notion of a page, and the treasure screen shares it — so NEXT and PREV re-populate with a
> slice instead of inventing a paging model in a class with two callers.

> **A row carries its item's own index.** Once the list pages, a row's position on screen is not
> the item's position in the pack, so READY on page two has to act on the ninth item and not the
> first. That is the whole reason `InventoryRow` has an `Index` at all.

> **Horizontal menu, vertical inventory.** Up and down move the item cursor, Page Up and Page Down
> turn the page, and left and right fall through to the menu underneath
> (`HMenuVInventoryKeyboardAction`, `RunEvent.cpp:748`). This is the only screen where the arrow
> keys are split between two things at once, and the cursor it moves is what a command acts on.

Two things written here a round ago were wrong, and are corrected:

> **The paging does not wrap.** `nextCharItemsPage` stops on the last page and `prevCharItemsPage`
> stops on the first (`Disptext.cpp:577`) — NEXT at the end does nothing at all, with no feedback.
> That reads as a stuck key, but the menu entry and the Page Down key share the helper and would
> otherwise disagree with each other.

> **The screen had no row cursor at all.** `ItemsForm.Select` existed and nothing ever called it,
> so READY always acted on row zero. The paging test passed because it checked the page rather
> than the row, and the readying test passed because row zero of page two *is* the ninth item.
> Two tests agreeing on a screen with one reachable row is not coverage. The cursor
> (`party.activeItem`) wraps within the page and clamps onto the shorter final page.

##### The shared inventory, as ported

`Inventory` — the data behind `ITEMS_MENU_DATA` (`RunEvent.cpp:7843`), which **the vault, the shop
and the camp all push, and so does combat**. Taken first among the inner screens for that reason:
it is the one with the most callers per screen built.

Four of its fourteen commands run — READY, NEXT, PREV, EXIT. The other ten each want machinery
this port does not have (a trade partner picker, the shop's price list, the scribe rules) and are
named.

> **The screen shows a *word*, not a tick.** `readyLocation` names a slot — WEAPON, SHIELD, ARMOR,
> HANDS, HEAD, WAIST, ROBE, CLOAK, FEET, FINGER, QUIVER, and seven more the engine can reach — so
> an inventory line says where a thing is worn rather than whether it is.

> **Decoding that field needed a matcher, not a decoder.** `Base38` folds six characters into a
> `DWORD` and is **not invertible in general** — nothing constrains a stored value to the words
> that exist. `ReadiedLocation.WordFor` therefore packs each known word and compares, which is
> exact and keeps one encoder rather than two that can drift. A value naming no slot honestly
> shows nothing. Both the packed form and the legacy ordinal are accepted, because a savegame can
> hold either — and the shipped corpus holds both, which is pinned in `InventoryCorpusTests`.

> **Cursing only blocks taking a thing off.** `ToggleReady` refuses to unready a cursed item, and
> never refuses to ready one — which is rather the point of a cursed item, and is the same rule
> that stops one being dropped (`CanUnReady`, `Items.cpp:1631`).

> **Two menu entries are both called `EXAMINE`** — one for ordinary items, one for special items
> and keys, which are different lists behind the same screen. Only the reference's comment beside
> the table says which is which.

##### The town-service shells, complete

`TAVERN`, `SHOP`, `VAULT` and `TEMPLE` — **all seven town services now present their menus and run
their exits, and 34 of the 44 event types execute.** The shell layer is done; what is left behind
it is the dozen inner screens, which is the honest boundary for estimating the rest.

Three things the last four added:

> **The temple is the only town service with two screens of its own.** It opens on a press-enter
> welcome showing the event's `Text`, and its menu shows **`Text2`** (`RunEvent.cpp:12361`). Two
> text fields for one event, with nothing but the state to say which is on screen — and the second
> screen calls `setMenu` again, which *replaces* the menu rather than adding to it. This port's
> `SetupFixedMenu` only appends, so the second screen had to reset first; the test caught it as a
> menu with eight entries where seven were expected.

> **`FIGHT` is the one town entry that chains rather than pushing a screen, and the one that
> explains itself.** A tavern whose `fightChain` names nothing says "Everyone runs away. There's no
> one to fight!" (`:9782`) — everywhere else a town service simply stays put, which is
> indistinguishable from a dropped keypress.

> **The vault spells `forceExit` as `ForceBackup`.** Three spellings across four event types behind
> one virtual `ForcePartyBackup`, as §camp already noted — this is the fourth.

One process note worth recording, because it has now cost three edits: the test asserting "an
unrun event is named" kept naming whichever type ran next. It now names `PlayMovieEvent`, which is
blocked on the **FFmpeg adapter** rather than merely unported — a test like that wants a subject
waiting on a whole subsystem, not the next thing on the list.

##### Camp and the training hall, as ported — and what the town services actually are

`CAMP_EVENT_DATA` and `TRAININGHALL`. **30 of the 44 event types now execute.** More useful than
either screen is what reading all seven together established, because it changes how the rest
should be estimated:

> **Every town service is an outer screen over inner screens, and the inner ones are the work.**
> `CAMP_EVENT_DATA` has no screen at all — it pushes `ENCAMP_MENU_DATA` and, on return, backs the
> party up and chains. That menu's twelve entries push **six separate event classes** (save, load,
> magic, rest, alter, journal). `TRAININGHALL` is a two-item yes/no whose YES pushes the
> character-picking menu that does the actual training. `VAULT_EVENT_DATA` is a treasure screen
> over an items menu. So "port the town services" is not seven screens; it is seven shells and
> perhaps a dozen inner ones, and the shells are cheap while the inner ones are not.

What is running: camp's menu with VIEW, TALK, ZAP and EXIT live and the other eight named; the
training hall's welcome with NO live and YES named.

> **`forceExit` does not mean "leave immediately".** It is `ForcePartyBackup()`, and it sends
> `TASKMSG_MovePartyBackward` when the screen closes — "step the party off the square on the way
> out", so walking into a shop does not leave them in the doorway re-triggering it. **Four event
> types spell the field three different ways** — `ForceExit`, `ForceBackup`, `forceExit` — behind
> the one virtual, which is why it reads like four unrelated flags in the readers.

> **A camp's TALK chains to a *global* event, not a level one.** It reads the active character's
> `TalkEvent` and pushes it through `PushGlobalEvent` after checking
> `AllowedAsGlobalEvent` (`RunEvent.cpp:9302`) — and below 0.681 it refuses outright, with the
> comment "not safe, didn't work properly in old versions".

##### The first town service, as ported

`SMALL_TOWN_DATA` — the hub the other six hang off, and the cheapest of them: a horizontal menu of
six destinations and an exit, with nothing about the party changing. **28 of the 44 event types now
execute.** It is worth taking first because it establishes the shape the rest of the tail needs.

> **A destination that names no event is a no-op, not a fallback.** The reference pushes a
> `DO_NOTHING_EVENT` (`RunEvent.cpp:10723`), which returns to the town screen — so choosing SHOP in
> a town with no shop leaves the player exactly where they were. Only EXIT runs the town's own
> chain. Every other menu-bearing event in this port falls back on the ordinary chain when its
> target is missing; this one does not.

> **Escape selects EXIT rather than cancelling** (`MapKeyCodeToMenuItem(KC_ESCAPE, 7)`), so a
> player who backs out of a town still runs its chain. The port's menu had no key→item mapping at
> all; it has a general one now, reset per event so no other type's Escape changes behaviour.

> **The menu is horizontal**, which no other event this runner presents is — and its shortcuts are
> the table's own indices rather than first letters, because `TEMPLE` and `TRAINING HALL` collide
> on `T`. The second uses index 9, the `H` of "HALL".

`PUB` is the menu's name for the tavern chain. The field is `TavernChain` and the label is not, so
a reader matching them up by name finds six fields and seven labels and one that agrees with
nothing.

##### A logic block, running, as ported

`LogicBlockRun`, `GameLogicBlockHost` and the `EventRunner` case — **`LogicBlock` now executes**,
which takes the event layer from 26 of 44 types to 27 and closes the most frequent inert one: 52
occurrences, more than any other unimplemented type.

The three halves were built separately and unwired on purpose — gates, then inputs, then actions —
because a gate network fed all-false inputs takes a branch rather than failing visibly. This joins
them, and two details only became testable once it did:

> **The working slots are filled as the network reads them.** A terminal's parameter can name an
> earlier terminal's result through `&A`‥`&L`, so the slot array has to be updated *during*
> evaluation rather than after it. The actions then see the whole array, which is how a design
> writes a computed value into an attribute.

> **A logic block is the only event type that draws nothing at all.** No text, no menu, no
> keypress — it finishes inside `Begin` and never reaches `Handle`, the same shape as a question
> whose options are all empty. And its chaining is not the ordinary chain: only
> `LogicBlockChaining.Always` defers to `chainEventHappen`; `OnResult` replaces the block with its
> own target and `Never` ends the run.

Two seams the host had to invent, both recorded rather than hidden:

> **A logic block names a quest; everything else in the engine uses its id.** `questData.GetStage`
> takes the name, where a `QUEST_EVENT_DATA` carries a packed id and `WorldState` is keyed by it.
> `GameLogicBlockHost` takes the design's name→id map at construction — the only place the two
> meet.

> **`global_asl` and `temp_asl` are two lists in the reference and one store here.** They are kept
> apart by a key prefix that a design cannot write, rather than merged, because a design that
> writes a temporary and reads a global expects to see nothing.

##### A logic block's actions, as ported

`LogicBlockActions` — `ProcessLBAction`'s twelve action types (`RunEvent.cpp:14157`), the result
gate in front of them, and an `ILogicBlockActionHost` extending the input one with the six things
an action writes. **All three parts of `LOGIC_BLOCK_DATA` are now ported**: the gate network, the
inputs and the actions. What is left is wiring the event into the runner.

The write half is the smaller one — mostly "insert or delete an attribute" — and what makes it
worth its own file is that **every parameter is a packed string**, a key and a value around an
`=`, sometimes with a level or a character selector in front, each layer a separate grammar.

> **`LBAT_setIconIndexByName` does not substitute its parameter.** Every other action calls
> `LBsubst` first; this one reads `*param` raw (`:14262`). Almost certainly an oversight, and
> reproduced — a design that worked around it by not using `&` there would break if it were
> "fixed". It also matches its character **case-sensitively**, where the selector grammar three
> cases above uses `CompareNoCase`.

> **A GPDL *action* runs with no arguments.** `ExecuteScript(*param, 1, NULL, 0)` against the input
> side's `(*param, 1, w, 6)` — so an action script cannot see the terminals an input script can.
> Two lines apart in behaviour, two hundred apart in the file.

> **The two sides check the character index differently.** The input side tests
> `(n < 0) || (n >= party.numCharacters)`; the action side tests only `n < 0` (`:14237` against
> `:13807`), so an index past the party would index out of bounds there. No selector form can
> produce one, which is presumably why it has never been noticed — this port bounds-checks both.

There is **no `removeTempASL`**: the enum has ten attribute actions and only three removals, so a
design can write the temporary store and never clear a single key from it.

##### A logic block's inputs, as ported

`LogicBlockInputs` — `ProcessLBInput`'s sixteen terminal types (`RunEvent.cpp:13777`), the two
string grammars under them, and an `ILogicBlockHost` naming exactly the state they read. This is
the half `LogicBlock` was deliberately left without: the gate network was ported and tested first,
*unwired*, because all-false inputs would have made every one of the corpus's 52 blocks take a
branch rather than fail visibly.

Fourteen of the sixteen run. `LBIT_RunTimeIf` needs the runtime keyword table (`GetDataSTRING` and
its seven width-specific siblings) and the two GPDL terminals need a script runner passed in;
both throw with a citation, as the sub-opcodes do.

> **`LBsubst` hangs the reference on an ampersand that names no slot.** Its loop advances `col` in
> the `else` of `if (p[col]=='&')` and in the substitution branch, and **nowhere else** — so an
> `&` whose successor is outside `A`‥`L`, or a trailing one, spins forever on the same character.
> A logic-block parameter containing ordinary prose — `"Bell & Dragon"` — locks the original up.
> This port advances past it and keeps the character, which is the only non-hanging reading and
> plainly what the code intends. **It is a deliberate divergence and the only one in the file**,
> because reproducing the behaviour means reproducing a freeze.

> **`SplitLevelKey` is `/<digits>/<key>` and falls back to the *current* level, not level 0.** A
> first draft read it as "leading digits, then the key", which would have sent every unqualified
> attribute to level 0 — and the citation is what caught it. Two quirks came with the correction:
> a leading slash with no closing one falls through to the current level, and **non-digits inside
> the number are skipped rather than terminal**, so `/1a2/key` reads as level 12.

> **Truth is "not empty", and it reaches the inputs too.** `LBIT_partySize` renders through
> `Format("%d")`, so an empty party yields `"0"` — which is **true**. A port that returned a
> boolean, or that treated `"0"` as false, inverts the branch on every block that tests party
> size.

An unrecognised terminal type is **not** an error that stops the block: the reference logs
"Bogus Logic Input-\<letter\> Type" and leaves the result empty, so the terminal reads false and
the block still runs. Throwing there would make a design with one bad terminal unplayable where
the original merely misbehaves — the same reasoning that keeps `EventBodyReader` returning null
rather than guessing.

##### The savegame tail, as ported — and Phase 1's exit criterion

`SaveGameTailReaders` and `SaveGameTailWriters` — the `ACTIVE_SPELL_LIST` and the seven
`Save`/`Restore` pairs a savegame ends with. **Both were unread when this began**, and porting the
reader and the writer together is what closes the last gap: a whole `.pty` now reads and writes,
and **Phase 1's round-trip exit criterion is met for every file kind the format has.**

> **That is a claim about *files*, not about saving.** A `.pty` read from disk writes back
> identically; turning a *game in progress* into one is a different problem and is not solved —
> see §the save and load screens. The two are easy to conflate, and conflating them would make
> "savegames are done" true of the format and false of the game.

The pairs turned out to be as small as the storing branch suggested — a count, a name and an
attribute list per record — but three things in them were not:

> **`Save` writes attributes through the ASL's *save* path and `Restore` reads them with the
> ordinary `Serialize`.** The save path skips read-only entries and counts the filtered set. So a
> savegame's attribute lists are a **subset** of the design's *by construction*: the save carries
> what gameplay changed and the design supplies the rest. That asymmetry is the point of the
> format, not a defect in it.

> **A `LEVEL_STATS`'s own length is decided by an attribute it carries.** `Save` inserts
> `__LEVEL_STATS_VERSION` into the attribute list, writes the list, then deletes the entry again —
> and the two trailing tables are gated on the value. Nothing else in this format decides how many
> bytes follow from inside its own ASL, and a writer that simply wrote the attributes it was handed
> would emit a version of 0 and then two tables the reader never looks for.

> **`COMBAT_TREASURE_DATA` is items-then-money; the `COMBAT_TREASURE` *event* is
> money-then-items.** Four lines apart in the same file, names one word different, layouts
> transposed. This is §the event layer's "member names are not types" rule again, and it is what
> the first draft got wrong — the tail read cleanly through the spell database and the global ASL
> and then drifted, which is exactly where a missing money sack would put it.

And one finding that is a defect, stated plainly:

> **`ACTIVE_SPELL`'s two branches disagree about field order, and the reference cannot round-trip
> its own output.** Storing writes `Lingers`, `casterLevel`, `lingerData` (`Spell.h:1288`); loading
> reads `Lingers`, `lingerData`, `casterLevel` (`:1310`). A `SPELL_LINGER_DATA` is never
> zero-length — a flag and two counts at minimum — so the orders cannot coincide, and a save the
> reference wrote with any active spell in it does not read back correctly *in the reference*.
> This port **writes the loading order**, deliberately setting aside the rule that a writer follows
> the storing branch, for the reason the rule exists: what matters is that the file loads. Neither
> shipped save has an active spell, so no corpus file can tell the difference — which is stated as
> a test rather than left implied.

One earlier claim had to be withdrawn:

> **"Every shipped design's wall-override tables are empty" was true of designs and false of
> saves.** A savegame's `LEVEL_STATS` carries one with sixteen entries of which three are present,
> the rest bare `-1` placeholders. The reader kept only the present rows in a dictionary, so the
> writer had to refuse such a table — and then met one. `WallOverrides` now holds the entries in
> wire order, placeholders included, with the dictionary as a projection.

##### The savegame body, as ported

`SaveGameWriter` — the whole `PARTY` record and the four structures after it, for both shipped
`.pty` files, with write-read-write byte identity.

**It stops where the reader does, and that boundary is the reader's.** A save continues past the
vaults with an `ACTIVE_SPELL_LIST` and seven `Save` calls, none of which has a reader — so there is
nothing in hand to write. What that leaves is the last gap in Phase 1, and it is **much smaller
than its description suggests**:

> **`Save` and `Restore` are a matched pair that writes almost nothing.** Each is just the object's
> ASL through the attribute list's save path — `ITEM_DATA::Save` is one line, and so are
> `MONSTER_DATA::Save` and `SPELL_DATA::Save`. The database-level ones add a count and a name per
> record so the loader can match objects against the design it is loading into. Only
> `GLOBAL_STATS::Save` adds anything else, a trailing `combatTreasure` item list, and its `Restore`
> reads it back symmetrically. `AslWriter` already has the save path they need. Seven "unexplored
> methods" is closer to seven one-liners and a name table.

Three things the writer had to get right that the reader's own tests could not have found:

> **A `VISIT_DATA` slot's level field is its loop index, not a stored value** (`Party.cpp:4584`).
> That is what lets the 254 empty slots be reconstructed at all — the reader keeps only the visited
> levels, and the rest are recoverable precisely because nothing but their position was ever
> written. All 255 go out regardless, a fixed 2,040 bytes before any bitmap.

> **The vault count written is the constant, and so is the number of vaults.** The storing branch
> writes `MAX_GLOBAL_VAULTS` and loops to it whatever the game holds, and the *loading* branch
> `die`s on any other count before clamping. A save with fewer occupied vaults is padded.

> **The version stamp is outside the compression and the body is inside it.** Eight bytes of
> `double` go on the raw file, then `car.Compress(true)` (`Dgngame.cpp:431`) — the sixth container
> framing, and the only one where compression begins after a bare scalar rather than after a magic.

One reader gap closed with it: `TRIGGER_FLAGS`'s first `int` — the one the engine itself calls
`eventStatusUnused` — was read and discarded, and is written unconditionally.

##### The sixth record type, as ported

`LEVEL` (`LevelFileWriter`) — **the last of the six**, and the one an editor actually edits. All
eighteen shipped levels round-trip whole, with write-read-write byte identity, which makes this
also the widest test the event writers get: 4,705 events written in place in a chain that has no
length prefixes anywhere.

> **A level file is never compressed, even in a design whose databases are.** `LoadLevel`
> constructs a `CAR` and leaves `ar.Compress(true)` commented out (`Level.cpp:2186`), so the
> payload is plain archive primitives at every version. The compression decision is per file
> *kind*, not per design — the same "constructed a `CAR` does not mean `CAR` bytes" distinction a
> `.chr` file turns on, arriving from a third direction.

> **The dimensions go out width-then-height while being declared height-then-width**
> (`Level.h:58`), and both are `BYTE`. Writing them in declaration order transposes every
> non-square level *silently* — the grid still reads back, with the wrong shape. Most of the corpus
> is square, so the test has to seek out the one level that is not.

Two reader gaps closed with it, both the shape this port keeps finding: `m_level` was read and
discarded, and the event chain kept only the bodies. An unrecognised ordinal is **four bytes and no
body**, so dropping one shortens the chain and every later event's position with it —
`LevelFile.Entries` now holds the chain as it sits on the wire, tags included, with `Events` left
as the body-only projection the engine's `EventLookup` wants.

**Two coverage gaps stated rather than papered over.** No cell in any of the eighteen levels sets
either of the background byte's two display flags, so the bit-packing has a fixture and no real
example. And no level's step-event table is in the pre-1.0210 shape, which is refused rather than
written — its slots are 8 where the modern table's are 255, and the reference's own table is a
fixed array of the full size.

##### The fifth record type, as ported

`GLOBAL_STATS` (`GlobalStatsWriter`) — **the record a design's `game.dat` is**, and the thing that
turns the character writer into a file rather than a fragment. Five of the six record types now
write. It is the *widest* record rather than the deepest: a run of scalars, a `LOGFONT` blit, two
picture-import lists, two title sequences, an ASL, eleven art slots, the sound queues, three record
lists, the characters, the level table with its cell contents, the currency and difficulty
configuration, the global event list, the journal and a spellbook. `GlobalTailWriters`,
`GlobalStatsTailWriters` and `CellContentsWriters` arrived with it.

> **It writes its own version as its first field.** No other record does, and it is what lets the
> loading branch tell one framing from another: it reads eight bytes and decides — the magic means
> "a version follows, then turn compression on, then the version again", anything else means those
> eight bytes *were* the version as a `double` (`GlobalData.cpp:4336`).

> **`WrittenVersion` is 5.26 — the first record whose own gates push past the embedded
> `PIC_DATA`'s 5.24.** Two fields force it. `creditsData` is written unconditionally and read only
> at **5.25** and above, so a file stamped 5.24 would have a whole title sequence read as one
> string. And `CharViewFrameVPArt` is gated `version >= _VERSION_526 || car.IsStoring()`
> (`:4500`) — **the storing side spelled out as unconditional inside the condition itself**, which
> is the plainest statement of the no-version-gates rule anywhere in the codebase.

**Three reader gaps surfaced writing the inverse**, all of them the same kind now:

> **Nine fields were read and discarded** — the retired `startEquip` slot (a literal zero in the
> reference), the four time deltas, `StartDarken`/`EndDarken`, and the whole `CursorArt`
> `PIC_DATA`. Every one is written unconditionally, so every one had to be kept.

> **The art block had to become positional.** The reader appended slots as their gates opened, so a
> ten-entry list from a 2.53 design and an eleven-entry one from 5.28 had *different slots at the
> same index*, and the writer could not tell which was missing. It now always returns all eleven
> with the absent ones empty — the same fix `SpellRecord.Scripts` needed, and for the same reason.

> **`WALL_OVERRIDES` loses where an absent row sat.** The table is a count then that many entries,
> each prefixed by a row number, with `-1` meaning "absent, no payload" — but the reader keeps only
> the present rows, in a dictionary. `EntryCount` is now kept so the writer emits the same number,
> and a table where the two disagree is refused rather than written with the placeholders bunched
> at the end. No shipped design has a non-empty table at all, which is why it has never mattered.

One divergence is stated rather than fixed. The reference normalises every picture import before
writing — forcing `picType` and running `SetDefaults()` — and for a file it produced those are
no-ops, which the corpus confirms. The exception: `SetDefaults` also sets a small pic's
`RestartFrame` to 1, and **that field only reaches the wire at 5.24**. A 2.53 or 3.55 design never
had one to read, so this writer emits the 0 it saw where the reference would emit 1. Reproducing it
faithfully needs the runtime viewport dimensions, which this layer does not have.

##### The event storing branches, as ported

`GameEventWriter`, `SimpleEventWriters`, `ContentEventWriters` and the `EventBodyWriter` dispatch —
**the shared blocker for both record types still unwritten**. A `GLOBAL_STATS` storing branch ends
with the design's global event list (`GlobalData.cpp:4556`) and a `LEVEL` is mostly events, so
neither could be written until these could.

**30 of the 44 types write, and between them they cover every event in every shipped level** — all
4,705 of them, across the 18 files of the two designs that have any, each written, read back and
written again to the same bytes. **The corpus has nothing left to say about the event layer.** The
order the types were taken in came from measuring rather than guessing:

> **The distribution is extremely skewed, and the first tranche was chosen from the wrong end of
> it.** The eight small subclasses grouped in `SimpleEventReaders` looked like the obvious start —
> they are short, self-contained and share a file. They turned out to be **53 of 575** events, 9%.
> A histogram over the two designs that ship levels — 4,705 events across 18 files — showed
> `TextStatement` alone at **3,451**, 73% of everything. Porting it plus the six next-commonest
> took coverage from 9% to 98%. The lesson generalises past this port: when work is a long tail of
> similar items, count them before choosing an order, because "small and self-contained" and
> "frequent" are unrelated properties.

The ten the corpus reaches least often were also the awkward ones — a tavern and a shop appear
**once each** in the entire corpus, so there is no second example to check a guess against and the
storing branch is the only source. The last of them turned up the most surprising shape in the
event layer:

> **A `TAVERN` always writes 255 tales.** Not the count of tales it holds — the reference writes
> `ar << MAX_TALES` and then loops to `MAX_TALES` (`GameEvent.cpp:9668`), so a tavern with three
> tales emits 255 of them and 252 blank sentinels. Writing the list's own count instead produces a
> file whose tale count and tale bodies disagree, and the reference's loading branch `ASSERT`s the
> count lies between 10 and 255 — a small one is a shape it believes impossible.

The **fourteen types that remain** appear **zero** times in any shipped design, so the corpus can
say nothing about them: they want `Export(JWriter&)` read alongside `Serialize`, which is a second
independent description of the same fields and is how `PASSWORD_DATA::matchCase` was pinned as
exported but not serialized. `EventBodyWriter.CanWrite` reports what is covered so a caller can
check a whole list before starting a file, and the dispatch throws a citation for the rest rather
than writing a truncated body — a body has no length prefix, so writing nothing would corrupt every
event after it.

> **`COMBAT_EVENT_DATA` was the one type standing between the port and a complete level**, and the
> reason it took a section of its own is that three of its lists sit *outside* the storing branch:
> `monsters` after the encounter's fields, and inside each monster its `items` after the money —
> the second past where a grep window usually stops. Two refusals came with it, both familiar: a
> monster entry from below 0.740 has no money sack in this port's model at all (unlike a monster
> *record*, where the reference has a default-constructed one to write), and one carrying an item
> by its pre-0.998101 numeric id cannot be named.

> **A field was named for the wrong thing.** The reader called `turningMod` — an
> `eventTurnUndeadModType` — `Terrain`. Same width, so no byte ever moved and no test could have
> caught it; nothing consumed it either. Writing the inverse is what surfaced it, because the
> writer cites `GameEvent.cpp:6973` and the citation contradicted the name. That is the third time
> transcribing the storing branch has found a reader defect invisible to the reader's own tests.

> **The event's ASL blocks are not named `…_ATTRIBUTES`.** They are `EVENT_DATA_ATTR` and
> `EVENTCONT_ATTR`. Writing the wrong name produces a file that reads back with the attributes
> attached to the wrong object — the sort of defect a round trip through one's own reader cannot
> see, because it is symmetric.

> **`CLASS_BASECLASS_ID` is one string on the wire whichever half it came from.** The reference
> picks between `classID` and `baseclassID` by the event's trigger (`GameEvent.h:826`); both derive
> from `CString` and only one is ever written, so the destination differs and the bytes do not.

One refusal, and it is the familiar one in a new place: below 0.998101 an editor-role design stores
an event's item, race, class and memorised-spell references as **numeric database keys**, which
this port renders as their digits. Writing `"12"` into a modern file would name an item *called*
`"12"`. `EventControl` now carries a `LegacyIds` flag for it — the provenance is not on the wire
and nothing else distinguishes the two readings, the same problem `MonsterRecord.LegacyIconFile`
solves for a monster.

##### The first whole file outside a database, as ported

`CharacterFileWriter` — a saved character (`.chr`). Small, and worth its own section for two
reasons: it is the first thing the port can write that is a **complete file the reference will
open** without a database around it, and it is the tightest test the character record has. A `.chr`
is a header and a `CHARACTER` and nothing else, so there is no slack anywhere for a field of the
wrong width to hide in.

> **The six shipped files cannot be reproduced byte for byte, and the reason was predicted.** They
> declare **3.64**, below the 5.24 the record is written at, so writing one back *upgrades* it —
> the same divergence `SomethingWild`'s `monsters.dat` has, from the same cause. What makes it a
> claim rather than an excuse is that the test says exactly what the upgrade is: take two four-byte
> zero runs back out of the rewritten record and the shipped bytes come back exactly. Those two
> runs are the `RestartFrame` at the end of the icon's `PIC_DATA` and the one at the end of the
> small pic's. In `Chrysia.chr` the first lands at byte 376 — the byte immediately after the icon
> ends.

> **A version below the writer's cannot be declared.** Stamping 3.64 on a file holding a 5.24
> record is the one combination nothing can read: the reader stops four bytes short at the icon and
> desynchronises from there. `Write` refuses it. The reference has the same asymmetry and resolves
> it identically — `SaveCharacter` stamps `ENGINE_VER`, not the version the character was loaded
> at.

> **The headerless branch is unreachable, not merely rare.** A `.chr` with no magic is assumed to
> be 0.563, which is below the 0.930 floor the engine enforces — so such a file throws on read and
> can never be produced. The reader's leniency there is for a file no build can load.

##### The fourth record type, as ported

`CHARACTER` (`CharacterRecordWriter`) — the format's largest record — with `CharacterLeafWriters`
under it for the four leaves nothing else had needed: the spellbook, the blockage list and the two
tagged adjustment lists. **Four of the six record types now write**, and this is the one the
savegames are built on.

There is no byte-for-byte claim to make here and the reason is structural: a character list sits in
the *middle* of `GLOBAL_STATS`, which has no writer, so there is no shipped file whose bytes this
alone reproduces. What stands in for it is the write-read-write identity — 29 characters across two
designs, written, read back and written again to the same bytes — which is what catches a field
that never went out at all.

> **The opener is a constant, not the record's own version.** The reference writes
> `CHARACTER_VERSION` — `0x80000001` — whatever the record was read as, and that is what makes the
> field a discriminator on the way back in: the high bit says "a version follows", its absence says
> the first `int` was a legacy index. The index the reference discards on load
> (`//uniqueKey = temp;`), so overwriting it loses nothing that was ever kept. Nothing else in the
> format self-identifies this way.

> **`spellLimitsType` is the smallest storing branch in the codebase.** One live statement —
> `car << UseLimits` — against five commented-out ones and a loading branch containing a whole
> pre-0.780 `BYTE` matrix (`GameRules.cpp:3611`). A reader gives no hint that the modern form on the
> wire is a single `int`.

> **Both adjustment lists open with the same tag.** The reference declares a local called
> `SAVersion` in each block and gives both `"SA0"`, so the tag says how a row is laid out and
> nothing about which list follows — position does the rest. The baseclass list's is `"BS0"`.

**Two reader gaps surfaced writing the inverse**, both of them the same shapes the monster writer
found:

> **`preSpellNamesKey` was being read and discarded.** The storing branch writes it
> unconditionally, so it had to be kept — and it is **non-zero on all 29 characters in the corpus**,
> which turns "keep it for tidiness" into "writing a zero there would have put 29 wrong keys into a
> file". That is pinned as a test.

> **The legacy undead type was being kept as its ordinal**, exactly as the monster reader once did:
> the reference names the index from `UndeadTypeText` as it loads (`Char.cpp:2727`) and this port
> stored `"1"` where the design means `"Skeleton"`. Finding the same defect twice, in two readers
> written months apart, is the argument for reading the loading branch from its start rather than
> from where a search lands.

One loading-side fixup is **deliberately not mirrored**: a character whose opener was an index has
its armour class reduced by the protection its readied items give (`Char.cpp:3015`), because old
versions folded that in. Keeping the raw `m_AC` is what lets the record be written back byte-exact;
the cost is that the port's in-memory AC differs from the reference's for such a character. That is
the per-field trade-off §10a of `SERIALIZATION.md` describes, and this is the field.

The refusals are the interesting negative result: **a record four times the size added no new kind
of refusal.** No icon, an item held by a legacy numeric id, and a pre-0.921 specab block are the
same three the monster writer refuses. Only one is new, and it is a distinction rather than a kind:

> **A missing money sack is a refusal here and not for a monster.** A monster below 0.906 has no
> sack because the reference had nothing to write, so an empty one is exact. A character below
> 0.661 has *loose coins* which the reference folds into the sack as it loads and this port
> discards — so an empty sack would take the character's money. Same absent member, opposite
> conclusion, and the difference is only visible in the loading branch.

**What the corpus does not reach is unusually large here and is stated rather than implied.** Six
of the record's structures — the money sack's contents, both adjustment lists, the blockage list,
the spell-effect list and the ASL — are empty in **every character of both designs**. The round
trip proves only that each is written at the right size; their contents are unit-tested alone. The
empty money sack is the same gap the monster corpus has, now confirmed across both record types.

##### The third record type, as ported

`SPELL_DATA` (`SpellRecordWriter`) — the format's largest record — with `DicePlusWriter`,
`BaseclassListWriter` and `SpellEffectsWriter` under it, and **`ci-tier3`'s `spells.dat` came back
byte for byte**, as its `items.dat` and `monsters.dat` already do. Three of the six record types
now write.

Two leaves arrived with it, and the first is the more interesting:

> **`DICEPLUS` has three forms and can only ever write one.** The storing branch writes `DP2` — a
> tag and two strings — and the entire numeric path beneath it is *commented out* (`class.cpp:2505`),
> which means `DP0` and `DP1` are shapes the reference reads and has never been able to produce.
> `ADJUSTMENT` and `GENERIC_REFERENCE` therefore have **no reachable writer at all**, and porting
> one would have been work for a code path nothing can reach. This is the same rule
> `SpecabWriter` and `MonsterRecordWriter` each found, arrived at from a third direction: the
> loading branch understands more shapes than the storing one emits.

> **A `DP0` or `DP1` is refused rather than written as an empty `DP2`.** The reference synthesises
> the text from the packed fields as it loads (`EncodeOldDicePlusText`); this port does not, so
> writing one out would emit an empty expression — a file that reads back cleanly with the dice
> silently gone. The same shape of refusal as the legacy specab block.

> **`SPELL_EFFECTS_DATA`'s `changeData` is written outside the storing branch**, after the brace
> that closes it (`Spell.cpp:273`), so it belongs to every effect at every version. It is the trap
> the reader's section already names, seen from the writing side — and the one field in the
> structure a writer is most likely to leave out.

The record itself has one refusal fewer than an item, and the reason is worth stating because it
inverts the item's:

> **The pre-0.998101 class bitmasks are not an obstacle.** An editor-role spell below that version
> carries a `WORD` school mask and a `WORD` cast mask, and the *reader* already expands both — the
> school into `"Magic User"` or `"Cleric"`, the mask into baseclass names — exactly as the reference
> does. So the modern form is in hand and nothing needs `baseclass.dat`. An old **item** is
> unwritable for the mirror-image reason: its `Usable_by_Class` bitmask is kept raw, and converting
> it needs the database that has no reader.

`WrittenVersion` is 5.24 again, set by the embedded `PIC_DATA` as it is for the other two. The
record's own highest gate is unusually high for a record body — **2.6**, where the
`SpellInitiation` / `SpellTermination` pair joins the wire, and it joins *before* the 1.0303
saving-throw group rather than after it. That ordering is why `SpellRecord.Scripts` is now **always
seven slots** with the absent ones left blank: a five-entry list from a 1.0303 design holds the
saving-throw group in the two places the initiation pair belongs, so a positional list that
shortened with the version would put the wrong script in the wrong place. Parameters and sounds
*are* padded, because there the missing entries are a suffix and the reference writes its own
default-constructed members for them.

> **The compiled script binaries are kept, and it costs nothing.** `SPELL_DATA`'s seven
> source/binary pairs are all emptied by the reference as it loads, and `CompileScripts` — which
> the storing branch calls first — turns out to be **entirely commented out** (`Spell.cpp:5210`):
> every `CompileScript` call in it is `//`'d, leaving a function that empties five already-empty
> strings. So a file the reference wrote holds fourteen blanks per record. The port keeps what it
> read anyway, as it already does for `DICEPLUS`'s `m_Bin`, and a corpus test asserts the shipped
> binaries really are empty — which makes the claim measured rather than assumed. An editor that
> edits a source must clear the binary beside it.

One thing the corpus says about the refusals: **DefaultDesign's 117 spells are 0 of 117 writable**,
and for the specab shape alone — the same reason its 44 monsters are 0 of 44. Give each record a
modern block and the rest goes out, which is what the test does, and is how the class-mask claim
above is pinned rather than asserted.

##### The second record type, as ported

`ITEM_DATA` (`ItemRecordWriter`) — and **`ci-tier3`'s `items.dat` came back byte for byte on the
first attempt**, which is what a second record type is really for: the first proves the format is
understood, the second proves the *method* is.

It shares three of the monster's four leaves and the same rule about the storing branch carrying no
version gates. Three things are its own:

> **Transcribe the `CAR` overload, not the `CArchive` one.** The latter's storing branch opens with
> `die("We should not be serializing itemdata with CArchive")` (`Items.cpp:2348`) — code that
> cannot run, describing a format that is never produced. §1's warning about the storing branch
> being the newer of the two has a sharper form here: one of them is a corpse.

> **`HitArt` is written twice and `MissileArt` once.** The pair goes out early, then `HitArt` alone
> again near the end (`:2698` and `:2744`). Both are on the wire and the reader consumes both. The
> trailing comment explains the asymmetry — the second copy is `HitArt`'s combat-directory form,
> and missile art keeps its place in the ASL rather than being repeated.

> **`ROF_Per_Round` is a `double` among `int`s** — eight bytes where its neighbours are four. Worse
> than the monster's `float` hit dice, because there the width matched and only the interpretation
> was wrong; here writing it narrower shifts the whole rest of the record.

**The record ends at its ASL, but the database does not**: an ammo-type list follows the records,
the same shape as the item list that follows a monster's attributes. A writer that stops at the
records leaves the reader taking that list's count from whatever comes next.

One record shape is refused: a pre-0.998101 `Usable_by_Class` bitmask, whose conversion to a
baseclass list needs `baseclass.dat` — still unread. Writing an empty list would make the item
usable by nobody.

##### The `CAR` write path, as ported

`CarLzwCompressor` and `CarArchiveWriter` — **the last wholly unexplored part of the format**.
Nothing could produce a compressed archive until these existed, which is why byte-identity with a
shipped design was out of reach and why Phase 5 could not start.

The encoder is tested by round-tripping through the decoder, which is the strongest specification
available: that decoder walks every compressed design in the corpus to exact end-of-file. A
120,000-byte pseudo-random stream is included deliberately, because it is long enough to **fill the
dictionary and force a reset** — with a guard test beside it asserting the input really is large
enough, so the reset path cannot look verified when it is not.

> **The bit packing is an OR into a zeroed buffer, not a write.** The reference does an unaligned
> 32-bit `|=` at `buffer + (index >> 3)` and relies on the buffer being zeroed after every flush,
> so a code spills into the following bytes and the next code ORs on top. Writing instead would
> clear the low bits of any code that straddles a byte boundary — which is most of them, since 13
> does not divide 8.

> **Filling the dictionary emits the pending code *before* the reset code.** Emitting the reset
> first would leave the decoder holding a code that only made sense against the table it had just
> cleared.

> **Flushing an untouched compressor still writes a full block of terminators.** The pending code
> starts at `0xFFFF` and thirteen bits of that *is* 8191, so the "nothing was written" case
> produces terminators rather than an empty file — which is what the decoder expects to find.

> **A string with an embedded NUL is written every time and never interned.** The reference takes
> a separate path that skips the table (`class.cpp:11927`), and the reader has the matching
> exclusion — so the two agree only if both skip it. Interning it would shift every later index by
> one and desynchronise the whole table.

> **A count is a flat `DWORD` here.** `CAR::WriteCount` delegates to MFC's two-tier escaping form
> only when `compressType` is 0, and this writer is always type 2. This is §4.3's trap from the
> writing side.

One observation worth recording: `CAR::Compress` always writes **2**, and every tagged database on
disk carries **1**. No code path in the reference produces a 1, so those files came from something
else or from a build that differed. Reading honours both; writing has only ever produced 2.

**`IArchiveWriteCursor` is that cursor's counterpart**, and the record writers now go through it —
so a whole `monsters.dat` can be written in the encoding it shipped in. All four modern designs
round-trip through the compressed path: read, write as `CAR`, read back, compare field by field.
A guard test beside it asserts the compressed form really is smaller than the plain one, so the
round trip cannot pass on a writer that compressed nothing.

Three of the cursor's methods are genuinely different between the encodings rather than merely
dispatched: **a string is length-prefixed in one and interned in the other**, a count is MFC's
escaping scheme against a flat `DWORD`, and there is no compressed equivalent of writing raw
string bytes. Everything else is the same bytes down a different pipe.

> **`ci-tier3`'s `monsters.dat` is reproduced byte for byte** — all 4,265 bytes of it, LZW and
> string interning included. That is Phase 1's round-trip exit criterion demonstrated for one
> record type against a real shipped file, and it holds under two conditions: the design is at or
> above `WrittenVersion`, so no field is added on the way out, and no record needed repairing as it
> was read.

The other two designs differ, and **both differences were predicted by things already documented**,
which is the useful part:

- **`SomethingWild` (3.55)** is below the 5.24 that adds the icon's `RestartFrame`, so writing it
  back *upgrades* it — four bytes per monster, from the first record's icon onward. The reference
  upgrades too when it saves an old design. The divergence appearing **early** is the tell.
- **`dc-default` (5.28)** adds nothing on the way out, but one of its 171 monsters has an **empty
  attack list on disk** and the reader forces such a monster to one attack (`Monster.cpp:764`). So
  the file is reproduced exactly up to that record and diverges from it on. The divergence
  appearing **late** is the tell.

Both are pinned as tests asserting *where* they diverge, not merely that they do — an early
divergence in the dc-default case, or a late one in SomethingWild's, would mean something other
than the cause claimed here.

##### The first whole record the port can write, as ported

`MONSTER_DATA::Serialize` (`Monster.cpp:629`) — `MonsterRecordWriter`, with `MonsterLeafWriters`
and `PicDataWriter` under it. **The first record type this port can write**, and the first thing
above the byte layer that produces a file rather than a fragment.

Monsters first because their records are the only ones in the corpus that carry real content in
every leaf the other databases share: an ASL block on all 570 records across four designs, special
abilities on 527, an embedded `PIC_DATA` on every one, an item list on 163, a money sack on all of
them. Items and spells reuse most of that.

> **One write path, whatever the version — and the reason is worth understanding before writing
> the next record type.** The reference's storing branch is a flat run of writes with **no version
> tests in it at all**; every gate lives in the loading half. That is not an oversight. A design is
> always saved at the *current* version, so on the way out every gate is open by construction,
> while the loading gates exist to read what older builds left behind. Mirroring them would emit an
> old shape into a file stamped new, which is the one combination nothing can read. `SpecabWriter`
> reached the same conclusion from its own gate; this is the general rule.

`MonsterRecordWriter.WrittenVersion` names the earliest version whose *reader* reads exactly the
shape written — **5.24**, bound by the icon's `RestartFrame`. Nothing is added to the record
between there and `PRODUCT_VER`, so anything in that range reads it identically.

**Four legacy shapes survive reading and cannot go out**, so `CanWrite` refuses them and says why:
a record with only a pre-0.640 icon filename (building a `PIC_DATA` from it needs
`SetDefaults()`, unported), an attack or a carried item still holding a pre-0.998101 *numeric* id,
and special abilities in the pre-0.921 shape. **`DefaultDesign` is 0 of 44 writable** for the first
three reasons at once, which is the useful demonstration that the refusal is not theoretical.

> **A missing item list or money sack is *not* one of them.** Those are absent below 0.694 and
> 0.906, where the reference writes its default-constructed members — an empty list with twelve
> zeroed slots, ten zeroed coin types. Writing empties there is exact, not a guess, and the
> distinction between "the reference has nothing to write" and "the port has lost something" is
> what separates the two lists.

- **The `DAS` blank convention applies to six strings and not to the rest.** Name, the four sounds
  and the icon filename go through it; `classID`, `undeadType`, item ids and spell ids are written
  verbatim. The reference marks the difference only by which macro it used at the call site.
- **`readyLocation` goes out exactly as it came in.** The reference's *reader* maps the ordinals
  0‥16 onto the base-38 packed constants (`itemReadiedLocation::Synonym`) and then stores the
  mapped value, so a reference load-and-save silently upgrades an old slot. This port reads the raw
  `DWORD`, which is what makes writing it back byte-exact.
- **The `PIC_DATA` variant matters when writing too**, and it is not a version question: `style` is
  written on the `CAR` path and commented out on the `CArchive` one, matching each path's reader.
  Four bytes, with nothing in the record to say which.
- **`$SYS$Race` is left alone.** The reference re-derives that attribute from its in-memory
  `raceID` before writing the ASL (`StoreStringAsASL`); this port never splits the two apart, so it
  writes the attribute back as read — the same bytes for any file the reference produced.

**Three reader gaps surfaced while writing the inverse**, all of them things the reference does
during a load that the port was not doing:

> **The legacy undead type was being kept as its ordinal.** Below 0.998115 the file holds an index
> which the reference names from `UndeadTypeText` as it loads (`Monster.cpp:816`); the port stored
> `"1"` where the design means `"Skeleton"`. Reading it is harmless in isolation — nothing compares
> it to anything — but *writing* it would have put the ordinal into a modern file permanently, and
> no turning table has a category called `"1"`. Index 0 must still come out empty rather than
> `"Not Undead"`, because "is this undead at all?" is asked everywhere as "is the string non-empty".

> **A monster that loads with no attacks is given one** — 1d6, message `"attacks"`
> (`Monster.cpp:764`) — at *every* version, not just in the legacy branch. Live in the corpus: one
> of `dc-default`'s 171 monsters has an empty attack list on disk, and a literal reader leaves it
> unable to attack at all.

> **The pre-0.750 attack expansion floors three of its four scalars, not one**, and the floors
> differ: one attack, ten sides, one die. A zero there means "unset", not "none".

**What the corpus test proves and what it does not.** All 570 records read, write and read again
unchanged, and writing what was read gives byte-identical output the second time — which catches a
field that never went out at all, since a byte the writer omits is one the reader takes from
somewhere else. It is **not** byte-identity with the shipped file: every modern `monsters.dat` in
the corpus is a compressed `CAR`, and decompressing one does not yield the plain stream either,
because `CAR` interns strings across the whole archive. Byte-identity needs the `CAR` writer —
both halves of it.

> **The reference does this exact round trip itself.** `WriteMonsterDB` saves the database, reads
> it straight back and compares (`Dbutils.cpp:476`), normalising the version first because the
> file it just wrote is at `PRODUCT_VER` and the one it loaded was not. That version fix-up is the
> same asymmetry `WrittenVersion` names.

One coverage gap worth stating rather than papering over: **every money sack in the corpus is
empty** — ten zeroed coin slots, no gems, no jewellery, across all 570 records. The sack's
non-empty form is covered by unit tests only. What the corpus does prove about it is that the empty
sack is written at exactly the right size, since anything else would leave the next record
misaligned.

##### Spell effects on a character outside combat, as ported

`GetAdjAC` and `GetAdjHitPoints` (`Char.cpp:13198`, `:13239`) — `Character.Effects` plus the
adjusted accessors. Combat has kept a `SpellEffectList` on each combatant for a while; a character
walking a corridor now keeps one too, so a blessed character has the armour class the blessing
gives.

The shape is uniform across the family and worth knowing before porting more of it: **base value,
then `ApplySpellEffectAdjustments`, then a clamp** — and the clamp differs per attribute.

> **The armour-class bounds run the opposite way from their names.** Armour class counts down, so
> `MAX_AC` is **10** — the *worst* — and `MIN_AC` is **−500**. A clamp written as "at least MIN, at
> most MAX" is right; one written from the names alone ("at least MAX") inverts the rule and makes
> every blessing useless.

**Hit points clamp to the character's own maximum above and to −10 below.** Ten below zero is where
a character is finally dead rather than dying, so no effect can drain someone past it, and no
healing effect can push anyone above their maximum however large it is.

`$GET_CHAR_ADJAC` now answers, and `$GET_CHAR_HITPOINTS` answers with the adjusted value it was
always supposed to — **there is no unadjusted form in the sub-opcode set**, so a script asking for
hit points always gets the adjusted number. `$GET_CHAR_EFFAC` is a third form again, folding in the
target's size and the attacker, and is still unanswered.

**THAC0 is the same trap twice over.** `MAX_THAC0` is 20 and `MIN_THAC0` is −500 — two lines below
the armour-class pair in the same header, counting the same way down. And **`GetAdjTHAC0` is more
than a clamp**, unlike its neighbours: it subtracts the character's hit bonus and the readied
weapon's attack bonus *before* applying spell effects, because a lower THAC0 is better. This port
takes both bonuses from the caller, having no readied-item model on a character outside combat.

> The reference fetches the readied item **before** testing whether one exists, so
> `GetItem(NO_READY_ITEM)` is called and its result handed to the hit-bonus lookup; only the
> weapon's attack bonus is properly guarded. Nothing observable turns on it.

##### A script that can reach game state, as ported

The attribute sub-opcodes (`GPDLexec.cpp:4178`, `:5498`, `:3379`) and `UAFcore/GameScriptHost.cs`.
**The first family of game-state calls the GPDL VM can actually serve** — until now it ran the
bytecode faithfully and refused every one of the ~250 calls that touch the engine.

`$SET_GLOBAL_ASL`, `$GET_GLOBAL_ASL`, `$SET_PARTY_ASL`, `$GET_PARTY_ASL`, `$IF_PARTY_ASL`,
`$DELETE_PARTY_ASL`, `$SET_CHAR_ASL`, `$GET_CHAR_ASL` and `$IF_CHAR_ASL` now run against the real
stores: the design's global one (§the attribute store), the party's own, and each character's.
`GameScriptHost` is the first `IGpdlHost` backed by a running game; `GpdlUnhostedEnvironment` keeps
its in-memory stand-in so the VM's own tests need no engine.

- **The value is on top of the stack, not the key.** GPDL pushes arguments left to right, so
  `$SET_GLOBAL_ASL(key, value)` leaves the value on top and it is popped first. Reading the pops in
  source order stores the key under the value.
- **A set yields the value**, so the expression is usable; a **delete yields false whatever
  happened** — the reference's own comment beside the push is "Must supply a result", so it exists
  to balance the stack rather than to say anything. A script testing a delete learns nothing.
- **A missing key reads as the empty string**, because `Lookup` returns a shared empty string rather
  than signalling (`ASL.cpp:1089`). A script cannot tell an unset attribute from one set to nothing
  by reading it — only by asking whether the key exists.
> **`$IF_CHAR_ASL` is not a test.** Despite the name it pushes the *value*, exactly as
> `$GET_CHAR_ASL` does (`GPDLexec.cpp:4452`) — there is no existence check anywhere in it, and the
> commented-out code above shows it was a lookup before too. A script using it as a boolean is
> really testing the value for emptiness, so an attribute deliberately set to nothing reads as
> false. Its party-scoped namesake `$IF_PARTY_ASL` *does* test existence, which makes the pair
> actively misleading.

- **Characters are named by id, not by party index.** A dated comment records the change: "almost
  all functions use the uniqueID of the character rather than the party index. I decided that the
  few exceptions should be treated as 'bugs'" (`:1845`). The *combat order* alternative the same
  comment mentions is not resolved by this port — a script naming a combatant by its place in the
  fight finds nobody.
- **An actor that resolves to nobody is not an error.** The reference puts a message box in front of
  the player and returns a null character whose store swallows the write, so a design with a
  typo'd actor limps rather than stops. Same here, without the dialog.
- **A script-set attribute carries no flags at all.** `InsertGlobalASL` defaults its `flags`
  parameter to zero and the sub-opcode passes nothing, so it is never marked modified. It still
  reaches a save game, so nothing observable turns on it — but the flag is not evidence a script
  wrote the value.

**Character stats too.** `$GET_CHAR_NAME`, `$GET_CHAR_AC`, `$GET_CHAR_HITPOINTS`,
`$GET_CHAR_MAXHITPOINTS`, `$GET_CHAR_RDYTOTRAIN` and `$GET_CHAR_GENDER` resolve their actor the
same way and read state `Character` already holds. They are one enum on the host rather than a
method each, which is what the reference does too — most of the family collapses into the two
macros `GET_CHAR_INT` and `GET_CHAR_STRING` (`GPDLexec.cpp:2269`).

- **An integer stat arrives as text**, because GPDL's stack holds nothing else. The reference
  pushes through `m_pushInteger1`, which does the same conversion, so a script comparing a stat
  against a literal is comparing strings.
- **`$GET_CHAR_AC` is the *base* armour class** (`GetBaseAC`); `$GET_CHAR_ADJAC` is the adjusted
  one and both now answer (§spell effects on a character outside combat). `$GET_CHAR_EFFAC` is a
  third form and does not.
- **`$GET_CHAR_Exp` is not a plain stat** and is not wired: it takes a baseclass argument and
  reports that class's experience alone.

The tests drive real GPDL source through the compiler and the VM rather than poking the
interpreter, so the argument order the code generator emits is under test alongside the
sub-opcodes.

##### The one thing a character's attribute store is used for, as ported

`AddKnowableSpell`, `DelKnowableSpell`, `ClrKnowableSpell` (`Char.cpp:1145`) —
`UAFcore/KnowableSpells.cs`, plus `Character.Attributes`.

**Checked for liveness before porting, and it is the only live use of the per-character attribute
list in the engine.** Everything else `char_asl` holds is design data nothing reads back. The
spells a character may still learn are kept in it under `$KnowableSpells$` as one packed string: a
bare concatenation of `?name` entries, so `?magic missile?sleep` is two spells. The delimiter
**prefixes** each entry rather than separating them, and there is no terminator.

That packing is where the traps are, and all three are the format's own consequence:

> **Membership is a substring test, not an entry test.** `list.Find("?" + name)` — so a spell whose
> entry is a prefix of another's silently fails to be added. With `?Fireball` already in the list,
> adding `Fire` finds `?Fire` inside it and refuses; the other way round works. Reproduced, because
> a design's spell names were chosen against it.

> **Removal has two branches, and the first exists only because of the packing.** The last entry has
> nothing after it, so the bounded `?name?` search cannot find it and it is matched as a *suffix*
> of the whole string instead. Every other entry is matched with its following delimiter — and the
> removal deliberately **leaves that delimiter behind**, because it introduces the entry after it.
> The reference's arithmetic for that (`Right(len - n - str.Length + 1)`) is off-by-one-looking and
> is not: the `+ 1` is the retained delimiter.

> **`ClrKnowableSpell` returns `false` unconditionally**, where its two siblings return whether
> anything changed. Nothing reads the result, so the inconsistency is invisible; this port returns
> whether there was a list to clear.

##### The attribute store, as ported

`A_ASLENTRY_L` (`ASL.h:95`, `ASL.cpp:1285`) — the named key/value stores a design's scripts read and
write. `UAFcore/AttributeList.cs`, wired into `Game` as the global one, and **the combat verdict now
reaches a design** through it.

The engine keeps several: one global, one per character, one per event, one per item. They are how a
design records state that outlives a single script. Read off the wire already (`AslReader`); what
was missing was somewhere to keep them at runtime.

**Two of the four flags do the work; two are labelled "info only. Not used" in the header itself.**

- **`ASLF_READONLY` is the load-bearing one.** It decides what a save game holds and what survives
  a restore. A read-only attribute comes from the design and is reloaded with it, so storing it in
  a save would only let a stale copy override the design later.
- **`ASLF_MODIFIED` is set by the caller, not by the container.** The header says the first
  insertion during play does not set it, and nothing in `Insert` does — it is a convention the call
  sites follow. The combat results screen passes it explicitly.

> **`Insert` returns true when the key was *already there*.** That reads backwards from "did it
> work", and it is deliberate: callers testing the result are testing for a pre-existing value.
> It also **replaces the flags, not just the value**, so inserting over a read-only attribute with
> no flags makes it writable. The reference does not guard that.

> **Read-only is not enforced by the container.** The flag's own comment says such an attribute
> "can't be deleted", but `Delete` takes a key and removes whatever it finds. The protection lives
> in the callers and in the save path — worth knowing before trusting the flag as a lock.

`CommitRestore` is two halves and both matter: discard every non-read-only entry, *then* take the
source's non-read-only entries. The discard is what stops a key the save game no longer has from
lingering; the filter on the way in is what stops a save overriding the design's read-only values.

**The combat verdict** is written under the key `"Combat Result"` — spelled with a space, and a
design tests it by that exact name — with the four values `Win`, `Lose`, `LoseButNeverDies` and
`Flee` (§the combat aftermath). `Game` writes it as the results screen does, flagged modified.

##### The combat aftermath, as ported

`DetermineVictoryExpPoints` and the results screen (`Combatants.cpp:4315`, `RunEvent.cpp:19669`) —
experience, treasure and the verdict a design's scripts read. `UAFcore/CombatAftermath.cs`, wired
into `Game`. **Combat is now a closed loop**: a fight starts from an event, runs to a verdict, pays
out, and hands the chain back.

- **Only the *dead* count, for both experience and treasure.** The test is
  `GetAdjStatus() == Dead` and nothing else, so a monster that fled, was turned or is merely
  unconscious is worth nothing and keeps its possessions. **A fight won by driving everything off
  the map pays no experience at all.**
- The monster modifier is a percentage added on top (`mod=100` doubles it), and the total is
  clamped at zero afterwards.
- **Treasure items carry experience of their own**, counted from the treasure rather than from what
  the party already holds and added *before* the share-out — so finding a magic sword pays for the
  finding.
- **Only characters with status `Okay` share**, and **the whole remainder goes to the first of
  them** rather than being spread: three survivors and 100 points gives 34, 33, 33.
- **Fled party members are restored to `Okay` on the way out**, on both a win and a flight — but
  *after* the experience is shared, so a character who ran does not share in the fight they left.
- Lingering spells are cleared at combat end (`RemoveLingerSpells`).

> **"Fled" is derived after the fact, not decided during the fight.** The reference settles on
> `MonsterWins` and only then scans the party for anyone with status `Fled`, promoting the result
> to `PartyRanAway` if it finds one. A loss where a single character escaped is therefore a
> *flight*, not a defeat — and the check is any member, not all of them.

> **A monster's spell-casting items do not drop.** The filter is
> `(Wpn_Type != SpellCaster && Wpn_Type != SpellLikeAbility) || CanBeTradeDropSoldDep`, so a wand
> a monster used is kept out of the treasure unless the design explicitly marks it tradeable.
> Dropping the filter hands the party every enemy wand in the game.

The verdict reaches a design through a global ASL named **`"Combat Result"`**, whose values are
`"Win"`, `"Lose"`, `"LoseButNeverDies"` and `"Flee"`. Those strings are the interface — a design
tests them by name — so they are transcribed rather than derived. The ASL layer itself is not
wired yet; `CombatResult` and `ResultText` are the seam.

##### Using an item, as ported

USE and the item-spell path (`ITEMS_MENU_DATA` → `CastItemSpell`, `RunEvent.cpp:15917`,
`Combatant.cpp:753`). **Every combat command is now implemented** except SPEED, which is a
presentation setting rather than a combat rule.

> **The item reader was throwing away the field the whole command rests on.** `ITEM_DATA::spellID`
> is read at `Items.cpp:2761` — a string, despite sitting among integers — and this port's
> `ItemRecordReader` consumed it with a bare `ar.ReadString()` and discarded the result. Without it
> nothing can know what a wand does. Now captured on `ItemNames`. Checked against the corpus:
> **135 of 551 items in `SomethingWild` and 78 of 479 in `Case` name a spell, and every one of them
> resolves** against that design's own spell database (`Potion of Invisibility` →
> `itemPotionInvisibility`, `Scroll of Hold Monster` → `Hold Monster`).
>
> The field is gated at design version **0.999647** — a bare literal in the C++ with no named
> constant. `ci-tier3` predates it, so **none of its 285 items name a spell** and USE has nothing to
> invoke there. That is not a defect; it is what the wire holds.

**`CastItemSpell` is nearly a copy of `CastSpell`, with two real differences.** There is no book
lookup and no `DecMemorized` — the item's charges are the resource — and **the overflow branch
lands a round later**: where the spell version re-times an overlong initiative spell to
`waitUntil = round`, the item version writes `round + 1` (`:815` against `:692`). The comment above
both is word for word the same, including the commented-out line it replaced, so it reads like a
slip; a one-round difference in when a wand goes off is the kind of thing a design is balanced
against, so it is kept and tested.

`StartInitialItemSpellCasting` differs from its spell twin in one more way worth knowing:
**targets are cleared only when the caster is not on automatic**, so an AI-driven item use keeps
whatever it had preselected.

VIEW is wired to report the acting combatant. SPEED is `GAME_SPEED_MENU_DATA`, a game-speed
control, and is the one command that still says it is not implemented — deliberately.

##### Turning, delaying and automatic, as ported

TURN, DELAY and QUICK (`COMBAT_DATA::TurnUndead`, `COMBATANT::DelayAction`, `COMBATANT::Quick` —
`Combatants.cpp:6311`, `Combatant.cpp:7685`, `:7034`). `UAFcore/TurnUndead.cs` plus the session
wiring.

> **The AD&D turning table is dead code.** `UndeadTurnTable` and `GetUndeadTurnValueByHD` are
> complete and correct (`GameRules.cpp:506`, `:538`) — thirteen undead rows against fourteen cleric
> levels — but their only caller, the exported `GetUndeadTurnValue`, was stubbed when the undead
> type stopped being an enum: it tests a sentinel, calls `NotImplemented(0x145ab)` and returns
> zero, with the line that would reach the table commented out beside it (`:629`). Nothing else
> calls either function. **Turning is entirely design-scripted** through the `TURN_ATTEMPT` hook,
> which returns the undead categories a cleric reaches. Not ported, for the same reason the other
> dead branches were not — but it is the most convincing-looking dead code in the codebase, and
> anyone porting combat will find it before they find the stub.

What is real is the application half:

- **Two passes, and the first ignores anyone already running.** A monster with status `Fled` or
  `Running` is skipped on pass 0 and considered on pass 1, so a standing monster is always turned
  in preference to one already leaving. Without the two passes a cleric could spend the whole
  attempt on monsters that were going anyway.
- The dead and the gone are skipped on both passes, so they never consume a slot.
- **A turned monster is set running, not removed** — status `Running`, `isTurned` set, and its
  last attacker set to the cleric, which is how it knows which way to run. A destroyed one becomes
  `Gone` and leaves the map.
- **The sentinel for "cannot turn" is 99, not zero.** `GetTurnUndeadLevel() < 99` is the whole
  condition, so any lower value passes — including zero and negatives.

**DELAY does not end the turn.** Initiative goes up by one, the state clears and the combatant
comes off the queue, but `turnIsDone` is untouched — so the round's walk reaches it again at its
new slot. That is the whole difference between DELAY and END. It is refused when
`initiative + 1` would reach `INITIATIVE_Never`, because a delayed turn must still come round
*this* round.

> **QUICK only ever turns automatic ON.** The combat menu calls `Quick(TRUE)` and nothing else
> (`RunEvent.cpp:15422`); there is no menu route back. Taking a party member back off automatic is
> bound to **the space bar** (`:15129`), handled before any state check — the reference's comment
> is "need to handle this regardless of state". Reading QUICK as a toggle gives the player a way
> back that the original does not have. Turning automatic off also has to undo what the AI had the
> combatant doing — path, targets, state and any spell in progress.

SPEED is the game-speed menu (`GAME_SPEED_MENU_DATA`), a presentation setting rather than a combat
rule, and is not ported. USE needs item invocation and is not started.

##### Lingering spells, as ported

`SPELL_LINGER_DATA` and `ProcessLingeringSpellEffects` (`Spell.h:1068`, `Char.cpp:18158`) — a spell
left standing on the map. `UAFcore/LingeringSpells.cs`, wired into `CombatSession`. About a fifth of
the spells in the shipped designs set the flag (78 / 9 / 61 of 377 / 117 / 318).

- **The check runs per round, not per move** (`Combatants.cpp:4605`, inside `StartNewRound`): every
  combatant on the map is tested against every lingering spell at the head of a round. A combatant
  that walks into a cloud and out again within one round is **never caught by it**; one that ends
  the round standing in it is caught at the start of the next.
- **Any one square of a footprint is enough.** The test walks the spell's squares looking for one
  inside the combatant's box, so a cloud touching only the corner of a large monster catches it.
- **"Once only" means once per combatant, not once in total.** `EligibleTarget` returns
  `!OnceOnly` for someone already caught and `TRUE` for someone new — so a once-only cloud keeps
  catching fresh arrivals forever, and only stops repeating on the same victim.
- **Catching is a side effect of asking.** `ActivateLingerSpellsOnTarget` adds the target to each
  spell's list as it activates it, which is the only thing that makes once-only work.
- **Only a combat cast lingers.** The reference stores
  `IsCombatActive() ? pSdata->Lingers : FALSE` (`Char.cpp:16324`) — a spell cast in camp leaves
  nothing behind however its record is authored, because there is no map to leave it on.

> **A lingering spell blocks movement by default.** `BlocksCombatant` sets its answer to "blocks"
> and only clears it when the `SPELL_LINGER_BLOCKAGE` script explicitly returns `'N'`
> (`Spell.cpp:7787`). A design that writes no blockage script gets a wall of fire that really is a
> wall. Getting this default backwards would let everybody walk through every cloud, and nothing in
> a test of the spell's own effects would notice.

`ProcessLingeringSpellEffects` re-rolls and re-applies every effect on a character that is *not*
flagged `EFFECT_ONCEONLY` — the naming inverts again, since that flag means "affect the target once
rather than once per round", so *not* once-only is the repeating case. That re-application is
driven here by the round-head pass rather than by walking each character's effect list, which is
the same work reached from the other end.

##### Choosing a spell's targets, as ported

`SPELL_TARGETING_DATA` and `COMBAT_SPELL_AIM_MENU_DATA` (`Spell.h:340`, `RunEvent.cpp:20176`) — the
player naming each target. `UAFcore/SpellTargetSelection.cs` plus two new session modes.
**All ten targeting modes now cast.**

The submenu is the *same six entries* as the ordinary AIM menu — the reference builds both from
`AimMenuData` — but TARGET does a different job: it takes a target, re-titles the menu with how
many are still wanted, steps the cursor on, and only ends the turn once nothing more is wanted.

- **Each limit is only enforced when it is set.** All three tests in `STD_CanAddTarget` are guarded
  by `> 0`, so a zero maximum means *no limit* rather than none allowed — which is exactly what
  lets `SelectByHitDice` zero `MaxTargets` and still work.
- **A target that exactly reaches the hit-dice budget is allowed.** The test refuses only what
  would *exceed* it, while `HDLimitReached` is `>=`, so the last pick both lands and ends the
  selection.
- **The hit-dice total only accumulates for the hit-dice mode**, so a budget cannot leak into a
  spell that does not use one.
- **Running out of combatants ends the selection as surely as filling the quota.** The count modes
  test `NumTargets() >= GetNumCombatants()` as well as against the maximum, so a spell allowed six
  targets in a fight with three stops after three rather than leaving the player pressing EXIT.
  The menu title is clamped the same way.
- **Only an empty selection prompts "ABORT THIS SPELL?"** Fewer targets than the maximum is a
  perfectly good cast, and EXIT takes it without asking.
- **An area spell needs both a range and a target count to be castable at all**
  (`ValidNumTargets`). A design leaving an area spell's quantity at zero cannot cast it, and
  nothing fills that in; the reference `die()`s and abandons the cast.

> **A computer-run caster is this port's own rule, not the reference's.** Monster casting there
> runs the design's Forth script, which is unported, so a monster here takes whatever it can
> legally reach in combatant order — respecting the spell's friend and enemy flags and every limit
> the selection enforces. Legal rather than arbitrary, and the seam to replace when Forth lands.

> **A transposed call in the reference feeds a garbage range into target selection.** At
> `RunEvent.cpp:20233` the distance is taken as
> `Distance(caster->self, caster->x, caster->y, target->self, target->y)` — five arguments where
> the matching overload is `Distance(sX, sY, attackee, dX, dY)`, so the caster's *index* arrives as
> an x coordinate and the target's x is missing entirely. The value goes straight to
> `C_AddTarget`'s range check. This port computes the distance properly, which is a deliberate
> divergence: reproducing it would mean reproducing an argument-order slip with no defensible
> behaviour behind it.

##### Spell resolution, as ported

`CHARACTER::InvokeSpellOnTarget` (`Char.cpp:15987`) — what a spell actually does to a target.
`UAFcore/SpellResolution.cs`, wired into `CombatSession`. **Casting now runs end to end**: choose
from the book, spend the memorised copy, wait out the casting time, get the turn back when the
clock says so, and land the effects.

The per-target sequence, in the reference's order:

1. **The non-cumulative check comes first — before the scripts, before the save.** A second casting
   of a spell the target already carries is not merely wasted, it never even rolls. It is the
   *spell* that is checked, by source; the per-attribute cumulative rule inside
   `SpellEffectList.Add` is a separate and independent gate.
2. The `DOES_SPELL_ATTACK_SUCCEED` chain — tried against the spell, then the target's race, then
   its monster record, then its character record, first non-empty answer winning, `'N'` meaning no.
3. **The saving throw, rolled only when `Save_Result` is not `NoSave`.** The reference guards the
   call, so a no-save spell spends no d20 and runs no save-succeeded script — not the same as
   rolling and ignoring the answer. Two thirds of the spells in every shipped design are `NoSave`.
4. Out on `noEffectWhatsoever`.
5. Roll and add each effect **flagged `EFFECT_TARGET`**; the others describe the caster or the map
   and are skipped silently.

> **One active-spell entry per cast, not per target.** The reference allocates the key before the
> target loop, so a fireball that caught four combatants expires from all four together rather than
> wearing off piecemeal.

**Five script hooks live in this function and none are ported**: the attack-succeeds chain, the
spell's begin script, each effect's activation and modification scripts, and
`INVOKE_SPELL_ON_TARGET`. All are optional and all default to "carry on", so a spell with no
scripts — which is most of them — resolves identically. The two that can refuse are exposed as
predicates, ready for GPDL.

**What the session does with a resolved cast.** Self and whole-party need no picking; the five area
shapes need only a square, and the aim cursor already is one, so they are laid out from the caster
towards it and resolved through §area geometry. **The three unit-picking modes are not wired** —
`SelectedByCount`, `TouchedTargets` and `SelectByHitDice` need `COMBAT_SPELL_AIM_MENU_DATA`, and
they say so rather than guessing. By spell count that leaves the commonest modes unreached; by
machinery, everything under them is built.

##### Dice expressions, as ported

`DICEPLUS::Roll` (`class.cpp:2193`) — the little arithmetic language a design writes every number
in. `UAF.Rules/DiceExpression.cs`. **Nothing numeric a spell does was reachable without this**: the
`DP2` form carries only source text, and *every* spell-effect expression in the shipped designs is
`DP2` — 164 of 164 in `SomethingWild`, 150 of 150 in `ci-tier3`. The packed numeric fields of the
older `DP0`/`DP1` forms are dead in practice.

The grammar, taken from every distinct expression in four designs: integer literals, `NdS` dice,
the identifier `level` (case-insensitively — designs write both `level` and `LEVEL`), `+ - * /`,
one unary sign, and parentheses. Real examples: `1`, `-1d8`, `2d8+1`, `-(1d6)*level`,
`-(1d4+1)*((level+1)/2)`, `6-(1/4*LEVEL)`.

> ~~**A deliberate divergence in route, not in result.**~~ **The divergence was in result too, and
> `DiceExpression` is now a façade over the transcription** — see §the re-measurement. Evaluating
> "the same grammar directly" was fine for spells and wrong for the corpus: the grammar has clamp
> operators this section never saw, and two of the divergences below were real behavioural
> differences rather than route. `DiceExpressionCorpusTests` kept checking every spell expression
> evaluates, and that assertion was true throughout — it just could not see what it was not
> looking at.

- **The arithmetic is integer throughout**, and that is not a rounding detail.
  `RDREXEC::InterpretExpression` works on an `int` stack and its dice callback returns `int`, so
  division truncates at every step. A design writing `6-(1/4*LEVEL)` gets **6 at every level**,
  because `1/4` is zero before the multiply happens. That expression is in `ci-tier3`.
- **`RollDice` returns nothing for a zero-sided or zero-count die**, so `1d0` — present in two
  designs — is zero rather than an error or a one.
- **Only one unary sign is allowed**: "We allow only one unary operator. Do you want more?"
  (`GPDLcomp.cpp:4341`).
- ~~An identifier the lookup does not know evaluates to **zero, not a failure**.~~ **Two different
  paths were being conflated.** A name `LookupRefKey` cannot place at all fails the *compile*
  (`compileDicePlusRDR` returns 0, "Unrecognized Runtime Variable reference"), so the whole
  expression yields nothing. Only a name that resolves to a database the interpreter has not
  implemented — abilities, traits, spellgroups — is the "Illegal RDR code" zero. The lookup
  callback is where a caller says which it has.

> **There are no fractional literals, and designs write them anyway.** The tokeniser treats only
> `'0'`–`'9'` as numeric (`GPDLcomp.cpp:4221`) and accumulates digits into an `int`; a decimal
> point falls through to the operator table and matches nothing. ~~All of them are dead.~~
> **Only half of them.** Where a term was expected — `.5*level`, in all four designs — that is an
> error and the expression contributes nothing. Where an *operator* was expected the compiler's
> loop simply breaks, so `1.5*level` in `ci-tier3` is **`1`**. This port called both of them dead,
> and asserted it, for the same reason it called trailing text a failure: it had reconstructed the
> grammar instead of reading it.

##### Area geometry, as ported

Which squares an area spell covers: `GetMapTilesInRectangle` and the circle built on it
(`Drawtile.cpp:4646`, `:4812`), `GetCombatantsAndTilesInCone` (`:5083`) and the two line shapes
(`:5540`). `UAFcore/SpellArea.cs`. **All five area targeting modes are covered.**

Its own header comment gives the convention — **"Width is normal to casting direction; Height is
parallel"** — and records that it was "totally rewritten to center the rectangle and rotate it for
the various directions", with the older corner-anchored version left commented out beneath. The
method is two half-planes through the target, one across the cast and one along it, intersected;
rather than test every square it floods outward four-connected from the target, which is equivalent
because the intersection of two slabs is convex. All arithmetic is in quarter-square integers.

- **The target square is always included, tested against nothing.** The flood seeds with it and
  marks it visited before the loop — so it is in the result even at 1×1, and even when it lies
  outside the map, which the reference does not check.
- **An even extent straddles the target one square off centre.** Coordinates are scaled by four and
  nudged by one (`targetX += (x0<0) ? -1 : 1`), putting the centre a quarter-square past the
  square's own position, so a width of 2 facing east takes the target's row and the one *below* it.
  An odd extent is properly centred because the nudge cancels.
- **The circle is a square pruned by distance**, side `radius * 2 | 1` — doubled then forced odd,
  "so that there is `radius` on both sides of the target". A radius of 2 is a 5×5 square before
  pruning, not 4×4. A negative radius covers nothing at all, not even the target.
- **The tile prune and the combatant prune use different distances.** Tiles go by
  `Distance(sx,sy,dx,dy)`, Euclidean rounded to nearest. Combatants go by the footprint-aware
  overload (`Drawtile.cpp:1699`), which walks a large monster's icon inwards to its nearest
  occupied square first — so a big monster is caught by a circle its top-left corner would fall
  outside of.
- **Order matters and is the flood's, not the combatant list's**: target square first, then north,
  east, south, west outward. That decides which of two targets a spell resolves against first.

**Two findings about diagonal casts, both reproduced.**

*Width and height swap meaning.* The two tests use `(dirY, dirX)` and `(dirX, −dirY)` — the
direction vector **reflected rather than rotated**. Reflection happens to give the perpendicular
for the four cardinal directions, which is the only case where the header comment holds; on a
diagonal the vector paired with `width` is the direction itself, so width measures extent *along*
the cast and height measures it *across*. The code contradicts its own comment there.

*A thin diagonal area collapses to one square.* The flood is four-connected (`deltax`/`deltay` are
the four cardinals) but a diagonal strip one square thick is only diagonally connected. The squares
that would pass both tests are unreachable from the seed and simply never appear — so an area spell
cast diagonally with a height of 1 hits **the target square and nothing else**, however long its
width says it should be. Not a rounding artefact: the geometry is right and the traversal cannot
reach it.

**The cone is a triangle whose apex is the target, not the caster.** The reference's own diagram
shows the caster off to one side of it — `C-----T ------>L`, with the base A–B standing across the
far point L — so a cone cast at an adjacent square starts *there* and spreads beyond; the squares
between caster and target are not in it. The caster's position sets the direction and nothing else.
Points are tested against the triangle over the bounding box of its corners rather than the
triangle being rasterised, because "the Point-In-Triangle test is slow"; all three edges count as
inside, and the far point is placed at `length - 0.000001` so the last row does not sit exactly on
the boundary and get swept in.

> **A cone cast on the caster's own square produces nothing.** The direction is
> `sinT = (Ty-Cy)/D` with `D` the caster-to-target distance, so a zero distance divides by zero.
> The reference has no guard: it builds a NaN triangle, which contains nothing. Same result, and
> the port returns empty explicitly rather than arriving there through NaN comparisons.

**A line is always exactly one square thick.** Both width-taking overloads (`Drawtile.cpp:5574`,
`:5588`) test the width for being positive and then drop it — they call the two-point version
without passing it on — and the directional overload has the widening loop written out and
commented, under "need the following only if line width can be greater than 1". A spell's line
width comes from `MaxTargets`, so for these two shapes that field is a zero test and nothing more.

**Bresenham is run in pixels, not squares.** Both ends go through `TerrainToWorldCoord` (×48) and
every step is converted back, so the line is drawn at forty-eight times the resolution and then
quantised. That makes it *denser* than a tile-resolution walk, not sparser: at each diagonal
transition both squares are visited. From (10,10) to (12,11) a tile walk gives three squares and
never enters (11,10); the pixel walk gives four and picks it up. The conversion takes each square's
**top-left corner** — the `+ COMBAT_TILE_WIDTH/2` that would have centred it is commented out at
both ends — so the line runs corner to corner and leans up and left of where it looks. The walk
stops at the first square outside the map rather than skipping it.

##### Saving throws and spell targeting, as ported

`DoesSavingThrowSucceed` / `DidSaveVersus` (`Char.cpp:11862`, `:8316`) and `InitTargeting` /
`NeedSpellTargeting` (`Char.cpp:15549`, `Globals.cpp:4176`) — whether a spell is resisted, and what
it is allowed to land on. `UAF.Rules/SavingThrow.cs`, `UAFcore/SpellTargeting.cs`.

The reference states the save rule itself in the comment heading `DoesSavingThrowSucceed`: each
save type has a single value that rises with the target's level; roll a d20; **a roll below that
value fails the save** and the full effect lands; a roll at or above it saves, and then
`spellSaveEffectType` says what the save was worth.

- **Magic resistance is checked first, and counts as a save rather than a bypass.** A target whose
  d100 comes in at or under its resistance returns saved without touching the d20 — so a
  save-for-half spell still does half damage to a fully resistant target. Resistance is not
  immunity.
- **A roll equal to the score saves.** The test is `roll < score` for failure, so the boundary
  belongs to the target.
- **The score is capped at 20 but has no floor.** `max(score, 1)` sits inside the commented-out
  script block, so a save value of zero or less succeeds on any roll.
- **The save is rolled even against your own party.** The comment above the function says no save
  is needed on yourself or on a willing recipient and that "party members are always assumed to be
  willing" — but the guard implementing it is commented out with a dated note ("Requested by Eric
  20121017"). The comment describes an older engine.
- `ModifySaveRollAsTarget` is live and gives the target +2 for protection from the caster's
  alignment, +1 for a shield, +2 for displacement. **The attacker's half is dead**:
  `ModifySaveRoll` returns false without touching anything.

> **Two of the five save types cover almost everything.** `Sp` (spells generally) accounts for 340
> of 377, 112 of 117 and 283 of 318 spells in the three designs; `ParPoiDM` takes most of the rest.
> **`RodStaffWand` is used by no shipped design at all.** `NoSave` is the commonest save result by
> far (259 / 84 / 214), so most spells simply land.

**A THAC0-resolved spell essentially never lands, and that is reproduced.** The `UseTHAC0` branch
tests `diceRoll > AC - adjTHAC0`, where hitting armour class `AC` with THAC0 `T` needs the roll to
reach `T - AC`. The subtraction is the wrong way round: THAC0 18 against armour class 6 gives a
threshold of −12, which every d20 clears, and the branch that then runs sets
`noEffectWhatsoever`. It is reachable only when armour class exceeds the caster's THAC0, which no
ordinary combatant has. **15, 6 and 12 spells** in the three designs use it — around 4% of each
spell book, quietly inert. Kept, because a design was balanced against what ships.

> **The saving-throw script's bonus is silently dropped for four of the five save types.**
> `DidSaveVersus` takes a `bonus` parameter and the live code never reads it — its only use was
> inside the deprecated block that is commented out (`Char.cpp:8351`). So a design writing a
> `SavingThrow` script to grant, say, +2 against a spell gets nothing unless that spell also uses
> `UseTHAC0`, which is the one branch that does add it. Reproduced.

> **`SaveForHalf` is inert: it behaves exactly like `NoSave`.** `DoesSavingThrowSucceed` writes
> `changeResult / 2.0` into a `SAVING_THROW_DATA`, and **nothing ever reads that field again**.
> Inside `InvokeSpellOnTarget` the struct is used for exactly one thing after the call —
> `if (stData.noEffectWhatsoever) return` — and the effect's own `changeResult` is a different
> field on a different struct, rolled independently by `GetChange()`. So `SaveNegates` works,
> because it sets `noEffectWhatsoever`; half damage does not exist. This is consistent with the
> dated note in `AddSpellEffect` (`Char.cpp:11994`) recording that spell effects were being applied
> despite a successful save — fixed in 2014 by adding an `EFFECT_NONE` flag rather than by wiring
> the multiplier up. **23 / 6 / 24 spells** in the three designs declare save-for-half and get full
> effect. `SavingThrow.Resolve` still returns the 0.5, so the seam is one line from being honest
> whenever that is wanted; nothing in the port consumes it yet either.

**Targeting.** Ten modes, and the setup is a table:

- `Self` takes one target at no range; `WholeParty` takes the party size; `SelectedByCount` takes
  the evaluated quantity. **A range of zero means unlimited** (stored as 1,000,000), not zero.
- **`TouchedTargets` is given a range of 9999, not 1** — the line setting 1 is commented out beside
  it, and the reach is enforced by `m_maxRangeX`/`m_maxRangeY`, both 1. A one-square box rather
  than a radius: the same thing on a square grid, arrived at differently.
- `SelectByHitDice` **replaces** the target count with a hit-dice budget, setting `MaxTargets` to
  zero outright.
- **Out of combat every area shape becomes the whole party.** Each area branch has an `else`
  commented "acts like ttype=WholeParty" — units rather than squares, the party size as the cap,
  and the design's width and height dropped on the floor.
- **`MaxTargets` means two different things.** For the area shapes it caps how many combatants the
  area catches; **for the two line shapes it is the line's width in squares**, passed straight into
  `GetCombatantsInLine`'s width parameter (`Combatant.cpp:7999`).

> **"Friend" means the caster's own side, not the party's.** `C_AddTarget` tests
> `targ.GetIsFriendly() == this->GetIsFriendly()`, so a monster casting a friends-only spell
> reaches other monsters. Reading it as "the party" makes every enemy buff heal the party instead.

What is *not* ported is the area geometry: `GetMapTilesInRectangle` and its callers
(`Drawtile.cpp:4646`), a flood fill in quarter-square coordinates against two rotated half-planes,
with the circle, cone and both lines built on top. Its own header comment gives the convention —
**"Width is normal to casting direction; Height is parallel"** — and the rectangle is centred on
the target rather than anchored at a corner. That, plus applying the effects, is what remains of
casting.

##### The casting clock, as ported

`COMBATANT::CastSpell` and `PENDING_SPELL_LIST` (`Combatant.cpp:615`, `Spell.cpp:7713`) — when a
begun spell lands, and what stops it landing. **A player can now cast: the spell is chosen from the
book, the memorised copy is spent, and the caster stands there interruptible until it comes due.**
`SpellCasting.cs`, `SpellList.cs`, `Casting.cs`.

The reference's own comment block beside the arithmetic (`Combatant.cpp:652`) is the clearest
statement of the model anywhere in the codebase: ten initiatives to a round, one round to the
minute, ten rounds to a turn, a spell needing whole rounds or turns lands at the *end* of one, and
**any hit on the caster during the casting time voids the spell**.

- **A spell can never wait past the round it was begun in.** An initiative-timed spell whose
  casting time pushes it beyond `INITIATIVE_Never` is not deferred — it is re-timed to the end of
  *this* round. The reference's comment says so outright: "we certainly don't want to wait many
  rounds", with the older `waitUntil += (rnd+1)` commented out above it.
- **Zero casting time collapses to immediate whatever the type says.** All three timed branches
  test for it and rewrite themselves.
- **The memorised copy is spent when the spell is begun, not when it lands.** An interrupted caster
  does not get it back — which is what makes interrupting an enemy caster worth doing.
- **Activation does not resolve the spell; it gives the caster its turn back.** `SpellActivate`
  clears `turnIsDone` and pushes the caster onto the queue's tail; targets are chosen and effects
  applied when that turn comes round, still in `ICS_Casting`. That is why a spell three rounds in
  the making interrupts the initiative order when it lands.
- **The turn ends immediately for a pending spell and continues for an immediate one.** The
  reference splits on exactly `IsSpellPending()` (`RunEvent.cpp:17104`).

> **What the shipped designs actually use.** Initiative timing dominates: 99 of 117 spells in
> `ci-tier3`, 244 of 377 in `SomethingWild`, 199 of 318 in `Case`. Rounds and turns are rare
> (15/3, 38/5, 32/4), and **`ci-tier3` has no immediate spells at all**. The clock is the normal
> path through casting, not an edge case — which is worth knowing before treating the pending list
> as an optimisation.
>
> Initiative runs 9–18 (`INITIATIVE_FirstDefault`/`LastDefault`), so the `> INITIATIVE_Never`
> re-timing fires when casting time exceeds `23 − initiative`. About a quarter of initiative-timed
> spells can reach it on a high roll (24 of 99, 59 of 244, 46 of 199); none reach it on every roll.
> The branch is live, not theoretical.

**Two reference bugs, one kept and one not.**

The initiative branch's two `if`s are not exclusive where the shape of the code wants `else if`.
After an overlong spell is re-timed to `waitUntil = round`, the second test asks whether
`waitUntil == initiative` — comparing a round number against an initiative. When they coincide
(round 5, a caster on initiative 5, casting time 19+) the spell just deferred to the round's end is
marked immediate instead. **Kept**, because a design was tuned against what ships.

`ProcessTimeSensitiveData`'s `castIt` is declared outside its loop and never reset, so any entry
that activates leaves the flag set for every entry after it, activating those too regardless of
their own timing; and the returned value is whatever the *last* entry decided rather than "anything
activated". **Not kept** — the port resets per entry and returns the true answer, which is what
every caller means. The one caller (`Combatants.cpp:1641`) only uses the result to decide whether
to look at the turn queue, so the leak is invisible there and destructive anywhere else.

> **`DecMemorized`'s count argument is a zero test and nothing more.** It refuses when zero and then
> decrements by exactly one whatever it was — asking for five spends one. And `SetUnMemorized`
> returns early when `selected` is false (`Spell.cpp:1254`), so a spell not marked for
> re-memorisation is **cast without ever being used up**. `selected` means "will memorise this
> again"; it has no business gating the spend, but it does.

Casting is half ported. What is missing is resolution: choosing targets
(`CAST_COMBAT_SPELL_MENU_DATA`), saving throws, applying the effects, and the lingering-spell area
effects. An immediate spell therefore says so rather than pretending — see §11.

##### Aiming and the small commands, as ported

The AIM submenu and manual aiming (`COMBAT_AIM_MENU_DATA` and `COMBAT_AIM_MANUAL_MENU_DATA`,
`RunEvent.cpp:19952`, `:20052`), plus BANDAGE and per-turn announcements. **A player can now pick
their target rather than swinging at whatever the cycle lands on.**

- **AIM opens a submenu** — NEXT, PREV, MANUAL, TARGET, CENTER, EXIT — instead of attacking
  outright. MANUAL hands the arrow keys to the cursor; the menu gives them up while it does, which
  is why the mode has to be checked before the ordinary menu keys.
- **TARGET only commits when the attack is actually possible.** The reference clears the target and
  stays in the menu otherwise, so a player pointing at something unreachable is told rather than
  silently losing the turn. EXIT likewise costs nothing.
- The reference models these as pushed events that replace or stack on the main menu and pop when
  done; a `CombatMenuMode` on the session is the same shape without an event stack, which this port
  does not have.

> **`CanBandage` is just `!IsDone()`** (`Combatant.cpp:7074`) — the entry is offered whenever the
> combatant can act, and `COMBAT_DATA::Bandage` then finds a target or does nothing. An earlier
> revision here gated the *menu* on somebody being dying, which is stricter than the original.
> Bandaging **stabilises rather than heals**: zero hit points and unconscious.

Commands now working: MOVE, AIM (with the full submenu), GUARD, BANDAGE and END. USE, CAST, TURN,
QUICK, DELAY, VIEW, SPEED and the design's special action still report that they are not
implemented, which is deliberate — see the combat session section.

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

##### `RACE_DATA`, as ported

`races.dat` reads at `RaceV2` and above. Structurally a sibling of `BASE_CLASS_DATA` — ability
requirements, five skill lists, a `Specab` tail — with `DICEPLUS` ranges for weight, height, age and
movement in place of experience thresholds. Verified by whole-file walks: `SomethingWild`, `Case`
and `Ambassador's_Letter` at 12 races each and the CI-saved 5.29 design at 6, every one to exact
EOF, closing on the same `$$Help` sentinel the other two databases use.

`Ambassador's_Letter` yields its invented races — `Helmetpanther`, `Earthswork`, `Mutant`,
`Helmettiger`, `Cyborg`, `Android`, `Insectoid` — and **`Helmettiger` is exactly the race its custom
`ninja` baseclass is restricted to**, so the two databases corroborate each other.

Four things this file taught, three of them by failing first:

- **`ABL0` is a legitimate `ABILITY_REQ` version.** The editor reads an extra `DWORD` key for it;
  the engine rejects the record. `BaseclassRecordReader` shared the same leaf and rejected both —
  no `baseclass.dat` in the corpus had reached it.
- **Below `RaceV2` the five resistance fields are *derived*, not read** (`class.cpp:3100`): the
  editor computes them from the race's name — Human can change class, Elf finds secret doors — and
  consumes nothing. Reading them anyway eats twenty bytes that were never written.
- **The five skill lists are gated on the *design* version**, not the container tag, so a 0.915
  design skips them even with a modern `races.dat`.
- **`CAR::DeSerialize` is a third ASL path and its count is an `int`, not a `WORD`**
  (`class.cpp:12117`). Both `Serialize` twins agree on 16 bits, which made that look like a property
  of the format; `races.dat` goes through `DeSerialize`. The entries are identical, key fixup
  included — only the count width differs. `AslReader.ReadDeSerialized` covers it.

> **`RaceV1` is refused rather than read.** It is the one container shape where the editor and the
> engine genuinely consume different streams — the editor takes `preSpellNameKey` and derives the
> resistances, the engine does neither — and `DefaultDesign` is the only fixture, which cannot
> distinguish the two halves. Same call as `Bcd1` and `CL1`.

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
**The treasure picture is drawn, and it comes from the zone rather than the event**
(`RunEvent.cpp:6588`): `currPic = levelData.zoneData.zones[zone].treasurePicture`, with the zone
taken from the party's own cell. So two treasures on one level can look different and the event
carries nothing that says which. Every zone in the reference designs names `smallpic_Treasure.png`
and it resolves, so the screen now shows coins and a dagger where the corridor was. It blits through
the colour key like every other sprite — `blitView` asks for `SmallPicDib | SpriteDib`.

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
- ~~**The level cap is only half-implemented.**~~ **Closed.** `races.dat` now reads, and
  `LoadedDesign.LevelCap` combines the baseclass's `MaxLevel$SYS$` with the race's. **The smaller
  wins** — `GetLevelCap` builds its `SKILL_COMPUTATION` with `minimize = true` and `GetSkillValue`
  takes the lower of the two (`class.cpp:5215`) — and an absent cap is absent rather than zero, so
  the other side wins outright.
  <br>The corpus does exercise this: `SomethingWild`'s Elf defines `MaxLevel$SYS$ = 40`, which is
  `HIGHEST_CHARACTER_LEVEL`, i.e. "no practical cap" written out explicitly rather than left
  absent. A test asserting the race had no cap failed on it.

`LoadedDesign.IsReadyToTrain` now derives the roster's blue name from the thresholds rather than
reading the design author's stored flag, falling back to the flag when `baseclass.dat` cannot be
read — which is the real state for `DefaultDesign`, whose `Bcd1` the reference engine refuses too.

Third: the **encumbrance and movement tables** (`GameRules.cpp:2109`, `Char.cpp:5719`), as
`Encumbrance` — the first piece of `GameRules.cpp` in the port, chosen because it is entirely
self-contained: two tables over a strength score, no equipment or spell effects involved. The
character sheet now shows both.

- **The allowance is a table, not a formula** — the steps are irregular (350, 100, 200, 350, 500 …)
  and the exceptional-strength bands are irregular again. Strengths 19–25 are the project's own
  addition to the original rules.
- **A strength of 3 or below computes to exactly zero and is floored at 1.** That matters because
  the movement bands divide the carried weight by it; a zero would make such a character maximally
  encumbered no matter what they carried.
- **The percentile is read as a `BYTE`**, so 256 wraps to 0 rather than saturating at the top band.
- **The bottom movement band is 1, not 0** — a character loaded past four times their allowance
  still moves, barely.

> One deviation, recorded rather than hidden: the reference divides by the **effective**
> encumbrance, which ignores magical items (`determineEffectiveEncumbrance`), while this uses the
> stored total because item records are not resolved in the sheet builder. The two agree for a
> character carrying nothing magical, and this errs toward reporting a character as slower than
> they are.

Fourth: **THAC0**, as `Thac0` — the attack number, from each baseclass's own 40-byte table. The
sheet shows it, so three of its five derived fields are now filled.

- **There are two definitions of `getCharTHAC0` and only one is compiled.** They sit either side of
  `#ifdef OldDualClass20180126`, which is defined nowhere in the tree, so the `#else` half
  (`Char.cpp:6023`) is live and the other is dead. Checking before transcribing is the
  `ProjectVersion.h` lesson; here the two halves differ.
- **Lower is better, so the best baseclass wins.** The walk starts at 20 and keeps the *minimum*,
  which is why a fighter/mage attacks as a fighter.
- **A drained baseclass keeps the number it had**, through `previousLevel` rather than falling back
  to unskilled.
- **`CanUseBaseclass` is the dual-class rule** (`Char.cpp:7427`): a *previous* baseclass counts only
  once some current one has climbed **strictly** past the level it was abandoned at, and only an
  undrained baseclass can release it — two abandoned halves cannot release each other.

Fifth: **armour class**, as `ArmorClass`, with the readied-equipment resolution it needs. Four of
the sheet's five derived fields are now filled; only DAMAGE is left.

- **The base is dexterity alone**, and equipment is applied at every read rather than folded in.
  The header says why: `m_AC` was renamed in 2010 because "we used to change AC as a PC readied
  armor and such, but it was not changed for enemies who wore armor. This made things very
  confusing." The old line survives commented out in `SetCharAC`. Reading the stored field and
  calling it the character's armour class reproduces exactly the bug the rename ended.
- **There is no penalty for low dexterity** — one point per point above 14, and a flat 10 below it.
- **Two different "adjusted" values exist and neither has both adjustments.** `GetEffectiveAC` is
  base plus readied items; `GetAdjAC` is base plus spell effects. Nothing combines them. This is
  the former, since the spell-effect layer does not exist.
- **No slot rules at all.** `GetProtectModForRdyItems` sums every readied item, so a design that
  lets a character ready two suits of armour gets both.
- **"Readied" is a base-38 location that is not `NOTRDY`**, not a boolean.

> **Two layering bugs the screenshot caught and the tests did not.** The values rendered as
> `ARMOR CLASS7` — the layout places these fields at x offset zero from their label, so the
> `%5i` padding *is* the gap. And the zone's treasure picture was showing through the character
> sheet, because `OwnsScreen` was still true underneath it; the sheet covers the screen art as
> well as the roster.

Sixth: the **strength table** and **weapon damage**. **The character sheet is now complete** — all
five derived fields carry real values.

- **The strength table is generated from the C++ switch, not typed.** 24 rows of six numbers with
  irregular bands in both directions is exactly where a hand-copied digit hides forever, so it was
  extracted mechanically, the way `DesignVersion` and the GPDL opcode tables were. A test re-derives
  it from `GameRules.cpp` at run time and compares, so the two cannot drift apart.
- **`case 18` splits by percentile with an exact-zero case first**, then four bands, then everything
  at 100 and up — the same shape as the encumbrance table and with different numbers.
- **A negative damage bonus is never shown.** The reference tests `dmg_bonus > 0`, so a weak
  character's penalty simply does not appear.
- **`isMissile` is not "is this a bow".** It is set only when the weapon is a bow or crossbow **and**
  ammo is readied in the quiver, so a bow with an empty quiver still collects the wielder's strength
  bonus. That reads like an oversight; it is what the reference does.

> **The missile rule was caught by rendering, after the tests passed.** The sheet showed
> `DAMAGE 1D6+1` beside `Light Crossbow`, and a crossbow should carry no strength bonus at all —
> the +1 was one this port had added. Fifth time this session that looking at the frame found
> something a green suite did not.

Not ported from this screen: the magical-item term, `max(Attack_Bonus, Dmg_Bonus_Sm)` applied to an
identified magical item, because "is this item magical" is a specab question the port does not
answer yet.

Seventh: the **spell-effect arithmetic**, as `SpellEffects` — the layer every `GetAdj*` accessor
routes through, and the last piece of shared machinery between the ported rules and combat.

- **Two of the three modes replace rather than adjust, so order decides the answer.** A percentage
  effect sets the value to a percentage *of* the original — 50% of 10 is 5, not 15 — and an absolute
  effect discards it outright. So an absolute effect anywhere in the list wipes out every effect
  above it, and the list's order is part of the result.
- **`ApplyChange` returns the new value, not a delta**, despite the caller's comment saying
  "return accumulated delta" (`Char.cpp:13073`). Reading the comment rather than the function would
  make every effect compound.
- **`EFFECT_NONE` is checked first**, so a saving throw that negates a percentage effect leaves the
  value alone rather than multiplying it by zero.
- The clamp belongs to the accessor, not the effect: `GetAdjAC` holds the result inside `MIN_AC`
  and `MAX_AC` *after* applying effects.

Eighth: **the attack roll**, as `ToHit` — the first piece of combat, and the payoff for everything
above it. It consumes `Thac0`, `ArmorClass` and `Strength` and produces the number a d20 must beat.

- **Everything folds into one target number rather than into the roll.** Bonuses are *subtracted*
  from the attacker's THAC0 along with the target's armour class, which is why a better armour
  class — a lower number — raises the target while a bonus lowers it.
- **A target below `MIN_THAC0` becomes 0, not the floor.** The reference tests `< MIN_THAC0` and
  assigns `0` (`Combatant.cpp:5211`), so an absurdly favourable attack lands on "any roll hits"
  rather than on -500. Clamping to the constant would look tidier and be wrong.
- **Equalling the target hits**, and **there is no natural-20 rule** — a 20 is simply a high roll,
  and the special treatment a 20 gets elsewhere is a vorpal special ability, not an automatic hit.
- **Damage floors at 1** (`Combatant.cpp:5491`): an attack that lands always does something.

What is supplied rather than computed: the environmental bonus (range, cover, lighting) and the
weapon's to-hit bonus, both of which have their own sub-computations, and the dice roll itself.

Ninth: **initiative**, as `Initiative` — turn order within a round.

- **Lower acts earlier.** The order is an ascending sort, so the number is a position in the round
  rather than a score to beat. The range is 9–18, from `RollDice(10, 1, 8)`.
- **Surprise replaces the roll rather than modifying it.** A surprised side takes the last slot
  outright and the other the first; the die is never consulted. Treating it as a bonus would leave
  uncertain what the reference makes certain.
- **The sort must be stable, and .NET's `List.Sort` is not.** The reference bubble sorts with a
  strict `>` (`Combatants.cpp:1514`), so equal initiatives never swap — and ties are common with a
  whole party rolling on a ten-sided range. An unstable sort reorders them, changing who strikes
  first, and nothing would show it until a save game diverged. `OrderBy` is documented stable;
  `Array.Sort` and `List.Sort` are explicitly not.

> **What is deliberately not here: durations, sources and stacking.** The reference tracks each
> effect's parent spell, expiry time and once-only bookkeeping, and that is the part that decides
> which effects are in the list at all. This is only the arithmetic — enough for the character sheet
> and the combat numbers, and honestly short of an effect system.

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
- **One deviation, still open now that `classes.dat` reads.** The reference takes the split's `n`
  from the *class definition* and writes into the *character*'s own stats,
  dropping any share whose baseclass the character lacks. This port counts the character's own
  baseclasses. The two agree wherever a character's stats match its class, which the Phase 1 walk
  found to hold; they differ only for a record that disagrees with its own class. `ClassRecordReader`
  now supplies the true baseclass list, so closing this is a small change nobody has made yet.
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

Everything that once stood here is done: the `vcxproj` retarget, the dumper, `ProjectVersion.h`, the
solution scaffold, the tagged database record bodies, the forms layer and the levelling rules. What
follows is current as of the status block at the top.

### The next piece of work: the event layer's engine half

**Combat is wired end to end and playable.** Walking onto a combat event starts a fight: the
level's `CombatEvent` builds the encounter (`EncounterBuilder`), `CombatSetup` places both sides on
a map derived from the dungeon, `CombatRound` + `TurnQueue` order the turns, `MonsterAi` drives the
monsters, `CombatPathFinder` + `CombatMovement` walk them, `Targeting` + `Attack` resolve the
swings, `CombatUpkeep` bleeds the dying, `OpportunityAttacks` interrupts, `SpellDuration` +
`SpellEffectList` expire what was cast, and `CombatRenderer` draws all of it with the zone's own
art. A player takes their turn through `CombatSession` — **every command but SPEED** — and
**casting is complete**: the clock, saving throws, all ten targeting modes, the area
geometry, the effects and the clouds a spell leaves behind. Read the twenty-one "as ported"
sections under §7 Phase 4 before touching any of it.

What is left, in order:

1. **The event layer's engine half — the largest user-visible gap.** Every one of the 44 types now
   reads (§the event layer), and thirty-five execute. The other 9 draw
   `[<name> here -- not implemented]`, which is honest but is most of what a design author writes.
   In corpus frequency order, and with what each is actually waiting on:
   - ~~**`LogicBlock` (52)**~~ — **done, and running** (§a logic block, running). The gate
     network, the sixteen terminals, the twelve actions and the runner wiring, with a
     `GameLogicBlockHost` over `Game`. `LBIT_RunTimeIf` wants the runtime keyword table and the
     four GPDL terminals and actions want a script runner; everything else runs. This was the most
     frequent inert type in the corpus.
   - **The inner screens behind the town shells.** All seven shells run (§the town-service
     shells, complete). The **inventory** is done and on screen in the shop and the vault, with a
     row cursor, paging and the full ready rules (§the shared inventory, §the inventory on screen,
     §the ready rules); what is left there is its ten unbuilt commands — of which **DROP is the
     one to take next**, and it is not small: it drops the item into the level's
     `CELL_LEVEL_CONTENTS`, so it needs the level's mutable cell-contents table before it can do
     anything.

     The **party menu** behind the training hall runs, and with it **training** (§the party menu,
     and training). There is no separate character picker — an earlier note here said there was,
     and it was wrong: characters are chosen with TAB, and the shared screen is
     `MAIN_MENU_DATA`. Three of its twelve entries run.

     The **save and load screens** run (§the save and load screens), and taking them turned up
     the real blocker behind saving: the `.pty` reads and writes whole, but the engine keeps no
     live counterpart for **visited squares, event trigger flags, the journal, blockages or vault
     contents**, so saving is refused rather than done lossily.

     **All of that state is now tracked** — trigger flags (which also fixed a live bug:
     `OnceOnly` now works), visited squares, blockages and vaults; the journal turned out never to
     have been missing. See §event trigger flags, §visited squares, §blockages and vaults.

     ~~**What is left is `SaveGameProjection` itself.**~~ **Done — saving works** (§saving works).
     A game writes to a slot and loads back, and the port is playable across sessions.

     **ADD, REMOVE, DELETE and CREATE run** (§ADD REMOVE and DELETE, §the character generator's
     spine), so seven of the party menu's twelve entries do. CREATE gets as far as alignment.

     **Text entry is built** (§typed text), which turned `EnterPassword` on — **nine event types
     inert, not ten** — and took the generator to its name step.

     The **ability roll** is ported, `ability.dat` reads — **all seven databases now do**
     (§ability.dat) — and the deterministic half of `generateNewCharacter` is done (§what a new
     character starts with).

     **`DICEPLUS::Roll` is done, and it is now a transcription rather than a reconstruction**
     (§the re-measurement). `RDRCOMP` and `RDREXEC` are ~250 lines, not the 13k the file sizes
     suggested, so the tokeniser, the operator table and the postfix interpreter are all ported
     directly and the *script* half of GPDL stays in priority 3 where it belongs. The two
     evaluators this port had grown — one for spells, one for character creation — are one.

     **CREATE CHARACTER runs all ten steps** and writes a `.chr` with real ability scores, hit
     points and age (§the character generator's spine, §rolling a character against a real design).

     ~~**Next: MODIFY**, the same wizard re-entered over an existing character.~~ **Done, and it
     was never a wizard** — it is `CHOOSESTATS` over the active party member and nothing else
     (§MODIFY, which is not what the plan said it was). That screen was also the generator's one
     skipped step, so porting it closed both. **Ten of twelve party-menu entries now run.**

     **CHANGE CLASS is done too, and it decides nothing** — the engine delegates the whole
     question to two design scripts, and a design without them has a permanently dark entry
     (§CHANGE CLASS). **All twelve party-menu entries now run**, which closes the party menu.

     **Reloading the level on load is done**, and so are cross-level teleports — one extraction
     served both (§the level load).

     **The journal runs**, and SAVE, LOAD and TALK are wired from camp — **seven of camp's twelve
     entries** (§the camp screen's journal).

     **ALTER and the marching order run too** — **eight of camp's twelve entries** (§ALTER).

     **REST runs and now heals** — **nine of camp's twelve entries** (§REST, §time passing).
     What it still does not do is memorise spells, which wants the per-character spell list.

     **MAGIC's hub runs** — **ten of camp's twelve entries** (§MAGIC). Only FIX and QUIT are left,
     and FIX waits on spell casting.

     **The memorisation clock is ported** (§the memorisation clock), which is what REST's spell
     recovery and MAGIC's MEMORIZE both stand on.

     **The memorise working list is ported too** (§the memorise screen's working list) — slots,
     adjustments, and the three commands.

     **The MEMORIZE screen runs** (§the MEMORIZE screen), and a live character finally has a
     spell book to edit.

     **Resting memorises now**, and opening the rest screen fills in the duration and wakes the
     unconscious (§what opening a rest does). **REST and MEMORIZE are both complete.**

     **DONATE runs** (§the temple's donation), including its trigger chain.

     **HEAL runs too**, with the price scale the whole game shares (§HEAL). The temple is
     complete bar the casting.

     ~~**Next: the shop** — BUY and APPRAISE, which price items through that same scale.~~
     **APPRAISE runs** (§APPRAISE), off both of the temple's entries and the shop's, including the
     value the party can never roll — and a correction to whose flag decides that a service will
     not appraise a kind (§a correction).

     ~~**Next: BUY** — the shop's other entry.~~ **BUY runs** (§BUY), with the encumbrance rules
     the whole game shares and the wrong error a shop shows for a bundle it cannot carry. **The
     shop is complete bar SELL**, which lives on the inventory screen rather than the shop's menu.

     ~~**Next: FIX** — `party.FixParty(0)` from camp and `FixParty(1)` from HEAL, the same call in
     two environments.~~ **FIX is ported and both entries reach it** (§FIX), including the script
     default that turns out to be the engine's actual healing rule. It is **held back at the host**
     until spells resolve, because the loop is ended by the casting and a cast that resolves
     nothing never terminates — the reason is in the source, one line from switching on.

     **Camp is at eleven of twelve entries** (QUIT alone is unbuilt) and the temple is complete
     bar its own casting.

     ~~**Next: the spell resolution layer** — `CHARACTER::CastSpell` and the effect application
     behind it.~~ **The non-combat casting path runs** (§casting outside combat) — and most of
     what I expected to write was already ported for combat, so the round was the path into it
     plus the interface that lets both paths share one resolution.

     ~~**Next: how a spell effect reaches an attribute**~~ **Settled** (§the permanent branch):
     `AddSpellEffect`'s two branches are exclusive, a permanent spell writes the attribute and
     stores nothing, and that is what makes a cure move stored hit points. **FIX is switched on**
     and proven end to end. A remove-all divergence in `SpellEffectList.Add` was found and fixed
     on the way.

     **Camp is complete bar QUIT, and the temple bar its own CAST.**

     ~~**Next: the temple's CAST and MAGIC's cast entry**~~ **Both open the cast list now**
     (§the cast list), along with the parameter aliasing that decides what a spell's dice fields
     mean — where the field *names* turn out to be fossils that point at the wrong ones.

     **Camp is complete bar QUIT and DISPLAY**, and the temple is complete.

     ~~**Next: the non-combat target picker**~~ **It runs** (§the target picker), and with it
     **every non-combat spell in a design resolves** — the three picking modes included.

     **The town services are done.** Camp is complete bar QUIT and DISPLAY; the temple, the shop,
     the vault, the tavern and the training hall are complete.

     ~~**Next: Priority 3** — the ~250 GPDL sub-opcodes and the Forth VM.~~ **The hook harness is
     ported** (§running a record's own scripts): `FIX_CHARACTER` and `CanCastSpells` run against a
     record's real abilities, and the callback that decides how a hook chains turns out to be dead
     code in one of its two forms.

     ~~**Next: the sub-opcodes themselves** — 83 of ~387.~~ **The ability-score family is in**
     (§the ability scores): 21 opcodes, taking the VM to **104**, with the three layers a script
     reads a score through. The rest of the named hooks (`DOES_SPELL_ATTACK_SUCCEED`,
     `SPELL_CASTER_LEVEL`, the spell begin/end scripts, `SCRIBE_OR_WHATEVER`) are one call each on
     top of `SpecabScripts`, so the useful order stays opcodes first and hooks as they are needed.

     ~~**Next: the rest of `GET_CHAR_*` and the `SET_CHAR_*` setters**~~ **Done** (§the rest of the
     character block): 44 more, **104 → 148**, and the character block is complete apart from the
     per-baseclass calls that take an argument.

     ~~**Next: the party and combat families**~~ **The party block is in** (§the party block):
     17 more, **148 → 165**, including a setter that pushes nothing where all its siblings do.

     ~~**Next: the combat family**~~ **The combat queries are in** (§the combat queries): 12 more,
     **165 → 177**, including a stack imbalance that is the mirror of the party block's and is
     *conditional* on whether a fight is running.

     ~~**Next: two things, in this order.** First **the context calls**…~~ **The contexts are in**
     (§the script context): 4 more, **177 → 181**, and the two live hooks set theirs.

     ~~**Next: `GameScriptHost`'s combat side.**~~ **Done** (§backing the combat calls): the
     opcodes now answer from the live `CombatSession`, and two of the selectors turn out not to do
     what their names say.

     ~~**Next: the special-ability sub-opcodes**~~ **The introspection half is in** (§what a script
     can learn): 6 more, **181 → 187**, and the specab pair now reaches the script that reads it.

     **Next: the `SA_<record>_GET` lookups and their setters** — the other half of the family, and
     the first thing that needs a specab store the port can address *by record* rather than only
     the one currently running. `$SA_REMOVE` records its request rather than writing for exactly
     that reason, so the same piece unblocks both. After it: auras, the `DAT_*` database reads,
     and the script-calling opcodes (`ForEachPartyMember`, `ForEachPossession`) that drive
     `SpecabScripts` over a collection.



2. ~~**The rest of the archive writer.**~~ **Done — this was the largest structural gap in the
   port and it is closed.** All six record types write: monsters, items and spells each reproducing
   `ci-tier3`'s database byte for byte, characters round-tripping 29 records across two designs,
   `GLOBAL_STATS` round-tripping both designs whole, and every one of the 18 shipped levels
   round-tripping with byte identity. All four whole-file framings are written — `.chr`, `.lvl`,
   `game.dat` and `.pty` — and **Phase 1's round-trip exit criterion is met**. See §the archive
   writer's first layers and the six record-type sections.

   What is left of the writer is the **fourteen event types that appear in no shipped design**.
   Nothing is blocked on them, and they want `Export(JWriter&)` read alongside `Serialize`, since
   there is no corpus to check a guess against.

   The rule the whole exercise turned on, for anyone extending it: the storing branch has **no
   version gates**. Six record types in it held every time, and the two apparent exceptions are
   gates that cannot close — one open by construction (§the third record type) and one whose
   condition says `|| car.IsStoring()` out loud (§the fifth). The other rule earned here is that
   **writing the inverse finds reader defects nothing else does**: nine discarded fields, two
   mis-named ones, two lists whose shape hid which entry was missing, and one place where the
   reference's own two branches disagree.
3. **The rest of the GPDL sub-opcodes.** The attribute family now runs against real game state
   (§a script that can reach game state) and is the proof the seam works; the other ~250 calls —
   character stats, party queries, combat state — still throw with a citation. They are individually
   small and collectively large, and each needs the port to have the state it asks about.
   The attribute family is done — global, party and per-character — and nine character stats with
   it, including both adjusted forms now that characters carry spell effects
   (§spell effects on a character outside combat). What is left in this family wants state the port
   does not have: the ability-score calls need `baseclass.dat`, which has no reader, and
   `$GET_CHAR_EFFAC` needs the attacker as well as the target.
4. **The Forth VM** — a real subsystem, and now a smaller prize than it looked: its only consumer
   is a script that is the same in every shipped design bar one line
   (§the monster AI's priority ordering), and that script's decision function now runs in combat.
   What still needs it: a design that edits `AI_Script.BLK`,
   the `TURN_ATTEMPT` hook that turning undead depends on
   (§turning, delaying and automatic), and a monster's own choice of spell targets
   (§choosing a spell's targets).

Expect the GPDL script hooks to keep being the ragged edge: `IS_COMBAT_READY` and `IS_VALID_TARGET`
are already stubbed permissively (see the combatant and targeting sections), `ON_STEP` is skipped
in movement, and the scripted AI needs Forth.


#### The GPDL wiring that monster placement did *not* need

It turned out to be smaller than expected and is still worth doing, but nothing is blocked on it.
The design's `CombatPlacement` script only selects between six fixed program strings, all of which
are built in — so the engine runs without GPDL, and a design that authors its own placement script
is the only thing currently missed. To close it: a `specialAbilities.txt` parser, the
`SUBOP_GET_PARTY_FACING` and `SUBOP_MonsterPlacement` sub-opcodes (both trivial — the first is
`GET_LITERAL_INT(party.facing)`, the second hands its string to `TurtlePlacement.Run`), and two new
`IGpdlHost` members. `CombatSetup.Begin` already takes the program as a parameter, so it is a
call-site change.

`GenerateOutdoorCombatMap` (`Drawtile.cpp:2892`) is also unported. It shares the three-pass shape
but randomises terrain from `WildernessTileDensity` instead of reading the level, and the
wilderness half of the expansion table is already transcribed and unreachable — the 35
`DBL_HIGH_*` / `SGL_HIGH_*` junction types. Nothing needs it until outdoor encounters run.

**The spell-effect layer is complete.** `SpellEffects` has the arithmetic, and
`SpellDuration` + `SpellEffectList` now have the durations and stacking it was missing — see the
"as ported" section. What remains unported there is the *casting* half: choosing a spell, saving
throws, and the lingering-spell area effects (`activeSpellList.LingerSpell*`), which movement and
the round both call and neither has.

### Standing gaps, none of them blocking the above

| Gap | Why it matters | Size |
|---|---|---|
| ~~**`ArchiveWriter`**~~ | **Done.** All six record types, every shared leaf, both halves of the `CAR` write path, 30 of the 44 event bodies, and the four whole-file framings — `.chr`, `.lvl`, `game.dat`, `.pty`. Three shipped databases are reproduced byte for byte and everything else round-trips. **Phase 1's exit criterion is met**, and Phase 5 is unblocked | — |
| **GPDL reference bytecode** | `oracle/golden/gpdl/` holds 4 scripts and **0 `.bin` goldens**, so `GpdlOracleDiffTests` returns early. Phase 2's exit criterion cannot be demonstrated without them. Needs only a Windows oracle run | Small |
| **10 event types are read but not executed** | Every type now has a reader and 34 execute. **All seven town-service shells run.** What is left is the roughly dozen **inner** screens behind them — save, load, magic, rest, alter, journal, character-picking, items, buy, appraise, heal, donate — plus `EnterPassword` (needs text entry), `EncounterEvent` (the monsters-approaching loop) and `PlayMovieEvent` (the FFmpeg adapter) | Large |
| **`ability.dat`, `spellgroups.dat`, `traits.dat`** | The last unread databases. Framing reads; record bodies do not. Nothing currently needs them | Small |
| **~250 GPDL sub-opcodes, and the Forth VM** | Each throws `NotSupportedException` naming its source line. The Forth VM is not started | Large |
| **Global script hooks** | **`CombatPlacement` is done** — the parser, `RunGlobalScript`, both sub-opcodes and the call site. `PartyArrangement` and `PartyOrigin<direction>` remain: both have faithful built-in defaults and are call-site changes now that the bridge exists | Small |
| **`GenerateOutdoorCombatMap`** | Outdoor encounters have no map. Same three-pass shape, but randomised from `WildernessTileDensity`; the wilderness expansion cases are already transcribed | Medium |
| **Per-cell wall/blockage overrides** | The 5.x `WALL_OVERRIDE_INDEX` / `BLOCKAGE_OVERRIDE` tables win over a cell's own values in both the viewport and the combat map, and neither consults them. Read, but not threaded through. Every shipped design's tables are empty | Small |
| **FFmpeg adapter, `UAF.Media.Avalonia`** | Video degrades to a skipped cutscene, which is the intended contract. Avalonia is Phase 5's concern | Small / deferred |
| **`UAFcore.App` split** | `UAFcore` is currently the executable. Must happen before Phase 4b; `Game` is already written to survive it | Small |

### Rules that have earned their place

- **Read the loading branch from its start, not from where a search lands.** Every serialization
  bug in this port came from transcribing a fragment. The `races.dat` reader failed three times in
  one sitting for exactly this — and the legacy undead-type ordinal was stored unconverted in
  **two** readers written months apart, monsters and characters, found both times only by writing
  the inverse.
- **Check whether the code is live before porting it.** `ProjectVersion.h`, `MultiBoxTextAction`,
  one of the two `getCharTHAC0` definitions, one of the two `findEmptyCell`s, the whole A\*
  pathfinder, `ComputeDistanceFromParty`, `CombatantsStateText` (the *plural* one) and the entire
  morale computation are all dead. The `#ifdef` that decides is often nowhere near the function —
  and **four of those are dead not by `#ifdef` but by having no reader, or by one hard-coded
  assignment upstream**, which only a search for consumers finds. `CheckMorale` is the clearest
  case: forty lines of arithmetic feeding a variable the next line overwrites with `FALSE`.
  **A fifth way, found writing spells: commented out in the only branch that could reach it.**
  `DICEPLUS`'s whole numeric storing path is `/* … */`, so `ADJUSTMENT` and `GENERIC_REFERENCE`
  have no reachable writer at all; `SPELL_DATA::CompileScripts` is a hundred lines of which every
  active statement is `Binary.Empty()`. Both look live to a grep and are not.
- **Cite the function you actually ported, and check the citation.** A first draft of
  `CombatRound.EndTurn` was invented whole and attributed to `Combatants.cpp:6187`, which turned
  out to just delegate to the combatant. Reading it revealed the real mechanism — `getNextCombatant`
  pulling by initiative — which is a different design. The wrong citation was the tell.
- **When a test fails, check the assertion before the code.** Six times now the port was
  right and the test encoded an assumption — `$$Help`, the zero-width row marker, the golden frame,
  the shared spell rows, a level-9 magic user's THAC0, and a guessed ratio between two combat maps.
- **A passing test proves nothing when its fixture and the code share an author's misreading.**
  The N,S,E,W wall order survived three green tests for exactly that reason. Prefer an assertion
  over real data — a shared edge read from both sides settled it in two lines.
- **The same struct can use two field orders.** `AREA_MAP_DATA` stores backgrounds in compass
  order and walls in N,S,E,W, with a different accessor for each. Check the declaration, not the
  neighbouring field.
- **Count a long tail before choosing an order in it.** The first tranche of event writers was
  picked because eight subclasses shared a file and were short; they were 9% of a real level. One
  histogram showed a single type — `TextStatement` — was 73%. "Small and self-contained" and
  "frequent" are unrelated properties, and only one of them is worth ordering by.
- **Generate tables, do not type them.** `DesignVersion`, the GPDL opcodes and the strength table
  are all extracted from the C++ mechanically, and the strength table has a test that re-derives it
  from `GameRules.cpp` at run time so the two cannot drift.
- **Refuse a shape rather than guess at it.** `Bcd1`, `CL1` and `RaceV1` are all refused with a
  message naming the version, because each is a container the only available fixture cannot
  distinguish the editor's reading of from the engine's.

### Decisions taken

| Decision | Choice | Rationale |
|---|---|---|
| Reference oracle | **GitHub Actions `windows-latest`** | No local Windows needed; host is arm64, where an MSVC VM would be emulated |
| Audio backend | **SDL3 audio + MeltySynth** | Permissive licensing resolves the GPL v2 / BASS conflict; SDL3 is well-tested on arm64. Extended: SDL3 also provides the game's window, presentation and input (see §6) |
| Format compatibility | **Read all versions, write current** | Matches original behaviour; preserves ~25 years of community designs |
| Post-Phase-1 priority | **UAFedit first** | A read-only inspector validates the data layer end-to-end and grows into the editor; visible cross-platform result in ~2 months instead of ~8 |
| Video decoding | **FFmpeg / libav** (LGPL build) | Only realistic way to decode the legacy VfW codecs in existing designs (Cinepak, Indeo, MS-Video1); optional at runtime, and in its own assembly because the C# bindings are LGPL v3 (§6.1 correction) |
