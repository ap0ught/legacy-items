#!/usr/bin/env bash
set -euo pipefail

# Install/update the Legacy Items (Inheritance) mod into the local Caves of Qud Mods folder.
# Usage: ./install.sh [--dest /path/to/Mods]
# Env:   QUD_MODS_DIR overrides the Mods directory.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEFAULT_MODS_DIR="${QUD_MODS_DIR:-$HOME/.config/unity3d/Freehold Games/CavesOfQud/Mods}"
DEST_BASE="$DEFAULT_MODS_DIR"

if [[ "${1:-}" == "--dest" && -n "${2:-}" ]]; then
  DEST_BASE="$2"
fi

DEST="$DEST_BASE/LegacyItems"

if [[ ! -f "$SCRIPT_DIR/manifest.json" ]]; then
  echo "error: expected manifest.json in repo root (run from repo root)" >&2
  exit 1
fi

mkdir -p "$DEST"
rsync -a --delete \
  --exclude='.git' --exclude='.gitignore' --exclude='bin' --exclude='obj' \
  --exclude='*.csproj' --exclude='Directory.Build.props' --exclude='workshop.json' \
  --exclude='install.sh' --exclude='AGENT.md' \
  "$SCRIPT_DIR/" "$DEST/"
echo "Installed Legacy Items -> $DEST"
echo "Restart Caves of Qud to recompile, then check: grep -A6 'LEGACY ITEMS\\|LegacyItems' \"$HOME/.config/unity3d/Freehold Games/CavesOfQud/build_log.txt\" | head -8"
