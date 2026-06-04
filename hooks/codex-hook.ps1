param(
    [Parameter(Mandatory=$true)]
    [string]$EventName
)

$ErrorActionPreference = "Stop"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom

$toolRoot = if ($env:SIGNAL_LIGHT_HOME) { $env:SIGNAL_LIGHT_HOME } else { Split-Path -Parent $PSScriptRoot }
$signalRoot = if ($env:SIGNAL_LIGHT_ROOT) { $env:SIGNAL_LIGHT_ROOT } else { Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "SignalLight" }
$eventsDirectory = Join-Path $signalRoot "events"
$sessionsDirectory = Join-Path $signalRoot "sessions"
$diagnosticsDirectory = Join-Path $signalRoot "diagnostics"
$hookHistoryDirectory = Join-Path $diagnosticsDirectory "hooks"
$snapshotPath = Join-Path $signalRoot "snapshot.json"
$diagnosticsPath = Join-Path $diagnosticsDirectory "latest-hook-context.json"
$hookErrorPath = Join-Path $diagnosticsDirectory "latest-hook-error.json"
$permissionWatchScript = Join-Path $PSScriptRoot "codex-permission-watch.ps1"
$script:stdin = ""

trap {
    try {
        New-Item -ItemType Directory -Force -Path $diagnosticsDirectory | Out-Null
        $errorDiagnostics = [pscustomobject]@{
            schemaVersion = 1
            receivedAt = [DateTimeOffset]::Now.ToString("o")
            eventName = $EventName
            mappedState = "hookError"
            toolRoot = $toolRoot
            signalRoot = $signalRoot
            error = $_.Exception.Message
            scriptStackTrace = [string]$_.ScriptStackTrace
            rawPayload = $script:stdin
        }
        $json = $errorDiagnostics | ConvertTo-Json -Depth 20
        [System.IO.File]::WriteAllText($hookErrorPath, $json, $utf8NoBom)
        [System.IO.File]::WriteAllText($diagnosticsPath, $json, $utf8NoBom)
    } catch {
    }

    exit 0
}

function Ensure-DataDirectories {
    New-Item -ItemType Directory -Force -Path $signalRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $eventsDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $sessionsDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $diagnosticsDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $hookHistoryDirectory | Out-Null
}

function Write-JsonNoBom {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,
        [Parameter(Mandatory=$true)]
        [object]$Value
    )

    $json = $Value | ConvertTo-Json -Depth 30
    [System.IO.File]::WriteAllText($Path, $json, $utf8NoBom)
}

function Write-JsonAtomic {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,
        [Parameter(Mandatory=$true)]
        [object]$Value
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $tempPath = "$Path.$PID.$([guid]::NewGuid().ToString('N')).tmp"
        try {
            Write-JsonNoBom -Path $tempPath -Value $Value
            Move-Item -LiteralPath $tempPath -Destination $Path -Force
            return
        } catch {
            $lastError = $_
            Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds (20 * $attempt)
        }
    }

    throw $lastError
}

function Repair-Mojibake {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $Text
    }

    $hasNonAscii = $false
    foreach ($ch in $Text.ToCharArray()) {
        if ([int][char]$ch -gt 127) {
            $hasNonAscii = $true
            break
        }
    }

    if (-not $hasNonAscii) {
        return $Text
    }

    $encodings = @()
    try { $encodings += [System.Text.Encoding]::Default } catch {}
    try { $encodings += [System.Text.Encoding]::GetEncoding(936) } catch {}
    try { $encodings += [System.Text.Encoding]::GetEncoding(54936) } catch {}

    foreach ($encoding in $encodings) {
        try {
            $bytes = $encoding.GetBytes($Text)
            $fixed = [System.Text.Encoding]::UTF8.GetString($bytes)
            if ([string]::IsNullOrWhiteSpace($fixed) -or [string]::Equals($fixed, $Text, [StringComparison]::Ordinal)) {
                continue
            }

            $cjkCount = 0
            $hasReplacementCharacter = $false
            foreach ($ch in $fixed.ToCharArray()) {
                $code = [int][char]$ch
                if ($code -eq 0xFFFD) {
                    $hasReplacementCharacter = $true
                    break
                }

                if ($code -ge 0x4E00 -and $code -le 0x9FFF) {
                    $cjkCount++
                }
            }

            if (-not $hasReplacementCharacter -and $cjkCount -gt 0) {
                return $fixed
            }
        } catch {
        }
    }

    return $Text
}

function Sanitize-Id {
    param([string]$Value)

    $chars = @()
    foreach ($ch in $Value.ToCharArray()) {
        if ([char]::IsLetterOrDigit($ch) -or $ch -eq '-' -or $ch -eq '_') {
            $chars += $ch
        }
    }

    if ($chars.Count -eq 0) {
        return ([guid]::NewGuid().ToString("N"))
    }

    return -join $chars
}

function Get-State {
    param([string]$Name)

    switch ($Name) {
        "UserPromptSubmit" { return "thinking" }
        "PreToolUse" { return "thinking" }
        "PostToolUse" { return "thinking" }
        "PermissionRequest" { return "waiting" }
        "Stop" { return "completed" }
        "SessionStart" { return "ignored" }
        default { return "unknown" }
    }
}

function Test-CancelledPayload {
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

function Start-PermissionWatcher {
    param(
        [string]$TranscriptPath,
        [string]$SessionId,
        [string]$Workspace,
        [string]$Title,
        [string]$CommandText
    )

    $missingRequiredPath = [string]::IsNullOrWhiteSpace($TranscriptPath) -or -not (Test-Path -LiteralPath $TranscriptPath) -or -not (Test-Path -LiteralPath $permissionWatchScript)
    $startDiagnostics = [pscustomobject]@{
        schemaVersion = 1
        receivedAt = [DateTimeOffset]::Now.ToString("o")
        status = if ($missingRequiredPath) { "skipped" } else { "starting" }
        transcriptPath = $TranscriptPath
        transcriptExists = -not [string]::IsNullOrWhiteSpace($TranscriptPath) -and (Test-Path -LiteralPath $TranscriptPath)
        permissionWatchScript = $permissionWatchScript
        permissionWatchScriptExists = Test-Path -LiteralPath $permissionWatchScript
        sessionId = $SessionId
        workspace = $Workspace
        title = $Title
        commandText = $CommandText
        initialLength = 0
        error = ""
    }
    Write-JsonAtomic -Path (Join-Path $diagnosticsDirectory "latest-permission-watch-start.json") -Value $startDiagnostics

    if ($missingRequiredPath) {
        return
    }

    $initialLength = 0
    try {
        $initialLength = [int](Get-Item -LiteralPath $TranscriptPath -ErrorAction SilentlyContinue).Length
    } catch {
        $initialLength = 0
    }

    try {
        Start-Process `
            -FilePath "powershell" `
            -ArgumentList @(
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                "`"$permissionWatchScript`"",
                "-HookScript",
                "`"$PSCommandPath`"",
                "-TranscriptPath",
                "`"$TranscriptPath`"",
                "-SessionId",
                "`"$SessionId`"",
                "-Workspace",
                "`"$Workspace`"",
                "-Title",
                "`"$Title`"",
                "-CommandText",
                "`"$CommandText`"",
                "-InitialLength",
                "$initialLength"
            ) `
            -WindowStyle Hidden `
            -ErrorAction SilentlyContinue | Out-Null
        $startDiagnostics.status = "started"
        $startDiagnostics.initialLength = $initialLength
        Write-JsonAtomic -Path (Join-Path $diagnosticsDirectory "latest-permission-watch-start.json") -Value $startDiagnostics
    } catch {
        $startDiagnostics.status = "start-error"
        $startDiagnostics.error = $_.Exception.Message
        Write-JsonAtomic -Path (Join-Path $diagnosticsDirectory "latest-permission-watch-start.json") -Value $startDiagnostics
    }
}

function Get-EventType {
    param([string]$State)

    switch ($State) {
        "thinking" { return "taskStarted" }
        "waiting" { return "userActionRequired" }
        "completed" { return "taskCompleted" }
        "failed" { return "taskFailed" }
        default { return "unknown" }
    }
}

function Get-TextProperty {
    param(
        [object]$Target,
        [string]$Name
    )

    if ($null -eq $Target) {
        return ""
    }

    $property = $Target.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value -or $property.Value -is [bool]) {
        return ""
    }

    $text = [string]$property.Value
    if ([string]::IsNullOrWhiteSpace($text) -or $text -ieq "true" -or $text -ieq "false") {
        return ""
    }

    return (Repair-Mojibake -Text $text)
}

function Find-TextProperty {
    param(
        [object]$Target,
        [string[]]$Names
    )

    if ($null -eq $Target) {
        return ""
    }

    foreach ($name in $Names) {
        $text = Get-TextProperty -Target $Target -Name $name
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            return $text
        }
    }

    foreach ($property in @($Target.PSObject.Properties)) {
        if ($null -eq $property.Value -or $property.Value -is [string] -or $property.Value -is [bool]) {
            continue
        }

        if ($property.Value -is [System.Array]) {
            foreach ($item in @($property.Value)) {
                $text = Find-TextProperty -Target $item -Names $Names
                if (-not [string]::IsNullOrWhiteSpace($text)) {
                    return $text
                }
            }
            continue
        }

        $nestedText = Find-TextProperty -Target $property.Value -Names $Names
        if (-not [string]::IsNullOrWhiteSpace($nestedText)) {
            return $nestedText
        }
    }

    return ""
}

function Load-Sessions {
    if (-not (Test-Path -LiteralPath $sessionsDirectory)) {
        return @()
    }

    $sessions = @()
    foreach ($path in Get-ChildItem -LiteralPath $sessionsDirectory -Filter "*.json" -ErrorAction SilentlyContinue) {
        try {
            $sessions += (Get-Content -LiteralPath $path.FullName -Raw -Encoding UTF8 | ConvertFrom-Json)
        } catch {
        }
    }

    return @($sessions)
}

function Get-Priority {
    param([object]$Session)

    switch ([string]$Session.state) {
        "waiting" { return 0 }
        "failed" { return 1 }
        "thinking" { return 2 }
        "completed" { return 3 }
        "idle" { return 4 }
        "stale" { return 5 }
        default { return 6 }
    }
}

function Get-AggregateState {
    param([object[]]$Sessions)

    if ($Sessions.Count -eq 0) {
        return "idle"
    }

    foreach ($state in @("waiting", "failed", "thinking", "completed")) {
        if (@($Sessions | Where-Object { [string]$_.state -eq $state }).Count -gt 0) {
            return $state
        }
    }

    return "unknown"
}

function Find-PreviousSession {
    param(
        [object[]]$Sessions,
        [string]$SessionId,
        [string]$Source,
        [string]$Adapter,
        [string]$Workspace,
        [string]$Title,
        [string]$State
    )

    if (-not [string]::IsNullOrWhiteSpace($SessionId)) {
        $safeSessionId = Sanitize-Id -Value $SessionId
        $sessionMatch = $Sessions | Where-Object { [string]$_.sessionId -eq $SessionId -or [string]$_.sessionId -eq $safeSessionId } | Select-Object -First 1
        if ($sessionMatch) {
            return $sessionMatch
        }

        if ($EventName -eq "UserPromptSubmit") {
            return $null
        }
    }

    if ($State -eq "thinking" -and $EventName -notin @("PreToolUse", "PostToolUse")) {
        return $null
    }

    $candidates = @($Sessions | Where-Object {
        [string]$_.source -eq $Source -and
        [string]$_.adapter -eq $Adapter -and
        ([string]::IsNullOrWhiteSpace($Workspace) -or [string]$_.workspace -eq $Workspace)
    } | Sort-Object updatedAt -Descending)

    if (-not [string]::IsNullOrWhiteSpace($Title)) {
        $titleMatch = $candidates | Where-Object { [string]$_.displayName -eq $Title } | Select-Object -First 1
        if ($titleMatch) {
            return $titleMatch
        }
    }

    if ($EventName -in @("PreToolUse", "PostToolUse")) {
        $waitingMatch = $candidates | Where-Object { [string]$_.state -eq "waiting" } | Select-Object -First 1
        if (-not $waitingMatch) {
            $waitingMatch = $Sessions |
                Where-Object { [string]$_.source -eq $Source -and [string]$_.adapter -eq $Adapter -and [string]$_.state -eq "waiting" } |
                Sort-Object updatedAt -Descending |
                Select-Object -First 1
        }
        if ($waitingMatch) {
            return $waitingMatch
        }
    }

    $active = @($candidates | Where-Object { [string]$_.state -in @("thinking", "waiting", "stale") })
    if ($active.Count -eq 1) {
        return $active[0]
    }

    if ($active.Count -eq 0) {
        return $candidates | Select-Object -First 1
    }

    return $null
}

Ensure-DataDirectories

$script:stdin = [Console]::In.ReadToEnd().TrimStart([char]0xFEFF)
$stdin = $script:stdin
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

$cancellationSignal = Find-TextProperty -Target $payload -Names @("tool_response", "toolResponse", "message", "status", "decision", "permission_resolution", "permissionResolution")
$cancelledPayload = Test-CancelledPayload -Text $cancellationSignal
$state = Get-State -Name $EventName
if ($EventName -eq "PostToolUse" -and $cancelledPayload) {
    $state = "completed"
}

$session = Find-TextProperty -Target $payload -Names @("session_id", "sessionId", "session", "conversation_id", "conversationId", "conversation", "thread_id", "threadId", "terminal_id", "terminalId", "terminal", "process_id", "processId", "pid")
$workspace = Find-TextProperty -Target $payload -Names @("cwd", "workspace", "workdir", "current_working_directory")
$title = Find-TextProperty -Target $payload -Names @("prompt", "user_prompt", "userPrompt", "message", "text", "input")
$title = Repair-Mojibake -Text $title
$transcriptPath = Find-TextProperty -Target $payload -Names @("transcript_path", "transcriptPath")
$permissionResolution = Find-TextProperty -Target $payload -Names @("permission_resolution", "permissionResolution")
$commandText = Find-TextProperty -Target $payload -Names @("command", "cmd")

$diagnostics = [pscustomobject]@{
    schemaVersion = 1
    receivedAt = [DateTimeOffset]::Now.ToString("o")
    eventName = $EventName
    mappedState = $state
    toolRoot = $toolRoot
    signalRoot = $signalRoot
    agentPath = ""
    agentFound = $false
    session = $session
    workspace = $workspace
    title = $title
    transcriptPath = $transcriptPath
    permissionResolution = $permissionResolution
    commandText = $commandText
    cancelledPayload = $cancelledPayload
    cancellationSignal = $cancellationSignal
    payloadParseError = $payloadParseError
    rawPayload = $stdin
}
Write-JsonAtomic -Path $diagnosticsPath -Value $diagnostics
$diagnosticsHistoryPath = Join-Path $hookHistoryDirectory ("{0:yyyyMMddHHmmssfff}-{1}.json" -f ([DateTimeOffset]::Now), $EventName)
Write-JsonAtomic -Path $diagnosticsHistoryPath -Value $diagnostics

if ($state -eq "ignored") {
    exit 0
}

$source = "codex-cli"
$adapter = "codex-hooks"
$eventType = Get-EventType -State $state
$now = [DateTimeOffset]::Now
$sessions = @(Load-Sessions)
$previous = Find-PreviousSession -Sessions $sessions -SessionId $session -Source $source -Adapter $adapter -Workspace $workspace -Title $title -State $state

if ($state -eq "completed" -and -not $previous -and [string]::IsNullOrWhiteSpace($title)) {
    exit 0
}

if ($previous -and -not [string]::IsNullOrWhiteSpace([string]$previous.sessionId)) {
    $sessionId = [string]$previous.sessionId
} elseif (-not [string]::IsNullOrWhiteSpace($session)) {
    $sessionId = Sanitize-Id -Value $session
} elseif (-not [string]::IsNullOrWhiteSpace($workspace) -or -not [string]::IsNullOrWhiteSpace($title)) {
    $sessionId = Sanitize-Id -Value "$source-$adapter-$workspace-$title"
} else {
    $sessionId = [guid]::NewGuid().ToString("N")
}

$displayName = if (-not [string]::IsNullOrWhiteSpace($title)) {
    if ($title.Length -gt 48) { $title.Substring(0, 48) } else { $title }
} elseif ($previous -and -not [string]::IsNullOrWhiteSpace([string]$previous.displayName)) {
    [string]$previous.displayName
} else {
    "AI Task"
}

$effectiveWorkspace = if ($previous -and [string]::IsNullOrWhiteSpace($title) -and -not [string]::IsNullOrWhiteSpace([string]$previous.workspace)) {
    [string]$previous.workspace
} else {
    $workspace
}

$startedAt = if ($previous -and $previous.startedAt) {
    [string]$previous.startedAt
} elseif ($previous -and $previous.updatedAt) {
    [string]$previous.updatedAt
} else {
    $now.ToString("o")
}

$event = [pscustomobject]@{
    schemaVersion = 1
    eventId = [guid]::NewGuid().ToString("N")
    eventType = $eventType
    sessionId = $sessionId
    conversationId = ""
    source = $source
    adapter = $adapter
    workspace = $effectiveWorkspace
    title = $displayName
    processId = $null
    processStartTime = $null
    createdAt = $now.ToString("o")
    payload = @{
        hookEventName = $EventName
        permissionResolution = $permissionResolution
    }
}

$sessionState = [pscustomobject]@{
    sessionId = $sessionId
    displayName = $displayName
    source = $source
    adapter = $adapter
    workspace = $effectiveWorkspace
    state = $state
    lastEventType = $eventType
    startedAt = $startedAt
    updatedAt = $now.ToString("o")
}

$eventPath = Join-Path $eventsDirectory ("{0:yyyyMMddHHmmssfff}-{1}.json" -f $now, $event.eventId)
$sessionPath = Join-Path $sessionsDirectory "$sessionId.json"
Write-JsonAtomic -Path $eventPath -Value $event
Write-JsonAtomic -Path $sessionPath -Value $sessionState

$visibleSessions = @(Load-Sessions | Sort-Object @{ Expression = { Get-Priority -Session $_ } }, @{ Expression = { $_.updatedAt }; Descending = $true })
$snapshot = [pscustomobject]@{
    schemaVersion = 1
    aggregateState = Get-AggregateState -Sessions $visibleSessions
    updatedAt = $now.ToString("o")
    sessions = $visibleSessions
    completedCount = @($visibleSessions | Where-Object { [string]$_.state -in @("completed", "idle") }).Count
    totalCount = $visibleSessions.Count
}
Write-JsonAtomic -Path $snapshotPath -Value $snapshot

if ($EventName -eq "PermissionRequest") {
    Start-PermissionWatcher -TranscriptPath $transcriptPath -SessionId $sessionId -Workspace $effectiveWorkspace -Title $displayName -CommandText $commandText
}

exit 0
