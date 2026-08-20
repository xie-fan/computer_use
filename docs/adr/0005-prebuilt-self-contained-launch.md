# Prebuilt self-contained exe; launch never publishes

`dotnet publish` writes human-readable logs to stdout, which would corrupt MCP stdio JSON-RPC. The server is a prebuilt .NET 10 self-contained `win-x64` exe; `launch-mcp.cmd` only validates it, sets `COMPUTER_USE_HOST_PID` when possible, and execs it with cwd at the runtime directory. Diagnostics go to stderr.

## Considered Options

- `dotnet publish` (or `dotnet run`) as the MCP command
- Publishing directly into `%USERPROFILE%\computer-use-mcp` as the stdio working tree
