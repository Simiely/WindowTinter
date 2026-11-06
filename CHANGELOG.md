# CHANGELOG.md · 版本记录

> 按版本从新到旧。完整问题记录见 [DEV.md](DEV.md)。
> 版本号说明：WPF 重写阶段内部曾以「v11」指代；自 7.2.0 起统一使用 csproj 里的语义化版本。

## v7.2.0 — 2026-11-06（首个 WPF 正式版本）

### 本轮新增 / 修复（2026-11-05 ~ 11-06）

- **单卡参数独立于全局默认**：每个目标带「已自定义」标记，只有改动过的维度才脱离全局；旧配置的 `GlobalTransparency` / `GlobalCornerRadius` 开关无损迁移，不把全局值硬拷贝成单卡值
- **单卡启用开关**：`TargetInfo.Enabled` 分层于全局总闸——全局暂停时，各卡自己的启用状态保持不变
- **每行卡片数可持久化**（1 / 2）；选中卡在双列布局中占满整行；副行改单行省略，双列窄卡不再横向溢出
- **卡片折叠**：整卡点击展开、chevron 单独折叠（不把目标带到前台、不滚动列表）、`Esc` 收起；滑块拖动中按 `Esc` 只结束拖动、不收起卡片
- **拾取器取消**：`Esc` / 鼠标右键 / HUD 取消按钮。取消不写配置、不绑定、不触碰目标窗口状态；`Esc` 在隧道阶段吞掉，不泄漏给后台窗口
- **底栏「全局默认」折叠面板**；撤掉底栏「解除绑定」入口；刷新按钮改为图标 + 文字（只有图标随刷新旋转）
- **窗口圆角收口**：根 Border 画外轮廓与 1px 描边、根内容按 `RectangleGeometry` 内圆角裁切，消除四角断线与双弧；最大化切直角并取消裁切
- **圆角参数定稿 23 / 14 / 13 / 0**（窗口外轮廓 / 根边框描边弧 / 内容裁切 / 裁切内缩），升级为正式配置字段 `Corner*`；旧调参版的 `DebugCorner` 段启动时自动迁移并清除
- **圆角调试快捷键保留**：`Ctrl+Alt+方向键`（`Shift` 切到第二组），±1px 即时生效；底栏读数只在调过之后显示，正常使用不可见
- 版本号升至 7.2.0；构建 0 警告 0 错误

### 自 WPF 重写以来累计（节选）

- 无边框窗 + `WindowChrome` + 自绘窗控（右上 46×32，hover `#3A3A3A` / 关闭 `#C42B1C`）
- 深色 token 重构（`Themes/Dark.xaml`）；卡片 / 拾取器 / 设置面板 / 托盘菜单统一深色
- 托盘常驻（`Hardcodet.NotifyIcon.Wpf` + 自绘深色 WPF 菜单）；关闭主窗口 = 收进托盘
- 黑底板改为纯 Win32 分层窗口（`CreateWindowExW` + `UpdateLayeredWindow` + `SetWindowRgn`），物理像素直控，吞 `WM_DPICHANGED`
- 最小化 / 还原：`MINIMIZESTART` 立即隐藏底板 + 250ms 抑制窗口 + 500ms 轮询兜底；`DWMWA_CLOAKED` 时隐藏底板，避免切换虚拟桌面后留下黑块
- 释放还原：绑定前记录原始 `ExStyle` / `WS_EX_LAYERED` / alpha / topmost，只回滚本程序改动过的部分
- 无边框窗最大化按显示器工作区钳制（`WM_GETMINMAXINFO`），Win10 下不再压住任务栏
- 提权探测：目标以管理员身份运行而本程序不是时，提示「以管理员身份运行」
- 单实例（命名 Mutex `WindowTinter.Wpf`）；启动时按配置批量重绑目标
- 快照：后台线程抓图，绑定 / 窗口首次显示时自动抓图，失败显示占位

## v11 (WPF) — UI 全面重写 + 单文件发布（proto-shadow 分支）

- **迁移到 WPF**：为 1:1 还原 UI 设计稿（3 列扑克牌卡片 + 窗口快照 + teal 胶囊开关 + 圆角卡片），从 WinForms 迁移到 WPF（XAML 原生圆角/阴影/数据绑定/DPI 矢量）
- **MVVM 架构**：手写 `ObservableObject` / `RelayCommand`（无第三方框架）；`TargetViewModel` / `MainViewModel` 承载全部领域逻辑
- **托盘**：Hardcodet.NotifyIcon.Wpf 图标 + Win32 `TrackPopupMenu` 原生菜单；`SetPreferredAppMode(AllowDark)` 跟随系统深浅色；专用隐藏辅助窗口 owner，右键托盘不影响主窗口 Z 序
- **窗口拾取器 / 重命名对话框 / 关于对话框**：WPF 深色风格重写
- **黑底 DPI 修复**：BlackPlate 重写为纯 Win32 分层窗口（`CreateWindowExW` + `UpdateLayeredWindow` + 吞 `WM_DPICHANGED`），物理像素直控，DPI 切换不错位；进程 manifest 声明 `PerMonitorV2`
- **快照优化**：抓图移后台线程；绑定 / 窗口首次出现（EVENT_OBJECT_SHOW）自动抓图，也可手动刷新
- **UI 交互**：卡片整卡点击选中（全局统一时不可选）、主色调 #FF9292、深色标题栏（沉浸式 DWM）、全局字体调大 8%（真实参数，无渲染缩放）
- **单文件发布**：app.ico 嵌入程序集，`PublishSingleFile` 框架依赖发布 → 仅 1 个 `WindowTinter.exe`（~730KB）

## v5.6.0 — 主逻辑重构（状态机化）

- 目标生命周期收敛为显式状态机：`待激活 ↔ 监控中`，统一 `BindTarget / ReleaseTarget / RemoveTarget` 三个入口（原 6 处散落清理逻辑收敛为 1 处）
- WinEvent 事件即时驱动：窗口销毁 → 立即转"待激活"（不再等 3s 定时器兜底）
- 窗口身份 = 进程 + 类名（`TargetInfo.WindowClass` 参与判等，旧配置兼容）；匹配链：类名收窄 → 标题精确 → 标题包含 → 唯一窗口 → 面积最大
- **无标题窗口可自动重绑定**（此前 title 为空永远绑不回）
- 透明度恢复兼容原生分层窗口（浏览器等）：不再粗暴移除 `WS_EX_LAYERED`，还原无异常色块
- 效果应用差异计算（`ApplyEntryEffect`，entry 持 `LastAlpha/OriginallyLayered`），拖动不再高频刷新
- 列表 UI 统一 `RebuildTargetList` 重建，删除 `AddTargetUI/AddPendingUI/RemovePendingUI` 三份代码与 `SetChildIndex` hack
- 选中目标仅一次性带到前台（`HWND_TOP`），不永久置顶，Z 序遵循 Windows 默认逻辑
- 构建 0 警告 0 错误；冒烟通过；发布产物 `dist/WindowTinter-v5.6.0-win-x64/`

## v5.5.3 — 置顶语义修正 + 产物版本化

- 选中目标改为一次性带到前台（不抢焦点），移除轮询里的强制置顶；窗口前后遮挡完全遵循 Windows 默认逻辑
- 发布产物版本化到 `dist/WindowTinter-v<版本>-win-x64/`；`.gitignore` 增补 `dist/`

## v5.5.2 — 版本号统一

- 版本号升至 5.5.2（csproj + 代码 fallback，关于页与 Release 一致）

## v3.9.0 — 文档与仓库清理

- 版本号推进 3.9.0；README/DEV.md 文档更新；清理游离文件；确保仓库可直接 dotnet build

## v3.7.0 — 全局/单窗口透明度 + 稳定化

- 全局/单窗口透明度开关 + 目标选中按钮；动态标题子串匹配回退；GDI 位图缓存（HBITMAP 复用）；提权检测气泡；UI 遮挡修复

## v3.6.2 — UI 重构 + CI/CD

- UI 重构（按钮下移 + 状态栏整合）+ GitHub Actions CI/CD（tag 自动发布）+ GitHub Pages 落地页 + 多项稳定性修复

## v3.2.2 — 配置迁移 + 拾取器零闪烁

- 配置路径迁移（%AppData% → exe 同目录）+ 启动透明度恢复 + 后台点击激活 + 拾取器零闪烁方案（EnumWindows）+ 提示窗修复

## v3.0.1 — 全面审计修复

- BeginInvoke 闪白修复 + .exe 兼容迁移 + CloseReason 修复 + TargetInfo 值语义 + 设置即时持久化 + app.ico 绝对路径 + KeepTransparency + 超椭圆图标 + 深色滚动条

## v2.6 — 后台透明 + 死循环修复

- 后台透明 + 独立滑块 + WinEvent 死循环修复 + 全面审计

## v1.0 ~ v2.5 — 早期演进

- v1.0 蒙版+反色+热键+单窗口 → v1.1 删热键加多窗口 → v2.0 删反色（Magnifier 弯路）→ v2.3 恢复蒙版+前景检查 → v2.5 CreateHandle + 100ms Timer + ApplyMaskNow

## 备注

- 版本日志原在 DEV.md「架构演进」章节，本文件为提炼版
