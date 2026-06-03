$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$hookScript = Join-Path $repoRoot "hooks\codex-hook.ps1"
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE ".codex" }
$hooksJson = Join-Path $codexHome "hooks.json"
New-Item -ItemType Directory -Force -Path $codexHome | Out-Null

if (-not (Test-Path -LiteralPath $hookScript)) {
    throw "SignalLight hook script was not found: $hookScript"
}

function Ensure-Property {
    param(
        [Parameter(Mandatory=$true)]
        [object]$Target,
        [Parameter(Mandatory=$true)]
        [string]$Name,
        [object]$Value
    )

    if (-not ($Target.PSObject.Properties.Name -contains $Name)) {
        $Target | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
    }
}

function New-HookCommand {
    param(
        [Parameter(Mandatory=$true)]
        [string]$EventName
    )

    $escapedHookScript = $hookScript.Replace('"', '\"')
    return "powershell -NoProfile -ExecutionPolicy Bypass -File `"$escapedHookScript`" -EventName `"$EventName`""
}

function Remove-OwnedHookEntries {
    param(
        [Parameter(Mandatory=$true)]
        [AllowEmptyCollection()]
        [object[]]$Groups
    )

    $keptGroups = @()
    foreach ($group in $Groups) {
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

    return @($keptGroups)
}

if (Test-Path -LiteralPath $hooksJson) {
    $raw = Get-Content -LiteralPath $hooksJson -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        $config = [pscustomobject]@{ hooks = [pscustomobject]@{} }
    } else {
        try {
            $config = $raw | ConvertFrom-Json
        } catch {
            $backup = "$hooksJson.invalid-$(Get-Date -Format 'yyyyMMddHHmmss').bak"
            Copy-Item -LiteralPath $hooksJson -Destination $backup -Force
            $config = [pscustomobject]@{ hooks = [pscustomobject]@{} }
            Write-Warning "Existing hooks.json was invalid. A backup was written to $backup."
        }
    }
} else {
    $config = [pscustomobject]@{ hooks = [pscustomobject]@{} }
}

Ensure-Property -Target $config -Name "hooks" -Value ([pscustomobject]@{})

$events = @("UserPromptSubmit", "PermissionRequest", "Stop", "SessionStart")
foreach ($event in $events) {
    Ensure-Property -Target $config.hooks -Name $event -Value @()
    $existingGroups = @($config.hooks.$event)
    $groups = @(Remove-OwnedHookEntries -Groups $existingGroups)
    $groups += [pscustomobject]@{
        hooks = @(
            [pscustomobject]@{
                type = "command"
                command = (New-HookCommand -EventName $event)
                statusMessage = "SignalLight: $event"
            }
        )
    }
    $config.hooks.$event = @($groups)
}

if (Test-Path -LiteralPath $hooksJson) {
    Copy-Item -LiteralPath $hooksJson -Destination "$hooksJson.bak" -Force
}

Set-Content -LiteralPath $hooksJson -Value ($config | ConvertTo-Json -Depth 20) -Encoding UTF8

Write-Host "SignalLight hooks installed into $hooksJson"
Write-Host "Run /hooks in Codex and trust the SignalLight hook commands if prompted."
