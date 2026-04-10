# lucidVIEW Cross-Platform Publish Script
# Builds compact single-file executables for Windows, Linux, and macOS.
# On macOS, wraps the published binary in a proper .app bundle so double-click
# launches lucidVIEW with a Dock icon instead of opening Terminal.

param(
    [ValidateSet('all', 'win', 'linux', 'osx', 'osx-x64', 'osx-arm64')]
    [string]$Platform = 'all',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'MarkdownViewer/MarkdownViewer.csproj'
$outputBase = Join-Path $PSScriptRoot 'publish'

# Runtime identifiers for each platform target
$runtimes = [ordered]@{
    'win'       = 'win-x64'
    'linux'     = 'linux-x64'
    'osx-x64'   = 'osx-x64'
    'osx-arm64' = 'osx-arm64'
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

if ($success) {
    Write-Host "`nAll builds completed!" -ForegroundColor Green
    Write-Host "Output directory: $outputBase" -ForegroundColor Cyan
} else {
    Write-Host "`nSome builds failed!" -ForegroundColor Red
    exit 1
}
