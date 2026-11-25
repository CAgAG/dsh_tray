# DeepSeek Harness Tray Launcher

**English** | [简体中文](README.zh-CN.md)

A tiny Windows tray app that starts `dsh web` with one double-click, shows its status in the tray, and can open, restart or stop it without ever starting a second instance.

[Download the latest release](https://github.com/CAgAG/dsh_tray/releases/latest)

## Features

- Double-click to start DSH (same as `npx @deepseek-ai/dsh web`, no console window)
- Tray icon shows status: green = running, blue = external instance, yellow = starting/stopping, red = failed, grey = stopped
- Left-click or double-click opens the page using the launch token (avoids the 401 page)
- Right-click menu: open page, copy URL, restart, stop, view logs, edit config, start with Windows, quit
- Single instance; attaches to an already running DSH instead of starting another one
- Quitting only removes the tray icon: DSH keeps running and is adopted on the next launch

## Requirements

- Windows 10/11
- Node.js ≥ 24.9
- At least one successful `npx @deepseek-ai/dsh web` run

## Install and use

1. Run `install.cmd` (or launch `dist\DeepSeekHarnessTray.exe` directly).
2. Double-click "DeepSeek Harness" on the desktop.
3. On first run, config and logs are created in `%LOCALAPPDATA%\DeepSeekHarnessTray`.

## Config and logs

- Config: `%LOCALAPPDATA%\DeepSeekHarnessTray\config.ini` (port, bind address, auto-open, ...)
- UI language: `language=auto` (follow Windows), `en`, or `zh` in `config.ini`
- Logs: `launcher.log` (launcher), `dsh-web.log` (DSH output)
- Stop DSH: tray menu → "Stop DSH", or run `DeepSeekHarnessTray.exe --stop`
- Other commands: `--status`, `--diagnose`, `--start`, `--stop`, `--restart`, `--quit`

## Build from source

Requires the .NET Framework 4.x that ships with Windows (includes `csc.exe`):

```bat
build.cmd
```

Output: `dist\DeepSeekHarnessTray.exe`.

## Release

```bat
.\bump-version.cmd 1.2.0
git add -A
git commit -m "release: v1.2.0"
.\release.cmd 1.2.0 -Push
```

`-Push` pushes the tag; GitHub Actions builds the exe and creates the release. Use `-Manual` to publish directly from the local machine.

## Troubleshooting

- **The shortcut seems to do nothing**: check Task Manager for a leftover `DeepSeekHarnessTray` process; do not run the exe from DSH's terminal or a sandbox.
- **The page asks for re-authentication / 401**: it is an external instance started from a terminal; use tray menu → "Restart DSH" to take it over.
- **Port already in use**: the launcher falls back to a random port; you can also change `port` in `config.ini`.

See [CHANGELOG.md](CHANGELOG.md) for version history.
