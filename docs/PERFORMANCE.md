# 性能基线

> 对应版本 7.2.0。**注意：这是「当前机器上的运行实例」实测，不是隔离干净环境的复测**。仓库中没有更早的性能基线数据可复测（`DEV.md` 只有定性经验，没有数字）。

## 1. 本次实测（2026-11-06）

测试对象：正在运行的 `WindowTinter.exe`（7.2.0，单文件框架依赖发布，799,510 字节）。

测试方法：`Get-Process` 读取 `TotalProcessorTime`，间隔 10 秒前后各采样一次；内存取 `WorkingSet64` / `PrivateMemorySize64`；`Threads.Count`。

| 指标 | 数值 | 说明 |
|---|---|---|
| CPU（单核百分比） | **0.16%** | 10 秒窗口内 CPU 时间增量 ÷ 10s ÷ 1 核 |
| CPU（整机百分比） | **0.01%** | 28 个逻辑处理器 |
| 进程累计 CPU 时间 | 1.62 s | 自本次启动以来 |
| 工作集（Working Set） | **134.2 MB** | WPF 应用常驻 |
| 私有内存（Private） | **109.1 MB** | |
| 线程数 | 26 | |
| 句柄数 | 1,302 | `Process.HandleCount` |

## 2. 测试条件（必须连同数字一起引用）

- 操作系统：Microsoft Windows 11 IoT 企业版 LTSC
- 逻辑处理器：28
- 显示缩放：96 DPI（100%）
- 目标列表：**空**（`Targets: []`，没有任何被压暗窗口，底板未创建）
- 主窗口处于正常使用状态；桌面有其它应用程序在运行（**非干净环境**）
- 采样窗口：10 秒，单次采样；未做多次取均值
- 版本：7.2.0（`FileVersion 7.2.0.0`）

**结论**：空闲（无目标）时 CPU 占用可忽略（<0.2% 单核）；内存约 134 MB 工作集，属于 WPF + .NET 6 的常态水平。

## 3. 未测量的场景（不要引用上面的数字去推断）

- 绑定 1 个 / 3 个 / 10 个目标时的 CPU 与内存
- 拖动滑块、频繁刷新快照、目标窗口快速移动时的峰值 CPU
- 最小化/还原、切换虚拟桌面（cloak）时的瞬时开销
- 24 小时以上驻留的内存增长（是否有泄漏）
- 快照线程（`PrintWindow`）的单次耗时
- WinEvent 回调频率（桌面级 `LOCATIONCHANGE` 被句柄过滤前的事件量）

## 4. 已知的性能相关设计（代码事实）

- WinEvent 回调线程先按**已绑定句柄**过滤（`_boundHandles` 数组），不把全桌面事件投递到 UI 线程。
- `ApplyEntryEffect` 用 `LastAlpha` 差异计算，alpha 不变不调系统 API。
- 底板位置在 500ms 轮询 + WinEvent 驱动下更新，不是逐帧跟随。
- 快照改为手动/首次抓取，不再 3 秒定时抓图；同一目标已有 `PrintWindow` 在跑时不重复排队。
- 设置保存有 200ms 防抖（滑块拖动）。
- 跨进程 `SetWindowPos(SWP_FRAMECHANGED)` 会阻塞调用线程，代码里用 `RedrawWindow` 异步替代（见 `AGENTS.md`）。

## 5. 复测方法（供后续补充数据）

```powershell
$p = Get-Process WindowTinter
$c0 = $p.TotalProcessorTime.TotalMilliseconds
Start-Sleep -Seconds 10
$p.Refresh()
$c1 = $p.TotalProcessorTime.TotalMilliseconds
"单核 CPU% = " + [math]::Round(($c1-$c0)/1000/10*100,2)
"工作集 MB = " + [math]::Round($p.WorkingSet64/1MB,1)
```

复测时请记录：OS 版本、逻辑处理器数、显示缩放、绑定的目标数量与类型、是否有其它负载、采样时长。只有条件一致的数字才能对比。
