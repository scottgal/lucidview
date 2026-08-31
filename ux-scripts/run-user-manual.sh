#!/usr/bin/env bash
# Runs verify-user-manual.yaml, then checks that the manual's screenshots were
# really painted rather than merely referenced.
#
# Usage: ux-scripts/run-user-manual.sh [output-dir]
#
# Needs no network. Leaves nothing behind, touches no real profile, and can be
# run twice in a row: the profile is a throwaway MYLO_DATA_DIR directory
# removed by an EXIT/INT/TERM trap. See ux-scripts/reader-harness.sh for how,
# and why.
#
# MYLO_FORCE_WINDOW_MENU=1 is what makes the Help menu clickable at all. On
# macOS mylo's menus are a NativeMenu drawn by AppKit outside any surface
# Avalonia owns, so the harness can neither find an item nor click one. The
# switch is Debug-only and renders the same menu description as the in-window
# Menu that Windows and Linux get, which is the only way this route can be
# driven here. F1, checked at the end of the same script, is the route that
# works without it.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

OUT="${1:-/tmp/lr-user-manual}"

reader_require_app
if ! python3 -c 'import PIL' 2>/dev/null; then
    echo "Pillow is needed to check the captured pane: python3 -m pip install pillow" >&2
    exit 1
fi
rm -rf "$OUT"

PROFILE="$(mktemp -d)"
trap 'rm -rf "$PROFILE"' EXIT INT TERM

reader_seed_profile "$PROFILE"

# Pinned, for the same reason run-reader-mermaid.sh pins it: the seeded
# settings leave the theme on Auto, which follows the machine's appearance and
# would decide what the colour check below is looking at. Light is also the
# appearance the checked-in screenshots were captured in, so the manual's own
# pictures are a light UI inside a light pane and the accents the check counts
# are the ones actually there.
sed -i '' 's/"theme": "Auto"/"theme": "Light"/' "$PROFILE/settings.json"

MYLO_DATA_DIR="$PROFILE" MYLO_FORCE_WINDOW_MENU=1 "$READER_APP" ${MYLO_UX_MODE:---ux-headless} \
    --ux-test --script "$READER_REPO/ux-scripts/verify-user-manual.yaml" --output "$OUT"

# The script's Expect proves an Image control is in the rendered manual, which
# it would be whether or not its source resolved. This proves paint happened.
#
# What it looks for is saturated colour. Every screenshot in the manual is a
# picture of mylo, and mylo's chrome carries a saturated accent: the selected
# sidebar row, the checked filter segment, the unread dot. The manual's prose
# has none - body text in the Light theme is near-black on near-white, and the
# section this snip lands on holds no code fence, so there is no syntax
# highlighting to be mistaken for a picture either.
#
# Measured on this pane, both ways round. With the images drawing: 1353
# saturated pixels. With UserManual.RewriteImagePaths taken out of the load
# path, so that every reference resolves against the temporary directory and
# nothing loads: 0. The threshold sits in that gap.
#
# Worth recording what that experiment also showed: with the images broken,
# every Expect in the script above still passed, including the one that finds
# an Image control in the rendered manual. A broken image is still a control.
# This check is the only thing between "the manual opened" and "the manual is
# a page of missing pictures".
PANE="$OUT/manual-pane.png"
if [[ ! -s "$PANE" ]]; then
    echo "No pane snip at $PANE, so nothing was checked. The script above stopped early." >&2
    exit 1
fi

python3 - "$PANE" <<'PY'
import colorsys
import sys

from PIL import Image

MIN_SATURATED_PIXELS = 600

path = sys.argv[1]
image = Image.open(path).convert("RGB")
pixels = image.tobytes()

saturated = 0
for i in range(0, len(pixels), 3):
    r, g, b = pixels[i], pixels[i + 1], pixels[i + 2]
    _, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    if s >= 0.35 and v >= 0.15:
        saturated += 1

print(f"Manual pane colour check: {saturated} saturated pixels")

if saturated < MIN_SATURATED_PIXELS:
    sys.exit(
        f"{path} holds no drawn screenshot: expected at least "
        f"{MIN_SATURATED_PIXELS} saturated pixels, which is what a picture of "
        f"mylo's own chrome produces and what the manual's prose cannot. "
        f"Either the images stopped resolving, or the manual's opening "
        f"sections moved and the Scroll offset in verify-user-manual.yaml no "
        f"longer lands on one."
    )
PY
