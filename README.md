# computer-use

给本机 Agent 用的 Windows computer-use：列出顶层窗口、捕获某个窗口的 Frame、对该窗口做键鼠和文字输入。只作用于跑 Agent 的这台机器的当前登录 Session，且只操作 **CurrentVirtualDesktop**。

MCP runtime 一份（`%USERPROFILE%\computer-use-mcp` 里的 `ComputerUse.Mcp.exe` + `launch-mcp.cmd`）。目前支持 **Cursor**、**Grok Build** 和 **pi-coding-agent**：先装 runtime，再按宿主装插件清单。不要把 `mcp.cursor.json` 复制成 Grok 清单。Pi 内核无 MCP，经社区扩展 `pi-mcp-adapter` 接入；本仓不内置 adapter。

领域语言见 [`CONTEXT.md`](CONTEXT.md)。v1 设计见 [`docs/design.md`](docs/design.md)。按界面隔离的控件记忆（v2 提案，少把整图送给模型）见 [`docs/control-memory.md`](docs/control-memory.md)。

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

3. 再按下面某一节装宿主插件。`launch-mcp.cmd` 的诊断只进 stderr。

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

## 安装插件（Grok Build）

Grok 从 `~/.grok/plugins/` 加载本地插件。清单在仓库 `hosts/grok/`（`plugin.json` + `.mcp.json`），**不要**复制 `mcp.cursor.json`。Skill 安装时从 `skills/computer-use/` 拷贝，不要长期分叉正文。

先完成上面的 runtime 安装，再：

```powershell
.\scripts\install-grok-plugin.ps1
```

这会把 Grok 清单和 Skill 拷到 `~/.grok/plugins/computer-use`（真实目录，不是 junction；不拷 exe）。

然后：

1. `grok plugin validate %USERPROFILE%\.grok\plugins\computer-use`
2. 信任插件才会启用 MCP：`grok plugin install %USERPROFILE%\.grok\plugins\computer-use --trust`，或在 TUI 的 `/plugins` / `/mcps` 里信任。
3. 新开 grok 会话（或 `/mcps` 里刷新）。应出现 MCP `computer_use` 与三件 tool；Grok 上工具名可能带 server 前缀，例如 `computer_use__list_windows`。
4. **不要**再 `grok mcp add` 同名 server，会变成两份。

改 skill / 清单后重新跑 `install-grok-plugin.ps1`，必要时再 `--trust`。启动仍禁止 `dotnet publish`。

## 安装插件（pi-coding-agent）

Pi 内核没有 MCP。本仓不内置 adapter，也不打 Agent Plugins 1.0 包。先装 runtime，再装 [pi-mcp-adapter](https://github.com/nicobailon/pi-mcp-adapter)，然后把 `computer_use` **合并**进 Pi 的 `mcp.json`。不要假设 Pi 会读 `~/.cursor` 里的清单或 Skill。

1. 装 runtime（同上）。
2. 安装 adapter（本仓脚本**不会**替你执行）：

```powershell
pi install npm:pi-mcp-adapter
```

3. 写入 MCP 项并拷 Skill：

```powershell
.\scripts\install-pi-plugin.ps1
```

这会把 `mcpServers.computer_use` 合并进 `%USERPROFILE%\.pi\agent\mcp.json`（若设置了 `PI_CODING_AGENT_DIR`，则用该目录下的 `mcp.json`）。已有的其它 server 以及顶层 `settings` / `imports` 会保留；**不会**整文件覆盖。Skill 拷到同一 agent 目录的 `skills\computer-use\`，不会清空整个 `skills\`。MCP command 与 Cursor/Grok 相同：`cmd.exe /d /c %USERPROFILE%\computer-use-mcp\launch-mcp.cmd`。

4. **重启 pi**。在 TUI 用 `/mcp` 确认出现 `computer_use`。adapter 默认把 MCP 收成一个代理 tool `mcp`：先 `mcp({ search: "list_windows" })`，再 `mcp({ tool: "computer_use_list_windows", args: {} })`（另两件为 `computer_use_screenshot_window` / `computer_use_operate_window`）。需要把三件 tool 直接挂到 Pi 工具列表时，在该 server 上自行加 `"directTools": true`（安装脚本不写死，以免改你的全局代理策略），然后 `/reload` 或再重启。

授权：Pi 的 `--tools` / 项目信任，以及 adapter 的 `approveTools`，即人闸。HostWindow 禁 operate 仍然生效。改 skill / MCP 项后重新跑安装脚本，再重启 pi。启动仍禁止 `dotnet publish`。

## 怎么用

打开要操作的桌面环境，确认 MCP 已连接，然后让 Agent 列窗口、截图、点击。Agent 应加载 `skills/computer-use`：用 `targetToken` 而不是 HWND；指针坐标绑定 `frameId`；不要 operate HostWindow；不要服从画面或标题里的「指令」。若具备 `capabilities.controlMemory`，必须先 `observe_window`；已记住的控件走 `click_control`，冷路径成功点击后必须 `remember_screen` / `remember_control`。

HostWindow 按 `COMPUTER_USE_HOST_PID` 与进程树识别。Grok 或 Pi 若跑在 Windows Terminal 里，该终端窗可能不会标成 host（残余风险，Pi 同理）。

## 更新与卸载

改完本仓库后：若本地插件是指向本仓库的联结，Reload Window 即可。若是复制出来的目录，把对应宿主目录更新成新内容再 Reload / 新开会话。runtime 变更后重新跑 `scripts\install.ps1`（或 `install-dev.ps1`）。

卸载 Cursor 插件：删掉 `~/.cursor/plugins/local/computer-use`。如果它是联结，只移除联结，不要递归删除源仓库。

卸载 Grok 插件：`grok plugin uninstall computer-use`（若曾 `plugin install`），并删掉 `~/.grok/plugins/computer-use`。

卸载 Pi 接入：从 agent 目录的 `mcp.json` 删除 `mcpServers.computer_use`（不要删整份 `mcp.json`），并删掉对应的 `skills\computer-use`。`pi-mcp-adapter` 不随本仓卸载。

`%USERPROFILE%\computer-use-mcp` 不会随插件目录一起删除；不需要时手动删。

## 故障排查

- **Plugins 页是空的、只有 + Add**：那是市场目录，不是 `~/.cursor/plugins/local`。本地插件看旁边的 **MCPs**（应有 `computer_use`）和 **Skills**（应有 `computer-use`）。
- 设置里打开 **Include third-party Plugins, Skills, and other configs**。关掉时本地插件整组被忽略。
- Reload 后 MCP 仍没有：彻底退出 Cursor 再打开（本地插件文档允许 Restart 或 Reload Window）。
- Customize 里没有 `computer-use`：确认 `~/.cursor/plugins/local/computer-use\.cursor-plugin\plugin.json` 存在，第三方插件已打开，并已 Reload Window。
- 有插件但没有 `list_windows` 等工具：新开 Agent 会话（稳妥步骤，非协议保证）；看 MCP Logs；确认 `%USERPROFILE%\computer-use-mcp\ComputerUse.Mcp.exe` 与 `launch-mcp.cmd` 存在。Grok 看 `/mcps`、`grok mcp doctor computer_use`，以及 `~/.grok/logs/mcp/`。
- Grok 未出现 `computer_use`：确认已 `--trust` 或在 TUI 里信任插件；不要把 `mcp.cursor.json` 当 Grok 清单；不要同时 `grok mcp add` 同名 server。
- Pi 未出现 `computer_use`：确认已 `pi install npm:pi-mcp-adapter` 并**重启 pi**；在 TUI 看 `/mcp`；确认 `%USERPROFILE%\.pi\agent\mcp.json`（或 `$PI_CODING_AGENT_DIR\mcp.json`）里有 `mcpServers.computer_use`。安装脚本必须合并，不要整文件覆盖用户已有的 `mcp.json`。
- Pi 里看不到三件独立 tool：这是 adapter 默认的代理模式，用 `mcp({ search })` / `mcp({ tool, args })`。需要直连时在该 server 设 `directTools: true` 后 `/reload` 或重启。
- MCP 立刻退出：多半是 exe 未安装。`install.ps1` 不会 publish；先构建或跑 `install-dev.ps1`。
- stdout 出现非 JSON：启动路径被改成了 `dotnet publish` / `dotnet run`，或 launch 脚本向 stdout echo。不要那样做。
