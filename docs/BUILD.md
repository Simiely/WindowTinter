# 构建与发布

> 对应版本 7.2.0。

## 1. 前提

- Windows x64
- .NET 6 SDK（或更高 SDK，但目标框架保持 `net6.0-windows`）
- PowerShell 5+

## 2. 目录陷阱（第一次构建必看）

仓库根目录的 `WindowTinter.csproj` 是 **WinForms 历史项目**，产物也叫 `WindowTinter.exe`。当前应用在 `WindowTinter.Wpf/`。**所有构建命令都必须在 `WindowTinter.Wpf/` 下执行**，否则构建的是旧程序。

## 3. 构建

```powershell
cd WindowTinter.Wpf
dotnet build -c Release
```

验收标准：**0 警告 0 错误**。7.2.0 的基线构建结果就是 0/0。

## 4. 单文件发布

```powershell
cd WindowTinter.Wpf
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true `
  -o ..\publish\WindowTinter-v7.2.0-win-x64
```

说明：

- `--self-contained false`：框架依赖发布，运行需要 .NET 6 Desktop Runtime（x64）
- `PublishSingleFile=true`：主程序合并为单个 `WindowTinter.exe`；`app.ico` 已嵌入程序集，不需要外部 ico
- 默认会同时生成 `WindowTinter.pdb`（约 65 KB）。不想要调试符号可加 `-p:DebugType=None -p:DebugSymbols=false`
- 不会复制 `settings.json`：配置在运行时首次生成在 exe 同目录

7.2.0 的发布产物基线：

| 文件 | 字节数 |
|---|---|
| `WindowTinter.exe` | 799,510 |
| `WindowTinter.pdb` | 66,828 |

`FileVersion = 7.2.0.0`，`ProductVersion` 附带构建时的 commit 短哈希。

### exe 正在运行时覆盖失败

Windows 会锁住正在运行的 exe。两种处理：

1. 先退出程序再 publish；
2. 先发布到独立目录（如 `...-win-x64.staging`），再用后台脚本等进程退出后 `Copy-Item -Force` 覆盖正式目录。**不要把 staging 的 `settings.json` 一起拷进正式目录**。

## 5. 打包 Release zip

只包运行所需文件，**不要**打包本机 `settings.json`、staging 目录、日志：

```powershell
# 在仓库外准备干净目录
$dir = "<发布目录>\v7.2.0"
Copy-Item <publish>\WindowTinter.exe $dir
Copy-Item <publish>\WindowTinter.pdb $dir
# 手写一份 使用说明.txt 放进去
Compress-Archive -Path $dir\* -DestinationPath "<发布目录>\WindowTinter-v7.2.0-win-x64.zip"
```

校验：

```powershell
(Get-Item <zip>).Length
(Get-FileHash <zip> -Algorithm SHA256).Hash
```

## 6. 发布到 GitHub

```powershell
git tag v7.2.0
git push origin v7.2.0
gh release create v7.2.0 <zip> --title "v7.2.0 · 暗幕 WPF 正式版" --notes-file <说明文件>
```

凭据走环境变量（`$env:GH_TOKEN`），**不要**写进 `.git/config`、remote URL、脚本或文档；用完立即清空。

## 7. 发布前检查清单

- [ ] 在 `WindowTinter.Wpf/` 下 `dotnet build -c Release` 为 0 警告 0 错误
- [ ] `git status` 干净；`bin/`、`obj/`、`publish/`、`.staging/`、`*.settings.json` 均未被追踪
- [ ] 发布目录里**没有** `WindowTinter.settings.json`
- [ ] `dotnet publish` 后 exe 的 `FileVersion` 与 csproj 版本一致
- [ ] zip 里只有 exe / pdb / 使用说明
- [ ] 无目标时启动一次做冒烟（空列表高度 560、托盘图标、关闭即隐藏）
- [ ] 版本号只在 `WindowTinter.Wpf.csproj` 维护（`Version` / `AssemblyVersion` / `FileVersion`）

## 8. 常见构建问题

- **`NETSDK1045` / 找不到 net6.0 目标**：安装 .NET 6 SDK，或确认没有在根目录构建（根项目的目标框架不同）。
- **发布后 exe 启动立即退出**：多半是单实例 Mutex 已有实例在跑；先结束旧进程。
- **改了 XAML 但界面没变**：确认构建的是 `WindowTinter.Wpf` 项目；`obj\Release\net6.0-windows\Views\*.baml` 时间戳可作参考。
- **发布包过大**：检查是否误用了 `--self-contained true`（会把整个运行时打进去）。
