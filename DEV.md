# DEV.md — 开发笔记与踩坑记录

> 本文件记录本项目开发过程中遇到的**关键问题、技术决策与修复过程**。
> 目的是：以后再做「给某个原生 Windows 窗口加深色 / 反色」之类的需求时，
> 可以直接参考，少走弯路。

---

## 1. 核心判断：原生客户端不能注入 CSS

**问题**：用户想要「百度网盘深色模式」。搜索到的大量教程都说可以，但实测无效。

**交叉验证结论**：
- 官方百度网盘 Windows 客户端 = **原生 C++ + duilib（DirectUI）**，不是网页壳（非 Electron / Chromium）。
- 网上 90% 的「百度网盘深色模式」教程其实是**手机 App**（路径：我的 → 设置 → 夜间模式），
  对 Windows 桌面端完全无效 —— 属于典型的搜索污染。
- 因为不是网页，所以**不可能通过注入 CSS / JS 来做真·深色主题**。

**决策**：只能走**外部覆盖**（在窗口之上叠加蒙版或反色），而非修改内部渲染。
这一点是整个工具技术路线的根基，先确认再动手，避免浪费大量时间。

**可复用的判断方法**：用任务管理器 / 进程资源管理器看目标进程的模块依赖——
如果依赖里有 `libcef`、`Electron`、`Chromium`、`WebKit` 之类，才可能是网页壳，能做 CSS 注入；
否则一律按「原生控件」处理，走外部覆盖。

---

## 2. 深色蒙版：分层窗口 + 鼠标穿透

**技术点**：Win32 的 `WS_EX_LAYERED`（分层窗口，支持半透明）配合 `WS_EX_TRANSPARENT`（鼠标穿透）。

```csharp
// 在 OnHandleCreated 时追加扩展样式
int ex = GetWindowLong(Handle, GWL_EXSTYLE);
ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT;
SetWindowLong(Handle, GWL_EXSTYLE, ex);

// 设定半透明黑色
SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA); // alpha: 0-255
```

**关键点**：
- `WS_EX_TRANSPARENT` 让鼠标点击「穿过」蒙版落到下层窗口，
  所以覆盖百度网盘时用户依然能正常点击、输入、拖拽——这是体验成立的前提。
- 用 `SetWindowPos` 带 `SWP_NOACTIVATE` 把蒙版盖到目标之上，并定时同步目标窗口的
  `RECT`（移动 / 缩放 / 最小化都要处理：最小化或不可见时 `Hide()`）。
- 关键常量：`WS_EX_LAYERED=0x80000`、`WS_EX_TRANSPARENT=0x20`、`GWL_EXSTYLE=-20`、`LWA_ALPHA=0x2`。

**踩坑**：蒙版窗口自己**不要**设 `WS_EX_TOPMOST` 之外的焦点行为，否则会抢焦点打断用户操作；
定位用 `SetWindowPos` 且务必带 `SWP_NOACTIVATE | SWP_NOZORDER` 之外的合适标志，避免把目标窗口顶起/盖住交互。

---

## 3. 真·反色：Magnification API 颜色矩阵

**技术点**：用 Windows 自带的 **Magnification API**（放大镜）做实验性反色。它本质是为放大镜设计的，
但我们可以用一个 1× 的隐藏放大镜窗口，对目标区域套颜色变换。

**反色矩阵**（`MAGCOLOREFFECT`，5×5 = 25 个元素，行主序）：

```
[ -1,  0,  0,  0,  1 ]   // R' = 1 - R
[  0, -1,  0,  0,  1 ]   // G' = 1 - G
[  0,  0, -1,  0,  1 ]   // B' = 1 - B
[  0,  0,  0,  1,  0 ]   // A' = A
[  0,  0,  0,  0,  1 ]
```

**调用流程**：
1. `MagInitialize()` —— 必须最先调用，且要在创建放大镜窗口之前。
2. 创建一个**隐藏的宿主 Form**（不显示），再在其上 `CreateWindowEx(0, "Magnifier", ...)`。
3. `MagSetWindowTransform` 设单位矩阵（1× 不变形）。
4. `MagSetColorEffect(hwnd, ref effect)` 套上面的反色矩阵。
5. 每帧：`MagSetWindowSource(hwnd, rect)` 指定要反色的源区域 + `SetWindowPos` 把放大镜窗口摆到目标位置覆盖它。
6. 退出：`DestroyWindow` + `MagUninitialize()`。

**踩坑**：
- `MagInitialize` 失败通常意味着当前系统不支持 / 已被占用；要做失败兜底（回退到蒙版模式）。
- 反色对某些窗口（如硬件加速的 GPU 渲染内容、视频）可能**不生效或闪烁**——所以标记为「实验性」，
  默认模式用更稳的蒙版。
- 放大镜窗口必须有一个真实存在的父窗口句柄来承载；直接用 `IntPtr.Zero` 创建在很多系统上会失败。

---

## 4. Linux 沙箱无法构建 WinForms —— 用「类型桩」做编译校验

**问题**：开发环境是 Linux（无 Windows Desktop 工作负载），直接 `dotnet build` 会报：

```
NETSDK1100: Windows is required to build Windows desktop applications
```

这是**平台门禁**，不是代码错误。

**解决方案**：写一组「类型桩（stubs）」把 WinForms / 系统绘图 API 占位出来，
只做**编译期类型 / 语法校验**，证明 8 个源文件类型正确、方法签名对得上：

1. 临时建 `Stubs/WinFormsStubs.cs` 与 `Stubs/SystemDrawingStubs.cs`，
   用 `partial class` / 占位类型模拟 `Form`、`Control`、`NotifyIcon`、`MessageBox`、`Color`、`Brush` 等。
2. 临时 csproj 把 `TargetFramework` 改成普通 `net6.0`（去掉 `-windows`），
   引用 `System.Drawing.Common` 包，关闭对 WinForms SDK 的依赖。
3. `dotnet build` 通过（**Build succeeded, 0 Error**）即说明源码层面没问题。
4. **校验后删除 Stubs 与临时 bin/obj，恢复正式 `net6.0-windows` csproj**——
   桩代码只用于本地校验，绝不能进正式提交。

**教训**：跨平台 CI 无法直接编译 Windows 桌面应用。如果以后要 CI 出 exe，
需要在 Windows runner（GitHub Actions `windows-latest`）上构建，而不是在 Linux 上硬编。

---

## 5. 修复过的真实 Bug（共 4 个）

| # | 现象 | 根因 | 修复 |
|---|---|---|---|
| 1 | `Program.cs` 编译报错找不到 `SystemIcons` | 缺 `using System.Drawing;` | 顶部补 `using System.Drawing;` |
| 2 | 保存为「反色」模式且启用后，启动蒙版不出现 | `OnLoad` 只调 `ApplyMode()` 未调 `_invert.Start()`，反色镜头未初始化 | 在 `if (_settings.Enabled)` 块内补 `if (_settings.Mode == "Invert") _invert.Start();` |
| 3 | 从「反色」切回「蒙版」后，反色层残留 | `SetMode` 切回蒙版时未隐藏反色层 | 在 `SetMode` 的 `else` 分支加 `_invert.Hide();` |
| 4 | `Hotkey.cs` 编译报错找不到 `Keys` | 缺 `using System.Windows.Forms;` | 顶部补 `using System.Windows.Forms;` |

**额外坑（环境）**：本机 .NET SDK 是 6.0.301，不支持 `net8.0-windows`，
`dotnet build` 报 `NETSDK1045`；把 csproj 改成 `net6.0-windows` 解决。

---

## 6. 现成方案对比（需求调研）

| 方案 | 类型 | 是否需要安装 | 局限 |
|---|---|---|---|
| **WindowTinter（本项目）** | 自建小工具 | 是（单 exe） | 反色为实验性 |
| [WindowTop](https://github.com/WindowTop/WindowTop-App) | 第三方工具 | 是 | Dark Mode 多为 Pro 付费 / 试用 |
| Desktop Dimmer | 第三方工具 | 是 | 只能整屏调暗，不能精准罩单个窗口 |
| Windows 夜间灯光 | 系统内置 | 否 | 偏暖黄，不是真深色，对白色背景压暗有限 |
| 系统深色模式 / 高对比度 | 系统内置 | 否 | 原生客户端不跟随 |
| 放大镜反色 `Win + +` → `Ctrl + Alt + I` | 系统内置 | 否 | 全局反色，无法只罩一个窗口 |

**结论**：要「只罩百度网盘一个窗口」且免费可控，自建是更优解。

---

## 7. 设置与默认值（快速参考）

- 默认目标进程名：`BaiduNetdisk.exe`
- 默认模式：`Mask`（深色蒙版）；可选 `Invert`（真·反色）
- 默认不透明度 `Alpha = 115`（约 45% 黑）
- 默认热键：`Ctrl + Alt + D`
- 设置文件：`%LOCALAPPDATA%/WindowTinter/settings.json`
- 开机自启：注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 写入 `WindowTinter`
- 目标用「进程名」而非 `hwnd` 持久化——因为 `hwnd` 跨进程 / 重启不稳定，启动再按进程名重查。
