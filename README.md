# ⚠️ NSFW / content warning (please read, then laugh, then read again)

Running this launcher **may** cause:

- mild-to-severe **discomfort**
- sudden **aversion to vanilla Skyrim**
- **nausea** (disk space graphs, progress bars, or the modlist)
- an unexplained urge to install *just one more* optional separator
- social consequences if someone walks in while the splash art is full-screen

This is a **multi-game Flappy library launcher**. Packs can include adult-oriented content depending on what the maintainers ship.  
If that is not your cup of mead: **close the window, go outside, touch grass, respect your limits.**  
If it is: welcome. Stay hydrated. Use Repair if reality desyncs.

---

# Flappy Launcher

Source for **Flappy Launcher** — a multi-game desktop library (**Re-Dovah**, **Flappy** 4.0.0, …), published mainly for **transparency**: so players can see what runs on their PC when they install or update a pack.

| | |
|--|--|
| **Product** | Flappy Launcher |
| **Exe** | `Flappy Launcher.exe` |
| **Version (this tree)** | **0.2.0** (launcher only — not a game version) |
| **Framework** | **.NET 8.0 Windows Desktop** (Self-Contained Single-File) |
| **CDN zip** | `launcher/Flappy-Launcher.zip` |
| **Manifest** | `launcher/version.json` |

This is **not** a full game dump and **not** the mod archives. It is the installer/UI that:

- gear **Settings** (RU/EN, repair, VR add/remove, bug report, uninstall)
- shows a Steam-style **game rail** (multiple titles: Re-Dovah, Flappy Standard, Doom)
- reads each game’s `index.json` from the CDN (or a local torrent bundle)
- downloads/verifies multi-package `.7z` units with download speed & ETA indicators
- bundles embedded **7-Zip Extra** (`7za.exe`, `7za.dll`, `7zxa.dll`)
- atomic extraction and instant resume support
- supports **AE only** vs **AE+VR** where a title supports it
- Update / Repair by fingerprint
- Play via Mod Organizer 2 custom executables
- launcher self-update (`launcher/version.json`)

Built on ideas from Universal Game Launcher (Teemu Sillanpää, MIT), heavily adapted for Flappy.

## Player FAQ

| Question | Answer |
|----------|--------|
| Why is the source public? | So you can audit what the exe does (network, files, elevation). |
| Does this include the full modpack? | **No.** Packages live on the CDN / torrent, not in this repo. |
| Can I build it myself? | Yes — see below. Prefer official builds when available. |
| Does it phone home? | It talks to the configured CDN (`cdn.flappy.su` by default) for index/packages/optional self-update and opens community links you click. |

## Requirements (build)

- Windows 10/11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build

1. Open `FlappyReDovahLauncher.sln` in Visual Studio or build via CLI:
```powershell
dotnet publish FlappyReDovahLauncher/FlappyReDovahLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Or 1-click build and package:
```powershell
.\Publish-Launcher.bat
```
(or `Publish-Launcher.ps1 -Version 0.2.0`)

### Ship folder (official package)

```
Flappy Launcher.exe
Flappy Launcher.exe.config
```

Logos and splashes are **embedded**. 7-Zip tools are **downloaded** on first install/repair into `%LocalAppData%\FlappyLauncher\tools\7zip\`.

Do **not** ship `bin/`, `obj/`, or game package trees in this git repo.

## Assets (edit → rebuild)

Splashes (`FlappyReDovahLauncher/Resources/`):

| File | Use |
|------|-----|
| `bg_re_dovah.png` | Re-Dovah splash |
| `bg_flappy_400.png` | Flappy 4.0.0 poster / splash |

Rail logos (`FlappyReDovahLauncher/Assets/`):

| File | Use |
|------|-----|
| `logo_re_dovah.png` | Re-Dovah rail icon |
| `logo_flappy_400.png` | Flappy 4.0.0 rail icon (shared) |
| `discord.png` / `boosty.png` | Social buttons |

Prefer logos as **PNG 128×128 or 256×256**, square, transparent background.

## Configuration

Main knobs: `FlappyReDovahLauncher/Constants.cs`

- Product name / exe / package names  
- `CDN_PACKAGES_BASE_URL` (default `https://cdn.flappy.su/`)  
- Discord / Boosty links  
- `DOWNLOAD_PARALLELISM` (fixed **2**, not user-configurable)  

Games are registered in `GameCatalog.cs` (id, CDN folder, install folder, VR flag, splash/logo resources).

## CDN layout (not this repo)

```
https://cdn.flappy.su/
  index.json                    # Re-Dovah (CDN root today)
  packages/...
  flappy/ ...                   # Flappy 4.0.0 tree (when published)
  launcher/version.json
  launcher/Flappy-Launcher.zip
```

## Tools (maintainers)

| File | Purpose |
|------|---------|
| `Publish-Launcher.bat` | 1-click build Release zip + version.json and copy to CDN |
| `Publish-Launcher.ps1` | Core PowerShell build and packaging automation |

Game packs are built on the server builder (OliveTin Web UI), not locally.
Sync scripts:
- `sync-push.bat` / `sync-push.sh`: push mods to builder
- `sync-pull.bat` / `sync-pull.sh`: pull changes from builder

## Security / privacy

- **No real server passwords** belong in this repository.  
- Online install needs network access to the CDN.  
- First Skyrim path setup may request elevation once (registry `Installed Path`).  
- Logs: `launcher.log` next to the exe (local unless you send them).

## License

See [LICENSE](LICENSE). Original Universal Game Launcher MIT notice is preserved for derived portions.

## Disclaimer

Provided as-is for the Flappy community. Modding can break saves, frames, and friendships. You accept responsibility for what you install. The NSFW warning at the top was only *half* joking.
