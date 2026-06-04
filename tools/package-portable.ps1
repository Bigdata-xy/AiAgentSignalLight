param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repoRoot "dist"
$packageName = "SignalLight-Portable-$Runtime"
$packageRoot = Join-Path $dist $packageName
$zipPath = Join-Path $dist "$packageName.zip"

New-Item -ItemType Directory -Force -Path $dist | Out-Null

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null

$publishArgs = @(
    "-c", $Configuration,
    "--self-contained", "false",
    "-p:UseAppHost=false"
)

dotnet publish (Join-Path $repoRoot "src\SignalLight.App\SignalLight.App.csproj") @publishArgs -o (Join-Path $packageRoot "app")
dotnet publish (Join-Path $repoRoot "src\SignalLight.Agent\SignalLight.Agent.csproj") @publishArgs -o (Join-Path $packageRoot "agent")
Copy-Item -Recurse -Force (Join-Path $repoRoot "hooks") (Join-Path $packageRoot "hooks")
Copy-Item -Force (Join-Path $repoRoot "tools\install-hooks.ps1") (Join-Path $packageRoot "install-hooks.ps1")
Copy-Item -Force (Join-Path $repoRoot "tools\uninstall-hooks.ps1") (Join-Path $packageRoot "uninstall-hooks.ps1")
Copy-Item -Force (Join-Path $repoRoot "README.md") (Join-Path $packageRoot "README.md")

$licensePath = Join-Path $repoRoot "LICENSE"
if (Test-Path -LiteralPath $licensePath) {
    Copy-Item -Force $licensePath (Join-Path $packageRoot "LICENSE")
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force
Write-Host "Portable package written to $zipPath"
