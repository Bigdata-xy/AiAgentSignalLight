# 2026-06-05 Documentation And State Policy Consolidation Record

This update only consolidated project documentation and unified descriptive logic. No Git operations were performed.

## Update Scope

Added:

- `docs/04-protocol/state-completion-policy.md`

Recovered and rewrote as UTF-8:

- `README.zh-CN.md`
- `docs/05-engineering/reproducible-project-state-guide.md`

Updated for consistency:

- `README.md`
- `docs/03-architecture/architecture.md`
- `docs/04-protocol/event-protocol.md`
- `docs/07-user-guide/usage-tutorial.md`
- `docs/README.md`
- `docs/00-progress/current-completion-record.md`

## Unified State Policy

| Lamp | Meaning | Source |
|---|---|---|
| Red | Running | `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, or Agent running/thinking/red |
| Yellow | Waiting for manual authorization | `PermissionRequest` |
| Green | Task completed or idle | Codex `Stop` or explicit Codex authorization cancellation / interruption |

Completion decisions accept only:

- Codex `Stop` hook.
- Codex explicitly cancels authorization or interrupts the current turn.

These manual completion states are no longer accepted as real completion authority:

```text
completed
done
idle
green
```

This avoids incorrectly turning green while a task is still running.

## Authorization Flow Policy

Current real state transitions:

```text
Submit prompt -> red
Codex requests authorization -> yellow
Authorization is approved and a continued-execution signal is received -> red
Codex Stop or explicit cancellation/interruption -> green
```

Notes:

- Yellow has no fixed timeout.
- SignalLight does not guess from process command lines whether the user clicked `Yes`.
- After authorization is approved, SignalLight switches back to red only after receiving a later Codex `PreToolUse`, `PostToolUse`, or explicit approval record.
- If the user selects `No` and Codex interrupts the current turn, SignalLight uses explicit Codex cancellation / interruption text to move the task to completed / idle.

## Green Confirmation Window

After the UI receives a completion event, it does not show green immediately. It waits for a 0.8 second confirmation window.

If a new red or yellow event appears during the window, green display is canceled.

Purpose:

- Avoid a brief green flash during execution.
- Avoid giving the user a false completion signal when Codex enters the next execution stage immediately after `Stop`.

## Documentation Maintenance Requirements

- Current facts are determined by the code, scripts, and `docs/04-protocol/state-completion-policy.md`.
- The user tutorial explains how to use the product.
- The engineering state document explains how to reproduce the project state, how to troubleshoot it, and why the current implementation is shaped this way.
- Historical progress documents may keep process history, but new milestone records must explicitly update the current implementation if history conflicts with it.
- All formal documentation must remain UTF-8.
