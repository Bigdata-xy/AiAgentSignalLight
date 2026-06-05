# SignalLight Phase Execution Plan

## 1. Execution Objective

This plan is for the generic AI / Agent traffic signal product `SignalLight`. The product must keep red, yellow, and green traffic lights as the main UI, while also providing low dependency, portable deployment, generic adapter support, and diagnostics.

Core objectives:

- Prioritize Codex hooks in the first phase.
- Do not bind the architecture to Codex; later it should extend to AI websites, CLI agents, self-built agents, IDE plugins, and browser plugins.
- Use local JSON storage by default, without hard dependencies on SQLite, a background service, or extra runtimes.
- Prioritize a portable package for distribution.
- After one-time configuration, any project or connected source should be easy to use.

## 2. Phase Overview

| Phase | Name | Goal | Result |
|---|---|---|---|
| Phase 0 | Project initialization | Establish engineering skeleton and protocol boundaries | Buildable and testable base project |
| Phase 1 | Codex MVP | Implement Codex hooks to traffic-light state | Minimal local closed loop |
| Phase 2 | Multi-session and diagnostics | Complete task badge drawer, diagnostics export, trust guidance, and tray entry points | Local tool usable for daily work |
| Phase 3 | Generic adapters | Support CLI, file, and generic event reporting | No longer only a Codex tool |
| Phase 4 | Browser adapters | Connect AI website waiting states | Cover web AI scenarios |
| Phase 5 | Release quality | Packaging, installer, docs, tests, and diagnostics loop | Distributable release |

## 3. Phase 0: Project Initialization

### Goal

Establish the basic structure of the new project and avoid carrying over old project naming and state-file protocols.

### Tasks

- Create the solution and project structure:

```text
SignalLight/
  src/
    SignalLight.App/
    SignalLight.Core/
    SignalLight.Agent/
    SignalLight.Adapters/
    SignalLight.Storage/
  tests/
    SignalLight.Core.Tests/
    SignalLight.Agent.Tests/
    SignalLight.Storage.Tests/
  docs/
  tools/
  installer/
```

- Standardize UTF-8 documentation encoding.
- Define the generic event protocol.
- Define the default local data directory:

```text
%LOCALAPPDATA%\SignalLight\
```

- Establish baseline CI and local test commands.

### Deliverables

- Openable solution.
- Base models in the Core project.
- Command-line entry point in the Agent project.
- Basic window in the App project.
- Protocol documents under docs.

### Acceptance Criteria

- `dotnet build` passes.
- `dotnet test` passes.
- Project name, namespaces, and filenames are not bound to Codex.
- Chinese documentation is UTF-8 and has no mojibake.

## 4. Phase 1: Codex MVP

### Goal

Complete the first end-to-end chain:

```text
Codex hooks -> codex-hook.ps1 -> JSON state -> WPF traffic light UI
```

### Tasks

- Implement the `SignalLight.Agent emit` command.
- Implement the Codex hook PowerShell template.
- Implement the hook installation script `install-hooks.ps1`.
- Implement primary JSON storage:

```text
snapshot.json
sessions\<session-id>.json
events\*.json
diagnostics\latest-hook-context.json
```

- Implement Core state transitions:

```text
TaskStarted -> Thinking -> red
UserActionRequired -> Waiting -> yellow
TaskCompleted -> Completed / Idle -> green
TaskFailed -> Failed -> exception state
```

- Implement the WPF traffic-light main window.
- Provide an automated Phase 1 validation script that simulates Codex hook input and validates state files.

### Deliverables

- `SignalLight.App.dll` runnable through `dotnet`.
- `SignalLight.Agent.dll` runnable through `dotnet`.
- Codex hook template.
- Hook installation script.
- Red/yellow/green traffic-light UI.
- Phase 1 automated validation script.

### Acceptance Criteria

- A user-submitted Codex request turns the lamp red.
- A Codex permission request turns the lamp yellow.
- Codex completion turns the lamp green.
- When the App is not running, Agent can still write event, session, and snapshot files.
- When the App starts, it can read the latest state.
- SQLite is not required.
- A background service is not required.
- The automated validation script can prove the code path is usable through a simulated hook chain.

## 5. Phase 2: Multi-Session And Diagnostics

### Goal

Make the product usable for daily work by solving multi-session, trust, path, and diagnostics issues while keeping the main window small.

### Tasks

- Implement multi-session storage.
- Implement traffic-light main state aggregation:

```text
Waiting > Failed > Thinking > Completed > Idle > Stale > Unknown
```

- Implement the lower-right task-count badge.
- Implement the task drawer opened by clicking the badge.
- The task drawer displays:

```text
task prompt excerpt
state badge
workspace summary
running duration or completed duration
single-row delete entry point
```

- Implement hook trust guidance.
- Put diagnostics in the tray menu and diagnostics export instead of keeping them permanently in the main window.
- Implement the tray menu.
- Implement stale session expiration.
- Implement completed-session cleanup.

### Deliverables

- Compact floating traffic-light window.
- Task-count badge and task drawer.
- Single-task deletion.
- Diagnostics export.
- Tray menu.
- Hook reinstall entry point.
- Session cleanup strategy.

### Acceptance Criteria

- When multiple Codex sessions run at the same time, the main lamp aggregates by priority.
- The task drawer explains why the main lamp is in the current state.
- The top of each task row shows the task prompt excerpt and keeps it after completion.
- Task rows do not show adapter internals such as `source/adapter`.
- The user can delete a current task row from the drawer.
- If hooks are not trusted or a hook fails, diagnostics export can locate the issue.
- Completed sessions can be hidden automatically or cleaned manually.

## 6. Phase 3: Generic Adapters

### Goal

Expand the product from a Codex-specific tool into a generic AI / Agent signal light.

### Tasks

- Harden the generic `emit` command:

```text
signal-agent emit --source cli --state running --title "AutoAgent"
signal-agent emit --source generic --state waiting --title "Need approval"
signal-agent emit --source generic --state completed --title "Done"
```

- Implement a generic JSON drop-in event directory.
- Implement display for source / adapter fields.
- Support non-Codex session IDs.
- Add an adapter registry:

```text
codex-hooks
generic-cli
file-drop
```

### Deliverables

- Generic `emit` contract.
- Generic event protocol documentation.
- Non-Codex example scripts.
- Adapter registry.

### Acceptance Criteria

- Red/yellow/green states can be triggered by command without running Codex.
- Any external tool can connect by writing JSON or calling Agent.
- The UI can distinguish event sources.
- Core does not depend on Codex types.

## 7. Phase 4: Browser Adapters

### Goal

Support deployment to AI websites or Web Agents that require waiting.

### Tasks

- Implement a userscript prototype first.
- Implement a browser extension later.
- Define the web state recognition interface:

```text
generating -> TaskStarted
needs confirmation -> UserActionRequired
finished -> TaskCompleted
error -> TaskFailed
```

- Support tab ID, URL, and title as session context.
- Report to Agent through a local companion bridge or file.

### Deliverables

- Userscript prototype.
- Browser extension prototype.
- Adaptation notes for ChatGPT / Claude / Gemini and similar sites.
- Web Agent integration documentation.

### Acceptance Criteria

- On at least one AI website, generating an answer turns the lamp red.
- When the website waits for user confirmation or input, the lamp turns yellow.
- After generation finishes, the lamp turns green.
- Normal website usage is not affected.

## 8. Phase 5: Release Quality

### Goal

Bring the product to distributable, diagnosable, and upgradeable quality.

### Tasks

- Generate a portable package:

```text
SignalLight-Portable.zip
  app\SignalLight.App.dll
  agent\SignalLight.Agent.dll
  install-hooks.ps1
  uninstall-hooks.ps1
  hooks\codex-hook.ps1
  README.md
```

- Optionally generate an installer.
- Add version checks.
- Add diagnostics package export.
- Add complete user documentation:

```text
quick start
Codex integration
generic CLI integration
browser integration
troubleshooting
uninstall instructions
```

- Add an end-to-end validation checklist.

### Deliverables

- Portable zip.
- Installer.
- User documentation.
- Diagnostics export.
- Release checklist.

### Acceptance Criteria

- A new machine can be configured from the extracted package by following the documentation.
- It can run without installing extra runtime components beyond the chosen runtime strategy.
- No invalid commands remain after hooks are uninstalled.
- The diagnostics package helps locate untriggered hooks, path errors, missing Agent files, and similar issues.

## 9. Test Plan

### Core Tests

- State-machine transitions.
- Aggregation priority.
- Session de-duplication.
- Session expiration.
- JSON reads/writes.
- Event protocol version compatibility.

### Agent Tests

- `emit` command argument parsing.
- stdin JSON parsing.
- Missing-field handling.
- Fallback writes.
- Diagnostic file writes.

### Adapter Tests

- Codex hook event mapping.
- Generic CLI event mapping.
- File-drop event import.
- Browser state event mapping.

### App Tests

- Startup smoke test.
- UI refresh after state-file changes.
- Task badge and drawer display.
- Drawer task-row deletion.
- Tray diagnostics export.
- Tray exit.

## 10. Phase Priority Recommendation

Priority order:

```text
Phase 0 -> Phase 1 -> Phase 2
```

After these three phases are complete, the product can already work as a low-dependency local traffic light for Codex.

Then proceed with:

```text
Phase 3
```

This phase is the key step that turns the product from a Codex tool into a generic AI / Agent signal light.

Finally proceed with:

```text
Phase 4 -> Phase 5
```

Browser adapters and release quality can progress in parallel, but large-scale development should not start before the Core protocol is stable.

## 11. Key Risks And Controls

| Risk | Impact | Control |
|---|---|---|
| Codex hook input changes | Codex adapter breaks | Preserve raw payload and maintain the adapter separately |
| Browser site DOM changes | Web adapter instability | Keep per-site adapters and validate with userscript first |
| Over-reliance on background services | Deployment complexity | Do not build a background service in the MVP |
| Introducing SQLite too early | Adds dependency and migration cost | Use JSON for the MVP |
| Product name is bound to Codex | Future extension is limited | Use SignalLight / AI Traffic Signal |
| UI stops being a traffic light | Violates product constraint | Strictly keep red/yellow/green traffic lights as the main UI |

## 12. Current Recommended Next Steps

The Phase 2 compact usable milestone is complete. Recommended next steps:

1. Complete real Codex `/hooks` trust validation and manual red/yellow/green acceptance.
2. Record the acceptance results in `docs/00-progress`.
3. If validation passes, start Phase 3 generic adapter development.
4. If validation fails, first use `diagnostics/latest-hook-context.json` and the diagnostics export package to fix hook path, payload, or trust issues.
