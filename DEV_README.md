# 暗幕 开发笔记 — DEV_README

## 项目概况

- **语言/框架**：C# 10 / .NET 6.0-windows / WinForms
- **入口**：`Program.cs` — `MainForm`（partial 类，`MainForm.UI.cs` 含界面代码）
- **构建**：`dotnet build -c Release` / 单文件发布 `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true`

---

## 关键问题纪要

### 1. DWM 阴影导致 `GetWindowRect` 偏大

**现象**：黑底板比窗口实际可见区大一圈（微信等含 DWM 阴影的程序尤为明显）。

**根因**：`GetWindowRect` 返回的矩形**包含 DWM 阴影边**（4~8 px），而阴影是透明区域，底板在此处未被窗口遮挡，直接暴露。

**解决**：改用 `DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, ...)` 获取排除阴影后的真实可见矩形。失败时回退 `GetWindowRect`。

**涉及文件**：
- `Native.cs` — `DwmGetWindowAttribute` P/Invoke + `GetVisibleWindowRect()` 辅助方法
- `Program.cs:244-245` — `CreateEntry` 的 `OnUpdate` 中传给 `BlackPlate.AlignBehind` 前调用 `GetVisibleWindowRect`

### 2. 黑底板的 Z 序必须严格黏在目标正后方

**原理**：`SetLayeredWindowAttributes` 的 `LWA_ALPHA` 是整窗均匀半透明，露出的像素来自 Z 序中**紧贴它正后方**的窗口。如果有其它窗口插进中间，半透明透出的就不再是黑底板。

**解决**：`BlackPlate.AlignBehind` 使用 `SetWindowPos(Handle, targetHandle, ...)` 将底板插到目标正后方。现有 `WinEvent` + 250ms 轮询在每次目标移动/显示变化时重新对齐。

**涉及文件**：
- `BlackPlate.cs:50-52` — `SetWindowPos(Handle, targetHandle, ...)`

### 3. `TargetInfo` 作为 Dictionary Key 的哈希稳定性

**风险**：`TargetInfo` 被用作 `Dictionary<,>` 和 `HashSet<>` 的 key。如果 `ProcessName` 或 `WindowTitle` 在入字典后被修改，哈希值变化导致条目永久"丢失"。

**解决**：`ProcessName` 和 `WindowTitle` 设为 `{ get; init; }`（仅构造时可写）。`BackgroundAlpha` 仍为 `{ get; set; }`（不在哈希计算中）。

**涉及文件**：
- `Settings.cs:13-14`
- `Program.cs:880-892` — `PickWindow` 改用对象初始化器构造

### 4. `MainForm` 拆分为 Partial 文件

**动机**：`MainForm` 约 1000 行，包含 UI 构建、事件驱动、窗口管理、权限检测、托盘、WinEvent 钩子。

**拆分方案**：
- `Program.cs` — 核心状态、生命周期（OnLoad/OnShown/OnActivated）、条目管理（CreateEntry/SetTargetAlpha/TryBindTarget/AddTargetUI/AddPendingUI/RemoveEntry/UnbindAll）、提权检测、Quit、Main
- `MainForm.UI.cs` — BuildUI、辅助方法（AddGroup/AddCheck/AddButton/CreateSelectButton/CreateRemoveButton）、深色主题、UI 同步、选中目标、托盘、操作回调（ToggleEnabled/PickWindow/RefindAllWindows/SetBgAlpha）、WinEvent

**注意事项**：`partial class MainForm` 仅需在一个文件中声明基类 `: Form`，其它 partial 文件可省略。

### 5. 界面滚动条深色模式

调用 `Native.SetWindowTheme(hwnd, "DarkMode_Explorer", null)` 使 `FlowLayoutPanel` 的滚动条跟随系统暗色模式。

### 6. 分段去抖（Slider Debounce）

**问题**：拖动透明度滑块时 `ValueChanged` 频繁触发，每次调用 `_settings.Save()` 写盘（JSON 序列化 + 原子文件写入）。

**解决**：引入 200ms `Timer`，仅在停止拖动 200ms 后执行一次 `Save()`。

**涉及文件**：
- `Program.cs:147-148` — `_saveDebounceTimer` 初始化
- `MainForm.UI.cs:367-368` — `SetBgAlpha` 中的 `Stop()`+`Start()`

### 7. 提权检测

目标窗口以管理员运行时，非提权的本程序无法修改其 `WS_EX_LAYERED` 样式（`SetWindowLongPtr` 静默失败）。`TryBindTarget` 中检测目标是否提权，若是则弹出托盘提示。

**涉及文件**：
- `Program.cs:288-340` — `IsTargetElevated` / `IsCurrentProcessElevated`
- `Program.cs:375-385` — 绑定时的提权检查

### 8. WinEvent 跨线程安全

`WinEventProcCallback` 运行在系统线程池，不能直接访问 WinForms 控件。所有 UI 操作通过 `BeginInvoke` 封送到 UI 线程。

**涉及文件**：
- `MainForm.UI.cs:466-491` — WinEvent 回调

### 9. 窗口拾取器（WindowPickerForm）

全屏半透明表单 + 十字光标 + 反转边框高亮。左键选中、右键/Esc 取消。

- `EnumWindows` 按 Z 序从顶到底枚举
- 30ms 鼠标移动节流
- `PatBlt(DSTINVERT)` 异或方式绘制边框（两次调用自动消除）

### 10. 启动恢复透明度

程序启动时遍历配置的目标窗口，无条件恢复透明度到 255（`SetTargetAlpha(h, 255)`），以清理上次强制杀进程可能残留的透明效果。

**涉及文件**：
- `Program.cs:192-200` — `RestoreAllTargets()`

---

## 发布流程

```bash
# 1. 版本号写入 .csproj
# 2. 构建 + 单文件发布
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

# 3. 标签 + Release
git tag v5.1.0
git push origin v5.1.0
gh release create v5.1.0 bin/Release/net6.0-windows/win-x64/publish/WindowTinter.exe --notes "xxx"
```

---
