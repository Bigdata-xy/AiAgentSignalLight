param(
    [Parameter(Mandatory=$true)]
    [string]$EventName
)

$toolRoot = if ($env:SIGNAL_LIGHT_HOME) { $env:SIGNAL_LIGHT_HOME } else { Split-Path -Parent $PSScriptRoot }
$signalRoot = if ($env:SIGNAL_LIGHT_ROOT) { $env:SIGNAL_LIGHT_ROOT } else { Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "SignalLight" }
$diagnosticsDirectory = Join-Path $signalRoot "diagnostics"
$diagnosticsPath = Join-Path $diagnosticsDirectory "latest-hook-context.json"
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
$payloadParseError = ""
if (-not [string]::IsNullOrWhiteSpace($stdin)) {
    try {
        $payload = $stdin | ConvertFrom-Json
    } catch {
        $payloadParseError = $_.Exception.Message
        $payload = $null
    }
}

$session = if ($env:SIGNAL_LIGHT_SESSION_ID) { $env:SIGNAL_LIGHT_SESSION_ID } elseif ($payload.session_id) { $payload.session_id } elseif ($payload.conversation_id) { $payload.conversation_id } else { "" }
$workspace = if ($env:SIGNAL_LIGHT_WORKSPACE) { $env:SIGNAL_LIGHT_WORKSPACE } elseif ($payload.cwd) { $payload.cwd } else { "" }
$title = if ($env:SIGNAL_LIGHT_TITLE) { $env:SIGNAL_LIGHT_TITLE } elseif ($payload.prompt) { $payload.prompt } else { "Codex" }

try {
    New-Item -ItemType Directory -Force -Path $diagnosticsDirectory | Out-Null
    $diagnostics = [pscustomobject]@{
        schemaVersion = 1
        receivedAt = [DateTimeOffset]::Now.ToString("o")
        eventName = $EventName
        mappedState = $state
        toolRoot = $toolRoot
        signalRoot = $signalRoot
        agentPath = if ($agent) { [string]$agent } else { "" }
        agentFound = [bool]$agent
        session = $session
        workspace = $workspace
        title = $title
        payloadParseError = $payloadParseError
        rawPayload = $stdin
    }
    $diagnosticsTempPath = "$diagnosticsPath.tmp"
    $diagnostics | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $diagnosticsTempPath -Encoding UTF8
    Move-Item -LiteralPath $diagnosticsTempPath -Destination $diagnosticsPath -Force
} catch {
}

if ($agent) {
    $arguments = @("emit", "--state", $state, "--source", "codex-cli", "--adapter", "codex-hooks", "--title", $title)
    if (-not [string]::IsNullOrWhiteSpace($session)) {
        $arguments += @("--session", $session)
    }
    if (-not [string]::IsNullOrWhiteSpace($workspace)) {
        $arguments += @("--workspace", $workspace)
    }
    if (-not [string]::IsNullOrWhiteSpace($signalRoot)) {
        $arguments += @("--root", $signalRoot)
    }

    & $agent @arguments
    exit $LASTEXITCODE
}

exit 0
