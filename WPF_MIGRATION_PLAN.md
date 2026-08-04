# WindowTinter → WPF 迁移计划（v11）

> 决策：WinForms 容器化 4 版仍无法 1:1 还原设计稿（圆角/阴影/hover/数据驱动列表非 WinForms 原生能力）。
> 换 WPF：XAML 声明式 UI + DataTemplate 卡片网格 + 原生圆角/阴影/动画，视觉 1:1。
> 铁律：**功能逻辑 0 重写**——纯逻辑（TargetTracker/SnapshotService/Settings/Elevation/Native/BindTarget/ReleaseTarget/ApplyEntryEffect）原样搬；
> 仅 2 个功能窗体（黑底 BlackPlate / 拾取器 WindowPickerForm）机械换容器（Form→Window，内部逻辑原样）；UI 编排层（BuildUI/Rebuild/UpdateUI）重写为 XAML+绑定。

## 项目结构（模块化）

```
WindowTinter.Wpf/                        ← 新 WPF 项目（旧 WinForms 保留，随时回退）
  WindowTinter.Wpf.csproj                net6.0-windows + UseWPF=true
  Program.cs                             入口（单实例）
  App.xaml / App.xaml.cs                 资源合并 + 启动
  Themes/Dark.xaml                       深色主题资源（颜色/圆角/按钮/开关/滑块模板）
  Views/MainWindow.xaml(.cs)             主窗口：6 分区布局 + 目标卡片网格
  Views/BlackPlateWindow.xaml(.cs)       黑底垫底窗口（透明分层，逻辑原样搬）
  Views/WindowPickerWindow.xaml(.cs)     窗口拾取器（逻辑原样搬）
  Views/RenameDialog.xaml(.cs)           重命名对话框
  ViewModels/ObservableObject.cs         手写 MVVM 基类（INotifyPropertyChanged）
  ViewModels/MainViewModel.cs            主 VM：目标集合/命令/状态机编排
  ViewModels/TargetViewModel.cs          单目标卡片 VM（名称/快照/选中/压暗值）
  Services/TargetTracker.cs              跟踪（原样搬）
  Services/SnapshotService.cs            快照（原样搬 + Bitmap→BitmapSource 适配）
  Services/Settings.cs                   配置（原样搬）
  Services/Elevation.cs                  提权（原样搬）
  Services/Native.cs                     P/Invoke（原样搬）
  Services/WindowEffects.cs              压暗/置顶/垫黑应用（原 BindTarget/ReleaseTarget/ApplyEntryEffect 编排）
```

## 主线逻辑（保证通畅）

```
启动 → 读配置(Settings) → 绑定循环(TargetTracker.FindMatch) → TargetViewModel 入列
     → TargetTracker.OnUpdate → WindowEffects.ApplyEffect(压暗+黑底跟随) / SnapshotService 刷快照
     → 窗口销毁 WinEvent → 卡片转待激活 → 重开自动重绑（3s 兜底 + 事件即时）
     → 暂停/删除/退出 → 全部还原
```

## 支线逻辑（保证通畅）

- 托盘：Hardcodet.NotifyIcon.Wpf（状态/启停/退出）
- 开机自启：Settings.ApplyStartWithWindows（原样）
- 提权提示：Elevation（原样）
- 重命名：RenameDialog
- 快照：3s 定时 + 事件即时 + 单卡/全量手动

## 实施阶段（每步构建验证 + 里程碑截图）

| 阶段 | 内容 | 验收 |
|------|------|------|
| **A 骨架** | 新建 WPF 项目 + 主题 Dark.xaml + 空主窗口（6 行 Grid） | 构建 0 错误，窗口能开 |
| **B 静态 UI 1:1** | MainWindow.xaml 完整设计稿布局 + 假数据卡片网格（ItemsControl+WrapPanel+DataTemplate） | **截图与 UI_REDESIGN.html 并排对比 1:1** |
| **C 逻辑接入** | 搬 Services 原样 + MainViewModel/TargetViewModel + BlackPlateWindow/WindowPickerWindow 换壳 + 托盘/重命名 | 功能端到端：绑定/压暗/黑底跟随/快照/重绑 |
| **D 联调发布** | 冒烟 + 端到端截图 + 发布 dist\WindowTinter-v11-framework-dependent\ + 归档 v10 + 提交 | 轻量版可运行 |

## 铁律

- 纯逻辑文件 `git cp` 原样搬，diff 只允许：命名空间 + WinForms→WPF 互操作适配（Bitmap→BitmapSource、BeginInvoke→Dispatcher）
- 不引第三方 MVVM 库（手写 ObservableObject，避免 NuGet 依赖风险）；托盘需 Hardcodet.NotifyIcon.Wpf（WPF 无原生，成熟 NuGet）
- 旧 WinForms 版保留在 `WindowTinter/`（原 csproj），可随时回退
