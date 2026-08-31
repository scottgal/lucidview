#!/usr/bin/env bash
# Proves that opening an article fetches it when the stored copy is only the
# feed's summary. Seeds one item down to "never downloaded" so the on-open
# fetch is the only thing that could produce an extracted body.
#
# Needs network. Only mostlylucid.net is contacted, which is the maintainer's
# own site. Throwaway profile, removed on the way out including on failure.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"
reader_require_app

READER_PROFILE="$(mktemp -d)"
trap 'rm -rf "$READER_PROFILE"' EXIT INT TERM
reader_seed_profile "$READER_PROFILE"

sqlite3 "$READER_PROFILE/reader.db" <<'SQL'
UPDATE items
SET offline_state = 0,
    content_source = 0,
    content_markdown = NULL,
    summary = 'Only the feed summary is stored for this one.',
    link = 'https://www.mostlylucid.net/blog/signal-shingle-architecture'
WHERE id = (SELECT MIN(id) FROM items);
SQL

MYLO_DATA_DIR="$READER_PROFILE" "$READER_APP" ${MYLO_UX_MODE:---ux-headless} \
    --ux-test --script "$READER_REPO/ux-scripts/verify-open-fetches.yaml" \
    --output "${1:-/tmp/lr-open-fetches}"
