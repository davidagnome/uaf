#!/usr/bin/env bash
#
# Publishes the player (UAFcoreApp) and, optionally, the editor (UAFedit) as self-contained
# executables for one or more runtime identifiers.
#
#   tools/publish.sh [--editor] [rid ...]
#
# With no rid, publishes for the host's own runtime identifier. Each published folder lands in
# out/publish/<rid>/ and carries the SDL3 natives and the .NET runtime, so it runs on a machine
# with no .NET installed.
#
# The self-contained apphost is named UAFcoreApp, not UAFcore.App: macOS kills any executable whose
# file name ends in .App, taking it for an application bundle directory (see the UAFcore.App
# csproj).

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/out/publish"

EDITOR=0
RIDS=()

for arg in "$@"; do
  case "$arg" in
    --editor) EDITOR=1 ;;
    *) RIDS+=("$arg") ;;
  esac
done

if [ "${#RIDS[@]}" -eq 0 ]; then
  RID="$(dotnet --info | awk '/RID:/{print $2; exit}')"
  if [ -z "$RID" ]; then
    echo "no rid given and none could be detected from \`dotnet --info\`" >&2
    exit 2
  fi
  RIDS+=("$RID")
fi

for rid in "${RIDS[@]}"; do
  dest="$OUT/$rid"
  echo "publishing UAFcoreApp for $rid -> $dest"
  dotnet publish "$ROOT/dotnet/src/UAFcore.App/UAFcore.App.csproj" \
    -c Release -r "$rid" --self-contained -o "$dest"

  if [ "$EDITOR" = "1" ]; then
    echo "publishing UAFedit for $rid -> $dest/editor"
    dotnet publish "$ROOT/dotnet/src/UAFedit/UAFedit.csproj" \
      -c Release -r "$rid" --self-contained -o "$dest/editor"
  fi
done

echo
echo "published:"
for rid in "${RIDS[@]}"; do
  echo "  $OUT/$rid/$( [ "$EDITOR" = "1" ] && printf 'UAFcoreApp (and editor/)' || printf 'UAFcoreApp' )"
done
