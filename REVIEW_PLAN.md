# WindowTinter 全面审核报告 + 模块化改造计划（proto-shadow 基线）

- 基线：`proto-shadow` 分支 = v5.6.0 重构版（9e02111）+ 垫底绑定强化（1ef65c5）
- 日期：2026-08-04
- 状态：功能已定稿（用户真机确认"支持很完美、无 bug"），本轮只做代码质量审核 + 计划，**不立即改代码**

---

## 一、主线逻辑审核结论（通畅 ✅）

目标窗口生命周期链路完整，无逻辑断裂：

```
OnLoad → RestoreAllTargets(清残留) → BuildTray → InstallWinEventHook → autoBindTimer(3s 兜底)
  → BindTarget: FindMatch(类名/标题/唯一/面积) → CreateEntry → RefreshNow → 入列 → UI
  → ApplyEntryEffect(alpha 差异 + BlackPlate.AlignBehind)
  → TargetTracker 500ms 轮询 + WinEvent(LOCATIONCHANGE/HIDE/SHOW/REORDER/DESTROY) 驱动
  → ReleaseTarget: 还原 alpha + HidePlate + Dispose → 待激活
  → RemoveTarget / UnbindAll / RefindAllWindows / Quit 全部收敛到 ReleaseTarget
```

**Z 序不变式**（黑底紧贴目标下方）由三道防线维护，已真机验证：
1. `EVENT_OBJECT_REORDER` 事件直驱重插（常量已修正：HIDE=0x8003 / SHOW=0x8002）
2. `TargetTracker` 500ms 轮询每次 tick 都触发 OnUpdate（不做变更守卫）
3. `BlackPlate.AlignBehind` 每次调用都 `SetWindowPos(Handle, targetHandle, ...)` 重插

**判定**：主线（生命周期状态机）逻辑清晰、方向正确，不需要动。

## 二、支线功能审核结论

| 功能 | 状态 | 说明 |
|------|------|------|
| 窗口拾取器 | ✅ 良好 | WindowFromPoint + GetAncestor(GA_ROOT)，过滤桌面/任务栏/透明层 |
| 透明度/黑底 | ✅ 良好 | originallyLayered 兼容原生分层窗口；DPI 物理→逻辑换算 |
| 圆角 | ✅ 良好 | SetWindowRgn + CreateRoundRectRgn，缓存 HRGN |
| 高 DPI | ✅ 良好 | PerMonitorV2 + AutoScaleMode.Dpi + BlackPlate 吞 WM_DPICHANGED |
| 托盘/开机自启 | ✅ 良好 | /startup 参数驻留托盘，IsWindowOpen 判断开合 |
| 配置持久化 | ✅ 良好 | JSON 原子写（tmp+Move），旧格式迁移（0-255/单窗口/.exe 后缀） |
| 提权提示 | ✅ 必要 | 垫底方案需改目标样式，管理员窗口需提示（贴膜方案已删，垫底保留） |

## 三、模块化评估（重点问题 ⚠️）

**结论：模块化不彻底——落后于贴膜 v5 已做过的拆分，两处职责混合严重。**

### Program.cs（617 行）混入 5 类职责
| 职责 | 行数 | 应归属 |
|------|------|--------|
| Main 入口 + 窗体状态 + 生命周期 | ~150 | Program.cs（保留） |
| **JumpTrackBar 控件** | 37 | **独立 JumpTrackBar.cs** |
| **TargetEntry 数据类** | 10 | **独立 TargetEntry.cs** |
| 绑定/释放/删除 + 效果应用（领域逻辑） | ~200 | **独立 MainForm.cs** |
| 提权检测（IsTargetElevated 等） | 50 | 独立工具或并入 MainForm.cs |
| UI 列表重建（RebuildTargetList/CreateTargetPanel） | 80 | 应归 MainForm.UI.cs |

### MainForm.UI.cs（658 行）混入系统事件
| 职责 | 行数 | 应归属 |
|------|------|--------|
| UI 构建/主题/同步/托盘/回调 | ~610 | MainForm.UI.cs（保留） |
| **WinEvent 钩子 + 回调** | 45 | **独立 MainForm.Events.cs** |

### 死代码 / 重复
| 问题 | 位置 | 说明 |
|------|------|------|
| `IsBound` 死字段 | Program.cs:226,445,488 | **只写不读**，删 |
| 状态文案 ×2 重复 | UpdateUI vs RefreshTrayMenu | 抽 `GetStatusText()` |
| 效果还原逻辑 ×2 重复 | ApplyEntryEffect 失效分支 vs ToggleEnabled 停用分支 | 统一走 `ApplyEntryEffect` |
| `RefindAllWindows` 非批量 | UnbindAll+逐个 BindTarget 各重建一次 UI | 最多 2N 次全量重建，改批量 |
| `BindTarget` 先激活后入列 | Program.cs:444 `RefreshNow()` 在 `Add` 前 | 改为先入列再激活（贴膜 v2 已修过，垫底版遗漏） |
| `HidePlate` 无 SWP_NOZORDER | BlackPlate.cs:126 | 防御性补上 |

## 四、分阶段修改计划（每阶段构建验证，零行为变更）

### 阶段 1：模块化拆分（纯搬移，零行为）
1. 抽 `TargetEntry.cs`（数据类，独立顶层 internal class）
2. 抽 `JumpTrackBar.cs`（滑块控件）
3. 抽 `MainForm.Events.cs`（窗体关闭拦截 + WinEvent 钩子）
4. 抽 `MainForm.cs`（领域逻辑：Bind/Release/Remove/UnbindAll/ApplyEntryEffect + 提权检测）
5. Program.cs 瘦身为入口 + 状态 + 生命周期（目标 ~200 行）
6. RebuildTargetList/CreateTargetPanel 移入 MainForm.UI.cs

### 阶段 2：批量事务化（行为优化，修 RefindAll 闪动）
1. `BindTarget` 增加 `refreshUI` 参数（默认 true）；**先入列再激活**
2. `ReleaseTarget` 已有 `updateUI` 参数
3. `UnbindAll / RefindAllWindows / OnLoad / autoBindTimer / ToggleEnabled` 全部改批量：
   先全量释放（不逐次重建 UI）→ 全部重绑（不重建）→ 最后统一 `RebuildTargetList()` 一次
4. 删除 `IsBound` 死字段

### 阶段 3：逻辑收敛（去重）
1. 抽 `GetStatusText()`：UpdateUI + RefreshTrayMenu 共用
2. `ToggleEnabled` 停用分支统一走 `ApplyEntryEffect`（删重复还原逻辑）
3. `HidePlate` 补 `SWP_NOZORDER`

### 阶段 4：验证与发布
1. `dotnet build -c Release` → 0 警告 0 错误
2. 端到端回归（tools/shot + click + testtarget + move）：
   - 启动绑定 → 截图
   - 移动目标 → 黑底跟随 → 截图
   - 重新查找 → 黑底严丝合缝 → 截图
   - 暂停/恢复 → 效果正确
3. 发布 `dist/proto-shadow-v3`（单文件模式），git 提交

## 五、预期效果

- Program.cs 617 → ~200 行；MainForm.UI.cs 658 → ~610 行；新增 MainForm.cs ~200 行
- "出问题进对应文件"：绑定/效果 → MainForm.cs；界面/托盘 → MainForm.UI.cs；
  系统事件 → MainForm.Events.cs；数据 → TargetEntry.cs；控件 → JumpTrackBar.cs
- 重新查找/启停不再闪动（一次重建）；无死代码；无重复逻辑
