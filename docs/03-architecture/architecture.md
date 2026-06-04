# SignalLight Architecture

SignalLight is structured around adapters, a domain core, local storage, and a compact traffic-light UI.

```text
Codex lifecycle hooks
-> hooks\codex-hook.ps1
-> local JSON events / sessions / snapshot
-> SignalLight.Core aggregation
-> SignalLight.App WPF refresh
-> traffic-light UI / task drawer / tray diagnostics
```

The generic shape remains:

```text
Adapters -> SignalLight.Agent / hook writer -> SignalLight.Core -> SignalLight.Storage -> SignalLight.App
```

## Boundaries

- Adapters collect tool-specific events.
- Codex lifecycle hooks currently call the PowerShell hook script directly.
- `SignalLight.Agent` remains the local CLI ingestion surface, but manual `completed/done/idle/green` emits are ignored.
- Core owns state transitions, aggregation, title preservation, stale handling, and completion authority.
- Storage persists JSON snapshots, sessions, events, and diagnostics.
- App renders the mandatory traffic-light UI, task badge/drawer, tray actions, and diagnostics export.

Codex is implemented as the first adapter. The product remains generic for future AI websites and agents.

## State Ownership

The current authoritative state policy lives in:

```text
docs/04-protocol/state-completion-policy.md
```

Key rules:

- Red means the task is actively running or thinking.
- Yellow means Codex is waiting for user authorization.
- Yellow has no timeout; it remains yellow until a real follow-up signal arrives.
- Green is only accepted from Codex `Stop` or explicit Codex cancel/interruption handling.
- The UI delays green display by 0.8 seconds to avoid a false green flash between adjacent Codex events.

## Diagnostics

Primary diagnostics are written under:

```text
%LOCALAPPDATA%\SignalLight\diagnostics
```

Important files:

- `latest-hook-context.json`: latest Codex hook input and mapping result.
- `latest-hook-error.json`: latest hook internal error, if any.
- `latest-permission-watch.json`: latest permission watcher result.
- `hooks/*.json`: historical hook diagnostics.
- `permission-watch/*.json`: historical permission watcher diagnostics.
