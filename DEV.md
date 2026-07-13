# DEV.md — 开发笔记

> 本项目从 v1 到 v2.5 的完整踩坑记录。以后做「窗口覆盖 / 蒙版 / 暗化」类工具时直接参考。

---

## 1. 渲染方案：UpdateLayeredWindow（唯一正确解）

### 选型

```
UpdateLayeredWindow + WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST
  → 纯黑位图 × alpha → DWM 硬件合成
  → WS_EX_TRANSPARENT 鼠标穿透（MSDN 官方文档确认）
  → TopMost = true（构造器）+ CreateParams 加 WS_EX_TOPMOST
```

### 为什么不用 Magnifier API

| 问题 | 说明 |
|---|---|
| 真窗口 TOPMOST | 物理遮挡所有窗口 |
| 即使加 WS_EX_TRANSPARENT | 仍视觉遮挡 |
| 屏幕抓取而非窗口抓取 | 上层窗口盖住目标时捕获错误内容 |
| PowerToys 也不用 | Always On Top 也是 WS_EX_LAYERED 窗口 |

**结论**：Magnifier 适用于"全屏放大"，不适用于"单窗口暗化"。

### 为什么不用 SetLayeredWindowAttributes

`SetLayeredWindowAttributes` 和 `UpdateLayeredWindow` 互斥——同一窗口只能用一种。UpdateLayeredWindow 支持每像素 alpha + 位图渲染，更灵活。

---

## 2. Z 序问题（核心难题）

### 需求

目标在后台时压暗，但不挡上层窗口。

### 尝试的 5 种方案

| # | 方案 | 结果 |
|---|---|---|
| 1 | `SetWindowPos(targetHandle)` 插入目标上方 | `WS_EX_TOPMOST` 锁段，拉不下来 |
| 2 | 去掉 WS_EX_TOPMOST + `HWND_NOTOPMOST` 降段 | 段迁移不稳定 |
| 3 | Z 序遍历 + 25% 遮挡阈值 | 武断，时对时错 |
| 4 | `SetWindowLong(GWL_EXSTYLE)` 动态切换 TOPMOST | 无效，窗口段已在创建时确定 |
| 5 | Magnifier API | 更差，还挡交互 |

### 最终方案：前景检查

```csharp
// ShouldShowMask
return Native.GetForegroundWindow() == targetHandle;
```

- 前台 → 显示
- 后台 → 隐藏

**简单、100% 确定、从不盖错。** 这是业界标准（参考：窗口叠加 overlay 文档、PowerToys 的做法）。

---

## 3. 首次渲染不稳定（最重要的问题）

### 现象

启动时蒙版"有时候有、有时候没有"，拖动目标窗口才出现。

### 根因链

```
问题 1: Form.Handle 懒创建
  AlignTo 里首次访问 Handle → 内部调 CreateHandle → 此时窗口状态不完整

修复 1: 显式 CreateHandle
  if (!IsHandleCreated) CreateHandle();

问题 2: UpdateLayeredWindow 需要消息泵
  CreateHandle 后立即调 UpdateLayeredWindow → 静默失败

试了 BeginInvoke 推迟一帧 → 不可靠：
  OnShown 中 BeginInvoke 可能被同步执行（消息泵特殊启动状态）
  → 推迟不够 → 仍然失败

最终方案: 100ms 一次性 Timer
  OnShown → start timer → 100ms → WM_TIMER → 消息泵必然已运转 → 成功
```

```csharp
private void OnShown(object _, EventArgs __)
{
    foreach (var e in _entries) e.Tracker.RefreshNow();
    var t = new Timer { Interval = 100 };
    t.Tick += (s, args) => { t.Stop(); t.Dispose(); foreach (var e in _entries) ApplyMaskNow(e); };
    t.Start();
}
```

**教训**：`BeginInvoke` 在 Form 生命周期的 `Shown` 阶段不可靠。`WM_TIMER` 是内核级调度，必然在消息泵正常循环后才触发。

---

## 4. 前景检查的时序陷阱

### 现象

添加窗口后蒙版不显示。

### 根因

`PickWindow` 流程：

```
Hide WindowTinter → 用户选目标 → Show + BringToFront + Activate
→ WindowTinter 变前台
→ TryBindTarget → RefreshNow → OnUpdate
→ ShouldShowMask: GetForegroundWindow() == WindowTinter ≠ 目标 → false
→ mask.Hide()
```

### 修复

绑定后立即 `ApplyMaskNow`——绕过 `ShouldShowMask`，直接渲染。

```csharp
TryBindTarget(info);
var entry = _entries.FirstOrDefault(e => e.Info == info);
if (entry != null) ApplyMaskNow(entry);
```

`ApplyMaskNow` 只检查窗口可见性和尺寸，不检查前景状态。后续切窗口时 `OnUpdate` 里的 `ShouldShowMask` 正常工作。

### 同样影响

- 3 秒自动绑定定时器 → 绑定后同样立即 `ApplyMaskNow`
- `OnShown` → `RefreshNow` + 100ms Timer → `ApplyMaskNow`

---

## 5. 窗口关闭再打开的重绑定

### 现象

目标关闭再打开后，3 秒定时器不重新绑定。

### 根因

旧条目 handle 失效但未清理。`_entries.Any(e => e.Info == t)` 返回 true（旧条目还在），跳过绑定。

### 修复

1. 定时器 tick 先清理 `!IsWindow(TargetHandle)` 的旧条目
2. `TryBindTarget` 发现同 `Info` 的旧条目时复用（更新 handle）

---

## 6. WS_EX_TOPMOST 动态切换实验（失败）

尝试去掉 `CreateParams` 里的 `WS_EX_TOPMOST`，用 `SetWindowLong(Handle, GWL_EXSTYLE, ...)` 在运行时动态添加/移除。

**结果**：无效。`SetWindowLong` 修改扩展样式后，顶层段状态不跟随变化。一旦窗口创建时不在 TOPMOST 段，后续 `SetWindowPos(HWND_TOPMOST)` 也无法将窗口推入顶层段（反之亦然——创建时在 TOPMOST 段，去掉样式后也降不下来）。

**教训**：`WS_EX_TOPMOST` 必须在窗口创建时通过 `CreateParams` 设定，运行时不可改变其 Z 序段归属。

---

## 7. 多屏适配

`UpdateLayeredWindow` 的 `pptDst` 参数是屏幕坐标（不是相对坐标）。`GetWindowRect` 返回的也是屏幕坐标。两者天然匹配，无需额外处理。DPI 使用 `PerMonitorV2` 模式，多屏不同缩放比正常工作。

---

## 8. Timer 生命周期

WinForms `Timer` 如果是局部变量，可能被 GC 回收——即使有 Tick 事件订阅也不构成强引用。必须声明为类字段。

```csharp
// 错误
var timer = new Timer { Interval = 3000 };

// 正确
private Timer _autoBindTimer;
_autoBindTimer = new Timer { Interval = 3000 };
```

---

## 9. 架构演进总结

| 版本 | 变化 | 经验 |
|---|---|---|
| v1.0 | 蒙版+反色+热键+单窗口 | 功能杂 |
| v1.1 | 删热键、加多窗口 | 反色残废 |
| v2.0 | 删反色（破） | Magnifier 弯路 |
| v2.3 | 恢复蒙版、前景检查 | 简单即对 |
| v2.5 | CreateHandle + 100ms Timer + ApplyMaskNow | 稳定 |

---

## 参考

- [MSDN: Layered Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows)
- [MSDN: WS_EX_TRANSPARENT](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [PowerToys Always On Top](https://github.com/microsoft/PowerToys/tree/main/src/modules/alwaysontop)
- [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)
