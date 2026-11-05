# CHANGELOG.md · 版本记录

> 按版本从新到旧。完整问题记录见 [DEV.md](DEV.md)。

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
