$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$zipPath = Join-Path $root "dist\SignalLight-Portable-win-x64.zip"
$runDir = Join-Path $root "dist\SignalLight-Portable-win-x64-run"
$installScript = Join-Path $runDir "install-hooks.ps1"
$appDll = Join-Path $runDir "app\SignalLight.App.dll"
$bridgeDll = Join-Path $runDir "bridge\SignalLight.Bridge.dll"
$logDir = Join-Path $root ".local"
$statusLog = Join-Path $logDir "start-signal-light.status.json"

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Write-StartupStatus {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Step,
        [string]$Message = "",
        [int]$ProcessId = 0,
        [bool]$HasExited = $false,
        [int]$ExitCode = 0
    )

    $status = [pscustomobject]@{
        time = [DateTimeOffset]::Now.ToString("o")
        step = $Step
        message = $Message
        processId = $ProcessId
        hasExited = $HasExited
        exitCode = $ExitCode
    }
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($statusLog, ($status | ConvertTo-Json -Depth 5), $encoding)
}

function Stop-ExistingSignalLight {
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            ($_.CommandLine -like "*SignalLight.App.dll*" -or $_.CommandLine -like "*SignalLight.Bridge.dll*") -and
            ($_.CommandLine -like "*$runDir*" -or $_.CommandLine -like "*$root*")
        } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }

    Start-Sleep -Milliseconds 300
}

function Clear-RunDirectory {
    if (-not (Test-Path -LiteralPath $runDir)) {
        return
    }

    $lastError = $null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $runDir -Recurse -Force
            return
        } catch {
            $lastError = $_
            Start-Sleep -Milliseconds (200 * $attempt)
        }
    }

    throw $lastError
}

if (-not (Test-Path -LiteralPath $zipPath)) {
    throw "Portable package was not found: $zipPath"
}

Write-StartupStatus -Step "stopping-existing"
Stop-ExistingSignalLight

Write-StartupStatus -Step "clearing-run-directory"
Clear-RunDirectory

Write-StartupStatus -Step "extracting"
Unblock-File -LiteralPath $zipPath -ErrorAction SilentlyContinue
Expand-Archive -LiteralPath $zipPath -DestinationPath $runDir -Force

Get-ChildItem -LiteralPath $runDir -Recurse -File | ForEach-Object {
    Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $installScript)) {
    throw "install-hooks.ps1 was not found after extraction: $installScript"
}

if (-not (Test-Path -LiteralPath $appDll)) {
    throw "SignalLight.App.dll was not found after extraction: $appDll"
}

if (-not (Test-Path -LiteralPath $bridgeDll)) {
    throw "SignalLight.Bridge.dll was not found after extraction: $bridgeDll"
}

Write-StartupStatus -Step "installing-hooks"
& $installScript

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source

Write-StartupStatus -Step "stopping-before-launch"
Stop-ExistingSignalLight

function Start-DotNetDll {
    param(
        [Parameter(Mandatory=$true)]
        [string]$DllPath,
        [Parameter(Mandatory=$true)]
        [string]$Name
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $dotnet
    $startInfo.Arguments = "`"$DllPath`""
    $startInfo.WorkingDirectory = Split-Path -Parent $DllPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Failed to start $Name."
    }

    Start-Sleep -Milliseconds 800
    $process.Refresh()
    if ($process.HasExited) {
        Write-Warning "$Name exited immediately. See: $statusLog"
        Write-StartupStatus -Step "exited" -Message "$Name exited immediately." -ProcessId $process.Id -HasExited $true -ExitCode $process.ExitCode
        exit $process.ExitCode
    }

    return $process
}

Write-StartupStatus -Step "launching-bridge"
$bridgeProcess = Start-DotNetDll -DllPath $bridgeDll -Name "SignalLight RemoteBridge"

Write-StartupStatus -Step "launching-app"
$process = Start-DotNetDll -DllPath $appDll -Name "SignalLight App"

Write-StartupStatus -Step "running" -Message "SignalLight started." -ProcessId $process.Id
Write-Host "SignalLight started as PID $($process.Id). RemoteBridge started as PID $($bridgeProcess.Id). You can close this terminal; use the tray icon to exit SignalLight."
