# SignalLight Project State And Reproducibility Guide

Date: 2026-06-05

This document records the current real implementation, startup method, state policy, resolved issues, and verification process. It does not include Git push operations.

## 1. Project Positioning

SignalLight is a local AI / Agent state signal light. The current MVP prioritizes Codex CLI hooks: it converts the Codex task lifecycle into local JSON state files, then shows the three primary states, red, yellow, and green, in a small Windows WPF window.

Current usable scope:

- Windows WPF desktop UI.
- Local Codex CLI hooks.
- Local JSON storage.
- Portable zip package.
- One-command startup script.
- Task drawer, task deletion, tray actions, and diagnostics export.

Currently not included:

- A replacement Codex client.
- A cloud service.
- A cross-platform desktop release.
- A signed official Windows installer.

## 2. Current Architecture

```text
Codex lifecycle hook
-> powershell hooks\codex-hook.ps1 -EventName <event>
-> local JSON events / sessions / snapshot
-> SignalLight.App WPF FileSystemWatcher refresh
-> traffic-light UI / task drawer / tray diagnostics
```

Main projects:

- `src/SignalLight.Core`: events, sessions, state machine, and aggregation rules.
- `src/SignalLight.Storage`: local JSON reads/writes and path management.
- `src/SignalLight.Agent`: CLI event ingestion entry point.
- `src/SignalLight.Adapters`: adapter contracts for Codex, browsers, and other sources.
- `src/SignalLight.App`: WPF traffic-light UI.
- `tools`: hook installation, packaging, and validation scripts.
- `hooks`: PowerShell hook templates and compatibility scripts.
- `docs`: planning, architecture, protocol, engineering, tutorial, and progress documents.

## 3. State Policy

The authoritative state policy is:

```text
docs/04-protocol/state-completion-policy.md
```

Brief rules:

| Codex event | SignalLight state | Lamp |
|---|---|---|
| `UserPromptSubmit` | `Thinking` | Red |
| `PermissionRequest` | `Waiting` | Yellow |
| `PreToolUse` | `Thinking` | Red |
| `PostToolUse` | `Thinking` | Red |
| `Stop` | `Completed` | Green |
| `SessionStart` | ignored | No change |

Completion state is accepted only from:

- Codex `Stop` hook.
- Codex explicitly cancels authorization or interrupts the current turn.

Manual `Agent emit --state completed/done/idle/green` is ignored and cannot mark a task complete.

Yellow has no fixed duration. After `PermissionRequest`, if no real follow-up signal arrives, the UI remains yellow. After the user approves authorization, the UI switches back to red only after receiving `PreToolUse`, `PostToolUse`, or an explicit approval record.

After the UI receives a completion event, it uses a 0.8 second green confirmation window. If a new red or yellow event appears during the window, green display is canceled.

## 4. Local Data

Default data directory:

```text
%LOCALAPPDATA%\SignalLight
```

Main files:

- `snapshot.json`: current aggregate snapshot.
- `sessions/*.json`: latest state for each task or session.
- `events/*.json`: event history.
- `diagnostics/latest-hook-context.json`: latest hook input, parse result, and mapped state.
- `diagnostics/latest-hook-error.json`: latest internal hook error.
- `diagnostics/latest-permission-watch.json`: latest authorization watcher result.
- `diagnostics/hooks/*.json`: historical hook diagnostics.
- `diagnostics/permission-watch/*.json`: historical authorization watcher diagnostics.

## 5. Startup And Runtime

Recommended one-command startup:

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

The script performs:

1. Locate `dist\SignalLight-Portable-win-x64.zip`.
2. Extract it to `dist\SignalLight-Portable-win-x64-run`.
3. Run `install-hooks.ps1` to write Codex hooks.
4. Stop existing SignalLight App instances.
5. Start `dotnet app\SignalLight.App.dll` in the background.
6. Write logs to `.local\start-signal-light.out.log` and `.local\start-signal-light.err.log`.

After startup, the PowerShell terminal can be closed. To exit SignalLight, use the system tray menu item `Exit`.

## 6. Hooks Installation Logic

The installation script writes:

```text
%USERPROFILE%\.codex\hooks.json
```

If `CODEX_HOME` is set, it writes:

```text
%CODEX_HOME%\hooks.json
```

Current Codex lifecycle hook command shape:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "UserPromptSubmit"
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "PreToolUse"
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "PostToolUse"
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "PermissionRequest"
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "Stop"
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "SessionStart"
```

After installation, run this in Codex:

```text
/hooks
```

Then trust the SignalLight commands. Before they are trusted, Codex will not execute the hooks.

## 7. Resolved Issues

| Issue | Current handling |
|---|---|
| `hooks.json` parse failure | The installation script writes UTF-8 without BOM; damaged files are backed up and rebuilt |
| Mojibake in Chinese task titles | Chinese documents were rewritten as UTF-8; hook diagnostics keep the raw payload for troubleshooting |
| `SignalLight.Agent.dll` blocked by Smart App Control | Codex lifecycle hooks now call the PowerShell hook script directly |
| Green before task completion | `SessionStart` is ignored; completion is accepted only from Codex `Stop` or explicit cancellation/interruption |
| Yellow remains after authorization | Added `PreToolUse`, `PostToolUse`, and watcher diagnostics, but unreliable process detection is not used |
| Red before the user clicked Yes | Removed command-line process fallback detection to avoid matching the watcher itself |
| Title becomes `Codex` / `true` after completion | Core ignores placeholder titles and preserves the real prompt excerpt |
| Duration becomes `0s` after completion | `StartedAt` is used to calculate running/completed duration |
| Multiple terminals affect each other | Prefer session/conversation/terminal/process IDs and avoid completing other active tasks arbitrarily |
| Startup requires keeping a terminal open | The one-command script starts `dotnet app\SignalLight.App.dll` in the background; exit through the tray |
| Drawer information is redundant | The drawer keeps only task excerpt, state, workspace summary, and duration |

## 8. Build And Packaging

Build:

```powershell
cd "B:\AI Traffic Signal"
dotnet build SignalLight.sln
```

Package:

```powershell
cd "B:\AI Traffic Signal"
.\tools\package-portable.ps1
```

Portable package output:

```text
dist\SignalLight-Portable-win-x64.zip
dist\SignalLight-Portable-win-x64\
```

The portable package should contain:

```text
app\SignalLight.App.dll
app\SignalLight.App.deps.json
app\SignalLight.App.runtimeconfig.json
agent\SignalLight.Agent.dll
agent\SignalLight.Agent.deps.json
agent\SignalLight.Agent.runtimeconfig.json
hooks\codex-hook.ps1
install-hooks.ps1
uninstall-hooks.ps1
README.md
LICENSE
```

## 9. Verification

Recommended verification:

```powershell
cd "B:\AI Traffic Signal"
dotnet test SignalLight.sln
.\tools\validate-phase1.ps1
.\tools\package-portable.ps1
```

Currently verified as passing:

- `dotnet test SignalLight.sln`
- `tools\validate-phase1.ps1`
- `tools\package-portable.ps1`
- Manual `Agent emit --state completed/done/idle/green` does not switch the snapshot to completed

Real Codex verification:

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

Then run this in Codex:

```text
/hooks
```

After trusting hooks, submit a real prompt and observe:

```text
Submit prompt -> red blinking
Permission request -> yellow blinking
Authorization approved and continued-execution signal received -> red blinking
Task ends or explicit cancellation occurs -> green blinking
```

## 10. Diagnostic Commands

View the latest hook input:

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-context.json"
```

View the authorization watcher:

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-permission-watch.json"
```

View the current snapshot:

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\snapshot.json"
```

View session files:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\SignalLight\sessions"
```

View startup error logs:

```powershell
Get-Content "B:\AI Traffic Signal\.local\start-signal-light.err.log"
```

View hooks configuration:

```powershell
Get-Content "$env:USERPROFILE\.codex\hooks.json"
```

## 11. Platform Assessment

Currently directly usable:

- Windows 10 / Windows 11 x64
- .NET 8 Desktop Runtime or .NET 8 SDK
- Codex CLI
- PowerShell

Currently not fully directly usable:

- macOS
- Ubuntu / other Linux

Reasons:

- `SignalLight.App` is WPF and targets the Windows desktop.
- The current portable package target is `win-x64`.
- Startup scripts, hook installation scripts, and tray behavior are designed for Windows / PowerShell.

Portable parts:

- `SignalLight.Core`
- `SignalLight.Storage`
- `SignalLight.Agent`

Cross-platform support would require a separate Avalonia, MAUI, Electron, Tauri, or Web UI implementation, plus rewritten hook installation scripts.

## 12. Documentation Maintenance Rules

- Current facts are determined by the code, scripts, and `docs/04-protocol/state-completion-policy.md`.
- The user tutorial explains how to use the product.
- This document explains how to reproduce the project state, how to troubleshoot it, and why the current implementation is shaped this way.
- Historical progress documents may keep process history, but if they conflict with the current implementation, new milestone records should explicitly update the current state.
- All formal documentation must be saved as UTF-8.
