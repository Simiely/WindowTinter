# DEV.md — 开发笔记与关键问题

> 记录本项目开发过程中遇到的核心技术决策、踩坑和最终方案。
> 以后做类似「窗口覆盖 / 蒙版 / 暗化」需求时直接参考。

---

## 1. 蒙版渲染方案选择

### 最终方案：UpdateLayeredWindow + WS_EX_TRANSPARENT

```
MaskOverlay（Form）
  CreateParams: WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST
  UpdateLayeredWindow(Handle, hdcScreen, ..., ULW_ALPHA)
  → 纯黑位图以指定 alpha 交给 DWM 合成
  → WS_EX_TRANSPARENT 保证鼠标穿透（MSDN 官方文档确认）
```

**关键点**：
- `SetLayeredWindowAttributes` 和 `UpdateLayeredWindow` 互斥，不能混用
- 位图只在尺寸变化时重建，拖滑块时复用 → 零帧分配
- `TopMost = true` 在构造器里设，`CreateParams` 加 `WS_EX_TOPMOST`

### 失败方案：Magnifier API（DimLens）

尝试用 `MagInitialize` + `MagSetColorEffect` 做暗化——**不可行**：

| 问题 | 说明 |
|---|---|
| Magnifier 是 TOPMOST 真窗口 | 物理遮挡所有窗口，无法穿透 |
| 即使加 WS_EX_TRANSPARENT | 仍视觉遮挡上层窗口 |
| 捕获屏幕像素而非窗口 | 上层窗口盖住目标时捕获错误内容 |
| 颜色矩阵暗化 | 效果不如纯位图 alpha 可控 |

**教训**：Magnifier 适用于「全屏放大/反色」，不适合「单窗口暗化」。PowerToys Always On Top 也是用 WS_EX_LAYERED 窗口，不用 Magnifier。

---

## 2. Z 序问题：「后台不盖上层窗口」

### 需求

用户希望目标在后台时，蒙版只压暗目标、不盖住上层窗口。

### 尝试的多种方案

| 方案 | 结果 |
|---|---|
| `SetWindowPos(targetHandle)` 插入目标上方 | 蒙版有 `WS_EX_TOPMOST`，锁在顶层段，`SetWindowPos` 拉不下来 |
| 去掉 WS_EX_TOPMOST + `HWND_NOTOPMOST` 降段 | 窗口段迁移不稳定，目标 TOPMOST 时失效 |
| Z 序遍历 + 遮挡百分比判断 | 25% 阈值武断，时对时错 |
| Magnifier + WS_EX_TRANSPARENT 宿主 | Magnifier 仍盖一切 |

### 最终决策：前景检查

```csharp
// ShouldShowMask
return Native.GetForegroundWindow() == targetHandle;
```

- 目标前台 → 显示蒙版
- 目标后台 → 隐藏

**简单、可靠、零 bug**。PowerToys、osu! overlay 等工具都用同类策略。

---

## 3. 首次渲染不出现（句柄创建时序）

### 现象

添加窗口后蒙版不显示，拖动目标窗口才出现。

### 根因

`AlignTo` 里首次访问 `Handle` 属性时才懒创建 Form 句柄。在 `CreateHandle` 完成前调用 `UpdateLayeredWindow`，窗口状态不完整。

### 修复

```csharp
public void AlignTo(Native.RECT r)
{
    if (!IsHandleCreated) CreateHandle(); // 强制提前创建
    Native.SetWindowPos(Handle, ...);
    RenderLayered(...);
}
```

---

## 4. 前景检查的时序陷阱（添加窗口后蒙版仍不显示）

### 现象

修复句柄创建后，添加窗口蒙版仍不显示。

### 根因

`PickWindow` 的流程：

```
PickWindow:
  Hide WindowTinter
  → 用户选目标 → OK
  → Show + BringToFront + Activate  // WindowTinter 变前台
  → TryBindTarget(info)
  → RefreshNow → OnUpdate
  → ShouldShowMask → GetForegroundWindow() != 目标（是 WindowTinter）
  → mask.Hide()  ← BUG!
```

用户拖动目标 → WinEvent 触发 → 此时目标为前台 → 蒙版才出现。

### 修复

绑定后立即 `ApplyMaskNow`，绕过前景检查直接显示。后续切窗口时前景检查正常工作。

```csharp
TryBindTarget(info);
var entry = _entries.FirstOrDefault(e => e.Info == info);
if (entry != null) ApplyMaskNow(entry);
```

---

## 5. 3 秒自动绑定定时器不工作

### 原因

与 #4 同根：定时器绑定后 `OnUpdate` 因前景检查隐藏蒙版。

### 修复

定时器里绑定后同样立即 `ApplyMaskNow`。

---

## 6. 多屏（副屏）适配

### 问题

Magnifier 方案中 `MagSetWindowSource` 源坐标写死为 `(0,0,w,h)`，导致副屏窗口无法捕获。

### 修复

源坐标改为目标窗口的屏幕坐标：

```csharp
// 错误
MagSetWindowSource(hwnd, new RECT(0, 0, w, h));

// 正确
MagSetWindowSource(hwnd, new RECT(r.Left, r.Top, r.Right, r.Bottom));
```

> 注意：此修复在 Magnifier 方案被废弃后不再需要，但 MaskOverlay 的 `UpdateLayeredWindow` 同样需要注意屏幕坐标：`pptDst` 参数是屏幕坐标，`SetWindowPos` 位置也是屏幕坐标。

---

## 7. 窗口关闭再打开的重新绑定

### 问题

目标程序关闭后，旧条目 handle 失效但未清理。新窗口启动后，3 秒定时器检查 `_entries.Any(e => e.Info == t)` 返回 true（旧条目还在），跳过绑定。

### 修复

1. 定时器 tick 先清理 `!IsWindow(TargetHandle)` 的旧条目
2. `TryBindTarget` 发现同 `Info` 的旧条目时复用（更新 handle）而非创建新的

```csharp
// 定时器清理
if (!Native.IsWindow(e.Tracker.TargetHandle) && e.Tracker.TargetHandle != IntPtr.Zero)
{
    e.Mask.Hide(); e.Tracker.Dispose(); e.Mask.Dispose();
    _entries.RemoveAt(i);
}

// TryBindTarget 复用旧条目
var stale = _entries.FirstOrDefault(e => e.Info == info && !Native.IsWindow(e.Tracker.TargetHandle));
if (stale != null) { stale.Tracker.TargetHandle = h; stale.Tracker.RefreshNow(); return; }
```

---

## 8. 代码架构：从复杂到精简

### 历程

| 阶段 | 状态 | 问题 |
|---|---|---|
| v1.x | 蒙版 + 反色 + 热键 + 单窗口 | 功能杂、代码散 |
| v2.0 | 删热键、多窗口、反色残废 | Magnifier 乱入 |
| v2.1 | 删反色、只保留蒙版 | `IsSignificantlyOccluded` 残留 |
| v2.2 | 前景检查 + 绑定后强制显示 | 干净稳定 |

### 教训

- 复杂方案（Magnifier、Z 序遍历、遮挡百分比）不如简单方案（前景检查）
- 搜索验证后再动手：PowerToys、MSDN 都有明确的最佳实践
- 不要同时维护多个模式——先做好一个

---

## 9. WS_EX_TRANSPARENT 与 WS_EX_LAYERED 的配合

来自 MSDN 官方文档：

> 如果分层窗口具有 WS_EX_TRANSPARENT 扩展样式，则忽略分层窗口的形状，
> 鼠标事件传递到下层窗口。WS_EX_TRANSPARENT 仅影响鼠标输入，不影响键盘焦点或窗口激活。

**这意味着**：`WS_EX_LAYERED | WS_EX_TRANSPARENT` 组合就是「视觉存在、输入穿透」的标准做法。

---

## 10. DPI 感知

当前设置：`Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)`。

蒙版使用屏幕坐标（`GetWindowRect` / `SetWindowPos`），在 PerMonitorV2 模式下正确。若目标窗口在不同 DPI 屏之间移动，需额外处理。

---

## 参考

- [MSDN: Layered Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows)
- [MSDN: WS_EX_TRANSPARENT](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [PowerToys Always On Top](https://github.com/microsoft/PowerToys/tree/main/src/modules/alwaysontop)
- [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)
