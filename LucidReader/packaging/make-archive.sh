#!/usr/bin/env bash
# Publishes mylo for a Windows or Linux RID and packages the result.
#
# Usage: LucidReader/packaging/make-archive.sh <rid> [output-dir]
#        rid         win-x64, win-arm64, linux-x64 or linux-arm64
#        output-dir  defaults to publish/mylo-<rid>
#
# macOS is not handled here. It needs a .app bundle with an Info.plist, an
# .icns and an ad-hoc signature, which is a different enough job to have its
# own script: LucidReader/macos/make-bundle.sh.
#
# What this produces is a directory named mylo-<rid> containing the executable,
# its native libraries and the bundled manual, archived as a .zip for Windows
# and a .tar.gz for Linux. It is a folder rather than a single file on purpose:
# IncludeNativeLibrariesForSelfExtract is false on every RID and the reasons
# are recorded in LucidReader.csproj. The short version is that the manual has
# to sit beside the executable as loose files anyway, so a folder is what a
# user unzips either way.
#
# Linux also gets a mylo.desktop and the app icon, so a user who wants a menu
# entry has the two files to copy rather than having to write the .desktop
# themselves. Nothing installs them; see install.txt in the archive.
set -euo pipefail

RID="${1:-}"
case "$RID" in
    win-x64|win-arm64|linux-x64|linux-arm64) ;;
    *)
        echo "usage: $0 <win-x64|win-arm64|linux-x64|linux-arm64> [output-dir]" >&2
        exit 1
        ;;
esac

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${2:-$REPO/publish/mylo-$RID}"
PROJECT="$REPO/LucidReader/LucidReader.csproj"

VERSION="$(sed -n 's:.*<MyloVersion>\(.*\)</MyloVersion>.*:\1:p' "$REPO/mylo-version.props" | head -1)"
[[ -n "$VERSION" ]] || { echo "could not read <MyloVersion> from mylo-version.props" >&2; exit 1; }

rm -rf "$OUT"
mkdir -p "$OUT"
# Absolute from here on. CI passes a relative output directory, and both `tar
# -C` and the `cd` before the zip would otherwise resolve the archive path
# against the wrong directory.
OUT="$(cd "$OUT" && pwd)"

PAYLOAD="$OUT/mylo-$RID"
mkdir -p "$PAYLOAD"

dotnet publish "$PROJECT" -c Release -r "$RID" -o "$PAYLOAD" \
    -p:PublishSingleFile=true \
    -p:SelfContained=true \
    -p:PublishReadyToRun=true \
    -p:PublishReadyToRunComposite=false \
    -p:PublishTrimmed=false \
    -p:IncludeNativeLibrariesForSelfExtract=false \
    -p:DebugType=none \
    -p:DebugSymbols=false

# Belt and braces. The Release PropertyGroup and the DropNativeSymbolsFromPublish
# target in LucidReader.csproj should already have kept these out.
find "$PAYLOAD" \( -name '*.pdb' -o -name '*.xml' \) -delete

if [[ "$RID" == win-* ]]; then
    EXE="$PAYLOAD/mylo.exe"
    NATIVE=(e_sqlite3.dll libSkiaSharp.dll libHarfBuzzSharp.dll libonigwrap.dll av_libglesv2.dll)
else
    EXE="$PAYLOAD/mylo"
    NATIVE=(libe_sqlite3.so libSkiaSharp.so libHarfBuzzSharp.so libonigwrap.so)
fi

[[ -f "$EXE" ]] || { echo "missing executable: $EXE" >&2; exit 1; }
for lib in "${NATIVE[@]}"; do
    [[ -f "$PAYLOAD/$lib" ]] || { echo "missing native library: $lib" >&2; exit 1; }
done
[[ -f "$PAYLOAD/manual/user-manual.md" ]] || { echo "missing bundled manual" >&2; exit 1; }

# Run the thing we just built, when this machine can run it. See
# LucidReader/SmokeTest.cs for why a check that executes the packaged binary
# exists at all: 0.2.4 shipped a build that aborted on its first HTTPS request
# with the whole unit suite green, because the fault was in the published
# artifact and nothing ever ran the published artifact.
#
# Only when the RID matches this host, and stated rather than hidden. A
# win-x64 payload cannot be executed on a Linux runner, so a cross-RID build
# genuinely is not verified here and should not print anything suggesting it
# was. release-mylo.yml builds each RID on a matching runner, which is where
# the coverage is real.
HOST_RID=""
case "$(uname -s)/$(uname -m)" in
    Linux/x86_64)  HOST_RID="linux-x64" ;;
    Linux/aarch64) HOST_RID="linux-arm64" ;;
    MINGW*|MSYS*|CYGWIN*)
        case "$(uname -m)" in
            x86_64) HOST_RID="win-x64" ;;
            aarch64|arm64) HOST_RID="win-arm64" ;;
        esac
        ;;
esac

if [[ "$RID" == "$HOST_RID" ]]; then
    echo "running smoke test against the built payload"
    if ! "$EXE" --smoke-test; then
        echo "smoke test failed: this build does not work, refusing to package it" >&2
        exit 1
    fi
else
    echo "skipping smoke test: $RID cannot be executed on this host (${HOST_RID:-unknown})"
fi

if [[ "$RID" == win-* ]]; then
    cat >"$PAYLOAD/install.txt" <<EOF
mylo $VERSION for Windows ($RID)

Unzip this folder somewhere you keep applications, then run mylo.exe.
Keep the folder together: the .dll files beside mylo.exe are the graphics,
text-shaping and database libraries it loads at startup, and manual\\ is the
built-in user manual the Help menu and F1 open. Moving mylo.exe out on its own
gives you an app that starts and then fails at the first database read.

SmartScreen will warn on the first run, because this build is not signed with
a paid Windows code-signing certificate. Choose More info, then Run anyway.

mylo keeps its database and settings in %APPDATA%\\mylo\\. Delete that folder
for a clean slate. Subscriptions import and export as OPML from Settings.
EOF
else
    cp "$REPO/LucidReader/Assets/mylo.png" "$PAYLOAD/mylo.png"
    cat >"$PAYLOAD/mylo.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=mylo
GenericName=Feed Reader
Comment=A native RSS and Atom reader
Exec=mylo
Icon=mylo
Terminal=false
Categories=Network;News;
EOF
    cat >"$PAYLOAD/install.txt" <<EOF
mylo $VERSION for Linux ($RID)

Extract this folder somewhere you keep applications, then run ./mylo.
Keep the folder together: the .so files beside the executable are the
graphics, text-shaping and database libraries it loads at startup, and manual/
is the built-in user manual the Help menu and F1 open. Moving the executable
out on its own gives you an app that starts and then fails at the first
database read.

For a menu entry, edit the Exec line in mylo.desktop to the full path of the
executable, then:

    install -Dm644 mylo.png  ~/.local/share/icons/hicolor/512x512/apps/mylo.png
    install -Dm644 mylo.desktop ~/.local/share/applications/mylo.desktop

Two things behave differently on Linux than on macOS, and both are by design
rather than pending work:

  * The status item in the system tray needs the desktop session to run a
    StatusNotifierItem host. GNOME without an AppIndicator extension has none,
    so the tray icon will not appear there. Everything the status item offers
    is also on the menu bar inside the window.
  * New-article alerts are drawn as a toast inside the mylo window rather than
    handed to the desktop notification service.

mylo keeps its database and settings in \$XDG_CONFIG_HOME/mylo/, which is
~/.config/mylo/ unless you have set that variable. Delete that folder for a
clean slate. Subscriptions import and export as OPML from Settings.
EOF
fi

if [[ "$RID" == win-* ]]; then
    ARCHIVE="$OUT/mylo-$RID.zip"
    (cd "$OUT" && zip -qry "mylo-$RID.zip" "mylo-$RID")
else
    ARCHIVE="$OUT/mylo-$RID.tar.gz"
    # Before the tar, not after: the mode has to be in the archive, or the
    # first thing a user does after extracting is work out why ./mylo says
    # permission denied.
    chmod +x "$PAYLOAD/mylo"
    tar -czf "$ARCHIVE" -C "$OUT" "mylo-$RID"
fi

echo "payload: $PAYLOAD ($(du -sh "$PAYLOAD" | cut -f1))"
echo "archive: $ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"
