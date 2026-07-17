# 暗幕 (WindowTinter) v5.1.0

**给任意窗口设置透明度、并在其正下方垫纯黑的 Windows 常驻小工具。**

当窗口半透明时，露出的通常是背后杂乱的桌面或其它窗口——暗幕可以在窗口正下方垫一层纯黑底板，让半透明稳定呈现为"压暗"效果。

## 功能

- **窗口透明度控制** — 统一或按窗口独立配置透明度（0%~100%）
- **纯黑下层遮罩** — 半透明窗口背后垫纯黑，避免透出桌面/其它窗口
- **多窗口同时控制** — 可添加多个目标窗口，各自独立配置
- **全局/单独模式** — 全局统一透明度，或为每个窗口单独设置
- **自动绑定** — 目标窗口关闭再启动后自动重新绑定
- **深色主题** — 原生暗色 UI，配合沉浸式深色标题栏
- **托盘驻留** — 关闭窗口最小化到系统托盘，持续工作

## 使用

1. 运行 `WindowTinter.exe`
2. 点击 **+ 添加窗口**，鼠标移到目标窗口上点击（十字光标）
3. 拖动 **窗口透明度** 滑块调整半透明程度
4. 勾选 **下方垫黑色（下层遮罩）** 在目标正后方垫纯黑底板
5. 关闭窗口即最小化到托盘，右键托盘图标可切换/退出

### 提示

- 取消勾选 **全局统一透明度** 后，可单独选择每个窗口并独立设置值
- 如果目标以管理员身份运行，请右键"以管理员身份运行"本程序
- 配置文件 `WindowTinter.settings.json` 与 exe 同目录

## 下载

从 [Releases](https://github.com/Simiely/WindowTinter/releases) 下载最新 `WindowTinter.exe`。

**要求**：[.NET 6 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/6.0)（x64）

## 构建

```bash
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

---
