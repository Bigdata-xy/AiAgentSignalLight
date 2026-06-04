# 2026-06-04 Real Codex Hook Debug Record

## Summary

Real Codex hook validation is currently normal after switching the installed Codex hooks from direct Agent DLL execution to the pure PowerShell hook script.

The working installed hook shape is:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "<event>"
```

The previous installed hook shape is no longer recommended in this Smart App Control environment:

```text
dotnet "<portable-root>\agent\SignalLight.Agent.dll" hook --event "<event>"
```

## Symptoms Observed

- `UserPromptSubmit hook (failed)` with `hook exited with code 1`.
- `PermissionRequest`, `PreToolUse`, `PostToolUse`, and `Stop` also reported hook failures after the direct Agent DLL hook was installed.
- Codex appeared to be disconnected from SignalLight because hook failure interrupted the turn.
- A stale `dotnet ... SignalLight.Agent.dll hook --event PermissionRequest` process was observed and removed.

## Root Cause

The installed Codex hook command pointed to:

```text
dotnet "B:\AI Traffic Signal\dist\SignalLight-Portable-win-x64-run\agent\SignalLight.Agent.dll" hook --event ...
```

Manual reproduction showed:

```text
System.IO.FileLoadException
应用程序控制策略已阻止此文件
0x800711C7
```

This means Windows Application Control / Smart App Control blocked loading `SignalLight.Agent.dll` from the portable run directory. When Codex invoked the hook, the process exited non-zero and Codex reported hook failure.

## Fix Applied

`tools/install-hooks.ps1` now installs Codex hooks that call:

```text
hooks\codex-hook.ps1
```

instead of:

```text
agent\SignalLight.Agent.dll
```

The pure PowerShell hook writes SignalLight JSON state directly:

```text
%LOCALAPPDATA%\SignalLight\events
%LOCALAPPDATA%\SignalLight\sessions
%LOCALAPPDATA%\SignalLight\snapshot.json
%LOCALAPPDATA%\SignalLight\diagnostics
```

The hook script also has a top-level `trap`: if hook internals fail, it writes diagnostics and exits `0`, so SignalLight errors do not interrupt Codex.

## Event Mapping

Current Codex hook mapping:

```text
UserPromptSubmit  -> Thinking  -> red
PermissionRequest -> Waiting   -> yellow
PreToolUse        -> Thinking  -> red
PostToolUse       -> Thinking  -> red
Stop              -> Completed -> green
SessionStart      -> ignored
```

`PostToolUse` was added as an additional recovery point because real Codex permission approval does not always produce a reliable `PreToolUse` transition visible to SignalLight. If authorization is canceled and no tool execution continues, there should be no tool resume event.

## Manual Verification

After repackaging and running:

```powershell
.\start-signal-light.ps1
```

`hooks.json` was confirmed to contain PowerShell hook commands for:

```text
UserPromptSubmit
PermissionRequest
PreToolUse
PostToolUse
Stop
SessionStart
```

Manual execution of every installed event returned exit code `0`:

```text
UserPromptSubmit  ExitCode=0
PermissionRequest ExitCode=0
PreToolUse        ExitCode=0
PostToolUse       ExitCode=0
Stop              ExitCode=0
SessionStart      ExitCode=0
```

Real Codex testing then showed the expected event sequence was being written:

```text
taskStarted
userActionRequired
taskStarted
```

This corresponds to:

```text
red -> yellow -> red
```

Task completion still depends on Codex emitting `Stop`; when `Stop` is emitted it maps to green.

2026-06-05 policy update:

- Codex `Stop` remains the normal completion source.
- Explicit Codex authorization cancel / turn interruption can also complete the current waiting task.
- Manual `SignalLight.Agent emit --state completed/done/idle/green` is ignored and cannot mark a task complete.
- Generic denied/canceled words in unrelated payload fields are not treated as completion.
- Process command-line detection is not used as a permission approval fallback because it can falsely turn red before the user clicks `Yes`.

## Operational Notes

After hook command changes, run in Codex:

```text
/hooks
```

Trust all SignalLight hooks again if review is shown.

If Codex reports hook failures again, inspect:

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-context.json"
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-error.json"
Get-Content "$env:USERPROFILE\.codex\hooks.json"
```

Expected hook commands should reference:

```text
codex-hook.ps1
```

They should not reference:

```text
SignalLight.Agent.dll
```

for Codex lifecycle hooks in this machine environment.
