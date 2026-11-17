# 历史归档（WinForms 时代与迁移计划）

> 这里的文档描述的是**过去的状态**，保留原因只有一个：里面有很多"当时为什么这么选"的推理，
> 换个项目做同类工具（窗口覆盖 / 蒙版 / 暗化 / 拾取器）时还能直接参考。
>
> **当前状态请看**：[../ARCHITECTURE.md](../ARCHITECTURE.md)（架构）、[../BUILD.md](../BUILD.md)（构建）、
> [../../DEVELOPMENT.md](../../DEVELOPMENT.md)（开发入口）。

## 时间线

| 阶段 | 版本 | 技术栈 | 记录 |
|---|---|---|---|
| 摸索期 | v1 → v3.9.0 | WinForms | [winforms-notes-v1-v3.9.md](winforms-notes-v1-v3.9.md) |
| 稳定期 | v5.5.2 | WinForms | [winforms-notes-v5.5.2.md](winforms-notes-v5.5.2.md) |
| 主逻辑重构 | v5.5.3 → v5.6.0 | WinForms | [refactor-plan-v5.6.md](refactor-plan-v5.6.md)、[fix-report-v5.6.md](fix-report-v5.6.md) |
| 审核与模块化 | proto-shadow | WinForms | [review-plan-proto-shadow.md](review-plan-proto-shadow.md) |
| 容器化尝试 | v8 | WinForms | [ui-container-plan.md](ui-container-plan.md) |
| **WPF 重写** | v11 → 7.x | **WPF** | [wpf-migration-plan.md](wpf-migration-plan.md) |

## 各篇一句话

- **winforms-notes-v1-v3.9.md**（618 行）：渲染方案选型（为什么用 `UpdateLayeredWindow` 而不是
  Magnifier API / WGC）、Z 序策略、WinEvent 死循环、`.exe` 后缀兼容等最早的一批坑。**选型推理仍然有效。**
- **winforms-notes-v5.5.2.md**（169 行）：v5.5.2 的项目概况 + 13 条问题纪要
  （DWM 阴影导致 `GetWindowRect` 偏大、`SetWindowRgn` 圆角裁剪的两条失败尝试等）。
- **refactor-plan-v5.6.md**：把主逻辑一次理顺的 A~E 五阶段计划（已完成）。
- **fix-report-v5.6.md**：v5.5.2 → v5.6.0 的测试与修复报告（构建产物、验证项）。
- **review-plan-proto-shadow.md**：proto-shadow 基线上的代码质量审核 + 模块化改造计划。
- **ui-container-plan.md**：把绝对定位 UI 改成 `TableLayoutPanel` 流式布局的计划——
  **这是 WPF 迁移之前的最后一次 WinForms 尝试**，最终因为无法 1:1 还原设计稿而被替代。
- **wpf-migration-plan.md**：从 WinForms 换到 WPF 的决策与"功能逻辑 0 重写"铁律。
  当前 `WindowTinter.Wpf/` 就是按这份计划迁出来的。

## 维护约定

- **不要更新这里的文档**。它们记录的是当时的事实，改了就不叫历史了。
- 新的决定写进 [../DECISIONS.md](../DECISIONS.md)，新的架构写进 [../ARCHITECTURE.md](../ARCHITECTURE.md)。
- 需要引用这里的结论时，**用链接，不要复制段落**。
