# Dungeon Craft file format — reference

What the archive format actually is, as established by reading `src/Shared` against real design
files and cross-checking every claim against the C++ oracle. This is the *reference*; the porting
strategy and schedule live in [PORTING-PLAN.md](PORTING-PLAN.md).

Every rule here has a citation. Where a rule was learned by getting it wrong first, that is said
so — the mistakes are the useful part, because each one produced plausible-looking output rather
than an error.

---

## 1. The cardinal rule

> **Transcribe from the loading branch, never the storing branch.**

Almost every `Serialize` is `if (ar.IsStoring()) { … } else { … }`, and **the two halves are not
mirror images**. The storing branch is frequently the *newer* of the two: it writes today's layout,
while the loading branch still understands a decade of older ones. `ITEM_DATA::Serialize` even
opens its storing branch with `die("We should not be serializing itemdata with CArchive")`
(`Items.cpp:2348`) — code that cannot run, describing a format that is never produced.

A reader transcribed from the storing branch will read modern files correctly and every older file
wrongly.

---

## 2. Version numbers

Versions are **doubles**, compared with `>=` against 97 named constants in `Externs.h` plus a
number of bare literals. They are waypoints, not an enumeration: real designs carry versions that
match no constant (2.53, 3.55), and some gates exist only as inline numbers (`0.999647`,
`0.930279`, `0.998918`).

**`DesignVersion.All` is therefore not the set of valid versions.** Treating it as such — for
example, to validate a file — rejects real designs.

Two constants worth memorising, because they gate the largest behavioural forks:

| Constant | Value | Gates |
|---|---|---|
| `VersionSpellIDs` | 0.998100 | editor art block in `ITEM_DATA` |
| `VersionSpellNames` | 0.998101 | legacy-conversion branches throughout |

Beware unpadded names: **`_VERSION_524` is 5.24, not 0.524** (`Externs.h:174`). Neighbouring
constants are written `_VERSION_0524_`, so the shape of the name is not a reliable guide.

`DesignVersion.Generated.cs` is transcribed from `Externs.h` and `Globals.cpp` by
`dotnet/tools/gen-design-versions.py`, and CI fails if it drifts.

---

## 3. Four container framings

There is no single header. Which framing applies depends on the file, and two of them are
distinguished only by content.

### 3.1 Plain `game.dat`

No magic. The first 8 bytes **are** the version double, followed immediately by the payload.

### 3.2 Magic `game.dat` — compression starts mid-stream

```
0xFABCDEFABCDEFABF   (8 bytes)
version              (8 bytes, uncompressed)
compressType         (1 byte)
version              (8 bytes, ← now inside the LZW stream)
payload…
```

The version appears **twice**, and the second read comes from the compressed stream. This is not a
container-vs-content distinction — they are the same value, read twice, once either side of the
compression boundary.

> Getting this wrong produced binary noise from an integration probe while every per-type unit test
> continued to pass, because the databases in the same folder are framed differently.

### 3.3 Databases (`items.dat`, `monsters.dat`, `spells.dat`)

Magic, then version, then `compressType`, then the payload — compression enabled from the payload's
first byte.

### 3.4 Tagged databases (`ability`, `baseclass`, `classes`, `races`, `spellgroups`, `traits`)

Unlike anything else (`class.cpp:3489`):

```cpp
car >> version;                  // a STRING tag, e.g. "RaceV1" — no version double at all
if (version > "RaceV0")          // LEXICOGRAPHIC comparison gates compression
    car.Compress(true);
count = car.ReadCount();
for (count) data.Serialize(car, version);   // records receive the STRING
```

Three things are unique: the version is a string, the compression gate is a string comparison, and
`DesignVersion` does not apply at all. Modelling these files with a numeric version is a category
error.

Note also that `CAR::Compress(true)` always *writes* 2, yet every tagged database on disk carries
**1**. That is not cosmetic — see §4.2.

---

## 4. Three archive tiers

| Tier | `compressType` | Encoding |
|---|---|---|
| 1 | — | plain `CArchive` |
| 2 | 0 or 1 | `CAR`, no compression |
| 3 | 2 | `CAR` + 13-bit LZW |

### 4.1 The `CArchive` and `CAR` overloads are different readers

**This is the single most expensive trap in the format.** Most classes define `Serialize(CArchive&)`
*and* `Serialize(CAR&)`, and they are not the same function:

- `ITEM_DATA` reads `preSpellNameKey` under different gates in each.
- `PIC_DATA` reads a `style` field at ≥ 0.900 on the `CAR` path (`PicData.cpp:203`) that the
  `CArchive` path has **commented out** (`PicData.cpp:139`).

Worse, **which overload applies is not the same question as whether the file is compressed**. An
archive with `compressType` 0 or 1 still runs the `CAR` code path — just without LZW. So tier 2
files use `CAR` field order with plain encoding.

> The `PIC_DATA` divergence cost a debugging session. Four bytes, no marker, and every field after
> it still decoded to plausible values: item record 0 read perfectly with `style` missed, and the
> damage only surfaced 12 bytes later as an impossible message count. `PicDataReader` therefore
> takes an explicit `PicArchiveVariant` rather than inferring one.

### 4.2 Compressed `CAR` is a different *encoding*, not LZW bolted on

Beyond compression, `CAR` **interns strings**. Each string is written as a `uint` index; index 0
means "new", followed by a 4-byte length and the bytes. Any other value is a back-reference into a
table built from the start of the stream.

Consequences:

- A compressed structure **cannot be read by seeking to it**. The plain encoding of the same block
  is self-describing; the compressed one is not.
- The string reader gates its embedded-NUL handling on `compressType > 1`, so type-1 streams intern
  NUL-bearing strings that type-2 streams deliberately do not. Getting this wrong shifts every
  later table index.

LZW details: 13-bit codes in 52-byte blocks (416 bits = 32 codes), 8190 resets the table, 8191
ends the stream. The C++ decoder over-reads its input buffer on the final block; zero-padding the
block reproduces its behaviour without the undefined read.

### 4.3 `ReadCount` is not `ReadUInt32`

`CAR::ReadCount` (`class.cpp:11707`) delegates to MFC's `CArchive::ReadCount` — a `WORD` that
escapes to a `DWORD` on `0xFFFF` — **only when `compressType` is 0**. Types 1 and 2 read a flat
`DWORD`.

So the same call site consumes **2 bytes in a tier-2 archive and 4 in a tier-3 one** for identical
small counts. Nothing in the record signals which; it follows from the container. A reader with one
fixed count encoding will work on one tier and silently drift on the other.

Note this is *not* the same as "counts are 4 bytes": collection counts written with `operator<<`
are plain ints everywhere. Only `ReadCount` call sites behave this way — `DICEPLUS` adjustments use
it, while `BASECLASS_LIST` does not.

---

## 5. Strings

### 5.1 Counted, MBCS, Windows-1252

`AfxWriteStringLength` encoding: a length byte; `0xFF` escapes to a `WORD`; `0xFFFF` escapes to a
`DWORD`. This is an MBCS build, so `CString` is single-byte — **Windows-1252, not UTF-8**.

### 5.2 The `DAS` blank convention

```cpp
#define DAS(archive,cstring) { archive >> cstring; \
  if ((cstring==ArchiveBlank)||(cstring=="*")) cstring=""; }
```

An empty string is stored as `"*"` and decoded back to empty. **This is applied selectively.** Some
readers use `DAS`, others read verbatim — `A_CStringPAIR_L` (§7) does not decode, so a `"*"` there
stays `"*"`. Applying it uniformly is wrong in both directions.

### 5.3 Strings that look like integers

Several ID types derive from `CString` and therefore take the *string* path:

| Type | Declared |
|---|---|
| `SPELL_ID` | `Externs.h:1324` |
| `BASECLASS_ID` | `Externs.h:1222` |

> `SPELL_ID` was mis-modelled three times — skipped entirely, then read as an int. **Both wrong
> versions produced readable output.** Only the oracle diff settled it. If a field name reads like
> an identifier, check whether its type derives from `CString` before assuming it is numeric.

---

## 6. ASL — attribute/string lists

Almost every major class ends with `*_asl.Serialize(ar, "…_ATTRIBUTES")`, so **no record can be
read to completion, and no reader can advance to the next record, without this** (`ASL.cpp:1386`):

```cpp
if (version >= 0.505)          // _ASL_LEVEL_; below this NOTHING is read — not even a count
{
    ar >> mapName;             // must equal the expected literal
    ar >> count;               // WORD — 16 bits, not 32
    for (count) { key; flags; value }
}
```

Five properties, each verified against real files:

1. **The map name is a sync marker, not a label.** The reference throws on mismatch
   (`ASL.cpp:1420`). Preserve that: a misaligned reader essentially never reproduces the exact
   expected string, making this the one built-in checkpoint the format offers.
2. **The block carries payload.** `MissileArt` was migrated into the attribute map rather than
   given a version gate (`Items.cpp:2627`), so an ASL cannot be read-and-discarded.
3. **Entries are hash-ordered.** The container is a `CMapStringToPtr` walked with `GetNextAssoc`.
   The same four `GLOBAL_STATS` keys come out as `RunAsVersion, GuidedTourVersion,
   SpecialItemKeyQtyVersion, ItemUseEventVersion` uncompressed, but `GuidedTourVersion,
   ItemUseEventVersion, RunAsVersion, SpecialItemKeyQtyVersion` in every compressed design tested.
   **Look entries up by key, never by index**, and compare round-trips as sets.
4. **The compressed path applies a key fixup the plain path does not.** Characters below 0x20 get
   +0x20 (`ASL.cpp:1236`); the `CArchive` twin at `:1247` reads verbatim. The same key can differ
   between a compressed and an uncompressed design.
5. **Two write paths, one read format.** `Serialize` writes every entry (design files); `Save`
   drops anything flagged `ASLF_READONLY` (savegames, `ASL.cpp:1489`). Since every
   `GLOBAL_STATS_ATTRIBUTES` entry is `0x05` (`READONLY|DESIGN`), a savegame correctly writes a
   count of **zero** there.

Flags (`ASL.h:143`): `READONLY 1`, `MODIFIED 2`, `DESIGN 4`, `SYSTEM 8`; `EDITOR = READONLY|DESIGN`.

### The map names follow no convention

There are **seventeen**, listed in `AslMaps`. Most end in `_ATTRIBUTES`, but four end in `_ATTR`
(`EVENT_DATA_ATTR`, `EVENTCONT_ATTR`, `STEPEVENT_ATTR`, `TIME_EVENT_ATTR`) and two have no suffix
at all (`TALE`, `TAVTALE`).

> This list was first built by grepping for `"[A-Z_]+_ATTRIBUTES"`. That returned twelve names and
> looked complete — every record type ported at the time was covered. The five it missed are all in
> the event classes, so the gap would only have surfaced on the first event read, as a map-name
> mismatch with no obvious cause. **Grep for the call, not for the literal:**
> `_asl.Serialize(…, "…")`.

---

## 7. Special abilities

`Specab.cpp` runs to 2,240 lines, but the serialized shape hangs off one gate (`Specab.cpp:1155`):

```cpp
if (version <= 0.920 && !ar.IsStoring())   // legacy conversion
else  m_specialAbilities.Serialize(ar);    // an A_CStringPAIR_L
```

**The gate is asymmetric.** The legacy branch is conditioned on `!IsStoring()`: old designs are
*read* in the old shape but always *written* in the new one. A port treating this as a symmetric
format fork will write files the reference cannot read.

Both branches are live. DefaultDesign (0.915) takes the legacy path; designs at 2.53, 3.55 and 5.28
take the modern one.

### The modern form — `A_CStringPAIR_L` (`ASL.cpp:1848`)

An `int` count, then key/value string pairs. No map name, no flags byte, no `DAS` decoding.

Two contrasts with its sibling ASL, which lives in the same file and is easy to conflate: it counts
with a 32-bit `int` where ASL uses a `WORD`, and it reads strings verbatim where ASL's legacy
branch applies `DAS`.

**Empty keys occur in real designs and must be tolerated.** `A_ASLENTRY_L::Update` refuses an empty
key (`ASL.cpp:1311`), which makes "keys are never empty" a tempting invariant — but
`A_CStringPAIR_L::Serialize` inserts whatever is on the wire. A reader that validates non-empty
keys rejects designs the reference loads without complaint.

### The legacy form

`int count` (clamped at 0, never rejected), then either bare `WORD` ordinals below 0.850, or one
slot per ability type carrying scripts and up to 14 messages. `NUM_SPECIAL_ABILITIES` is 32, so a
count of 32 is normal. An exact-equality gate — `version == 0.850` — adds one unused `int`; it is
one of the few places the format tests a single version rather than a range.

A message count above 14 is fatal in the reference (`die(0xab537)`). **Mirror that rather than
clamping**: it is what turns a silent byte-level drift into an immediate, locatable failure.

---

## 7a. Dice expressions

`DICEPLUS` (`class.cpp:2494`) is **self-versioning by string tag**, like the tagged databases and
unlike everything else — the surrounding `DesignVersion` does not select the branch:

| Tag | Layout |
|---|---|
| `DP2` | two strings (`m_Text`, `m_Bin`) and nothing else — the modern form |
| `DP1` | `char`, `BYTE`, `char`, `int`, `int`, `char`, then adjustments |
| `DP0` | as `DP1` but the clamps are `BYTE` on the wire, widened into `int` fields |

The three are structurally unrelated, so a reader that assumes the numeric layout desynchronises
badly on any modern design, where every dice expression is `DP2`.

**The numeric fields are one byte.** `m_numDice`, `m_bonus` and `m_sign` are `char`, `m_numSides`
is `BYTE` (`class.h:842`), despite names that read like integers. After reading, a negative dice
count is normalised to a positive count with `sign = -1` (`class.cpp:2546`) — keep the raw value
and you disagree with the reference on every affected record.

Contained structures: `ADJUSTMENT` is `short[3]` then `char[3]` then a `GENERIC_REFERENCE` — six
bytes then three, not twelve then twelve. `GENERIC_REFERENCE` is a name, a one-byte `char` type,
and an `int` key; it decodes `"*"` inline rather than calling `DAS`.

---

## 7b. Events

Events live in level files, inside a `GameEventList` (`GameEvent.cpp:3601`):

```cpp
ar >> m_level;
ar >> count;
for (count) {
    ar >> temp;                       // the eventType ordinal
    data = CreateNewEvent(temp);      // null for NoEvent / InnEvent / GPDLEvent
    if (data != NULL) data->Serialize(ar, version);
}
```

Three things a reader has to get right:

- **A null dispatch consumes nothing further.** `NoEvent` and a couple of obsolete ordinals produce
  no object, so the entry is exactly four bytes. Treating every counted entry as a full event
  desynchronises on the first one.
- **The ordinal appears twice.** The list reads it to choose the class; `GameEvent::Serialize` then
  reads it again into the event's own field — but *after* the control block and two `PIC_DATA`, not
  immediately. Both are real bytes.
- **Ordinals are positional.** The C++ enum assigns no explicit values below 1000, so an ordinal
  only means "index into this exact sequence". Inserting a member anywhere but the end renumbers
  everything after it and silently reinterprets every event in every existing level. Several
  ordinals also share one layout — `Stairs`, `Teleporter` and `TransferModule` are all
  `TRANSFER_EVENT_DATA`.

Every event opens with the same base: an `EVENT_CONTROL` (which has its own ASL), two `PIC_DATA`,
the type, id, x, y, two chain ids, three strings, and the event ASL. That the two ASL markers
appear in **equal counts** in a real level file is a cheap structural check on the whole shape.

Recurring shapes across the concrete subclasses, worth expecting rather than discovering:

- **Fixed-size arrays with no count.** `GUIDED_TOUR` writes 24 steps, question events write 5
  options, `TAVERN` writes 5 drinks — always, regardless of how many are used. Unused slots carry
  the blank sentinel.
- **Counted lists whose count is *not* the array size.** `RANDOM_EVENT_DATA` declares 14 slots and
  serializes 13 (`for i = 1; i < 14` indexing `[i-1]`).
- **Trailing structures outside the branch.** `COMBAT_EVENT_DATA` ends with its monster list,
  `SHOP` and `WHO_PAYS` with an item list and two transfer blocks. Always read past the closing
  brace.
- **Shapes that change at a version.** `TAVERN` writes ten bare tales below 0.910 and a counted
  list above it.

### Member names are not types

This is the single most expensive mistake in the port — it has cost four separate bugs.

Three different classes declare a member called `items`, with three different types.
`SPECIAL_ITEM_KEY_EVENT_DATA::items` is a `SPECIAL_OBJECT_EVENT_LIST` — ten bytes per entry — while
others of that name are `ITEM_LIST`s. Grepping for `items;` and taking the first hit produces a
reader that runs, consumes plausible-looking bytes, and desynchronises a few events later.

Worse, the *types themselves* come in near-identical pairs:

| Member | Looks like | Actually is |
|---|---|---|
| `COMBAT_EVENT_DATA::bgSounds` | `BACKGROUND_SOUNDS` | **`BACKGROUND_SOUND_DATA`** |
| `SPECIAL_ITEM_KEY_EVENT_DATA::items` | `ITEM_LIST` | **`SPECIAL_OBJECT_EVENT_LIST`** |
| `CHARACTER::blockageData` | `BlockageDataType` | **`BLOCKAGE_STATUS`** (a *list* of them) |
| `QUESTION_LIST_DATA::buttons` | same as `QUESTION_BUTTON_DATA::buttons` | `QLIST_DATA` vs `QBUTTON_DATA` |

`CHARACTER::blockageData` is the subtlest of these: `BLOCKAGE_STATUS` is a counted **list** of
`BlockageDataType`, so reading the singular type consumes 14 bytes where the file holds a 4-byte
count. It only bites on designs that actually have characters.

`BACKGROUND_SOUNDS` is one list of names. `BACKGROUND_SOUND_DATA` wraps **two** of them (day and
night) and adds `UseNightMusic`, `EndTime` and `StartTime`. Reading the wrong one costs 16 bytes
per combat event — and because those 16 bytes swallow the monster count that follows, every
encounter appears to have zero monsters and the event list drifts from there on.

> That one took two full sessions to find. The symptoms pointed everywhere except at it: phantom
> event ordinals (600 and 1800 — actually `EndTime` and `StartTime`), phantom `NoEvent` entries,
> and a level that appeared to contain a combat followed by a null dispatch when it actually
> contains two combats.

**Read the member declaration inside the class you are porting**, not the first match in the
header. When two types share a name prefix, check which one.

### Raw `char` arrays are not strings

`GEM_CONFIG::name` and `COIN_TYPE::Name` are declared `char name[MAX + 1]`, and the loop writes
`MAX` *characters* one at a time — ten single bytes, NUL-padded, not ten counted strings and not
one counted string. The `+ 1` is the terminator and is never serialized.

Also note `MONEY_DATA_TYPE::Coins` is an array of `COIN_TYPE` **records** while
`MONEY_SACK::Coins` of the same name is a plain `int[]`.

### Walking an event list is self-checking

Because each event's fields are only reachable through the previous event's, reading the *N*th
event at all means the preceding *N*−1 were each read at exactly the right length. Two cheap
guards make that failure loud instead of silent:

- **Stop on an ordinal outside the enum.** A drifted stream produces values like 65536 or
  1884488752. Skipping them (as "unknown, reads nothing") hides the drift and lets the walk limp
  on for dozens of entries.
- **Distinguish "unported" from "reads nothing".** They look identical to a reader that only asks
  whether it has a handler.

`EVENT_CONTROL` gates on the `version` parameter in some places and on the **global**
`LoadingVersion` in others, sometimes four lines apart (`GameEvent.cpp:1641` vs `:1644`). They
should agree when loading a design, but the inconsistency is real.

### The whole level is one chain

`LEVEL::Serialize` (`Level.cpp:1224`) runs: dimensions (`BYTE` each), the cell grid at **15 bytes
per cell**, the event list, `zoneData`, the level ASL, step events, wall sets, background sets,
then blockage keys. Nothing is length-prefixed at the file level, so every structure sits at
whatever offset the previous one left behind.

That makes **"the file reads to exactly its last byte"** the single strongest assertion available
for a level — it certifies the grid, every event, and every trailing table at once.

Fixed tables, written in full regardless of use: 16 zones, 8 blockage keys, 24 guided-tour steps,
5 question options, 5 tavern drinks. Counted tables whose count arrived later: wall sets (0.600),
background sets (0.660), tavern tales (0.910). Step events are 8 slots below 1.0210 and 255 above.

`STEP_EVENT_DATA` is **not** a `GameEvent` despite the name — no shared base, and its own ASL name
`STEPEVENT_ATTR`.

### Level files are not compressed

`LEVEL::Serialize` (`Level.cpp:1224`) runs: dimensions (`BYTE` each), the cell grid at **15 bytes
per cell**, `eventData`, `zoneData`, the level ASL, step events (8 below 1.0210, otherwise
`MAX_STEP_EVENTS`), wall and background sets, then blockage keys.

`eventData` comes *before* `zoneData`, so the event list is reachable with only the cell grid read.

Note that a `.lvl` file reads as **plain** even in a design whose databases are LZW-compressed —
Case.dsn is 2.53, well past the 0.930 compression gate, and its levels are still uncompressed. The
compression decision is per file kind, not per design; do not infer one from the other.

---

## 8. Type traps

Win32 `BOOL` is a **4-byte int**, not a byte. Beyond that:

| Field | Declared | Trap |
|---|---|---|
| `PIC_DATA.AlphaValue` | `WORD` | 2 bytes among 4-byte neighbours |
| `ITEM_DATA.ROF_Per_Round` | `double` | 8 bytes among `long`s |
| `MONSTER_DATA.Hit_Dice` | `float` | **same width**, wrong value — see below |
| `ITEM.cursed` | `BYTE` | 1 byte between `int`s |
| `MONSTER_DATA.ItemMask` | `BYTE` | retired, but still on the wire below 0.998101 |
| `DICEPLUS.m_numDice`, `m_bonus`, `m_sign` | `char` | 1 byte each, int-sounding names |
| `SPELL_EFFECTS_DATA.changeResult` | `double` | 8 bytes among `DWORD`s |

### The width trap that alignment checks cannot catch

`MONSTER_DATA.Hit_Dice` is a **`float`** among `long`s (`Monster.h:410`). Every other entry above
changes how many bytes are consumed, so getting one wrong desynchronises the stream and something
downstream fails loudly. This one does not: four bytes either way, so the walk still lands on EOF
and every name still matches — the value is simply nonsense.

A kobold has a *quarter* hit die. Read as a float, `Hit_Dice` is `0.25`; read as an int, the same
bytes are `1,048,576,000`. Nothing in a structural check distinguishes these. **Assert on a known
value.**
| `GLOBAL_STATS.startX/Y/Facing` | `BYTE` | three singles among `int`s |
| `GLOBAL_STATS.logfont` | `LOGFONT` | a raw **60-byte** struct blit (`LOGFONTA`; the wide variant would be 92) |
| `TITLE_SCREEN_DATA` count | `DWORD` | where neighbouring lists use `int` |
| `AutoDarkenAmount` | `BOOL` | holds a *magnitude* — do not narrow to `bool` |

### Base-38 packed names

`Location_Readied` is a `DWORD` that does **not** hold the value on disk. Legacy designs wrote
small ordinals (0 = weapon hand, …) which the loading branch rewrites into packed six-character
names (`Items.h:105`):

```
base38(a..f) = ((((a*38+b)*38+c)*38+d)*38+e)*38+f     'A'→12 … 'Z'→37, blank→1
```

So ordinal 10 becomes `QUIVER` = 2,286,454,785 — which is what the oracle reports. A reader keeping
the raw ordinal disagrees with the reference on every old design, with nothing to indicate it.

---

## 9. Structures that hide extra bytes

Things read *after* an obvious loop or branch, which a reader can miss while appearing to succeed:

- **Check the lines after the closing brace of every `Serialize`.** Fields placed *outside* the
  `if (IsStoring()) … else …` run on both paths and belong to every record. `SPELL_EFFECTS_DATA`
  ends with `changeData.Serialize(ar)` — a whole `DICEPLUS` — at `Spell.cpp:273`, one line past the
  brace that closes the loading branch. `ITEM_DATA` parks its `specAbs` and ASL the same way.
- **`items.dat` ends with an ammo-type list** — `int count` then that many `DAS` strings, at ≥ 0.690,
  *outside* the record loop (`Items.cpp:3091`). Stopping at the last record leaves 22 bytes
  unconsumed in DefaultDesign.
- **`ITEM_DATA` reads `HitArt` twice** — once in the early art block, again at ≥ 0.690.
- **`MONSTER_DATA` continues past its ASL.** `myItems` (> 0.693) and `money` (≥ 0.906) follow the
  attribute list (`Monster.cpp:851`). It is the only record type where the ASL is not last, so a
  reader modelled on `ITEM_DATA` stops three structures early.
- **`ITEM_LIST` ends with a `READY_ITEMS`** — twelve equipment slots after the item loop.

### Wire order follows source order, not version order

In `SPELL_DATA`'s script block the `>= 2.6` group is written **before** the `>= 1.0303` group
(`Spell.cpp:4232`, `:4241`). A design at 2.53 therefore reads the *second* group and not the first.
Sorting version gates numerically when transcribing gets both the order and the count wrong for
everything in between.

### Sentinels that look like corruption

`SPELL_EFFECTS_DATA.changeResult` is a `double` whose "no change" value is
`-1.2345678901234568e18` — the digits `1234567890123456`. It reads like a misaligned field and is
not one.

> This is why **"the stream lands exactly on EOF" is the highest-value cheap assertion available.**
> It is what surfaced the ammo list, and it is hard to satisfy by accident: any wrong field width
> anywhere in any record ends the walk early or runs off the end.

---

## 10. Validating a reader

The two-sided contract, with both halves proven by observation:

```
C++ reference ──dump──> golden JSON ──diff──> C# reader
```

- Extending the dumper made the drift check fire correctly, then clear.
- Deliberately corrupting the golden made exactly the relevant tests fail.

**Fixtures span the format's whole life** — 0.915 (uncompressed, oracle-dumpable), 2.53, 3.55, 5.28
(compressed), and 5.29 generated by CI itself via `-savedesign` so C# and C++ read identical bytes.

These take opposite branches at nearly every fork, which is what makes them worth having:

| | DefaultDesign | The compressed designs |
|---|---|---|
| Archive tier | plain / uncompressed `CAR` | LZW |
| `Specab` | legacy conversion | modern `A_CStringPAIR_L` |
| Usability | `Usable_by_Class` bitmask | `BASECLASS_ID` string list |
| `PIC_DATA` | no `RestartFrame` | `RestartFrame` present |

Assertions worth writing, in descending order of value:

1. **Stream lands exactly on EOF** after a whole-file walk.
2. **Field-by-field diff against the oracle** — catches wrong values that alignment checks miss.
3. **Trailing structures decode** (the ammo list) — proves no record drifted.
4. **Round decimals** (`startTime` = 800, `startExp` = 30,000,000) — a one-byte slip yields
   arbitrary noise, not round numbers.
5. Printability of names — the weakest; use it to *locate* drift, not to prove its absence.
