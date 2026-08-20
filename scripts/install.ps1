#Requires -Version 5.1
param(
    [string]$PublishDir,
    [string]$RuntimeHome = $(Join-Path $env:USERPROFILE "computer-use-mcp")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Copy an already-built win-x64 tree into the MCP runtime home.
# Never publish here: publish logs on stdout would break MCP JSON-RPC if this
# directory were used as the server command.

$RepoRoot = Split-Path -Parent $PSScriptRoot
$CanonicalPublish = Join-Path $RepoRoot "artifacts\win-x64"
$RepoRuntime = Join-Path $RepoRoot "runtime\windows-amd64"
$LaunchSrc = Join-Path $PSScriptRoot "launch-mcp.cmd"

function Test-McpExe {
    param([Parameter(Mandatory = $true)][string]$Directory)
    if (-not (Test-Path -LiteralPath $Directory)) {
        return $false
    }
    return Test-Path -LiteralPath (Join-Path $Directory "ComputerUse.Mcp.exe")
}

function Find-BinPublishDir {
    $binRoot = Join-Path $RepoRoot "src\ComputerUse.Mcp\bin"
    if (-not (Test-Path -LiteralPath $binRoot)) {
        return $null
    }

    $hit = Get-ChildItem -LiteralPath $binRoot -Recurse -Filter "ComputerUse.Mcp.exe" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '[\\/]win-x64[\\/]publish$' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($hit) {
        return $hit.DirectoryName
    }
    return $null
}

function Resolve-PublishDir {
    if ($PublishDir) {
        if (-not (Test-McpExe -Directory $PublishDir)) {
            throw "PublishDir has no ComputerUse.Mcp.exe: $PublishDir"
        }
        return (Resolve-Path -LiteralPath $PublishDir).Path
    }

    if (Test-McpExe -Directory $CanonicalPublish) {
        return (Resolve-Path -LiteralPath $CanonicalPublish).Path
    }

    $binPublish = Find-BinPublishDir
    if ($binPublish) {
        return (Resolve-Path -LiteralPath $binPublish).Path
    }

    if (Test-McpExe -Directory $RepoRuntime) {
        return (Resolve-Path -LiteralPath $RepoRuntime).Path
    }

    throw @"
No ComputerUse.Mcp.exe found.
Looked at:
  $CanonicalPublish
  src\ComputerUse.Mcp\bin\**\win-x64\publish\
  $RepoRuntime
Build the self-contained win-x64 output first:
  dotnet publish src\ComputerUse.Mcp\ComputerUse.Mcp.csproj -c Release -r win-x64 --self-contained true
or run scripts\install-dev.ps1 (publish then copy). Do not publish into $RuntimeHome.
"@
}

function Copy-Runtime {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$DestDir
    )

    $sourceFull = (Resolve-Path -LiteralPath $SourceDir).Path
    New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
    $destFull = (Resolve-Path -LiteralPath $DestDir).Path

    if ($sourceFull.TrimEnd('\') -eq $destFull.TrimEnd('\')) {
        throw "Refusing to install: source and runtime home are the same ($destFull). Publish must not target the MCP runtime directory."
    }

    Get-ChildItem -LiteralPath $sourceFull -Force | Where-Object {
        $_.Name -ne ".gitkeep" -and $_.Name -ne "launch-mcp.cmd"
    } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destFull $_.Name) -Recurse -Force
    }

    if (-not (Test-Path -LiteralPath $LaunchSrc)) {
        throw "Missing launcher template: $LaunchSrc"
    }
    Copy-Item -LiteralPath $LaunchSrc -Destination (Join-Path $destFull "launch-mcp.cmd") -Force

    $exe = Join-Path $destFull "ComputerUse.Mcp.exe"
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Copy finished but ComputerUse.Mcp.exe is missing at $exe"
    }
}

$source = Resolve-PublishDir
Write-Output "Source: $source"
Copy-Runtime -SourceDir $source -DestDir $RuntimeHome
Write-Output "Runtime ready at $RuntimeHome"
Write-Output "Launcher: $(Join-Path $RuntimeHome 'launch-mcp.cmd')"
Write-Output "MCP command stays cmd.exe /d /c %USERPROFILE%\computer-use-mcp\launch-mcp.cmd (stdout must stay JSON-RPC only)."
