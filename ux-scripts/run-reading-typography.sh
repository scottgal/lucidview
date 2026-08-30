#!/usr/bin/env bash
# Runs verify-reading-typography.yaml against a freshly seeded scratch profile.
#
# Usage: ux-scripts/run-reading-typography.sh [output-dir]
#
# Needs no network. The script deliberately leaves the settings it changed
# changed, which only works because the profile it changed them in is thrown
# away on the way out, including on failure and on interrupt: see
# ux-scripts/reader-harness.sh.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

reader_run verify-reading-typography.yaml "${1:-/tmp/lr-reading-typography}"
