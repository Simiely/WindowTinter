# 暗幕 WindowTinter 7.2.0

给任意 Windows 窗口设置透明度（压暗），并在窗口正下方垫一层纯黑底板，让半透明窗口稳定呈现为「压暗」，而不是透出背后的桌面和其它窗口。

单文件 WPF 小工具，常驻托盘；配置写在 exe 同目录的 `WindowTinter.settings.json`。

> **仓库结构提示**：当前应用是 [`WindowTinter.Wpf/`](./WindowTinter.Wpf/)（WPF / .NET 6）。根目录的 `MainForm*.cs`、`TargetCard.cs`、`WindowPickerForm.cs`、`WindowTinter.csproj` 等是 WinForms 时代的历史源码，仅作归档，不参与构建；根目录的 `index.html` 是早期落地页。

## 功能

- **窗口压暗**：0%~100% 透明度（100% = 完全不透明），全局默认 + 每张卡片独立自定义
- **纯黑底板**：半透明窗口正下方垫纯黑（`UpdateLayeredWindow` 分层窗口），DPI 切换不错位；底板圆角贴合窗口
- **多窗口并行**：卡片列表展示每个目标（缩略图 / 标题 / 状态），支持单列或双列布局
- **单卡开关**：全局总闸关闭时，各卡自己的启用状态保持不变
- **卡片折叠**：点卡片展开参数区，`Esc` 收起；chevron 可单独折叠而不把目标带到前台
- **自动重绑**：目标窗口关闭再启动后，按「进程 + 窗口类名 / 标题」重新绑定（3 秒兜底 + WinEvent 即时响应）
- **快照**：绑定与窗口首次出现时自动抓图，也可点刷新按钮；抓不到的窗口显示占位
- **托盘驻留**：关闭主窗口即最小化到托盘，效果持续；托盘菜单为深色自绘菜单
- **开机自启**：写入 `HKCU\...\Run`

## 下载与运行

从 [Releases](https://github.com/Simiely/WindowTinter/releases) 下载 `WindowTinter-v7.2.0-win-x64.zip`，解压后直接运行 `WindowTinter.exe`。

**前提**：

- Windows 10 / 11，x64
- [.NET 6 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/6.0)（框架依赖发布，不内置运行时）
- 不需要安装：没有安装器、不写注册表（除可选的开机自启项）

**首次运行**会在 exe 同目录生成 `WindowTinter.settings.json`；程序完全以这个文件为准，没有云端同步。

## 使用

1. 点「添加窗口」，进入拾取器：移动鼠标高亮目标窗口，左键确认。
2. 在底部「全局默认」面板调整压暗强度与圆角；需要逐窗口不同值时，点开卡片单独设置（会标记为「已自定义」，不再跟随全局）。
3. 调整完后关闭主窗口 = 收进托盘，效果继续；托盘图标双击恢复主窗口。

**拾取器取消**：`Esc`、鼠标右键、或拾取器 HUD 上的取消按钮。取消不会写入配置、不会绑定、不会改动任何目标窗口状态。

**快捷键**（主窗口内，键盘可达）：

| 快捷键 | 作用 |
|---|---|
| `Esc` | 收起当前展开的卡片（滑块拖动中按下则只结束拖动，不收起） |
| `Enter` / `Space` | 聚焦卡片后展开该卡片 |
| `Ctrl+Alt+↑ / ↓` | 窗口外轮廓圆角 ±1px（0~32） |
| `Ctrl+Alt+← / →` | 内容裁切圆角 ±1px（0~32） |
| `Ctrl+Alt+Shift+↑ / ↓` | 裁切内缩 ±1px（0~6） |
| `Ctrl+Alt+Shift+← / →` | 根边框描边弧 ±1px（0~32） |

圆角快捷键改的是「窗口自身外形」的调试参数，改动即时生效（自己 `SetWindowRgn` 重建区域，不重启），并立即写入配置；调过之后底栏才会出现读数（`外 N/Npx · 框 N · 裁 N · 缩 N`，括号内是实际下发给系统的设备像素半径）。正常使用看不到这块读数。

**开机自启行为（如实说明）**：勾选后会写入注册表命令 `"<exe路径>" /startup`。当前 WPF 版**没有解析 `/startup`**：开机自启与手动双击行为相同，都会显示主窗口。旧 WinForms 版「自启只驻留托盘」的行为在 WPF 版尚未实现（见 [docs/LIMITATIONS.md](./docs/LIMITATIONS.md)）。

## 常见问题

- **Windows 提示已阻止此应用（SmartScreen）**：发布包未做代码签名。点「更多信息 → 仍要运行」。
- **配置丢失 / 设置不保存**：程序在只读目录（如 `Program Files`）、被移动的便携盘、或多用户共享目录下无法写盘，`Settings.Save()` 会静默失败。把 exe 放到可写目录（如自己的桌面/用户目录）再运行。
- **Win10 上圆角底板边缘露出缝隙**：Win10 窗口本身是直角，底板圆角比窗口更"圆"，四角会露出底色。建议把圆角半径设为 `0`。
- **调整某个窗口没反应**：目标以管理员权限运行时，本程序也必须「以管理员身份运行」（跨完整性级别调用会静默失败，程序会弹窗提示）。
- **桌面 / 任务栏选不中**：拾取器显式排除 `Progman` / `WorkerW` / `Shell_TrayWnd`，桌面层不支持压暗。
- **关闭主窗口后程序还在**：默认「最小化到托盘」，托盘右键可退出；也可在设置里关闭该选项。
- **开机会弹出主窗口**：见上节 `/startup` 说明，属于当前未实现项。

## 构建

必须在 WPF 项目目录执行（根目录的 `WindowTinter.csproj` 是旧 WinForms 项目，不要构建它）：

```powershell
cd WindowTinter.Wpf
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ..\publish\WindowTinter-v7.2.0-win-x64
```

详细步骤、产物说明与发布流程见 [docs/BUILD.md](./docs/BUILD.md)。

## 技术栈

- **UI**：WPF（XAML 圆角/数据绑定/DPI 矢量），深色主题 `Themes/Dark.xaml`；无边框窗 + `WindowChrome`
- **架构**：轻量 MVVM（手写 `ObservableObject` / `RelayCommand`，无框架、无 DI 容器）
- **黑底**：纯 Win32 分层窗口（`CreateWindowExW` + `UpdateLayeredWindow` + `SetWindowRgn`），物理像素直控
- **托盘**：`Hardcodet.NotifyIcon.Wpf` + 自绘深色 WPF 菜单
- **发布**：.NET 6 单文件（框架依赖），图标嵌入程序集

## 文档索引

| 文档 | 内容 |
|---|---|
| [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) | 架构与主线：绑定/应用/还原、底板、跟随、纯函数边界、线程模型 |
| [docs/LIMITATIONS.md](./docs/LIMITATIONS.md) | 已知限制、不支持项与未验证清单 |
| [docs/PERFORMANCE.md](./docs/PERFORMANCE.md) | 性能实测数据、测试条件与未测项 |
| [docs/BUILD.md](./docs/BUILD.md) | 构建、单文件发布、打包与 Release 流程 |
| [docs/INSTALL.md](./docs/INSTALL.md) | 安装、升级、卸载与配置迁移 |
| [docs/DECISIONS.md](./docs/DECISIONS.md) | 「不值得做 / 不要做」的结论，避免重复劳动 |
| [CHANGELOG.md](./CHANGELOG.md) | 版本变更记录 |
| [AGENTS.md](./AGENTS.md) | 给 AI / 维护者的关键坑与约定 |
| [DEV.md](./DEV.md) | WinForms 时代的问题记录（历史资料） |

## 许可

[MIT](./LICENSE)
