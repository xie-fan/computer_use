@echo off
rem MCP JSON-RPC is stdout. Never echo or log there.
cd /d "%~dp0" || (
    >&2 echo computer-use: failed to set cwd to the runtime directory.
    exit /b 1
)

set "EXE=%~dp0ComputerUse.Mcp.exe"
if not exist "%EXE%" (
    >&2 echo computer-use: ComputerUse.Mcp.exe not found in "%~dp0"
    >&2 echo computer-use: copy a published win-x64 build with scripts\install.ps1 or scripts\install-dev.ps1.
    exit /b 1
)

rem When Cursor starts cmd.exe /d /c this script, cmd's parent is the host.
rem PowerShell's parent is this cmd; cmd's parent is Cursor.
if not defined COMPUTER_USE_HOST_PID (
    for /f "usebackq delims=" %%P in (`powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; $ErrorActionPreference='SilentlyContinue'; $self = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $PID); $cmd = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $self.ParentProcessId); if ($cmd -and $cmd.ParentProcessId) { Write-Output $cmd.ParentProcessId }" 2^>nul`) do set "COMPUTER_USE_HOST_PID=%%P"
)

"%EXE%"
exit /b %ERRORLEVEL%
