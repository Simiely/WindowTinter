# DEV.md — 开发笔记

> 本项目从 v1 到 v2.6 的完整踩坑记录。以后做「窗口覆盖 / 蒙版 / 暗化」类工具时直接参考。

---

## 1. 渲染方案：UpdateLayeredWindow

### 为什么用 UpdateLayeredWindow 而不是 Magnifier API

| 方案 | 原理 | 问题 |
|---|---|---|
| **UpdateLayeredWindow** | 纯黑位图 × alpha，DWM 硬件合成 | 蒙版在 TOPMOST 段，物理盖住上层窗口 |
| **Magnifier API** | DWM 捕获屏幕像素 + 颜色矩阵 | 同样是 TOPMOST 真窗口，且挡交互；PowerToys 也不这么用 |
| **WGC + Direct2D** | 只抓目标窗口帧，理论上不盖上层 | 需要 Win10 1903+ 和大量 WinRT/D3D11 基础设施，SharpDX 依赖 |

**结论**：UpdateLayeredWindow 是最简单可靠的方案。Z 序问题通过"前台蒙版 + 后台透明"策略解决。

### 为什么前景和后台用不同策略

- **前台**：目标本身在最上面，不存在被遮挡问题 → 蒙版直盖
- **后台**：蒙版会盖住上层窗口 → 改为直接修改目标窗口透明度（`SetLayeredWindowAttributes`）

---

## 2. 后台透明：SetLayeredWindowAttributes

```csharp
// 后台：加 WS_EX_LAYERED，设 alpha
SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED);
SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
InvalidateRect(hwnd, IntPtr.Zero, true);  // 强制立即重绘

// 恢复：去掉 WS_EX_LAYERED
SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~WS_EX_LAYERED);
InvalidateRect(hwnd, IntPtr.Zero, true);
```

### 关键教训

- **不要用 `SetWindowPos(SWP_FRAMECHANGED)`** — 底层是同步 `SendMessage(WM_NCCALCSIZE)` 到目标进程，目标消息泵忙时 UI 线程卡死
- **用 `InvalidateRect` 替代** — 异步、不阻塞、效果等同
- **不要用 `ThreadPool` 做跨进程窗口操作** — 多个线程同时 `SetWindowLong` 同一个 hwnd 会造成竞态。`SetWindowLong` 和 `SetLayeredWindowAttributes` 本身不阻塞（直接改内核窗口结构），放 UI 线程同步调用即可
- **某些应用不支持 `WS_EX_LAYERED`** — 设置透明度可能无效果或异常，`try/catch` 兜底

---

## 3. WinEvent 死循环（最严重的 Bug）

### 现象

目标切后台 → 程序卡死，窗口拖不动。

### 根因链

```
SetTargetAlpha → SetWindowLong(改 WS_EX_LAYERED) + InvalidateRect
  → 触发 EVENT_OBJECT_LOCATIONCHANGE（目标窗口样式变了）
  → WinEventProcCallback → BeginInvoke(RefreshNow)
  → Refresh → OnUpdate → SetTargetAlpha → 🔄 死循环
```

### 修复过程

| 尝试 | 结果 |
|---|---|
| 去掉 Refresh 的 rect 变更守卫 | 导致无条件触发 OnUpdate → 死循环 |
| 加 `_lastBgAlpha` 守卫 | 守卫本身有 bug：`!_lastFg \|\| _lastBgAlpha != targetAlpha` → `!_lastFg` 在后台时永远 true，守卫形同虚设 |
| 修复守卫：仅用 `_lastBgAlpha != targetAlpha` | 正确——前景切后台时 alpha 从 255 变到新值触发一次，之后值不变跳过 |

### 最终守卫策略

```
Refresh()       → rect/visible 不变则不触发 OnUpdate（阻断位置变化引起的回环）
RefreshForeground() → 无视守卫，强制触发 OnUpdate（前台切换必须更新）
OnUpdate 内部   → _lastBgAlpha 与目标值比较，不变则不调 SetTargetAlpha（阻断重复修改引起的回环）
```

### 其他回环阻断措施

- **WinEvent 按事件类型分派**：`FOREGROUND` → `RefreshForeground`，`LOCATIONCHANGE` → `RefreshNow`（有守卫），互不干扰
- **去掉高频 `ZORDERCHANGES` 事件监听**：每次系统 Z 序变化都触发会堆积 BeginInvoke
- **Hook 范围精确到 `0x0003-0x800B`**：之前误写成 `0x8001`，导致 `SHOW/HIDE/LOCATIONCHANGE` 都不在范围内

---

## 4. 首次渲染不稳定

### 现象

启动时蒙版"有时候有、有时候没有"，拖动目标窗口才出现。

### 根因

```
Form.Handle 懒创建 → CreateHandle 里窗口状态不完整
  → BeginInvoke 推迟一帧 → OnShown 中 BeginInvoke 可能被同步执行（消息泵特殊启动状态）
  → 100ms 一次性 Timer → WM_TIMER 内核级调度，必然在消息泵正常后才触发 → 稳定
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

**教训**：`BeginInvoke` 在 Form 生命周期 `Shown` 阶段不可靠。`WM_TIMER` 是可靠替代。

---

## 5. 前景检查的时序陷阱

### 现象

添加窗口后蒙版不显示。

### 根因

`PickWindow` 流程中 WindowTinter 自己成了前台窗口，`ShouldShowMask` 返回 false。

### 修复

绑定后立即 `ApplyMaskNow` → `RefreshForeground` → 绕过 rect 守卫，强制触发 OnUpdate。后续切窗口时前台/后台逻辑正常运作。

---

## 6. 窗口关闭再打开的重绑定

### 现象

目标关闭再打开后，3 秒定时器不重新绑定。

### 根因

旧条目 handle 失效但未清理。`_entries.Any(e => e.Info == t)` 返回 true（旧条目还在），跳过绑定。

### 修复

1. 定时器 tick 先清理 `!IsWindow(TargetHandle)` 的旧条目
2. `TryBindTarget` 发现同 `Info` 的旧条目时更新 handle 复用

---

## 7. WINEVENT_SKIPOWNPROCESS 盲区

### 现象

点击 WindowTinter 自身时，目标不切换到透明状态。

### 根因

`WINEVENT_SKIPOWNPROCESS` 跳过了 WindowTinter 自己成为前台的事件，所以 `EVENT_SYSTEM_FOREGROUND` 回调不触发。

### 修复

监听 `Form.Activated` 事件，当 WindowTinter 被激活时调用 `RefreshForeground()`。加 `_inActivated` 重入锁防止 DWM 焦点回弹触发二次激活。

---

## 8. 跨线程访问 _entries

### 问题

`WinEventProcCallback` 在 WinEvent 线程执行 `_entries.FirstOrDefault()`，而 UI 线程可能同时修改列表。

### 修复

将 `FirstOrDefault` 移入 `BeginInvoke` 内，确保读取和操作都在 UI 线程：

```csharp
// 旧：WinEvent 线程读 _entries
var match = _entries.FirstOrDefault(e => e.Tracker.TargetHandle == hwnd);

// 新：在 BeginInvoke 内读
BeginInvoke(new Action(() =>
{
    var match = _entries.FirstOrDefault(e => e.Tracker.TargetHandle == targetHwnd);
    if (match != null) match.Tracker.RefreshNow();
}));
```

---

## 9. 架构演进

| 版本 | 变化 |
|---|---|
| v1.0 | 蒙版+反色+热键+单窗口 |
| v1.1 | 删热键、加多窗口 |
| v2.0 | 删反色，Magnifier 弯路 |
| v2.3 | 恢复蒙版、前景检查 |
| v2.5 | CreateHandle + 100ms Timer + ApplyMaskNow |
| v2.6 | 后台透明（SetLayeredWindowAttributes）+ 独立滑块 + 死循环修复 + 全面代码审计 |

---

## 10. 代码审计清单（通用参考）

做窗口操作类工具时必查：

- [ ] 跨进程 `SetWindowPos` 是否带 `SWP_FRAMECHANGED`（会阻塞 UI 线程）
- [ ] WinEvent 回调是否会使目标窗口状态变化再触发新事件（回环）
- [ ] 变更守卫是否用 `||` 连接了恒真条件（`!_lastFg` 类 bug）
- [ ] `SetWinEventHook` 范围是否覆盖了所有需要的事件类型
- [ ] WinEvent 线程是否有跨线程访问 UI 集合
- [ ] `WINEVENT_SKIPOWNPROCESS` 是否遗漏自身窗口切换场景
- [ ] `BeginInvoke` 在 `OnShown`/`OnLoad` 阶段是否可靠
- [ ] Form.Handle 是否在首次使用前显式 `CreateHandle()`
- [ ] WinForms `Timer` 是否为类字段（防 GC）
- [ ] `SetWindowLong` 改 `WS_EX_LAYERED` 后是否有 `InvalidateRect` 刷新
- [ ] 退出时是否恢复目标窗口透明度（`FormClosed` 比 `FormClosing` 更可靠）

---

## 11. 退出清理：FormClosed vs FormClosing

### 现象
关闭 WindowTinter 后，后台半透明的目标窗口未恢复全不透明。

### 根因
`Quit()` 方法包含所有清理逻辑（`SetTargetAlpha(255)` 等），但未被任何退出路径调用。托盘"退出"和点 × 都是设置 `_reallyQuit = true; Close()`，而 `OnFormClosing` 只检查最小化到托盘。

### 修复
将 `Quit()` 挂载到 `FormClosed` 事件——无论哪种退出方式，窗口关闭后必然触发。同时移除 `Quit()` 内冗余的 `Application.Exit()`（窗口已关闭，程序自然结束）。

---

## 12. 移除 Debug 日志

v2.6 正式发布版移除了 `DebugLog.cs` 及所有调用点、`Settings.DebugEnabled`、UI 中"查看日志"按钮。程序回归纯功能，零日志输出，减少约 40 行代码和一个源文件。

---

## 参考

- [MSDN: Layered Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows)
- [MSDN: WS_EX_TRANSPARENT](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [PowerToys Always On Top](https://github.com/microsoft/PowerToys/tree/main/src/modules/alwaysontop)
- [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)
