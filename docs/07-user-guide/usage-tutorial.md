# SignalLight Usage Tutorial

This document explains how to use the SignalLight portable package with Codex and view AI / Agent runtime state through the desktop red/yellow/green traffic light.

## 1. Current Usable State

The current version can already be used as a local MVP.

Verified:

- The project builds.
- Automated tests pass.
- The Phase 1 simulated Codex hook chain passes.
- The portable package can be generated.

Still requires the user to complete manually in the real environment:

- Run `/hooks` in Codex.
- Trust the SignalLight hook commands.
- Observe red, yellow, and green state changes with real Codex tasks.

## 2. State Meanings

| Lamp | Internal state | Meaning |
|---|---|---|
| Red | `Thinking` | Codex / AI / Agent is processing a task |
| Yellow | `Waiting` | Waiting for user input, permission confirmation, or manual action |
| Green | `Completed` / `Idle` | Task completed or currently idle |
| Exception/stale | `Failed` / `Stale` / `Unknown` | Task failed, state has not updated for too long, or state cannot be determined |

Current completion decisions are strict: only the Codex `Stop` hook, or an explicit Codex authorization cancellation / current-turn interruption, can make a task enter green. Manual or test commands that write `completed`, `done`, `idle`, or `green` do not mark a real task complete.

Yellow has no fixed duration. After Codex requests authorization, SignalLight stays yellow until the user confirms. After authorization is approved, SignalLight switches back to red only after receiving a later Codex `PreToolUse`, `PostToolUse`, or explicit approval record.

Full state policy:

```text
docs/04-protocol/state-completion-policy.md
```

## 3. Recommended Usage: Portable Package

Portable package location:

```text
B:\AI Traffic Signal\dist\SignalLight-Portable-win-x64.zip
```

Recommended startup from the project root:

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

This script automatically extracts the latest portable package, installs hooks, and starts SignalLight.

The startup script runs SignalLight as an independent background process. After the script returns, the PowerShell window can be closed and the traffic light will keep running. Use `Exit` in the system tray to quit.

### 3.1 Extract The Portable Package

If manual extraction is needed, open PowerShell and run:

```powershell
cd "B:\AI Traffic Signal"
Expand-Archive -LiteralPath ".\dist\SignalLight-Portable-win-x64.zip" -DestinationPath ".\dist\SignalLight-Portable-win-x64-run" -Force
cd ".\dist\SignalLight-Portable-win-x64-run"
```

After extraction, the directory should contain:

```text
app\SignalLight.App.dll
agent\SignalLight.Agent.dll
hooks\codex-hook.ps1
install-hooks.ps1
uninstall-hooks.ps1
README.md
LICENSE
```

## 4. Install Codex Hooks

Run this in the portable package directory:

```powershell
.\install-hooks.ps1
```

This script writes SignalLight hook commands into the Codex hooks configuration:

```text
%USERPROFILE%\.codex\hooks.json
```

If `CODEX_HOME` is set, it writes to:

```text
%CODEX_HOME%\hooks.json
```

The script registers these Codex events:

```text
UserPromptSubmit
PreToolUse
PostToolUse
PermissionRequest
Stop
SessionStart
```

When these events trigger, Codex directly calls:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File hooks\codex-hook.ps1 -EventName <event>
```

The current main chain lets `hooks\codex-hook.ps1` read Codex stdin directly and write local JSON. This avoids Windows Smart App Control / application control policy blocking `SignalLight.Agent.dll` inside the portable package.

The hook ultimately writes state to:

```text
%LOCALAPPDATA%\SignalLight\
```

## 5. Start SignalLight

Run this in the portable package directory:

```powershell
dotnet .\app\SignalLight.App.dll
```

DLL startup is currently recommended because it can avoid Windows Smart App Control blocking local unsigned exe files.

After startup, a small desktop traffic-light window appears. The window can be dragged and also shows a tray icon. When started through `start-signal-light.ps1`, the App does not depend on the current PowerShell window; closing the terminal does not close the traffic light.

The main window shows only the red, yellow, and green lamps plus the task-count badge by default. Other operations are hidden in the tray menu and task drawer so the window does not occupy too much screen space.

The first level of the tray menu keeps only common entry points:

- Show / Hide
- Hooks
- Diagnostics
- Exit

The `Hooks` submenu contains `Install` and `Uninstall` for connecting or removing Codex hooks.

The `Diagnostics` submenu contains `Open data`, `Export`, and `Clear done` for viewing local data, exporting troubleshooting packages, and clearing completed sessions.

## 6. Trust Hooks In Codex

Open Codex and enter this in any session:

```text
/hooks
```

After SignalLight hook commands appear, choose to trust them.

This step is required. Before trust is granted, Codex will not execute the hooks and SignalLight will not receive real Codex events.

## 7. Normal Codex Usage

After hooks are installed and trusted, use Codex normally.

Typical state changes:

```text
Submit prompt -> red
Codex requests permission -> yellow
Authorization approved and continued-execution signal received -> red
Task completed -> green
```

If `No` is selected on the authorization screen, Codex interrupts the current turn. SignalLight only recognizes explicit Codex cancellation / interruption signals and moves that task to idle/completed display; it does not misclassify completion merely because words such as `denied` or `canceled` appear in ordinary text.

After receiving `Stop`, the UI first waits for a 0.8 second confirmation window. If a new red or yellow event arrives during that time, green is not displayed, avoiding a brief green flash during execution.

SignalLight uses the lower-right badge to show multi-session completion counts. For example, `2/3` means 2 of 3 visible sessions are completed.

Click the lower-right badge to open the task drawer. Each task row in the drawer shows:

- Task prompt excerpt or workspace directory name
- Current state
- Workspace summary
- Running duration or completed duration

The drawer does not show `source/adapter`. After a task completes, it still keeps the original task excerpt and does not become `Codex`.

The `x` on the right side of each row deletes that task record. After deletion, SignalLight refreshes the task list, count badge, and main lamp state.

## 8. Verify Connectivity

### 8.1 Check In The Window

If the connection works, the main SignalLight lamp color changes with Codex state:

```text
Submit prompt -> red blinking
Permission request -> yellow blinking
Authorization approved and continued-execution signal received -> red blinking
Task completed -> green blinking
```

For detailed hook diagnostics, use `Diagnostics > Open data` or `Diagnostics > Export` from the tray menu.

To view current task details, click the task-count badge in the lower-right corner of the window to open the drawer.

### 8.2 Check In PowerShell

View the local data directory:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\SignalLight"
```

View event files:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\SignalLight\events"
```

View the current snapshot:

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\snapshot.json"
```

View the latest hook diagnostics:

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-context.json"
```

## 9. Export Diagnostics

Click this in the SignalLight tray menu:

```text
Diagnostics > Export
```

The diagnostics package is generated under:

```text
%LOCALAPPDATA%\SignalLight\diagnostics\
```

The diagnostics package usually contains:

- `snapshot.json`
- `sessions/*.json`
- `events/*.json`
- `diagnostics/latest-hook-context.json`
- Codex `hooks.json`

## 10. Clear Completed Sessions

Click this in the SignalLight tray menu:

```text
Diagnostics > Clear done
```

It deletes local session files that are already completed or idle and rebuilds the current snapshot.

It does not delete event history files.

## 11. Uninstall SignalLight Hooks

If Codex should no longer connect to SignalLight, run this in the portable package directory:

```powershell
.\uninstall-hooks.ps1
```

It removes only hook entries written by SignalLight and keeps any other user hooks.

## 12. Daily Commands

Recommended first-time use:

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

Manual first-time use:

```powershell
cd "B:\AI Traffic Signal"
Expand-Archive -LiteralPath ".\dist\SignalLight-Portable-win-x64.zip" -DestinationPath ".\dist\SignalLight-Portable-win-x64-run" -Force
cd ".\dist\SignalLight-Portable-win-x64-run"
.\install-hooks.ps1
dotnet .\app\SignalLight.App.dll
```

Then run this in Codex:

```text
/hooks
```

Trust the SignalLight hook commands.

For daily startup later, this is usually enough:

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

After the command finishes, the terminal window can be closed. Use the tray menu item `Exit` to exit SignalLight.

Only rerun this when hooks are changed, deleted, or need to be reconnected:

```powershell
.\install-hooks.ps1
```

## 13. Run From Development Directory

If the portable package is not used, run directly from the source directory:

```powershell
cd "B:\AI Traffic Signal"
.\tools\install-hooks.ps1
dotnet run --project .\src\SignalLight.App\SignalLight.App.csproj
```

Then run this in Codex:

```text
/hooks
```

## 14. Automated Phase 1 Chain Validation

The project includes an automated validation script that simulates Codex hooks:

```powershell
cd "B:\AI Traffic Signal"
.\tools\validate-phase1.ps1
```

It verifies:

- `UserPromptSubmit` -> `Thinking`
- `PermissionRequest` -> `Waiting`
- `PreToolUse` -> `Thinking`
- `PostToolUse` -> `Thinking`
- `Stop` -> `Completed`
- Manual `completed/done/idle/green` is not treated as a real completion source
- `snapshot.json`
- `sessions/*.json`
- `events/*.json`
- `diagnostics/latest-hook-context.json`

Note: this script validates the simulated hook chain. It is not equivalent to the real Codex `/hooks` trust flow.

## 15. FAQ

### 15.1 The Window Does Not Change

Check:

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-context.json"
```

If the file does not exist, Codex hooks usually did not trigger.

Fix:

1. Confirm `.\install-hooks.ps1` has been executed.
2. Run `/hooks` in Codex.
3. Trust the SignalLight hook commands.
4. Submit another Codex prompt.

### 15.2 Codex Reports `hooks.json parse failed`

If Codex shows:

```text
failed to parse hooks config ... expected value at line 1 column 1
```

The usual cause is an old `hooks.json` written with BOM or damaged file content. Reinstall hooks with the current version:

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

The current installation script writes `hooks.json` as UTF-8 without BOM.

### 15.3 `SignalLight.App.exe` Is Blocked By Application Control Policy

If PowerShell shows:

```text
The application control policy has blocked this file.
```

The current version has switched to the DLL startup strategy. Use the latest startup script first:

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

If it is still blocked, the system may also restrict local DLL loading through `dotnet`. In that case, add `B:\AI Traffic Signal` or the portable package directory to the system allow list.

### 15.4 Hooks Show As Not Installed

Run again:

```powershell
.\install-hooks.ps1
```

Then run this in Codex:

```text
/hooks
```

### 15.5 Agent Missing

If diagnostics show that Agent cannot be found, confirm the portable package directory structure has not been changed:

```text
agent\SignalLight.Agent.dll
hooks\codex-hook.ps1
```

Do not copy only the `app` directory to run it. Hooks also need the `agent` and `hooks` directories.

### 15.6 Completely Stop Codex Integration

Run:

```powershell
.\uninstall-hooks.ps1
```

Then close the SignalLight window or exit from the tray menu.

### 15.7 It Does Not Immediately Turn From Yellow To Red After Authorization

This usually does not mean the UI is stuck. It means Codex did not write an approval record that SignalLight can reliably recognize at the instant authorization was approved.

SignalLight currently does not use process command lines as fallback detection, because that can match the watcher process itself and turn red before the user clicks `Yes`.

Current reliable rule:

```text
PermissionRequest -> yellow
PreToolUse/PostToolUse/explicit approval -> red
Stop or explicit Codex cancellation/interruption -> green
```

To troubleshoot the real event order, check:

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-context.json"
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-permission-watch.json"
```
