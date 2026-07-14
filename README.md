# 暗幕 — WindowTinter v3.6.2

> 给任意 Windows 窗口叠加**深色半透明蒙版**的常驻小工具。
> 目标切到后台自动变半透明，不遮挡其他窗口。支持多窗口同时监控。

🌐 **产品页面**: [simiely.github.io/WindowTinter](https://simiely.github.io/WindowTinter/)

---

## 功能

- **前景蒙版** — 点击目标窗口自动显示暗色蒙版，鼠标穿透不挡交互
- **后台透明** — 切走目标后自动变半透明，不遮挡任何其他窗口
- **保持透明度模式** — 开关后不走蒙版，前台后台始终用统一透明度
- **双滑块** — 蒙版透明度 + 后台透明度各独立调节（0~100%）
- **多窗口** — 不限数量，任意添加
- **自动跟随** — 移动/缩放/最小化蒙版同步
- **自动发现** — 目标程序启动后自动绑定
- **托盘常驻** — 最小化到系统托盘，后台静默
- **开机自启** — 可选

---

## 快速开始

1. 下载 [Release](https://github.com/Simiely/WindowTinter/releases) 中 `WindowTinter-release.zip`，解压运行 `WindowTinter.exe`
2. 点击 **"+ 添加窗口"** → 十字光标拾取目标窗口（左键选择 / 右键或 Esc 取消）
3. 勾选 **"启用覆盖"** → 点击目标窗口自动变暗，切走自动变半透明
4. 拖动双滑块实时调节蒙版和后台透明度
5. **"窗口保持透明度"** — 开启后前台后台始终用统一透明度，不叠加蒙版
6. 关闭窗口自动最小化到托盘继续监控

> 配置保存在 exe 同目录 `WindowTinter.settings.json`。图标 `app.ico` 需与 exe 同目录。

---

## 构建

```powershell
cd WindowTinter
dotnet build -c Release
```

> 需要 .NET 6+ SDK 和 Windows 环境。Release 包为单文件自包含，无需安装 .NET 运行时。

---

## 技术栈

| 组件 | 技术 |
|---|---|
| 前景蒙版 | `UpdateLayeredWindow` + `WS_EX_LAYERED \| WS_EX_TRANSPARENT` |
| 后台透明 | `SetLayeredWindowAttributes` + `WS_EX_LAYERED` |
| 鼠标穿透 | `WS_EX_TRANSPARENT` |
| 窗口跟踪 | `SetWinEventHook` + 250ms 轮询兜底 |
| 框架 | .NET 6 WinForms |

---

## 开发笔记

详见 **[DEV.md](./DEV.md)** — 完整踩坑记录，涵盖 25 个关键技术问题、架构演进、代码审计清单。

---

## 许可

MIT License.
