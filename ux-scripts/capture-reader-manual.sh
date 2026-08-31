#!/usr/bin/env bash
# Regenerates every screenshot in mylo's bundled user manual.
#
# Usage: ux-scripts/capture-reader-manual.sh [output-dir]
#
# Default output is LucidReader/Assets/manual/screenshots, which is what the
# manual references and what LucidReader.csproj copies beside the executable.
# Pass a directory to look at a run without overwriting the checked-in set.
#
# Run this after any change to the reader's chrome. The alternative is a manual
# full of pictures of a version of mylo that no longer exists, which is worse
# than no pictures at all because it reads as documentation.
#
# What it does: seeds a throwaway MYLO_DATA_DIR profile from the shared
# fixture, layers on the two things the fixture has no reason to carry (tags,
# and fetch timestamps for the per-feed update line), pins the theme so the
# whole set matches, then runs two scripts against it. The profile is removed
# by an EXIT/INT/TERM trap, so this can be run twice in a row from any starting
# state and touches no real profile. See ux-scripts/reader-harness.sh for why
# that shape is the rule here.
#
# Headless: nothing appears on screen and nothing takes focus. Set
# MYLO_UX_ONSCREEN=1 to watch it happen.
#
# Network: one pair of screenshots, the add-feed discovery, resolves xkcd.com.
# Everything else is offline. Without network the run fails at that step and
# the screenshots taken before it are still written.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

OUT="${1:-$READER_REPO/LucidReader/Assets/manual/screenshots}"
THEME="${MYLO_MANUAL_THEME:-Light}"

reader_require_app
if ! python3 -c 'import PIL' 2>/dev/null; then
    echo "Pillow is needed to size the captures: python3 -m pip install pillow" >&2
    exit 1
fi
mkdir -p "$OUT"

PROFILE="$(mktemp -d)"
trap 'rm -rf "$PROFILE"' EXIT INT TERM

reader_seed_profile "$PROFILE"

# Pinned rather than left on Auto. Auto follows the machine's appearance, so
# the set would come out light on one laptop and dark on the next, and half
# light and half dark if the two scripts below ran either side of sunset.
sed -i '' "s/\"theme\": \"Auto\"/\"theme\": \"$THEME\"/" "$PROFILE/settings.json"

# Put the mark-as-read dwell back to the shipped default. reader_seed_profile
# drops it to 300ms so a driving script can wait the dwell out deliberately;
# here it is a number a reader can see, in the Reading tab screenshot, and a
# picture of a setting has to show what a new profile actually has.
sed -i '' 's/"markReadDwellMilliseconds": 300/"markReadDwellMilliseconds": 800/' \
    "$PROFILE/settings.json"

# The fixture seeds no tags and no fetch timestamps, because the scripts that
# assert against it are about neither. The manual is about both: without this
# the sidebar has no Tags section, the article has no tag chips, and the
# per-feed update line reads "Not updated yet" on every feed.
#
# The half-minute offsets are the same trick run-feed-update-chunk.sh uses: a
# feed seeded exactly four minutes ago reads "4 min ago" only until the run's
# own startup pushes it to five, and seeding it four and a half minutes ago
# leaves a thirty second window in which the wording cannot change.
sqlite3 "$PROFILE/reader.db" <<'SQL'
UPDATE feeds
   SET last_fetched_utc = strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-4 minutes', '-30 seconds'),
       last_success_utc = strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-4 minutes', '-30 seconds'),
       next_due_utc     = strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '+26 minutes', '+30 seconds')
 WHERE id = 1;

INSERT INTO tags (id, name) VALUES (1, 'rendering'), (2, 'read later');
INSERT INTO item_tags (item_id, tag_id) VALUES (1, 1), (1, 2), (2, 2);

-- The fixture's own bodies are two short lines, which is all its assertions
-- need and nothing at all to look at. A picture of the reading pane is meant
-- to show what the reading pane does with an article, so the one article the
-- manual shows is rewritten here with the shapes a real post has: headings, a
-- list, a quote and a code fence. No row is added and no title changes, so
-- every other script's counts and locators are untouched.
UPDATE items
   SET content_markdown = 'Everything drawn in this pane is drawn by Avalonia. There is no browser
engine in mylo, so an article renders at native speed and looks like the rest
of the application rather than like a web page inside it.

## What the renderer handles

- Headings, lists, tables and block quotes
- Fenced code, with syntax highlighting
- Images, cached locally once they have been fetched once
- Mermaid diagrams, drawn rather than screenshotted

> The pipeline runs on its own thread, so a long article never blocks the
> list you are scrolling.

```csharp
var article = await items.GetAsync(id);
reading.Markdown = article.ContentMarkdown ?? article.Summary;
```

The column width, type size, line height and code size are all yours to set,
under Settings, Reading.'
 WHERE id = 1;
SQL

MYLO_DATA_DIR="$PROFILE" "$READER_APP" ${MYLO_UX_MODE:---ux-headless} \
    --ux-test --script "$READER_REPO/ux-scripts/capture-reader-manual.yaml" --output "$OUT"

# Same launch-rate limit reader_seed_profile documents: macOS occasionally
# refuses a second app process a display link when one has only just exited.
sleep 2

# The menu shots, in their own process because the switch that makes the menus
# visible to the harness also puts a menu bar into the window that macOS does
# not have. See capture-reader-menus.yaml.
MYLO_DATA_DIR="$PROFILE" MYLO_FORCE_WINDOW_MENU=1 "$READER_APP" ${MYLO_UX_MODE:---ux-headless} \
    --ux-test --script "$READER_REPO/ux-scripts/capture-reader-menus.yaml" --output "$OUT"

# result.json is the harness's own report, not a screenshot, and a copy of it
# in the manual's asset folder would be shipped inside the app for no reason.
rm -f "$OUT/result.json"

# Sized down to fit the reading column, and this is not cosmetic.
#
# LiveMarkdown gives a markdown image its natural size. There is no fit-to-
# width anywhere in the path: the Image sits in an InlineUIContainer inside a
# TextBlock, which measures it at whatever the bitmap is, so a 1280px capture
# dropped into a 672px reading pane is drawn 1280px wide and clipped at the
# pane's edge. Half of every full-window screenshot in the manual was simply
# not on screen, and nothing said so.
#
# 600 is the width that fits mylo's own default: a 1280x820 window in the
# three-pane layout leaves the reading pane 672px, of which the centred column
# is a little less. Snips narrower than that (the layout button, the offline
# badge, the tag strip) are left exactly as captured - upscaling a 90px snip to
# 600 would only make it blurry.
python3 - "$OUT" <<'PY'
import pathlib
import sys

from PIL import Image

MAX_WIDTH = 600

for path in sorted(pathlib.Path(sys.argv[1]).glob("*.png")):
    with Image.open(path) as image:
        if image.width <= MAX_WIDTH:
            continue
        height = round(image.height * MAX_WIDTH / image.width)
        resized = image.convert("RGB").resize((MAX_WIDTH, height), Image.LANCZOS)
    resized.save(path)
    print(f"  sized {path.name} to {MAX_WIDTH}x{height}")
PY

echo
echo "Screenshots in $OUT:"
ls -1 "$OUT"
