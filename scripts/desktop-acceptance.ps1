#Requires -Version 5.1
param(
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Manual first-period desktop checklist. Not part of `dotnet test`.
# Talks stdio JSON-RPC to a freshly published ComputerUse.Mcp.exe so this
# Cursor session's still-v1 plugin MCP is not required.

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "src\ComputerUse.Mcp\ComputerUse.Mcp.csproj"
$PublishDir = Join-Path $RepoRoot "artifacts\win-x64"
$Harness = Join-Path $RepoRoot "tools\DesktopAcceptance\DesktopAcceptance.csproj"

function Resolve-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $local = Join-Path $env:LOCALAPPDATA "dotnet-sdk-10\dotnet.exe"
    if (Test-Path -LiteralPath $local) { return $local }
    throw "No .NET SDK found."
}

$dotnet = Resolve-Dotnet
Write-Output "Publishing MCP to $PublishDir"
& $dotnet publish $Project -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $PublishDir "ComputerUse.Mcp.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing $exe" }

Write-Output "Running desktop acceptance against $exe"
& $dotnet run --project $Harness -c $Configuration -- $exe
exit $LASTEXITCODE
