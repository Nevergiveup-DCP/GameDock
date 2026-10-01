# Changelog

## 1.1 — 2026-10-02

Added

- `--stats` — writes `stats_report.txt`: total playtime, games played vs never launched,
  the split across platforms, and the top ten by hours. Useful for answering "what did I
  actually play this year" without opening the GUI.
- `--export-csv` — writes `library_export.csv` with every field GameDock stores
  (name, platform, status, seconds, hours, launch count, added, last played, Steam appid,
  exe path, progress note). UTF-8 with BOM, so Excel on a Chinese Windows opens the titles
  correctly, and fields containing commas or quotes are escaped.
- Both are documented in README.md / README.zh-CN.md under "Command line".

Not changed

- The GUI, the scanner, and the recommendation scoring are untouched by this release.
  If a scan already worked for you, it still works the same way.

## 1.0 — 2026-09-29

- First release: Steam / Epic / folder scanning, playtime tracking with import from
  Steam's `localconfig.vdf`, "what to play tonight" scoring, time budget, learning from
  your picks, progress notes, cover art, console-app detection, and automatic backups
  with an HMAC signature tied to the machine.
