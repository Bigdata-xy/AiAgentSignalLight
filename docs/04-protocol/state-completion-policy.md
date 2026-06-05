# SignalLight State And Completion Policy

Date: 2026-06-05

This document is the authoritative state policy for the current project. If README files, user guides, architecture documents, or progress records differ from this document, this document takes precedence.

## 1. State Sources

SignalLight display state comes from local event files:

```text
%LOCALAPPDATA%\SignalLight\events
%LOCALAPPDATA%\SignalLight\sessions
%LOCALAPPDATA%\SignalLight\snapshot.json
```

Codex lifecycle hooks write these files through the PowerShell script:

```text
hooks\codex-hook.ps1
```

Codex lifecycle hooks no longer load `SignalLight.Agent.dll` directly, because Windows Smart App Control / Code Integrity on this machine may block unsigned DLLs inside the portable package.

## 2. Lamp Meanings

| Lamp | Aggregate state | Meaning |
|---|---|---|
| Red | `Thinking` | Codex / AI / Agent is running a task |
| Yellow | `Waiting` | Codex is waiting for user authorization or manual action |
| Green | `Completed` / `Idle` | The current task has truly ended, or there is no active task |
| Exception | `Failed` / `Stale` / `Unknown` | The task failed, the state is stale, or the state cannot be determined |

Aggregate priority:

```text
Waiting > Failed > Thinking > Completed > Idle > Stale > Unknown
```

Therefore, if any task is waiting for authorization, the main lamp displays yellow first. If any task is still running, other completion events must not make the main lamp turn green early.

## 3. Codex Hook Mapping

| Codex event | SignalLight event | Lamp | Description |
|---|---|---|---|
| `UserPromptSubmit` | `TaskStarted` | Red | The user submitted a prompt and the task started |
| `PermissionRequest` | `UserActionRequired` | Yellow | Codex requested manual authorization |
| `PreToolUse` | `TaskStarted` | Red | A tool is about to run and the task continues |
| `PostToolUse` | `TaskStarted` | Red | Codex is still processing the turn after tool execution |
| `Stop` | `TaskCompleted` | Green | The current Codex turn ended |
| `SessionStart` | ignored | No change | Recorded only for diagnostics; it does not write task state |

## 4. Completion Decision

Only two event classes can currently mark a task complete:

1. Codex `Stop` hook.
2. Codex explicitly cancels authorization or interrupts the current turn.

Authorization cancellation / interruption is recognized only from explicit Codex text, for example:

```text
you canceled the request
you cancelled the request
conversation interrupted
<turn_aborted>
turn_aborted
turn aborted
the user interrupted the previous turn on purpose
```

These strings are read only from structured result fields, such as:

```text
tool_response
message
status
decision
permission_resolution
```

A task is not marked complete merely because similar words appear in the original prompt, command text, or unrelated fields.

## 5. Manual `completed` Is Ignored

To prevent test scripts or other tools from incorrectly turning an unfinished task green, the current Agent ignores these manual completion-like states:

```text
completed
done
idle
green
```

In other words, this command will not turn a task green:

```powershell
dotnet SignalLight.Agent.dll emit --state completed
```

Manual emit can still write red, yellow, failed, and heartbeat-like states, but it cannot replace Codex `Stop` as completion authority.

## 6. Authorization Flow

Expected normal flow:

```text
Submit prompt -> red
Codex requests authorization -> yellow
Authorization is approved and execution continues -> red
Codex turn ends -> green
```

Practical limits:

- SignalLight cannot guess whether the user clicked `Yes`.
- If Codex does not immediately write a recognizable approval transcript or follow-up hook after authorization is approved, SignalLight stays yellow.
- SignalLight switches back to red only after receiving a real `PreToolUse`, `PostToolUse`, or explicit approval record.

The project previously tried to detect whether a tool had started by inspecting process command lines. That approach could match the watcher process itself and turn red before the user had authorized the request, so that fallback detection has been removed.

## 7. Yellow Has No Timeout

Yellow has no fixed duration limit. As long as Codex is still waiting for user authorization, SignalLight should remain yellow.

If the user selects `No` and Codex interrupts the current turn, SignalLight uses Codex cancellation / interruption text to move the task to completed / idle.

## 8. Delayed Green Confirmation

After the UI receives `Completed`, it does not display green immediately. It waits for a 0.8 second confirmation window.

If no new red or yellow event arrives within those 0.8 seconds, green is displayed.

If a new `Thinking` or `Waiting` event arrives immediately, green display is canceled and the UI continues showing the real execution state.

This policy avoids a brief green flash when Codex triggers `Stop` during an intermediate stage.

## 9. Diagnostic Files

When troubleshooting state transitions, check these first:

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-context.json"
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-permission-watch.json"
Get-Content "$env:LOCALAPPDATA\SignalLight\snapshot.json"
```

Historical hook diagnostics:

```text
%LOCALAPPDATA%\SignalLight\diagnostics\hooks\
%LOCALAPPDATA%\SignalLight\diagnostics\permission-watch\
```

## 10. Current Verification Status

Verified:

- `dotnet test SignalLight.sln` passes.
- `tools\validate-phase1.ps1` passes.
- `tools\package-portable.ps1` passes.
- Manual `Agent emit --state completed/done/idle/green` does not switch the snapshot to completed.

Still needs continued observation in real Codex environments:

- Whether different Codex versions write an approval transcript immediately after authorization is approved.
- Ownership accuracy for concurrent tasks across multiple terminals when stable session IDs are missing.
