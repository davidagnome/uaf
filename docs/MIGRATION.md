# Migrating designs to the .NET port

This document is for people with existing Dungeon Craft designs (`.dsn` folders) who want to open
them with the new C# port — the game `UAFcoreApp` and the editor `UAFedit`. It assumes you already
have a design that the original Windows tools could open.

## Your design is a folder

A Dungeon Craft design is a directory, not a single file. It contains `Data/` (the databases and
level files: `game.dat`, `items.dat`, `monsters.dat`, `spells.dat`, `Level###.lvl`, and the tagged
databases `ability.dat`, `baseclass.dat`, `classes.dat`, `races.dat`, `spellgroups.dat`,
`traits.dat`, `specialAbilities.dat`), a `Resources/` folder for art and sounds, and a `config.txt`
with the layout and palette settings.

**Copy the folder, do not move it.** The port reads the same files the Windows tools do, but it
writes designs back at the current format version. Keeping the original untouched is the difference
between a mistake and a lost design.

## Everything is read, one version is written

The port reads every design version the Windows engine reads — roughly 0.500 through 5.29. Saving
writes the current format (5.24 for records, 5.26 for the design header). This matches the original
editor's own behaviour: it, too, restamps whatever it loads when it saves. A design you merely
*open* is not touched; a design you *save* is upgraded.

The one deliberate refusal: `baseclass.dat` / `classes.dat` / `races.dat` still in the oldest
legacy shapes (`Bcd1`, `CL1`, `RaceV1` — the editor's own template ships these) are not rewritten,
because the reference editor itself refuses `Bcd1` and asks you to re-import a `baseclass.txt`.
Everything else round-trips.

## Filenames are case-insensitive

Designs were authored on Windows and assume Windows path semantics: a record may name
`WYA_UD_Medieval.png` while the file is actually `wya_ud_medieval.png`. Windows does not care; a
case-sensitive filesystem (Linux, or a case-sensitive APFS volume) does. The port resolves every
constructed filename case-insensitively, so a design loads as-is on any platform. If you *write*
new art from a non-Windows machine, match the case the design already references to avoid ambiguity.

## Running the game

```
UAFcoreApp path/to/Design.dsn
```

`UAFcoreApp` opens the design, loads its config and art, and plays it. `--dump <out.png>` renders
one frame and exits without a window, which is also how the port is smoke-tested on machines with
no display.

## Using the editor

`UAFedit` is the Avalonia editor. It opens a `.dsn` folder, edits levels, events, the item/monster/
spell databases and special abilities, and saves — and a design it saves is read back by the
original `UAFWinEd.exe`. `File > New` copies the bundled template into a folder you choose, so the
template itself is never overwritten.

## Importing a DOS FRUA design

The port imports original Gold-Box-era FRUA designs (the `GAME001.DAT`, `GEO###.DAT`,
`MONST###.DAT`, `STRG###.DAT` format). Import is into an existing design folder (it supplies the
rules databases FRUA has no equivalent for), and the result is a design `UAFcoreApp` plays. The
importer is verified byte-for-byte against the original editor's output for every field both agree
on, and deliberately fixes a few places the original loses data (monster and NPC identities,
training-hall classes).

## What is not there yet

- **Video cutscenes** (`PlayMovie` events) are skipped rather than played — they need an FFmpeg
  binding that has not been wired up.
- **Outdoor combat maps** are not generated yet, so wilderness encounters use the indoor map.
- A few event types the engine has never shipped in a design (`Encounter`, `TavernTales`) are read
  but not yet executed.

None of these stops a design loading; they degrade to a skipped or simpler path.
