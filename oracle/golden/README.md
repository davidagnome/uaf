# Golden oracle dumps

Canonical JSON produced by the **C++ reference implementation**, used to validate the .NET port.
See `docs/PORTING-PLAN.md` §8.

## How these are produced

The `Oracle (C++ reference build)` workflow builds `UAFWinEd.exe` and runs:

```
UAFWinEd.exe "-config <design>.dsn" "-dumpjson <out>.json"
```

Note the quoting — flag and value must be a **single argument**; `CUAFCommandLineInfo::ParseParam`
splits them with `strchr(param, ' ')` inside one token. Passing them separately silently yields
empty values.

The run uploads an `oracle-json` artifact. Download it and commit the file here:

```bash
gh run download <run-id> -R davidagnome/uaf -n oracle-json -D oracle/golden/
```

`gh run view --log` resolves to the *upstream* remote and 404s — always pass `-R davidagnome/uaf`.

## What makes a dump usable as ground truth

- **`_meta.ok` must be `true`.** A dump written after a failed load carries default state, not
  design data, and is worse than no fixture at all.
- **`_meta.designVersion` must be outside `[0.998101, 0.9988]`.** The editor itself warns it
  cannot reliably load that range (`Level.cpp:3340`), so a disagreement there settles nothing.

## When the drift check fails

The Oracle workflow regenerates the dump every run and fails if it differs from the committed
file. That fires for two very different reasons, and the log diff is how you tell them apart:

1. **You extended the dumper** (added a field or section). Expected — the golden is simply older
   than the code. Review the diff to confirm only the intended additions appear, then download
   the new artifact and commit it.
2. **The reference implementation's behaviour changed.** This invalidates the baseline the C#
   port is validated against. Do **not** refresh the golden until you understand why: the whole
   point of the check is that this cannot be absorbed silently.

The check cannot distinguish the two, which is deliberate — a human reading the diff can, and a
heuristic that guessed would defeat the purpose.

## Fields excluded from comparison

- **`_meta.diagnostics`** — informational, and contains absolute build-machine paths.

Everything else is compared field-by-field by `OracleDiffTests`. Paths in the compared fields are
deliberately folder names rather than absolute paths so a dump regenerated on another machine
still matches.

## Coverage gap

Every design currently available is **tier 1 or 2** (plain `CArchive`, or `CAR` without
compression). Nothing here exercises the tier-3 LZW path. A `DefaultDesign` re-saved by the
shipped `UAFWinEd.exe` would write at 5.29 — past the 0.930 compression gate — and close that gap.
