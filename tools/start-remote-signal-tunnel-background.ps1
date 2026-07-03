param(
    [Parameter(Mandatory=$true)]
    [string]$HostName,
    [string]$User = "",
    [int]$SshPort = 22,
    [string]$IdentityFile = "",
    [int]$RemotePort = 37631,
    [int]$LocalPort = 37631,
    [int]$ReconnectDelaySeconds = 5,
    [int]$ServerAliveInterval = 30,
    [int]$ServerAliveCountMax = 3,
    [string]$LogDirectory = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptRoot
$tunnelScript = Join-Path $scriptRoot "start-remote-signal-ssh.ps1"
$logRoot = if ([string]::IsNullOrWhiteSpace($LogDirectory)) { Join-Path $repoRoot ".local" } else { $LogDirectory }
$backgroundStatusPath = Join-Path $logRoot "remote-tunnel-background.status.json"
$stdoutPath = Join-Path $logRoot "remote-tunnel-background.out.log"
$stderrPath = Join-Path $logRoot "remote-tunnel-background.err.log"

New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

if (-not (Test-Path -LiteralPath $tunnelScript)) {
    throw "Remote tunnel script was not found: $tunnelScript"
}

function Stop-PreviousBackgroundTunnel {
    if (-not (Test-Path -LiteralPath $backgroundStatusPath)) {
        return
    }

    try {
        $previous = Get-Content -LiteralPath $backgroundStatusPath -Raw | ConvertFrom-Json
        $previousProcessId = [int]$previous.processId
        if ($previousProcessId -le 0) {
            return
        }

        $process = Get-Process -Id $previousProcessId -ErrorAction SilentlyContinue
        if ($process -and ($process.ProcessName -eq "powershell" -or $process.ProcessName -eq "pwsh")) {
            Stop-Process -Id $previousProcessId -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 300
        }
    } catch {
    }
}

function Stop-ExistingSshTunnel {
    $target = if ([string]::IsNullOrWhiteSpace($User)) { $HostName } else { "$User@$HostName" }
    $forward = "${RemotePort}:127.0.0.1:${LocalPort}"

    Get-CimInstance Win32_Process -Filter "Name = 'ssh.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -like "*$target*" -and
            $_.CommandLine -like "*$forward*" -and
            $_.CommandLine -like "*-R*"
        } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
}

function Write-BackgroundStatus {
    param(
        [int]$ProcessId,
        [string]$State,
        [string]$Message = ""
    )

    $status = [pscustomobject]@{
        time = [DateTimeOffset]::Now.ToString("o")
        state = $State
        message = $Message
        processId = $ProcessId
        hostName = $HostName
        user = $User
        sshPort = $SshPort
        remotePort = $RemotePort
        localPort = $LocalPort
        stdout = $stdoutPath
        stderr = $stderrPath
    }
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($backgroundStatusPath, ($status | ConvertTo-Json -Depth 5), $encoding)
}

Stop-PreviousBackgroundTunnel
Stop-ExistingSshTunnel

$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", "`"$tunnelScript`"",
    "-HostName", $HostName,
    "-SshPort", "$SshPort",
    "-RemotePort", "$RemotePort",
    "-LocalPort", "$LocalPort",
    "-ReconnectDelaySeconds", "$ReconnectDelaySeconds",
    "-ServerAliveInterval", "$ServerAliveInterval",
    "-ServerAliveCountMax", "$ServerAliveCountMax",
    "-NoShell",
    "-Reconnect",
    "-LogDirectory", "`"$logRoot`""
)

if (-not [string]::IsNullOrWhiteSpace($User)) {
    $arguments += @("-User", $User)
}

if (-not [string]::IsNullOrWhiteSpace($IdentityFile)) {
    $arguments += @("-IdentityFile", "`"$IdentityFile`"")
}

$process = Start-Process `
    -FilePath "powershell.exe" `
    -ArgumentList $arguments `
    -WorkingDirectory $repoRoot `
    -WindowStyle Hidden `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath `
    -PassThru

if ($null -eq $process) {
    throw "Failed to start remote SignalLight tunnel in the background."
}

Write-BackgroundStatus -ProcessId $process.Id -State "started" -Message "Background remote SignalLight tunnel was started."
Write-Host "Remote SignalLight tunnel started in the background as PID $($process.Id)."
Write-Host "You can close this terminal."
Write-Host "Status: $backgroundStatusPath"
Write-Host "Log: $(Join-Path $logRoot "remote-tunnel.log")"
