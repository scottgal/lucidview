#!/usr/bin/env bash
# Publishes mylo and assembles a macOS .app bundle around the result.
#
# Usage: LucidReader/macos/make-bundle.sh [rid] [output-dir]
#        rid         defaults to osx-arm64, the other sensible value is osx-x64
#        output-dir  defaults to publish/mylo-<rid>, the .app lands inside it
#
# Why a script and not ten lines inlined in release.yml: the bundle is the only
# shape of mylo that actually works on macOS, so it has to be buildable and
# testable locally rather than only inside CI.
#
# IncludeNativeLibrariesForSelfExtract is false in LucidReader.csproj, on
# purpose and for a good reason recorded there. The consequence is that a
# publish is not one file: it is the mylo executable plus five dylibs
# (libe_sqlite3, libSkiaSharp, libAvaloniaNative, libHarfBuzzSharp,
# libonigwrap). Copy only the executable, as any naive packaging step would,
# and you ship something that opens a window and then dies the moment it
# touches the database. So this copies the whole publish directory into
# Contents/MacOS and then checks the dylibs actually arrived.
set -euo pipefail

RID="${1:-osx-arm64}"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${2:-$REPO/publish/mylo-$RID}"
PROJECT="$REPO/LucidReader/LucidReader.csproj"

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT" | head -1)"
[[ -n "$VERSION" ]] || { echo "could not read <Version> from $PROJECT" >&2; exit 1; }

STAGE="$OUT/publish"
rm -rf "$OUT"
mkdir -p "$OUT"

dotnet publish "$PROJECT" -c Release -r "$RID" -o "$STAGE" \
    -p:PublishSingleFile=true \
    -p:SelfContained=true \
    -p:PublishReadyToRun=true \
    -p:PublishReadyToRunComposite=false \
    -p:PublishTrimmed=false \
    -p:IncludeNativeLibrariesForSelfExtract=false \
    -p:DebugType=none \
    -p:DebugSymbols=false

APP="$OUT/mylo.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp -R "$STAGE"/. "$APP/Contents/MacOS/"
# Belt and braces. AllowedReferenceRelatedFileExtensions in the Release
# PropertyGroup should already have kept these out of the publish directory.
find "$APP/Contents/MacOS" \( -name '*.pdb' -o -name '*.xml' \) -delete
chmod +x "$APP/Contents/MacOS/mylo"

cp "$REPO/LucidReader/icon/mylo.icns" "$APP/Contents/Resources/mylo.icns"

# The plist in this folder carries $(AppVersion) and $(AppBuildVersion)
# placeholders so the version lives in one place, the csproj.
sed -e "s/\$(AppVersion)/$VERSION/" -e "s/\$(AppBuildVersion)/$VERSION/" \
    "$REPO/LucidReader/macos/Info.plist" >"$APP/Contents/Info.plist"

for lib in libe_sqlite3 libSkiaSharp libAvaloniaNative libHarfBuzzSharp libonigwrap; do
    if [[ ! -f "$APP/Contents/MacOS/$lib.dylib" ]]; then
        echo "missing native library in bundle: $lib.dylib" >&2
        exit 1
    fi
done

rm -rf "$STAGE"

# Ad-hoc signature so Gatekeeper does not quarantine a download outright.
# Real distribution needs a Developer ID and notarisation, same as lucidVIEW.
codesign --force --deep --sign - "$APP"
codesign --verify --deep "$APP"

echo "built $APP ($(du -sh "$APP" | cut -f1))"
