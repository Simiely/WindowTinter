# AGENTS.md · 项目规则

> 给 AI / 未来的你：只记代码里看不出的关键信息。WinForms 时代的历史问题记录见 [DEV.md](DEV.md)。

## 技术栈（v6.0.1 · WPF 版）

- **UI**：C# WPF + .NET 6.0（`net6.0-windows`，`UseWPF=true`），深色主题 `Themes/Dark.xaml`（18 色 token + 圆角按钮/胶囊开关/暗色复选框/暗色滚动条/工具提示）
- **架构**：MVVM（手写 `ObservableObject` / `RelayCommand`，无第三方框架）；`ViewModels/MainViewModel.cs` 承载全部领域逻辑
- **黑底（BlackPlate）**：**纯 Win32 分层窗口**（`CreateWindowExW` + `UpdateLayeredWindow` + `SetWindowRgn`），物理像素直控、静态 WndProc 吞 `WM_DPICHANGED`——**不是 WPF Window**（WPF AllowsTransparency 窗口的 DIP 布局系统会覆盖物理定位，DPI 切换必错位）
- **DPI 感知**：`app.manifest` 声明 `PerMonitorV2`（缺了会被系统位图拉伸、切换缩放错位）
- **托盘**：Hardcodet.NotifyIcon.Wpf（图标/ToolTip）+ **Win32 `TrackPopupMenu` 原生菜单**（`CreatePopupMenu`/`AppendMenuW`/`TrackPopupMenu(TPM_RETURNCMD)`）；菜单 owner = **专用隐藏辅助窗口**（不能是主窗口句柄，否则右键托盘把主窗口带到前台）；`App.OnStartup` 最早调用 `SetPreferredAppMode(AllowDark)`（uxtheme #135）让原生菜单跟随系统深色
- **快照**：PrintWindow 抓图 + 后台线程（`Task.Run`）+ `Dispatcher.BeginInvoke` 回 UI；抓图移后台线程避免 UI 卡顿
- **配置**：System.Text.Json 存 exe 同目录 `WindowTinter.settings.json`（含目标列表、参数、窗口状态）
- **发布**：**单文件**（框架依赖）——`dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false`；app.ico 已嵌入程序集（`<Resource Include>` + pack URI 加载），无外部文件

## 关键坑（WPF 时代实测）

1. **WPF `Window.Left/Top/Width/Height` 是 DIP**：任何物理像素定位（GetCursorPos/SetWindowPos 等）必须与 Left/Top 分开用——需要物理直控就用 Win32 窗口（BlackPlate 就是这么做的），不要混用
2. **`PresentationSource.FromVisual()` 在窗口 `Show()` 之前返回 null**：DPI 换算拿不到 → 自绘 Popup 定位错乱；纯物理像素方案（GetCursorPos + SetWindowPos）最稳
3. **Hardcodet 系托盘菜单样式不可控**：H.NotifyIcon README 确认"托盘菜单被转换成 Win32 PopupMenu"——WPF ContextMenu 的 Foreground/Background 对系统菜单不生效；直接用 Win32 `TrackPopupMenu` 最可靠
4. **原生菜单深色**：系统深色模式下 TrackPopupMenu 仍可能浅色——必须在任何窗口创建前调用 `SetPreferredAppMode(AllowDark)` + `FlushMenuThemes()`（uxtheme.dll 序号 #135/#136）
5. **WPF 单文件发布**：必须 `-r win-x64` 指定 RID；publish 不清理旧文件（换产物前先清空输出目录）；DebugType=None 去 pdb
6. **快照时序**：绑定后抓图必须发生在卡片 VM 创建（`SyncUI`）之后，否则 `FindVm` 返回 null 快照被丢弃（添加窗口路径补了一次）
7. **窗口状态保存**：`SourceInitialized` 时恢复（DPI/屏幕信息就绪）；恢复前校验位置在可见屏幕内（防多屏拔掉后窗口飞出）；最大化时用 `RestoreBounds` 存还原尺寸
8. **WPF 按钮文字色**：自定义模板里 `ContentPresenter` 对 Foreground 继承不可靠——本项目的三个按钮模板直接改用 `TextBlock` 绑定 `Content`/`Foreground`
9. **进程/类名绑定**：目标窗口识别 = 进程 + 类名（`TargetInfo.WindowClass` 参与判等），匹配链：类名收窄 → 标题精确 → 标题包含 → 唯一窗口 → 面积最大
10. **托盘辅助窗口**：`CreateWindowExW("Static")` 不可见窗口作 TrackPopupMenu owner；`GWLP_USERDATA` 挂实例（GCHandle），Dispose 时 `DestroyWindow`

## 约定

- 领域逻辑（BindTarget/ReleaseTarget/ApplyEntryEffect 等）从 WinForms 版**原样搬移不改逻辑**，只做容器适配（Timer→DispatcherTimer、BeginInvoke→Dispatcher.BeginInvoke）
- 配置与 exe 同目录（`Path.GetDirectoryName(Environment.ProcessPath)`），不用 %AppData%
- 版本号：csproj `Version`（6.0.1）+ 主窗口标题 + AboutDialog + TrayService fallback 四处同步
- 发布目录：`dist/WindowTinter-v<版本>-framework-dependent/`（单文件 exe）；zip 同名打包
- 调试期不推送；发布轻量版进 dist；推送/Release 用 GitHub API（`git credential fill` 提取 PAT）

## 常用命令

```bash
# 构建（本机无系统 SDK，dotnet 在用户目录）
"$HOME/.dotnet/dotnet.exe" build WindowTinter.Wpf/WindowTinter.Wpf.csproj

# 单文件发布
"$HOME/.dotnet/dotnet.exe" publish WindowTinter.Wpf/WindowTinter.Wpf.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist/WindowTinter-v6.0.1-framework-dependent/
```
