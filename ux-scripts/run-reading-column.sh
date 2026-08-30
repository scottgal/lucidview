#!/usr/bin/env bash
# Proves the reading column is centred, by measuring it.
#
# Usage: ux-scripts/run-reading-column.sh [output-dir]
#
# Runs verify-reading-column.yaml against three scratch profiles that differ
# only in their reading settings, then measures reading-pane.png from each with
# PIL. Three things have to hold, and none is a property assertion:
#
#   1. Left and right margins are equal, to within a pixel or two. That is
#      what "centred with a variable margin" means, and it is the thing an
#      external Style cannot fake.
#   2. The column is narrower in the second run than the first, and the margin
#      is correspondingly wider. That proves the setting reaches the layout
#      rather than the column merely happening to look centred.
#   3. A third run at a bigger font size and a looser line height pushes the
#      article visibly further down the pane. FontSize was a dead setting in
#      mylo until now, and the reading pane's font sizes are set by
#      LiveMarkdown.Avalonia's application-level stylesheet, so "the setting is
#      stored" and "the text got bigger" are genuinely different claims.
#   4. A fourth run raises only codeFontSize. The article gets taller while the
#      hairline above the body stays exactly where it was, which is what
#      separates "the code block grew" from "everything grew". The article is
#      given a fenced code block for this, in the scratch profile only.
#
# Needs no network and touches no real profile: every run gets its own
# MYLO_DATA_DIR seeded from reader-fixture.sql, removed on the way out
# including on failure and on interrupt, so this is repeatable by construction
# and can be run twice in a row. See reader-harness.sh for the rest of that.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

OUT_ROOT="${1:-/tmp/lr-reading-column}"
WIDE_WIDTH=760
NARROW_WIDTH=420

# Two profiles are alive at once here, so the shared reader_run (which tracks
# exactly one) is not used; this keeps its own list and removes all of them.
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

run_profile() {
    local width="$1" font="$2" line_height="$3" code_font="$4" out="$5"
    local profile
    profile="$(mktemp -d)"
    PROFILES+=("$profile")

    reader_seed_profile "$profile"

    # Patched after seeding rather than by editing reader-harness.sh: the
    # shared fixture settings are what every other script measures against and
    # must not move because this one wants a different column.
    python3 - "$profile/settings.json" "$width" "$font" "$line_height" "$code_font" <<'PY'
import json, sys
path = sys.argv[1]
width, font, line_height, code_font = (float(a) for a in sys.argv[2:6])
with open(path) as f:
    settings = json.load(f)
settings["columnWidth"] = width
settings["fontSize"] = font
settings["lineHeight"] = line_height
settings["codeFontSize"] = code_font
with open(path, "w") as f:
    json.dump(settings, f, indent=2)
PY

    # A fenced code block, so codeFontSize has something to be measured on.
    # Patched into this scratch profile only: reader-fixture.sql is shared with
    # the other driving scripts and their assertions depend on it as it stands.
    # Quoted heredoc, so the fence's backticks stay backticks instead of being
    # taken for command substitution.
    sqlite3 "$profile/reader.db" <<'SQL'
UPDATE items SET content_markdown = 'The compositor pipeline, end to end.

```csharp
var frame = compositor.BeginFrame();
```

Rendering happens on its own thread.' WHERE guid = 'alpha-1';
SQL

    MYLO_DATA_DIR="$profile" "$READER_APP" \
        --ux-test --script "$READER_REPO/ux-scripts/verify-reading-column.yaml" \
        --output "$out"

    # Same launch-rate limit reader-harness.sh documents: macOS occasionally
    # refuses a second app process a display link straight after one exits.
    sleep 2
}

rm -rf "$OUT_ROOT"
mkdir -p "$OUT_ROOT"

#           width           font  line  code  output
run_profile "$WIDE_WIDTH"   15    1.5   13    "$OUT_ROOT/wide"
run_profile "$NARROW_WIDTH" 15    1.5   13    "$OUT_ROOT/narrow"
run_profile "$WIDE_WIDTH"   26    2.2   13    "$OUT_ROOT/large-type"
run_profile "$WIDE_WIDTH"   15    1.5   28    "$OUT_ROOT/large-code"

python3 - "$READER_REPO/ux-scripts/measure-reading-column.py" \
         "$OUT_ROOT/wide/reading-pane.png" \
         "$OUT_ROOT/narrow/reading-pane.png" \
         "$OUT_ROOT/large-type/reading-pane.png" \
         "$OUT_ROOT/large-code/reading-pane.png" <<'PY'
import json, subprocess, sys

measure = sys.argv[1]

results = []
for image in sys.argv[2:]:
    results.append(json.loads(subprocess.check_output([sys.executable, measure, image])))

wide, narrow, large, large_code = results
failures = []

for label, r in (("wide", wide), ("narrow", narrow),
                 ("large-type", large), ("large-code", large_code)):
    print(f"{label}: pane {r['image_width']}px  column {r['column_span']}px  "
          f"left margin {r['left_margin']}px  right margin {r['right_margin']}px  "
          f"hairline y={r['widest_row_y']}  article bottom y={r['content_bottom']}")
    # Two physical pixels on a 2x display is one device-independent pixel, and
    # a centred odd-width column genuinely lands half a pixel off.
    if abs(r["left_margin"] - r["right_margin"]) > 2:
        failures.append(f"{label}: margins differ by "
                        f"{abs(r['left_margin'] - r['right_margin'])}px, so the column is not centred")

if narrow["column_span"] >= wide["column_span"]:
    failures.append("the narrow run's column is not narrower, so columnWidth never reached the layout")
if narrow["left_margin"] <= wide["left_margin"]:
    failures.append("the narrow run's margin did not grow, so the margin is not variable")

# The header above the hairline is the article title and byline, both sized
# from FontSize; the article body below it is sized and spaced from FontSize
# and LineHeight. If either had gone nowhere, these would not move.
if large["widest_row_y"] <= wide["widest_row_y"]:
    failures.append("a bigger font size did not push the hairline down, "
                    "so FontSize is not reaching the article header")
if large["content_bottom"] <= wide["content_bottom"]:
    failures.append("a bigger font size and looser line height did not make the article taller, "
                    "so the typography settings are not reaching the rendered markdown")
if large["column_span"] != wide["column_span"]:
    failures.append("the column changed width with the font size, "
                    "which means the type is being scaled by a transform rather than sized")

# Only codeFontSize differs between this run and the wide one, so the header
# above the hairline must be exactly where it was and the article below it must
# be taller. That is what separates "the code grew" from "everything grew".
if large_code["widest_row_y"] != wide["widest_row_y"]:
    failures.append("raising codeFontSize moved the article header, "
                    "so it is not confined to code")
if large_code["content_bottom"] <= wide["content_bottom"]:
    failures.append("raising codeFontSize did not make the code block taller, "
                    "so CodeFontSize is not reaching the rendered markdown")

if failures:
    for f in failures:
        print("FAIL: " + f)
    sys.exit(1)

print("PASS: the column is centred at every width, narrowing it widened the margin, "
      "bigger type made the article taller without moving the column, "
      "and code size moved only the code.")
PY
