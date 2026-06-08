param(
    [Parameter(Mandatory=$true)]
    [string]$HostName,
    [string]$User = "",
    [int]$SshPort = 22,
    [string]$KeyPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($KeyPath)) {
    $sshDir = Join-Path $env:USERPROFILE ".ssh"
    $KeyPath = Join-Path $sshDir "id_ed25519_signal_light"
} else {
    $sshDir = Split-Path -Parent $KeyPath
}

New-Item -ItemType Directory -Force -Path $sshDir | Out-Null

$publicKeyPath = "$KeyPath.pub"
if (-not (Test-Path -LiteralPath $KeyPath) -or -not (Test-Path -LiteralPath $publicKeyPath)) {
    Write-Host "Creating SSH key: $KeyPath"
    & ssh-keygen -t ed25519 -f $KeyPath -N '""' -C "SignalLight-remote-hook"
    if ($LASTEXITCODE -ne 0) {
        throw "ssh-keygen failed."
    }
}

$target = if ([string]::IsNullOrWhiteSpace($User)) { $HostName } else { "$User@$HostName" }

function Test-KeyLogin {
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & ssh -o BatchMode=yes -o ConnectTimeout=10 -o IdentitiesOnly=yes -i $KeyPath -p $SshPort $target "echo key-ok" 2>$null
        return $LASTEXITCODE -eq 0
    } finally {
        $script:ErrorActionPreference = $previousErrorActionPreference
    }
}

Write-Host "Checking key login for $target ..."
if (Test-KeyLogin) {
    Write-Host "SSH key is already installed."
    Write-Host $KeyPath
    exit 0
}

Write-Host "Installing SSH public key on $target."
Write-Host "You should be prompted for the remote password once."

$publicKey = (Get-Content -LiteralPath $publicKeyPath -Raw).Trim()
$remoteCommand = "umask 077; mkdir -p ~/.ssh; touch ~/.ssh/authorized_keys; cat >> ~/.ssh/authorized_keys; chmod 700 ~/.ssh; chmod 600 ~/.ssh/authorized_keys"

$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    $publicKey | & ssh -p $SshPort $target $remoteCommand
} finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
if ($LASTEXITCODE -ne 0) {
    throw "Failed to install SSH public key."
}

Write-Host "Verifying key login ..."
if (-not (Test-KeyLogin)) {
    throw "SSH public key was installed, but key login still failed."
}

Write-Host "SSH key login is ready."
Write-Host $KeyPath
