<div align="center">

# GameDock

**Steam tells you what you own. GameDock tells you what you play tonight.**

A Windows desktop game library manager built around one question other launchers ignore.

[English](README.md) | [简体中文](README.zh-CN.md)

![GameDock demo](docs/demo.gif)

</div>

---

## What's new in 1.1

A library you can't get out of is a library you don't own. Two headless commands were added
for exactly that reason:

- `GameDock.exe --stats` writes `%APPDATA%\GameDock\stats_report.txt` — total playtime, the
  split across platforms, and your top ten games by hours
- `GameDock.exe --export-csv` writes `%APPDATA%\GameDock\library_export.csv` — the whole
  library as a spreadsheet, UTF-8 with BOM so Excel opens Chinese titles correctly

Full history: [CHANGELOG.md](CHANGELOG.md)

## The problem

My Steam library has 200+ games. About once a week I would sit down, scroll through the
whole list, and end up closing the computer and scrolling my phone instead.

It was never a lack of games. It was a **lack of a decision**.

GameDock only tries to fix that.

## Features

- **Aggregates your libraries** — scans Steam (including library folders on every drive),
  Epic, and any folder you point it at, into one list
- **Tracks playtime** across all of them, and **imports the playtime Steam already
  recorded for you** (read from `localconfig.vdf`). A game you played 88 minutes on
  Steam won't show up here as 6 seconds.
- **"What to play tonight"** — scores every game by playtime, status, and how long it has
  been idle, then returns a short list
- **Time budget** — tell it "I only have 30 minutes" and it drops the candidates whose
  own session history says you usually play them for two hours. The estimate comes from
  your own sessions, not from an online playtime database
- **It learns from you** — every candidate has "就它了 / 换一个". What you pick gets
  weighted up, what you skip gets weighted down. The scoring stays a rule you can read,
  but its inputs change with your choices
- **Progress notes** — write down where you stopped (*"chapter 3, just beat the boss,
  sneak is Ctrl"*). Launch a game you haven't touched in a while and GameDock shows it
  back to you before starting
- **Cover art** — set your own image, or fetch the official Steam header image
- **Console app detection** — some "games" are console programs. GameDock reads the PE
  subsystem and can hide the console window on launch
- **Your data is protected** — a backup is written before every change (last 30 kept),
  you can take a permanent snapshot that is never auto-deleted, and an HMAC-SHA256
  signature tied to this machine warns you if the file was modified from outside.
  Playtime you accumulated is not something you can buy back.

### How the scoring works

There is no machine learning here, on purpose. The rule is written down and you can argue with it:

```
status: playing +60 / shelved +25 / unclassified +15
just started (under 3h)      +20
heavily played (over 60h)    +10
idle over 30 days            +18
idle 7-30 days               +10
played within 24 hours       -25
plus a small random jitter
```

The top three become the short list. A recommendation you can't reproduce is a
recommendation you stop trusting by the second day — so this one is deterministic.

## How this differs from Playnite

[Playnite](https://playnite.link) is excellent and does far more than this project:
libraries, themes, extensions, emulators, metadata providers.
**If you want a complete library manager, go use Playnite.**

GameDock deliberately does less. It only adds the two layers Playnite doesn't have:

| Layer | What it means |
| --- | --- |
| **Decision** | Answers "what should I play tonight", not "what do I own" |
| **Memory** | Progress notes that resurface when you return to a shelved game |

## The interface (it is in Chinese)

The UI is Chinese-only right now — localizing it properly means re-checking the hand-drawn
layout for every string, and there are ~290 of them. So instead, here is a labelled map:

![Annotated GameDock interface](docs/ui-annotated.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | 自动扫描 | Auto-scan Steam (every library drive) and Epic |
| 2 | 扫文件夹 | Scan a folder you pick |
| 3 | 手动添加 | Add one `.exe` by hand |
| 4 | 刷新 | Re-check whether the running game has exited |
| 5 | 同步星港 | Sync the games published in Starport |
| 6 | 批量封面 | Batch-fetch official Steam cover art |
| 7 | 加速器 | Launch a game accelerator before the game |
| 8 | 数据 | Backups, permanent snapshots, integrity check |
| 9 | (search box) | Filter the library by name or platform |
| 10 | 今晚玩什么？ | The recommendation engine |
| 11 | (game list) | Click a row to select |
| 12 | (details panel) | Cover, playtime, progress note, launch path |
| 13 | 启 动 游 戏 | Start the selected game |
| 14 | 在玩 / 搁置 / 通关 / 弃坑 | Set the game's status |
| 15 | 我停在哪 | Write down where you stopped |

Inside the recommendation panel you'll also find a time-budget row
(`不限 / 30 分 / 1 小时 / 2 小时`) and, on each candidate, `就它了` (take it) and
`换一个` (skip this one).

## Honest limitations

- **Windows only**
- **The UI is Chinese.** Localization is not implemented yet — see Roadmap
- The interface is drawn by hand with GDI+, no UI framework. It looks unusual, and it is
  not DPI-perfect on every display
- **No installer.** Download the zip, or build it yourself
- Scanning a Steam library on a slow removable drive can take 20+ seconds. It runs on a
  background thread so the window never freezes, but it is slow
- Steam achievements are **not** read yet, so a game's progress can only be a text note

## Download

Get the latest zip from [Releases](../../releases). Unzip anywhere and run `GameDock.exe`.
No installation, no administrator rights.

## Command line

GameDock is a GUI app, but these work without opening a window — handy for scripts, and for
checking that a scan actually found what you expected:

| Command | What it writes |
|---|---|
| `GameDock.exe --scan-folder "D:\Games"` | `scan_report.txt` — what a scan of that folder found |
| `GameDock.exe --list` | `list_report.txt` — every game, platform, status and playtime |
| `GameDock.exe --launch "Hollow Knight"` | launches that game by name (no window) |
| `GameDock.exe --stats` | `stats_report.txt` — totals, per-platform split, top ten |
| `GameDock.exe --export-csv` | `library_export.csv` — the full library as a spreadsheet |

Everything is written to `%APPDATA%\GameDock`.

## Build

Requires .NET Framework 4.x (ships with Windows). No Visual Studio, no NuGet,
no third-party dependencies.

```
build.bat
```

That produces `GameDock.exe`.

## Where your data lives

Everything is under `%APPDATA%\GameDock`. **Nothing is uploaded anywhere.**

The only time the app touches the network is when you click "从 Steam 获取" to download
a cover image, or when you explicitly ask it to check your Steam library.

## Roadmap

- [ ] Localization — extract UI strings, then accept community translations
- [x] Time-budget recommendations — "I only have 90 minutes tonight"
- [x] Feedback loop so the scoring learns from what you actually play
- [ ] Read Steam achievements to turn progress notes into real progress percentages
- [ ] Installer

## License

[MIT](LICENSE)
