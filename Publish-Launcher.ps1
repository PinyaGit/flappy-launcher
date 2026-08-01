# Build Flappy Launcher Release → Publish\ + self-update zip
# Public/maintainer script — no secrets.
#
# Output:
#   Publish\Flappy Launcher.exe
#   Publish\cdn-launcher\Flappy-Launcher.zip
#   Publish\cdn-launcher\version.json
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
$exeName = 'Flappy Launcher.exe'
$configName = 'Flappy Launcher.exe.config'
$zipName = 'Flappy-Launcher.zip'
$zipUrlRel = "launcher/$zipName"

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

New-Item -ItemType Directory -Force -Path $publish | Out-Null
foreach ($f in @($exeName, $configName)) {
    $from = Join-Path $release $f
    if (-not (Test-Path -LiteralPath $from)) { throw "Missing: $from" }
    Copy-Item -LiteralPath $from -Destination (Join-Path $publish $f) -Force
}

if (-not $Version) {
    $exe = Join-Path $publish $exeName
    $vi = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    $Version = ($vi.FileVersion -split '\.')[0..2] -join '.'
    if (-not $Version) { $Version = '0.0.1' }
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$zipPath = Join-Path $outDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$stage = Join-Path $env:TEMP ('FlappyLaunchZip_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    Copy-Item (Join-Path $publish $exeName) (Join-Path $stage $exeName) -Force
    Copy-Item (Join-Path $publish $configName) (Join-Path $stage $configName) -Force
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

$notesEsc = ($Notes -replace '\\', '\\' -replace '"', '\"' -replace "`r", '' -replace "`n", '\n')
$json = @"
{
  "version": "$Version",
  "url": "$zipUrlRel",
  "sha256": "$hash",
  "size": $size,
  "mandatory": $($Mandatory.ToString().ToLowerInvariant()),
  "notes": "$notesEsc"
}
"@
$utf8 = New-Object System.Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $outDir 'version.json'), $json.Trim() + "`n", $utf8)

Write-Host ""
Write-Host "  Flappy Launcher $Version"
Write-Host "  Publish : $publish"
Write-Host "  Zip     : $zipPath"
Write-Host "  sha256  : $hash"
Write-Host "  Upload version.json + $zipName to CDN launcher/"
Write-Host ""
