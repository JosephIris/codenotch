$ErrorActionPreference = 'Stop'
$plugin = Join-Path $env:APPDATA 'Elgato/StreamDeck/Plugins/com.anthropic.claude-usage.sdPlugin'
if (-not (Test-Path -LiteralPath (Join-Path $plugin 'manifest.json'))) { throw 'Claude Usage Stream Deck plugin is not installed.' }
$actions = Join-Path $plugin 'actions'
$backup = Join-Path $actions 'claude-api.js.before-codenotch'
if (-not (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath (Join-Path $actions 'claude-api.js') -Destination $backup }
# Preserve any existing Codenotch rate-limit deadline when installing the collector.
$cacheDirectory = Join-Path $env:LOCALAPPDATA 'ClaudeUsage'
$cache = Join-Path $cacheDirectory 'streamdeck.json'
if (-not (Test-Path -LiteralPath $cache)) {
    $retryAt = 0
    $archive = Join-Path $env:LOCALAPPDATA 'Codenotch/usage.json'
    if (Test-Path -LiteralPath $archive) {
        $saved = Get-Content -Raw -LiteralPath $archive | ConvertFrom-Json
        if ($saved.RetryAfter.claude) { $retryAt = ([DateTimeOffset]::Parse($saved.RetryAfter.claude)).ToUnixTimeMilliseconds() }
    }
    New-Item -ItemType Directory -Force -Path $cacheDirectory | Out-Null
    [IO.File]::WriteAllText($cache, (@{ version = 1; retryAt = $retryAt } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'collector.mjs') -Destination $actions
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'claude-api.js') -Destination $actions
'{"version":1}' | Set-Content -LiteralPath (Join-Path $actions 'codenotch-bridge.json') -Encoding utf8
Write-Output 'Installed shared Claude collector. Original API module backed up beside it.'
