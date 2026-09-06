param([string]$Executable = (Join-Path $PSScriptRoot 'Codenotch/bin/Release/net8.0-windows/Codenotch.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NotchMouse {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
}
'@
$outputDirectory = Join-Path $PSScriptRoot 'artifacts/interaction'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$previousCursor = [System.Windows.Forms.Cursor]::Position
$previewProcess = Start-Process -FilePath $Executable -ArgumentList '--demo' -PassThru -WindowStyle Hidden
function Find-Window([string]$name) {
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $previewProcess.Id),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name))
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}
function Invoke-Button($window, [string]$name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $button) { throw "Button missing: $name" }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 250
}
try {
    $settings = $null
    for ($attempt = 0; $attempt -lt 40 -and $null -eq $settings; $attempt++) {
        Start-Sleep -Milliseconds 250
        $settings = Find-Window 'Codenotch · Demo preview'
    }
    if ($null -eq $settings) { throw 'First-launch integrations did not open' }
    Invoke-Button $settings 'Appearance'
    foreach ($edge in @('Left', 'Top', 'Bottom', 'Right')) { Invoke-Button $settings $edge }
    Invoke-Button $settings 'On hover'
    Invoke-Button $settings 'Integrations'
    Invoke-Button $settings '✕'
    Start-Sleep -Milliseconds 600
    $notch = Find-Window 'Codenotch · DEMO PREVIEW'
    if ($null -eq $notch) { throw 'Closing settings closed the notch' }
    $bounds = $notch.Current.BoundingRectangle
    $wakeX = [int]($bounds.Right - 3)
    $wakeY = [int]($bounds.Top + ($bounds.Height - 34) / 2)
    [NotchMouse]::SetCursorPos($wakeX, $wakeY) | Out-Null
    Start-Sleep -Milliseconds 550
    $cells = $notch.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $claudeCell = $null
    foreach ($cell in $cells) { if ($cell.Current.Name -like 'Claude Code,*') { $claudeCell = $cell; break } }
    if ($null -eq $claudeCell) { throw 'Provider cell is not accessible after unfolding' }
    $ring = $claudeCell.Current.BoundingRectangle
    [NotchMouse]::SetCursorPos([int]($ring.Left + $ring.Width / 2), [int]($ring.Top + 22)) | Out-Null
    Start-Sleep -Milliseconds 400
    $tooltip = Find-Window 'Codenotch · Claude Code usage'
    Write-Output "Notch bounds: $bounds; provider bounds: $ring"
    $screen = [System.Windows.Forms.Screen]::FromPoint([System.Drawing.Point]::new($wakeX, $wakeY)).WorkingArea
    $captureWidth = [Math]::Min(650, $screen.Width)
    $bitmap = [System.Drawing.Bitmap]::new($captureWidth, $screen.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($screen.Right - $captureWidth, $screen.Top, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path $outputDirectory 'hover-desktop.png'))
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    if ($null -eq $tooltip) { throw 'Hover did not open the usage card' }
    $cardBounds = $tooltip.Current.BoundingRectangle
    [NotchMouse]::SetCursorPos([int]($cardBounds.Left + $cardBounds.Width / 2), [int]($cardBounds.Top + $cardBounds.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 500
    if ($null -eq (Find-Window 'Codenotch · Claude Code usage')) { throw 'Usage card collapsed while the pointer was inside it' }
    [NotchMouse]::SetCursorPos([int]($bounds.Left - 400), [int]($bounds.Bottom + 30)) | Out-Null
    Start-Sleep -Milliseconds 700
    if ($null -ne (Find-Window 'Codenotch · Claude Code usage')) { throw 'Usage card did not dismiss after pointer exit' }
    Invoke-Button $notch 'Open integrations and settings'
    if ($null -eq (Find-Window 'Codenotch · Demo preview')) { throw 'Settings orb failed to reopen integrations' }
    Write-Output 'PASS: setup, appearance controls, four edges, mouse unfold, provider hover, crossing to card, card dismissal, accessible settings orb.'
} finally {
    [NotchMouse]::SetCursorPos($previousCursor.X, $previousCursor.Y) | Out-Null
    if (-not $previewProcess.HasExited) { Stop-Process -Id $previewProcess.Id }
}
