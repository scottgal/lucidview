#!/usr/bin/env bash
# Runs verify-feed-update-chunk.yaml against a freshly seeded scratch profile.
#
# Usage: ux-scripts/run-feed-update-chunk.sh [output-dir] [theme]
#
# theme is Auto (the default), Light or Dark, and only exists so the same run
# can be captured in both appearances for a legibility check. Every assertion
# in the script is identical in all three.
#
# Needs no network: the fixture's feed addresses are under the reserved .test
# TLD and startup refresh is off in the seeded settings. Leaves nothing behind,
# touches no real profile, and can be run twice in a row - the profile is a
# throwaway MYLO_DATA_DIR directory removed by an EXIT/INT/TERM trap. See
# ux-scripts/reader-harness.sh for how, and why.
#
# The shared fixture is not enough on its own here: it seeds no fetch
# timestamps at all, so every feed in it reads "Not updated yet". The extra SQL
# below is what gives the script a healthy feed, an auto-paused one and a
# never-fetched one to walk through.
#
# The half-minute offsets are deliberate. A feed seeded exactly four minutes
# ago reads "4 min ago" only until the run's own startup time pushes it to
# five; seeding it four and a half minutes ago leaves a thirty second window in
# which the wording cannot change, which is far longer than the run needs.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

OUT="${1:-/tmp/lr-feed-update-chunk}"
THEME="${2:-Auto}"

reader_require_app

PROFILE="$(mktemp -d)"
trap 'rm -rf "$PROFILE"' EXIT INT TERM

reader_seed_profile "$PROFILE"

if [[ "$THEME" != "Auto" ]]; then
    sed -i '' "s/\"theme\": \"Auto\"/\"theme\": \"$THEME\"/" "$PROFILE/settings.json"
fi

sqlite3 "$PROFILE/reader.db" <<'SQL'
-- Harness Alpha: healthy, fetched four and a half minutes ago, due again in
-- twenty-six and a half.
UPDATE feeds
   SET last_fetched_utc = strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-4 minutes', '-30 seconds'),
       last_success_utc = strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-4 minutes', '-30 seconds'),
       next_due_utc     = strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '+26 minutes', '+30 seconds')
 WHERE id = 1;

-- Harness Beta: auto-paused after repeated failures, the state the Core layer
-- puts a feed in after BackoffPolicy.AutoPauseThreshold consecutive failures.
UPDATE feeds
   SET is_enabled           = 0,
       consecutive_failures = 20,
       last_error           = 'Name or service not known',
       auto_paused_utc      = strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-1 hours'),
       last_fetched_utc     = strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-1 hours')
 WHERE id = 2;

-- Harness Gamma: added and never fetched. No items, which is the point: this
-- is what a subscription looks like between being added and its first refresh.
INSERT INTO feeds (id, folder_id, feed_url, site_url, title, is_enabled)
VALUES (3, NULL, 'https://harness-gamma.test/feed.xml', 'https://harness-gamma.test/', 'Harness Gamma', 1);
SQL

MYLO_DATA_DIR="$PROFILE" "$READER_APP" \
    --ux-test --script "$READER_REPO/ux-scripts/verify-feed-update-chunk.yaml" --output "$OUT"
