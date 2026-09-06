# Codenotch for Windows

A native .NET 8 / WPF port of [vinzdg/codenotch](https://github.com/vinzdg/codenotch), with no third-party runtime packages. Windows 10/11. The original macOS app remains in `Sources/`.

## Run

With the .NET 8 SDK installed, from the repository root:

```powershell
./Windows/build.ps1 demo     # sample readings; no credentials, network, or saved settings
./Windows/build.ps1 run      # real usage
```

First launch opens **Integrations**, showing which tools are connected, missing a sign-in, unreachable, or rate-limited. **Connect** checks for an existing session and opens the owning tool's sign-in or installation guide when needed. Claude Code uses `claude auth login`; Codex uses `codex login`; Cursor opens its editor. Return to Codenotch after signing in, or click **Check connection**. No passwords or API keys are entered into Codenotch.

The notch rests as a small black handle at the screen edge. Hover to unfold it, then hover a provider for its usage card. Move into the card without it disappearing. Click a connected ring to open its usage page, or click the animated orb below the notch for settings. The tray menu also opens settings. Starting the app again brings its settings window forward.

The Windows UI now ports the upstream glyph outlines, inverse bezel corners, 44-point rings, separate percentage labels, measured spacing, and green/yellow/orange usage bands. The notch unfolds smoothly; ring readings ease to new values; hover cards fade and slide; the orb's arc becomes a rotating gear. Animations respect Windows reduced-motion settings. Choose hover-only or always-visible mode, any monitor, and any of the four working-area edges. The notch and cards do not take keyboard focus.

Demo mode requires an explicit `--demo` flag (or the `demo` build task). It is labeled **DEMO** on the notch, cards, and settings, uses a separate instance identifier, and never reads or writes live account state. The old `CODENOTCH_DEMO` environment variable no longer silently changes the Windows app's mode.

Disconnect stops subsequent credential reads, cancels any request in progress, and discards saved usage. A late response cannot restore a disconnected provider. Credentials are never changed or saved by Codenotch; the owning tools manage their own sign-in. Providers refresh independently every five minutes, with persisted backoff that manual refresh also respects. Rate-limited buttons count down to the next permitted attempt. Network failures keep dated, dimmed readings; rejected authentication clears the previous account's numbers. Providers without readings do not appear as invented percentages or anonymous rings.

## Provider support

| Provider | Windows source |
| --- | --- |
| Claude Code | `%USERPROFILE%/.claude/.credentials.json`, or `CLAUDE_CONFIG_DIR`; additional `.claude-*` profiles discovered at launch. OAuth usage endpoint. Open Claude Code to refresh expired credentials. |
| Codex | Live `codex.exe app-server` when found in PATH, `.codex/bin`, or a PATH-based npm installation. Override with `CODENOTCH_CODEX_EXE` (absolute executable path). Falls back to the newest recorded rate-limit snapshot in local rollouts; snapshots older than five minutes are labeled stale. Respects `CODEX_HOME`. |
| Cursor | `%APPDATA%/Cursor/User/globalStorage/state.vscdb`, read-only with WAL support, then Cursor's usage-summary endpoint. |
| GLM | Claude settings with a Z.ai/BigModel base URL, plaintext ZCode plan credentials, or OpenCode auth under `XDG_DATA_HOME` / `~/.local/share`. Encrypted ZCode tokens are skipped. |
| Antigravity | Not yet supported on Windows. |

These readers reuse the upstream response formats. The internal vendor APIs may change. An absent percentage stays unknown; it is never replaced with zero. Native Windows installations are supported; this build does not discover credentials inside WSL distributions.

Session busy/waiting indicators, Antigravity, automatic updates, and launch at login are still not ported. The small spinner indicates a usage refresh, not an active coding session. Codex live usage was verified on the development PC. Claude's rate-limited response was verified and is surfaced correctly. Cursor and GLM sign-in flows still need validation with installed, signed-in accounts; fixture tests alone do not establish that every tool version works.

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

It renders all four edges, the integrations window, a usage card, a reference scene, and several unfolding animation frames under `Windows/artifacts/smoke-v2/`, then exits automatically. CI runs this check and uploads the preview images separately from the executable packages.

For actual desktop interaction checks:

```powershell
./Windows/ui-smoke.ps1
```

This launches an isolated preview, exercises the settings controls and all four edges, moves the pointer to unfold the notch, opens a provider card, crosses into it, verifies dismissal, and reopens settings through the accessible orb. It restores the pointer and exits the preview afterward. A local screenshot is saved under `Windows/artifacts/interaction/`; it may include nearby desktop content and is not uploaded by CI. Mixed-DPI multi-monitor behavior still needs validation with that hardware.

`--settings` opens the integrations window on launch. The developer-only `--verify-live` switch writes a credential-free connection report and a screenshot of the live integrations window under `Windows/artifacts/live-verification/`. It performs the same authenticated reads as normal live mode and honors saved backoff. These local artifacts are ignored by Git.

Regenerate the glyph asset after an upstream outline change with `node Windows/generate-glyphs.mjs`. This copies the upstream vector coordinates into a WPF-compatible path resource; it does not substitute approximated logos.

The executable, taskbar/settings window, and notification-area icon use the original full-color Codenotch artwork. `node Windows/generate-icons.mjs` packages the upstream 16/32/64/128/256-pixel images into the Windows ICO resource. Live and preview instances have separate, stable taskbar identities.

Licensed under the upstream MIT license; see `../LICENSE`.
