# 暗幕 — WindowTinter

> 给任意 Windows 窗口叠加**深色半透明蒙版**的常驻小工具。
> 目标窗口在前台时显示暗色蒙版（鼠标穿透、不挡操作）；切到后台后自动变半透明，不遮挡其他窗口。支持同时监控多个窗口。

🌐 **产品主页**: [simiely.github.io/WindowTinter](https://simiely.github.io/WindowTinter/)
💻 **源码仓库**: [github.com/Simiely/WindowTinter](https://github.com/Simiely/WindowTinter)

![.NET](https://img.shields.io/badge/.NET-6.0%20Windows-purple)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-blue)
![License](https://img.shields.io/badge/License-MIT-green)
![Version](https://img.shields.io/badge/Version-3.9.0-orange)

---

## 目录

- [简介](#简介)
- [功能特性](#功能特性)
- [效果示意](#效果示意)
- [快速开始](#快速开始)
- [交互说明](#交互说明)
- [配置](#配置)
- [工作原理](#工作原理)
- [从源码构建](#从源码构建)
- [已知限制与故障排查](#已知限制与故障排查)
- [开发笔记](#开发笔记)
- [优化方向](#优化方向)
- [许可](#许可)

---

## 简介

**暗幕（WindowTinter）** 解决的是一个很具体的痛点：某些软件（阅读器、IDE、视频播放器、网页）本身没有夜间模式或护眼暗化，长时间使用刺眼。它不改变目标程序，而是用一个**置顶的纯黑半透明图层**盖在目标窗口上，把亮度压下来；一旦你切走，蒙版立刻消失，目标窗口改用自己的半透明样式退到后台，既不刺眼也不挡事。

工具本身常驻系统托盘，配置随 exe 走，解压即用、无需安装。

---

## 功能特性

- **前景蒙版** — 目标在前台时自动显示暗色蒙版，蒙版带 `WS_EX_TRANSPARENT`，鼠标点击完全穿透到下层窗口，不影响任何交互。
- **后台透明** — 目标切到后台后，通过给目标窗口自身加 `WS_EX_LAYERED` 让它整体半透明，不再遮挡其他窗口。
- **保持透明度模式（KeepTransparency）** — 开启后不走蒙版，前台后台统一用「窗口（后台）」滑块的透明度值，适合只想让窗口恒定变暗、不需要黑蒙版的场景。
- **双独立滑块**
  - **蒙版（前台）**：蒙版层的暗度，0–100%。
  - **窗口（后台）**：目标窗口退到后台时的半透明度，0–100%。
- **全局 / 单窗口透明度**
  - 默认开启「**全局统一透明度**」：所有应用共用上方两套滑块的值。
  - 关闭该开关后，每个目标窗口前出现「**○**」选中按钮，选中后上方滑块只配置该窗口；不同程序可设不同暗度（各自的蒙版 / 后台透明度随配置保存）。
- **多窗口同时监控** — 数量不限，每个目标独立管理、独立状态。
- **自动跟随** — 目标窗口移动 / 缩放 / 最小化 / 还原，蒙版同步对齐（`SetWinEventHook` 事件驱动 + 250ms 轮询兜底）。
- **自动发现** — 目标程序启动后（或被关闭后重开），每 3 秒自动尝试重新绑定，无需手动操作。
- **托盘常驻** — 关闭主窗口默认最小化到托盘继续后台监控；双击托盘图标可开关主窗口。
- **开机自启** — 可选，通过注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 实现。
- **深色界面** — 主窗口、滚动条、标题栏均应用 Windows 深色模式。
- **DPI 感知** — `PerMonitorV2`，高分屏 / 多屏不同缩放下显示正常。

---

## 效果示意

<!-- 截图占位：建议把主界面截图放到 docs/screenshot.png 后取消下一行注释 -->
<!-- ![主界面](docs/screenshot.png) -->

（README 待补充一张主界面截图。可用「📂 配置文件夹」旁的窗口，或运行后按 `PrintScreen` 自行截取。）

---

## 快速开始

1. 到 [Release](https://github.com/Simiely/WindowTinter/releases) 下载 `WindowTinter-v3.9.0.zip`，解压得到 `WindowTinter.exe` 与 `app.ico`（**两者必须同目录**）。
2. 双击运行 `WindowTinter.exe`。首次运行主窗口即出现，同时托盘出现图标。
3. 点击 **「+ 添加窗口」**，光标变成十字，移到目标窗口上会高亮其边框：
   - **左键** 选定该窗口；
   - **右键** 或 **Esc** 取消拾取。
4. 勾选目标条目里的 **「启用覆盖」**（或顶部的全局「启用覆盖」）→ 切到该窗口自动变暗，切走自动变半透明。
5. 拖动 **蒙版（前台）** 与 **窗口（后台）** 双滑块，实时调节暗度与后台透明度。
   - 按住滑块轨道任意位置可直接跳到该位置（自定义 `JumpTrackBar`），拖动时所有目标强制显示蒙版以便预览效果。
   - **全局统一透明度**（透明度分组内）默认勾选：所有监控窗口共用这两套值。取消勾选后进入「单窗口配置」模式——每个目标前有「○」按钮，点选后滑块只调该窗口，不同程序可各自设定。
6. **「窗口保持透明度」** — 勾选后前台也直接用统一透明度，不再叠加黑蒙版。
7. 关闭主窗口默认最小化到托盘继续监控；右键托盘图标 →「退出」可彻底关闭。

> 配置保存在 exe 同目录的 `WindowTinter.settings.json`。图标 `app.ico` 必须与 exe 同目录，否则开机自启时会因工作目录不同而无法加载图标导致崩溃。

---

## 交互说明

### 窗口拾取器（「+ 添加窗口」）

| 操作 | 效果 |
|---|---|
| 移动鼠标 | 十字光标下的最顶层窗口高亮反转边框 |
| 左键点击 | 选定该窗口并加入监控列表 |
| 右键 / Esc | 取消拾取 |

拾取器采用 `EnumWindows` 按 Z 序遍历的方案，**不对自己做显隐**，因此全程零闪烁。

### 系统托盘菜单

| 菜单项 | 作用 |
|---|---|
| 状态行（灰字） | 显示当前监控中 / 待激活数量 |
| 最小化到托盘 / 打开设置窗口 | 切换主窗口显隐（双击托盘图标等同此操作）|
| 启用 / 停用 | 全局开关蒙版与后台透明 |
| 退出 | 彻底退出并清理所有蒙版 / 透明度 |

### 主窗口按钮

- **🔄 重新查找** — 解绑全部并重新按配置绑定（目标多了 / 卡住时用）。
- **📂 配置文件夹** — 打开 exe 所在目录（方便备份 / 编辑 `settings.json`）。
- **ℹ 关于** — 版本与配置位置信息。
- **💾 保存配置** — 手动落盘（设置本身已即时持久化，此按钮为兜底）。
- **🚪 退出** — 彻底退出。
- **○ 选中按钮（单窗口配置模式）** — 关闭「全局统一透明度」后，每个目标前出现。点击选中该窗口，上方双滑块即只配置它；被选中的窗口以高亮底色标示。

---

## 配置

配置文件：`WindowTinter.settings.json`，位于 **exe 同目录**（便携设计，换目录不丢配置）。所有设置变更均即时写入，无需退出保存。

### JSON 结构示例

```json
{
  "Targets": [
    { "ProcessName": "notepad", "WindowTitle": "无标题 - 记事本", "Alpha": 75, "BackgroundAlpha": 50 }
  ],
  "Alpha": 75,
  "BackgroundAlpha": 50,
  "Enabled": true,
  "StartWithWindows": false,
  "MinimizeToTray": true,
  "KeepTransparency": false,
  "GlobalTransparency": true
}
```

### 字段说明

| 字段 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `Targets` | `TargetInfo[]` | `[]` | 监控目标列表，每项含 `ProcessName`（不含 `.exe`）、`WindowTitle`（精确匹配），以及可选的 `Alpha` / `BackgroundAlpha`（见下）|
| `Alpha` | `int` (0–100) | `75` | 蒙版（前台）暗度（全局统一模式生效）|
| `BackgroundAlpha` | `int` (0–100) | `50` | 窗口退到后台时的半透明度（全局统一模式生效）|
| `Enabled` | `bool` | `true` | 全局开关 |
| `StartWithWindows` | `bool` | `false` | 开机自启 |
| `MinimizeToTray` | `bool` | `true` | 关闭窗口时最小化到托盘（否则直接退出）|
| `KeepTransparency` | `bool` | `false` | 保持透明度模式（不走蒙版）|
| `GlobalTransparency` | `bool` | `true` | `true`=所有应用共用全局 `Alpha`/`BackgroundAlpha`；`false`=每个目标单独配置（见下）|

每个 `TargetInfo` 在「单窗口配置」模式（`GlobalTransparency=false`）下可带独立透明度：

| `TargetInfo` 字段 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `ProcessName` | `string` | `""` | 进程名（不含 `.exe`）|
| `WindowTitle` | `string` | `""` | 窗口标题（精确匹配）|
| `Alpha` | `int` (0–100) | `75` | 该窗口前台蒙版暗度（仅 `GlobalTransparency=false` 时生效）|
| `BackgroundAlpha` | `int` (0–100) | `50` | 该窗口退到后台时的半透明度（仅 `GlobalTransparency=false` 时生效）|

> 切换「全局统一透明度」开关时，程序会把当前全局值写入各目标作为各自起点，因此不会突然跳变。

> **向后兼容**：v2.x 旧配置（带 `.exe` 后缀的进程名、单窗口字段）会在首次加载时自动迁移，无需手动处理。

---

## 工作原理

### 1. 前景蒙版（盖在窗口上的黑层）

蒙版本身是一个**置顶、无边框的分层窗口**（`WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TRANSPARENT`）：

- 用 `UpdateLayeredWindow` + 一张纯黑 `Bitmap` × alpha，交给 DWM 硬件合成，物理盖在目标窗口之上；
- `WS_EX_TRANSPARENT` 让鼠标事件穿透，用户照常操作下层窗口；
- `WS_EX_TOPMOST` 保证蒙版永远在目标窗口上方。

> 为什么不用 Magnifier API / WGC？前者同样是真窗口且挡交互，后者需要 Win10 1903+ 与大量 WinRT/D3D 基础设施。对「盖一层黑布」的需求来说 `UpdateLayeredWindow` 最简单可靠。详见 [DEV.md](./DEV.md) 第 1 节。

### 2. 后台透明（让窗口自己变暗）

目标切到后台时，不再用蒙版，而是直接给**目标窗口**加 `WS_EX_LAYERED` 并调用 `SetLayeredWindowAttributes` 设置整体 alpha。窗口退到后台后自然半透明，不挡其他内容。恢复前台时用 `RedrawWindow` 清掉分层样式，避免 `InvalidateRect` 在某些情况下的残留。

### 3. 保持透明度模式

开启 `KeepTransparency` 后，无论前台后台都只走 `SetLayeredWindowAttributes`，用 `BackgroundAlpha` 的设定值，完全跳过蒙版。

### 4. 自动跟随（事件驱动 + 轮询兜底）

- `SetWinEventHook` 监听 `EVENT_SYSTEM_FOREGROUND` 与前台的 `EVENT_OBJECT_LOCATIONCHANGE / SHOW / HIDE / DESTROY`；
- 每个目标另有一个 **250ms** 定时器兜底轮询位置 / 可见性，事件漏掉时也能对齐；
- 前台切换、蒙版/透明切换用 `BeginInvoke` 错开一帧，并加 `IsWindow` + `GetForegroundWindow` 守卫，消除「闪白」与时序竞态（详见 DEV.md 第 3、4 节）。

### 5. 自动发现

主窗口加载一个 **3 秒** 定时器：清理已销毁的条目（放回「待激活」），并扫描配置中的目标是否已启动、尚未绑定，是则自动绑定。

### 6. 深色界面

- 标题栏：`DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)`；
- 列表滚动条：`SetWindowTheme("DarkMode_Explorer")`；
- 其余控件手动按深色配色主题化。

### 技术栈一览

| 组件 | 技术 |
|---|---|
| 前景蒙版 | `UpdateLayeredWindow` + `WS_EX_LAYERED \| WS_EX_TOPMOST \| WS_EX_TRANSPARENT` |
| 后台透明 | `SetLayeredWindowAttributes` + `WS_EX_LAYERED` |
| 鼠标穿透 | `WS_EX_TRANSPARENT` |
| 窗口跟踪 | `SetWinEventHook` + 250ms 轮询兜底 |
| 窗口拾取 | `EnumWindows` 按 Z 序遍历（零闪烁）|
| 框架 | .NET 6 WinForms（单文件自包含发布）|

---

## 从源码构建

### 前置要求

- Windows 10 或更高版本（依赖 DWM 合成、深色模式 API）
- [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)

### 本地构建（调试）

```powershell
git clone https://github.com/Simiely/WindowTinter.git
cd WindowTinter
dotnet build -c Release
```

构建产物在 `bin/Release/net6.0-windows/`。直接运行 `WindowTinter.exe` 即可（需 `app.ico` 同目录）。

### 发布单文件自包含包（无需目标机安装 .NET）

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

产物位于 `bin/Release/net6.0-windows/win-x64/publish/`，将 `WindowTinter.exe` 与 `app.ico` 一并打包分发即可。

### CI / CD

推送以 `v` 开头的 tag（如 `v3.9.0`）会触发 GitHub Actions：自动构建单文件 exe、打包并发布到 GitHub Release；同时 `main` 分支根目录的 `index.html` 会自动部署到 GitHub Pages（产品主页）。

---

## 已知限制与故障排查

- **部分程序不支持 `WS_EX_LAYERED`**：个别应用（尤其是自绘 UI 或某些游戏 / 全屏独占程序）设置透明度可能无效或表现异常。程序已用 `try/catch` 兜底，遇到此类窗口建议改用「保持透明度」模式或直接移出监控。
- **基于标题匹配**：目标绑定依赖「进程名 + 精确窗口标题」。如果目标窗口标题会动态变化（如浏览器标签页、含实时时间的播放器），标题一变可能掉回「待激活」，点「🔄 重新查找」或重新添加即可。（后续可加「标题包含」模糊匹配，见[优化方向](#优化方向)。）
- **提权窗口**：若目标程序以管理员身份运行，而本工具以普通权限运行，跨完整性级别修改其窗口样式可能失败。此时请以管理员身份运行 `WindowTinter.exe`。
- **强制结束后的残留**：若被任务管理器强杀，目标窗口可能短暂残留半透明；程序下次启动会自动用 `RedrawWindow` 修复，无需手动处理。
- **无颜色反相**：蒙版是纯黑半透明层，只压暗不反色（v2 曾尝试反色后已移除）。

---

## 开发笔记

完整的踩坑记录、架构演进与代码审计清单见 **[DEV.md](./DEV.md)**，覆盖 25 个关键技术问题：

- 渲染方案选型（UpdateLayeredWindow vs Magnifier vs WGC）
- 后台透明与 `SetWindowLong` 的坑（别用 `SWP_FRAMECHANGED`、用 `InvalidateRect`）
- WinEvent 死循环与闪白修复
- `.exe` 后缀迁移、关机拦截、配置即时持久化
- 拾取器零闪烁方案、图标路径崩溃、退出清理顺序
- GitHub Actions CI/CD 与 GitHub Pages

---

## 优化方向

以下方向供后续迭代参考（也是本项目「需要优化」的重点）：

1. **收窄 WinEvent 监听范围**：当前 `SetWinEventHook` 以 `idProcess=0` 系统级监听 `EVENT_OBJECT_LOCATIONCHANGE` 等事件，任何窗口移动都会触发回调。可改为**按目标进程分别挂钩**（`idProcess = 目标 pid`），或仅保留 `EVENT_SYSTEM_FOREGROUND` + `SHOW/HIDE/DESTROY`，位置跟随完全交给 250ms 轮询，显著降低系统级回调开销。
2. **窗口匹配更健壮**：`FindByTitleAndProcess` 目前要求标题**精确相等**。可加入「标题包含（子串）」回退匹配，让浏览器、编辑器等动态标题窗口稳定保持绑定。
3. **蒙版位图缓存**：`MaskOverlay.RenderLayered` 每次渲染都 `GetHbitmap()` 新建并 `DeleteObject` 释放 GDI 位图，移动蒙版时存在 GDI 句柄抖动，可缓存 HBITMAP 句柄减少开销。
4. **代码注释修正**：`Settings.cs` 顶部 XML 注释写「配置存于 `%AppData%/WindowTinter/`」，实际已迁移到 exe 同目录，注释与行为不一致，易误导维护者。

---

## 许可

[MIT License](./LICENSE)。可自由使用、修改、再分发。
