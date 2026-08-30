#!/usr/bin/env bash
# Runs verify-close-to-status-item.yaml against a scratch profile seeded with
# "keep mylo running in the menu bar" turned on.
#
# Usage: ux-scripts/run-close-to-status-item.sh [output-dir]
#
# Same throwaway profile as every other reader script (see
# ux-scripts/reader-harness.sh), with two settings appended to the seeded
# settings.json: the behaviour under test does not exist without both, and
# neither is the default.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

reader_require_app

# Global, not local, for the same reason reader_run's is: the EXIT trap runs
# in the outer shell after this file has finished.
READER_PROFILE="$(mktemp -d)"
trap 'rm -rf "$READER_PROFILE"' EXIT INT TERM

reader_seed_profile "$READER_PROFILE"

# Rewritten rather than patched with sed: the seeded file is written by
# reader_seed_profile as a whole document, and a settings file assembled by
# two different mechanisms is one nobody can predict the contents of.
python3 - "$READER_PROFILE/settings.json" <<'PY'
import json, sys
path = sys.argv[1]
with open(path) as handle:
    settings = json.load(handle)
settings["showStatusItem"] = True
settings["closeKeepsRunning"] = True
with open(path, "w") as handle:
    json.dump(settings, handle, indent=2)
PY

MYLO_DATA_DIR="$READER_PROFILE" \
    "$READER_APP" --ux-test \
    --script "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/verify-close-to-status-item.yaml" \
    --output "${1:-/tmp/lr-close-to-status-item}"
