param(
    [string]$ShortcutName = "SignalLight Remote 172.16.11.106"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repoRoot "start-remote-signal-my-172.16.11.106.cmd"
if (-not (Test-Path -LiteralPath $target)) {
    throw "Shortcut target was not found: $target"
}

$desktop = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktop "$ShortcutName.lnk"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $target
$shortcut.WorkingDirectory = $repoRoot
$shortcut.WindowStyle = 1
$shortcut.Description = "Start SignalLight, configure remote hooks, and open the SSH reverse tunnel for 172.16.11.106."
$shortcut.IconLocation = "$env:SystemRoot\System32\shell32.dll,167"
$shortcut.Save()

Write-Host "Shortcut created: $shortcutPath"
