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

## Adding the workflow step

After the GPDLcomp build in `.github/workflows/oracle-cpp.yml`:

```powershell
$dir = "oracle\golden\gpdl"
Get-ChildItem "$dir\*.txt" | ForEach-Object {
  $bin = [IO.Path]::ChangeExtension($_.FullName, ".bin")
  $lst = [IO.Path]::ChangeExtension($_.FullName, ".lst")
  $p = Start-Process -FilePath $gpdlcomp -ArgumentList @($_.FullName, $bin, $lst) `
                     -Wait -PassThru -RedirectStandardInput NUL
  if (-not (Test-Path $bin) -or (Get-Item $bin).Length -eq 0) {
    Write-Output "::error::GPDLcomp produced no output for $($_.Name)"
    exit 1
  }
}
```

Two things will bite otherwise, both of the same shape as the Phase 0 lessons in the porting plan:

- **`-RedirectStandardInput NUL` is not optional.** GPDLcomp calls `gets_s` after every error
  message and after its usage banner (`src/GPDL/GPDL.cpp:39`, `:57`). A script that fails to compile
  will **hang the runner** rather than fail it.
- **GPDLcomp always exits 0** (`GPDL.cpp:111`), even when compilation failed. The step must test for
  a non-empty `.bin` and `exit 1` itself — a script that writes `::error::` without exiting
  non-zero still reports success.

`GPDLcomp.exe` is a console subsystem app, so unlike `UAFWinEd.exe` it does block PowerShell —
but use `Start-Process -Wait` anyway so the redirect applies.

## When a diff fails

The listing (`.lst`) is the artefact to read first: it names an address and a mnemonic, where the
`.bin` names a byte offset in a file with three concatenated segments and no framing. An offset
inside the first `4 + 4*codeLength` bytes is a code-generation difference; past that it is the
constant pool (ordering, interning, or the function-entry markers); past that again it is the
public-function index (ordering, or the `outer@inner` naming).
