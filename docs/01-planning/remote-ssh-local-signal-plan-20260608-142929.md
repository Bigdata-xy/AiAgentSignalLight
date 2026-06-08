# Remote SSH To Local SignalLight Feature Plan

Date: 2026-06-08
Timestamp: 20260608-142929

## 1. Problem

Current SignalLight can light the local Windows indicator only when local Codex hooks write local JSON state files:

```text
local Codex hook
-> hooks/codex-hook.ps1
-> %LOCALAPPDATA%\SignalLight\events / sessions / snapshot.json
-> SignalLight.App FileSystemWatcher
-> local traffic light UI
```

When the user connects to a remote server through SSH and runs AI / Codex work on that server, the remote hooks execute on the remote machine. They cannot write the local Windows `%LOCALAPPDATA%\SignalLight` directory, and the local WPF app does not watch remote files. Therefore the local indicator does not light.

The missing feature is a remote-to-local event transport:

```text
remote AI / Codex lifecycle event
-> secure transport over the existing SSH connection
-> local SignalLight ingestion
-> local JSON snapshot
-> local WPF indicator
```

## 2. Recommended Direction

Use a local SignalLight bridge plus SSH reverse port forwarding.

Recommended runtime shape:

```text
Local Windows
  SignalLight.App
  SignalLight.RemoteBridge listens on 127.0.0.1:37631
        ^
        | SSH reverse tunnel
        |
Remote server
  remote Codex hook / shell adapter
  POST http://127.0.0.1:<remote-forwarded-port>/api/events
```

Why this is the right first implementation:

- The remote server never needs direct network access to the local Windows machine.
- The bridge can stay bound to `127.0.0.1`, avoiding LAN exposure.
- The SSH connection already provides encryption.
- SignalLight keeps one authoritative local state store.
- The existing WPF refresh path remains unchanged.
- Multiple remote servers can be represented as separate sessions by adding `remoteHost`, `workspace`, and `sessionId` metadata.

## 3. Feature Boundary

Implement only event transport and local ingestion first.

In scope:

- Add a local loopback receiver for remote events.
- Add a remote hook script that sends lifecycle events to the receiver through SSH reverse forwarding.
- Preserve existing red / yellow / green semantics.
- Add diagnostics for bridge connectivity, authentication failure, and last remote event.
- Add startup and user documentation.

Out of scope for the first release:

- Public cloud relay service.
- Permanent background Windows service.
- Bidirectional remote command execution from SignalLight.
- Replacing the current Codex hook model.
- Cross-platform desktop UI.

## 4. Protocol

Remote hook sends JSON to the local bridge:

```json
{
  "schemaVersion": 1,
  "eventType": "TaskStarted",
  "source": "remote-ssh",
  "adapter": "codex-remote-hooks",
  "remoteHost": "dev-server-01",
  "remoteUser": "ubuntu",
  "workspace": "/home/ubuntu/project",
  "sessionId": "remote-codex-session-id",
  "title": "user prompt excerpt",
  "createdAt": "2026-06-08T14:29:29+08:00",
  "payload": {
    "codexEvent": "UserPromptSubmit"
  }
}
```

Event mapping should match the existing canonical state policy:

| Remote Codex event | SignalLight event | Lamp |
|---|---|---|
| `UserPromptSubmit` | `TaskStarted` | Red |
| `PermissionRequest` | `UserActionRequired` | Yellow |
| `PreToolUse` | `TaskStarted` | Red |
| `PostToolUse` | `TaskStarted` | Red |
| `Stop` | `TaskCompleted` | Green |
| explicit cancellation / interruption | `TaskCompleted` | Green |
| unknown event | `Unknown` plus diagnostics | No trusted completion |

Important policy point:

Generic manual `emit --state completed` currently does not turn the light green. Remote completion must not reuse that generic path. The bridge should treat remote `Stop` as trusted only after the request passes bridge authentication and schema validation.

## 5. Authentication And Safety

The local bridge must require a per-machine secret token.

Recommended validation:

- Store a generated token in `%LOCALAPPDATA%\SignalLight\remote-bridge.json`.
- Remote hook sends `Authorization: Bearer <token>`.
- Optionally upgrade to HMAC later: `HMAC-SHA256(token, body)`.
- Reject missing or invalid tokens with `401`.
- Reject malformed payloads with `400`.
- Write rejected request diagnostics without changing lamp state.

Network binding:

- Local bridge binds only to `127.0.0.1`.
- Remote access happens through SSH reverse forwarding.
- Do not bind to `0.0.0.0` for the initial implementation.

## 6. SSH Connection Shape

Manual prototype command:

```powershell
ssh -R 37631:127.0.0.1:37631 user@remote-host
```

Remote hook then posts to:

```text
http://127.0.0.1:37631/api/events
```

For production use, add an optional helper script:

```powershell
tools\start-remote-signal-ssh.ps1 -Host remote-host -User user -RemotePort 37631 -LocalPort 37631
```

The helper should:

- Start or verify local SignalLight.
- Start or verify the local bridge.
- Open SSH with reverse forwarding.
- Print the remote environment variables needed by the remote hook.

Recommended remote environment variables:

```text
SIGNAL_LIGHT_BRIDGE_URL=http://127.0.0.1:37631/api/events
SIGNAL_LIGHT_BRIDGE_TOKEN=<generated-token>
SIGNAL_LIGHT_REMOTE_HOST=<remote-host-label>
```

## 7. Implementation Plan

### Phase 1: Local Bridge

Add a new local bridge component. Two reasonable options:

1. Add `SignalLight.Bridge` as a small .NET console app.
2. Embed bridge startup in `SignalLight.App`.

Recommended first choice: separate `SignalLight.Bridge`.

Reason: it keeps the WPF app simple, makes diagnostics clearer, and allows the bridge to restart independently. The existing startup script can launch both the WPF app and bridge.

Bridge responsibilities:

- Listen on `127.0.0.1:<port>`.
- Accept `POST /api/events`.
- Validate token and JSON schema.
- Convert payload to `SignalEvent`.
- Save event, session, and snapshot through the same `JsonSignalStore` and `SignalStateEngine`.
- Write diagnostics to `%LOCALAPPDATA%\SignalLight\diagnostics\remote-bridge`.

Required code changes:

- Add `src/SignalLight.Bridge/SignalLight.Bridge.csproj`.
- Reuse `SignalLight.Core` and `SignalLight.Storage`.
- Add bridge settings model for port and token.
- Add bridge diagnostics writer.
- Update `SignalLight.sln`.
- Update `tools/package-portable.ps1` to include `bridge/`.
- Update `start-signal-light.ps1` to launch the bridge.

### Phase 2: Remote Hook Adapter

Add remote-side scripts:

```text
remote-hooks/codex-remote-hook.sh
remote-hooks/codex-remote-hook.ps1
```

The shell script should:

- Read Codex hook payload from stdin.
- Map the remote Codex event name to the bridge protocol.
- Include remote metadata from environment variables and system commands.
- POST to `$SIGNAL_LIGHT_BRIDGE_URL`.
- Exit `0` even on bridge failure, after writing local diagnostics, so Codex is not interrupted.

Suggested install helper:

```text
tools/install-remote-hooks.ps1
```

This can copy or print the remote hook content and show how to add it to the remote Codex `hooks.json`.

### Phase 3: Diagnostics And UI Visibility

Add diagnostics first; add UI controls only if needed.

Diagnostics files:

```text
%LOCALAPPDATA%\SignalLight\diagnostics\remote-bridge\latest-request.json
%LOCALAPPDATA%\SignalLight\diagnostics\remote-bridge\latest-error.json
%LOCALAPPDATA%\SignalLight\diagnostics\remote-bridge\latest-rejected-request.json
```

Optional UI additions:

- Tray menu item: `Remote bridge status`.
- Diagnostics export should include `remote-bridge` files.
- Drawer row can show `remoteHost` in workspace text when source is `remote-ssh`.

### Phase 4: Multi-Remote Support

Use stable session identity:

```text
remote:<remoteHost>:<remoteUser>:<workspace>:<sessionId>
```

If Codex does not provide a stable session ID on the remote machine, fallback to:

```text
remote:<remoteHost>:<remoteUser>:<workspace>:<processId>
```

The aggregate priority remains unchanged:

```text
Waiting > Failed > Thinking > Completed > Idle > Stale > Unknown
```

That means one remote task waiting for approval should turn the local lamp yellow even if another local task has completed.

## 8. Test Plan

Unit tests:

- Bridge rejects requests without token.
- Bridge rejects invalid JSON.
- Bridge maps `UserPromptSubmit` to `TaskStarted`.
- Bridge maps `PermissionRequest` to `UserActionRequired`.
- Bridge accepts authenticated `Stop` as `TaskCompleted`.
- Remote sessions do not accidentally complete unrelated local sessions.
- Multiple remote hosts produce separate sessions.

Integration tests:

- Start bridge on a test root directory.
- POST running / waiting / running / completed events.
- Verify `snapshot.json` aggregate state changes red / yellow / red / green.
- Verify diagnostics are written for failed requests.

Manual verification:

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
ssh -R 37631:127.0.0.1:37631 user@remote-host
```

On the remote server:

```bash
curl -X POST "$SIGNAL_LIGHT_BRIDGE_URL" \
  -H "Authorization: Bearer $SIGNAL_LIGHT_BRIDGE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"schemaVersion":1,"eventType":"TaskStarted","source":"remote-ssh","adapter":"manual-test","remoteHost":"dev-server","workspace":"/tmp","sessionId":"demo","title":"remote demo"}'
```

Expected result: local SignalLight turns red and shows a remote session row.

## 9. Rollout Plan

1. Build the local bridge and test it with local `curl` / PowerShell `Invoke-RestMethod`.
2. Add packaging and startup integration.
3. Add remote hook scripts.
4. Verify with one SSH remote server using reverse forwarding.
5. Add docs to the user guide.
6. Only after the above works, add optional UI status controls.

## 10. Risks And Mitigations

| Risk | Mitigation |
|---|---|
| SSH reverse forwarding disabled by server policy | Provide fallback pull mode using `ssh` / `scp` to read remote event files |
| Remote hook failure interrupts Codex | Remote hook must trap errors and exit `0`, same as current local PowerShell hook policy |
| Remote completion turns green incorrectly | Accept `TaskCompleted` only from authenticated bridge protocol and match by stable session ID |
| Multiple machines overwrite each other | Prefix session IDs with remote host, user, workspace, and remote session ID |
| Local port conflict | Make bridge port configurable and record selected port in diagnostics |
| Token leaks in remote shell history | Prefer environment file with restricted permissions and avoid printing token after setup |

## 11. Fallback Design

If SSH reverse forwarding is unavailable, use a local puller:

```text
remote Codex hook
-> remote ~/.signal-light/events/*.json
local SignalLight.RemotePuller
-> ssh/scp/sftp pull
-> local SignalLight JSON store
```

This is less responsive and requires local SSH credentials, but it works when `ssh -R` is blocked.

The bridge design should remain the primary path because it is simpler during active SSH sessions and has lower latency.

## 12. Acceptance Criteria

The feature is complete when:

- Starting local SignalLight also starts or clearly exposes the remote bridge.
- A remote Codex hook can turn the local lamp red, yellow, red, then green through an SSH reverse tunnel.
- Invalid or unauthenticated remote requests never change the lamp.
- Local Codex hooks continue to work unchanged.
- Diagnostics export includes remote bridge state.
- Tests cover bridge authentication, event mapping, session matching, and completion policy.
