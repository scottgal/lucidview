#!/usr/bin/env bash
# Screenshots the application menu against a seeded scratch profile.
#
# Usage: ux-scripts/run-menus.sh [output-dir]
#
# MYLO_FORCE_WINDOW_MENU=1 is what makes this possible at all on macOS: the
# menus the app really shows there are a NativeMenu in the system menu bar,
# which the harness cannot see, click or capture. The switch is Debug-only
# (see UseNativeMenu in LucidReader/Views/MainWindow.Menu.cs) and renders the
# same menu description as the in-window Menu that Windows and Linux get.
#
# Needs no network: the fixture's feed addresses are under the reserved .test
# TLD and startup refresh is off in the seeded settings. Leaves nothing
# behind and can be run twice in a row, for the reasons in reader-harness.sh.
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reader-harness.sh"

export MYLO_FORCE_WINDOW_MENU=1

reader_run verify-menus.yaml "${1:-/tmp/lr-menus}"
