#!/usr/bin/env bash
# Proves the toolbar's layout button really collapses panes, by measuring them.
#
# Usage: ux-scripts/run-pane-layout.sh [output-dir]
#
# Three things are checked, and only the first is something a property
# assertion could have told us:
#
#   1. The panes' IsVisible flags and their splitters' follow the mode. That is
#      in verify-pane-layout.yaml itself.
#   2. The pixels moved. measure-pane-layout.py finds the sidebar's grey column
#      and the reading column's centre in each screenshot. With all three panes
#      the sidebar occupies the left 260px and the reading column is centred
#      near the right of the window; collapse the sidebar and the grey column
#      is gone entirely and the reading column's centre moves left; collapse
#      the list too and it moves left again to the middle of the window. A
#      pane whose content was merely hidden would leave the centre where it
#      was, which is the failure this catches and the Expects cannot.
#   3. The mode survives a restart. The second half runs two processes against
#      one profile directory: the first collapses to reading-only and exits,
#      the second is launched afterwards and has to come up collapsed.
#
# Needs no network and touches no real profile: every run gets its own
# LUCIDREADER_DATA_DIR seeded from reader-fixture.sql and removed on the way
# out including on failure and on interrupt, so this is repeatable by
# construction and can be run twice in a row. See reader-harness.sh.
#
# The theme is forced to Light rather than left on Auto, because the
# measurement names exact colours and Auto makes the answer depend on whatever
# the machine's appearance happens to be that afternoon.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

OUT_ROOT="${1:-/tmp/lr-pane-layout}"

PROFILES=()
cleanup() {
    local profile
    for profile in "${PROFILES[@]+"${PROFILES[@]}"}"; do
        rm -rf "$profile"
    done
}
trap cleanup EXIT INT TERM

reader_require_app

if ! python3 -c 'import PIL' 2>/dev/null; then
    echo "Pillow is needed to measure the screenshots: python3 -m pip install pillow" >&2
    exit 1
fi

new_profile() {
    local profile
    profile="$(mktemp -d)"
    PROFILES+=("$profile")

    reader_seed_profile "$profile"

    python3 - "$profile/settings.json" <<'PY'
import json, sys
path = sys.argv[1]
with open(path) as f:
    settings = json.load(f)
settings["theme"] = "Light"
with open(path, "w") as f:
    json.dump(settings, f, indent=2)
PY

    echo "$profile"
}

run_script() {
    local profile="$1" script="$2" out="$3"

    LUCIDREADER_DATA_DIR="$profile" "$READER_APP" \
        --ux-test --script "$READER_REPO/ux-scripts/$script" --output "$out"

    # Same launch-rate limit reader-harness.sh documents: macOS occasionally
    # refuses a second app process a display link straight after one exits.
    sleep 2
}

rm -rf "$OUT_ROOT"
mkdir -p "$OUT_ROOT"

# ---------------------------------------------------------------------------
# The cycle, in one process.
# ---------------------------------------------------------------------------
CYCLE_PROFILE="$(new_profile)"
run_script "$CYCLE_PROFILE" verify-pane-layout.yaml "$OUT_ROOT/cycle"

# ---------------------------------------------------------------------------
# The restart, in two processes against one profile.
# ---------------------------------------------------------------------------
RESTART_PROFILE="$(new_profile)"
run_script "$RESTART_PROFILE" verify-pane-layout-persist.yaml "$OUT_ROOT/persist"

STORED="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("layoutMode"))' \
    "$RESTART_PROFILE/settings.json")"
if [[ "$STORED" != "ReadingOnly" ]]; then
    echo "FAIL: settings.json holds layoutMode=$STORED after collapsing twice, expected ReadingOnly" >&2
    exit 1
fi
echo "settings.json holds layoutMode=$STORED after the first process exited"

run_script "$RESTART_PROFILE" verify-pane-layout-restore.yaml "$OUT_ROOT/restart"

# ---------------------------------------------------------------------------
# The measurement.
# ---------------------------------------------------------------------------
python3 "$READER_REPO/ux-scripts/measure-pane-layout.py" \
    "$OUT_ROOT/cycle/01-three-pane.png" \
    "$OUT_ROOT/cycle/02-list-and-reading.png" \
    "$OUT_ROOT/cycle/03-reading-only.png" \
    "$OUT_ROOT/cycle/04-back-to-three-pane.png" \
    "$OUT_ROOT/restart/restart-reading-only.png"
