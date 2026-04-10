# lucidVIEW MSIX packager.
#
# Produces publish/store/lucidVIEW.msix ready for sideload testing or upload to
# the Microsoft Partner Center. Run on Windows with the Windows 10/11 SDK
# installed (MakeAppx.exe + signtool.exe in $env:ProgramFiles*\Windows Kits\10\bin\).
#
# Required env vars or params:
#   STORE_IDENTITY_NAME       e.g. "12345ABCD.lucidVIEW"
#   STORE_IDENTITY_PUBLISHER  e.g. "CN=12345ABCD-1111-2222-3333-444455556666"
# Optional:
#   STORE_CERT_PATH           path to .pfx (skips signing if absent)
#   STORE_CERT_PASSWORD       .pfx password
#   -Version                  four-part version, default yyyy.M.d.0

param(
    [string]$IdentityName      = $env:STORE_IDENTITY_NAME,
    [string]$IdentityPublisher = $env:STORE_IDENTITY_PUBLISHER,
    [string]$Version           = (Get-Date -Format 'yyyy.M.d.0'),
    [string]$CertPath          = $env:STORE_CERT_PATH,
    [string]$CertPassword      = $env:STORE_CERT_PASSWORD
)

$ErrorActionPreference = 'Stop'

if (-not $IdentityName -or -not $IdentityPublisher) {
    throw "STORE_IDENTITY_NAME and STORE_IDENTITY_PUBLISHER must be set (env or params). See docs/windows-store.md."
}

$root        = $PSScriptRoot
$projectPath = Join-Path $root 'MarkdownViewer/MarkdownViewer.csproj'
$stageDir    = Join-Path $root 'publish/store-stage'
$outDir      = Join-Path $root 'publish/store'
$msixOut     = Join-Path $outDir 'lucidVIEW.msix'

# --- 1. Resolve Windows SDK tools --------------------------------------------

function Get-WindowsKitTool([string]$name) {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    if (-not (Test-Path $kitsRoot)) {
        throw "Windows 10/11 SDK not found at $kitsRoot. Install via Visual Studio Installer or standalone SDK."
    }
    # Pick the highest-versioned 10.0.x.0/x64 directory that contains the tool
    $candidate = Get-ChildItem -Path $kitsRoot -Directory |
        Where-Object { $_.Name -match '^10\.0\.' } |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "x64/$name" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if (-not $candidate) { throw "$name not found under $kitsRoot. Install the Windows SDK." }
    return $candidate
}

$makeAppx = Get-WindowsKitTool 'makeappx.exe'
$signtool = if ($CertPath) { Get-WindowsKitTool 'signtool.exe' } else { $null }

# --- 2. Publish win-x64 single-file build into staging -----------------------

if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host '=== Publishing win-x64 ===' -ForegroundColor Cyan
& dotnet publish $projectPath `
    -c Release -r win-x64 `
    -p:PublishSingleFile=true `
    -p:SelfContained=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:PublishReadyToRunComposite=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $stageDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# --- 3. Substitute identity tokens into the manifest -------------------------

Write-Host '=== Preparing AppxManifest ===' -ForegroundColor Cyan
$manifestSrc = Join-Path $root 'MarkdownViewer/Package.appxmanifest'
$manifest    = Get-Content $manifestSrc -Raw
$manifest = $manifest.
    Replace('$(StoreIdentityName)',      $IdentityName).
    Replace('$(StoreIdentityPublisher)', $IdentityPublisher).
    Replace('$(StoreVersion)',           $Version)
$manifestOut = Join-Path $stageDir 'AppxManifest.xml'
Set-Content -Path $manifestOut -Value $manifest -Encoding UTF8

# --- 4. Copy tile assets into the staged layout ------------------------------

$assetsOut = Join-Path $stageDir 'Assets/Store'
New-Item -ItemType Directory -Force -Path $assetsOut | Out-Null
Copy-Item (Join-Path $root 'MarkdownViewer/Assets/Store/*.png') -Destination $assetsOut -Force

# --- 5. MakeAppx pack --------------------------------------------------------

Write-Host '=== MakeAppx pack ===' -ForegroundColor Cyan
& $makeAppx pack /d $stageDir /p $msixOut /overwrite
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx pack failed' }

# --- 6. Optional sign for sideload testing -----------------------------------

if ($signtool) {
    Write-Host '=== signtool sign ===' -ForegroundColor Cyan
    if ($CertPassword) {
        & $signtool sign /fd SHA256 /a /f $CertPath /p $CertPassword $msixOut
    } else {
        & $signtool sign /fd SHA256 /a /f $CertPath $msixOut
    }
    if ($LASTEXITCODE -ne 0) { throw 'signtool sign failed' }
} else {
    Write-Host 'Skipped signing (STORE_CERT_PATH not set). Store re-signs on submission.' -ForegroundColor Yellow
}

Write-Host ''
Write-Host "MSIX written: $msixOut" -ForegroundColor Green
$size = (Get-Item $msixOut).Length
Write-Host ("Size: {0:N2} MB" -f ($size / 1MB)) -ForegroundColor Green
