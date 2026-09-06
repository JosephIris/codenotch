# Install Codenotch and the Stream Deck counters on another Windows PC

## Codenotch

1. Sign into GitHub and open [the Windows builds](https://github.com/JosephIris/codenotch/actions/workflows/windows.yml). Choose the latest successful `windows-port` run and download the **Codenotch-Windows** artifact.
2. Extract the artifact, then extract `Codenotch-win-x64.zip` for an Intel/AMD PC, or `Codenotch-win-arm64.zip` for a Windows ARM PC. Keep the resulting folder somewhere permanent. The packaged app includes its .NET runtime.
3. Run `Codenotch.exe`. Integrations opens on first launch. Install and sign into Claude Code and Codex on this PC, then click **Check connection**. Use your ChatGPT account for Codex subscription limits. Other providers are optional.
4. Choose your display and edge in Appearance. Hover over the edge handle to expand it, then hover over a usage ring for the detailed caps and reset times. The settings orb and tray icon reopen Integrations.
5. To start automatically, press Win+R, enter `shell:startup`, and put a shortcut to your extracted `Codenotch.exe` in that folder.

## Transfer the Stream Deck plugin

The existing Claude plugin is custom, so transfer its installed folder from this PC:

`%APPDATA%\Elgato\StreamDeck\Plugins\com.anthropic.claude-usage.sdPlugin`

1. Quit Stream Deck on both PCs before copying. Copy that complete plugin folder, including `actions`, `imgs`, `node_modules`, `plugin.js`, `package.json`, and `manifest.json`, to the same Plugins location on the new PC. You can omit `logs` and `*.before-*` backups. Restart Stream Deck on both PCs.
2. In Stream Deck's action list, find **Claude Usage**. Drag **5h Session**, **Weekly Usage**, and **Codex Usage** to adjacent empty keys. No existing keys need to be replaced.
3. Keep Stream Deck running for the Claude collector, and Codenotch running for Codex updates. The Codex counter uses a mint gauge showing percentage **used**, the allowance window, and reset countdown. Press it to cycle through windows when your account reports multiple limits.

If updating an older copy of the custom plugin instead, download [the windows-port source](https://github.com/JosephIris/codenotch/tree/windows-port), quit Stream Deck, and run these commands from the repository folder in PowerShell:

```powershell
./Windows/streamdeck/install.ps1
./Windows/streamdeck/install-codex.ps1
```

Then reopen Stream Deck and add **Codex Usage** to an empty key. Both installers preserve backups of the files they replace.

## How the usage connections work

- Claude: one collector inside Stream Deck shares requests between both Claude keys, caches successful responses for five minutes, and persists cooldowns. Codenotch reads that local cache without additional Claude requests.
- Codex: Codenotch gets the subscription allowance through the signed-in Codex app-server interface. The Stream Deck key reads Codenotch's existing cache every 15 seconds, making no additional OpenAI requests. This is the Codex/ChatGPT allowance, not an OpenAI API billing budget.
- Keep each PC's sign-in local. Do not copy `.claude/.credentials.json`, `.codex/auth.json`, or cached usage from the first PC. The plugin does not contain those credentials.
- An old reading is marked **STALE**; an unavailable one is not shown as zero. Reopen the owning app and check Integrations. Rate limits can still happen: the collector waits for the retry deadline instead of retrying repeatedly.
- These caches are shared within each PC, not between PCs. Each running Stream Deck installation has its own collector.

Source and ongoing updates: [JosephIris/codenotch, windows-port](https://github.com/JosephIris/codenotch/tree/windows-port).
