# ⚠️ NSFW / content warning (please read, then laugh, then read again)

Running this launcher **may** cause:

- mild-to-severe **discomfort**
- sudden **aversion to vanilla Skyrim**
- **nausea** (disk space graphs, progress bars, or the modlist)
- an unexplained urge to install *just one more* optional separator
- social consequences if someone walks in while the splash art is full-screen

This is a **Skyrim AE/VR modpack launcher**. The pack can include adult-oriented content depending on what the maintainers ship.  
If that is not your cup of mead: **close the window, go outside, touch grass, respect your limits.**  
If it is: welcome. Stay hydrated. Use Repair if reality desyncs.

---

# Flappy Launcher

Source for the **Flappy Re-Dovah** desktop launcher — published mainly for **transparency**: so players can see what runs on their PC when they install or update the modpack.

This is **not** a full game dump and **not** the mod archives themselves. It is the installer/UI that:

- reads `index.json` from the CDN (or a local torrent bundle)
- downloads/verifies multi-package `.7z` units
- extracts with `7za`
- supports **AE only** vs **AE+VR** channels
- Update / Repair by fingerprint
- Play via Mod Organizer 2 custom executables
- optional launcher self-update (`launcher/version.json`)

Built on ideas from Universal Game Launcher (Teemu Sillanpää, MIT), heavily adapted for Flappy Re-Dovah.

## Player FAQ

| Question | Answer |
|----------|--------|
| Why is the source public? | So you can audit what the exe does (network, files, registry). |
| Does this include the full modpack? | **No.** Packages live on the CDN / torrent, not in this repo. |
| Can I build it myself? | Yes — see below. Prefer official builds from the team when available. |
| Does it phone home? | It talks to the configured CDN (`cdn.flappy.su` by default) for index/packages/optional self-update and loads linked community URLs you click. |

## Requirements (build)

- Windows 10/11  
- Visual Studio with .NET desktop workload  
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)  
- `7za.exe` (x64) beside the built exe for extraction  

## Build

1. Open `FlappyReDovahLauncher.sln`
2. Build **Release**
3. Output: `FlappyReDovahLauncher\bin\Release\Flappy Re-Dovah.exe`
4. Copy next to the exe: `7za.exe` (+ `7za.dll` / `7zxa.dll` if needed)

### Ship folder

```text
Flappy Re-Dovah.exe
Flappy Re-Dovah.exe.config
7za.exe
7za.dll
7zxa.dll
```

Do **not** ship `bin/`, `obj/`, or game package trees in this git repo.

## Configuration

Main knobs: `FlappyReDovahLauncher/Constants.cs`

- `CDN_PACKAGES_BASE_URL` / package base (default `https://cdn.flappy.su/`)
- Discord / Boosty links
- Parallel download count, chunk size
- Local mode: if `index.json` + `packages\` sit next to the exe → offline/torrent unpack (no CDN)

## CDN layout (packages, not this repo)

```text
https://cdn.flappy.su/
  index.json
  version.txt
  packages/mods/*.7z
  packages/core/*.7z
  launcher/version.json              # optional self-update
  launcher/Flappy-Re-Dovah-Launcher.zip
```

## Tools (maintainers)

| File | Purpose |
|------|---------|
| `Publish-Launcher.ps1` | Build Release zip + `version.json` for self-update |
| `Upload-Launcher-CDN.bat` | Upload launcher package (edit **CHANGE ME** secrets first) |

Upload script ships with **placeholder** host/password only. Put real credentials in a private local copy (e.g. `*.local.bat`, gitignored) or use SSH keys.

## Security / privacy notes

- No real server passwords belong in this repository.  
- The running launcher needs network access to the CDN for online install/update.  
- On first Skyrim path setup it may request elevation once (registry `Installed Path`).  
- Logs: `launcher.log` next to the exe (local only unless you send them).

## License

See [LICENSE](LICENSE). Original Universal Game Launcher MIT notice is preserved for derived portions.

## Disclaimer

Provided as-is for the Flappy Re-Dovah community. Modding can break saves, frames, and friendships. You accept responsibility for what you install. The NSFW warning at the top was only *half* joking.
