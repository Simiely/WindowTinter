# DEV.md — 开发笔记

> 本项目从 v1 到 v3.0.1 的完整踩坑记录。以后做「窗口覆盖 / 蒙版 / 暗化」类工具时直接参考。

---

## 1. 渲染方案：UpdateLayeredWindow

### 为什么用 UpdateLayeredWindow 而不是 Magnifier API

| 方案 | 原理 | 问题 |
|---|---|---|
| **UpdateLayeredWindow** | 纯黑位图 × alpha，DWM 硬件合成 | 蒙版在 TOPMOST 段，物理盖住上层窗口 |
| **Magnifier API** | DWM 捕获屏幕像素 + 颜色矩阵 | 同样是 TOPMOST 真窗口，且挡交互；PowerToys 也不这么用 |
| **WGC + Direct2D** | 只抓目标窗口帧，理论上不盖上层 | 需要 Win10 1903+ 和大量 WinRT/D3D11 基础设施，SharpDX 依赖 |

**结论**：UpdateLayeredWindow 是最简单可靠的方案。Z 序问题通过"前台蒙版 + 后台透明"策略解决。

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
- **不要用 `ThreadPool` 做跨进程窗口操作** — 多个线程同时 `SetWindowLong` 同一个 hwnd 会造成竞态
- **某些应用不支持 `WS_EX_LAYERED`** — 设置透明度可能无效果或异常，`try/catch` 兜底
- **hwnd 回收复用风险** — 窗口销毁后 hwnd 可能被 OS 回收给新窗口。`SetWindowLong(hwnd, ...)` 前需先 `IsWindow(hwnd)` 检查，不能仅靠入口守卫（两次 Win32 调用之间有 TOCTOU 窗口）

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

### 最终守卫策略

```
Refresh()       → rect/visible 不变则不触发 OnUpdate（阻断位置变化引起的回环）
RefreshForeground() → 无视守卫，强制触发 OnUpdate（前台切换必须更新）
OnUpdate 内部   → _lastBgAlpha 与目标值比较，不变则不调 SetTargetAlpha（阻断重复修改引起的回环）
```

---

## 4. 前后台切换闪白 — BeginInvoke 时序竞态

### 现象

目标从后台切前台（或反过来）时，偶尔闪一下白色。

### 根因

OnUpdate 中先做蒙版/透明操作，再做互补操作——中间有一帧缝隙：

```
切后台：HideMask() 先执行 → 窗口全亮 → SetTargetAlpha() 后执行 → 变暗  ← 闪白
切前台：SetTargetAlpha(255) 先执行 → 窗口全亮 → AlignTo() 后执行 → 盖蒙版  ← 闪白
```

### 修复

用 `BeginInvoke` 让两个操作分隔一帧，先做的效果先稳定下来：

```csharp
// 后台：先设透明度，延迟一帧再撤蒙版
SetTargetAlpha(hwnd, targetAlpha);
BeginInvoke(() => { if (IsWindow(hwnd) && GetForegroundWindow() != hwnd) HideMask(); });

// 前台：先盖蒙版，延迟一帧再恢复透明度
AlignTo(r);
BeginInvoke(() => { if (IsWindow(hwnd) && GetForegroundWindow() == hwnd) SetTargetAlpha(hwnd, 255); });
```

### 教训

BeginInvoke 闭包捕获的变量在执行时可能已过时。必须加守卫检查（*IsWindow + GetForegroundWindow*），否则前台 BeginInvoke 可能在目标已切后台时误恢复全不透明。

---

## 5. .exe 后缀兼容性 — 待激活失败

### 现象

从 v2.x 升级后，已配置的目标窗口打开后一直显示"待激活"，不自动绑定。

### 根因

v2.x 存储 ProcessName 带 `.exe` 后缀（"notepad.exe"），v3.x 改为不带后缀。`FindByTitleAndProcess` 在匹配时去掉了 `.exe` 剥离逻辑——导致 `Process.GetProcessById().ProcessName`（"notepad"）≠ 存储的 "notepad.exe"。

### 修复

1. `FindByTitleAndProcess` 恢复 `.exe` 后缀剥离（向后兼容旧配置）
2. `Settings.Load()` 新增迁移：首次加载时遍历全部 Targets，去掉 `.exe` 后缀并覆写配置文件

**教训**：修改持久化格式时必须加迁移代码，不然存量用户全挂。

---

## 6. FormClosing 阻止系统关机

### 现象

开启"最小化到托盘"后，Windows 关机/注销被阻止。

### 根因

`OnFormClosing` 中 `e.Cancel = true; Hide()` 无条件拦截了所有关闭请求，包括 `CloseReason.WindowsShutDown`。

### 修复

```csharp
if (!_reallyQuit && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
    { _settings.Save(); e.Cancel = true; Hide(); }
```

**教训**：`FormClosing` 处理中必须检查 `CloseReason`。用户关闭、系统关机、任务管理器结束是三种不同的语义。

---

## 7. settings.json 持久化一致性

### 问题

多个设置项（滑块、复选框）的值变更仅在关闭程序时才保存到 JSON。如果进程崩溃或被强制结束，所有未保存的改动丢失。

### 修复

每个值变更的回调中直接调用 `_settings.Save()`：背景透明度、蒙版透明度、启用开关、开机自启、最小化到托盘、保持透明度——全部即时持久化。

**教训**：常驻托盘程序无法依赖"正常退出→保存"路径，必须即时写盘。

---

## 8. TargetInfo 值语义重构

### 问题

整个代码库用 `==` 比较 `TargetInfo` 对象，依赖引用相等。反序列化后重新创建的对象，即使内容相同也无法匹配。

### 修复

`TargetInfo` 实现 `IEquatable<TargetInfo>`，重写 `Equals` / `GetHashCode` / `==` / `!=`，基于 `ProcessName` + `WindowTitle` 不区分大小写值语义比较。

---

## 9. app.ico 绝对路径 — 开机自启崩溃

### 现象

设置开机自启后，重启电脑程序启动即崩溃。

### 根因

Windows 从注册表 `Run` 键启动程序时，工作目录是 `C:\Users\<用户名>` 而非 exe 所在目录。`new Icon("app.ico")` 相对路径找不到文件 → `FileNotFoundException` → 构造函数崩。

### 修复

```csharp
var iconPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "app.ico");
_appIcon = new Icon(iconPath);
```

---

## 10. _appIcon Dispose 顺序 — 退出崩溃

### 现象

点"退出"时程序崩：`ObjectDisposedException: Cannot access a disposed object. Object name: 'Icon'.`

### 根因

`Quit()` 中先 `_appIcon.Dispose()`，然后 `_tray.Visible = false` 触发 `NotifyIcon.UpdateIcon` → 访问已销毁的 `Icon.Handle`。

### 修复

`_appIcon.Dispose()` 放在 `_tray.Visible = false` 之后、所有清理之后执行。

**教训**：Dispose 共享资源的顺序至关重要——依赖方必须先停止使用，然后才能释放。`NotifyIcon` 对 `Icon` 的引用一直持续到其自身清理完成。

---

## 11. 窗口拾取器闪烁修复

### 现象

点击"+ 添加窗口"后，全屏和任务栏出现快速闪烁。

### 根因

每次鼠标移动都执行 `ShowWindow(SW_HIDE)` + `ShowWindow(SW_SHOWNOACTIVATE)`，DWM 对每次显隐产生过渡动画，高频率形成闪烁。

### 修复

`WindowFromPoint` 的 MSDN 文档明确写：**忽略已禁用（WS_DISABLED）的窗口**。用 `Enabled = false` 临时禁用拾取窗：

```csharp
Enabled = false;
IntPtr h = Native.WindowFromPoint(pt);
Enabled = true;
```

**教训**：任何时候需要让 `WindowFromPoint` 忽略自己的窗口，用 `WS_DISABLED`（`Enabled = false`），不要用 `ShowWindow`。后者触发 DWM 合成管线，性能差且视觉干扰严重。

---

## 12. 首次渲染不稳定

### 现象

启动时蒙版"有时候有、有时候没有"，拖动目标窗口才出现。

### 根因

`BeginInvoke` 在 Form 生命周期 `Shown` 阶段不可靠——消息泵可能同步执行。`WM_TIMER` 是可靠替代：

```csharp
private void OnShown(object _, EventArgs __)
{
    foreach (var e in _entries) e.Tracker.RefreshNow();
    _onShownTimer = new Timer { Interval = 100 };
    _onShownTimer.Tick += (s, args) => { _onShownTimer.Stop(); _onShownTimer.Dispose(); ApplyMaskNow(); };
    _onShownTimer.Start();
}
```

---

## 13. 退出清理：FormClosed vs FormClosing

### 修复

将 `Quit()` 挂载到 `FormClosed`——无论哪种退出方式，窗口关闭后必然触发。`Quit` 中先隐藏托盘再释放资源，确保 `NotifyIcon` → `Icon` 依赖链安全释放。

---

## 14. 窗口关闭再打开的重绑定

### 根因

旧条目 handle 失效但未清理。`_entries.Any(e => e.Info == t)` 返回 true（旧条目还在），跳过绑定。

### 修复

1. 定时器 tick 先清理 `!IsWindow(TargetHandle)` 的旧条目
2. 将已销毁条目放回待激活列表，而非直接丢弃

---

## 15. 跨线程访问 _entries

### 修复

`WinEventProcCallback` 中将 `_entries.FirstOrDefault()` 移入 `BeginInvoke` 内。`ObjectDisposedException` + `InvalidOperationException` 双捕获防止窗口关闭时崩溃。

---

## 16. KeepTransparency 模式

### 设计

"窗口保持透明度"开关——开启后不叠加蒙版，前台后台统一走 `SetLayeredWindowAttributes`，用后台透明度滑块的设定值。对不想要蒙版、只需要恒定透明度的场景。

### OnUpdate 路径

```csharp
if (_settings.KeepTransparency)
{
    HideMask();
    SetTargetAlpha(hwnd, (byte)((100 - _settings.BackgroundAlpha) * 255 / 100));
    return;
}
// 正常蒙版模式 ...
```

---

## 17. 超椭圆图标

Python + Pillow + NumPy 处理，1920×1920 源图 → 超椭圆裁剪（n=4 大尺寸 / n=8 小尺寸） → 6 分辨率 ICO（16/32/48/64/128/256）。`SetWindowTheme("DarkMode_Explorer")` 应用深色滚动条。

---

## 18. WinEvent 异常分类捕获

```csharp
try { BeginInvoke(...); }
catch (ObjectDisposedException) { }  // 表单已关闭
catch (InvalidOperationException) { }  // BeginInvoke 在非 UI 线程被调用
```

只捕获明确预期的异常，其余异常不静默。

---

## 架构演进

| 版本 | 变化 |
|---|---|
| v1.0 | 蒙版+反色+热键+单窗口 |
| v1.1 | 删热键、加多窗口 |
| v2.0 | 删反色，Magnifier 弯路 |
| v2.3 | 恢复蒙版、前景检查 |
| v2.5 | CreateHandle + 100ms Timer + ApplyMaskNow |
| v2.6 | 后台透明 + 独立滑块 + 死循环修复 + 全面审计 |
| v3.0.1 | BeginInvoke 闪白修复 + .exe 兼容迁移 + CloseReason 修复 + TargetInfo 值语义 + 设置即时持久化 + app.ico 绝对路径 + KeepTransparency + 超椭圆图标 + 深色滚动条 + 8 轮审计 |

---

## 代码审计清单（通用参考）

做窗口操作类工具时必查：

- [ ] 跨进程 `SetWindowPos` 是否带 `SWP_FRAMECHANGED`（会阻塞 UI 线程）
- [ ] WinEvent 回调是否会使目标窗口状态变化再触发新事件（回环）
- [ ] 变更守卫是否用 `||` 连接了恒真条件
- [ ] BeginInvoke 闭包变量过期风险（加 IsWindow/GetForegroundWindow 守卫）
- [ ] `SetWinEventHook` 范围是否覆盖了所有需要的事件类型
- [ ] WinEvent 线程是否有跨线程访问 UI 集合
- [ ] `FormClosing` 是否检查 `CloseReason`
- [ ] 持久化格式变更是否有迁移代码
- [ ] `Dispose` 顺序：依赖方先停用，再释放共享资源
- [ ] 资源路径是否用绝对路径（开机自启工作目录 ≠ exe 目录）
- [ ] 设置变更是否即时持久化（托盘程序无法依赖"退出时保存"）
- [ ] `SetWindowLong` 改 `WS_EX_LAYERED` 后是否有 `InvalidateRect` 刷新
- [ ] hwnd 操作前是否有 TOCTOU 防护（`IsWindow` 在操作前再查一次）

---

## 参考

- [MSDN: Layered Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows)
- [MSDN: WS_EX_TRANSPARENT](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [MSDN: WindowFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-windowfrompoint)
- [PowerToys Always On Top](https://github.com/microsoft/PowerToys/tree/main/src/modules/alwaysontop)
