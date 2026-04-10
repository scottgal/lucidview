# Windows Store (MSIX) packaging

lucidVIEW ships to the Microsoft Store as an MSIX package built from
`MarkdownViewer/Package.appxmanifest` plus the tile assets in
`MarkdownViewer/Assets/Store/`. The full pipeline is one PowerShell script:

```powershell
pwsh ./pack-msix.ps1
```

It produces `publish/store/lucidVIEW.msix`. Submission to Partner Center stays
manual.

## One-time setup

### 1. Reserve the app on Partner Center

1. Sign in to <https://partner.microsoft.com/dashboard>
2. Reserve the app name "lucidVIEW" (or your preferred name)
3. Read off the publisher identity from **App identity**:
   - **Package/Identity/Name** → e.g. `12345ABCD.lucidVIEW`
   - **Package/Identity/Publisher** → e.g. `CN=12345ABCD-1111-2222-3333-444455556666`

### 2. Set the identity env vars

```powershell
[Environment]::SetEnvironmentVariable('STORE_IDENTITY_NAME',      '12345ABCD.lucidVIEW',                          'User')
[Environment]::SetEnvironmentVariable('STORE_IDENTITY_PUBLISHER', 'CN=12345ABCD-1111-2222-3333-444455556666',     'User')
```

For CI, add the same values as `STORE_IDENTITY_NAME` and `STORE_IDENTITY_PUBLISHER`
**repository secrets** in GitHub.

### 3. (Optional) Generate a local test cert for sideload testing

The Store re-signs your MSIX on upload, so a self-signed cert is fine for
local validation. Subject CN must match the publisher identity exactly.

```powershell
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject 'CN=12345ABCD-1111-2222-3333-444455556666' `
    -KeyUsage DigitalSignature `
    -CertStoreLocation 'Cert:\CurrentUser\My'

# Export to .pfx
$pwd = ConvertTo-SecureString -String 'changeit' -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $env:USERPROFILE\lucidview-test.pfx -Password $pwd

# Trust the cert so sideloaded MSIX installs
Import-PfxCertificate -FilePath $env:USERPROFILE\lucidview-test.pfx `
    -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' -Password $pwd

[Environment]::SetEnvironmentVariable('STORE_CERT_PATH',     "$env:USERPROFILE\lucidview-test.pfx", 'User')
[Environment]::SetEnvironmentVariable('STORE_CERT_PASSWORD', 'changeit',                            'User')
```

## Build the MSIX

```powershell
pwsh ./pack-msix.ps1                       # version defaults to yyyy.M.d.0
pwsh ./pack-msix.ps1 -Version 1.2.3.0      # explicit version
```

The script:

1. Resolves `MakeAppx.exe` and `signtool.exe` from the latest installed Windows SDK
2. `dotnet publish -r win-x64` (single-file, self-contained, ReadyToRun) into `publish/store-stage/`
3. Substitutes `$(StoreIdentityName)`, `$(StoreIdentityPublisher)`, `$(StoreVersion)` tokens in `Package.appxmanifest` and writes the result as `AppxManifest.xml` next to the binary
4. Copies `MarkdownViewer/Assets/Store/*.png` into `publish/store-stage/Assets/Store/`
5. Runs `MakeAppx pack` to produce `publish/store/lucidVIEW.msix`
6. If `STORE_CERT_PATH` is set, runs `signtool sign` for sideload testing

## Sideload + smoke test

```powershell
Add-AppxPackage publish/store/lucidVIEW.msix -ForceUpdateFromAnyVersion
Start-Process "shell:AppsFolder\$env:STORE_IDENTITY_NAME!App"
```

Verify:
- Tile in Start menu shows the lucidVIEW icon
- Splash screen renders on launch (`#1e1e1e` background)
- Window opens with no console
- Right-click any `.md` file in Explorer → "Open with" lists lucidVIEW
- `markdown://example` URLs open lucidVIEW (protocol handler)

Uninstall when done:
```powershell
Get-AppxPackage *lucidVIEW* | Remove-AppxPackage
```

## Run the Windows App Certification Kit (WACK)

WACK ships with the Windows SDK at
`${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe`.

```powershell
$wack = "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe"
& $wack reset
& $wack test -appxpackagepath publish/store/lucidVIEW.msix -reportoutputpath wack-report.xml
```

A clean WACK report is required by Partner Center certification. Fix any
flagged issues before submitting.

## Submit to Partner Center

1. Upload `publish/store/lucidVIEW.msix` under **Packages** in your Partner Center submission
2. Fill in the Store listing (name, description, screenshots, age rating, privacy policy URL)
3. Submit for certification

Manual prerequisites you'll need outside this repo:

- Microsoft Partner Center developer account ($19 one-time)
- Privacy policy URL hosted somewhere (lucidVIEW stores nothing remote — a one-paragraph "no data leaves your device" page is fine)
- 4+ Store screenshots at 1366×768 minimum

## CI: GitHub Actions workflow

`.github/workflows/store-publish.yml` (manual dispatch) runs `pack-msix.ps1`
on `windows-latest` and uploads `lucidVIEW.msix` as a build artifact. Trigger
from the Actions tab when you want a fresh MSIX without booting Windows
locally. Add `STORE_IDENTITY_NAME` and `STORE_IDENTITY_PUBLISHER` as repository
secrets first.

## Why MakeAppx instead of a WAP project

A Windows Application Packaging (`.wapproj`) project requires Visual Studio's
MSBuild on Windows and pulls in extra plumbing. The MakeAppx + signtool
approach in `pack-msix.ps1` runs anywhere the standalone Windows SDK is
installed (including the `windows-latest` GitHub runner) and is one
PowerShell script you can read top to bottom. Microsoft documents both flows
at <https://learn.microsoft.com/en-us/windows/msix/package/manual-packaging-root>.

## Regenerating tile assets

The eight PNGs in `MarkdownViewer/Assets/Store/` are generated from
`MarkdownViewer/Assets/lucidview-icon.svg`. To regenerate (macOS):

```bash
SRC=MarkdownViewer/Assets/lucidview-icon.svg
OUT=MarkdownViewer/Assets/Store
TMP=$(mktemp -d)
qlmanage -t -s 1024 -o "$TMP" "$SRC"
MASTER="$TMP/$(basename $SRC).png"
sips -z 44   44   "$MASTER" --out "$OUT/Square44x44Logo.png"
sips -z 71   71   "$MASTER" --out "$OUT/Square71x71Logo.png"
sips -z 150  150  "$MASTER" --out "$OUT/Square150x150Logo.png"
sips -z 310  310  "$MASTER" --out "$OUT/Square310x310Logo.png"
sips -z 50   50   "$MASTER" --out "$OUT/StoreLogo.png"
sips -z 44   44   "$MASTER" --out "$OUT/FileIcon.png"
sips -z 150 310  "$MASTER" --out "$OUT/Wide310x150Logo.png"
sips -z 300 620  "$MASTER" --out "$OUT/SplashScreen.png"
rm -rf "$TMP"
```

The Wide and Splash tiles get stretched (sips can't pad). The current SVG has
enough internal padding that this looks fine, but for a polished Store listing
you may want to manually composite the icon centered on a `#1a1a2e` background
at the exact tile size.
