#!/usr/bin/env bash
# Runs reader-settings.yaml against a freshly seeded scratch profile.
#
# Usage: ux-scripts/run-reader-settings.sh [output-dir]
#
# The script edits settings and per-feed overrides for real, which is the point
# of it. It does that in a throwaway profile rather than a real one, and puts
# every change back within the run so each round trip is proved in both
# directions. See ux-scripts/reader-harness.sh.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

reader_run reader-settings.yaml "${1:-/tmp/lr-reader-settings}"
