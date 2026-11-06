# 「不值得做 / 不要做」的结论

> 目的是避免后来的人重复已经排除的方案。每条都给出理由与依据；有争议的标注「未验证」。

## 1. 机制层：不要替换 UpdateLayeredWindow

**不要**改成 Magnifier API 或 WGC + Direct2D：

| 方案 | 为什么不做 |
|---|---|
| Magnifier API | 同样是 TOPMOST 真窗口，仍会挡交互；DWM 捕获屏幕像素 + 颜色矩阵，链路复杂。v2.0 走过「反色/Magnifier」弯路，已删除 |
| WGC + Direct2D | 需要 Win10 1903+ 与大量 WinRT/D3D11 基础设施，引入 SharpDX 级依赖；本工具只需要一张纯黑位图 |
| 给目标窗口加 `WS_EX_TRANSPARENT` 遮挡层 | `WS_EX_TRANSPARENT`（穿透）与接收鼠标事件互斥；曾用极短时间切换样式的方案，时序脆弱，已弃用 |

依据：`DEV.md` 第 1 节；当前 `Services/BlackPlate.cs` 用 `UpdateLayeredWindow`。

## 2. 不要重写为「全量 MVVM 框架 + DI 容器」

现状是手写 `ObservableObject` / `RelayCommand` + 构造函数里 new 服务。够用，且这套代码的价值集中在 Win32 时序与状态还原，不在分层优雅。引入 CommunityToolkit.Mvvm / Prism / DI 只会增加启动面与调试难度，不会改善任何用户可见行为。

同理**不要**把 `MainViewModel` 拆成十几个 Handler：效果应用必须保持单一入口（`ApplyEntryEffect` + 外层 try/catch），拆散后极易出现「某条路径忘了还原 alpha」。

## 3. 不要为「性能」提前重构

已有优化都是有具体证据才做的：WinEvent 按句柄过滤、`LastAlpha` 差异计算、快照去重、200ms 保存防抖、`RedrawWindow` 替代 `SetWindowPos(SWP_FRAMECHANGED)`（后者跨进程会阻塞 UI 线程）。在没有实测数据（见 `PERFORMANCE.md`）前，不要引入线程池重排、对象池、双缓冲位图复用之类的改动——收益未证实，回归风险真实。

## 4. 不要用「显示/隐藏窗口」解决点击穿透与拾取

`ShowWindow` 触发 DWM 过渡动画，既闪又慢。拾取器改用 `EnumWindows` + `PtInRect`；需要让 `WindowFromPoint` 忽略自身时用 `WS_DISABLED`（`Enabled=false`），不要用 `ShowWindow`。依据：`DEV.md` 的拾取器章节与 `WindowPickerWindow` 现有实现。

## 5. 不要把配置迁到 `%LOCALAPPDATA%` / 注册表 / 数据库

历史上试过 `%LOCALAPPDATA%`（commit `015c44a`），随后 `d7cb512` 回滚，并保留一次性反向迁移。结论：**便携工具的配置必须跟 exe 同目录**，否则「换目录/换机器配置丢失」的投诉无解。`settings.json` + 原子替换（`.tmp` + `Move`）已经足够。

## 6. 不要移除「绑定前状态捕获」

不要为了简化把 `OriginalExStyle / OriginalLayered / OriginalAlpha / OriginalTopmost` 删掉、退出时一律写 255 并移除 `WS_EX_LAYERED`。浏览器等原生分层窗口会被改坏（历史 bug）。还原必须只回滚本程序改动过的部分，这是不可简化的复杂度。

## 7. 不要给圆角加更多自动「对齐」逻辑

主窗四参数（外轮廓 / 描边弧 / 裁切 / 内缩）是逐像素试出来的（23/14/13/0）。不要再写「自动推导 = outer-1」之类的规则：此前正是 `clip = outer - 1` 的假设导致外轮廓与描边弧不重合、四角出现黑缝（commit `a30dc4f`、`565572a`）。要改就在快捷键上调，调好再固化。

## 8. 待议 / 明确未验证，不要据此下结论

- 「用 WinRT `Windows.UI.Composition` 替代 Win32 分层窗口」：未验证，且会引入 Win10 版本门槛。
- 「用 SetWindowDisplayAffinity / 捕获排除」：与压暗目标无关，不要混用。
- 「桌面（Progman）压暗」：未实现，且绘制桌面图标层风险高，暂不做。
- 「独占全屏支持」：未验证，不要承诺支持。

## 9. 文档与提交约定

- 数字必须注明来源与测试条件；没测过的写「未验证」。
- CHANGELOG 只写真实提交里的改动，不写计划。
- 发布版本的版本号只维护在 `WindowTinter.Wpf.csproj`。
