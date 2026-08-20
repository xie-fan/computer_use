#Requires -Version 5.1
# Copy the Grok plugin surface into %USERPROFILE%\.grok\plugins\computer-use.
# Real directory only (same caution as the Cursor installer for reparse points).
# Does not publish or copy ComputerUse.Mcp.exe; runtime stays in %USERPROFILE%\computer-use-mcp.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Dest = Join-Path $env:USERPROFILE ".grok\plugins\computer-use"
$PluginsRoot = Split-Path -Parent $Dest
$GrokHost = Join-Path $RepoRoot "hosts\grok"
$SkillsSrc = Join-Path $RepoRoot "skills"

if (-not (Test-Path -LiteralPath $GrokHost)) {
    throw "Grok plugin surface not found: $GrokHost"
}

New-Item -ItemType Directory -Path $PluginsRoot -Force | Out-Null

if (Test-Path -LiteralPath $Dest) {
    $item = Get-Item -LiteralPath $Dest -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        cmd.exe /c rmdir "$Dest"
        if (Test-Path -LiteralPath $Dest) {
            throw "Failed to remove junction: $Dest"
        }
    } else {
        Remove-Item -LiteralPath $Dest -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $Dest -Force | Out-Null

Get-ChildItem -LiteralPath $GrokHost -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $Dest $_.Name) -Recurse -Force
}

if (-not (Test-Path -LiteralPath $SkillsSrc)) {
    throw "Skill source not found: $SkillsSrc"
}
Copy-Item -LiteralPath $SkillsSrc -Destination (Join-Path $Dest "skills") -Recurse -Force

Write-Output "Grok plugin copied to $Dest (real directory, not a junction)."
Write-Output "Next: grok plugin validate `"$Dest`""
Write-Output "MCP is enabled only after the plugin is trusted. Either:"
Write-Output "  grok plugin install `"$Dest`" --trust"
Write-Output "or trust it in the TUI (/plugins or /mcps). Then look for computer_use."
Write-Output "Do not also run grok mcp add for the same server (that would duplicate it)."
Write-Output "Runtime is still %USERPROFILE%\computer-use-mcp; this script does not publish or copy the exe."
