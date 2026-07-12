# WindowTinter

> 给**任意窗口**叠加**深色护眼蒙版**或**真·反色**的 Windows 常驻小工具。
> 典型场景：官方百度网盘 Windows 客户端太亮、没有自带深色模式，用它一键压暗。

---

## 为什么需要它

官方百度网盘 Windows 客户端是基于 **原生 C++ + duilib（DirectUI）** 写的，不是 Electron / Chromium 网页壳。
因此**无法注入 CSS 做真正的深色主题**——网上 90% 所谓「百度网盘深色模式」教程其实是**手机 App**
（路径：我的 → 设置 → 夜间模式），对 Windows 桌面端完全无效。

结论：要让原生客户端变暗，只能走**外部覆盖**路线。本项目提供两种覆盖方式，可自由切换：

| 模式 | 原理 | 稳定性 | 适用 |
|---|---|---|---|
| **深色蒙版** | 在目标窗口上盖一层半透明黑 | 稳定，鼠标可穿透 | 日常护眼首选 |
| **真·反色** | 用 Magnification API 对目标区域反相 | 实验性，偶有渲染瑕疵 | 极致压暗 / 特殊需求 |

---

## 功能一览

- ✅ **通用任意窗口**：默认盯百度网盘，也能用「选择窗口」点击拾取任意程序。
- ✅ **两种覆盖模式**：深色蒙版 / 真·反色，托盘菜单一键切换。
- ✅ **托盘常驻**：后台运行，不占任务栏；**左键 / 右键点击托盘图标**即可弹出菜单操作（无快捷键）。
- ✅ **托盘菜单操作**：启用 / 停用、切模式、调透明度、选窗、退出，全部在托盘菜单完成。
- ✅ **透明度可调**：蒙版浓淡实时增减。
- ✅ **开机自启**：写入注册表 `Run` 项。
- ✅ **自动跟随**：目标窗口移动 / 缩放 / 最小化时，蒙版自动同步或隐藏。
- ✅ **只盖可见区域**：用窗口区域裁剪，盖在目标之上的其他窗口**不会被压暗 / 反色**（v1.1.0 修复）。

---

## 构建（在 Windows 上）

本项目是 WinForms 应用（`net6.0-windows`），**只能在 Windows 上构建与运行**。

```powershell
# 需要 .NET 6 SDK（或更高版本）
cd WindowTinter
dotnet build -c Release

# 产物：
bin/Release/net6.0-windows/WindowTinter.exe
```

> 若本机是 .NET 8 SDK，把 `WindowTinter.csproj` 里的 `net6.0-windows`
> 改成 `net8.0-windows` 即可，其余不变。

> **不想自己构建？** 已配置 GitHub Actions：打 `v*` tag 推送后自动在 Windows runner 上
> `dotnet publish`（单文件自包含 exe）并打包成 `WindowTinter.zip` 上传到对应 **Release**。
> 直接去 [Releases](https://github.com/Simiely/WindowTinter/releases) 下载解压即可用，无需安装 .NET。

> **v1.1.0 改进**：覆盖层现在只作用于目标窗口的**可见区域**（盖在上面的其他窗口不会被误伤）；
> 反色镜头改为对放大镜窗口本身做区域裁剪，只反色百度**可见部分**，被盖住的地方露出真实内容；
> 窗口跟踪改为**事件驱动**（监听窗口移动/置顶/前台切换），仅在几何或遮挡变化时才重绘，
> 不再每 100ms 无脑刷新；**已移除全局热键**，操作全部走托盘菜单。

---

## 使用（超级简单）

1. **运行** `WindowTinter.exe`，系统托盘出现图标（主窗口自动隐藏，不干扰你）。
2. **左键或右键点击托盘图标**打开菜单：
   - `启用 / 停用` —— 开关覆盖层（默认对 `BaiduNetdisk.exe` 生效）。
   - `模式` —— `深色蒙版`（稳定）或 `真·反色`（实验）。
   - `透明度` —— `调暗一点 (-)` / `调亮一点 (+)` 实时调节。
   - `选择窗口…` —— 点击屏幕上的任意窗口进行拾取；
     `重新绑定百度网盘` —— 自动查找客户端窗口。
   - `开机自启` —— 勾选后写入注册表，下次开机自启。
   - `退出` —— 关闭程序。
3. 蒙版层为**鼠标穿透**——覆盖百度网盘时你照样能正常点击、拖拽、输入；
   窗口移动 / 缩放 / 最小化时蒙版自动跟随或隐藏。

> 设置持久化在 `%LOCALAPPDATA%/WindowTinter/settings.json`，下次启动自动恢复。

---

## 原理简述

- **深色蒙版**：创建一个 `WS_EX_LAYERED | WS_EX_TRANSPARENT` 的半透明黑色分层窗口，
  `SetLayeredWindowAttributes` 设定不透明度，`SetWindowPos` 精准盖在目标窗口之上并定时同步位置尺寸。
  `WS_EX_TRANSPARENT` 让鼠标点击「穿透」蒙版落到下层窗口。
- **真·反色**：通过 `MagInitialize` + `CreateWindowEx` 创建 1× 放大镜窗口，
  用 `MagSetColorEffect` 套一个反色颜色矩阵（`R'=1-R`，`G'=1-G`，`B'=1-B`），
  对目标区域做反相覆盖（实验性，部分窗口可能有渲染瑕疵）。

更多踩坑与关键技术细节，见 **[DEV.md](./DEV.md)**。

---

## 文件结构

| 文件 | 作用 |
|---|---|
| `Native.cs` | 全部 Win32 / Magnification P/Invoke 声明 |
| `Settings.cs` | JSON 持久化 + 注册表开机自启 |
| `TargetTracker.cs` | 枚举 / 拾取 / 跟踪目标窗口 |
| `MaskOverlay.cs` | 深色蒙版分层窗口 |
| `InvertLens.cs` | Magnifier 反色镜头 |
| `WindowPickerForm.cs` | 点击拾取窗口的捕获层 |
| `Program.cs` | 主控制器（托盘 / 菜单 / 模式切换） |

---

## 不想自建？现成替代

- **WindowTop**（[WindowTop/WindowTop-App](https://github.com/WindowTop/WindowTop-App)）：
  能给任意窗口套深色，但 Dark Mode 多为 Pro 付费 / 试用。
- **系统级方案**：Windows 夜间灯光、系统深色模式、高对比度主题、
  放大镜反色（`Win + +` 后 `Ctrl + Alt + I`）——无需安装软件，但不够精准。

---

## 许可

MIT License。详见 [LICENSE](./LICENSE)。
