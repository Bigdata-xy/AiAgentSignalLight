# Smart App Control Blocking And DLL Startup Strategy

Date: 2026-06-04

## 1. Symptom

When running the apphost exe from an early portable package, PowerShell may show:

```text
SignalLight.App.exe cannot run: the application control policy has blocked this file.
```

At the same time, Codex hooks may already have been installed successfully to:

```text
C:\Users\<user>\.codex\hooks.json
```

This means hook configuration writes and App process startup are two separate issues. Hooks can install successfully, but if the App or Agent exe is blocked by the system, the red/yellow/green state chain still cannot close the loop.

## 2. Troubleshooting Conclusion

AppLocker logs show:

```text
The AppLocker component is not available on this SKU.
```

This means the current block is not directly caused by AppLocker rules.

Code Integrity logs show:

```text
ProviderName: Microsoft-Windows-CodeIntegrity
Id: 3118
Message: Smart App Control Block Details
```

Therefore, the root cause is classified as:

```text
Smart App Control / Code Integrity blocking local unsigned exe files.
```

## 3. Why The Initial exe Could Run

Being able to run at first does not mean the exe will remain trusted after later repackaging. Windows application control policy may consider:

- File hash.
- File signature.
- File origin marker.
- File reputation.
- Path policy.
- Current Smart App Control state.

Each time `dotnet publish` or packaging runs again, the contents of `SignalLight.App.exe` and `SignalLight.Agent.exe` may change, and their file hashes also change. Even if the filenames are the same, the system may treat them as new unknown programs.

If the old run directory is deleted and a new package is extracted again, the trust state of the old runnable exe does not automatically transfer to the new file.

## 4. Difference Between exe And DLL Startup

### exe Mode

```powershell
.\app\SignalLight.App.exe
.\agent\SignalLight.Agent.exe
```

Advantages:

- Runs directly like a normal Windows program.
- Can be started by double-clicking.
- Suitable for signed official release environments already trusted by the system.

Disadvantages:

- The current exe is a locally built unsigned program.
- Smart App Control may identify it as unknown and block it.
- Repackaging changes the hash and may trigger blocking again.

### DLL + dotnet Mode

```powershell
dotnet .\app\SignalLight.App.dll
dotnet .\agent\SignalLight.Agent.dll
```

Advantages:

- The actual startup process is the system-trusted `dotnet.exe`.
- It can avoid Smart App Control blocking local unsigned apphost exe files.
- App UI, Codex hooks, Agent writes, task drawer, and task deletion behavior remain unchanged.

Disadvantages:

- Requires .NET 8 Runtime or SDK installed on the machine.
- A DLL cannot be started by double-clicking directly.
- The startup command is less intuitive than an exe.

## 5. Current Adopted Strategy

The project keeps all functionality, but the portable package now uses framework-dependent DLL startup.

The packaging script uses:

```powershell
--self-contained false
-p:UseAppHost=false
```

It generates:

```text
app\SignalLight.App.dll
app\SignalLight.App.runtimeconfig.json
app\SignalLight.App.deps.json

agent\SignalLight.Agent.dll
agent\SignalLight.Agent.runtimeconfig.json
agent\SignalLight.Agent.deps.json
```

It no longer depends on:

```text
app\SignalLight.App.exe
agent\SignalLight.Agent.exe
```

## 6. Current Startup Chain

The user runs:

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

The script runs:

```text
1. Extract dist\SignalLight-Portable-win-x64.zip
2. Run Unblock-File on the zip and extracted files
3. Run install-hooks.ps1
4. Start dotnet app\SignalLight.App.dll in the background
```

Codex hook execution:

```text
Codex hook event
-> powershell hooks\codex-hook.ps1 -EventName <event>
-> write to %LOCALAPPDATA%\SignalLight
-> App watches snapshot / diagnostics and refreshes the red/yellow/green UI
```

In the current local environment, directly loading `SignalLight.Agent.dll` from the portable package can be blocked by application control policy. Therefore, real Codex lifecycle hooks use the pure PowerShell hook script to write JSON directly. The Agent DLL remains in the package for development, manual emit, or future environment compatibility, but it is not the current main entry point for real Codex hooks.

The App is started by the startup script as an independent background process. After the PowerShell terminal closes, SignalLight should keep running. Exit is controlled by the tray menu item `Exit`.

## 7. Verification Commands

Build:

```powershell
dotnet build SignalLight.sln
```

Package:

```powershell
tools\package-portable.ps1
```

Phase validation:

```powershell
tools\validate-phase1.ps1 -Configuration Release
```

Check portable package directories:

```powershell
Get-ChildItem ".\dist\SignalLight-Portable-win-x64\app"
Get-ChildItem ".\dist\SignalLight-Portable-win-x64\agent"
```

You should see `.dll`, `.deps.json`, and `.runtimeconfig.json`; the package should not depend on `SignalLight.App.exe` or `SignalLight.Agent.exe`.

## 8. Future Optional Strategies

If exe mode should be restored later, prioritize:

- Code-sign `SignalLight.App.exe` and `SignalLight.Agent.exe`.
- Use an installer to install into a trusted directory.
- Allow the release directory in Windows security policy.
- Establish stable versioning and release hashes to reduce frequent unknown-file changes.

Before signing is available, DLL + dotnet mode is the more stable runtime strategy in the current local Smart App Control environment.
