#!/usr/bin/env bash
# Runs verify-mostlylucid.yaml, which pastes the bare domain mostlylucid.net
# into Add Feed and subscribes to whatever discovery comes back with.
#
# The "Added" assertion only holds against a database that does not already
# have those feeds, so the rows are removed both before and after. That is
# also what makes the script runnable twice in a row, and what stops a test
# run leaving subscriptions behind in a working database.
#
# Needs network: it fetches https://mostlylucid.net and its feeds.
#
# -e matters here: without it a failing cleanup DELETE (locked database, a
# changed schema) still lets the harness launch against rows that were meant
# to be gone, and the real problem shows up as a confusing Expect mismatch
# instead of the error it is.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$REPO/LucidReader/bin/Debug/net10.0/osx-arm64/mylo"
DB="$HOME/Library/Application Support/mylo/reader.db"
OUT="${1:-/tmp/lr-mostlylucid}"

if [[ ! -x "$APP" ]]; then
    echo "Build first: dotnet build LucidReader/LucidReader.csproj" >&2
    exit 1
fi
if [[ ! -f "$DB" ]]; then
    echo "No database at $DB. Launch the app once to create it." >&2
    exit 1
fi

# Deleting a feed cascades to its items and their tombstones, so this leaves
# no orphans behind. Scoped to mostlylucid.net only, which is all this script
# subscribes to.
#
# PRAGMA foreign_keys=ON is load-bearing and easy to leave out: SQLite
# defaults it OFF, and the sqlite3 CLI is not the app, so without it the
# ON DELETE CASCADE on items.feed_id never fires. A sibling script ran
# without it for a while and left 48 orphaned item rows, and their FTS
# entries, in the development database - rows no feed owned, no query could
# reach, and nothing would ever clean up.
cleanup() {
    sqlite3 "$DB" "PRAGMA foreign_keys=ON;
                   DELETE FROM feeds WHERE feed_url LIKE '%mostlylucid.net%';"
}
trap cleanup EXIT INT TERM

cleanup

"$APP" --ux-test --script "$REPO/ux-scripts/verify-mostlylucid.yaml" --output "$OUT"
