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

# Pick a runner.
#
# CrossOver's `wine` is NOT plain wine: it resolves a "bottle" (a Windows environment) first and
# fails with "Unable to find the 'default' bottle" if none exists. So it needs --bottle, and the
# bottle has to have been created. Creating one is left to the user rather than done here -- it
# writes a few hundred megabytes into their CrossOver support directory, which is not something a
# build script should do behind their back.
BOTTLE=${UAF_BOTTLE:-${CX_BOTTLE:-uaf-oracle}}
CXBIN=/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin

RUN=""
RUN_ARGS=()

# DO NOT look for the bottle on disk. Two earlier versions of this script did and both were
# wrong: bottles can sit under the configured BottleDir (`defaults read
# com.codeweavers.CrossOver BottleDir`, often moved off the boot volume) OR under the default
# ~/Library/Application Support/CrossOver/Bottles -- and a machine can have some in each. Ask
# cxbottle, which is the only thing that knows.
if [ "$(uname -s)" = "Darwin" ] || [ "$(uname -s)" = "Linux" ]; then
    if [ -x "$CXBIN/wine" ]; then
        if ! "$CXBIN/cxbottle" --bottle "$BOTTLE" --status >/dev/null 2>&1; then
            cat >&2 <<EOF
CrossOver has no '$BOTTLE' bottle. Create a dedicated one -- don't reuse a game bottle, since
the import writes into its drive_c:

  "$CXBIN/cxbottle" --bottle $BOTTLE --create --template winxp

winxp is the apt template: this tree's vcxproj files target the XP toolsets, so the reference
binaries are built for it. Or point at an existing bottle with UAF_BOTTLE=<name>.
EOF
            exit 2
        fi
        RUN=$CXBIN/wine
        RUN_ARGS=(--bottle "$BOTTLE")
    elif command -v wine >/dev/null 2>&1; then
        RUN=wine
    else
        echo "no wine found (looked for CrossOver and \`wine\` on PATH)" >&2
        exit 2
    fi
fi
echo "runner: ${RUN:-native} ${RUN_ARGS[*]:-}"

# The import needs a real design to import INTO, not an empty directory: config.txt supplies the
# screen and tile geometry, and without it those stay zero and the editor divides by one of them
# (an unhandled division by zero at 004240C2, the first time this was tried). So seed a scratch
# copy of DefaultDesign, exactly as the -savedesign tier-3 step runs on a copy.
TEMPLATE=${UAF_TEMPLATE_DESIGN:-src/UAFWinEd/DefaultDesign.dsn}
[ -d "$TEMPLATE" ] || { echo "no template design at $TEMPLATE" >&2; exit 2; }

rm -rf "$OUT"
mkdir -p "$(dirname "$OUT")"
cp -R "$TEMPLATE" "$OUT"
echo "scratch design: $OUT  (seeded from $TEMPLATE)"

# -config takes the design DIRECTORY to import into -- not a path to config.txt. Passing the
# file instead makes DefaultFoldersFromDesign resolve the wrong root, and saveDesign() then
# writes a folder named after the concatenation: "config.txtHeirs to skull crag.dsn".
#
# ARGUMENT FORM IS NOT THE USUAL ONE. CUAFCommandLineInfo::ParseParam (Globals.cpp) splits flag
# from value with strchr(param, ' ') INSIDE one token, so each flag and its value must be passed
# as a SINGLE argument -- "-config X", not -config X. Passing them apart leaves the value empty
# and the app exits having done nothing.
ARGS=("-config $OUT" "-importfrua $DESIGN")
[ -n "$UAPATH" ] && ARGS+=("-uapath $UAPATH")

set +e
if [ -n "$RUN" ]; then
    "$RUN" "${RUN_ARGS[@]}" "$EXE" "${ARGS[@]}"
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
