# DEVELOPMENT.md · 开发入口

> 当前版本 **7.3.2（WPF）**。根目录的 `WindowTinter.csproj`、`MainForm*.cs`、`Program.cs` 等
> 是 WinForms 时代的源码，**仅归档，不要构建**（会产出另一个同名程序）。

## 一、按需要读哪份

| 想知道 | 看这里 |
|---|---|
| 架构与效果应用主线 | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| 怎么构建、单文件发布、发版 | [docs/BUILD.md](docs/BUILD.md) |
| 哪些事「不值得做 / 不要做」 | [docs/DECISIONS.md](docs/DECISIONS.md) |
| 已知限制与未验证项 | [docs/LIMITATIONS.md](docs/LIMITATIONS.md) |
| 性能基线与测试条件 | [docs/PERFORMANCE.md](docs/PERFORMANCE.md) |
| 安装 / 升级 / 卸载 | [docs/INSTALL.md](docs/INSTALL.md) |
| 规则与 15 条关键坑 | [AGENTS.md](AGENTS.md) |
| WinForms 时代怎么走到今天的 | [docs/history/](docs/history/README.md) |

本文件只讲**怎么在这个仓库里干活**，不重复上面任何一份的内容。

## 二、代码组织

当前应用是 `WindowTinter.Wpf/`，三层：

| 目录 | 职责 |
|---|---|
| `Services/` | Win32 封装与领域服务：`Native`、`BlackPlate`、`TargetTracker`、`SnapshotService`、`Settings`、`Elevation`、`TrayService`、`WindowTheme` |
| `ViewModels/` | `MainViewModel`（领域逻辑主体）、`TargetViewModel`（单卡状态）、`ObservableObject` |
| `Views/` | `MainWindow`、`WindowPickerWindow`、`SettingsDialog`、`RenameDialog`、`AboutDialog`、`CardsPanel` |

依赖只有两个：`Hardcodet.NotifyIcon.Wpf 2.0.1`（托盘）、`System.Drawing.Common 6.0.0`（快照位图）。
**没有** DI 容器、MVVM 框架、日志框架——不要为了"规范"引入它们（理由见 [docs/DECISIONS.md](docs/DECISIONS.md)）。

**没有测试工程**。验证靠构建 + 真机冒烟；改效果相关代码前先读
[AGENTS.md](AGENTS.md) 的关键坑（尤其第 2、4 条：WinEvent 回环与 hwnd 复用）。

## 三、改一个功能的路径

1. 领域逻辑改 `Services/*` 与 `MainViewModel`；UI 改 `Views/*.xaml` + 对应 VM。
2. 任何跨进程操作前加 `IsWindow(hwnd)` 守卫；改窗口样式后确认不会触发 WinEvent 回环。
3. 改持久化格式必须写迁移代码（`.exe` 后缀剥离那次教训）。
4. 构建**必须在 WPF 目录**：

```bash
cd WindowTinter.Wpf
dotnet build -c Release          # 期望 0 警告
```

## 四、提交前检查

- [ ] `dotnet build -c Release` **0 警告 0 错误**
- [ ] 版本号只改 `WindowTinter.Wpf/WindowTinter.Wpf.csproj` 的 `Version` / `AssemblyVersion` / `FileVersion`
- [ ] `CHANGELOG.md` 有对应的 `## vX.Y.Z` 段落——**没有它 CI 会拒绝发布**
- [ ] 文档检查：`python tools/repo-kit/check_repo.py --root .`

## 五、发布

推 `v*` tag 触发 [.github/workflows/release.yml](.github/workflows/release.yml)：校验版本与 CHANGELOG、
0 警告构建、创建或更新 Release。手动验证用：

```bash
gh workflow run release.yml -f version=X.Y.Z -f dry_run=true
```

产物只含 `WindowTinter.exe` + `WindowTinter.pdb` + `使用说明.txt`；
**不要**打包 `WindowTinter.settings.json`、staging 目录、日志（详见 [docs/BUILD.md](docs/BUILD.md)）。
