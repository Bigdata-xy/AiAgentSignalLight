# Signal Event Protocol

All adapters emit the same event shape.

```json
{
  "schemaVersion": 1,
  "eventId": "...",
  "eventType": "TaskStarted",
  "sessionId": "...",
  "source": "codex-cli",
  "adapter": "codex-hooks",
  "workspace": "B:\\project",
  "title": "Task title",
  "createdAt": "2026-06-03T15:00:00+08:00",
  "payload": {}
}
```

## Event Types

- `TaskStarted`
- `UserActionRequired`
- `TaskCompleted`
- `TaskFailed`
- `Heartbeat`
- `SessionEnded`

## State Contract

The protocol is stricter than a generic status writer. SignalLight must not show green merely because a helper, test script, or tool writes a completion-like word.

Current authoritative completion sources:

- Codex `Stop` hook.
- Explicit Codex authorization cancel / turn interruption detected from structured hook or transcript fields.

Ignored manual completion states:

```text
completed
done
idle
green
```

`SignalLight.Agent emit` ignores those states. They are valid domain/display names, but they are not accepted as manual completion authority.

## Codex Mapping

| Codex event | Event type | Display state |
|---|---|---|
| `UserPromptSubmit` | `TaskStarted` | `Thinking` / red |
| `PermissionRequest` | `UserActionRequired` | `Waiting` / yellow |
| `PreToolUse` | `TaskStarted` | `Thinking` / red |
| `PostToolUse` | `TaskStarted` | `Thinking` / red |
| `Stop` | `TaskCompleted` | `Completed` / green |
| `SessionStart` | ignored | no display change |

The WPF UI applies a 0.8 second green confirmation window after `TaskCompleted`. If a new `TaskStarted` or `UserActionRequired` event arrives during that window, green display is canceled.

For the full policy, see:

```text
docs/04-protocol/state-completion-policy.md
```
