#Requires -Version 5.1
# Merge computer_use into Pi's mcp.json and copy the Skill into the Pi agent dir.
# Does not publish, copy ComputerUse.Mcp.exe, run `pi install`, or emit an Agent Plugins 1.0 package.
# Runtime stays in %USERPROFILE%\computer-use-mcp.

param(
    [string]$AgentDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$RuntimeExe = Join-Path $env:USERPROFILE "computer-use-mcp\ComputerUse.Mcp.exe"
$LaunchArg = "%USERPROFILE%\computer-use-mcp\launch-mcp.cmd"
$SkillSrc = Join-Path $RepoRoot "skills\computer-use"

if (-not $AgentDir) {
    if ($env:PI_CODING_AGENT_DIR -and $env:PI_CODING_AGENT_DIR.Trim()) {
        $AgentDir = $env:PI_CODING_AGENT_DIR.Trim()
    } else {
        $AgentDir = Join-Path $env:USERPROFILE ".pi\agent"
    }
}

function ConvertTo-JsonString {
    param([Parameter(Mandatory = $true)][string]$Value)
    $escaped = $Value.Replace("\", "\\").Replace('"', '\"').Replace("`r", "\r").Replace("`n", "\n").Replace("`t", "\t")
    return '"' + $escaped + '"'
}

function ConvertTo-CanonicalJson {
    param(
        $Value,
        [int]$Indent = 0
    )
    $pad = "  " * $Indent
    $padInner = "  " * ($Indent + 1)

    if ($null -eq $Value) {
        return "null"
    }
    if ($Value -is [bool]) {
        if ($Value) { return "true" } else { return "false" }
    }
    if ($Value -is [string]) {
        return (ConvertTo-JsonString -Value $Value)
    }
    if ($Value -is [byte] -or $Value -is [int16] -or $Value -is [uint16] -or
        $Value -is [int] -or $Value -is [uint32] -or $Value -is [int64] -or $Value -is [uint64]) {
        return [string]$Value
    }
    if ($Value -is [decimal] -or $Value -is [double] -or $Value -is [float] -or $Value -is [single]) {
        return [string]$Value
    }
    if ($Value -is [System.Collections.IDictionary]) {
        $keys = @($Value.Keys)
        if ($keys.Count -eq 0) {
            return "{}"
        }
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($key in $keys) {
            $keyJson = ConvertTo-JsonString -Value ([string]$key)
            $valJson = ConvertTo-CanonicalJson -Value $Value[$key] -Indent ($Indent + 1)
            [void]$parts.Add("$padInner$keyJson`: $valJson")
        }
        return "{`n" + ($parts -join ",`n") + "`n$pad}"
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $items = @($Value)
        if ($items.Count -eq 0) {
            return "[]"
        }
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($item in $items) {
            $valJson = ConvertTo-CanonicalJson -Value $item -Indent ($Indent + 1)
            [void]$parts.Add("$padInner$valJson")
        }
        return "[`n" + ($parts -join ",`n") + "`n$pad]"
    }
    return (ConvertTo-JsonString -Value ([string]$Value))
}

function New-StringObjectDictionary {
    return New-Object "System.Collections.Generic.Dictionary[string,object]"
}

function New-LaunchArgs {
    $argsList = New-Object System.Collections.ArrayList
    [void]$argsList.Add("/d")
    [void]$argsList.Add("/c")
    [void]$argsList.Add($LaunchArg)
    return $argsList
}

function Test-HasKey {
    param(
        [Parameter(Mandatory = $true)]$Map,
        [Parameter(Mandatory = $true)][string]$Key
    )
    if ($null -eq $Map) {
        return $false
    }
    if ($Map.GetType().GetMethod("ContainsKey")) {
        return [bool]$Map.ContainsKey($Key)
    }
    if ($Map -is [System.Collections.IDictionary]) {
        return [bool]$Map.Contains($Key)
    }
    return $false
}

function Merge-ComputerUseServer {
    param([Parameter(Mandatory = $true)]$Config)

    if (-not ($Config -is [System.Collections.IDictionary])) {
        throw "mcp.json root must be a JSON object."
    }

    if (-not (Test-HasKey -Map $Config -Key "mcpServers") -or $null -eq $Config["mcpServers"]) {
        $Config["mcpServers"] = New-StringObjectDictionary
    }

    $servers = $Config["mcpServers"]
    if (-not ($servers -is [System.Collections.IDictionary])) {
        throw "mcp.json mcpServers must be a JSON object (not an array). Refusing to overwrite the file."
    }

    $launchArgs = New-LaunchArgs
    if ((Test-HasKey -Map $servers -Key "computer_use") -and ($servers["computer_use"] -is [System.Collections.IDictionary])) {
        $entry = $servers["computer_use"]
        $entry["command"] = "cmd.exe"
        $entry["args"] = $launchArgs
    } else {
        $entry = New-StringObjectDictionary
        $entry["command"] = "cmd.exe"
        $entry["args"] = $launchArgs
        $servers["computer_use"] = $entry
    }
}

function Read-McpConfig {
    param([Parameter(Mandatory = $true)][string]$Path)

    Add-Type -AssemblyName System.Web.Extensions
    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $serializer.MaxJsonLength = [int]::MaxValue
    $serializer.RecursionLimit = 100

    if (-not (Test-Path -LiteralPath $Path)) {
        return (New-StringObjectDictionary)
    }

    $raw = [System.IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return (New-StringObjectDictionary)
    }

    try {
        $parsed = $serializer.DeserializeObject($raw)
    } catch {
        throw "Failed to parse mcp.json as JSON (comments/trailing commas are not rewritten): $Path`n$($_.Exception.Message)"
    }

    if ($null -eq $parsed) {
        return (New-StringObjectDictionary)
    }
    if (-not ($parsed -is [System.Collections.IDictionary])) {
        throw "mcp.json root must be a JSON object: $Path"
    }
    return $parsed
}

function Write-McpConfig {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Config
    )

    $json = ConvertTo-CanonicalJson -Value $Config
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, $utf8)
}

function Get-PiMcpAdapterHint {
    param([Parameter(Mandatory = $true)][string]$Dir)

    $pkg = Join-Path $Dir "npm\node_modules\pi-mcp-adapter\package.json"
    if (Test-Path -LiteralPath $pkg) {
        try {
            $meta = Get-Content -LiteralPath $pkg -Raw | ConvertFrom-Json
            return "found npm:pi-mcp-adapter@$($meta.version)"
        } catch {
            return "found pi-mcp-adapter package files"
        }
    }

    $extDir = Join-Path $Dir "extensions"
    if (Test-Path -LiteralPath $extDir) {
        $hit = @(Get-ChildItem -LiteralPath $extDir -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match "mcp-adapter|pi-mcp-adapter" })
        if ($hit.Count -gt 0) {
            return "found adapter-like files in extensions: $($hit.Name -join ', ')"
        }
    }

    $settingsPath = Join-Path $Dir "settings.json"
    if (Test-Path -LiteralPath $settingsPath) {
        $text = Get-Content -LiteralPath $settingsPath -Raw
        if ($text -match "pi-mcp-adapter") {
            return "pi-mcp-adapter is listed in settings.json, but package files were not found under npm\node_modules"
        }
    }

    return $null
}

if (-not (Test-Path -LiteralPath $SkillSrc)) {
    throw "Skill source not found: $SkillSrc"
}

New-Item -ItemType Directory -Path $AgentDir -Force | Out-Null
$McpJsonPath = Join-Path $AgentDir "mcp.json"
$config = Read-McpConfig -Path $McpJsonPath
Merge-ComputerUseServer -Config $config
Write-McpConfig -Path $McpJsonPath -Config $config

$SkillsRoot = Join-Path $AgentDir "skills"
$SkillDest = Join-Path $SkillsRoot "computer-use"
New-Item -ItemType Directory -Path $SkillsRoot -Force | Out-Null
if (Test-Path -LiteralPath $SkillDest) {
    $item = Get-Item -LiteralPath $SkillDest -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        cmd.exe /c rmdir "$SkillDest"
        if (Test-Path -LiteralPath $SkillDest) {
            throw "Failed to remove junction: $SkillDest"
        }
    } else {
        Remove-Item -LiteralPath $SkillDest -Recurse -Force
    }
}
Copy-Item -LiteralPath $SkillSrc -Destination $SkillDest -Recurse -Force

Write-Output "Merged mcpServers.computer_use into $McpJsonPath (other servers and top-level fields kept)."
Write-Output "Skill copied to $SkillDest (sibling skills were not removed)."
Write-Output "MCP command matches Cursor/Grok: cmd.exe /d /c $LaunchArg"
Write-Output "Runtime is still %USERPROFILE%\computer-use-mcp; this script does not publish or copy the exe."
Write-Output "Restart pi, then run /mcp and look for computer_use. Pi has no built-in MCP; this repo does not vendor pi-mcp-adapter."

if (-not (Test-Path -LiteralPath $RuntimeExe)) {
    Write-Warning "ComputerUse.Mcp.exe not found at $RuntimeExe. Run .\scripts\install.ps1 (or install-dev.ps1) first."
}

$adapterHint = Get-PiMcpAdapterHint -Dir $AgentDir
if ($adapterHint) {
    Write-Output "Adapter: $adapterHint"
} else {
    Write-Warning "pi-mcp-adapter was not found under $AgentDir\npm or extensions. Install it yourself (this script will not):"
    Write-Output "  pi install npm:pi-mcp-adapter"
}
