# Remote SSH Local Signal Setup

Date: 2026-06-08

This document describes the implemented remote SSH signal path.

## 1. Runtime Shape

```text
Remote Codex hook
-> remote-hooks/codex-remote-hook.sh or .ps1
-> http://127.0.0.1:<remote-port>/api/events
-> SSH reverse forward
-> local SignalLight.Bridge on 127.0.0.1:<local-port>
-> local SignalLight JSON store
-> SignalLight.App WPF indicator
```

The local bridge is packaged in:

```text
bridge\SignalLight.Bridge.dll
```

The remote hook scripts are packaged in:

```text
remote-hooks\codex-remote-hook.sh
remote-hooks\codex-remote-hook.ps1
```

## 2. Local Startup

Build and package:

```powershell
cd "B:\AI Traffic Signal"
.\tools\package-portable.ps1
```

Start SignalLight:

```powershell
.\start-signal-light.ps1
```

The startup script now starts both:

```text
SignalLight.App
SignalLight.Bridge
```

The bridge writes or reuses its settings at:

```text
%LOCALAPPDATA%\SignalLight\remote-bridge.json
```

Default bridge settings:

```json
{
  "schemaVersion": 1,
  "bind": "127.0.0.1",
  "port": 37631,
  "token": "<generated-token>"
}
```

## 3. Open SSH Reverse Tunnel

Configure the remote server once:

```powershell
.\tools\configure-remote-signal.ps1 -HostName 172.16.11.106 -User xiaoyao -SshPort 3368 -RemoteLabel my-172.16.11.106
```

This copies the remote hook, writes `~/.signal-light/env`, and installs remote Codex hooks into `~/.codex/hooks.json`.

For the current server, a one-command wrapper is also available:

```powershell
.\start-remote-signal-my-172.16.11.106.ps1
```

or double-click:

```text
start-remote-signal-my-172.16.11.106.cmd
```

It starts local SignalLight, configures the remote hook, and then opens the SSH reverse tunnel. SSH / SCP may prompt for the server password.

Use the helper:

```powershell
.\tools\start-remote-signal-ssh.ps1 -HostName remote-host -User user
```

The helper:

- Ensures `%LOCALAPPDATA%\SignalLight\remote-bridge.json` exists.
- Reads the bridge token.
- Checks local bridge health before opening the tunnel.
- Prints the remote environment variables.
- Starts `ssh -R 37631:127.0.0.1:37631 user@remote-host`.
- Uses `ExitOnForwardFailure=yes`, `ServerAliveInterval`, `ServerAliveCountMax`, and `TCPKeepAlive` so broken tunnels fail visibly.

Equivalent manual command:

```powershell
ssh -o ExitOnForwardFailure=yes -o ServerAliveInterval=30 -o ServerAliveCountMax=3 -o TCPKeepAlive=yes -R 37631:127.0.0.1:37631 user@remote-host
```

Use `-NoShell` when only a tunnel is needed:

```powershell
.\tools\start-remote-signal-ssh.ps1 -HostName remote-host -User user -NoShell
```

Use `-Reconnect` for a monitored tunnel that restarts after disconnects:

```powershell
.\tools\start-remote-signal-ssh.ps1 -HostName remote-host -User user -NoShell -Reconnect
```

The server-specific wrapper uses `-NoShell -Reconnect` by default.

## 4. Remote Environment

Set these variables on the remote server:

```bash
export SIGNAL_LIGHT_BRIDGE_URL='http://127.0.0.1:37631/api/events'
export SIGNAL_LIGHT_BRIDGE_TOKEN='<token-from-local-remote-bridge.json>'
export SIGNAL_LIGHT_REMOTE_HOST='remote-host'
```

The helper prints the exact values to use.

For Codex hooks, prefer saving them in a file because `export` commands run from inside Codex do not modify the already-running Codex process environment:

```bash
mkdir -p ~/.signal-light
cat > ~/.signal-light/env <<'EOF'
export SIGNAL_LIGHT_BRIDGE_URL='http://127.0.0.1:37631/api/events'
export SIGNAL_LIGHT_BRIDGE_TOKEN='<token-from-local-remote-bridge.json>'
export SIGNAL_LIGHT_REMOTE_HOST='remote-host'
EOF
chmod 600 ~/.signal-light/env
```

`remote-hooks/codex-remote-hook.sh` automatically sources this file before sending events.

## 5. Remote Hook Command Shape

Linux / macOS remote:

```bash
remote-hooks/codex-remote-hook.sh --event UserPromptSubmit
remote-hooks/codex-remote-hook.sh --event PermissionRequest
remote-hooks/codex-remote-hook.sh --event PreToolUse
remote-hooks/codex-remote-hook.sh --event PostToolUse
remote-hooks/codex-remote-hook.sh --event Stop
```

PowerShell remote:

```powershell
remote-hooks\codex-remote-hook.ps1 -Event UserPromptSubmit
remote-hooks\codex-remote-hook.ps1 -Event PermissionRequest
remote-hooks\codex-remote-hook.ps1 -Event PreToolUse
remote-hooks\codex-remote-hook.ps1 -Event PostToolUse
remote-hooks\codex-remote-hook.ps1 -Event Stop
```

The scripts read hook payload from stdin, send JSON to the bridge, and always exit `0` so remote Codex work is not interrupted if the bridge is temporarily unavailable.

## 6. Manual Test

After the tunnel is open, run this on the remote server:

```bash
curl -fsS -X POST "$SIGNAL_LIGHT_BRIDGE_URL" \
  -H "Authorization: Bearer $SIGNAL_LIGHT_BRIDGE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"schemaVersion":1,"codexEvent":"UserPromptSubmit","source":"remote-ssh","adapter":"manual-test","remoteHost":"dev-server","remoteUser":"ubuntu","workspace":"/tmp","sessionId":"demo","title":"remote demo"}'
```

Expected result:

```text
Local SignalLight turns red and shows a remote session row.
```

Then complete it:

```bash
curl -fsS -X POST "$SIGNAL_LIGHT_BRIDGE_URL" \
  -H "Authorization: Bearer $SIGNAL_LIGHT_BRIDGE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"schemaVersion":1,"codexEvent":"Stop","source":"remote-ssh","adapter":"manual-test","remoteHost":"dev-server","remoteUser":"ubuntu","workspace":"/tmp","sessionId":"demo"}'
```

Expected result:

```text
Local SignalLight turns green after the normal green confirmation delay.
```

## 7. Diagnostics

Bridge diagnostics:

```text
%LOCALAPPDATA%\SignalLight\diagnostics\remote-bridge\latest-status.json
%LOCALAPPDATA%\SignalLight\diagnostics\remote-bridge\latest-request.json
%LOCALAPPDATA%\SignalLight\diagnostics\remote-bridge\latest-error.json
%LOCALAPPDATA%\SignalLight\diagnostics\remote-bridge\latest-rejected-request.json
```

Remote hook diagnostics:

```text
~/.signal-light/diagnostics/latest-remote-hook-error.txt
```

Tunnel monitor diagnostics:

```text
.local\remote-tunnel.status.json
.local\remote-tunnel.log
```

## 8. Security Notes

- The bridge binds to `127.0.0.1` by default.
- Remote access should go through SSH reverse forwarding.
- Requests must include `Authorization: Bearer <token>`.
- Invalid or missing tokens are rejected and do not change lamp state.
- Do not expose the bridge on `0.0.0.0` unless a separate network security review is completed.
