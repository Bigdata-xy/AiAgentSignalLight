param(
    [Parameter(Mandatory=$true)]
    [string]$HostName,
    [string]$User = "",
    [int]$SshPort = 22,
    [string]$IdentityFile = "",
    [string]$RemoteLabel = "",
    [int]$RemotePort = 37631
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$hookScript = Join-Path $repoRoot "remote-hooks\codex-remote-hook.sh"
$settingsPath = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "SignalLight\remote-bridge.json"

if (-not (Test-Path -LiteralPath $hookScript)) {
    throw "Remote hook script was not found: $hookScript"
}

if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw "Remote bridge settings were not found: $settingsPath. Run .\start-signal-light.ps1 first."
}

$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$token = [string]$settings.Token
if ([string]::IsNullOrWhiteSpace($token)) {
    $token = [string]$settings.token
}

if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Remote bridge token is empty in $settingsPath"
}

$target = if ([string]::IsNullOrWhiteSpace($User)) { $HostName } else { "$User@$HostName" }
$label = if ([string]::IsNullOrWhiteSpace($RemoteLabel)) { $HostName } else { $RemoteLabel }
$sshArgs = @()
$scpArgs = @()
if (-not [string]::IsNullOrWhiteSpace($IdentityFile)) {
    $sshArgs += @("-o", "IdentitiesOnly=yes", "-i", $IdentityFile)
    $scpArgs += @("-o", "IdentitiesOnly=yes", "-i", $IdentityFile)
}
$sshArgs += @("-p", "$SshPort")
$scpArgs += @("-P", "$SshPort")

Write-Host "Configuring SignalLight remote hooks on $target ..."

& ssh @sshArgs $target "mkdir -p ~/.signal-light ~/.codex"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to create remote directories."
}

& scp @scpArgs $hookScript "$target`:~/.signal-light/codex-remote-hook.sh"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to copy remote hook script."
}

& ssh @sshArgs $target "chmod +x ~/.signal-light/codex-remote-hook.sh"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to chmod remote hook script."
}

$envContent = @"
export SIGNAL_LIGHT_BRIDGE_URL='http://127.0.0.1:$RemotePort/api/events'
export SIGNAL_LIGHT_BRIDGE_TOKEN='$token'
export SIGNAL_LIGHT_REMOTE_HOST='$label'
"@
$envBase64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($envContent))
& ssh @sshArgs $target "printf '%s' '$envBase64' | base64 -d > ~/.signal-light/env && chmod 600 ~/.signal-light/env"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to write remote environment file."
}

$installer = @'
import json
from pathlib import Path

path = Path.home() / ".codex" / "hooks.json"
events = ["UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest", "Stop", "SessionStart"]
script = str(Path.home() / ".signal-light" / "codex-remote-hook.sh")

if path.exists() and path.read_text().strip():
    data = json.loads(path.read_text())
else:
    data = {}

data.setdefault("hooks", {})
for event in events:
    groups = data["hooks"].get(event, [])
    kept = []
    for group in groups:
        hooks = group.get("hooks", [])
        hooks = [
            h for h in hooks
            if "codex-remote-hook.sh" not in h.get("command", "")
            and not h.get("statusMessage", "").startswith("SignalLight Remote:")
        ]
        if hooks:
            group["hooks"] = hooks
            kept.append(group)
    kept.append({
        "hooks": [{
            "type": "command",
            "command": f"bash {script} --event {event}",
            "statusMessage": f"SignalLight Remote: {event}"
        }]
    })
    data["hooks"][event] = kept

path.write_text(json.dumps(data, indent=2, ensure_ascii=False))
print(f"installed hooks into {path}")
'@
$installerBase64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($installer))
& ssh @sshArgs $target "printf '%s' '$installerBase64' | base64 -d | python3 -"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to install remote Codex hooks."
}

& ssh @sshArgs $target "test -x ~/.signal-light/codex-remote-hook.sh && test -s ~/.signal-light/env && python3 -m json.tool ~/.codex/hooks.json >/dev/null && echo configured"
if ($LASTEXITCODE -ne 0) {
    throw "Remote verification failed."
}

Write-Host "Remote SignalLight hooks configured."
Write-Host "Keep the SSH reverse tunnel running:"
Write-Host ".\tools\start-remote-signal-ssh.ps1 -HostName $HostName -User $User -SshPort $SshPort"
Write-Host "Then restart remote Codex and run /hooks to trust SignalLight Remote hooks."
