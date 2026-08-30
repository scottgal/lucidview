#!/usr/bin/env bash
# Runs verify-hn-discovery.yaml, the real-world check on the IPv6 connect hang.
#
# Usage: ux-scripts/run-hn-discovery.sh [output-dir]
#
# Runs against a throwaway MYLO_DATA_DIR profile (see reader-harness.sh), which
# is removed on success, on failure and on interrupt, so this can be run twice
# in a row and leaves the real profile at ~/Library/Application Support/mylo
# untouched. Cancel, not Add, so nothing is subscribed even within the scratch
# profile.
#
# Needs network, and reaches one third-party host: news.ycombinator.com. That
# is deliberate and unavoidable - the bug was a property of that host's DNS,
# not of anything mylo could fake - but it is a handful of requests per run and
# nothing here loops.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

reader_run verify-hn-discovery.yaml "${1:-/tmp/lr-hn-discovery}"
