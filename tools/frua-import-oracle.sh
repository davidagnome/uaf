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
# Getting the exe: the Oracle workflow publishes it as the `uafwined-editor` artifact. Unzip it
# and point this script at the UAFWinEd.exe inside -- keep the EditorResources and
# TemplateDesign.dsn directories beside it, because the editor resolves its resources from the
# executable's own directory and will not start standalone.
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

# Finding the bottle: ask cxbottle FIRST, then look on disk -- and the second half is not
# redundant.
#
# An earlier version of this asked cxbottle alone, on the grounds that it "is the only thing that
# knows". It is not: cxbottle searches only the two DEFAULT directories and ignores the configured
# BottleDir entirely. On a machine whose bottles live off the boot volume -- `defaults read
# com.codeweavers.CrossOver BottleDir` -> /Volumes/.../Bottles -- it reports every one of them as
# missing while `wine --bottle <name>` runs them perfectly well. So a cxbottle miss is not
# evidence of absence, and treating it as such refused a bottle that worked.
#
# CX_BOTTLE_PATH is what tells wine where to look, and it must be exported for a bottle outside
# the defaults. dotnet/tests/UAFedit.Oracle.Tests/ReferenceEditor.cs does the same three steps.
BOTTLE_DIR=""
find_bottle_dir() {
    "$CXBIN/cxbottle" --bottle "$BOTTLE" --status >/dev/null 2>&1 && return 0

    local configured
    configured=$(defaults read com.codeweavers.CrossOver BottleDir 2>/dev/null || true)

    local d
    for d in "$configured" \
             "$HOME/Library/Application Support/CrossOver/Bottles" \
             "/Library/Application Support/CrossOver/Bottles"; do
        if [ -n "$d" ] && [ -d "$d/$BOTTLE" ]; then
            BOTTLE_DIR=$d
            return 0
        fi
    done

    return 1
}

if [ "$(uname -s)" = "Darwin" ] || [ "$(uname -s)" = "Linux" ]; then
    if [ -x "$CXBIN/wine" ]; then
        if ! find_bottle_dir; then
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
        RUN_ARGS=(--bottle "$BOTTLE" --cx-app)
        [ -n "$BOTTLE_DIR" ] && export CX_BOTTLE_PATH=$BOTTLE_DIR
    elif command -v wine >/dev/null 2>&1; then
        RUN=wine
    else
        echo "no wine found (looked for CrossOver and \`wine\` on PATH)" >&2
        exit 2
    fi
fi
echo "runner: ${RUN:-native} ${RUN_ARGS[*]:-}${CX_BOTTLE_PATH:+  (CX_BOTTLE_PATH=$CX_BOTTLE_PATH)}"

# The import needs a real design to import INTO, not an empty directory: config.txt supplies the
# screen and tile geometry, and without it those stay zero and the editor divides by one of them
# (an unhandled division by zero at 004240C2, the first time this was tried). So seed a scratch
# copy of DefaultDesign, exactly as the -savedesign tier-3 step runs on a copy.
# DefaultDesign is the reference's own template, and the C++ reads it happily -- but its game.dat
# is SHORT. It ends after questData with no character list, and the port's reader throws where
# MFC's archive quietly returns zero past EOF (see the standing-gaps table). Seeding from it gives
# a harness output whose header this port cannot fully read, which is a diff you cannot interpret.
# Override with a design that reads in full:
#
#   UAF_TEMPLATE_DESIGN=reference/Case.dsn tools/frua-import-oracle.sh ...
TEMPLATE=${UAF_TEMPLATE_DESIGN:-src/UAFWinEd/DefaultDesign.dsn}
[ -d "$TEMPLATE" ] || { echo "no template design at $TEMPLATE" >&2; exit 2; }

rm -rf "$OUT"
mkdir -p "$(dirname "$OUT")"
cp -R "$TEMPLATE" "$OUT"
echo "scratch design: $OUT  (seeded from $TEMPLATE)"

# Fingerprint the seed. Testing for game.dat afterwards proves NOTHING, because the template
# already has one -- the check would pass on a run that imported nothing at all, which is exactly
# what the first working invocation of this script did.
BEFORE=$(md5 -q "$OUT/Data/game.dat" 2>/dev/null || md5sum "$OUT/Data/game.dat" | cut -d' ' -f1)

# -config takes the design DIRECTORY to import into -- not a path to config.txt. Passing the
# file instead makes DefaultFoldersFromDesign resolve the wrong root, and saveDesign() then
# writes a folder named after the concatenation: "config.txtHeirs to skull crag.dsn".
#
# ARGUMENT FORM IS NOT THE USUAL ONE. CUAFCommandLineInfo::ParseParam (Globals.cpp) splits flag
# from value with strchr(param, ' ') INSIDE one token, so each flag and its value must be passed
# as a SINGLE argument -- "-config X", not -config X. Passing them apart leaves the value empty
# and the app exits having done nothing.
#
# THE PATHS MUST BE WINDOWS PATHS when a runner is in play. The exe resolves them through Win32,
# and a Unix path reaches it as a relative name it cannot open -- the import then does nothing and
# the only symptom is an unchanged game.dat. CrossOver maps Z: to the filesystem root, so an
# absolute macOS path needs no copying into the bottle: it becomes Z:\Volumes\...  Running
# natively on Windows, they are already Windows paths and are passed through.
winpath() {
    if [ -z "$RUN" ]; then
        printf '%s' "$1"
        return
    fi

    local abs
    abs=$(cd "$(dirname "$1")" 2>/dev/null && printf '%s/%s' "$(pwd -P)" "$(basename "$1")") \
        || abs=$1
    printf 'Z:%s' "${abs//\//\\}"
}

ARGS=("-config $(winpath "$OUT")" "-importfrua $(winpath "$DESIGN")")
[ -n "$UAPATH" ] && ARGS+=("-uapath $(winpath "$UAPATH")")
printf 'args:'; printf ' [%s]' "${ARGS[@]}"; echo

# The editor writes its trace to rte.LogDir() + "UafErr_Edit.txt" -- NOT "UAFErrors*.txt", which
# is what an earlier version of this script looked for and never found. LOG_ERRORS is 1 in
# DefaultDesign's config.txt, so the trace exists; it just has to be collected from the right name,
# and LogDir may resolve inside the bottle rather than under $OUT.
logs() {
    local found=0 f
    for f in $(find "$OUT" "$(dirname "$OUT")" -maxdepth 3 -name 'UafErr_*.txt' 2>/dev/null | sort -u); do
        echo "--- $f ---"; tail -60 "$f"; found=1
    done
    [ "$found" = "0" ] && echo "(no UafErr_*.txt found -- check rte.LogDir() inside the bottle)"
}

set +e
if [ -n "$RUN" ]; then
    "$RUN" "${RUN_ARGS[@]}" "$(winpath "$EXE")" "${ARGS[@]}"
else
    "$EXE" "${ARGS[@]}"
fi
status=$?
set -e

# UAFWinEd is a GUI-subsystem binary and its exit code says nothing useful; check for output,
# the same rule the GPDLcomp step learned the hard way.
echo "exit status: $status (informational only)"

if [ ! -f "$OUT/Data/game.dat" ]; then
    echo "::error::no game.dat in $OUT/Data -- the import destroyed the seed without replacing it" >&2
    logs
    exit 1
fi

AFTER=$(md5 -q "$OUT/Data/game.dat" 2>/dev/null || md5sum "$OUT/Data/game.dat" | cut -d' ' -f1)
if [ "$BEFORE" = "$AFTER" ]; then
    echo "::error::game.dat is byte-identical to the seed ($BEFORE) -- the import did not run" >&2
    echo "The design is still $TEMPLATE, unchanged. Nothing has been imported." >&2
    logs
    exit 1
fi
echo "game.dat changed: $BEFORE -> $AFTER"

echo
echo "imported design:"
ls -la "$OUT/Data" | head -20
echo
echo "next: point uaf-fileprobe at $OUT and diff against UAF.Import.Frua's reading of $DESIGN"
