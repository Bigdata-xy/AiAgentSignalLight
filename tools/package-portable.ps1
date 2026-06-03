param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repoRoot "dist\portable"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

dotnet publish (Join-Path $repoRoot "src\SignalLight.App\SignalLight.App.csproj") -c $Configuration -r $Runtime --self-contained true -o (Join-Path $dist "app")
dotnet publish (Join-Path $repoRoot "src\SignalLight.Agent\SignalLight.Agent.csproj") -c $Configuration -r $Runtime --self-contained true -o (Join-Path $dist "agent")
Copy-Item -Recurse -Force (Join-Path $repoRoot "hooks") (Join-Path $dist "hooks")
Copy-Item -Force (Join-Path $repoRoot "tools\install-hooks.ps1") (Join-Path $dist "install-hooks.ps1")
Copy-Item -Force (Join-Path $repoRoot "tools\uninstall-hooks.ps1") (Join-Path $dist "uninstall-hooks.ps1")
