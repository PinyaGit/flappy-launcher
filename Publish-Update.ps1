# ==============================================================================
# Publish-Update.ps1 — 1-Click Publishing for Flappy and Flappy Re-Dovah
# ==============================================================================
#
# Steps:
#   1. Sync local modpack files from D:\Games\... to server /opt/modpack-dev/...
#   2. Build single-file delta patch + full packages via build-patch.sh
#   3. Assemble updated torrent and seed in qBittorrent via assemble-torrent.sh
#
# Usage examples:
#   .\Publish-Update.ps1
#   .\Publish-Update.ps1 -Target re-dovah -Version 1.1.4
#   .\Publish-Update.ps1 -Target flappy -Version 4.1.7 -SkipSync
#   .\Publish-Update.ps1 -Target re-dovah -Full
#
# ==============================================================================

param(
    [ValidateSet('re-dovah', 'flappy', '')]
    [string]$Target = '',

    [string]$Version = '',

    [string]$GameDir = '',

    [switch]$Full,

    [int]$Mx = 5,

    [switch]$SkipSync,

    [switch]$RebuildTorrent,

    [string]$ServerHost = '192.168.1.119',
    [string]$ServerUser = 'root',
    [string]$ServerPass = 'fX7BpN4h0ABXkA',
    [string]$HostKey = 'SHA256:J9hun9+jVWXsJVGB9yCuhbQrYQUblm2nvbny/wf/iHk'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$plink = Join-Path $root 'tools\plink.exe'

if (-not (Test-Path -LiteralPath $plink)) {
    throw "plink.exe not found at $plink"
}

# --- 1. Select Target ---
if (-not $Target) {
    Write-Host ""
    Write-Host "=== Select Game Target ===" -ForegroundColor Cyan
    Write-Host "  [1] Flappy Re-Dovah  (Skyrim VR/AE)"
    Write-Host "  [2] Flappy           (Skyrim AE)"
    Write-Host ""
    $choice = Read-Host "Select option [1/2, default: 1]"
    if ($choice -eq '2') {
        $Target = 'flappy'
    } else {
        $Target = 're-dovah'
    }
}

$targetName = if ($Target -eq 're-dovah') { 'Flappy Re-Dovah' } else { 'Flappy' }
Write-Host ""
Write-Host ">>> Publishing for: $targetName ($Target)" -ForegroundColor Green

# --- 2. Determine Local Game Directory ---
if (-not $GameDir) {
    $defaultGameDir = if ($Target -eq 're-dovah') { 'D:\Games\Flappy Re-Dovah' } else { 'D:\Games\Flappy' }
    if (Test-Path -LiteralPath $defaultGameDir) {
        $GameDir = $defaultGameDir
    } else {
        $inputDir = Read-Host "Enter local game directory [e.g. $defaultGameDir]"
        $GameDir = if ($inputDir) { $inputDir } else { $defaultGameDir }
    }
}

if (-not (Test-Path -LiteralPath $GameDir)) {
    throw "Game directory not found: $GameDir"
}
Write-Host "    Local Source: $GameDir" -ForegroundColor DarkGray

# --- 3. Determine Version ---
if (-not $Version) {
    # Query current version from server CDN index.json
    $currentVer = '1.0.0'
    try {
        $queryCmd = "grep 'version' /var/www/cdn.flappy.su/$Target/index.json 2>/dev/null | head -n 1"
        $verLine = & $plink -hostkey $HostKey -batch -pw $ServerPass "$ServerUser@$ServerHost" $queryCmd
        if ($verLine -match ':\s*"([^"]+)"') {
            $currentVer = $matches[1].Trim()
        }
    } catch {
        Write-Host "    Could not auto-fetch current version from server." -ForegroundColor Yellow
    }

    # Suggest bumped patch version
    $suggestedVer = $currentVer
    $parts = $currentVer.Split('.')
    if ($parts.Length -ge 3 -and [int]::TryParse($parts[-1], [ref]$null)) {
        $parts[-1] = ([int]$parts[-1] + 1).ToString()
        $suggestedVer = $parts -join '.'
    }

    Write-Host "    Current server version: $currentVer" -ForegroundColor Yellow
    $userVer = Read-Host "Enter new version [default: $suggestedVer]"
    $Version = if ($userVer) { $userVer.Trim() } else { $suggestedVer }
}

Write-Host "    Target Version: $Version" -ForegroundColor Green
$modeStr = if ($Full) { '--full' } else { '--incremental' }
Write-Host "    Build Mode    : $modeStr (Compression: -mx$Mx)" -ForegroundColor DarkGray
Write-Host ""

# --- 4. STEP 1: Sync local files to server ---
if (-not $SkipSync) {
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " [1/3] Syncing local files to server ($Target)..." -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan

    # Ensure SMB connection
    & net use "\\$ServerHost" /user:$ServerUser $ServerPass 2>&1 | Out-Null

    $remoteShare = if ($Target -eq 're-dovah') { "\\$ServerHost\redovah-dev" } else { "\\$ServerHost\flappy-dev" }
    if (-not (Test-Path -LiteralPath $remoteShare)) {
        throw "Cannot access SMB share: $remoteShare. Verify Samba is running on $ServerHost."
    }

    Write-Host "  Robocopy: $GameDir -> $remoteShare" -ForegroundColor DarkGray

    $excludeDirs = @(
        'saves',
        '.flappy',
        '.flappy_cache',
        '.flappy_download',
        'download_cache',
        'enbcache',
        'Crash Logs',
        'crashdumps',
        'NetScriptFramework'
    )

    $excludeFiles = @(
        '*.ess',
        '*.skse',
        '*.log',
        '*.tmp',
        '*.bak',
        '*.dump',
        'desktop.ini',
        'Thumbs.db',
        '*.user',
        '*.suo',
        '.flappy_installed',
        '.flappy_channel',
        '.flappy_mode'
    )

    $roboArgs = @(
        $GameDir,
        $remoteShare,
        '/MIR',
        '/MT:16',
        '/R:2',
        '/W:1',
        '/NDL',
        '/NFL',
        '/NJH',
        '/NJS',
        '/XD'
    ) + $excludeDirs + @('/XF') + $excludeFiles

    & robocopy @roboArgs
    $rc = $LASTEXITCODE
    if ($rc -gt 7) {
        throw "Robocopy failed with exit code $rc"
    }
    Write-Host "  [OK] Local files synced successfully to server." -ForegroundColor Green
} else {
    Write-Host "  [SKIP] Skipping file sync (-SkipSync specified)." -ForegroundColor Yellow
}

Write-Host ""

# --- 5. STEP 2: Trigger build-patch.sh on LXC 119 ---
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " [2/3] Building update packages on server..." -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$buildCmd = "bash /opt/builder/scripts/build-patch.sh $Target $Version $Mx $modeStr"
Write-Host "  Remote cmd: $buildCmd" -ForegroundColor DarkGray
Write-Host ""

& $plink -hostkey $HostKey -batch -pw $ServerPass "$ServerUser@$ServerHost" $buildCmd
if ($LASTEXITCODE -ne 0) {
    throw "build-patch.sh failed on server with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "  [OK] Server build and CDN publication complete." -ForegroundColor Green
Write-Host ""

# --- 6. STEP 3: Reassemble torrent (optional, only with -RebuildTorrent) ---
if ($RebuildTorrent) {
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " [3/3] Assembling torrent & updating qBittorrent..." -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan

    $torrentCmd = 'bash /opt/builder/scripts/assemble-torrent.sh ""'
    Write-Host "  Remote cmd: $torrentCmd" -ForegroundColor DarkGray
    Write-Host ""

    & $plink -hostkey $HostKey -batch -pw $ServerPass "$ServerUser@$ServerHost" $torrentCmd
    if ($LASTEXITCODE -ne 0) {
        throw "assemble-torrent.sh failed on server with exit code $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "  [OK] Torrent assembled and registered in qBittorrent." -ForegroundColor Green
} else {
    Write-Host "  [STATIC] Торрент оставлен статичным для форума (пересборка не требуется)." -ForegroundColor DarkGray
    Write-Host "           Для пересборки торрента передайте ключ -RebuildTorrent." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  PUBLISH COMPLETE!" -ForegroundColor Green
Write-Host "  Target : $targetName ($Target)" -ForegroundColor Green
Write-Host "  Version: $Version" -ForegroundColor Green
Write-Host "  Delta  : CDN https://cdn.flappy.su/$Target/patches/ (single .7z)" -ForegroundColor Green
Write-Host "  Full   : CDN https://cdn.flappy.su/$Target/packages/ (hardlinked)" -ForegroundColor Green
Write-Host ("  Torrent: " + (if ($RebuildTorrent) { "qBittorrent seeding updated" } else { "Static base (untouched for forum)" })) -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
