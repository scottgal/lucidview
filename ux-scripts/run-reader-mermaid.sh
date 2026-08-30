#!/usr/bin/env bash
# Runs verify-reader-mermaid.yaml against a scratch profile whose first
# article body is a mermaid flowchart.
#
# Usage: ux-scripts/run-reader-mermaid.sh [output-dir]
#
# The fixture's own bodies are plain prose, so this script rewrites one of
# them rather than adding a sixth article: every other reader script asserts
# exact row counts against that fixture, and a new row would break all of
# them. The rewritten article is Compositor internals explained, which the
# script below selects by title.
#
# Needs no network. Leaves nothing behind, touches no real profile, and can
# be run twice in a row: see ux-scripts/reader-harness.sh for how, and why.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

OUT="${1:-/tmp/lr-reader-mermaid}"
reader_require_app
if ! python3 -c 'import PIL' 2>/dev/null; then
    echo "Pillow is needed to check the captured pane: python3 -m pip install pillow" >&2
    exit 1
fi
rm -rf "$OUT"

READER_PROFILE="$(mktemp -d)"
trap 'rm -rf "$READER_PROFILE"' EXIT INT TERM

reader_seed_profile "$READER_PROFILE"

# The seeded settings leave the theme on Auto, which follows the machine's
# appearance and would decide which half of the FlowchartCanvas palette the
# check below is looking at. Pinning it to Dark is what lets that check use
# fixed thresholds rather than ones that hold on one laptop and not the next.
python3 - "$READER_PROFILE/settings.json" <<'PY'
import json
import sys

path = sys.argv[1]
with open(path) as f:
    settings = json.load(f)
settings["theme"] = "Dark"
with open(path, "w") as f:
    json.dump(settings, f, indent=2)
PY

sqlite3 "$READER_PROFILE/reader.db" <<'SQL'
UPDATE items
SET content_markdown = 'Some prose before the diagram.

```mermaid
graph TD
    A[Feed poll] --> B{New items?}
    B -->|yes| C[Download article]
    B -->|no| D[Sleep]
    C --> E[Convert to markdown]
```

Some prose after the diagram.',
    offline_state = 2
WHERE title = 'Compositor internals explained';
SQL

MYLO_DATA_DIR="$READER_PROFILE" "$READER_APP" \
    --ux-test --script "$READER_REPO/ux-scripts/verify-reader-mermaid.yaml" --output "$OUT"

# The script's Expect proves a FlowchartCanvas is in the visual tree, which
# with one diagram in the document means the marker was found and replaced.
# This proves the canvas actually painted a diagram rather than sitting there
# empty, which no Expect can ask about: a FlowchartCanvas draws its nodes and
# labels straight onto a DrawingContext, so it has no children to assert on.
#
# What it looks for is colour. Every node in the FlowchartCanvas dark palette
# is a saturated fill, while an article that is only text is background,
# near-white glyphs and a grey rule, none of which is saturated at all. The
# numbers this is set against, measured on the panes the other reader scripts
# capture: this article drawn as a diagram gives 44666 saturated pixels with
# 35535 and 8731 in its two main hues, an ordinary prose article gives 916,
# and the worst case for a false pass, an article of syntax-highlighted code,
# gives 2692. The thresholds sit in that gap. They are deliberately about
# saturation and spread rather than specific colours, so that restyling the
# palette does not quietly turn this into a check of nothing.
PANE="$OUT/reading-pane.png"
if [[ ! -s "$PANE" ]]; then
    echo "No pane snip at $PANE, so nothing was checked. The script above stopped early." >&2
    exit 1
fi

python3 - "$PANE" <<'PY'
import colorsys
import sys

from PIL import Image

MIN_SATURATED_PIXELS = 10000
MIN_PIXELS_PER_HUE = 4000
MIN_HUES = 2

path = sys.argv[1]
image = Image.open(path).convert("RGB")
pixels = image.tobytes()
hues = {}
saturated = 0
for i in range(0, len(pixels), 3):
    r, g, b = pixels[i], pixels[i + 1], pixels[i + 2]
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    if s < 0.35 or v < 0.15:
        continue
    saturated += 1
    bucket = int(h * 12) % 12
    hues[bucket] = hues.get(bucket, 0) + 1

strong = sorted((n for n in hues.values() if n >= MIN_PIXELS_PER_HUE), reverse=True)
print(f"Pane colour check: {saturated} saturated pixels, {len(strong)} hues {strong}")

if saturated < MIN_SATURATED_PIXELS or len(strong) < MIN_HUES:
    sys.exit(
        f"{path} holds no drawn diagram: expected at least {MIN_SATURATED_PIXELS} "
        f"saturated pixels across {MIN_HUES} hues, which is what a flowchart's node "
        f"fills and strokes produce and what an article of plain text cannot."
    )
PY
