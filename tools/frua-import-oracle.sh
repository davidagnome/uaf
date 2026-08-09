#!/usr/bin/env bash
#
# Runs the C++ reference importer over a DOS FRUA design and leaves the result where the .NET
# port can be diffed against it.
#
# This is Phase 6's missing evidence. Everything UAF.Import.Frua asserts today is either internal
# consistency, plausibility of the decoded values, or agreement with a *reading* of UAImport.cpp.
# None of that is the same as producing what the reference binary produces -- which is the jump
# the GPDL goldens made, and which immediately found a use-after-free nobody knew about.
#
# Needs UAFWinEd.exe built (the Oracle workflow's artifact will do) and a Windows runtime. On
# macOS or Linux that means CrossOver or plain Wine; on Windows, nothing.
#
#   tools/frua-import-oracle.sh <UAFWinEd.exe> <frua-design.dsn> <output-dir> [ua-install-dir]
#
# CAUTION on trusting the result: Wine is a variable. If the port and the reference disagree,
# "is it Wine or is it us?" is a question you do not want to be asking. Run the same binary once
# on Windows CI and diff the two outputs against each other; if they agree, the local loop can be
# trusted for iteration and CI stays the authority.

set -euo pipefail

if [ $# -lt 3 ]; then
    sed -n '3,22p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
fi

EXE=$1
DESIGN=$2
OUT=$3
UAPATH=${4:-}

for p in "$EXE" "$DESIGN"; do
    [ -e "$p" ] || { echo "no such path: $p" >&2; exit 2; }
done

# Pick a runner. CrossOver ships its own wine; fall back to whatever is on PATH.
if [ "$(uname -s)" = "Darwin" ] || [ "$(uname -s)" = "Linux" ]; then
    CROSSOVER=/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine
    if [ -x "$CROSSOVER" ]; then
        RUN=$CROSSOVER
    elif command -v wine >/dev/null 2>&1; then
        RUN=wine
    else
        echo "no wine found (looked for CrossOver and \`wine\` on PATH)" >&2
        exit 2
    fi
else
    RUN=""
fi
echo "runner: ${RUN:-native}"

# The importer writes over the design it is pointed at, so it gets a scratch copy. This mirrors
# the same caution the -savedesign step takes for the tier-3 fixture.
rm -rf "$OUT"
mkdir -p "$OUT/Data"
echo "scratch design: $OUT"

# -config takes the design to import INTO, -importfrua the FRUA design to read.
#
# ARGUMENT FORM IS NOT THE USUAL ONE. CUAFCommandLineInfo::ParseParam (Globals.cpp) splits flag
# from value with strchr(param, ' ') INSIDE one token, so each flag and its value must be passed
# as a SINGLE argument -- "-config X", not -config X. Passing them apart leaves the value empty
# and the app exits having done nothing.
ARGS=("-config $OUT/Data/config.txt" "-importfrua $DESIGN")
[ -n "$UAPATH" ] && ARGS+=("-uapath $UAPATH")

set +e
if [ -n "$RUN" ]; then
    "$RUN" "$EXE" "${ARGS[@]}"
else
    "$EXE" "${ARGS[@]}"
fi
status=$?
set -e

# UAFWinEd is a GUI-subsystem binary and its exit code says nothing useful; check for output,
# the same rule the GPDLcomp step learned the hard way.
echo "exit status: $status (informational only)"

if [ ! -f "$OUT/Data/game.dat" ]; then
    echo "::error::no game.dat produced in $OUT/Data -- the import did not complete" >&2
    find "$OUT" -name 'UAFErrors*.txt' -exec sh -c 'echo "--- $1 ---"; tail -40 "$1"' _ {} \;
    exit 1
fi

echo
echo "imported design:"
ls -la "$OUT/Data" | head -20
echo
echo "next: point uaf-fileprobe at $OUT and diff against UAF.Import.Frua's reading of $DESIGN"
