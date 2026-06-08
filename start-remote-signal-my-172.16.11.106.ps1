$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$hostName = "172.16.11.106"
$user = "xiaoyao"
$sshPort = 3368
$remoteLabel = "my-172.16.11.106"
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
Write-Host "The next step opens an SSH reverse tunnel and keeps it running."
Write-Host "Do not close that SSH window while you want remote Codex events to light the local indicator."

Invoke-Step -Name "Open SSH reverse tunnel" -Script {
    & (Join-Path $root "tools\start-remote-signal-ssh.ps1") `
        -HostName $hostName `
        -User $user `
        -SshPort $sshPort `
        -IdentityFile $identityFile
}
