$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$hostName = "172.16.11.41"
$user = "xiaoyao"
$sshPort = 3390
$remoteLabel = "my-172.16.11.41"
$identityFile = Join-Path $env:USERPROFILE ".ssh\id_ed25519_signal_light"

function Invoke-Step {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Name,
        [Parameter(Mandatory=$true)]
        [scriptblock]$Script
    )

    Write-Host ""
    Write-Host "==> $Name"
    & $Script
}

Invoke-Step -Name "Start local SignalLight and RemoteBridge" -Script {
    & (Join-Path $root "start-signal-light.ps1")
}

Invoke-Step -Name "Ensure SSH key login" -Script {
    & (Join-Path $root "tools\ensure-remote-ssh-key.ps1") `
        -HostName $hostName `
        -User $user `
        -SshPort $sshPort `
        -KeyPath $identityFile
}

Invoke-Step -Name "Configure remote Codex hooks" -Script {
    & (Join-Path $root "tools\configure-remote-signal.ps1") `
        -HostName $hostName `
        -User $user `
        -SshPort $sshPort `
        -IdentityFile $identityFile `
        -RemoteLabel $remoteLabel
}

Write-Host ""
Write-Host "Remote SignalLight is configured."
Write-Host "The next step starts a monitored SSH reverse tunnel in the background."

Invoke-Step -Name "Start background SSH reverse tunnel" -Script {
    & (Join-Path $root "tools\start-remote-signal-tunnel-background.ps1") `
        -HostName $hostName `
        -User $user `
        -SshPort $sshPort `
        -IdentityFile $identityFile
}
