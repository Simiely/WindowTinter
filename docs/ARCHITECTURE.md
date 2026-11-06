# 架构与主线

> 对应版本 7.2.0。所有结论来自 `WindowTinter.Wpf/` 现有代码；本机未运行验证的部分会单独标注「未验证」。

## 1. 项目边界

| 路径 | 说明 |
|---|---|
| `WindowTinter.Wpf/WindowTinter.Wpf.csproj` | **当前应用**。`net6.0-windows`、`WinExe`、`AssemblyName=WindowTinter`、`x64`、`Version=7.2.0`、`PublishSingleFile=true` |
| `WindowTinter.Wpf/Services/*` | Win32 封装与领域服务：`Native`、`BlackPlate`、`TargetTracker`、`SnapshotService`、`Settings`、`Elevation`、`TrayService`、`WindowTheme` |
| `WindowTinter.Wpf/ViewModels/*` | `MainViewModel`（领域逻辑主体）、`TargetViewModel`（单卡 UI 状态）、`ObservableObject` |
| `WindowTinter.Wpf/Views/*` | `MainWindow`、`WindowPickerWindow`、`SettingsDialog`、`RenameDialog`、`AboutDialog`、`CardsPanel` |
| 根目录 `WindowTinter.csproj` + `MainForm*.cs` 等 | WinForms 历史源码，仅归档，**不要构建**（会产出另一个同名程序） |

依赖：`Hardcodet.NotifyIcon.Wpf 2.0.1`（托盘）、`System.Drawing.Common 6.0.0`（快照位图）。无 DI 容器、无 MVVM 框架、无日志框架。

## 2. 启动与生命周期

- `App.OnStartup`：先 `Native.EnableDarkMenus()`（决定 Win32 原生菜单是否跟随系统深色），再创建命名 Mutex `WindowTinter.Wpf` 做单实例；第二个实例直接 `Shutdown()`（不会激活已有窗口）。
- `MainViewModel` 构造：加载配置 → `ApplyStartWithWindows()` → 先建卡片 VM（占位）→ 按配置批量 `BindTarget` → 启动 3 秒自动重绑定时器 → 启动 200ms 保存防抖 → 安装 WinEvent 钩子 → `SyncUI()`。
- `WindowTinter` 的关闭语义：`Closing` 事件里若「最小化到托盘」开启则取消关闭并隐藏（效果继续）；真实退出（托盘退出 / 关闭该选项）才释放所有目标并保存窗口状态。
- 窗口状态（位置/尺寸/最大化）在 `SourceInitialized` 恢复，并在真实退出时保存；空列表自动收缩到 560 高，且收缩后的高度不写入配置。

## 3. 效果应用主线

```
TargetTracker（WinEvent + 500ms 轮询）
        │  (rect, visible)
        ▼
MainViewModel.ApplyEntryEffect(entry, visible)   ← 唯一入口，异常只写 Debug，不冒到 UI 线程
        ▼
MainViewModel.ApplyEntryEffectCore
   ├─ 不可见 / 全局关 / 单卡停用 / 最小化抑制 → HidePlate + 还原 alpha/topmost
   └─ 可见 → 差异计算 alpha（只有变化才 SetTargetAlpha）
             ├─ 底板：BackdropBlackPlate 且非 cloak → Plate.AlignBehind(h, visibleRect)
             └─ 否则 HidePlate
```

关键设计：

- **唯一入口**：所有蒙版显隐都走 `ApplyEntryEffect`；`ApplyEntryEffect` 外层 `try/catch`，任何 Win32 失败只记录不抛。
- **差异计算**：`TargetEntry.LastAlpha` 记录上次写入的 alpha，避免拖动滑块时高频调用系统 API。
- **状态捕获**（`BindTarget` 时一次性）：`OriginalExStyle`、`OriginalLayered`（目标是否本来就是 `WS_EX_LAYERED`）、`OriginalAlpha`、`OriginalTopmost`；`RestoreTargetState` 只回滚本程序改动过的部分——原本分层的窗口还原其原始 alpha（例如 200），不粗暴写 255，也不强删 `WS_EX_LAYERED`。
- **置顶**：绑定时若目标原本不是置顶，会清掉历史置顶残留（`SetWindowPos` 非 topmost）；原本置顶则保留。
- **最小化**：`MINIMIZESTART` 事件立即隐藏底板并置 `MinimizeSuppressed`；恢复需「可见 + 非图标化 + 距抑制 ≥250ms」，另加 500ms 轮询兜底（防 DWM 动画期间漏事件）。
- **虚拟桌面 / UWP 挂起**：`DwmGetWindowAttribute(DWMWA_CLOAKED)` 查询，cloak 时隐藏底板（cloak 不产生任何 WinEvent）。

## 4. 黑底板（BlackPlate）

- 纯 Win32 分层窗口：`CreateWindowExW(WS_POPUP, WS_EX_LAYERED | WS_EX_TOOLWINDOW)`，不置顶、不穿透。
- 内容为纯黑位图 × alpha，`UpdateLayeredWindow` 提交，DWM 硬件合成；不参与 WPF 布局。
- 圆角：`CreateRoundRectRgn` + `SetWindowRgn` 裁剪窗口形状，半径按目标窗口的 DIP 值换算。
- 位置：`AlignBehind(h, visibleRect)` 对齐目标可见区域；目标最小化 / cloak / 不可见时 `HidePlate()`。
- 吞掉 `WM_DPICHANGED`：底板尺寸全部用物理像素直控，窗口跨屏切换时不错位。

## 5. 主窗口圆角

四参数（正式字段，默认 `23 / 14 / 13 / 0`）：

| 参数 | 作用 | 落点 |
|---|---|---|
| `CornerOuter = 23` | 窗口外轮廓 | `WindowChrome.CornerRadius` + 直接 `SetWindowRgn`（HRGN） |
| `CornerFrame = 14` | 根 Border 的描边弧 | `RootFrame.CornerRadius` |
| `CornerClip = 13` | 内容裁切 | `RootContent.Clip = RectangleGeometry(...)` |
| `CornerInset = 0` | 裁切矩形相对窗口边缘内缩 | 同上 |

- `WindowChrome` 参数：`CaptionHeight=48`、`ResizeBorderThickness=6`、`GlassFrameThickness=0`、`UseAeroCaptionButtons=False`、`CornerRadius` 由 `ApplyCornerDebug()` 在启动时写入。
- **为什么自己调 `SetWindowRgn`**：WPF 的 `WindowChromeWorker._SetRoundingRegion` 在窗口状态未变时直接 return，运行时改 `CornerRadius` 只触发重绘、不重建 HRGN。`ApplyOuterRegion()` 用 `CreateRoundRectRgn(0, 0, ceil(W*scale)+1, ceil(H*scale)+1, ceil(R*scale), ceil(R*scale))` 后 `SetWindowRgn`，成功后 HRGN 归系统所有（失败才 `DeleteObject`）。
- **最大化**：`RootFrame.CornerRadius = 0`、`RootContent.Clip = null`、跳过圆角区域（交给 WindowChrome 的矩形区域）；`WM_GETMINMAXINFO` 钩子把最大化矩形钳制到显示器工作区，Win10 下不压任务栏。
- **DPI**：半径以设备像素 `ceil(R * DpiScaleX)` 下发；`DpiChanged` 与 `SizeChanged` 都重算裁切。125%/150% 下的实际观感未实测（见 `LIMITATIONS.md`）。

## 6. 纯函数 / 副作用边界

**无 Win32 副作用（可在任意线程/单测中调用）**：

- `TargetInfo.Equals / GetHashCode / ToString`：身份判定（进程 + 标题，类名仅双方非空时参与）。
- `Settings.Load` 中的格式迁移：0~255 alpha → 0~100、单窗口字段 → 列表、`GlobalTransparency` / `GlobalCornerRadius` 开关语义、`.exe` 后缀剥离、`DebugCorner` → `Corner*` 升级、越界 clamp。`LegacyUnifiedFlag` 是纯 JSON 读取。
- 效果差异计算：`EffectiveAlpha` / `EffectiveCorner`（读配置算值）、`targetAlpha = (100 - bgPct) * 255 / 100`。
- 圆角几何：DIP → 设备像素的 `Math.Ceiling` 换算、裁切矩形计算。

**有系统副作用（只在 UI 线程或指定线程调用）**：

- `Native` 的全部 P/Invoke；`BlackPlate` 的窗口创建/定位/显隐；`Settings.Save`（临时文件 + `File.Move` 原子替换）；注册表开机自启；托盘；快照 `PrintWindow`。
- WinEvent 回调线程只做句柄过滤（`_boundHandles` 快照，`volatile` 数组），命中后才 `Dispatcher` 投递到 UI 线程；快照在后台线程完成后回投 UI。

## 7. 拾取器

- 全屏覆盖层：四块 scrim 拼出「挖空」目标区域 + 蓝色描边四角标 + 左侧 HUD（与目标重叠时自动换到右侧）。
- 目标判定：`WindowFromPoint` 命中后做合法性校验；命中自身/桌面/任务栏等不可交互窗口时退回 `EnumWindows` Z 序枚举。
- 排除：`Progman`、`WorkerW`、`Shell_TrayWnd`、`Shell_SecondaryTrayWnd`、不可见、最小化、透明覆盖层、自身。
- 取消：`Esc`（隧道阶段 `e.Handled=true`，不泄漏给后台窗口）、鼠标右键、HUD 取消按钮。取消路径只关闭对话框（`DialogResult=false`），不写配置、不绑定、不改任何目标状态。

## 8. 配置存储

- 文件：`Path.GetDirectoryName(Environment.ProcessPath)` + `WindowTinter.settings.json`（exe 同目录）。
- 写入：序列化到 `.tmp` 再 `File.Move(overwrite)`，避免半截文件；失败静默（调试输出）。
- 时机：设置面板/卡片参数改动即时或 200ms 防抖保存；窗口状态在真实退出保存；圆角快捷键按下即保存。
- 字段：`Targets`、`BackgroundAlpha`、`CornerRadius`（**全局默认圆角**，与窗口 UI 圆角无关）、`Enabled`、`StartWithWindows`、`MinimizeToTray`、`BackdropBlackPlate`、`WindowLeft/Top/Width/Height`、`WindowMaximized`、`CardsPerRow`、`GlobalPanelExpanded`、`CornerOuter/Frame/Clip/Inset`、`CornerTuned`；迁移专用的 `DebugCorner`、`TargetProcessName/Title` 默认不写出。
