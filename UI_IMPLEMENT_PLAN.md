# WindowTinter 容器化重构实施计划

> 目标：把绝对定位 UI 改为 **TableLayoutPanel 流式容器布局**，一次解决「任意 DPI 不拥挤/不遮挡/无空白」+ 按设计稿（UI_REDESIGN.html）落地视觉。
> 原则：**分阶段、每阶段构建+冒烟验证、阶段间用户确认**，绝不再连环改。
> 基线：proto-shadow 分支（v8 轻量版），15 文件 2541 行。

---

## 设计稿要点（UI_REDESIGN.html 已定稿）

- 状态条（44px）：状态文字 + 启用主开关合一
- 目标窗口卡（绝对主角）：卡头（计数徽标 + ⟳刷新快照 + 添加 + 重新查找）+ **3 列扑克牌卡片网格**（窗口快照 + 状态灯 + 名称 + 常驻 ⟳ + ○✎×）+ 待激活占位 + 空状态引导
- 效果控制卡：垫黑底开关 + 全局统一压暗 + 压暗滑块 + 全局统一圆角 + 圆角滑块 + 底部一行提示
- 系统卡：开机自启 + 最小化托盘（一行横排）
- 操作栏：配置/关于（弱化靠左）+ 保存（强调靠右）+ 退出 + 页脚链接
- 窗口 **MinimumSize 440×640 可拉伸**，目标卡占剩余空间（Dock=Fill），拉高自动增行

---

## 阶段划分（每阶段：改 → 构建 0 警告 0 错误 → 冒烟 → 简报，用户确认后进下一阶段）

### 阶段 1：根骨架容器化（地基）
**目的**：容器 + 可拉伸 + DPI 正确的底座先成立，功能行为零变化。
- [ ] `Program.cs`：`ClientSize 470×695 → 440×640`（基准）、`FormBorderStyle.FixedDialog → Sizable`、`MinimumSize 440×640`、`MaximizeBox = true`
- [ ] `MainForm.UI.cs` `BuildUI` 重写：根 `TableLayoutPanel`（Dock=Fill，单列 6 行：状态条 Absolute 44 / 目标卡 **Percent 100%** / 效果卡 Absolute 190 / 系统卡 Absolute 44 / 操作栏 Absolute 46 / 页脚 Absolute 26）
- [ ] 5 个分区卡片 `Panel`（卡片底色 #26292E）+ 卡头行（#2C3036）
- [ ] 现有控件**按原相对坐标搬入各自分区卡**（内部布局不重排，阶段 2 做）
- [ ] `MainForm.Events.cs` `OnDpiChanged`：**删除 `DpiScaling.ScaleControlTree`**（容器自动重排），只保留 `_dpiScale` 更新 + `RebuildTargetList`
- [ ] 验证：高 DPI 初始打开正确、窗口可拉伸、功能（绑定/压暗/圆角）不回归

### 阶段 2：分区内部容器化
**目的**：每卡内部改用容器重排，消灭绝对定位。
- [ ] 状态条：TableLayoutPanel 一行（状态点 + 文字 + spacer + 启用开关）
- [ ] 效果卡：TableLayoutPanel 3 列（标签 | 滑块 Dock=Fill | 数值），垫黑底 + 两个全局开关归位
- [ ] 系统卡：FlowLayoutPanel 一行横排
- [ ] 操作栏：FlowLayoutPanel（次要靠左、保存强调靠右）
- [ ] 删除 `AddGroup/AddCheck/AddButton` 绝对定位辅助方法，全部改容器式
- [ ] 验证：各分区内容完整、任意宽度不溢出

### 阶段 3：目标卡片网格（设计稿核心）
**目的**：纵向列表 → 3 列扑克牌卡片网格。
- [ ] 新建 `TargetCard.cs`（UserControl）：快照区（阶段 4 接图）+ 状态灯 + 名称（两行省略）+ 常驻 ⟳ + 底部 ○✎×，hover/选中/待激活三态样式
- [ ] `_pnlTargets`：FlowLayoutPanel TopDown → **LeftToRight 3 列网格**（或 TableLayoutPanel 动态列）
- [ ] `CreateTargetPanel → CreateTargetCard`；`RebuildTargetList` 改网格 + 空状态引导（「点击 + 添加窗口 拾取要压暗的窗口」）
- [ ] 待激活：虚线卡片 + 「⏳ 窗口未启动」占位
- [ ] 验证：3 列网格正确换行、窗口拉高增行、增删/重命名/选中逻辑不回归

### 阶段 4：窗口快照模块
**目的**：每张卡显示目标窗口真实缩略图 + 三级刷新。
- [ ] 新建 `SnapshotService.cs`：`PrintWindow` 抓图（UWP/特殊窗口兜底 `DwmRegisterThumbnail`）+ 缩放缓存
- [ ] 刷新策略：3s 定时 + 目标移动/绑定瞬间即时 + 单卡 ⟳ 单张 + 卡头 ⟳ 全量
- [ ] `TargetCard` 快照区接图；待激活无图显示占位
- [ ] 验证：快照正确显示、刷新按钮生效、UWP 窗口兜底

### 阶段 5：视觉打磨
**目的**：对齐设计稿细节。
- [ ] 卡片圆角（Region/自绘）、选中蓝描边、待激活虚线、hover 高亮
- [ ] 启用主开关视觉（CheckBox 自绘为 switch 或保留复选）
- [ ] 计数徽标（监控/待激活）、ToolTip 收纳全部说明文字
- [ ] 深色主题 `ApplyDarkTheme/ThemeAll` 覆盖新控件
- [ ] 验证：与 UI_REDESIGN.html 视觉一致

### 阶段 6：验证与发布
- [ ] 构建 0 警告 0 错误；冒烟；端到端截图回归（绑定/移动/重新查找/快照）
- [ ] 清理死代码（`DpiScaling.cs` 若无引用删除；`_dpiScale` 用途收敛）
- [ ] 发布轻量版 `dist/WindowTinter-v9-framework-dependent/`
- [ ] git 提交（本地，不推送）

---

## 风险与对策
- **绝对定位残留**：阶段 1 允许临时坐标搬移，阶段 2 必须清零——用 `grep -c "Location = new Point"` 做验收门禁
- **DPI 双重缩放**：阶段 1 即删手动缩放，容器 AutoSize 接管；验收 = 跨 DPI 拖动无错位
- **快照性能**：PrintWindow 在 3s 定时下开销可控；缩略图按卡尺寸缩放缓存，不抓原图
- **视觉回归**：每阶段端到端截图对比，功能行为（绑定/压暗/圆角）永不改
