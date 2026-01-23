# lucidVIEW Cross-Platform Publish Script
# Builds compact single-file executables for Windows, Linux, and macOS

param(
    [ValidateSet('all', 'win', 'linux', 'osx')]
    [string]$Platform = 'all',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$projectPath = "$PSScriptRoot\MarkdownViewer\MarkdownViewer.csproj"
$outputBase = "$PSScriptRoot\publish"

# Runtime identifiers for each platform
$runtimes = @{
    'win'   = 'win-x64'
    'linux' = 'linux-x64'
    'osx'   = 'osx-x64'
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

function Publish-Platform {
    param([string]$Name, [string]$Rid)

    $outputDir = "$outputBase\$Name"
    Write-Host "`n=== Publishing for $Name ($Rid) ===" -ForegroundColor Cyan

    if ($Clean -and (Test-Path $outputDir)) {
        Write-Host "Cleaning $outputDir..." -ForegroundColor Yellow
        Remove-Item -Recurse -Force $outputDir
    }

    $args = @('publish', $projectPath) + $commonArgs + @(
        '--runtime', $Rid
        '--output', $outputDir
    )

    Write-Host "dotnet $($args -join ' ')" -ForegroundColor DarkGray
    & dotnet @args

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to publish for $Name" -ForegroundColor Red
        return $false
    }

    # Show output size
    $exeName = if ($Name -eq 'win') { 'lucidVIEW.exe' } else { 'lucidVIEW' }
    $exePath = Join-Path $outputDir $exeName
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

if ($Platform -eq 'all') {
    foreach ($p in $runtimes.GetEnumerator()) {
        if (-not (Publish-Platform -Name $p.Key -Rid $p.Value)) {
            $success = $false
        }
    }
} else {
    $rid = $runtimes[$Platform]
    if (-not (Publish-Platform -Name $Platform -Rid $rid)) {
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
