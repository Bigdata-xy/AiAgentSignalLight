param(
    [string]$Configuration = "Debug",
    [string]$ValidationRoot = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$hookScript = Join-Path $repoRoot "hooks\codex-hook.ps1"
$packageHookScript = Join-Path $repoRoot "dist\SignalLight-Portable-win-x64\hooks\codex-hook.ps1"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom

if ([string]::IsNullOrWhiteSpace($ValidationRoot)) {
    $ValidationRoot = Join-Path $repoRoot ".local\phase1-validation"
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,
        [Parameter(Mandatory=$true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label was not created: $Path"
    }
}

function Invoke-HookEvent {
    param(
        [Parameter(Mandatory=$true)]
        [string]$EventName,
        [Parameter(Mandatory=$true)]
        [string]$Prompt
    )

    $payload = @{
        session_id = "phase1-validation-session"
        cwd = $repoRoot
        prompt = $Prompt
    } | ConvertTo-Json -Compress

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = "powershell"
    $startInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$packageHookScript`" -EventName `"$EventName`""
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Failed to start Agent hook process."
    }

    $bytes = $utf8NoBom.GetBytes($payload)
    $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
    $process.StandardInput.Close()
    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()
    if (-not $process.WaitForExit(15000)) {
        $process.Kill()
        throw "Hook event timed out: $EventName"
    }

    if ($process.ExitCode -ne 0) {
        throw "Hook event failed: $EventName`n$output`n$errorOutput"
    }
}

function Assert-SnapshotState {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Expected
    )

    $snapshotPath = Join-Path $ValidationRoot "snapshot.json"
    Assert-FileExists -Path $snapshotPath -Label "snapshot.json"
    $snapshot = Get-Content -LiteralPath $snapshotPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$snapshot.aggregateState -ne $Expected) {
        throw "Expected aggregateState '$Expected', got '$($snapshot.aggregateState)'"
    }
}

function Assert-SessionTitle {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Expected
    )

    $sessionPath = Join-Path $ValidationRoot "sessions\phase1-validation-session.json"
    Assert-FileExists -Path $sessionPath -Label "session file"
    $session = Get-Content -LiteralPath $sessionPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$session.displayName -ne $Expected) {
        throw "Expected session displayName '$Expected', got '$($session.displayName)'"
    }
}

if (-not (Test-Path -LiteralPath $hookScript)) {
    throw "Hook script was not found: $hookScript"
}

if (Test-Path -LiteralPath $ValidationRoot) {
    Remove-Item -LiteralPath $ValidationRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $ValidationRoot | Out-Null

& (Join-Path $repoRoot "tools\package-portable.ps1") -Configuration $Configuration | Out-Host
Get-ChildItem -LiteralPath (Join-Path $repoRoot "dist\SignalLight-Portable-win-x64") -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in ".dll", ".exe" } |
    Unblock-File -ErrorAction SilentlyContinue
Assert-FileExists -Path $packageHookScript -Label "codex-hook.ps1"

$env:SIGNAL_LIGHT_HOME = $repoRoot
$env:SIGNAL_LIGHT_ROOT = $ValidationRoot
$ChinesePrompt = -join ([int[]](20462, 22797, 20013, 25991, 20219, 21153, 26631, 39064, 39564, 35777) | ForEach-Object { [char]$_ })

try {
    Invoke-HookEvent -EventName "UserPromptSubmit" -Prompt $ChinesePrompt
    Assert-SnapshotState -Expected "Thinking"
    Assert-SessionTitle -Expected $ChinesePrompt

    Invoke-HookEvent -EventName "PermissionRequest" -Prompt "Phase 1 yellow validation"
    Assert-SnapshotState -Expected "Waiting"

    Invoke-HookEvent -EventName "PreToolUse" -Prompt "Phase 1 resume validation"
    Assert-SnapshotState -Expected "Thinking"

    Invoke-HookEvent -EventName "PermissionRequest" -Prompt "Phase 1 yellow validation again"
    Assert-SnapshotState -Expected "Waiting"

    Invoke-HookEvent -EventName "PostToolUse" -Prompt "Phase 1 post-tool resume validation"
    Assert-SnapshotState -Expected "Thinking"

    Invoke-HookEvent -EventName "Stop" -Prompt "Phase 1 green validation"
    Assert-SnapshotState -Expected "Completed"

    Assert-FileExists -Path (Join-Path $ValidationRoot "diagnostics\latest-hook-context.json") -Label "latest hook diagnostics"
    Assert-FileExists -Path (Join-Path $ValidationRoot "sessions\phase1-validation-session.json") -Label "session file"

    $eventCount = @(Get-ChildItem -LiteralPath (Join-Path $ValidationRoot "events") -Filter "*.json").Count
    if ($eventCount -lt 6) {
        throw "Expected at least 6 event files, got $eventCount"
    }

    Write-Host "Phase 1 validation passed."
    Write-Host "Validation root: $ValidationRoot"
} finally {
    Remove-Item Env:\SIGNAL_LIGHT_HOME -ErrorAction SilentlyContinue
    Remove-Item Env:\SIGNAL_LIGHT_ROOT -ErrorAction SilentlyContinue
}
