param(
    [Alias("EventName")]
    [string]$Event = ""
)

$ErrorActionPreference = "Continue"

try {
    $envFile = Join-Path $HOME ".signal-light\env.ps1"
    if (Test-Path -LiteralPath $envFile) {
        . $envFile
    }

    $rawPayload = [Console]::In.ReadToEnd()
    $bridgeUrl = if ($env:SIGNAL_LIGHT_BRIDGE_URL) { $env:SIGNAL_LIGHT_BRIDGE_URL } else { "http://127.0.0.1:37631/api/events" }
    $token = $env:SIGNAL_LIGHT_BRIDGE_TOKEN
    $remoteHost = if ($env:SIGNAL_LIGHT_REMOTE_HOST) { $env:SIGNAL_LIGHT_REMOTE_HOST } else { [System.Net.Dns]::GetHostName() }
    $remoteUser = if ($env:SIGNAL_LIGHT_REMOTE_USER) { $env:SIGNAL_LIGHT_REMOTE_USER } else { [Environment]::UserName }
    $sessionId = if ($env:CODEX_SESSION_ID) { $env:CODEX_SESSION_ID } elseif ($env:CODEX_CONVERSATION_ID) { $env:CODEX_CONVERSATION_ID } else { [string]$PID }
    $title = ""

    try {
        if (-not [string]::IsNullOrWhiteSpace($rawPayload)) {
            $payloadObject = $rawPayload | ConvertFrom-Json -ErrorAction Stop
            foreach ($name in @("prompt", "user_prompt", "userPrompt", "message", "text", "input")) {
                if ($payloadObject.PSObject.Properties.Name -contains $name) {
                    $value = [string]$payloadObject.$name
                    if (-not [string]::IsNullOrWhiteSpace($value) -and $value -notin @("true", "false")) {
                        $title = $value
                        break
                    }
                }
            }

            foreach ($name in @("session_id", "sessionId", "session", "conversation_id", "conversationId", "conversation", "codex_session_id", "codexSessionId", "thread_id", "threadId", "terminal_id", "terminalId", "terminal", "process_id", "processId", "pid")) {
                if ($payloadObject.PSObject.Properties.Name -contains $name) {
                    $value = [string]$payloadObject.$name
                    if (-not [string]::IsNullOrWhiteSpace($value) -and $value -notin @("true", "false")) {
                        $sessionId = $value
                        break
                    }
                }
            }
        }
    } catch {
    }

    if ([string]::IsNullOrWhiteSpace($token)) {
        $diag = Join-Path $HOME ".signal-light\diagnostics"
        New-Item -ItemType Directory -Force -Path $diag | Out-Null
        "SIGNAL_LIGHT_BRIDGE_TOKEN is not set." | Set-Content -LiteralPath (Join-Path $diag "latest-remote-hook-error.txt") -Encoding UTF8
        exit 0
    }

    $body = [pscustomobject]@{
        schemaVersion = 1
        codexEvent = $Event
        source = "remote-ssh"
        adapter = "codex-remote-hooks"
        remoteHost = $remoteHost
        remoteUser = $remoteUser
        workspace = (Get-Location).Path
        sessionId = $sessionId
        title = $title
        payload = @{
            codexEvent = $Event
            rawPayload = $rawPayload
        }
    } | ConvertTo-Json -Depth 8

    Invoke-RestMethod -Method Post -Uri $bridgeUrl -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body $body | Out-Null
} catch {
    try {
        $diag = Join-Path $HOME ".signal-light\diagnostics"
        New-Item -ItemType Directory -Force -Path $diag | Out-Null
        $_ | Out-String | Set-Content -LiteralPath (Join-Path $diag "latest-remote-hook-error.txt") -Encoding UTF8
    } catch {
    }
}

exit 0
