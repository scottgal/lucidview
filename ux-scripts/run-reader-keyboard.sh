#!/usr/bin/env bash
# Runs verify-reader-keyboard.yaml against a freshly seeded scratch profile.
#
# Usage: ux-scripts/run-reader-keyboard.sh [output-dir]
#
# Needs no network, leaves nothing behind and touches no real profile: see
# ux-scripts/reader-harness.sh for how, and why.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

reader_run verify-reader-keyboard.yaml "${1:-/tmp/lr-reader-keyboard}"
