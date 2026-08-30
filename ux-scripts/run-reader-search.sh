#!/usr/bin/env bash
# Runs verify-reader-search.yaml against a freshly seeded scratch profile.
#
# Usage: ux-scripts/run-reader-search.sh [output-dir]
#
# Needs no network: the fixture's feed addresses are under the reserved .test
# TLD and startup refresh is off in the seeded settings. Leaves nothing behind,
# touches no real profile, and can be run twice in a row: see
# ux-scripts/reader-harness.sh for how, and why.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

reader_run verify-reader-search.yaml "${1:-/tmp/lr-reader-search}"
