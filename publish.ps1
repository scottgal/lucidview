# lucidVIEW Cross-Platform Publish Script
# Builds compact single-file executables for Windows, Linux, and macOS.
# On macOS, wraps the published binary in a proper .app bundle so double-click
# launches lucidVIEW with a Dock icon instead of opening Terminal.

param(
    [ValidateSet('all', 'win', 'linux', 'osx', 'osx-x64', 'osx-arm64')]
    [string]$Platform = 'all',
    [ValidateSet('Lean', 'Full', 'All')]
    [string]$Edition = 'Lean',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'MarkdownViewer/MarkdownViewer.csproj'
$outputBase = Join-Path $PSScriptRoot 'publish'

# Runtime identifiers for each platform target
$runtimes = [ordered]@{
    'win'         = 'win-x64'
    'win-arm64'   = 'win-arm64'
    'linux'       = 'linux-x64'
    'linux-arm64' = 'linux-arm64'
    'osx-x64'    = 'osx-x64'
    'osx-arm64'  = 'osx-arm64'
}

# Common publish parameters for compact single-file output
$commonArgs = @(
    '--configuration', 'Release'
    '-p:PublishSingleFile=true'
    '-p:SelfContained=true'
    '-p:IncludeNativeLibrariesForSelfExtract=true'
    '-p:EnableCompressionInSingleFile=true'
    '-p:IncludeAllContentForSelfExtract=true'
    '-p:DebugType=none'
    '-p:DebugSymbols=false'
    '-p:PublishTrimmed=false'  # Required for Avalonia/LiveMarkdown
    '-p:PublishReadyToRun=true'
    '-p:PublishReadyToRunComposite=true'
    '-p:StripSymbols=true'
    '-p:OptimizationPreference=Size'
)

function New-MacAppBundle {
    param([string]$RawPublishDir, [string]$BundleParentDir)

    $appRoot   = Join-Path $BundleParentDir 'lucidVIEW.app'
    $contents  = Join-Path $appRoot 'Contents'
    $macos     = Join-Path $contents 'MacOS'
    $resources = Join-Path $contents 'Resources'

    if (Test-Path $appRoot) { Remove-Item -Recurse -Force $appRoot }
    New-Item -ItemType Directory -Force -Path $macos, $resources | Out-Null

    Copy-Item (Join-Path $PSScriptRoot 'MarkdownViewer/macos/Info.plist') (Join-Path $contents 'Info.plist')
    Copy-Item (Join-Path $PSScriptRoot 'MarkdownViewer/Assets/lucidVIEW.icns') (Join-Path $resources 'lucidVIEW.icns')

    # Move every published file (binary + native libs) into Contents/MacOS
    Get-ChildItem -Path $RawPublishDir -Force | Move-Item -Destination $macos -Force

    # Bundle layout is now:
    #   $BundleParentDir/lucidVIEW.app/Contents/Info.plist
    #   $BundleParentDir/lucidVIEW.app/Contents/MacOS/lucidVIEW
    #   $BundleParentDir/lucidVIEW.app/Contents/Resources/lucidVIEW.icns
    # Drop the now-empty raw publish folder.
    if ((Get-ChildItem -Path $RawPublishDir -Force | Measure-Object).Count -eq 0) {
        Remove-Item -Force $RawPublishDir
    }

    # Mark the binary executable. The Windows publish path strips the +x bit on
    # mac binaries; on macOS/Linux this is a no-op for already-executable files.
    $binPath = Join-Path $macos 'lucidVIEW'
    if (Test-Path $binPath) {
        if ($IsMacOS -or $IsLinux) {
            chmod +x $binPath
        }

        # Ad-hoc codesign so Gatekeeper doesn't quarantine local builds.
        # Distribution builds need a real Developer ID — see docs/macos-bundle.md.
        if ($IsMacOS) {
            Write-Host "Ad-hoc signing $appRoot" -ForegroundColor DarkGray
            & codesign --force --deep --sign - $appRoot 2>&1 | Out-Null
        }
    }

    return $appRoot
}

function Publish-Platform {
    param([string]$Name, [string]$Rid)

    $outputDir = Join-Path $outputBase $Name
    Write-Host "`n=== Publishing for $Name ($Rid) ===" -ForegroundColor Cyan

    if ($Clean -and (Test-Path $outputDir)) {
        Write-Host "Cleaning $outputDir..." -ForegroundColor Yellow
        Remove-Item -Recurse -Force $outputDir
    }

    # macOS bundles: publish into a staging folder so we can move files into the .app layout.
    $isMac = $Name -like 'osx*'
    $publishTarget = if ($isMac) { Join-Path $outputDir '_raw' } else { $outputDir }

    $publishArgs = @('publish', $projectPath) + $commonArgs + @(
        '--runtime', $Rid
        '--output', $publishTarget
    )

    Write-Host "dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray
    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to publish for $Name" -ForegroundColor Red
        return $false
    }

    if ($isMac) {
        $appRoot = New-MacAppBundle -RawPublishDir $publishTarget -BundleParentDir $outputDir
        $exePath = Join-Path $appRoot 'Contents/MacOS/lucidVIEW'
    }
    else {
        $exeName = if ($Name -eq 'win') { 'lucidVIEW.exe' } else { 'lucidVIEW' }
        $exePath = Join-Path $outputDir $exeName
    }

    if (Test-Path $exePath) {
        $size = (Get-Item $exePath).Length
        $sizeMB = [math]::Round($size / 1MB, 2)
        Write-Host "Output: $exePath ($sizeMB MB)" -ForegroundColor Green
    }

    return $true
}

# Main
Write-Host "lucidVIEW Publisher" -ForegroundColor Magenta
Write-Host "===================" -ForegroundColor Magenta

$success = $true

# 'osx' is shorthand for both osx-x64 and osx-arm64
$platformsToBuild = switch ($Platform) {
    'all' { $runtimes.Keys }
    'osx' { @('osx-x64', 'osx-arm64') }
    default { @($Platform) }
}

# ── Lean builds ──────────────────────────────────────────────────────────────
if ($Edition -in @('Lean', 'All')) {
    foreach ($p in $platformsToBuild) {
        if (-not $runtimes.Contains($p)) {
            Write-Host "Unknown platform: $p" -ForegroundColor Red
            $success = $false
            continue
        }
        if (-not (Publish-Platform -Name $p -Rid $runtimes[$p])) {
            $success = $false
        }
    }
}

# ── FULL builds ───────────────────────────────────────────────────────────────
# FULL cannot use PublishSingleFile — Playwright's browser bundle and
# LlamaSharp's native libs must stay as loose files in the output directory.

function Publish-Full {
    param([string]$Rid)
    $fullProject = Join-Path $PSScriptRoot 'MarkdownViewer.Full/MarkdownViewer.Full.csproj'
    $fullOutput  = Join-Path $outputBase "full/$Rid"
    Write-Host "`n=== Publishing FULL for $Rid -> $fullOutput ===" -ForegroundColor Cyan

    $fullArgs = @(
        'publish', $fullProject
        '--configuration', 'Release'
        '--runtime', $Rid
        '--self-contained', 'true'
        '-p:PublishSingleFile=false'
        '-p:PublishReadyToRun=false'
        '-p:PublishTrimmed=false'
        '--output', $fullOutput
    )

    Write-Host "dotnet $($fullArgs -join ' ')" -ForegroundColor DarkGray
    & dotnet @fullArgs
    if ($LASTEXITCODE -ne 0) { throw "FULL publish failed for $Rid" }

    # Mark the binary executable on POSIX hosts.
    # AssemblyName in MarkdownViewer.Full.csproj is 'lucidVIEW' (same as lean).
    $exe = if ($Rid -like 'win-*') { 'lucidVIEW.exe' } else { 'lucidVIEW' }
    $exePath = Join-Path $fullOutput $exe
    if (($IsMacOS -or $IsLinux) -and (Test-Path $exePath)) {
        chmod +x $exePath
    }

    # Bake Playwright browsers into the published output so the bundle is
    # self-contained. Only bake when publishing for the current host RID —
    # cross-compiled binaries (e.g. win-x64 built on macOS) won't execute here.
    $hostRid = if ($IsWindows) { 'win-x64' } elseif ($IsMacOS) {
        if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq
            [System.Runtime.InteropServices.Architecture]::Arm64) { 'osx-arm64' } else { 'osx-x64' }
    } else { 'linux-x64' }

    if ($Rid -eq $hostRid -and (Test-Path $exePath)) {
        Write-Host "Baking Playwright browsers into $fullOutput/.playwright ..." -ForegroundColor DarkGray
        $env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $fullOutput '.playwright'
        & $exePath '--install-browsers'
        Remove-Item Env:PLAYWRIGHT_BROWSERS_PATH -ErrorAction SilentlyContinue
    } else {
        Write-Host "Skipping browser bake (cross-compile RID $Rid != host $hostRid)" -ForegroundColor Yellow
    }

    if (Test-Path $exePath) {
        $size = (Get-Item $exePath).Length
        $sizeMB = [math]::Round($size / 1MB, 2)
        Write-Host "Output: $exePath ($sizeMB MB)" -ForegroundColor Green
    }

    return $true
}

if ($Edition -in @('Full', 'All')) {
    foreach ($p in $platformsToBuild) {
        if (-not $runtimes.Contains($p)) {
            Write-Host "Unknown platform: $p" -ForegroundColor Red
            $success = $false
            continue
        }
        if (-not (Publish-Full -Rid $runtimes[$p])) {
            $success = $false
        }
    }
}

if ($success) {
    Write-Host "`nAll builds completed!" -ForegroundColor Green
    Write-Host "Output directory: $outputBase" -ForegroundColor Cyan
} else {
    Write-Host "`nSome builds failed!" -ForegroundColor Red
    exit 1
}
