# ==============================================================================
# Publish-Update.ps1 -- 1-Click Publishing for Flappy and Flappy Re-Dovah
# ==============================================================================
#
# Steps:
#   1. Sync local modpack files from D:\Games\... to server /opt/modpack-dev/...
#   2. Build single-file delta patch + full packages via build-patch.sh
#   3. Assemble updated torrent and seed in qBittorrent via assemble-torrent.sh
#
# Usage examples:
#   .\Publish-Update.ps1                                   (interactive wizard)
#   .\Publish-Update.ps1 -Target re-dovah -Version 1.2.0
#   .\Publish-Update.ps1 -Target flappy -Version 4.2.0 -SkipSync
#   .\Publish-Update.ps1 -Target re-dovah -Version 1.2.0 -ForceStockGame
#   .\Publish-Update.ps1 -Target re-dovah -Full -RebuildTorrent
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

    [switch]$NoClean,

    [switch]$ForceStockGame,

    [switch]$RebuildTorrent,

    [string]$ServerHost = '192.168.1.119',
    [string]$ServerUser = 'root',
    [string]$ServerPass = '',
    [string]$HostKey = 'SHA256:J9hun9+jVWXsJVGB9yCuhbQrYQUblm2nvbny/wf/iHk'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$root = $PSScriptRoot
$plink = Join-Path $root 'tools\plink.exe'

if (-not $ServerPass) {
    if ($env:FLAPPY_SERVER_PASS) {
        $ServerPass = $env:FLAPPY_SERVER_PASS
    } else {
        $sec = Read-Host -Prompt "Enter server password for $ServerUser@$ServerHost" -AsSecureString
        $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
        $ServerPass = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
    }
}

if (-not (Test-Path -LiteralPath $plink)) {
    throw "plink.exe not found at $plink"
}

$isInteractive = (-not $PSBoundParameters.ContainsKey('Target')) -or (-not $PSBoundParameters.ContainsKey('Version'))

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
Write-Host ">>> Selected Target: $targetName ($Target)" -ForegroundColor Green

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
Write-Host "    Local Source   : $GameDir" -ForegroundColor DarkGray

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

    Write-Host "    Current version: $currentVer" -ForegroundColor Yellow
    $userVer = Read-Host "Enter new version [default: $suggestedVer]"
    $Version = if ($userVer) { $userVer.Trim() } else { $suggestedVer }
}

Write-Host "    Target Version : $Version" -ForegroundColor Green
$modeStr = if ($Full) { '--full' } else { '--incremental' }

# --- 4. Interactive Wizard Prompts (if run without CLI switches) ---
if ($isInteractive) {
    # StockGame prompt
    if (-not $PSBoundParameters.ContainsKey('ForceStockGame')) {
        Write-Host ""
        Write-Host "--- StockGame Base Packages ---" -ForegroundColor DarkCyan
        Write-Host "  StockGame is vanilla base (~10 GB), skipped on normal patches." -ForegroundColor DarkGray
        $sgChoice = Read-Host "  Repack modified StockGame? [y/N, default: N]"
        if ($sgChoice -match '^(y|yes)$') {
            $ForceStockGame = [switch]::Present
        }
    }

    # Sync prompt
    if (-not $PSBoundParameters.ContainsKey('SkipSync')) {
        Write-Host ""
        Write-Host "--- Local File Sync (Robocopy) ---" -ForegroundColor DarkCyan
        $syncChoice = Read-Host "  Sync local game files to server? [Y/n, default: Y]"
        if ($syncChoice -match '^(n|no)$') {
            $SkipSync = [switch]::Present
        }
    }

    # Torrent prompt
    if (-not $PSBoundParameters.ContainsKey('RebuildTorrent')) {
        Write-Host ""
        Write-Host "--- qBittorrent & Seeding ---" -ForegroundColor DarkCyan
        Write-Host "  Rebuilding torrent re-hashes all packages and updates qBittorrent." -ForegroundColor DarkGray
        $torChoice = Read-Host "  Rebuild torrent and update seeding? [y/N, default: N]"
        if ($torChoice -match '^(y|yes)$') {
            $RebuildTorrent = [switch]::Present
        }
    }

    # Confirmation summary
    $buildModeStr   = if ($Full) { 'FULL rebuild (--full)' } else { 'Incremental patch' }
    $fileSyncStr    = if ($SkipSync) { 'NO (skipped)' } else { 'YES (Robocopy)' }
    $fileSyncColor  = if ($SkipSync) { 'Yellow' } else { 'Green' }
    $stockGameStr   = if ($ForceStockGame) { 'YES (force repack)' } else { 'NO (reusing existing)' }
    $stockGameColor = if ($ForceStockGame) { 'Yellow' } else { 'DarkGray' }
    $torrentStr     = if ($RebuildTorrent) { 'YES (qBittorrent update)' } else { 'NO (static forum base)' }
    $torrentColor   = if ($RebuildTorrent) { 'Yellow' } else { 'DarkGray' }

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "  PUBLISH PLAN CONFIRMATION" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "  Target          : $targetName ($Target)" -ForegroundColor Cyan
    Write-Host "  Version         : $Version" -ForegroundColor Cyan
    Write-Host "  Local Source    : $GameDir" -ForegroundColor DarkGray
    Write-Host "  Build Mode      : $buildModeStr" -ForegroundColor DarkGray
    Write-Host "  File Sync       : $fileSyncStr" -ForegroundColor $fileSyncColor
    Write-Host "  Repack StockGame: $stockGameStr" -ForegroundColor $stockGameColor
    Write-Host "  Rebuild Torrent : $torrentStr" -ForegroundColor $torrentColor
    Write-Host "============================================================" -ForegroundColor Cyan
    $confirm = Read-Host "Proceed with execution? [Y/n, default: Y]"
    if ($confirm -match '^(n|no)$') {
        Write-Host ""
        Write-Host "  [CANCELLED] Publishing aborted by user." -ForegroundColor Yellow
        Write-Host ""
        exit 0
    }
}

$totalTimer = [System.Diagnostics.Stopwatch]::StartNew()
$syncTimerElapsed = $null
$buildTimerElapsed = $null
$torrentTimerElapsed = $null

Write-Host ""

# --- 5. STEP 1: Sync local files to server ---
if (-not $SkipSync) {
    $swSync = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " [1/3] Syncing local files to server ($Target)..." -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan

    # Clean local runtime junk if 4_FlappyTools_clear_garbage.bat exists
    $cleanBat = Join-Path $GameDir '4_FlappyTools_clear_garbage.bat'
    if ((-not $NoClean) -and (Test-Path -LiteralPath $cleanBat)) {
        Write-Host "  [CLEAN] Cleaning garbage and logs via 4_FlappyTools_clear_garbage.bat..." -ForegroundColor Yellow
        & cmd.exe /c "`"$cleanBat`" /Y"
        Write-Host "  [OK] Local game folder cleaned." -ForegroundColor Green
        Write-Host ""
    }

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
    $swSync.Stop()
    $syncTimerElapsed = $swSync.Elapsed
    Write-Host ("  [OK] Local files synced successfully to server ({0:mm\:ss})." -f $syncTimerElapsed) -ForegroundColor Green
} else {
    Write-Host "  [SKIP] Skipping file sync (-SkipSync specified)." -ForegroundColor Yellow
}

Write-Host ""

# --- 6. STEP 2: Trigger build-patch.sh on LXC 119 ---
$swBuild = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " [2/3] Building update packages on server..." -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$extraStockFlag = if ($ForceStockGame) { ' --force-stock' } else { '' }
$buildCmd = "bash /opt/builder/scripts/build-patch.sh $Target $Version $Mx $modeStr$extraStockFlag"
Write-Host "  Remote cmd: $buildCmd" -ForegroundColor DarkGray
Write-Host ""

& $plink -hostkey $HostKey -batch -pw $ServerPass "$ServerUser@$ServerHost" $buildCmd
if ($LASTEXITCODE -ne 0) {
    throw "build-patch.sh failed on server with exit code $LASTEXITCODE"
}

$swBuild.Stop()
$buildTimerElapsed = $swBuild.Elapsed
Write-Host ""
Write-Host ("  [OK] Server build and CDN publication complete ({0:mm\:ss})." -f $buildTimerElapsed) -ForegroundColor Green
Write-Host ""

# --- 7. STEP 3: Reassemble torrent (optional, only with -RebuildTorrent) ---
if ($RebuildTorrent) {
    $swTor = [System.Diagnostics.Stopwatch]::StartNew()
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

    $swTor.Stop()
    $torrentTimerElapsed = $swTor.Elapsed
    Write-Host ""
    Write-Host ("  [OK] Torrent assembled and registered in qBittorrent ({0:mm\:ss})." -f $torrentTimerElapsed) -ForegroundColor Green
} else {
    Write-Host "  [STATIC] Torrent left static for forum (rebuild not requested)." -ForegroundColor DarkGray
    Write-Host "           To rebuild torrent, pass -RebuildTorrent switch." -ForegroundColor DarkGray
}

$totalTimer.Stop()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  PUBLISH COMPLETE!" -ForegroundColor Green
Write-Host "  Target          : $targetName ($Target)" -ForegroundColor Green
Write-Host "  Version         : $Version" -ForegroundColor Green
Write-Host "  Delta           : CDN https://cdn.flappy.su/$Target/patches/ (single .7z)" -ForegroundColor Green
Write-Host "  Full            : CDN https://cdn.flappy.su/$Target/packages/ (hardlinked)" -ForegroundColor Green
$summaryStock = if ($ForceStockGame) { "Re-packed and published" } else { "Reused (unchanged)" }
$summaryTorrent = if ($RebuildTorrent) { "qBittorrent seeding updated" } else { "Static base (untouched for forum)" }
Write-Host "  StockGame       : $summaryStock" -ForegroundColor Green
Write-Host "  Torrent         : $summaryTorrent" -ForegroundColor Green
Write-Host "------------------------------------------------------------" -ForegroundColor DarkGreen
if ($syncTimerElapsed) { Write-Host ("  Time - Sync     : {0:mm\:ss}" -f $syncTimerElapsed) -ForegroundColor DarkGray }
if ($buildTimerElapsed) { Write-Host ("  Time - Build    : {0:mm\:ss}" -f $buildTimerElapsed) -ForegroundColor DarkGray }
if ($torrentTimerElapsed) { Write-Host ("  Time - Torrent  : {0:mm\:ss}" -f $torrentTimerElapsed) -ForegroundColor DarkGray }
Write-Host ("  Time - Total    : {0:mm\:ss}" -f $totalTimer.Elapsed) -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
