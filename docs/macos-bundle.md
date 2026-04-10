# macOS .app bundle

lucidVIEW publishes as a real `.app` bundle on macOS so double-clicking launches
the GUI directly (no Terminal window) and the Dock shows the lucidVIEW icon.

## Build

```bash
pwsh ./publish.ps1 -Platform osx          # both osx-x64 and osx-arm64
pwsh ./publish.ps1 -Platform osx-arm64    # Apple Silicon only
pwsh ./publish.ps1 -Platform osx-x64      # Intel only
```

The script does:

1. `dotnet publish` into `publish/osx-arm64/_raw/` (single-file, self-contained, ReadyToRun)
2. Assembles `publish/osx-arm64/lucidVIEW.app` with this layout:
   ```
   lucidVIEW.app/
       Contents/
           Info.plist                  ← MarkdownViewer/macos/Info.plist
           MacOS/
               lucidVIEW               ← the published binary
               (native libs)
           Resources/
               lucidVIEW.icns          ← MarkdownViewer/Assets/lucidVIEW.icns
   ```
3. Ad-hoc codesigns the bundle (`codesign --force --deep --sign -`) so Gatekeeper
   doesn't quarantine the local build. Distribution builds need a real Developer ID.

## Smoke test

```bash
open publish/osx-arm64/lucidVIEW.app
```

Expected:
- No Terminal window opens
- lucidVIEW window appears with full chrome
- Dock shows the lucidVIEW icon (italic gray "l" + bold white "V" on dark background)
- Right-click any `.md` file in Finder → "Open With" menu lists lucidVIEW

## Regenerating the icon

The icon was generated from `MarkdownViewer/Assets/lucidview-icon.svg` using
`sips` + `iconutil` (macOS only). To regenerate:

```bash
SRC=MarkdownViewer/Assets/lucidview-icon.svg
TMP=$(mktemp -d)
qlmanage -t -s 1024 -o "$TMP" "$SRC"
ICONSET="$TMP/lucidVIEW.iconset"
mkdir -p "$ICONSET"
PNG="$TMP/$(basename $SRC).png"
sips -z 16   16   "$PNG" --out "$ICONSET/icon_16x16.png"
sips -z 32   32   "$PNG" --out "$ICONSET/icon_16x16@2x.png"
sips -z 32   32   "$PNG" --out "$ICONSET/icon_32x32.png"
sips -z 64   64   "$PNG" --out "$ICONSET/icon_32x32@2x.png"
sips -z 128  128  "$PNG" --out "$ICONSET/icon_128x128.png"
sips -z 256  256  "$PNG" --out "$ICONSET/icon_128x128@2x.png"
sips -z 256  256  "$PNG" --out "$ICONSET/icon_256x256.png"
sips -z 512  512  "$PNG" --out "$ICONSET/icon_256x256@2x.png"
sips -z 512  512  "$PNG" --out "$ICONSET/icon_512x512.png"
cp "$PNG" "$ICONSET/icon_512x512@2x.png"
iconutil -c icns "$ICONSET" -o MarkdownViewer/Assets/lucidVIEW.icns
rm -rf "$TMP"
```

The committed `lucidVIEW.icns` should be regenerated whenever `lucidview-icon.svg`
changes.

## Cross-OS publishing

If you publish from Windows or Linux for macOS:

- The `.app` is still assembled correctly, but the `lucidVIEW` binary will be
  missing the `+x` bit. The recipient needs to run
  `chmod +x lucidVIEW.app/Contents/MacOS/lucidVIEW` once.
- Ad-hoc codesigning is skipped because `codesign` is macOS-only. Recipients may
  see Gatekeeper warnings until they remove the quarantine attribute:
  `xattr -dr com.apple.quarantine lucidVIEW.app`.

## Distribution signing (out of scope here)

For Mac App Store or notarized distribution outside the Store, you need:

- An Apple Developer ID Application certificate in your login keychain
- A notarytool profile configured (`xcrun notarytool store-credentials`)
- Replace the ad-hoc `codesign --sign -` with `codesign --sign "Developer ID Application: Your Name (TEAMID)"`
- Run `xcrun notarytool submit lucidVIEW.app --keychain-profile <profile> --wait`
- `xcrun stapler staple lucidVIEW.app`

That's a separate task — this build script gets you to a working local `.app`,
not a Gatekeeper-clean redistributable.
