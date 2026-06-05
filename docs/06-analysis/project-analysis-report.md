# Codex Traffic Light Project Analysis Report

## 1. Project Positioning

`codex-traffic-light` is a purely local Windows desktop utility that shows Codex runtime state through a floating red/yellow/green traffic-light window. It does not directly integrate with Codex internals and does not run a server. Instead, it uses Codex hooks to execute PowerShell scripts, writes hook events to local JSON files, and lets a WPF program watch those files through `FileSystemWatcher` to refresh the UI.

The core goals can be summarized as:

- Red means Codex is processing the user request.
- Yellow means Codex is waiting for permission confirmation.
- Green means the current Codex task has ended or is idle.
- Support aggregate state and task lists when multiple Codex CLI or VS Code plugin sessions run at the same time.
- Keep everything local and avoid uploading user data.

This project is more like a Codex hooks state visualizer. It is not a replacement entry point for Codex and not a background daemon service.

## 2. Technology Stack And Engineering Structure

The old project used .NET 8 and mainly contained a desktop app, core library, tests, installation scripts, and release scripts.

Typical structure:

```text
src/
  CodexTrafficLight.App/       WPF desktop window, tray menu, file watcher, visual effects
  CodexTrafficLight.Core/      paths, hook installation, state files, sessions, settings, statistics
tests/
  CodexTrafficLight.Tests/     xUnit unit tests and structure assertion tests
installer/
  CodexTrafficLight.iss        Inno Setup installation script
tools/
  codex-light.ps1              Codex startup wrapper script
  publish-installer.ps1        publish and build installer package
```

One advantage is that Core and WPF are somewhat separated, so paths, JSON, hooks, and statistics logic can be tested separately.

## 3. Core Implementation Idea

Old project key chain:

```text
Codex hook event
  -> PowerShell hook script
  -> local JSON state files
  -> WPF FileSystemWatcher
  -> Red / Yellow / Green UI
```

State files usually include:

- Main state file.
- Session state files.
- Diagnostic files.
- Settings and statistics files.

This approach is low-dependency, purely local, easy to troubleshoot, and does not require a long-running background service.

## 4. Existing Design Strengths

### 4.1 Local First

All state is saved in local JSON files. No server, database, or network connection is required, matching the privacy and low-dependency goals.

### 4.2 Intuitive Traffic-Light Semantics

The mapping between red/yellow/green and Codex states is clear. The user can judge whether attention is needed without reading a complex panel.

### 4.3 Simple Hook Integration

Codex hooks can trigger scripts at key lifecycle events, which is suitable for lightweight state collection.

### 4.4 Diagnostic Awareness

The old project already recognized that hook trust, paths, event triggering, and JSON writes could fail, and started saving diagnostic information.

### 4.5 Packageable Distribution

A Windows desktop tool can be distributed as an installer or portable package, which suits personal workflows.

## 5. Main Problems

### 5.1 Product Boundary Is Over-Bound To Codex

The old project's naming, filenames, and protocol are biased toward Codex-specific usage. This limits later integration with other AI websites, Agents, or CLI tools.

Improvement direction:

- Use SignalLight as the product name.
- Use generic Core protocol events such as `TaskStarted` and `UserActionRequired`.
- Codex should be only an adapter, not the domain model.

### 5.2 Hook Scripts Carry Too Much Business Logic

PowerShell should only collect and forward. Complex business decisions should live in Agent and Core; otherwise scripts become hard to test, hard to maintain, and more vulnerable to execution environment differences.

Improvement direction:

```text
Codex hook command -> SignalLight.Agent -> Core state engine
```

### 5.3 State-File Protocol Is Not Generic Enough

Old filenames and fields can lock the product into a Codex context, and they lack a clear enough schema version and source/adapter fields.

Improvement direction:

```json
{
  "schemaVersion": 1,
  "eventId": "...",
  "eventType": "TaskStarted",
  "sessionId": "...",
  "source": "codex-cli",
  "adapter": "codex-hooks",
  "workspace": "...",
  "title": "...",
  "createdAt": "...",
  "payload": {}
}
```

### 5.4 UI And Diagnostics Levels Are Insufficient

A single traffic light can express the main state, but it cannot explain why that state is active. Daily use needs at least three information levels:

1. Mini traffic light.
2. Active session list.
3. Diagnostics and export page.

### 5.5 Damaged Chinese Documentation Encoding

Mojibake appeared in the old project or during migration, reducing documentation trustworthiness. Formal documentation must be recovered or rewritten as UTF-8.

## 6. SignalLight Improvement Direction

SignalLight should preserve the intuitive traffic-light experience from the old project while making a clearer architectural split:

```text
Adapters -> Agent -> Core -> Storage -> App
```

Module responsibilities:

- Adapters: Codex hooks, CLI, browser, file-drop.
- Agent: command-line event entry point and local writer.
- Core: event-to-state transitions, aggregation, expiration policy.
- Storage: JSON file reads and writes.
- App: traffic light, session list, diagnostics, tray, installation entry points.

This keeps the red/yellow/green experience while preventing the product from serving only Codex.

## 7. Current SignalLight State

The current new project has implemented:

- Generic event model.
- Core state machine.
- JSON storage.
- `SignalLight.Agent emit` command.
- Codex hook script.
- Hook installation and uninstallation scripts.
- WPF traffic-light UI.
- Multi-session list.
- Diagnostics summary and export.
- Tray entry point.
- Session expiration policy.
- Portable zip packaging.

Still to complete:

- Real Codex `/hooks` trust-flow validation.
- Real red/yellow/green trigger validation.
- WPF desktop visual validation.
- Generic file-drop adapter.
- Browser/userscript adapter.
- Installer and complete user documentation.

## 8. Risk Analysis

| Risk | Impact | Recommendation |
|---|---|---|
| Codex hook input changes | Codex adapter breaks | Save raw payload and show original input in diagnostics |
| User has not trusted hooks | State never updates | App shows trust steps and latest hook time |
| Agent path is wrong | Hook does not write state | Hook diagnostics record `agentPath` and `agentFound` |
| Multi-session state confusion | Main lamp misleads the user | Core owns unified aggregation priority |
| JSON file damage | UI read failure | Skip damaged files and preserve diagnostics |
| Documentation mojibake | Future development basis is unreliable | Rewrite all formal documents as UTF-8 |

## 9. Conclusion

The old project proved that showing AI tool state through a traffic light is a valid direction, but it was closer to a Codex-specific visualizer. SignalLight's value is productizing that direction:

- Preserve the traffic-light primary visual.
- Demote Codex to the first adapter.
- Use a generic event protocol for more AI / Agent sources.
- Use local JSON to keep dependencies low.
- Make diagnostics a first-class feature.
- Use a portable zip to lower the distribution barrier.

The recommended route is to complete real Codex acceptance first, then move into the generic adapter phase.
