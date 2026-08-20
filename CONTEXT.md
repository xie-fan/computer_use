# Computer Use

本机 Windows computer-use 的领域语言：当前登录 Session、CurrentVirtualDesktop 上的顶层 Window，以及对单个 Window 的 Capture 与 Action。

## Language

### Identity

**Window**:
顶层窗口，处于可见或最小化；不含隐藏窗、托盘、工具提示、子控件。
_Avoid_: 应用, 标签页, 窗体, 桌面

**TargetToken**:
插件签发的不透明身份，绑定一次观测到的 Window。HWND 只是 token 内的易失字段，不是身份。
_Avoid_: WindowHandle, 窗口 ID, HWND

**HostWindow**:
驱动本 MCP 的 Agent 宿主进程树里的窗口（由 `COMPUTER_USE_HOST_PID` 与进程树识别，不按 exe 名单独禁窗）。
_Avoid_: IDE 窗口

### Capture

**Frame**:
一次 Capture 的不可变快照，含画面、几何、DPI、后端和时间。
_Avoid_: 截图（口语可说截图，契约里用 Frame）

**FrameId**:
该 Frame 的不透明标识。指针坐标只相对签发它的那张返回图，且 operate 必须引用它。
_Avoid_: screenshotId

**Capture**:
针对单个 Window 的位图，不是桌面合成图。
_Avoid_: 截屏

**Monitor**:
一块物理屏幕。单次 list 里的 index 只在该次响应有效。
_Avoid_: 屏幕, 桌面, 显示器

### Desktop and session

**VirtualDesktop**:
Win+Tab 工作区。
_Avoid_: 桌面, Desktop

**CurrentVirtualDesktop**:
用户此刻正在看的 VirtualDesktop。v1 只在这里截图和输入。

**Session**:
当前 Windows 登录会话。前台窗口、指针、键盘、剪贴板、CurrentVirtualDesktop 都是 Session 全局状态。
_Avoid_: 桌面

### Operation

**Action**:
一次 `operate_window` 里对同一 TargetToken 的有序步骤。
_Avoid_: 命令

**Text**:
向已激活 Window 注入的 Unicode 字符串（UTF-16 code unit），不经过 IME。
_Avoid_: 打字, 输入法

**Paste**:
经系统剪贴板把字符串粘贴进已激活 Window。恢复剪贴板以 sequence number 为准，不能保证无损保存所有格式。
_Avoid_: 复制

**Coordinator**:
进程内串行化 Session 副作用的唯一入口。
_Avoid_: 锁, 队列

### Control memory (v2)

**AppKey**:
控件库的应用归档键：规范化进程镜像路径 + 窗口 className。
_Avoid_: 应用名, 标题, exe 短名

**Screen**:
同一 Window 内一种稳定视觉布局（一页 UI）。按钮只挂在某一个 Screen 下。
_Avoid_: 页面, 场景（口语可说页面，契约里用 Screen）

**ScreenId**:
MCP 签发的不透明界面身份。认屏靠指纹像素，不靠标签字符串。
_Avoid_: 界面名, screenName（那是 ScreenKey）

**ScreenKey**:
模型给的人类可读标签，只用于展示与 skill 对照。
_Avoid_: 当身份使用

**Control**:
挂在某个 Screen 下的可点击视觉块（模板图 + 归一化框）。
_Avoid_: 按钮坐标, 控件句柄

**ControlId**:
MCP 签发的不透明控件身份。
_Avoid_: 开始按钮, AutomationId（UIA 是后路，不是第一期主键）

**Observe**:
对本 Window 做 Capture、认屏并列出该屏 Control；默认不把 Frame 像素送给模型。
_Avoid_: 无图截图, 静默截屏
