#!/usr/bin/env bash
# Shared plumbing for the mylo driving scripts. Sourced, not run.
#
# Both reader-smoke.yaml and reader-settings.yaml assert exact numbers: five
# articles under All items, three under Harness Alpha, one starred, two search
# hits, a specific inherited-setting label. None of that can be asserted against
# whatever database the person running the script happens to have, and a script
# that mutates that database to make its own assertions true is worse still:
# that is exactly how verify-add-feed-writes.yaml ended up leaving two live
# subscriptions behind and skewing every later script's row counts.
#
# So each run gets its own profile directory, seeded from reader-fixture.sql,
# and deletes it again on the way out including on failure and on interrupt.
# MYLO_DATA_DIR (Debug builds only, see LucidReader/App.axaml.cs) is what
# points the app at it. The consequence worth having: these scripts are
# repeatable by construction rather than by remembering to clean up, they can
# be run twice in a row, they do not care what state they start from, and they
# leave the real ~/Library/Application Support/mylo untouched.

READER_REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
READER_APP="$READER_REPO/LucidReader/bin/Debug/net10.0/osx-arm64/mylo"
READER_FIXTURE="$READER_REPO/ux-scripts/reader-fixture.sql"

reader_require_app() {
    if [[ ! -x "$READER_APP" ]]; then
        echo "Build first: dotnet build LucidReader/LucidReader.csproj" >&2
        exit 1
    fi
    if ! command -v sqlite3 >/dev/null 2>&1; then
        echo "sqlite3 is needed to seed the fixture database." >&2
        exit 1
    fi
}

# Fills an already-created, already-trapped directory with a settings file and
# a seeded database.
#
# refreshOnStartup is off in the seeded settings, which does three things worth
# stating: no feed fetch is attempted, so the run needs no network and cannot
# be made flaky by one; RefreshScheduler never starts, so DescribeHealth
# returns an empty string and the status bar is left saying whatever the
# script's own actions put there; and the .test feed addresses in the fixture
# are never resolved. markReadDwellMilliseconds is dropped to 300 so the
# mark-as-read dwell is short enough for a script to wait out deliberately.
reader_seed_profile() {
    local dir="$1"

    cat >"$dir/settings.json" <<'JSON'
{
  "defaultRefreshIntervalMinutes": 30,
  "refreshOnStartup": false,
  "pauseWhenOffline": true,
  "maxConcurrentFetches": 4,
  "autoDownloadArticles": true,
  "fetchFullText": true,
  "cacheImages": false,
  "maxImageBytes": 5242880,
  "maxConcurrentDownloads": 2,
  "keepReadArticlesDays": 30,
  "keepUnreadForever": true,
  "keepUnreadDays": 180,
  "maxArticlesPerFeed": 500,
  "neverDeleteStarred": true,
  "theme": "Auto",
  "fontSize": 15,
  "columnWidth": 760,
  "markReadDwellMilliseconds": 300,
  "openLinksExternally": true,
  "enableOnlineFeedSearch": false
}
JSON

    # The schema is created by the app itself (SchemaMigrator), so this launches
    # it once against the empty directory rather than keeping a copy of the DDL
    # here that would silently rot the next time a migration lands.
    cat >"$dir/bootstrap.yaml" <<'YAML'
name: bootstrap
description: Open once so SchemaMigrator creates the database, then exit.
default_delay: 100
actions:
  - type: Wait
    value: "1500"
YAML

    # Retried, because macOS occasionally refuses a second app process a display
    # link when one has only just exited ("Avalonia.Native was not able to start
    # the RenderTimer", error -6661). That is a launch-rate limit, not a fault in
    # anything being tested, and it shows up as a crashed bootstrap with no
    # database rather than as a failed assertion.
    local attempt
    for attempt in 1 2 3; do
        MYLO_DATA_DIR="$dir" "$READER_APP" \
            --ux-test --script "$dir/bootstrap.yaml" --output "$dir/bootstrap" \
            >"$dir/bootstrap.log" 2>&1 || true
        [[ -f "$dir/reader.db" ]] && break
        sleep 3
    done

    if [[ ! -f "$dir/reader.db" ]]; then
        echo "The app did not create $dir/reader.db after 3 attempts. Last log:" >&2
        tail -20 "$dir/bootstrap.log" >&2
        exit 1
    fi

    sqlite3 "$dir/reader.db" <"$READER_FIXTURE"

    # Same launch-rate limit as above: give the bootstrap process time to let go
    # of the display link before the run below asks for one.
    sleep 2
}

# Runs a script against a scratch profile and reports pass or fail.
# Usage: reader_run <script.yaml> <output-dir>
reader_run() {
    local script="$1" out="$2"
    reader_require_app

    # Global, not local: the EXIT trap below runs after this function has
    # returned, in the outer shell, where a local would already be gone (and
    # under set -u that is an "unbound variable" error rather than a silent
    # failure to clean up).
    READER_PROFILE="$(mktemp -d)"

    # Set before anything is put in the directory, so a failure during seeding
    # cleans up too. The trap is what makes this repeatable rather than merely
    # tidy: an assertion failure, a crash or a Ctrl-C all still remove the
    # profile, so the next run starts from the same nothing this one did.
    trap 'rm -rf "$READER_PROFILE"' EXIT INT TERM

    reader_seed_profile "$READER_PROFILE"

    MYLO_DATA_DIR="$READER_PROFILE" "$READER_APP" \
        --ux-test --script "$READER_REPO/ux-scripts/$script" --output "$out"
}
