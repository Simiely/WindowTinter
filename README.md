# WindowTinter

> 给**任意 Windows 窗口**叠加**深色半透明蒙版**的常驻小工具。
> 典型场景：官方百度网盘客户端太亮、无自带深色模式，用它一键压暗。支持多窗口同时覆盖。

---

## 功能

- **深色蒙版** — `UpdateLayeredWindow` + `WS_EX_TRANSPARENT`，DWM 硬件合成暗化，鼠标穿透不挡交互
- **多窗口监控** — 不限一个目标，可添加任意多个窗口同时压暗
- **仅前台暗化** — 点击目标窗口自动变暗，切走立即恢复，不盖上层窗口
- **滑块调透明度** — 0~100%，实时生效，位图缓存无卡顿
- **窗口跟随** — WinEvent 驱动，目标移动/缩放/最小化时蒙版自动同步
- **自动绑定** — 目标程序启动后 3 秒内自动发现并遮罩
- **托盘常驻** — 最小化到系统托盘，后台静默运行
- **深色主题** — 设置界面跟随 Windows 深色模式
- **开机自启** — 可选写入注册表 `Run` 项
- **配置持久化** — JSON 文件保存，下次启动自动恢复

---

## 使用

1. 下载 [最新 Release](https://github.com/Simiely/WindowTinter/releases)，解压运行 `WindowTinter.exe`
2. 点击 **"+ 添加窗口"** → 鼠标十字光标拾取任意窗口
3. 勾选 **"启用覆盖"** → 目标窗口被选中时自动变暗
4. 拖**透明度**滑块实时调节浓度
5. 关闭窗口 → 最小化到托盘继续监控（可配置为直接退出）

> 配置保存在 exe 同目录 `WindowTinter.settings.json`。

---

## 构建

```powershell
cd WindowTinter
dotnet build -c Release
# 产物: bin/Release/net6.0-windows/WindowTinter.exe
```

> 需要 .NET 6 SDK。用 .NET 8 把 csproj 里 `net6.0-windows` 改成 `net8.0-windows` 即可。

---

## 原理

蒙版窗口使用 `WS_EX_LAYERED | WS_EX_TRANSPARENT` 扩展样式：
- `WS_EX_LAYERED` 支持分层透明度，`UpdateLayeredWindow` 把纯黑位图以指定 alpha 交给 DWM 合成
- `WS_EX_TRANSPARENT` 让鼠标点击直接穿透到下层窗口
- `SetWinEventHook` 监听前台切换/窗口变化，事件驱动刷新，250ms 轮询兜底
- 位图缓存：尺寸不变时复用，拖滑块零分配

---

## 踩坑与开发笔记

详见 **[DEV.md](./DEV.md)**，记录 Z 序陷阱、Magnifier 弯路、双屏适配等关键问题。

---

## 许可

MIT License.
