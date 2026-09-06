param([string]$ProfileManifest, [string]$Position = '3,3')
$ErrorActionPreference = 'Stop'
$plugin = Join-Path $env:APPDATA 'Elgato/StreamDeck/Plugins/com.anthropic.claude-usage.sdPlugin'
if (-not (Test-Path -LiteralPath (Join-Path $plugin 'manifest.json'))) { throw 'Install the Claude Usage Stream Deck plugin first.' }
if (Get-Process StreamDeck -ErrorAction SilentlyContinue) { throw 'Quit Stream Deck before installing the action.' }
$utf8 = [Text.UTF8Encoding]::new($false)
foreach ($file in @('plugin.js', 'manifest.json')) {
    $target = Join-Path $plugin $file
    if (-not (Test-Path -LiteralPath "$target.before-codex")) { Copy-Item -LiteralPath $target -Destination "$target.before-codex" }
}
foreach ($file in @('codex-usage.js', 'codex-display.mjs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $plugin 'actions') }
$entry = Join-Path $plugin 'plugin.js'
$source = [IO.File]::ReadAllText($entry)
if (-not $source.Contains('CodexUsageAction')) {
    $source = "import { CodexUsageAction } from './actions/codex-usage.js';`n" + $source.Replace('streamDeck.connect();', "streamDeck.actions.registerAction(new CodexUsageAction());`nstreamDeck.connect();")
    [IO.File]::WriteAllText($entry, $source, $utf8)
}
$manifestPath = Join-Path $plugin 'manifest.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if (-not ($manifest.Actions | Where-Object UUID -eq 'com.anthropic.claude-usage.codex')) {
    $manifest.Actions += [pscustomobject]@{
        Name = 'Codex Usage'; UUID = 'com.anthropic.claude-usage.codex'; Icon = 'imgs/actions/usage/codex'
        Tooltip = 'Codex / ChatGPT allowance from Codenotch. Press to cycle usage windows.'
        Controllers = @('Keypad'); States = @(@{ Image = 'imgs/actions/usage/codex' })
    }
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 100), $utf8)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'codex.svg') -Destination (Join-Path $plugin 'imgs/actions/usage/codex.svg')
if ($ProfileManifest) {
    $profile = Get-Content -Raw -LiteralPath $ProfileManifest | ConvertFrom-Json
    $controller = $profile.Controllers | Where-Object Type -eq 'Keypad' | Select-Object -First 1
    if (-not $controller) { throw 'No keypad controller in the selected profile.' }
    $existing = $controller.Actions.PSObject.Properties[$Position]
    if ($existing -and $existing.Value.UUID -ne 'com.anthropic.claude-usage.codex') { throw "Key $Position is occupied; nothing was replaced." }
    if (-not $existing) {
        if (-not (Test-Path -LiteralPath "$ProfileManifest.before-codex")) { Copy-Item -LiteralPath $ProfileManifest -Destination "$ProfileManifest.before-codex" }
        $action = [pscustomobject]@{
            ActionID = [guid]::NewGuid().ToString(); LinkedTitle = $true; Name = 'Codex Usage'
            Plugin = @{ Name = $manifest.Name; UUID = $manifest.UUID; Version = $manifest.Version }
            Resources = $null; Settings = @{}; State = 0; States = @(@{ ShowTitle = $false }); UUID = 'com.anthropic.claude-usage.codex'
        }
        $controller.Actions | Add-Member -NotePropertyName $Position -NotePropertyValue $action
        [IO.File]::WriteAllText($ProfileManifest, ($profile | ConvertTo-Json -Depth 100), $utf8)
    }
    Write-Output "Codex Usage placed at $Position. Existing keys preserved; profile backup saved."
}
Write-Output 'Codex action installed. Start Stream Deck and keep Codenotch running for updates.'
