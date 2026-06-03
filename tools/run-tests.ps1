$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
dotnet test $repoRoot --no-restore
