# Build Flappy Launcher Release (.NET 8) -> Publish\ + CDN
# Builds with dotnet publish (self-contained single-file win-x64), then publishes to CDN via Samba.
#
# Output:
#   Publish\Flappy Launcher.exe
#   \\192.168.1.119\cdn\launcher\Flappy-Launcher.zip
#   \\192.168.1.119\cdn\launcher\version.json
#
#Requires -Version 5.1
param(
    [string]$Version = '',
    [string]$Notes = '',
    [switch]$Mandatory,
    [switch]$SkipBuild,
    # CDN destination: SMB path to \\server\cdn\launcher on DEV, or production path
    [string]$CdnPath = '\\192.168.1.119\cdn\launcher'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csproj = Join-Path $root 'FlappyReDovahLauncher\FlappyReDovahLauncher.csproj'
$exeName = 'Flappy Launcher.exe'
$zipName = 'Flappy-Launcher.zip'
$zipUrlRel = "launcher/$zipName"

$publish = Join-Path $root 'Publish'
$publishNet8 = Join-Path $publish 'net8'
$localCdn = Join-Path $publish 'cdn-launcher'

# --- 1. Build ---
if (-not $SkipBuild) {
    Write-Host "[1/4] Building .NET 8 Single-File Release..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $publishNet8 | Out-Null
    & dotnet publish "$csproj" -c Release -r win-x64 --self-contained true "-p:PublishSingleFile=true" "-p:IncludeNativeLibrariesForSelfExtract=true" "-p:EnableCompressionInSingleFile=true" -o "$publishNet8"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
} else {
    Write-Host "[1/4] SkipBuild - using existing Release binaries." -ForegroundColor Yellow
}

# --- 2. Copy EXE to Publish\ ---
Write-Host "[2/4] Copying to Publish\..." -ForegroundColor Cyan
$fromExe = Join-Path $publishNet8 $exeName
if (-not (Test-Path -LiteralPath $fromExe)) { throw "Missing: $fromExe" }
Copy-Item -LiteralPath $fromExe -Destination (Join-Path $publish $exeName) -Force

# --- 3. Determine version ---
if (-not $Version) {
    $exe = Join-Path $publish $exeName
    $vi = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    $Version = ($vi.FileVersion -split '\.')[0..2] -join '.'
    if (-not $Version) { $Version = '0.2.0' }
}
Write-Host "    Version: $Version" -ForegroundColor Green

# --- 4. Create ZIP ---
Write-Host "[3/4] Creating Flappy-Launcher.zip..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $localCdn | Out-Null
$zipPath = Join-Path $localCdn $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$stage = Join-Path $env:TEMP ('FlappyLaunchZip_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    Copy-Item (Join-Path $publish $exeName) (Join-Path $stage $exeName) -Force
    $toolsRes = Join-Path $root 'FlappyReDovahLauncher\Resources'
    foreach ($toolFile in @('7za.exe', '7za.dll', '7zxa.dll')) {
        $src = Join-Path $toolsRes $toolFile
        if (Test-Path $src) {
            Copy-Item $src (Join-Path $publish $toolFile) -Force
            Copy-Item $src (Join-Path $stage $toolFile) -Force
        }
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath, 'Optimal', $false)
} finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

# --- Compute SHA256 ---
$sha = [Security.Cryptography.SHA256]::Create()
$fs = [IO.File]::OpenRead($zipPath)
try {
    $hash = ([BitConverter]::ToString($sha.ComputeHash($fs))).Replace('-', '').ToLowerInvariant()
} finally { $fs.Dispose(); $sha.Dispose() }
$size = [int64](Get-Item $zipPath).Length

# --- Build version.json ---
$jsonObj = [ordered]@{
    version   = $Version
    url       = $zipUrlRel
    sha256    = $hash
    size      = $size
    mandatory = [bool]$Mandatory
    notes     = $Notes
}
$json = ($jsonObj | ConvertTo-Json -Depth 5) + "`n"
$utf8 = New-Object System.Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $localCdn 'version.json'), $json, $utf8)

# --- 4. Copy to CDN (via Samba) ---
Write-Host "[4/4] Publishing to CDN: $CdnPath" -ForegroundColor Cyan
if (-not (Test-Path $CdnPath)) {
    Write-Warning "CDN path not accessible: $CdnPath"
    Write-Host "  -> Files saved locally to: $localCdn"
    Write-Host "  -> Run with -CdnPath to set target, or map \\192.168.1.119\cdn"
} else {
    New-Item -ItemType Directory -Force -Path $CdnPath | Out-Null
    Copy-Item (Join-Path $localCdn $zipName)       (Join-Path $CdnPath $zipName)      -Force
    Copy-Item (Join-Path $localCdn 'version.json') (Join-Path $CdnPath 'version.json') -Force
    Copy-Item (Join-Path $publish $exeName)        (Join-Path $CdnPath $exeName)       -Force
    Write-Host "  -> Published to CDN!" -ForegroundColor Green
}

Write-Host ""
Write-Host "  ============================================" -ForegroundColor Green
Write-Host "  Flappy Launcher $Version - DONE" -ForegroundColor Green
Write-Host "  ============================================"
Write-Host "  ZIP    : $zipPath ($([Math]::Round($size/1MB, 2)) MB)"
Write-Host "  sha256 : $hash"
Write-Host "  CDN    : $CdnPath"
Write-Host ""
