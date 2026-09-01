#!/usr/bin/env bash
# Proves the non-macOS window chrome branch runs, by measuring the gutter it
# is supposed to remove.
#
# Usage: ux-scripts/run-system-chrome.sh [output-dir]
#
# MainWindow.axaml gives the toolbar Margin="80,10,12,10". The 80 is the space
# the macOS traffic lights need. Windows and Linux put their system buttons on
# the right instead, so ConfigurePlatformChrome (MainWindow.Layout.cs) turns
# the extended client area off there and rewrites the margin to an even 12.
#
# That branch is dead code on the machine mylo is developed on, and its one
# silent failure is FindControl returning null: the margin would stay at 80,
# Windows would get an empty gutter on the left, and nothing would be thrown
# or logged. MYLO_FORCE_SYSTEM_CHROME=1, Debug builds only, makes the branch
# reachable here so that cannot happen unnoticed.
#
# The measurement is a pair, because one screenshot of a toolbar is a picture
# rather than a measurement. The same script runs twice against identically
# seeded profiles, once normally and once with the variable set, and the two
# toolbars are compared:
#
#   default   the leftmost 80px of the toolbar row are flat background
#   forced    the layout button's dark icon strokes appear inside the first 40
#
# **This says nothing about how mylo looks on Windows.** It cannot: this is a
# Mac. It says the code path Windows will take executes, finds the toolbar and
# moves it, which is the part that could have failed silently.
#
# Needs no network and touches no real profile: every run gets its own
# MYLO_DATA_DIR seeded from reader-fixture.sql and removed on the way out
# including on failure and on interrupt. See reader-harness.sh.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

OUT_ROOT="${1:-/tmp/lr-system-chrome}"

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

rm -rf "$OUT_ROOT"
mkdir -p "$OUT_ROOT"

DEFAULT_PROFILE="$(new_profile)"
MYLO_DATA_DIR="$DEFAULT_PROFILE" "$READER_APP" \
    --ux-test --script "$READER_REPO/ux-scripts/verify-system-chrome.yaml" \
    --output "$OUT_ROOT/default"

# Same launch-rate limit reader-harness.sh documents: macOS occasionally
# refuses a second app process a display link straight after one exits.
sleep 2

FORCED_PROFILE="$(new_profile)"
MYLO_FORCE_SYSTEM_CHROME=1 MYLO_DATA_DIR="$FORCED_PROFILE" "$READER_APP" \
    --ux-test --script "$READER_REPO/ux-scripts/verify-system-chrome.yaml" \
    --output "$OUT_ROOT/forced"

python3 - "$OUT_ROOT/default/toolbar.png" "$OUT_ROOT/forced/toolbar.png" <<'PY'
import sys
from PIL import Image

WINDOW_WIDTH = 1280          # MainWindow.axaml
MAC_GUTTER = 80              # the traffic-light margin
SYSTEM_GUTTER = 12           # what ConfigurePlatformChrome rewrites it to


def first_dark_column(path):
    """Leftmost column in the toolbar band holding an icon-dark pixel.

    The toolbar's background is a light panel in the Light theme and the
    layout button's icon is drawn with RowTitleBrush, which is near black. So
    the first dark column is where the toolbar's content starts, which is the
    margin. Looking for dark rather than for "not the background colour"
    keeps this from tripping over the hairline border or an antialiased edge.
    """
    image = Image.open(path).convert("RGB")
    scale = image.width / WINDOW_WIDTH

    # The toolbar row, in window points. Row 0 is the in-window menu, which is
    # hidden on macOS, so the toolbar starts at the top. Sampling a band well
    # inside it avoids the border on either edge.
    top = int(14 * scale)
    bottom = int(46 * scale)
    limit = int(200 * scale)

    for x in range(min(limit, image.width)):
        for y in range(top, min(bottom, image.height)):
            r, g, b = image.getpixel((x, y))
            if r < 120 and g < 120 and b < 120:
                return x / scale, scale
    return None, scale


default_x, scale = first_dark_column(sys.argv[1])
forced_x, _ = first_dark_column(sys.argv[2])

print(f"screenshot scale: {scale:g}x")
print(f"default  first toolbar content at x = {default_x}")
print(f"forced   first toolbar content at x = {forced_x}")

failures = []

if default_x is None:
    failures.append("default run: no toolbar content found at all")
elif default_x < MAC_GUTTER - 8:
    failures.append(
        f"default run: content at x={default_x:.0f}, expected the macOS "
        f"{MAC_GUTTER}px traffic-light gutter to be empty"
    )

if forced_x is None:
    failures.append("forced run: no toolbar content found at all")
elif forced_x > MAC_GUTTER - 20:
    failures.append(
        f"forced run: content still at x={forced_x:.0f}. "
        f"ConfigurePlatformChrome did not move the toolbar to {SYSTEM_GUTTER}px, "
        f"which is what Windows and Linux would get"
    )

if failures:
    for f in failures:
        print("FAIL: " + f)
    sys.exit(1)

print()
print(f"The macOS layout leaves the first {default_x:.0f}px of the toolbar empty "
      f"for the traffic lights.")
print(f"With the system title bar the toolbar starts at {forced_x:.0f}px instead, "
      f"so the gutter is gone.")
print()
print("Result: PASS")
PY
