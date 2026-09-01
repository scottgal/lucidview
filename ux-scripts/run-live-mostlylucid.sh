#!/usr/bin/env bash
# Drives mylo against real articles from https://www.mostlylucid.net.
#
# Usage: ux-scripts/run-live-mostlylucid.sh [output-dir]
#
# Needs network. mostlylucid.net is the maintainer's own site and the only
# host sanctioned for repeated automated fetching, so it is the only one this
# script touches: exactly two requests are made from the shell (the feed, to
# pick a search word) and the rest are made by the app itself while it
# subscribes and refreshes. Do not add a second publisher here.
#
# Headless, like every other script here, and for the reason
# ux-scripts/reader-harness.sh gives: driving the app on the native platform
# puts a window on screen and takes keyboard focus on every launch. Both
# launches below therefore pass --ux-headless unless MYLO_UX_MODE says
# otherwise.
#
# Everything happens in a throwaway profile, seeded and deleted by this
# script, for the reasons ux-scripts/reader-harness.sh sets out at length: a
# script that asserts on a database it did not create is asserting on
# whatever the person running it happened to have, and one that mutates the
# real database to make its own assertions true is worse. The real profile at
# ~/Library/Application Support/mylo is never opened.
#
# It is repeatable by construction: the profile is new every run and removed
# on the way out including on failure and on interrupt, so a second run starts
# from the same nothing the first one did.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

OUT="${1:-/tmp/lr-live-mostlylucid}"
FEED="https://www.mostlylucid.net/rss"

reader_require_app

if ! command -v python3 >/dev/null 2>&1; then
    echo "python3 is needed to read a search word out of the live feed." >&2
    exit 1
fi

PROFILE="$(mktemp -d)"
trap 'rm -rf "$PROFILE"' EXIT INT TERM

# refreshOnStartup is off so the fetch happens when the script subscribes
# rather than racing the window opening, and so an empty profile stays empty
# until then - the first screenshot asserts exactly that. The offline settings
# are on because the download path and the markdown conversion are half of
# what this run exists to check. Images are left off: they are a separate
# pipeline with its own scripts, and fetching them would mean a lot of
# unnecessary requests to a site being tested for something else.
#
# The presence of this file is also what keeps the first-run default feeds
# from firing: FirstRunSeedPolicy treats a profile with a settings.json as one
# that has been used before. That is deliberate and asserted below.
cat >"$PROFILE/settings.json" <<'JSON'
{
  "defaultRefreshIntervalMinutes": 30,
  "refreshOnStartup": false,
  "pauseWhenOffline": true,
  "maxConcurrentFetches": 4,
  "autoDownloadArticles": true,
  "fetchFullText": true,
  "cacheImages": false,
  "maxImageBytes": 5242880,
  "maxConcurrentDownloads": 2,
  "keepReadArticlesDays": 30,
  "keepUnreadForever": true,
  "keepUnreadDays": 180,
  "maxArticlesPerFeed": 500,
  "neverDeleteStarred": true,
  "theme": "Auto",
  "fontSize": 15,
  "columnWidth": 760,
  "markReadDwellMilliseconds": 300,
  "openLinksExternally": true,
  "enableOnlineFeedSearch": false
}
JSON

# The schema is created by the app itself, the same way reader_seed_profile
# does it, rather than by a copy of the DDL here that would rot on the next
# migration. Retried for the same reason: macOS occasionally refuses a second
# app process a display link when one has only just exited, which shows up as
# a crashed bootstrap with no database.
cat >"$PROFILE/bootstrap.yaml" <<'YAML'
name: bootstrap
description: Open once so SchemaMigrator creates the database, then exit.
default_delay: 100
actions:
  - type: Wait
    value: "1500"
YAML

for attempt in 1 2 3; do
    MYLO_DATA_DIR="$PROFILE" "$READER_APP" ${MYLO_UX_MODE:---ux-headless} \
        --ux-test --script "$PROFILE/bootstrap.yaml" --output "$PROFILE/bootstrap" \
        >"$PROFILE/bootstrap.log" 2>&1 || true
    [[ -f "$PROFILE/reader.db" ]] && break
    sleep 3
done

if [[ ! -f "$PROFILE/reader.db" ]]; then
    echo "The app did not create $PROFILE/reader.db after 3 attempts. Last log:" >&2
    tail -20 "$PROFILE/bootstrap.log" >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# The search word.
#
# It cannot be written into the script file: the assertion is that full-text
# search finds a word from a REAL article, and a word chosen in advance would
# only prove that FTS5 can find whatever this repository decided to say. So
# the live feed is read once here and the longest plain word in the newest
# article's title is used. Longest because a short word is likelier to be a
# preposition that appears in a dozen articles, which would still pass but
# would prove less.
# ---------------------------------------------------------------------------
curl -fsS -A "mylo live harness" -o "$PROFILE/feed.xml" "$FEED"

SEARCH_WORD="$(python3 - "$PROFILE/feed.xml" <<'PY'
# Read with a regex rather than an XML parser. This only needs the first
# <title> inside the first <item>, and pointing a stdlib XML parser at a
# document fetched over the network is a bigger tool than the job wants: it
# resolves external entities by default, which is a class of problem this
# script has no reason to be exposed to at all.
import re, sys

text = open(sys.argv[1], encoding='utf-8', errors='replace').read()

item = re.search(r'<(?:item|entry)\b.*?</(?:item|entry)>', text, re.S | re.I)
if not item:
    raise SystemExit("the feed came back with no items")

title = re.search(r'<title[^>]*>(.*?)</title>', item.group(0), re.S | re.I)
if not title:
    raise SystemExit("the newest item has no title")

# Unwrap CDATA and drop any markup the title carried.
raw = re.sub(r'<!\[CDATA\[(.*?)\]\]>', r'\1', title.group(1), flags=re.S)
raw = re.sub(r'<[^>]+>', ' ', raw)

words = re.findall(r'[A-Za-z]{6,}', raw)
if not words:
    raise SystemExit("the newest article's title has no word long enough to search for: " + raw.strip())

print(max(words, key=len).lower())
PY
)"

echo "Newest article title supplied the search word: $SEARCH_WORD"

SCRIPT="$PROFILE/verify-live-mostlylucid.yaml"
sed "s/__SEARCH_WORD__/$SEARCH_WORD/" \
    "$READER_REPO/ux-scripts/verify-live-mostlylucid.yaml.template" >"$SCRIPT"

# Same launch-rate limit reader_seed_profile documents: give the bootstrap
# process time to let go of the display link.
sleep 2

# The status is captured rather than allowed to end the script, because the
# excerpt below is most worth reading on the run that failed: an assertion
# about the reading pane is a great deal easier to explain once you can see
# what the conversion actually produced.
STATUS=0
MYLO_DATA_DIR="$PROFILE" "$READER_APP" ${MYLO_UX_MODE:---ux-headless} --ux-test --script "$SCRIPT" --output "$OUT" || STATUS=$?

# ---------------------------------------------------------------------------
# What actually arrived. The assertions above prove the app is showing
# something; this prints it, so the conversion quality can be judged by a
# person rather than inferred from a passing script. Run before the trap
# removes the profile.
# ---------------------------------------------------------------------------
echo
echo "===== what the run actually stored ====="
sqlite3 "$PROFILE/reader.db" <<'SQL'
.mode list
SELECT 'feeds: ' || COUNT(*) FROM feeds;
SELECT 'items: ' || COUNT(*) FROM items;
SELECT 'items with extracted markdown: ' || COUNT(*)
  FROM items WHERE content_source = 1 AND LENGTH(COALESCE(content_markdown,'')) > 200;
SELECT 'tags, none of them typed by this run: ' || COUNT(*) FROM tags;
SQL

# The tags themselves. Nothing in this run types one, so every name here came
# out of a <category> element on a real post, and printing them is how a
# person can see that the import produced names worth having rather than
# whatever the parser happened to accept.
echo
echo "===== publisher categories that became tags ====="
sqlite3 "$PROFILE/reader.db" <<'SQL'
.mode list
SELECT t.name || '  (' || COUNT(it.item_id) || ' articles)'
  FROM tags t JOIN item_tags it ON it.tag_id = t.id
 GROUP BY t.id, t.name
 ORDER BY COUNT(it.item_id) DESC, t.name COLLATE NOCASE
 LIMIT 40;
SQL

echo
echo "===== markdown excerpt from one real article ====="
sqlite3 "$PROFILE/reader.db" \
    "SELECT title || char(10) || char(10) || substr(content_markdown, 1, 1200)
     FROM items
     WHERE content_source = 1 AND LENGTH(COALESCE(content_markdown,'')) > 200
     ORDER BY LENGTH(content_markdown) DESC
     LIMIT 1;"

exit $STATUS
