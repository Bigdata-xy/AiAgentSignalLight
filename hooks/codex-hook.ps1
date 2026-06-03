param(
    [Parameter(Mandatory=$true)]
    [string]$EventName
)

$toolRoot = if ($env:SIGNAL_LIGHT_HOME) { $env:SIGNAL_LIGHT_HOME } else { Split-Path -Parent $PSScriptRoot }
$agentCandidates = @(
    (Join-Path $toolRoot "SignalLight.Agent.exe"),
    (Join-Path $toolRoot "agent\SignalLight.Agent.exe"),
    (Join-Path $toolRoot "src\SignalLight.Agent\bin\Debug\net8.0\SignalLight.Agent.exe"),
    (Join-Path $toolRoot "src\SignalLight.Agent\bin\Release\net8.0\SignalLight.Agent.exe")
)
$agent = $agentCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

$state = switch ($EventName) {
    "UserPromptSubmit" { "running" }
    "PermissionRequest" { "waiting" }
    "Stop" { "completed" }
    "SessionStart" { "completed" }
    default { "unknown" }
}

$stdin = [Console]::In.ReadToEnd()
$payload = $null
if (-not [string]::IsNullOrWhiteSpace($stdin)) {
    try {
        $payload = $stdin | ConvertFrom-Json
    } catch {
        $payload = $null
    }
}

$session = if ($env:SIGNAL_LIGHT_SESSION_ID) { $env:SIGNAL_LIGHT_SESSION_ID } elseif ($payload.session_id) { $payload.session_id } elseif ($payload.conversation_id) { $payload.conversation_id } else { "" }
$workspace = if ($env:SIGNAL_LIGHT_WORKSPACE) { $env:SIGNAL_LIGHT_WORKSPACE } elseif ($payload.cwd) { $payload.cwd } else { "" }
$title = if ($env:SIGNAL_LIGHT_TITLE) { $env:SIGNAL_LIGHT_TITLE } elseif ($payload.prompt) { $payload.prompt } else { "Codex" }

if ($agent) {
    $arguments = @("emit", "--state", $state, "--source", "codex-cli", "--adapter", "codex-hooks", "--title", $title)
    if (-not [string]::IsNullOrWhiteSpace($session)) {
        $arguments += @("--session", $session)
    }
    if (-not [string]::IsNullOrWhiteSpace($workspace)) {
        $arguments += @("--workspace", $workspace)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:SIGNAL_LIGHT_ROOT)) {
        $arguments += @("--root", $env:SIGNAL_LIGHT_ROOT)
    }

    & $agent @arguments
    exit $LASTEXITCODE
}

exit 0
