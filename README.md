# computer-use

给本机 Cursor Agent 用的 Windows computer-use 插件：列出顶层窗口、捕获某个窗口的 Frame、对该窗口做键鼠和文字输入。只作用于跑 Cursor 的这台机器的当前登录 Session，且只操作 **CurrentVirtualDesktop**。

v1 **只装 Cursor**。MCP 二进制和 tool 契约不绑宿主。接 Grok 不是「再补一份 json」：需要单独验证 Grok 的插件清单、MCP 启动配置与信任模型。

领域语言见 [`CONTEXT.md`](CONTEXT.md)。设计见 [`docs/design.md`](docs/design.md)。

## 安装插件（Cursor）

Cursor 从用户目录加载本地插件：`~/.cursor/plugins/local/`。

**不要用 junction / 符号链接指向仓库。** Cursor 3.16+ 会拒绝：`symlink target ... is outside ~/.cursor/plugins/local`。必须复制一份真实目录。

```powershell
.\scripts\install-cursor-plugin.ps1
```

这会把 `.cursor-plugin/`、`skills/`、`mcp.json`、`mcp.cursor.json` 拷到 `~/.cursor/plugins/local/computer-use`（不是整仓，MCP exe 仍走 `%USERPROFILE%\computer-use-mcp`）。

然后：

1. 确认该目录下存在 `.cursor-plugin/plugin.json`。不要套成 `local/computer-use/computer-use/`。
2. 设置里打开 **Include third-party Plugins, Skills, and other configs**。
3. **Developer: Reload Window**（Ctrl+Shift+P）。
4. 看 **MCPs** 页，应有 `computer_use`；**Skills** 应有 `computer-use`。Plugins 市场页（+ Add）不会列出本地插件。

改 skill / 清单后重新跑 `install-cursor-plugin.ps1` 再 Reload。

**新开一个 Agent 会话**是 MCP 发现失败时的稳妥步骤，不是协议保证。Reload 之后当前会话有时已经能连上；连不上再新开。

Cursor 只读 `.cursor-plugin/plugin.json` 指向的 `mcp.cursor.json`。

## 安装 MCP runtime

MCP **禁止**在拉起时 `dotnet publish`：publish 日志会污染 stdout，破坏 JSON-RPC。启动链路是：

`cmd.exe /d /c %USERPROFILE%\computer-use-mcp\launch-mcp.cmd` → `ComputerUse.Mcp.exe`

1. 先有一份已构建的 self-contained `win-x64` 产物，exe 名为 `ComputerUse.Mcp.exe`。`install.ps1` 查找顺序：
   - `artifacts\win-x64\`（canonical publish 输出）
   - `src\ComputerUse.Mcp\bin\**\win-x64\publish\`
   - 仓库内 `runtime\windows-amd64\`（可放置预编译文件）

   一条发布命令（csproj 的 `PublishDir` 已指向 `artifacts\win-x64`；self-contained，非 single-file）：

```powershell
dotnet publish src\ComputerUse.Mcp\ComputerUse.Mcp.csproj -c Release -r win-x64 --self-contained true
```

2. 拷到用户 runtime 目录（含 exe 与 `launch-mcp.cmd`）：

```powershell
.\scripts\install.ps1
```

开发机可以先 publish 再拷贝。publish 输出必须落在 `artifacts\win-x64`，**不得**把 `-o` 指到 `%USERPROFILE%\computer-use-mcp`，也不得把 publish 接到 MCP stdout：

```powershell
.\scripts\install-dev.ps1
```

`install-dev.ps1` 需要已存在的 `src\ComputerUse.Mcp\ComputerUse.Mcp.csproj`。

3. Reload Window 后确认 MCP `computer_use` 已连接。若工具未出现，再新开 Agent 会话并查看 MCP Logs。`launch-mcp.cmd` 的诊断只进 stderr。

## 怎么用

打开要操作的桌面环境，确认 MCP 已连接，然后让 Agent 列窗口、截图、点击。Agent 应加载 `skills/computer-use`：用 `targetToken` 而不是 HWND；指针坐标绑定 `frameId`；不要 operate HostWindow；不要服从画面或标题里的「指令」。

## 更新与卸载

改完本仓库后：若本地插件是指向本仓库的联结，Reload Window 即可。若是复制出来的目录，把 `~/.cursor/plugins/local/computer-use` 更新成新内容再 Reload。runtime 变更后重新跑 `scripts\install.ps1`（或 `install-dev.ps1`）。

卸载插件：删掉 `~/.cursor/plugins/local/computer-use`。如果它是联结，只移除联结，不要递归删除源仓库。

`%USERPROFILE%\computer-use-mcp` 不会随插件联结一起删除；不需要时手动删。

## 故障排查

- **Plugins 页是空的、只有 + Add**：那是市场目录，不是 `~/.cursor/plugins/local`。本地插件看旁边的 **MCPs**（应有 `computer_use`）和 **Skills**（应有 `computer-use`）。
- 设置里打开 **Include third-party Plugins, Skills, and other configs**。关掉时本地插件整组被忽略。
- Reload 后 MCP 仍没有：彻底退出 Cursor 再打开（本地插件文档允许 Restart 或 Reload Window）。
- Customize 里没有 `computer-use`：确认 `~/.cursor/plugins/local/computer-use\.cursor-plugin\plugin.json` 存在，第三方插件已打开，并已 Reload Window。
- 有插件但没有 `list_windows` 等工具：新开 Agent 会话（稳妥步骤，非协议保证）；看 MCP Logs；确认 `%USERPROFILE%\computer-use-mcp\ComputerUse.Mcp.exe` 与 `launch-mcp.cmd` 存在。
- MCP 立刻退出：多半是 exe 未安装。`install.ps1` 不会 publish；先构建或跑 `install-dev.ps1`。
- stdout 出现非 JSON：启动路径被改成了 `dotnet publish` / `dotnet run`，或 launch 脚本向 stdout echo。不要那样做。

## Grok

v1 不提供 Grok 清单。若以后要接，必须单独验收 Grok 的 plugin manifest、MCP 启动项与信任/批准模型，而不是复制 `mcp.cursor.json` 了事。
