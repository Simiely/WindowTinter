# WindowTinter 主逻辑重构计划（v5.5.3 → v5.6.0）

> ✅ **状态：A~E 五阶段全部完成（v5.6.0），构建 0 警告 0 错误，冒烟通过。**
> 目的：一次理顺主逻辑，不再点对点打补丁。
> 原则：状态收敛、入口统一、事件驱动、效果应用差异计算。

---

## 一、审核结论：主逻辑当前的 10 个结构性问题

### P0 · 结构性问题（"反复叠加修改"的根源）

| # | 问题 | 现状 |
|---|------|------|
| 1 | **目标生命周期无显式状态机** | 同一目标有三份状态拷贝：`Settings.Targets`（磁盘）、`_entries`（活跃）、`_pendingPanels`（待激活 UI）。"待激活"没有数据模型，靠"配置里有、_entries 里没有"隐式推导 |
| 2 | **清理逻辑重复 6 处** | `SetTargetAlpha(255) + SetTargetTopmost(false) + Plate.HidePlate() + Dispose` 在 autoBindTimer、RemoveEntry、UnbindAll、Quit、ToggleEnabled(false)、RestoreAllTargets 重复，没有统一 `ReleaseTarget` |
| 3 | **窗口销毁非事件驱动** | WinEvent `EVENT_OBJECT_DESTROY` 只触发 `RefreshNow`，条目仍留在 `_entries` 等 3s 定时器扫描才移除。窗口已死但 UI 仍显示"监控中"，存在最长 3s 的状态不一致窗口 |

### P1 · 正确性 bug（本次审计新发现）

| # | 问题 | 影响 |
|---|------|------|
| 4 | `FindByTitleAndProcess` 在 **title 为空时直接返回 Zero** | 无标题窗口（很多工具窗）一旦重开，永远绑不回来，一直"待激活" |
| 5 | `TargetInfo.Equals/GetHashCode` 不含 `WindowClass` | 同进程+同标题的多个不同窗口会被判等（"此窗口已添加"误判 / 条目冲突）；重绑定匹配链有"面积最大"启发式，同进程多窗口可能绑错 |
| 6 | `SetTargetAlpha` 粗暴增删 `WS_EX_LAYERED` | 对**原生 layered / 带颜色键**的窗口（浏览器等）会破坏其原有合成状态 → "设了透明度没反应 / 出现色块"的一类根因 |
| 7 | `CreateEntry` 用闭包捕获 `_lastBgAlpha`，OnUpdate 无节流 | 拖动窗口时 `LOCATIONCHANGE` 高频触发全量刷新；透明度逻辑与闭包状态耦合，难测试 |

### P2 · 一致性与可维护性

| # | 问题 | 现状 |
|---|------|------|
| 8 | 设置变更靠"全量 ApplyMaskNow"传播 | 滑块/开关变化 → `foreach` 全量刷新，无差异计算，性能与逻辑都不干净 |
| 9 | UI 面板构建重复 | `AddTargetUI` / `AddPendingUI` 双份代码 + `_selectButtons` 字典 + `SetChildIndex` 排序 hack，无统一 `RebuildTargetList` |
| 10 | 五个定时器/事件源职责重叠 | Tracker 250ms 轮询、autoBind 3s、onShown 100ms、saveDebounce 200ms、WinEvent，刷新职责边界模糊 |

---

## 二、目标架构

```
目标窗口身份 = 进程 + 窗口类名（主键）；标题仅作显示与辅助匹配

状态机：
  待激活 Pending ──BindTarget(绑定成功)──▶ 监控中 Bound
  监控中 Bound ──ReleaseTarget(销毁/隐藏)──▶ 待激活 Pending
  待激活/监控中 ──RemoveTarget(删除)──▶ 移除（释放资源+还原效果）

统一入口（仅此三处可改状态）：
  BindTarget(info)        // 查找 → 建 TargetEntry → 挂 UI → 生效
  ReleaseTarget(entry, reason)  // 还原效果 → 释放资源 → 转 Pending → 更新 UI
  RemoveTarget(info)      // 从设置移除 + Release + 更新 UI

驱动：WinEvent 即时迁移状态；3s 定时器只做"兜底扫描：尝试绑定未绑定项"
效果：ApplyEffect(entry) 做差异计算（alpha/corner/plate 位置），有变化才落系统调用
```

---

## 三、分阶段修改计划（每阶段独立构建验证）

### 阶段 A：身份与查找模型
**文件**：`Settings.cs`、`TargetTracker.cs`、`Native.cs`
- `TargetInfo.Equals/GetHashCode` 纳入 `WindowClass`（仅当非空时参与；旧配置 `WindowClass=""` 时身份=进程+标题，行为与现版本完全一致，向后兼容）；
- 重写 `FindByTitleAndProcess`：
  - title 为空时**不再直接返回 Zero**，跳过标题条件，走"进程+类名 → 仅进程（唯一窗口 / 面积最大）"；
  - 优先级链：进程+类名精确 → 标题精确 → 标题包含 → 仅进程唯一 → 面积最大；
  - 返回 `(handle, matchKind)`，matchKind 写日志，方便排查"绑到了哪个窗口、依据什么"。
- 验收：无标题窗口可重绑；同进程双窗口各自绑定不串。

### 阶段 B：生命周期状态机（核心重构）
**文件**：`Program.cs`（主要）、`MainForm.UI.cs`
- 新增统一入口 `BindTarget` / `ReleaseTarget` / `RemoveTarget`；`ReleaseTarget` 内部一次完成：还原 alpha → 还原 topmost → HidePlate → Dispose → 移除 UI → `UpdateUI`；
- `autoBindTimer` 简化为纯兜底：遍历 `Settings.Targets` 中无 entry 的 → `BindTarget`，删除内联清理逻辑；
- WinEvent 改造：`EVENT_OBJECT_DESTROY` 直接匹配 entry → `ReleaseTarget(entry, Destroyed)`（即时迁移，消除 3s 状态窗口）；`LOCATIONCHANGE/HIDE/SHOW` → `RefreshNow`；
- `OnLoad` / `OnShown` / `ToggleEnabled` / `RefindAllWindows` / `Quit` 全部改为调用统一入口；
- 删除 `UnbindAll` 的手写清理循环（并入 `ReleaseTarget` 的批量版本）。
- 验收：关掉目标窗口 <1s 内变"待激活"；删除/暂停/退出不再有重复清理代码。

### 阶段 C：效果应用解耦
**文件**：`Program.cs`、`TargetTracker.cs`、`BlackPlate.cs`
- `TargetEntry` 增加 `LastAlpha`（替代闭包 `_lastBgAlpha`）；新增 `ApplyEffect(entry)`：计算目标 alpha/corner/plateVisible → 与当前状态比较 → 有变化才 `SetTargetAlpha / SetPlateCorner / AlignBehind / HidePlate`；
- `SetTargetAlpha` 兼容重构：记录窗口**原生**是否 layered；alpha≥255 时——原本无 layered 才移除样式；原本有 layered 则设 `LWA_ALPHA=255` 保留，避免破坏浏览器等原生合成窗口；
- 保留 BlackPlate 已有的 DPI 换算 + 位置缓存；评估 `LOCATIONCHANGE` 高频时是否需要 16ms 合并（阶段内实测决定）。
- 验收：浏览器开透明度/还原后无残留色块；拖动窗口不闪。

### 阶段 D：UI 同步整理
**文件**：`MainForm.UI.cs`
- 新增 `RebuildTargetList()`：按状态（Bound/Pending）与配置顺序统一重建面板与 `_selectButtons`，删除 `AddTargetUI`/`AddPendingUI` 双份代码与 `SetChildIndex` hack；
- `SelectTarget` 行为保持：选中 + `BringTargetToTop` + `ApplyEffect`。
- 验收：增删目标/重开后列表顺序稳定、无重复面板。

### 阶段 E：验证与发布
- 构建 0 警告 0 错误；冒烟测试（启动/驻留/托盘）；
- 版本号 → **5.6.0**，产物发布到 `dist/WindowTinter-v5.6.0-win-x64/`；
- 更新 `FIX_REPORT.md`、`CHANGELOG.md`、`README.md` 相关章节。

---

## 四、兼容性与风险

| 项 | 说明 |
|----|------|
| 旧配置兼容 | `WindowClass` 缺省空串 → 身份降级为 进程+标题，与现版一致；反序列化无感 |
| 行为变化 | 窗口销毁→待激活由 3s 变为事件即时（更快，非破坏） |
| SetTargetAlpha 改动 | 恢复后浏览器等窗口可能保留 layered（视觉无差别），需真机确认还原无残留 |
| 范围控制 | 本重构不新增功能；只理顺主逻辑 + 修 P1 的 4 个正确性 bug（#4/#5/#6/#7） |

---

## 五、真机测试清单（重构后）

1. 添加一个**无标题**工具窗口 → 重启程序后能自动绑回；
2. 同进程两个不同标题的窗口（如两个 Chrome 窗口）→ 各自绑定不串、不误判"已添加"；
3. 浏览器设透明度 → 还原到 100% → 窗口无残留、无异常色块；
4. 关闭目标窗口 → 1 秒内（非 3 秒）列表变"待激活"；重开 → 自动回"监控中"；
5. 125%/150% 缩放屏：黑底贴合、圆角正常、拖动不闪；
6. 多个窗口重叠拾取 → 高亮跟随光标下正确窗口。
