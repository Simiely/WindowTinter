# WindowTinter 测试与修复报告

- 仓库：`github.com/Simiely/WindowTinter`（v5.5.2 → 重构版 **v5.6.0**）
- 日期：2026-08-04
- 环境：Windows + 隔离 .NET 6 SDK（6.0.428）
- 构建产物：`dist\WindowTinter-v5.6.0-win-x64\`（自包含、免装运行时）
- 重构计划与验收：见 `REFACTOR_PLAN.md`（A~E 五阶段，全部完成）

---

## 〇、v5.6.0 主逻辑重构总结（本次）

目标：一次理顺主逻辑，取代点对点打补丁。五大改动：

| 阶段 | 改动 | 解决 |
|------|------|------|
| A 身份模型 | `TargetInfo` 身份纳入窗口类名（旧配置兼容）；匹配链：类名收窄候选池→标题精确→标题包含→唯一窗口→面积最大；**title 为空不再直接返回 Zero** | 无标题窗口可重绑；同进程多窗口不绑串 |
| B 状态机 | 目标生命周期收敛为「待激活↔监控中」显式状态机，统一 `BindTarget / ReleaseTarget / RemoveTarget` 三个入口；WinEvent 销毁**事件即时迁移**（不再等 3s 定时器）；清理逻辑从 6 处收敛到 1 处 | 状态不再三处拷贝；删除/暂停/退出无重复清理代码 |
| C 效果解耦 | `ApplyEntryEffect` 显式差异计算（entry 持 `LastAlpha`/`OriginallyLayered`）；`SetTargetAlpha` 兼容**原生分层窗口**：恢复时不粗暴移除其 `WS_EX_LAYERED` | 浏览器还原后无异常色块；拖动不高频刷新 |
| D UI 整理 | `RebuildTargetList` + `CreateTargetPanel` 统一重建列表，删除 `AddTargetUI/AddPendingUI/RemovePendingUI` 三份代码与 `SetChildIndex` hack | 增删/重开列表顺序稳定 |
| E 发布 | 版本号 5.6.0；构建 0 警告 0 错误；冒烟通过；产物 `dist/WindowTinter-v5.6.0-win-x64/` | — |

---

## 一、测试结果

| 项 | 结果 |
|---|---|
| 克隆 | ✅ 直连 GitHub 不通，走 `ghfast.top` 加速镜像拉取成功 |
| 构建（修复前） | ✅ `dotnet build -c Release`，0 警告 0 错误 |
| 冒烟测试（修复前） | ✅ 进程启动，主窗口句柄正常，6s 存活无崩溃 |
| 构建（修复后） | ✅ 0 警告 0 错误 |
| 冒烟测试（修复后） | ✅ 进程启动，主窗口句柄正常，6s 存活无崩溃 |

> 说明：本程序是 Windows 桌面 GUI（窗口染色），沙箱无真实桌面会话，只能验证「能编译 + 能启动不崩溃」。
> 完整交互（拾取准确度、黑底对齐、重绑定）需你在真机双击验证。

---

## 二、修复明细（共 7 个文件，+211 / -60 → 后追加置顶修复，+/- 见 git log）

### 1. 窗口拾取不准（WindowPickerForm.cs + Native.cs）
**根因**：`GetWindowAtCursor` 用 `EnumWindows` + 矩形包含自己实现命中测试，未走系统真正的命中测试，导致：
- 透明覆盖层（输入法/翻译/录屏驻留窗）、桌面 `WorkerW`/`Progman`、任务栏等顶层窗口会顶掉真实目标；
- 目标窗口的子窗口（多窗格应用）命中不了。

**修复**：
- 优先 `WindowFromPoint(pt)`（系统级命中测试，尊重 Z 序/子窗口/`WS_EX_TRANSPARENT` 语义），再 `GetAncestor(GA_ROOT)` 取根；
- 新增 `IsIgnoredWindow` 过滤：自身、不可见、最小化、`Progman`/`WorkerW`/`Shell_TrayWnd`/`Shell_SecondaryTrayWnd`、`WS_EX_TRANSPARENT` 透明窗；
- 命中不可交互窗口时退回 Z 序枚举兜底。
- Native.cs 新增 `WindowFromPoint` / `GetClassName` / `GetDpiForWindow` 三个 P/Invoke。

### 2. 自动重绑定失败（TargetTracker.cs + Settings.cs + MainForm.UI.cs + Program.cs）
**根因**：重绑定只认「标题 + 进程名」。浏览器/编辑器重开后标题变了 → 精确与"标题包含"都匹配不上 → 目标永远"待激活"。且 `TryBindTarget` 里的旧条目复用分支是死代码（`_autoBindTimer` 每 3s 已先把死条目 dispose 并放回待激活）。

**修复**：匹配策略按优先级降级：
1. 标题完全一致 + 进程（保持原精确行为）；
2. 标题包含关键词 + 进程；
3. **进程 + 窗口类名**（新增 `TargetInfo.WindowClass`，拾取时记录；旧配置缺省空串，完全兼容）——重开后标题彻底变了也能找回；
4. 仅进程兜底：该进程唯一可接受窗口直接绑；多个时优先标题含关键词者，否则取**面积最大**者（用户关注的主窗口通常最大）。
- 删除 `TryBindTarget` 不可达的 stale 分支，避免误导。

### 3. 黑底缩放屏错位 + 闪烁（BlackPlate.cs + Native.cs）
**根因**：
- `GetWindowRect`/`DwmGetWindowAttribute(EXTENDED_FRAME_BOUNDS)` 返回**物理像素**，而进程是 `PerMonitorV2`，`SetWindowPos` 与 `UpdateLayeredWindow` 按**逻辑像素**解释坐标。100% 缩放下相等看不出问题，125%/150% 缩放屏必偏移/尺寸不对；
- 每 250ms 兜底轮询 + WinEvent 都全量重绘，造成闪烁与 GDI 抖动。

**修复**：
- 用 `GetDpiForWindow` 做物理→逻辑换算（scale = 96/dpi，坐标与尺寸一起换算），老系统无 API 时兜底 scale=1 不缩放；
- 加位置缓存：目标/坐标/尺寸均未变时跳过 `SetWindowPos`+`UpdateLayeredWindow`，只在变化时重绘；`HidePlate` 后强制重建。

### 4. 选中目标窗口时未置顶，被上层窗口遮挡破坏效果（已修正为 v5.5.3 语义）
**初版实现（v5.5.2 修复轮次）**：给目标窗口加 `WS_EX_TOPMOST` 永久置顶 —— 测试后确认**不符合预期**：窗口间前后遮挡应完全遵循 Windows 默认逻辑，目标窗口不该霸占最前。

**最终方案（v5.5.3）**：
- 移除轮询/更新路径里的所有强制置顶调用，目标窗口**从不被永久置顶**；
- 仅"选中目标"（列表点击选中）或"添加目标"的那一刻，用 `SetWindowPos(HWND_TOP, SWP_NOACTIVATE)` 把目标**一次性带到 Z 序顶部**（不激活、不抢焦点），方便查看半透明/黑底效果；
- 之后前后遮挡回归 Windows 默认逻辑：用户点别的窗口，其它窗口可正常盖上来；
- 暂停/删除/解绑/退出/启动恢复时仍统一清除可能的历史置顶残留（`HWND_NOTOPMOST`）。

### 5. 其他
- `TargetInfo` 新增 `WindowClass`（`init` 属性，JSON 缺省为空，旧配置无损迁移）。

---

## 三、如何验证

1. 直接运行构建产物：`bin\Release\net6.0-windows\WindowTinter.exe`（需已装 .NET 6 Desktop Runtime）
   或自包含版：`publish\win-x64\WindowTinter.exe`（免装运行时）。
2. 重点复测三个场景：
   - 点「+ 添加窗口」跨过多个重叠窗口移动鼠标，确认高亮跟随的正是光标下的窗口；
   - 给浏览器/编辑器窗口开透明度+黑底，在 125%/150% 缩放下拖动窗口，确认黑底与窗口边缘贴合、圆角正常、不闪；
   - 关掉已绑定的目标窗口再重开，3 秒内应从「待激活」自动变回「监控中」。

## 四、注意事项 / 已知限制

- **提权**：目标窗口以管理员运行时，本程序未提权则透明度修改会被系统静默拒绝（程序会弹一次气泡提示），需"以管理员身份运行"本程序。
- **UWP/商店应用窗口**：`WS_EX_LAYERED` 方式对 DirectComposition 窗口可能无效（平台限制，非代码 bug）。
- **黑底 DPI 换算**：按标准 API 语义实现，建议在缩放屏真机确认；100% 屏不受影响。
