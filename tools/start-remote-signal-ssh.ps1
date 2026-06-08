param(
    [Parameter(Mandatory=$true)]
    [string]$HostName,
    [string]$User = "",
    [int]$SshPort = 22,
    [string]$IdentityFile = "",
    [int]$RemotePort = 37631,
    [int]$LocalPort = 37631,
    [switch]$NoShell
)

$ErrorActionPreference = "Stop"

$signalRoot = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "SignalLight"
$settingsPath = Join-Path $signalRoot "remote-bridge.json"
New-Item -ItemType Directory -Force -Path $signalRoot | Out-Null

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

try {
    Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:$LocalPort/health" -TimeoutSec 2 | Out-Null
} catch {
    Write-Warning "SignalLight RemoteBridge is not responding on 127.0.0.1:$LocalPort. Run .\start-signal-light.ps1 first."
}

$target = if ([string]::IsNullOrWhiteSpace($User)) { $HostName } else { "$User@$HostName" }

Write-Host "Remote environment:"
Write-Host "export SIGNAL_LIGHT_BRIDGE_URL='http://127.0.0.1:$RemotePort/api/events'"
Write-Host "export SIGNAL_LIGHT_BRIDGE_TOKEN='$token'"
Write-Host "export SIGNAL_LIGHT_REMOTE_HOST='$HostName'"
Write-Host ""
Write-Host "Starting SSH reverse tunnel:"
if ([string]::IsNullOrWhiteSpace($IdentityFile)) {
    Write-Host "ssh -p $SshPort -R $RemotePort`:127.0.0.1`:$LocalPort $target"
} else {
    Write-Host "ssh -o IdentitiesOnly=yes -i `"$IdentityFile`" -p $SshPort -R $RemotePort`:127.0.0.1`:$LocalPort $target"
}

$args = @()
if (-not [string]::IsNullOrWhiteSpace($IdentityFile)) {
    $args += @("-o", "IdentitiesOnly=yes", "-i", $IdentityFile)
}
$args += @("-p", "$SshPort", "-R", "$RemotePort`:127.0.0.1`:$LocalPort")
if ($NoShell) {
    $args += "-N"
}
$args += $target

& ssh @args
