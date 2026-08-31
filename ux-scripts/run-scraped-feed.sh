#!/usr/bin/env bash
# Runs verify-scraped-feed.yaml: the whole article-list detection flow, end to
# end, against a page served from this machine.
#
# Usage: ux-scripts/run-scraped-feed.sh [output-dir]
#
# What it drives: paste an address that publishes no feed, watch mylo work out
# that the page is a list of articles and offer it, see the count and the
# sample titles, approve it (deliberately not pre-ticked - it is a guess), add
# it, see the feed and its articles appear in the sidebar and the item list,
# and refresh it.
#
# Needs no network and touches no third party. The page is a real one -
# LucidReader.Core.Tests/Fixtures/Html/mostlylucid-blog-index.html, saved from
# www.mostlylucid.net - with its two feed declarations removed, which is
# exactly the site this feature exists for: the same markup, published by
# somebody who never set up a feed. It is served over HTTP on loopback by
# python3's http.server, on a port picked at run time so two runs cannot
# collide.
#
# FeedUrlPolicy refuses loopback addresses, and must: that refusal is what
# stops an OPML file or a fetched page pointing mylo at 169.254.169.254 or at
# something on the local network. MYLO_ALLOW_LOOPBACK_FEEDS=1 opens loopback
# and only loopback, only in a Debug build (the code is inside #if DEBUG), and
# only for the life of this process. Link-local and the RFC1918 ranges stay
# refused with it set - see FeedUrlPolicyLoopbackEscapeTests, which pins that
# down.
#
# Repeatable by construction, and proven so by being run twice: the profile is
# a throwaway MYLO_DATA_DIR directory and the server is a background process,
# and an EXIT/INT/TERM trap removes both on success, on failure and on
# interrupt. The real profile at ~/Library/Application Support/mylo is never
# opened.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

OUT="${1:-/tmp/lr-scraped-feed}"

reader_require_app

if ! command -v python3 >/dev/null 2>&1; then
    echo "python3 is needed to serve the fixture page." >&2
    exit 1
fi

PROFILE="$(mktemp -d)"
SITE="$(mktemp -d)"
SERVER_PID=""

# Set before anything is created inside either directory, so a failure during
# setup still cleans up. Killing the server is the part that matters most: a
# leaked http.server holds its port and the next run picks a different one, but
# the process would outlive every run until the machine is restarted.
cleanup() {
    [[ -n "$SERVER_PID" ]] && kill "$SERVER_PID" 2>/dev/null || true
    rm -rf "$PROFILE" "$SITE"
}
trap cleanup EXIT INT TERM

# The fixture, with its feed declarations taken out. Built here rather than
# checked in as a second copy so it cannot drift from the page the unit tests
# measure the detector against.
python3 - "$READER_REPO/LucidReader.Core.Tests/Fixtures/Html/mostlylucid-blog-index.html" \
          "$SITE/index.html" <<'PY'
import re, sys

source, target = sys.argv[1], sys.argv[2]
html = open(source, encoding="utf-8").read()

# Stage 1 of autodiscovery reads these; stage 2 reads the anchors below.
html = re.sub(r"""<link[^>]*rel=["']?alternate[^>]*>""", "", html, flags=re.I)
html = re.sub(r"""<a[^>]*href=["']/(rss|atom|feed)(\.xml)?["'][^>]*>""", "<a>", html, flags=re.I)

if re.search("alternate", html, re.I):
    raise SystemExit("A feed declaration survived; the fixture's markup has changed.")

open(target, "w", encoding="utf-8").write(html)
PY

# A free port, asked of the OS rather than guessed, so two runs of this script
# at once cannot collide on a hard-coded number.
PORT="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')"

python3 -m http.server "$PORT" --bind 127.0.0.1 --directory "$SITE" >/dev/null 2>&1 &
SERVER_PID=$!

# Wait for the port to answer rather than sleeping a guessed amount: a fixed
# sleep is either too short on a loaded machine or wasted on an idle one.
for _ in $(seq 1 50); do
    if curl -sf "http://127.0.0.1:$PORT/" -o /dev/null; then break; fi
    sleep 0.1
done
if ! curl -sf "http://127.0.0.1:$PORT/" -o /dev/null; then
    echo "The fixture server did not come up on port $PORT." >&2
    exit 1
fi

reader_seed_profile "$PROFILE"

# Auto-download off. The scraped articles' links point at paths this little
# server does not serve, so leaving it on would queue twenty downloads that all
# 404 - no effect on any assertion, but a run full of recorded failures that
# say nothing about what is being tested.
python3 - "$PROFILE/settings.json" <<'PY'
import json, sys
path = sys.argv[1]
settings = json.load(open(path))
settings["autoDownloadArticles"] = False
settings["fetchFullText"] = False
json.dump(settings, open(path, "w"), indent=2)
PY

# The address is written into the script's own copy of the YAML, since the port
# is not known until now.
SCRIPT="$PROFILE/verify-scraped-feed.yaml"
sed "s|__SCRAPE_URL__|http://127.0.0.1:$PORT/|g" \
    "$READER_REPO/ux-scripts/verify-scraped-feed.yaml" >"$SCRIPT"

MYLO_DATA_DIR="$PROFILE" MYLO_ALLOW_LOOPBACK_FEEDS=1 \
    "$READER_APP" "${MYLO_UX_MODE:---ux-headless}" \
    --ux-test --script "$SCRIPT" --output "$OUT"
