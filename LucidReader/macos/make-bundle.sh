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

# Read from mylo-version.props, the file that actually holds the number, and
# not from the csproj. The csproj line is <Version>$(MyloVersion)</Version>:
# MSBuild expands that, sed does not, so reading it here captured the literal
# string "$(MyloVersion)" and substituted THAT into the plist. It was non-empty
# so the check below passed, and 0.2.4 shipped with
# CFBundleShortVersionString set to "$(MyloVersion)" - which is what the crash
# reports for it say to this day. make-archive.sh already read the props file;
# this now matches it.
VERSION="$(sed -n 's:.*<MyloVersion>\(.*\)</MyloVersion>.*:\1:p' "$REPO/mylo-version.props" | head -1)"
[[ -n "$VERSION" ]] || { echo "could not read <MyloVersion> from $REPO/mylo-version.props" >&2; exit 1; }
[[ "$VERSION" != *'$('* ]] || { echo "MyloVersion is unexpanded: $VERSION" >&2; exit 1; }

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

# The check that would have caught the bug above. Substituting the wrong thing
# produces a perfectly well-formed plist, so the only way to notice is to look
# for a placeholder that survived.
if grep -q '\$(' "$APP/Contents/Info.plist"; then
    echo "unsubstituted placeholder left in Info.plist:" >&2
    grep -n '\$(' "$APP/Contents/Info.plist" >&2
    exit 1
fi

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

# Run the thing we just built. This is the step whose absence let 0.2.4 ship a
# binary that aborted on its first HTTPS request while 1514 unit tests stayed
# green: the fault lived only in the published artifact, and nothing ever ran
# the published artifact. See LucidReader/SmokeTest.cs.
#
# Last, after signing, because signing is itself a way to break an app: a bad
# signature or a hardened-runtime flag without the matching entitlement
# produces a bundle that dies on launch, and checking before this point would
# not see it.
#
# Only when this host can actually execute the result. release-mylo.yml builds
# both osx-arm64 and osx-x64 on macos-latest, which is arm64, so the x64 bundle
# runs only if Rosetta happens to be installed. Attempting it regardless would
# make the release depend on a runner detail that has nothing to do with mylo.
HOST_ARCH="$(uname -m)"
case "$RID/$HOST_ARCH" in
    osx-arm64/arm64|osx-x64/x86_64) RUNNABLE=yes ;;
    osx-x64/arm64) arch -x86_64 /usr/bin/true 2>/dev/null && RUNNABLE=yes || RUNNABLE=no ;;
    *) RUNNABLE=no ;;
esac

if [[ "$RUNNABLE" == yes ]]; then
    echo "running smoke test against the built bundle"
    if ! "$APP/Contents/MacOS/mylo" --smoke-test; then
        echo "smoke test failed: this bundle does not work, refusing to publish it" >&2
        exit 1
    fi
else
    echo "skipping smoke test: $RID cannot be executed on this host ($HOST_ARCH)"
fi

echo "built $APP ($(du -sh "$APP" | cut -f1))"
