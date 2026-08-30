#!/usr/bin/env bash
# Runs verify-alerts.yaml against a freshly seeded scratch profile.
#
# Usage: ux-scripts/run-alerts.sh [output-dir]
#
# The script toggles two of the alert settings for real and puts them back
# inside the same run, so each round trip is proved in both directions. It
# does that in a throwaway profile, deleted from a trap on the way out
# including on failure and on interrupt. See ux-scripts/reader-harness.sh.
#
# The four alert settings are deliberately not written into the seeded
# settings.json: their defaults are what the script asserts first, and a
# seeded copy of those values would only prove the seed, not the defaults.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

reader_run verify-alerts.yaml "${1:-/tmp/lr-alerts}"
