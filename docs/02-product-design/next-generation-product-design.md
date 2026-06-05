# Generic AI / Agent Traffic Signal Product Design

## 1. Product Positioning

SignalLight is a purely local AI / Agent state signal light. It uses adapters to collect runtime events from AI tools, agents, websites, or CLIs; maintains local session state; and displays states such as processing, waiting for user input, and completed through a red/yellow/green traffic-light UI.

Codex is only the first-phase adapter target, not the product boundary. Later versions should connect to other AI websites, Agent tools, CLIs, IDE plugins, and automation systems that require waiting or confirmation.

One-sentence definition:

```text
SignalLight is a local AI / Agent traffic signal: adapters collect events, Core maintains the state machine, JSON stores real-time state, the desktop traffic-light UI displays state, the task-badge drawer shows current task information, and the tray menu provides diagnostics and operation entry points.
```

## 2. Design Goals

The product needs to solve three core problems:

1. Let the user know at a glance whether an AI / Agent is processing a task.
2. Let the user promptly notice whether an AI / Agent is waiting for input, permission confirmation, or manual takeover.
3. Make state clearer when multiple tools, projects, and sessions are running at the same time.

It also needs to avoid problems already exposed in the old implementation:

- Damaged Chinese documentation encoding.
- PowerShell hook scripts that are too long and mix business logic into scripts.
- Hook scripts directly deciding final UI state.
- State protocol that is too simple and lacks a schema version.
- Confusion between statistics semantics and multi-session aggregation semantics.
- Diagnostics hidden in files instead of being productized.

## 3. Core Principles

### 3.1 The Traffic-Light UI Must Be Preserved

The primary visual must be a red/yellow/green traffic light:

- Red: AI / Agent is processing.
- Yellow: waiting for user input, permission confirmation, or manual action.
- Green: completed, idle, or no active task.

Materials, animation, information density, and the task drawer can be enhanced, but the main UI must not become a status bar, dashboard, or ordinary card list.

### 3.2 Adapters Only Collect Events

Adapters do not directly decide final UI state. Recommended boundary:

```text
Adapters = event collectors
Core state engine = state decision maker
UI = state renderer
```

Codex hooks, browser scripts, and CLI wrappers are only responsible for converting external events into the unified event protocol.

### 3.3 Events And State Are Separate

Events represent what happened:

```text
TaskStarted
UserActionRequired
TaskCompleted
TaskFailed
Heartbeat
SessionEnded
```

State represents how the product should currently display it:

```text
Thinking
Waiting
Completed
Idle
Failed
Stale
Unknown
```

This allows more event sources without breaking UI logic.

### 3.4 Local First

All core data is stored locally by default. User data is not uploaded, and the product does not depend on a server.

Default data directory:

```text
%LOCALAPPDATA%\SignalLight\
```

Network features should be triggered only by the user, such as checking for updates or opening a release page.

### 3.5 Productize Diagnostics

Common problems for hook-based tools include:

- Hooks are not trusted.
- Hooks do not trigger.
- `CODEX_HOME` is inconsistent.
- Session ID is unstable.
- Workspace detection fails.
- PowerShell execution fails.
- Agent path is wrong.

Therefore, diagnostics are not an accessory feature; they are a core part of product reliability.

## 4. Overall Architecture

Recommended chain:

```text
Adapters -> SignalLight.Agent -> SignalLight.Core -> SignalLight.Storage -> SignalLight.App
```

Responsibility boundaries:

- Adapters: collect tool-specific events.
- Agent: normalize inbound events.
- Core: state transitions, aggregation, expiration policy.
- Storage: save JSON snapshot, sessions, events, and diagnostics.
- App: show traffic-light UI, task-badge drawer, diagnostics export, and operation entry points.

The MVP does not introduce a resident background service. When the App is not running, Agent can still write local JSON; when the App starts again, it reads the latest state.

## 5. Engineering Structure

```text
SignalLight/
  src/
    SignalLight.App/        WPF desktop UI
    SignalLight.Core/       state machine, session aggregation, protocol models
    SignalLight.Agent/      local event writer
    SignalLight.Adapters/   adapter boundaries for Codex, browsers, CLI, etc.
    SignalLight.Storage/    JSON storage and path resolution
  tests/
    SignalLight.Core.Tests/
    SignalLight.Agent.Tests/
    SignalLight.Storage.Tests/
  hooks/
    codex-hook.ps1
  tools/
    install-hooks.ps1
    uninstall-hooks.ps1
    package-portable.ps1
  docs/
```

## 6. Technology Choices

| Module | Technology | Notes |
|---|---|---|
| Desktop UI | .NET 8 WPF | Mature local Windows desktop experience |
| Core logic | .NET class library | State machine must be unit-testable |
| Local Agent | .NET console exe | Receives hooks, CLI, and generic events |
| Codex hook | PowerShell | Only forwarding and minimal diagnostics |
| Default storage | JSON files | Zero dependency, easy to debug and migrate |
| Optional enhancement | SQLite | Later history and statistics |
| UI notification | FileSystemWatcher | Simple and reliable for MVP |
| Release | Framework-dependent portable DLL zip | Started through `dotnet` to reduce Smart App Control blocking risk for local unsigned exe files |
| Tests | xUnit | Cover Core, Agent, and Storage |

## 7. Event Protocol

All adapters should output the same event shape:

```json
{
  "schemaVersion": 1,
  "eventId": "550e8400-e29b-41d4-a716-446655440000",
  "eventType": "TaskStarted",
  "sessionId": "codex-session-id",
  "source": "codex-cli",
  "adapter": "codex-hooks",
  "workspace": "B:\\project",
  "title": "Task title",
  "createdAt": "2026-06-04T15:00:00+08:00",
  "payload": {}
}
```

Codex hook mapping:

| Codex event | Generic event | Default state |
|---|---|---|
| `UserPromptSubmit` | `TaskStarted` | `Thinking` |
| `PermissionRequest` | `UserActionRequired` | `Waiting` |
| `PreToolUse` | `TaskStarted` | `Thinking` |
| `PostToolUse` | `TaskStarted` | `Thinking` |
| `Stop` | `TaskCompleted` | `Completed` |
| `SessionStart` | ignored | Does not write task state |

## 8. Local Storage

Default directory:

```text
%LOCALAPPDATA%\SignalLight\
  snapshot.json
  settings.json
  sessions\
    <session-id>.json
  events\
    <timestamp>-<event-id>.json
  diagnostics\
    latest-hook-context.json
```

Notes:

- `snapshot.json` stores aggregate state.
- `sessions/*.json` stores the latest state for each session.
- `events/*.json` stores the event stream.
- `diagnostics/latest-hook-context.json` stores the latest hook diagnostic information.

Writes should use temporary files followed by replacement where possible, reducing the risk of partially written files.

## 9. State Aggregation

The main lamp state is aggregated by user-attention priority:

```text
Waiting > Failed > Thinking > Completed > Idle > Stale > Unknown
```

Policy:

- If any session is waiting for user action, the main lamp shows yellow.
- If there is a failed session and no waiting session, show exception state.
- If there is a running session and no higher-priority session, show red.
- If only completed or idle sessions exist, show green.
- Running or waiting sessions that have not updated for too long are marked `Stale`.
- Completed sessions are hidden from the snapshot after the retention window.

## 10. UI Design

The main window must keep the traffic-light body:

```text
SignalLight
  red lamp
  yellow lamp
  green lamp
  completed / total
```

The current implementation uses a small floating window that shows only the traffic light and the lower-right task-count badge by default. Active lamps should have breathing or pulse feedback so the user can notice state changes in peripheral vision.

Clicking the task-count badge opens the task drawer. Each drawer task row shows:

- Task prompt excerpt or workspace directory name.
- Current state badge.
- Workspace summary.
- Running duration or completed duration.
- Single-row delete entry point.

Task rows do not show `source/adapter`, avoiding exposure of adapter details as primary user information. When a completion event has no real title, the task name should keep the previous prompt excerpt and should not fall back to `Codex`.

The tray menu provides:

- Show / Hide.
- Hooks > Install.
- Hooks > Uninstall.
- Diagnostics > Open data.
- Diagnostics > Export.
- Diagnostics > Clear done.
- Exit.

Diagnostic details do not stay permanently in the main window. When troubleshooting is needed, use the tray to open the data directory or export a diagnostics package.

## 11. Distribution Plan

Prioritize a portable package:

```text
SignalLight-Portable-win-x64.zip
  app\SignalLight.App.dll
  agent\SignalLight.Agent.dll
  hooks\codex-hook.ps1
  install-hooks.ps1
  uninstall-hooks.ps1
  README.md
  LICENSE
```

Recommended user flow:

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

Manual flow:

```powershell
Expand-Archive SignalLight-Portable-win-x64.zip
.\install-hooks.ps1
dotnet .\app\SignalLight.App.dll
```

Then run this in Codex:

```text
/hooks
```

After trusting SignalLight hook commands, Codex can report state from any project directory.

## 12. MVP Scope

Phase 1 MVP includes:

- WPF traffic-light window.
- SignalLight.Agent.
- Codex hook PowerShell template.
- Hook installation and uninstallation scripts.
- JSON storage.
- Basic diagnostic files.
- Automated Phase 1 simulation validation.

Phase 2 base usable scope includes:

- Small floating traffic-light UI.
- Breathing and pulse animation for active lamps.
- Task-count badge.
- Current task drawer.
- Single-task deletion.
- Hook installation and uninstallation entry points.
- Diagnostics export.
- Tray entry point.
- Session expiration policy.
- Completed-session cleanup.

Phase 3 and later:

- Generic file-drop adapter.
- Browser userscript adapter.
- Browser extension.
- Installer.
- Update mechanism.

## 13. Key Risks

| Risk | Control |
|---|---|
| Codex hook input changes | Preserve raw payload and show original input in diagnostics |
| User has not trusted hooks | App shows installation status and trust guidance |
| Agent path is wrong | Hook writes `agentFound` and `agentPath` |
| Session ID is unstable | Prefer external session ID; generate fallback when missing |
| File watcher misses events | App rereads snapshot and sessions on startup |
| Mojibake in Chinese documentation | Rewrite and save all formal documents as UTF-8 |

## 14. Recommended Route

Short term:

1. Complete real Codex `/hooks` trust validation.
2. Verify real red/yellow/green states.
3. Persist Phase 1 acceptance results.

Medium term:

1. Harden the Agent emit contract.
2. Add a file-drop adapter.
3. Add a non-Codex integration example.

Long term:

1. Browser userscript.
2. Browser extension.
3. Installer and automatic updates.
