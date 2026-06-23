#!/usr/bin/env bash
#
# lucidVIEW one-liner installer for macOS.
#
# Downloads the latest release matching the host architecture, removes the
# Gatekeeper quarantine attribute (lucidVIEW is ad-hoc codesigned, not
# notarized), drops the app into /Applications, and opens it.
#
# Usage (curl-pipe):
#
#   curl -fsSL https://raw.githubusercontent.com/scottgal/lucidview/main/Scripts/install-macos.sh | bash
#
# Or after a zip download, from inside the unzipped folder:
#
#   bash install-macos.sh
#
# Inspect before running. The script makes three changes:
#   1. Downloads lucidVIEW-osx-{arm64,x64}.zip into /tmp
#   2. xattr -dr com.apple.quarantine on the unzipped .app
#   3. mv lucidVIEW.app /Applications/

set -euo pipefail

REPO="scottgal/lucidview"
INSTALL_DIR="${LUCIDVIEW_INSTALL_DIR:-/Applications}"

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "lucidVIEW installer: this script only runs on macOS." >&2
    exit 1
fi

case "$(uname -m)" in
    arm64)  ARCH="arm64" ;;
    x86_64) ARCH="x64" ;;
    *)
        echo "lucidVIEW installer: unsupported architecture $(uname -m)." >&2
        exit 1
        ;;
esac

ASSET="lucidVIEW-osx-${ARCH}.zip"
URL="https://github.com/${REPO}/releases/latest/download/${ASSET}"

TMP_DIR="$(mktemp -d -t lucidview-install)"
trap 'rm -rf "$TMP_DIR"' EXIT

echo "▸ Downloading ${ASSET}…"
curl -fL --progress-bar -o "${TMP_DIR}/${ASSET}" "${URL}"

echo "▸ Unpacking…"
unzip -q -o "${TMP_DIR}/${ASSET}" -d "${TMP_DIR}"

APP_PATH="$(find "${TMP_DIR}" -maxdepth 2 -name 'lucidVIEW.app' -print -quit)"
if [[ -z "${APP_PATH}" ]]; then
    echo "lucidVIEW installer: could not find lucidVIEW.app in the unpacked zip." >&2
    exit 1
fi

echo "▸ Removing quarantine attribute (Gatekeeper would otherwise block first launch)…"
xattr -dr com.apple.quarantine "${APP_PATH}" 2>/dev/null || true

DEST="${INSTALL_DIR}/lucidVIEW.app"
if [[ -d "${DEST}" ]]; then
    echo "▸ Replacing existing ${DEST}…"
    rm -rf "${DEST}"
fi

echo "▸ Installing to ${DEST}…"
if ! mv "${APP_PATH}" "${INSTALL_DIR}/" 2>/dev/null; then
    # /Applications usually needs admin; retry under sudo if interactive.
    if [[ -t 0 ]]; then
        sudo mv "${APP_PATH}" "${INSTALL_DIR}/"
    else
        echo "lucidVIEW installer: ${INSTALL_DIR} is not writable. Re-run with sudo, or set LUCIDVIEW_INSTALL_DIR=\$HOME/Applications and retry." >&2
        exit 1
    fi
fi

echo "▸ Launching lucidVIEW…"
open "${DEST}"

echo
echo "✔ lucidVIEW installed at ${DEST}."
echo "  Pin to the Dock from the running window if you want it there permanently."
