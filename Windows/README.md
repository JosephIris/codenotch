# Codenotch for Windows

A native .NET 8 / WPF port of [vinzdg/codenotch](https://github.com/vinzdg/codenotch), with no third-party runtime packages. Windows 10/11. The original macOS app remains in `Sources/`.

## Run

With the .NET 8 SDK installed, from the repository root:

```powershell
./Windows/build.ps1 demo     # sample readings; no credentials, network, or saved settings
./Windows/build.ps1 run      # real usage
```

The notch starts as a small pill on the right edge. Hover to expand, hover a ring for usage windows and reset times, and click the gear or right-click for settings. The tray menu can always reopen the settings. Select any monitor and any of its four working-area edges; bottom placement sits above the taskbar. The overlay does not take keyboard focus. Only one instance runs per Windows session; quit the demo before launching live mode.

Provider switches stop subsequent credential reads and discard saved usage. An already-running request may finish, but its result is discarded after disabling. Credentials are never changed or saved by Codenotch. A five-minute polling interval and persisted rate-limit backoff apply to Refresh now as well. Last successful readings remain dimmed and explicitly stale when a fetch fails.

## Provider support

| Provider | Windows source |
| --- | --- |
| Claude Code | `%USERPROFILE%/.claude/.credentials.json`, or `CLAUDE_CONFIG_DIR`; additional `.claude-*` profiles discovered at launch. OAuth usage endpoint. Open Claude Code to refresh expired credentials. |
| Codex | Live `codex.exe app-server` when found in PATH, `.codex/bin`, or a PATH-based npm installation. Override with `CODENOTCH_CODEX_EXE` (absolute executable path). Falls back to the newest recorded rate-limit snapshot in local rollouts; snapshots older than five minutes are labeled stale. Respects `CODEX_HOME`. |
| Cursor | `%APPDATA%/Cursor/User/globalStorage/state.vscdb`, read-only with WAL support, then Cursor's usage-summary endpoint. |
| GLM | Claude settings with a Z.ai/BigModel base URL, plaintext ZCode plan credentials, or OpenCode auth under `XDG_DATA_HOME` / `~/.local/share`. Encrypted ZCode tokens are skipped. |
| Antigravity | Not yet supported on Windows; explicitly marked in settings. |

These readers reuse the upstream response formats. The internal vendor APIs may change. An absent percentage stays unknown; it is never replaced with zero. Native Windows installations are supported; this build does not discover credentials inside WSL distributions.

This first Windows version does **not** yet port session busy/waiting indicators, Antigravity, automatic updates, or launch at login. It uses drawn rings rather than the macOS provider glyphs. Live provider authentication needs validation with each installed, signed-in tool; parser tests do not establish that every account or tool version works.

Settings, last readings, and retry deadlines live in `%LOCALAPPDATA%/Codenotch/`. This directory contains no tokens. The app does not log HTTP bodies, credentials, or session content.

## Build and package

```powershell
./Windows/build.ps1 build
./Windows/build.ps1 test
./Windows/build.ps1 publish                   # standalone x64 executable + zip
./Windows/build.ps1 publish -Runtime win-arm64
```

Publish output is in `Windows/artifacts/`. Standalone packages include the .NET runtime and do not need an installer or administrator rights. Packages are unsigned. The Windows CI workflow builds, runs fixture tests, and creates x64 and ARM64 artifacts.

For an isolated UI startup smoke test (sample data only):

```powershell
./Windows/Codenotch/bin/Release/net8.0-windows/Codenotch.exe --smoke-test
```

It renders all four edge orientations to PNG files under `Windows/artifacts/smoke/` and exits automatically. Visual checks on multiple monitors and mixed DPI still need a desktop session with that hardware.

Licensed under the upstream MIT license; see `../LICENSE`.
