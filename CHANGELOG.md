# CHANGELOG.md · 版本记录

> 按版本从新到旧。完整问题记录见 [DEV.md](DEV.md)。

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
