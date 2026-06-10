param(
    [Parameter(Mandatory=$true)]
    [string]$HostName,
    [string]$User = "",
    [int]$SshPort = 22,
    [string]$IdentityFile = "",
    [int]$RemotePort = 37631,
    [int]$LocalPort = 37631,
    [switch]$NoShell,
    [switch]$Reconnect,
    [int]$ReconnectDelaySeconds = 5,
    [int]$ServerAliveInterval = 30,
    [int]$ServerAliveCountMax = 3,
    [string]$LogDirectory = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$repoRoot = if ((Split-Path -Leaf $scriptRoot) -eq "tools") { Split-Path -Parent $scriptRoot } else { $scriptRoot }
$signalRoot = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "SignalLight"
$settingsPath = Join-Path $signalRoot "remote-bridge.json"
$logRoot = if ([string]::IsNullOrWhiteSpace($LogDirectory)) { Join-Path $repoRoot ".local" } else { $LogDirectory }
$statusPath = Join-Path $logRoot "remote-tunnel.status.json"
$logPath = Join-Path $logRoot "remote-tunnel.log"
New-Item -ItemType Directory -Force -Path $signalRoot | Out-Null
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

function Write-TunnelLog {
    param([string]$Message)

    $line = "{0} {1}" -f ([DateTimeOffset]::Now.ToString("o")), $Message
    Write-Host $line
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

function Write-TunnelStatus {
    param(
        [Parameter(Mandatory=$true)]
        [string]$State,
        [int]$Attempt = 0,
        [int]$ExitCode = 0,
        [string]$Message = ""
    )

    $status = [pscustomobject]@{
        time = [DateTimeOffset]::Now.ToString("o")
        state = $State
        attempt = $Attempt
        exitCode = $ExitCode
        message = $Message
        hostName = $HostName
        user = $User
        sshPort = $SshPort
        remotePort = $RemotePort
        localPort = $LocalPort
        reconnect = [bool]$Reconnect
        noShell = [bool]$NoShell
    }
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($statusPath, ($status | ConvertTo-Json -Depth 5), $encoding)
}

function Test-LocalBridgeHealth {
    try {
        Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:$LocalPort/health" -TimeoutSec 2 | Out-Null
        return $true
    } catch {
        return $false
    }
}

if (-not (Test-Path -LiteralPath $settingsPath)) {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $token = -join ($bytes | ForEach-Object { $_.ToString("x2") })
    $settings = [pscustomobject]@{
        schemaVersion = 1
        bind = "127.0.0.1"
        port = $LocalPort
        token = $token
    }
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 5), $encoding)
}

$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$token = [string]$settings.token
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Remote bridge token is empty in $settingsPath"
}

if ($ReconnectDelaySeconds -lt 1) {
    $ReconnectDelaySeconds = 1
}

if ($ServerAliveInterval -lt 1) {
    $ServerAliveInterval = 30
}

if ($ServerAliveCountMax -lt 1) {
    $ServerAliveCountMax = 3
}

$target = if ([string]::IsNullOrWhiteSpace($User)) { $HostName } else { "$User@$HostName" }

Write-Host "Remote environment:"
Write-Host "export SIGNAL_LIGHT_BRIDGE_URL='http://127.0.0.1:$RemotePort/api/events'"
Write-Host "export SIGNAL_LIGHT_BRIDGE_TOKEN='$token'"
Write-Host "export SIGNAL_LIGHT_REMOTE_HOST='$HostName'"
Write-Host ""
Write-Host "Starting SSH reverse tunnel:"
if ([string]::IsNullOrWhiteSpace($IdentityFile)) {
    Write-Host "ssh -o ExitOnForwardFailure=yes -o ServerAliveInterval=$ServerAliveInterval -o ServerAliveCountMax=$ServerAliveCountMax -o TCPKeepAlive=yes -p $SshPort -R $RemotePort`:127.0.0.1`:$LocalPort $target"
} else {
    Write-Host "ssh -o ExitOnForwardFailure=yes -o ServerAliveInterval=$ServerAliveInterval -o ServerAliveCountMax=$ServerAliveCountMax -o TCPKeepAlive=yes -o IdentitiesOnly=yes -i `"$IdentityFile`" -p $SshPort -R $RemotePort`:127.0.0.1`:$LocalPort $target"
}

function New-SshArguments {
    $sshArgs = @(
        "-o", "ExitOnForwardFailure=yes",
        "-o", "ServerAliveInterval=$ServerAliveInterval",
        "-o", "ServerAliveCountMax=$ServerAliveCountMax",
        "-o", "TCPKeepAlive=yes"
    )
    if (-not [string]::IsNullOrWhiteSpace($IdentityFile)) {
        $sshArgs += @("-o", "IdentitiesOnly=yes", "-i", $IdentityFile)
    }
    $sshArgs += @("-p", "$SshPort", "-R", "$RemotePort`:127.0.0.1`:$LocalPort")
    if ($NoShell) {
        $sshArgs += "-N"
    }
    $sshArgs += $target
    return $sshArgs
}

$attempt = 0
$lastExitCode = 0
do {
    $attempt++
    if (-not (Test-LocalBridgeHealth)) {
        $message = "SignalLight RemoteBridge is not responding on 127.0.0.1:$LocalPort. Run .\start-signal-light.ps1 first."
        Write-Warning $message
        Write-TunnelStatus -State "local-bridge-unavailable" -Attempt $attempt -ExitCode 2 -Message $message
        Write-TunnelLog $message
        if (-not $Reconnect) {
            exit 2
        }
        Start-Sleep -Seconds $ReconnectDelaySeconds
        continue
    }

    $args = New-SshArguments
    Write-TunnelStatus -State "connecting" -Attempt $attempt -Message "Starting SSH reverse tunnel."
    Write-TunnelLog "Starting SSH reverse tunnel attempt $attempt."
    & ssh @args
    $lastExitCode = $LASTEXITCODE

    $message = "SSH reverse tunnel exited with code $lastExitCode."
    Write-TunnelStatus -State "disconnected" -Attempt $attempt -ExitCode $lastExitCode -Message $message
    Write-TunnelLog $message

    if ($Reconnect) {
        Write-TunnelLog "Reconnecting in $ReconnectDelaySeconds seconds."
        Start-Sleep -Seconds $ReconnectDelaySeconds
    }
} while ($Reconnect)

exit $lastExitCode
