# Flappy Launcher

Multi-game library launcher (**Re-Dovah** + **Flappy 4.0.0**).

| | |
|--|--|
| **Exe** | `Flappy Launcher.exe` |
| **Version** | **0.0.5** |
| **CDN** | `https://cdn.flappy.su/` |
| **Package** | `launcher/Flappy-Launcher.zip` |

## Highlights (0.0.5)

- 3 parallel CDN download workers
- Library: Re-Dovah (live) + Flappy (4.0.0 stub)
- Per-game splash/logos (`bg_re_dovah`, `bg_flappy_400`, …)
- Status text follows selected game

## Build

Open `FlappyReDovahLauncher.sln`, Release build → `bin\Release\Flappy Launcher.exe`

Or: `.\Publish-Launcher.ps1 -Version 0.0.5`

## License

See [LICENSE](LICENSE).
