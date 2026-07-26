# Dungeon Craft → .NET 10 / Avalonia Porting Plan

**Targets:** `UAFWin` → **UAFcore** (game engine + player), `UAFWinEd` → **UAFedit** (design editor)
**Stack:** .NET 10, C#, Avalonia 11.x, cross-platform (Windows / macOS / Linux)
**Status:** Plan — no code written yet
**Date:** 2026-07-25

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

### 3.2 Container layout: two shapes and an archive switch

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
`"ClassV1"`, `"RaceV1"`, `"SpGrpV1"`, `"TraitV1"` — then `01` and a count. **No magic and no
version double**; these newer DBs carry their schema version in the tag suffix, so the
`DesignVersion` gates do not apply to them at all.

**Orthogonal to both: the archive layer is version-selected, in three tiers, with per-file-type
thresholds.** This is the single easiest thing in the format to get wrong.

| Tier | Archive | items.dat (`Items.cpp:3424`) | level/game (`Level.cpp:2168`) |
|---|---|---|---|
| 1 | plain `CArchive` — no wrapper, no LZW, no string interning | `< 0.697` | `< 0.573` |
| 2 | `CAR`, **uncompressed** | `[0.697, 0.930)` | — |
| 3 | `CAR` + LZW (`Compress(true)`) | `>= 0.930` (`_SPECIAL_ABILITIES_VERSION_`) | — |

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
> branches. Until then, treat `CarLzwDecompressor` as transcribed-but-untested: the algorithm is
> faithfully derived from `class.cpp:12215`, but no byte of real compressed data has passed
> through it.

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
    UAF.Media.Avalonia/          Avalonia/Skia implementation of UAF.Media
    UAF.Import.Frua/             DOS FRUA importer: GEO/MONST/STRG/GAME .DAT, GLB, PCX/LBM
    UAFcore/                     engine: GameEvent tree, task scheduler, combat, viewport,
                                 tile rendering, forms                        [was UAFWin]
    UAFcore.App/                 Avalonia shell for the player
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
| Video for Windows | deferred; FFMediaToolkit if needed | only 12 `PlayMovie` call sites |
| zlib | `System.IO.Compression` | note: raw deflate vs. zlib header — verify framing |
| libpng 1.0.8 | `SkiaSharp` or `ImageSharp` | 36 `.png` references |
| `regexp.cpp` | `System.Text.RegularExpressions` | verify the dialect matches; it is a Spencer-style engine, not PCRE |
| `AfxBeginThread` / `CThread` | `Task` / dedicated `Thread` | §4.4 |
| `SharedQueue.h` + `CRITICAL_SECTION` | `System.Threading.Channels` | direct conceptual match |
| `Stackwalker.cpp` | `System.Diagnostics.StackTrace` | drop entirely |

**On rendering:** the original is a software blitter — surfaces, source colour keys, palette
manipulation, per-pixel `GetColorAt`. GPU abstractions (MonoGame, Veldrid, Silk.NET) fight this
rather than help. **Recommendation: keep it a software blitter.** Render into a managed
`byte[]`/`uint[]` framebuffer with the same colour-key semantics, then present it as an Avalonia
`WriteableBitmap` inside a custom `Control`. This preserves pixel fidelity — which for a
faithful port of a 1993 game is the point — keeps `Drawtile.cpp` and `Viewport.cpp` mechanical
to translate, and makes rendering unit-testable by hashing the framebuffer. Use Skia only for
final scaling/presentation.

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
4. Build the JSON dumper. **Implement it as a `--dump-json` mode inside `UAFWinEd`, not as a
   standalone project.** `Shared/GlobalData.cpp` includes headers from *both* apps
   (`UAFWinEd/UAFWinEd.h`, `UAFWinEd/resource.h`, `UAFWin/Dungeon.h`) plus `Graphics.h` and
   `SoundMgr.h`, so a separate tool would have to untangle that include graph before dumping a
   single byte. The editor project already compiles all of it. The load path itself is GUI-free —
   `BOOL loadDesign(LPCSTR name)` (`Level.cpp:3309`) is a free function — so the mode branches
   early in `CUAFWinEdApp::InitInstance` (near the existing `ParseCommandLine` at
   `UAFWinEd.cpp:979`), loads, dumps canonical JSON (stable key order, invariant number
   formatting), and exits before any window is created.
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

Available fixtures: `reference/example_dsn/SL4-FATH.DSN` (a community FRUA campaign — 179 files,
22 levels, 36 monsters, 118 tile libraries) and the Steam-bundled `HEIRS.DSN` / `TUTORIAL.DSN`.
All three are FRUA, not Dungeon Craft.

**Filename case is not consistent, and this breaks the port off Windows.** `SL4-FATH.DSN` contains
both `8X8D1009.TLB` and `8x8d0315.TLB`, both `.TLB` and `.tlb`, plus `Back*.tlb`. Windows hides
this; macOS and Linux do not. Any importer code building a name with a fixed-case format string
(`"8X8D%04d.TLB"`) will silently fail to find half the tile libraries. Case-insensitive asset
resolution therefore belongs in **Phase 6, not Phase 7 polish** — and it needs a lookup that
resolves against a case-folded directory index rather than trusting the constructed name.

### Type traps found while reading real files

Confirmed against `DefaultDesign.dsn/Data/game.dat` by walking
`GLOBAL_STATS::Serialize(CArchive&)` (`GlobalData.cpp:3862`) field by field:

- **`BOOL` is a 4-byte `int`, and is not always boolean.** `AutoDarkenAmount` is declared `BOOL`
  in `GlobalData.h` but holds **256** in the fixture — it is an integer amount wearing a `BOOL`
  type. Mapping the C++ `BOOL` fields onto C# `bool` would silently destroy the value. Model
  every `BOOL` as `int` at the serialization layer and only interpret higher up.
- **`maxParty_maxPCs` packing is not universal.** The accessors treat it as
  `(partySize << 16) | maxPCs`, but the fixture's raw value is `8`, which unpacks to
  `partySize = 0`. Either the packing postdates this design or it is version-gated somewhere not
  yet traced. The dumper emits both the raw and unpacked forms precisely so the oracle can settle
  this.
- **Empty strings are sentinel-encoded.** The `AS`/`DAS` macros (`Externs.h:1937`) substitute
  `ArchiveBlank` — `"*"` (`Globals.cpp:167`) — for empty strings. `DAS` accepts a literal `"*"`
  *as well as* the configured sentinel, because released builds shipped with `"*"`; dropping that
  leniency turns empty strings into literal asterisks in affected designs.
- **`logfont` is a raw struct blob** — `ar.Write(&logfont, sizeof(logfont))`, i.e. a 60-byte
  `LOGFONTA` written verbatim, not field-by-field. It must be read as fixed-size bytes.

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
| Audio backend | **SDL3 audio + MeltySynth** | Permissive licensing resolves the GPL v2 / BASS conflict; SDL3 is well-tested on arm64 |
| Format compatibility | **Read all versions, write current** | Matches original behaviour; preserves ~25 years of community designs |
| Post-Phase-1 priority | **UAFedit first** | A read-only inspector validates the data layer end-to-end and grows into the editor; visible cross-platform result in ~2 months instead of ~8 |
