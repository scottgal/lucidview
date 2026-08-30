#!/usr/bin/env bash
# Runs verify-refresh-health.yaml, which needs an auto-paused feed to exist.
#
# Auto-pause takes 20 consecutive fetch failures, so it cannot be driven from
# the UI inside a test. This seeds one row, runs the script, and removes the
# row again, so the check is repeatable instead of depending on database state
# a previous session left behind. Cleanup runs on failure and on interrupt too,
# which is why the seed uses a feed_url no real subscription would collide with.
# -e matters here: without it a failing seed INSERT (locked database, a
# changed schema) still let the harness launch, and the real problem showed up
# as a confusing Expect mismatch instead of the error it is.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$REPO/LucidReader/bin/Debug/net10.0/osx-arm64/mylo"
DB="$HOME/Library/Application Support/mylo/reader.db"
OUT="${1:-/tmp/lr-refresh-health}"
SEED_URL='https://paused.example/feed.xml'

if [[ ! -x "$APP" ]]; then
    echo "Build first: dotnet build LucidReader/LucidReader.csproj" >&2
    exit 1
fi
if [[ ! -f "$DB" ]]; then
    echo "No database at $DB. Launch the app once to create it." >&2
    exit 1
fi

cleanup() {
    sqlite3 "$DB" "DELETE FROM feeds WHERE feed_url = '$SEED_URL';"
}
trap cleanup EXIT INT TERM

# Remove any row a previous interrupted run left behind, so the seed cannot
# hit the unique index on feed_url and leave the script asserting against two
# paused feeds instead of one.
cleanup

sqlite3 "$DB" "INSERT INTO feeds (feed_url, title, is_enabled, consecutive_failures, last_error, auto_paused_utc)
               VALUES ('$SEED_URL', 'Paused Feed', 0, 20, 'Name or service not known', '2026-08-29T12:00:00.0000000+00:00');"

"$APP" --ux-test --script "$REPO/ux-scripts/verify-refresh-health.yaml" --output "$OUT"
