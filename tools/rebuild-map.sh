#!/usr/bin/env bash
# Regenerates the campaign map from its JSON. Run this after editing
# data/campaigns/<campaign>/map.json or provinces.json — the game reads the generated images, not
# the JSON, so nothing changes in Godot until this has run.
#
#   ./tools/rebuild-map.sh
#
# Keeps its own virtualenv in tools/.venv so numpy and pillow never touch the system python.
set -euo pipefail

TOOLS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VENV="$TOOLS_DIR/.venv"

if [ ! -x "$VENV/bin/python" ]; then
	echo "Setting up $VENV (first run only)..."
	python3 -m venv "$VENV"
	"$VENV/bin/pip" install --quiet --upgrade pip numpy pillow
fi

"$VENV/bin/python" "$TOOLS_DIR/generate_campaign_map.py"
echo
echo "Done. Back in Godot: the images re-import when the editor regains focus."
echo "If they do not, use Project > Reload Current Project, then F6 on scene/campaign-map/campaign_map.tscn."
