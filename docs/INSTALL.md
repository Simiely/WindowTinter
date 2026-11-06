# 安装、升级与卸载

> 对应版本 7.2.0。程序是绿色单文件，没有安装器。

## 1. 安装

1. 从 [Releases](https://github.com/Simiely/WindowTinter/releases) 下载 `WindowTinter-v7.2.0-win-x64.zip`。
2. 解压到**可写目录**（例如 `%USERPROFILE%\Tools\WindowTinter`）。不要放在 `Program Files` 或只读共享盘——配置写不进去且不会报错。
3. 安装 [.NET 6 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/6.0)。缺少运行时时，Windows 会提示下载。
4. 双击 `WindowTinter.exe`。首次运行生成 `WindowTinter.settings.json`。
5. SmartScreen 拦截时：点「更多信息 → 仍要运行」（未做代码签名）。

**不需要**：管理员权限（除非要压暗同样以管理员运行的目标窗口）、.NET SDK、注册 DLL。

## 2. 文件与注册表足迹

| 位置 | 内容 |
|---|---|
| exe 同目录 `WindowTinter.exe` | 程序（可选 `WindowTinter.pdb` 调试符号） |
| exe 同目录 `WindowTinter.settings.json` | 全部配置：目标列表、全局参数、窗口位置、UI 状态 |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `WindowTinter` | 仅在勾选「开机自启」时存在，值为 `"<exe路径>" /startup` |
| `%LOCALAPPDATA%\WindowTinter\` | 仅在历史版本留下过配置；启动时会一次性反向迁移回 exe 目录并删除 |

程序**没有**其它注册表写入、没有系统服务、没有计划任务、没有本地日志文件。

## 3. 升级

1. 关闭程序（托盘右键退出；只在主窗口点 × 只是收进托盘）。
2. 用新 `WindowTinter.exe` 覆盖旧文件（可保留 `WindowTinter.pdb`）。
3. **保留** `WindowTinter.settings.json`。启动时自动做旧配置迁移：
   - 0~255 的透明度 → 0~100
   - 单窗口字段 → 目标列表
   - 旧 `GlobalTransparency` / `GlobalCornerRadius` 开关 → 「已自定义」标记
   - `ProcessName` 的 `.exe` 后缀剥离
   - 旧调参版 `DebugCorner` → 正式 `Corner*` 字段，并清除该段
4. 首次启动后建议看一眼目标列表是否完整；迁移失败时文件解析异常会退回默认配置（不覆盖原文件，除非后续保存）。

**降级**：旧版本只写自己认识的字段，新字段（如 `Corner*`）会丢失；降级前先备份 `WindowTinter.settings.json`。

## 4. 开机自启

- 在设置里勾选「开机自启」→ 写入 `HKCU\...\Run`，命令带 `/startup`。
- **当前 WPF 版不解析 `/startup`**：开机启动会显示主窗口，与手动启动相同（详见 `LIMITATIONS.md`）。
- 取消勾选 → 删除该注册表值。手动删除注册表项同样有效。

## 5. 卸载

1. 托盘右键退出（或任务管理器结束 `WindowTinter.exe`）。
2. 删除 exe 与 `WindowTinter.settings.json`。
3. 若勾选过开机自启，先在设置里取消，或手动删除 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WindowTinter`。
4. 可选：删除 `%LOCALAPPDATA%\WindowTinter`（正常情况下不存在）。

卸载不会改动任何被压暗过的窗口——程序在退出时会按绑定前记录的状态还原 alpha / `WS_EX_LAYERED` / topmost。若曾在程序异常退出后留下半透明窗口，重启该目标程序即可恢复。

## 6. 便携使用

整个目录（exe + settings.json）可以复制到另一台机器继续用；配置跟随 exe 目录，不跟随用户账户。换机器后窗口位置会自动校验是否在当前可见屏幕内，越界则回到默认居中。
