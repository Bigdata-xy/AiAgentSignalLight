# Manual Phase 1 Validation Checklist

Date: 2026-06-03

Use this checklist to validate the real local Phase 1 loop:

```text
Codex hook -> codex-hook.ps1 -> SignalLight.Agent -> JSON snapshot -> WPF refresh
```

## Preconditions

- `dotnet build SignalLight.sln` passes.
- `dotnet test` passes.
- `SignalLight.App.exe` and `SignalLight.Agent.exe` exist under `src/*/bin/Debug`.
- Codex can access the configured `hooks.json`.

## Steps

1. Install hooks:

```powershell
.\tools\install-hooks.ps1
```

2. Open Codex and run `/hooks`.

3. Trust the SignalLight hook commands when prompted.

4. Start the WPF app:

```powershell
dotnet run --project .\src\SignalLight.App\SignalLight.App.csproj
```

5. Trigger a Codex prompt.

Expected:

- `snapshot.json` is created under `%LOCALAPPDATA%\SignalLight`.
- The WPF light changes to red.

6. Trigger a permission request.

Expected:

- The WPF light changes to yellow.

7. Let the Codex task finish.

Expected:

- The WPF light changes to green.

8. Confirm events and sessions exist:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\SignalLight\events"
Get-ChildItem "$env:LOCALAPPDATA\SignalLight\sessions"
```

9. Run uninstall:

```powershell
.\tools\uninstall-hooks.ps1
```

Expected:

- SignalLight-owned hook entries are removed.
- Unrelated user hook entries remain.

## Notes

- The automated tests already validate hook installation against a temporary `CODEX_HOME`.
- This checklist is still required because Codex trust prompts and live WPF refresh behavior are user-environment dependent.
