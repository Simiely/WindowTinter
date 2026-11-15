# AGENTS.md · 项目规则

> 给 AI / 未来的你：只记代码里看不出的关键信息。详细问题记录见 [DEV.md](DEV.md)（WinForms 时代资料）。

## 技术栈

- **当前应用：WPF / .NET 6.0**（`WindowTinter.Wpf/WindowTinter.Wpf.csproj`，`net6.0-windows`）。根目录的 `WindowTinter.csproj`、`MainForm*.cs` 是 WinForms 历史源码，仅归档，**不要构建**。
- 目标框架必须匹配用户运行时——用户机器只有 .NET 6，SDK 版本可更高。
- 黑底板渲染：`UpdateLayeredWindow`（纯黑位图 × alpha，DWM 硬件合成）——不用 Magnifier API。
- 窗口操作：Win32 P/Invoke（SetWindowLong / SetLayeredWindowAttributes / SetWinEventHook / EnumWindows）。
- 托盘：`Hardcodet.NotifyIcon.Wpf`（深色自绘菜单）。
- 发布：单文件、框架依赖（需要 .NET 6 Desktop Runtime）。`.github/workflows/release.yml` 在推送 `v*` tag 时自动校验版本 / CHANGELOG、0 警告构建并创建/更新 Release；手动验证用 `workflow_dispatch`（版本 + `dry_run=true`）。

## 关键坑

1. **跨进程 `SetWindowPos(SWP_FRAMECHANGED)` 会阻塞 UI 线程**（底层同步 SendMessage）——用 `InvalidateRect` 异步替代
2. **WinEvent 回调死循环**：改窗口样式触发 EVENT_OBJECT_LOCATIONCHANGE → 回调再改 → 回环。守卫：Refresh 不变不触发 + OnUpdate 内 alpha 不变不调 SetTargetAlpha
3. **BeginInvoke 闭包变量过期**：执行时加 `IsWindow + GetForegroundWindow` 守卫检查
4. **hwnd 回收复用风险**：操作前必须 `IsWindow(hwnd)`（两次 Win32 调用之间有 TOCTOU 窗口）
5. **WPF `Closing` 没有 `CloseReason`**：取消关闭后必须 `Hide()` + `ShowInTaskbar=false`；真实退出用 `ReallyQuit` 标记，二者不能混
6. **托盘常驻程序**：设置变更即时 `Save()`（不能依赖"退出时保存"）；启动时做状态修复（杀进程残留的 WS_EX_LAYERED 用 RedrawWindow 清）
7. **便携工具配置与 exe 同目录**（`Path.GetDirectoryName(Environment.ProcessPath)`）——`%AppData%` 会造成"换目录配置丢失"
8. **静默启动不 `Show()` 主窗**（`/startup`）：托盘必须在 `MainWindow` 构造函数里创建，不能放 `Loaded`（`Loaded` 只有窗口显示才触发）
9. **窗口从未显示过时的退出**：走 `MainWindow.RequestQuit()`，不要依赖 `Close()` 的 `Closing` 链路，否则可能残留进程
10. **持久化格式变更必须加迁移代码**（如 .exe 后缀剥离），否则存量用户全挂
11. **Dispose 顺序**：依赖方先停用再释放共享资源（托盘先隐藏/释放，再释放图标）
12. **窗口拾取**：用 `EnumWindows` 按 Z 序遍历 + PtInRect，不用 ShowWindow 显隐（DWM 过渡动画闪烁）
13. **跨完整性级别静默失败**：目标提权而本程序未提权时设透明度无反应——主动探测 TokenElevation 并提示用户
14. **第二实例**：命名 Mutex 冲突时，手动启动通过命名事件 `WindowTinter.Wpf.ShowWindow` 唤起已有主窗；带 `/startup` 的重复启动静默退出
15. **版本号禁止硬编码**：唯一来源是 `WindowTinter.Wpf.csproj` 的 `Version`；窗口标题、标题栏、关于页、托盘提示一律从 `Assembly.GetName().Version` 读取

## 约定

- 前台蒙版 + 后台透明（WS_EX_LAYERED alpha）策略；KeepTransparency 模式统一走 SetLayeredWindowAttributes
- 全屏/单窗口透明度开关：关闭全局时把当前值写入每个目标避免跳变
- **Release zip 只含**：`WindowTinter.exe` + `WindowTinter.pdb` + `使用说明.txt`；**不要**包含 `WindowTinter.settings.json`、staging 目录、日志（单文件发布，不再有 dll / deps.json）
- CHANGELOG 只写真实提交里的改动，不写计划；性能/耗时数字必须注明测试条件与样本量

## 常用命令

- 构建（必须在 WPF 目录）：
  `cd WindowTinter.Wpf; dotnet build -c Release`
- 单文件发布：
  `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ..\publish\WindowTinter-vX.Y.Z-win-x64`
- 发布流程：推 `v*` tag 自动触发 [.github/workflows/release.yml](.github/workflows/release.yml)；手动验证用 `gh workflow run release.yml -f version=X.Y.Z -f dry_run=true`；人工兜底步骤见 `docs/BUILD.md` §5–§7
- 版本号：只改 `WindowTinter.Wpf.csproj` 的 `Version` / `AssemblyVersion` / `FileVersion`；同时必须在 `CHANGELOG.md` 增加 `## vX.Y.Z` 段落，否则 CI 拒绝发布
