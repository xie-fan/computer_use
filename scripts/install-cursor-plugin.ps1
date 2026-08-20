#Requires -Version 5.1
# Copy the Cursor plugin surface into %USERPROFILE%\.cursor\plugins\local\computer-use.
# Cursor 3.16+ rejects junctions/symlinks whose target is outside that folder.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Dest = Join-Path $env:USERPROFILE ".cursor\plugins\local\computer-use"
$LocalRoot = Split-Path -Parent $Dest

New-Item -ItemType Directory -Path $LocalRoot -Force | Out-Null

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

$files = @(
    "mcp.json",
    "mcp.cursor.json"
)
foreach ($file in $files) {
    Copy-Item -LiteralPath (Join-Path $RepoRoot $file) -Destination (Join-Path $Dest $file) -Force
}

Copy-Item -LiteralPath (Join-Path $RepoRoot ".cursor-plugin") -Destination (Join-Path $Dest ".cursor-plugin") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $RepoRoot "skills") -Destination (Join-Path $Dest "skills") -Recurse -Force

Write-Output "Cursor plugin copied to $Dest (real directory, not a junction)."
Write-Output "Reload Window or restart Cursor, then check MCPs for computer_use."
