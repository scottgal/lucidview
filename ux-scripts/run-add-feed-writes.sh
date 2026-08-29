#!/usr/bin/env bash
# Runs verify-add-feed-writes.yaml, which subscribes to xkcd's two feeds for
# real and then asserts the status bar said "Added".
#
# That assertion only holds the first time: on a second run the feeds are
# already subscribed, so the app correctly reports them as duplicates and the
# script fails. Removing the rows before and after makes it repeatable, and
# stops a test run leaving subscriptions behind in a working database.
#
# Needs network: it fetches https://xkcd.com and follows its feed links.
# -e matters here: without it a failing cleanup DELETE (locked database, a
# changed schema) still let the harness launch against rows that were meant to
# be gone, and the real problem showed up as a confusing Expect mismatch
# instead of the error it is.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$REPO/LucidReader/bin/Debug/net10.0/osx-arm64/lucidREADER"
DB="$HOME/Library/Application Support/lucidREADER/reader.db"
OUT="${1:-/tmp/lr-add-feed-writes}"

if [[ ! -x "$APP" ]]; then
    echo "Build first: dotnet build LucidReader/LucidReader.csproj" >&2
    exit 1
fi
if [[ ! -f "$DB" ]]; then
    echo "No database at $DB. Launch the app once to create it." >&2
    exit 1
fi

# Deleting a feed cascades to its items and their tombstones, so this leaves no
# orphans behind. Scoped to xkcd.com only, which is what the script subscribes to.
#
# PRAGMA foreign_keys=ON is load-bearing and easy to leave out: SQLite defaults
# it OFF, and the sqlite3 CLI is not the app, so without it the ON DELETE
# CASCADE on items.feed_id never fires. This script ran without it for a while
# and left 48 orphaned item rows, and their FTS entries, in the development
# database - rows that no feed owned, that no query could reach, and that
# nothing would ever clean up.
cleanup() {
    sqlite3 "$DB" "PRAGMA foreign_keys=ON;
                   DELETE FROM feeds WHERE feed_url LIKE 'https://xkcd.com/%';"
}
trap cleanup EXIT INT TERM

cleanup

"$APP" --ux-test --script "$REPO/ux-scripts/verify-add-feed-writes.yaml" --output "$OUT"
