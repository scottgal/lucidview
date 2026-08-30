#!/usr/bin/env bash
# Proves the duplicate-article fix against a real publisher.
#
# Usage: ux-scripts/run-live-dedupe.sh [output-dir]
#
# Needs network. mostlylucid.net is the maintainer's own site and the only
# host sanctioned for repeated automated fetching, so it is the only one this
# script touches. Do not add a second publisher here.
#
# Two runs, against two throwaway profiles:
#
#   BEFORE  a profile seeded with BOTH of the site's feeds already
#           subscribed, which is what a user who pressed Add before the
#           alternate-format detection existed has today. Both are refreshed.
#           The database ends up with two rows per article; the item list
#           shows each article once.
#
#   AFTER   an empty profile, subscribed through the add-feed dialog with
#           nothing unticked. Discovery now recognises the second feed as the
#           same articles in another format and leaves it unticked, so one
#           subscription is created and one row per article is stored.
#
# The article count both are measured against comes from the live feed, read
# once here, not from a number written down in this file: how many posts
# mostlylucid.net carries today is the site's business.
#
# Repeatable by construction. Each profile is new, and both are removed on the
# way out including on failure and on interrupt, so a second run starts from
# the same nothing the first one did and the real profile at
# ~/Library/Application Support/mylo is never opened.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

OUT="${1:-/tmp/lr-live-dedupe}"
SITE="https://www.mostlylucid.net"
RSS_FEED="$SITE/rss"
ATOM_FEED="$SITE/atom"

reader_require_app

if ! command -v python3 >/dev/null 2>&1; then
    echo "python3 is needed to count the articles in the live feed." >&2
    exit 1
fi

BEFORE_PROFILE="$(mktemp -d)"
AFTER_PROFILE="$(mktemp -d)"
trap 'rm -rf "$BEFORE_PROFILE" "$AFTER_PROFILE"' EXIT INT TERM

# refreshOnStartup is off so a fetch only happens when this script asks for
# one. Offline downloading is off too: this run is about how many rows are
# stored and how many the list shows, and downloading every article's full
# text would mean a great many more requests to a site being tested for
# something else entirely.
write_settings() {
    cat >"$1/settings.json" <<'JSON'
{
  "defaultRefreshIntervalMinutes": 30,
  "refreshOnStartup": false,
  "pauseWhenOffline": true,
  "maxConcurrentFetches": 4,
  "autoDownloadArticles": false,
  "fetchFullText": false,
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
}

# The schema is created by the app itself rather than by a copy of the DDL
# here that would rot on the next migration. Retried because macOS
# occasionally refuses a second app process a display link when one has only
# just exited; see reader-harness.sh.
bootstrap_profile() {
    local dir="$1"

    cat >"$dir/bootstrap.yaml" <<'YAML'
name: bootstrap
description: Open once so SchemaMigrator creates the database, then exit.
default_delay: 100
actions:
  - type: Wait
    value: "1500"
YAML

    local attempt
    for attempt in 1 2 3; do
        MYLO_DATA_DIR="$dir" "$READER_APP" \
            --ux-test --script "$dir/bootstrap.yaml" --output "$dir/bootstrap" \
            >"$dir/bootstrap.log" 2>&1 || true
        [[ -f "$dir/reader.db" ]] && break
        sleep 3
    done

    if [[ ! -f "$dir/reader.db" ]]; then
        echo "The app did not create $dir/reader.db after 3 attempts. Last log:" >&2
        tail -20 "$dir/bootstrap.log" >&2
        exit 1
    fi

    sleep 2
}

count_query() {
    sqlite3 "$1/reader.db" "$2"
}

# ---------------------------------------------------------------------------
# How many articles the site is actually publishing.
#
# Counted from the live RSS feed, and counted as DISTINCT normalised links,
# which is the same identity the reader groups on. A number written into this
# file would only prove that the reader agrees with this file.
# ---------------------------------------------------------------------------
curl -fsS -A "mylo live harness" -o "$BEFORE_PROFILE/rss.xml" "$RSS_FEED"

# Written to a file and then run, rather than fed to python3 through a
# here-document inside a command substitution. macOS ships bash 3.2, whose
# parser scans the body of such a here-document for quotes it has no business
# reading, and this program's regexes contain an odd number of double quotes -
# which it reports as an unexpected end of file three lines into a comment.
cat >"$BEFORE_PROFILE/count-articles.py" <<'PY'
# Regex rather than an XML parser, for the reason run-live-mostlylucid.sh
# gives: this only needs the item links, and pointing a stdlib XML parser at a
# document fetched over the network resolves external entities by default.
import re, sys
from urllib.parse import urlsplit, parse_qsl, urlencode

text = open(sys.argv[1], encoding='utf-8', errors='replace').read()

links = set()
for item in re.findall(r'<(?:item|entry)\b.*?</(?:item|entry)>', text, re.S | re.I):
    match = (re.search(r'<link[^>]*\bhref\s*=\s*["\']([^"\']+)', item, re.I)
             or re.search(r'<link[^>]*>(.*?)</link>', item, re.S | re.I))
    if not match:
        continue
    raw = re.sub(r'<!\[CDATA\[(.*?)\]\]>', r'\1', match.group(1), flags=re.S).strip()
    if not raw:
        continue

    # The same normalisation LucidReader.Core.Feeds.CanonicalArticleId applies.
    parts = urlsplit(raw)
    if parts.scheme.lower() not in ('http', 'https'):
        continue
    path = parts.path[:-1] if len(parts.path) > 1 and parts.path.endswith('/') else parts.path
    query = urlencode([
        (k, v) for k, v in parse_qsl(parts.query, keep_blank_values=True)
        if not k.lower().startswith('utm_') and k.lower() not in ('fbclid', 'gclid', 'ref')
    ])
    links.add(f"{parts.scheme.lower()}://{parts.hostname.lower()}{path}" + (f"?{query}" if query else ""))

if not links:
    raise SystemExit("the feed came back with no item links")

print(len(links))
PY

REAL_ARTICLES="$(python3 "$BEFORE_PROFILE/count-articles.py" "$BEFORE_PROFILE/rss.xml")"

echo "mostlylucid.net is publishing $REAL_ARTICLES distinct articles in its RSS feed right now."
echo

# ===========================================================================
# BEFORE: the doubles an existing user already has.
# ===========================================================================
echo "===== BEFORE: both feeds already subscribed ====="

write_settings "$BEFORE_PROFILE"
bootstrap_profile "$BEFORE_PROFILE"

sqlite3 "$BEFORE_PROFILE/reader.db" <<SQL
INSERT INTO feeds (feed_url, site_url, title, is_enabled)
VALUES ('$RSS_FEED', '$SITE/', 'mostlylucid (RSS)', 1);
INSERT INTO feeds (feed_url, site_url, title, is_enabled)
VALUES ('$ATOM_FEED', '$SITE/', 'mostlylucid (Atom)', 1);
SQL

BEFORE_STATUS=0
MYLO_DATA_DIR="$BEFORE_PROFILE" "$READER_APP" \
    --ux-test --script "$READER_REPO/ux-scripts/verify-live-dedupe-existing.yaml" \
    --output "$OUT/before" || BEFORE_STATUS=$?

BEFORE_FEEDS="$(count_query "$BEFORE_PROFILE" 'SELECT count(*) FROM feeds;')"
BEFORE_ROWS="$(count_query "$BEFORE_PROFILE" 'SELECT count(*) FROM items;')"
BEFORE_ARTICLES="$(count_query "$BEFORE_PROFILE" \
    "SELECT count(DISTINCT COALESCE(canonical_id, 'row:' || id)) FROM items;")"
BEFORE_UNREAD="$(count_query "$BEFORE_PROFILE" \
    "SELECT count(DISTINCT COALESCE(canonical_id, 'row:' || id)) FROM items WHERE is_read = 0;")"

echo "  subscriptions:          $BEFORE_FEEDS"
echo "  rows stored:            $BEFORE_ROWS"
echo "  articles the list shows: $BEFORE_ARTICLES"
echo "  unread articles:        $BEFORE_UNREAD"
echo

# Same launch-rate limit reader_seed_profile documents.
sleep 3

# ===========================================================================
# AFTER: subscribing through the dialog, taking its defaults.
# ===========================================================================
echo "===== AFTER: subscribed through the add-feed dialog ====="

write_settings "$AFTER_PROFILE"
bootstrap_profile "$AFTER_PROFILE"

AFTER_STATUS=0
MYLO_DATA_DIR="$AFTER_PROFILE" "$READER_APP" \
    --ux-test --script "$READER_REPO/ux-scripts/verify-live-dedupe-subscribe.yaml" \
    --output "$OUT/after" || AFTER_STATUS=$?

AFTER_FEEDS="$(count_query "$AFTER_PROFILE" 'SELECT count(*) FROM feeds;')"
AFTER_ROWS="$(count_query "$AFTER_PROFILE" 'SELECT count(*) FROM items;')"
AFTER_ARTICLES="$(count_query "$AFTER_PROFILE" \
    "SELECT count(DISTINCT COALESCE(canonical_id, 'row:' || id)) FROM items;")"

echo "  subscriptions:          $AFTER_FEEDS"
echo "  rows stored:            $AFTER_ROWS"
echo "  articles the list shows: $AFTER_ARTICLES"
echo

# ===========================================================================
# The assertions.
# ===========================================================================
FAILURES=0
check() {
    if [[ "$2" == "$3" ]]; then
        echo "  ok    $1 ($2)"
    else
        echo "  FAIL  $1: expected $3, got $2"
        FAILURES=$((FAILURES + 1))
    fi
}

check_at_least() {
    if [[ "$2" -ge "$3" ]]; then
        echo "  ok    $1 ($2)"
    else
        echo "  FAIL  $1: expected at least $3, got $2"
        FAILURES=$((FAILURES + 1))
    fi
}

echo "===== assertions ====="
check "the driving script for BEFORE passed" "$BEFORE_STATUS" "0"
check "the driving script for AFTER passed" "$AFTER_STATUS" "0"

check "BEFORE keeps both of the user's subscriptions" "$BEFORE_FEEDS" "2"
check_at_least "BEFORE stored a row for every article twice" "$BEFORE_ROWS" \
    "$((REAL_ARTICLES * 2))"
check "BEFORE shows each article once even so" "$BEFORE_ARTICLES" "$REAL_ARTICLES"
check "BEFORE counts a read article as read under both feeds" "$BEFORE_UNREAD" \
    "$((REAL_ARTICLES - 1))"

check "AFTER created one subscription, not two" "$AFTER_FEEDS" "1"
check "AFTER stored one row per article" "$AFTER_ROWS" "$REAL_ARTICLES"
check "AFTER shows one article per row" "$AFTER_ARTICLES" "$REAL_ARTICLES"

echo
if [[ "$FAILURES" -eq 0 ]]; then
    echo "Result: PASS"
    exit 0
fi

echo "Result: FAIL ($FAILURES)"
exit 1
