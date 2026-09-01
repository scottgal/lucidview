#!/usr/bin/env bash
# Rewrites packaging/homebrew/mylo.rb to point at a published mylo release.
#
# Usage: packaging/homebrew/update-cask.sh [tag]
#        tag  a mylo release tag, e.g. mylo-v0.2.3. Defaults to the newest
#             tag matching mylo-v* that has both macOS assets attached.
#
# It downloads the two macOS zips from the release, checksums them, and writes
# the version and both sha256 values back into the cask. Nothing else in the
# file is touched, so the comments and the Gatekeeper postflight survive.
#
# This exists so the checksums are never typed. A hand-copied sha256 with one
# character wrong fails at install time with a message about a mismatched
# download, which reads to a user like the file was tampered with rather than
# like the cask is stale.
#
# It does not commit, push, or touch a tap. Run it, read the diff, then copy
# the file into the tap yourself. See README.md in this directory.
set -euo pipefail

REPO_SLUG="scottgal/lucidview"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CASK="$HERE/mylo.rb"

TAG="${1:-}"
if [[ -z "$TAG" ]]; then
    TAG="$(gh release list --repo "$REPO_SLUG" --limit 100 \
        --json tagName --jq '[.[] | select(.tagName | startswith("mylo-v"))][0].tagName')"
    [[ -n "$TAG" && "$TAG" != "null" ]] || { echo "no mylo-v* release found" >&2; exit 1; }
fi

VERSION="${TAG#mylo-v}"
echo "tag     $TAG"
echo "version $VERSION"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

gh release download "$TAG" --repo "$REPO_SLUG" --dir "$WORK" \
    --pattern 'mylo-osx-arm64.zip' --pattern 'mylo-osx-x64.zip'

for f in mylo-osx-arm64.zip mylo-osx-x64.zip; do
    [[ -f "$WORK/$f" ]] || { echo "release $TAG has no $f" >&2; exit 1; }
done

ARM="$(shasum -a 256 "$WORK/mylo-osx-arm64.zip" | cut -d' ' -f1)"
INTEL="$(shasum -a 256 "$WORK/mylo-osx-x64.zip" | cut -d' ' -f1)"

echo "arm     $ARM"
echo "intel   $INTEL"

# Anchored on the leading two spaces of the cask's own indentation so this
# cannot rewrite the same words where they appear in a comment above.
perl -0pi -e "
    s/^  version \"[^\"]*\"\$/  version \"$VERSION\"/m;
    s/^  sha256 arm:   \"[0-9a-f]*\",\$/  sha256 arm:   \"$ARM\",/m;
    s/^         intel: \"[0-9a-f]*\"\$/         intel: \"$INTEL\"/m;
" "$CASK"

grep -E '^  version |^  sha256 arm:|^         intel:' "$CASK"
echo
echo "updated $CASK"
echo "check it with: brew style $CASK"
