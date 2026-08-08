# GPDL conformance corpus and golden bytecode

The scripts here are the input for the byte-identity check in
`dotnet/tests/UAF.Scripting.Tests/GpdlOracleDiffTests.cs`. Each `<name>.txt` is compiled by the
reference `GPDLcomp.exe` in the Oracle workflow; the resulting `<name>.bin` and `<name>.lst` are
committed beside it, and the test compiles the same `.txt` with `gpdlc` and diffs both.

The tests **return early** while no `.bin` exists, so a green suite does not yet mean byte-identity
has been shown. See `docs/PORTING-PLAN.md` § "Phase 2 status — GPDL".

## Why this corpus and not `src/GPDL/talk.txt`

`talk.txt` is described in the plan as the conformance corpus, but **it does not compile** — with
either compiler. It calls `$GET_CHAR_CHA`, `$SET_CHAR_CHA`, `$Race` and `$Class`, none of which
survive in `systemfunctions[]`, and the reference compiler rejects it at `talk.txt:357`. It is a
sample from an earlier revision of the function table.

These scripts are written to be compilable *and* to exercise each code-generation mechanism
separately, so a diff failure points at one construct rather than at "somewhere in 860 words":

| File | Covers |
|---|---|
| `basics.txt` | literals, adjacent-literal concatenation, constant interning, `$IF`/`$ELSE`, string relations, `$RETURN` with and without a value |
| `control.txt` | `$WHILE` with `$BREAK` and `$CONTINUE`, `$SWITCH` with `$CASE`/`$GCASE`/`$DEFAULT` and fall-through, `$RESPOND` |
| `arith.txt` | both arithmetic families — the `$` bignum functions and the `#` hardware operators — plus operator precedence, unary `!` and `-#`, `=#` |
| `structure.txt` | nested functions, `@`-qualified public names, prototypes, locals, globals, parameter passing, default parameter values, `#PUBLIC` |

## Producing the goldens

**The workflow step exists** — `Compile GPDL goldens (reference compiler)` in
`.github/workflows/oracle-cpp.yml`, after the three MSVC builds. This section used to describe how
to add it. To get the goldens into the tree:

1. Run the **Oracle (C++ reference build)** workflow — it fires on any push touching `src/**`,
   `oracle/**` or its own file, and can be started by hand from the Actions tab
   (`workflow_dispatch`).
2. Download the **`gpdl-goldens`** artifact from that run — **only from a run whose
   `Compile GPDL goldens` step is green.** The artifact uploads on failure too, so that a broken
   compile can be inspected, which means a red run can hand you a partial set. Committing one of
   those is the exact state the tests cannot detect.
3. Commit its `.bin` and `.lst` files here, beside the `.txt` they came from.

That third step is manual on purpose: the workflow has `contents: read` and does not write to the
repository, and a golden that the reference produced is a fact worth landing under review rather
than automatically.

The step compiles into a scratch directory rather than in place, so that once goldens *are*
committed the following step can hash fresh output against them and fail on reference drift. Writing
straight into `oracle/golden/gpdl` would have it comparing a file with itself.

Three things bite, all of the same shape as the Phase 0 lessons in the porting plan:

- **Stdin must be redirected.** GPDLcomp calls `gets_s` after every error message and after its
  usage banner (`src/GPDL/GPDL.cpp:40`, `:58`). A script that fails to compile — or one bad
  argument — will **hang the runner** rather than fail it. The step redirects from an empty temp
  file rather than the `NUL` device, because `Start-Process -RedirectStandardInput` wants a real
  path; `gets_s` hits EOF and returns immediately either way.
- **GPDLcomp always exits 0** (`GPDL.cpp:111`) on the compile path: a failed compile skips
  `WriteCode` entirely (the `if (result == 0)` guard at `:96`) and leaves an **empty** `.bin`
  behind. Success is "the file exists and has bytes in it", never the exit code. Only `usage()`
  exits non-zero, and it does so *after* the `gets_s` above.
- **A partial set is worse than none.** `GpdlOracleDiffTests` silently skips a `.txt` with no
  `.bin`, so a half-populated directory leaves the suite green over whatever it missed. The step
  refuses to publish unless every script compiled;
  `Goldens_are_either_complete_or_absent_but_never_partial` guards the committed side.

`GPDLcomp.exe` is a console subsystem app, so unlike `UAFWinEd.exe` it does block PowerShell —
but `Start-Process -Wait` is used anyway so the redirects apply.

## When a diff fails

The listing (`.lst`) is the artefact to read first: it names an address and a mnemonic, where the
`.bin` names a byte offset in a file with three concatenated segments and no framing. An offset
inside the first `4 + 4*codeLength` bytes is a code-generation difference; past that it is the
constant pool (ordering, interning, or the function-entry markers); past that again it is the
public-function index (ordering, or the `outer@inner` naming).
