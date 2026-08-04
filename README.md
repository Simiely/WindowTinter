# 暗幕 (WindowTinter) v5.6.0

**给任意窗口设置透明度、并在其正下方垫纯黑的 Windows 常驻小工具。** 纯 WPF 重写，单文件发布。

当窗口半透明时，露出的通常是背后杂乱的桌面或其它窗口——暗幕可以在窗口正下方垫一层纯黑底板，让半透明稳定呈现为"压暗"效果。

## 功能

- **窗口压暗控制** — 统一或按窗口独立配置压暗强度（0%~100%）
- **纯黑下层遮罩** — 半透明窗口背后垫纯黑，避免透出桌面/其它窗口；纯 Win32 分层窗口实现，**DPI 切换不错位**
- **底板圆角** — 0~20px 圆角，贴合 Win11 窗口圆角，可全局统一或逐窗口独立设置
- **多窗口同时控制** — 3 列扑克牌卡片展示目标窗口，实时快照、各自独立配置
- **全局/单独模式** — 压暗与圆角各有独立的全局开关，关闭后可点卡片单独设置
- **自动绑定** — 目标窗口关闭再启动后自动重新绑定（进程+类名识别）
- **窗口快照** — 启动绑定与窗口首次出现时自动抓图，也可手动「⟳ 刷新快照」
- **深色主题** — 粉色主调（#FF9292）原生暗色 UI，沉浸式深色标题栏
- **托盘驻留** — 关闭窗口最小化到系统托盘，效果持续运行；菜单跟随系统深浅色主题

## 使用

1. 运行 `WindowTinter.exe`（单文件，无需额外 dll / ico）
2. 点击 **+ 添加窗口**，鼠标移到目标窗口上点击（拾取器十字光标）
3. 调整 **效果控制** 组中的参数：
   - 拖动 **压暗强度** 滑块调整半透明程度
   - 拖动 **圆角半径** 滑块贴合窗口圆角（Win11 建议 8~15px）
4. 关闭窗口即最小化到托盘，双击托盘图标恢复窗口

### 目标卡片操作

- **点击卡片** = 选中该窗口单独调整（勾选"全局统一"时不可选）
- **✎** = 重命名别名　**×** = 移除目标
- 卡片快照区显示窗口实时缩略图；窗口未启动时显示「⏳ 窗口未启动」

### 提示

- 取消勾选 **全局统一压暗** 或 **全局统一圆角** 后，点击卡片选中窗口单独设置
- 如果目标以管理员身份运行，请右键"以管理员身份运行"本程序
- 配置文件 `WindowTinter.settings.json` 与 exe 同目录（首次运行自动生成）
- 勾选 **开机自启** 后，开机仅驻留托盘图标、不弹出主窗口；双击托盘图标即可打开设置窗口

## 下载

从 [Releases](https://github.com/Simiely/WindowTinter/releases) 下载最新 `WindowTinter.zip`（内含单个 `WindowTinter.exe`）。

**要求**：[.NET 6 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/6.0)（x64）

## 构建

```bash
# 需要 .NET 6 SDK（本机路径示例：~/.dotnet/dotnet.exe）
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist/WindowTinter-v11-framework-dependent/
```

## 技术栈

- **UI**：WPF（XAML 原生圆角/阴影/数据绑定/DPI 矢量），深色主题 `Themes/Dark.xaml`
- **架构**：MVVM（手写 ObservableObject / RelayCommand，无第三方框架）
- **黑底**：纯 Win32 分层窗口（`CreateWindowExW` + `UpdateLayeredWindow`），物理像素直控、吞 `WM_DPICHANGED`，DPI 切换不错位
- **托盘**：Hardcodet.NotifyIcon.Wpf 图标 + Win32 `TrackPopupMenu` 原生菜单（`SetPreferredAppMode` 跟随系统深色）
- **发布**：.NET 6 单文件（框架依赖），app.ico 嵌入程序集

---

## 文档索引

| 文档 | 给谁看 | 内容 |
|---|---|---|
| [`AGENTS.md`](./AGENTS.md) | AI / 未来的你 | 技术栈、关键坑（Win32 窗口操作/托盘生命周期）、构建命令 |
| [`DEV.md`](./DEV.md) | 开发者 | WinForms 时代问题记录 + 架构演进 |
| [`WPF_MIGRATION_PLAN.md`](./WPF_MIGRATION_PLAN.md) | 开发者 | WinForms → WPF 迁移计划与适配点 |
| [`CHANGELOG.md`](./CHANGELOG.md) | 所有人 | 版本变更记录 |
