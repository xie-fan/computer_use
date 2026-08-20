#Requires -Version 5.1
param(
    [string]$Configuration = "Release",
    [string]$Project,
    [string]$RuntimeHome = $(Join-Path $env:USERPROFILE "computer-use-mcp")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Publish to artifacts\win-x64 (canonical, also set as PublishDir in the csproj),
# then copy. Never pass -o to %USERPROFILE%\computer-use-mcp: that directory is
# the MCP stdio launch home. Never run this script as the MCP command.

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Project) {
    $Project = Join-Path $RepoRoot "src\ComputerUse.Mcp\ComputerUse.Mcp.csproj"
}

if (-not (Test-Path -LiteralPath $Project)) {
    throw "MCP project not found: $Project. Do not publish into $RuntimeHome. Create the csproj first, or copy an already-built tree with scripts\install.ps1."
}

$canonicalPublish = Join-Path $RepoRoot "artifacts\win-x64"
$runtimeFull = [IO.Path]::GetFullPath($RuntimeHome)
$canonicalFull = [IO.Path]::GetFullPath($canonicalPublish)

if ($canonicalFull.TrimEnd('\') -eq $runtimeFull.TrimEnd('\')) {
    throw "Canonical publish dir resolved to the MCP runtime home ($runtimeFull). Publish must not target the MCP runtime directory."
}

function Test-DotnetHasSdk {
    param([Parameter(Mandatory = $true)][string]$DotnetPath)
    if (-not (Test-Path -LiteralPath $DotnetPath)) {
        return $false
    }
    $sdks = & $DotnetPath --list-sdks 2>$null
    return $LASTEXITCODE -eq 0 -and $sdks
}

function Resolve-Dotnet {
    $candidates = @()
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) {
        $candidates += $cmd.Source
    }
    $candidates += (Join-Path $env:LOCALAPPDATA "dotnet-sdk-10\dotnet.exe")

    foreach ($candidate in $candidates) {
        if (Test-DotnetHasSdk -DotnetPath $candidate) {
            return $candidate
        }
    }

    throw "No .NET SDK found. Checked PATH and $(Join-Path $env:LOCALAPPDATA 'dotnet-sdk-10\dotnet.exe')."
}

$dotnet = Resolve-Dotnet

Write-Output "Publishing $Project (self-contained win-x64) to $canonicalPublish, not $RuntimeHome"

& $dotnet publish $Project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o $canonicalPublish
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $canonicalPublish "ComputerUse.Mcp.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "dotnet publish succeeded but ComputerUse.Mcp.exe was not under $canonicalPublish. Refusing to use $RuntimeHome as publish output."
}

Write-Output "Publish output: $canonicalPublish"

$install = Join-Path $PSScriptRoot "install.ps1"
& $install -PublishDir $canonicalPublish -RuntimeHome $RuntimeHome
