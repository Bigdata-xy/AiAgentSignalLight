$ErrorActionPreference = "Stop"
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE ".codex" }
$hooksJson = Join-Path $codexHome "hooks.json"

if (-not (Test-Path -LiteralPath $hooksJson)) {
    Write-Host "No Codex hooks.json found at $hooksJson"
    exit 0
}

$config = (Get-Content -LiteralPath $hooksJson -Raw) | ConvertFrom-Json
if (-not $config.hooks) {
    Write-Host "No hooks section found in $hooksJson"
    exit 0
}

$events = @("UserPromptSubmit", "PermissionRequest", "Stop", "SessionStart")
foreach ($event in $events) {
    if (-not ($config.hooks.PSObject.Properties.Name -contains $event)) {
        continue
    }

    $keptGroups = @()
    foreach ($group in @($config.hooks.$event)) {
        if (-not $group.hooks) {
            $keptGroups += $group
            continue
        }

        $keptHooks = @()
        foreach ($hook in @($group.hooks)) {
            $command = [string]$hook.command
            $statusMessage = [string]$hook.statusMessage
            if ($command -like "*codex-hook.ps1*" -or $statusMessage -like "SignalLight:*") {
                continue
            }

            $keptHooks += $hook
        }

        if ($keptHooks.Count -gt 0) {
            $group.hooks = @($keptHooks)
            $keptGroups += $group
        }
    }

    $config.hooks.$event = @($keptGroups)
}

Copy-Item -LiteralPath $hooksJson -Destination "$hooksJson.bak" -Force
Set-Content -LiteralPath $hooksJson -Value ($config | ConvertTo-Json -Depth 20) -Encoding UTF8
Write-Host "SignalLight hooks removed from $hooksJson"
