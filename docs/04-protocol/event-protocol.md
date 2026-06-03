# Signal Event Protocol

All adapters must emit the same event shape.

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
