param(
    [Parameter(Mandatory=$true)]
    [string]$HookScript,

    [Parameter(Mandatory=$true)]
    [string]$TranscriptPath,

    [string]$SessionId = "",
    [string]$Workspace = "",
    [string]$Title = "",
    [string]$CommandText = "",
    [int]$InitialLength = 0,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = "SilentlyContinue"
$pollIntervalMilliseconds = 50
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom
$signalRoot = if ($env:SIGNAL_LIGHT_ROOT) { $env:SIGNAL_LIGHT_ROOT } else { Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "SignalLight" }
$diagnosticsDirectory = Join-Path $signalRoot "diagnostics"
$watchHistoryDirectory = Join-Path $diagnosticsDirectory "permission-watch"
$latestWatchPath = Join-Path $diagnosticsDirectory "latest-permission-watch.json"

function Write-WatchDiagnostics {
    param(
        [string]$Status,
        [string]$Detail = "",
        [int]$Offset = 0,
        [int]$ReadLength = 0
    )

    try {
        New-Item -ItemType Directory -Force -Path $diagnosticsDirectory | Out-Null
        New-Item -ItemType Directory -Force -Path $watchHistoryDirectory | Out-Null
        $value = [pscustomobject]@{
            schemaVersion = 1
            receivedAt = [DateTimeOffset]::Now.ToString("o")
            status = $Status
            detail = $Detail
            hookScript = $HookScript
            transcriptPath = $TranscriptPath
            sessionId = $SessionId
            workspace = $Workspace
            title = $Title
            commandText = $CommandText
            initialLength = $InitialLength
            offset = $Offset
            readLength = $ReadLength
            timeoutSeconds = $TimeoutSeconds
        }
        $json = $value | ConvertTo-Json -Depth 10
        [System.IO.File]::WriteAllText($latestWatchPath, $json, $utf8NoBom)
        $historyPath = Join-Path $watchHistoryDirectory ("{0:yyyyMMddHHmmssfff}-{1}.json" -f ([DateTimeOffset]::Now), $Status)
        [System.IO.File]::WriteAllText($historyPath, $json, $utf8NoBom)
    } catch {
    }
}

function Test-ApprovedText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    $lower = $Text.ToLowerInvariant()
    return $lower.Contains("you approved codex to run") `
        -or $lower.Contains("approved codex to run") `
        -or $lower.Contains('"approved"') `
        -or $lower.Contains('"decision":"approve"') `
        -or $lower.Contains('"status":"approved"')
}

function Test-CancelledText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    $lower = $Text.ToLowerInvariant()
    return $lower.Contains("you canceled the request") `
        -or $lower.Contains("you cancelled the request") `
        -or $lower.Contains("conversation interrupted") `
        -or $lower.Contains("<turn_aborted>") `
        -or $lower.Contains("turn_aborted") `
        -or $lower.Contains("turn aborted") `
        -or $lower.Contains("the user interrupted the previous turn on purpose")
}

function Invoke-SignalHook {
    param(
        [string]$EventName,
        [string]$Resolution
    )

    $payload = [pscustomobject]@{
        session_id = $SessionId
        cwd = $Workspace
        prompt = $Title
        permission_resolution = $Resolution
        tool_response = if ($Resolution -eq "cancelled") { "You canceled the request. Conversation interrupted." } else { "" }
    } | ConvertTo-Json -Compress

    $payload | powershell -NoProfile -ExecutionPolicy Bypass -File $HookScript -EventName $EventName | Out-Null
}

if (-not (Test-Path -LiteralPath $HookScript)) {
    Write-WatchDiagnostics -Status "missing-hook-script" -Detail "Hook script was not found."
    exit 0
}

$deadline = [DateTimeOffset]::Now.AddSeconds($TimeoutSeconds)
$offset = [Math]::Max(0, $InitialLength)
Write-WatchDiagnostics -Status "started" -Offset $offset

while ([DateTimeOffset]::Now -lt $deadline) {
    if (-not (Test-Path -LiteralPath $TranscriptPath)) {
        Start-Sleep -Milliseconds $pollIntervalMilliseconds
        continue
    }

    try {
        $stream = [System.IO.File]::Open($TranscriptPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            if ($stream.Length -gt $offset) {
                $stream.Seek($offset, [System.IO.SeekOrigin]::Begin) | Out-Null
                $reader = New-Object System.IO.StreamReader($stream, $utf8NoBom, $true)
                $text = $reader.ReadToEnd()
                $offset = [int]$stream.Length
                Write-WatchDiagnostics -Status "read" -Offset $offset -ReadLength $text.Length

                if (Test-CancelledText -Text $text) {
                    Write-WatchDiagnostics -Status "matched-cancelled" -Detail ($text.Substring(0, [Math]::Min(300, $text.Length))) -Offset $offset -ReadLength $text.Length
                    Invoke-SignalHook -EventName "PostToolUse" -Resolution "cancelled"
                    exit 0
                }

                if (Test-ApprovedText -Text $text) {
                    Write-WatchDiagnostics -Status "matched-approved" -Detail ($text.Substring(0, [Math]::Min(300, $text.Length))) -Offset $offset -ReadLength $text.Length
                    Invoke-SignalHook -EventName "PreToolUse" -Resolution "approved"
                    exit 0
                }
            }
        } finally {
            $stream.Dispose()
        }
    } catch {
        Write-WatchDiagnostics -Status "error" -Detail $_.Exception.Message -Offset $offset
    }

    Start-Sleep -Milliseconds $pollIntervalMilliseconds
}

Write-WatchDiagnostics -Status "timeout" -Offset $offset
exit 0
