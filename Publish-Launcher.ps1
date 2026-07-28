# Build launcher zip + version.json for CDN self-update
# Output: Publish\cdn-launcher\
#   version.json
#   Flappy-Re-Dovah-Launcher.zip
#
# Upload BOTH to: https://cdn.flappy.su/launcher/
#
#Requires -Version 5.1
param(
    [string]$Version = '',
    [string]$Notes = '',
    [switch]$Mandatory,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'FlappyReDovahLauncher\FlappyReDovahLauncher.csproj'
$release = Join-Path $root 'FlappyReDovahLauncher\bin\Release'
$publish = Join-Path $root 'Publish'
$outDir = Join-Path $publish 'cdn-launcher'
$msbCandidates = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
)
$msb = $msbCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $SkipBuild) {
    if (-not $msb) { throw 'MSBuild not found' }
    Write-Host "Building Release..."
    & $msb (Join-Path $root 'FlappyReDovahLauncher.sln') /t:Rebuild /p:Configuration=Release /p:Platform='Any CPU' /v:m /nologo
    if ($LASTEXITCODE -ne 0) { throw "MSBuild failed: $LASTEXITCODE" }
}

# Refresh Publish folder
New-Item -ItemType Directory -Force -Path $publish | Out-Null
$copy = @('Flappy Re-Dovah.exe', 'Flappy Re-Dovah.exe.config', '7za.exe', '7za.dll', '7zxa.dll')
foreach ($f in $copy) {
    $from = Join-Path $release $f
    if (-not (Test-Path -LiteralPath $from)) { throw "Missing: $from" }
    Copy-Item -LiteralPath $from -Destination (Join-Path $publish $f) -Force
}

# Version from assembly if not passed
if (-not $Version) {
    $exe = Join-Path $publish 'Flappy Re-Dovah.exe'
    $vi = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    $Version = ($vi.FileVersion -split '\.')[0..2] -join '.'
    if (-not $Version) { $Version = '1.0.0' }
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$zipPath = Join-Path $outDir 'Flappy-Re-Dovah-Launcher.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Zip only launcher files (no game install)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stage = Join-Path $env:TEMP ('FlappyLaunchZip_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    foreach ($f in $copy) {
        Copy-Item (Join-Path $publish $f) (Join-Path $stage $f) -Force
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath, 'Optimal', $false)
} finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

$sha = [Security.Cryptography.SHA256]::Create()
$fs = [IO.File]::OpenRead($zipPath)
try {
    $hash = ([BitConverter]::ToString($sha.ComputeHash($fs))).Replace('-', '').ToLowerInvariant()
} finally { $fs.Dispose(); $sha.Dispose() }
$size = [int64](Get-Item $zipPath).Length

$manifest = [ordered]@{
    version   = $Version
    url       = 'launcher/Flappy-Re-Dovah-Launcher.zip'
    sha256    = $hash
    size      = $size
    mandatory = [bool]$Mandatory
    notes     = [string]$Notes
}
$jsonPath = Join-Path $outDir 'version.json'
$utf8 = New-Object System.Text.UTF8Encoding $false
# Manual JSON to avoid PS ConvertTo-Json encoding quirks on some hosts
$notesEsc = ($Notes -replace '\\', '\\' -replace '"', '\"' -replace "`r", '' -replace "`n", '\n')
$json = @"
{
  "version": "$Version",
  "url": "launcher/Flappy-Re-Dovah-Launcher.zip",
  "sha256": "$hash",
  "size": $size,
  "mandatory": $($Mandatory.ToString().ToLowerInvariant()),
  "notes": "$notesEsc"
}
"@
[IO.File]::WriteAllText($jsonPath, $json.Trim() + "`n", $utf8)

Write-Host ''
Write-Host '  CDN launcher package ready:'
Write-Host "    $outDir"
Write-Host "    version  : $Version"
Write-Host "    zip size : $([math]::Round($size/1KB,1)) KB"
Write-Host "    sha256   : $hash"
Write-Host ''
Write-Host '  Upload to CDN (both files):'
Write-Host '    https://cdn.flappy.su/launcher/version.json'
Write-Host '    https://cdn.flappy.su/launcher/Flappy-Re-Dovah-Launcher.zip'
Write-Host ''
# Ensure upload helper exists in outDir (points to root self-contained bat)
$uploadRoot = Join-Path $root 'Upload-Launcher-CDN.bat'
$uploadStub = Join-Path $outDir 'Upload-CDN.bat'
if (Test-Path -LiteralPath $uploadRoot) {
    @(
        '@echo off',
        ':: Uploads this folder via root script (do not rename Upload-Launcher-CDN.bat)',
        "call `"$uploadRoot`""
    ) | Set-Content -LiteralPath $uploadStub -Encoding ASCII
}

Write-Host '  Upload (edit CHANGE_ME secrets in bat first; never commit real passwords):'
Write-Host "    $uploadRoot"
Write-Host ''
