#!/usr/bin/env bash
# Runs verify-article-tags.yaml against a freshly seeded scratch profile.
#
# Usage: ux-scripts/run-article-tags.sh [output-dir]
#
# Needs no network: the fixture's feed addresses are under the reserved .test
# TLD and startup refresh is off in the seeded settings. Leaves nothing behind,
# touches no real profile, and can be run twice in a row - reader_run creates
# the profile, seeds it, and traps EXIT, INT and TERM to remove it again. See
# ux-scripts/reader-harness.sh for how, and why.
#
# The fixture seeds no tags, so every tag the script sees is one it created,
# and every one of them is gone by the end of it.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

reader_run verify-article-tags.yaml "${1:-/tmp/lr-article-tags}"
