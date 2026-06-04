# 2026-06-04 Phase 2 Base Milestone

## Milestone

The project has reached the Phase 2 compact usable milestone.

This means Phase 1 code work is mostly complete, and the first usable Phase 2 surfaces now exist:

- multi-session visibility
- compact traffic-light surface
- breathing/pulse lamp animation
- task-count badge and task drawer
- row-level task deletion
- hook trust status
- latest hook diagnostics
- diagnostics export
- tray show/hide/exit actions
- completed-session cleanup
- stale active session handling

Phase 1 automated validation now passes. Real Codex `/hooks` trust validation and WPF visual validation are still manual acceptance items.

## Completed In This Milestone

### Core

- `SignalStateEngine.BuildSnapshot` now supports expiry behavior.
- Active sessions in `Thinking` or `Waiting` become `Stale` after the stale threshold.
- Completed or idle sessions outside the retention window are hidden from snapshots.
- Added tests for stale active sessions and completed-session retention.

### App

- Reworked the WPF window into a compact floating traffic-light surface.
- Kept the red/yellow/green traffic-light UI as the primary visual anchor.
- Added breathing and pulse animation for active lamps.
- Added a task-count badge that opens a current-task drawer.
- Added a drawer list with task prompt excerpt, state, workspace summary, and update age.
- Removed `source/adapter` from the visible task row metadata.
- Added row-level task deletion from the drawer.
- Added diagnostics export to zip.
- Added completed-session cleanup.
- Added a tray icon with Show, Hide, and Exit.
- Moved hook and diagnostics operations into tray submenus.

### Packaging

- Re-ran portable packaging after the WPF/WinForms tray integration.
- Verified `dist\SignalLight-Portable-win-x64.zip` is generated.

### Documentation Encoding

- Rewrote the long Chinese planning, product design, and analysis documents as clean UTF-8 Markdown.
- Removed the mojibake risk from the active documentation notes.

### Phase 1 Validation

- Added `tools\validate-phase1.ps1`.
- The script simulates Codex hook events for red, yellow, and green states.
- The script verifies snapshot, session, event, and latest diagnostics output.

## Verified Commands

```powershell
dotnet build SignalLight.sln
dotnet test SignalLight.sln
tools\validate-phase1.ps1
tools\package-portable.ps1
```

Results:

- Build passed with 0 warnings and 0 errors.
- Core/storage tests passed; Agent tests may be skipped by Windows Application Control policy in this environment.
- Phase 1 validation passed.
- Portable package generated: `dist\SignalLight-Portable-win-x64.zip`.

## Remaining Acceptance Work

- Run real Codex `/hooks` trust flow.
- Verify red state after a real Codex prompt.
- Verify yellow state after a real Codex permission request.
- Verify green state after a real Codex stop/completion.
- Observe WPF refresh, tray behavior, and diagnostic export manually on the desktop.
- Verify task drawer, row deletion, and task prompt excerpt retention with live Codex events.
- Confirm GitHub remote synchronization from a network that can reach github.com.

## Recommended Next Milestone

Phase 3 should start only after the real Codex loop is manually proven.

The next implementation milestone should be:

```text
Generic adapter base:
Agent emit contract hardening + file-drop adapter + non-Codex source display
```
