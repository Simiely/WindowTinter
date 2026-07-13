# WindowTinter

> 给**任意 Windows 窗口**叠加**深色半透明蒙版**的常驻小工具。
> 典型场景：百度网盘、微信等无自带深色模式的客户端，一键压暗护眼。支持多窗口同时覆盖。

---

## 功能

- **深色蒙版** — `UpdateLayeredWindow` + `WS_EX_TRANSPARENT`，DWM 硬件暗化，鼠标穿透不挡交互
- **多窗口监控** — 不限数量，可添加任意窗口
- **仅前台暗化** — 点击目标自动压暗，切走立即恢复，不盖上层窗口
- **0~100% 透明度** — 滑块实时调节，位图缓存零卡顿
- **窗口自动跟随** — WinEvent 驱动，移动/缩放/最小化蒙版同步
- **自动绑定** — 目标程序启动后 3 秒内自动发现并遮罩
- **托盘常驻** — 最小化到系统托盘，后台静默
- **开机自启** — 可选写入注册表 `Run` 项

---

## 使用

1. 下载 [Release](https://github.com/Simiely/WindowTinter/releases) 中 `WindowTinter.exe` 直接运行（无需安装 .NET）
2. 点击 **"+ 添加窗口"** → 十字光标拾取任意窗口
3. 勾选 **"启用覆盖"** → 目标被选中时自动变暗
4. 拖动**透明度滑块**实时调节
5. 关闭设置窗口 → 最小化到托盘继续监控

> 配置保存在 exe 同目录 `WindowTinter.settings.json`。

---

## 构建

```powershell
cd WindowTinter
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

> 需要 .NET 6+ SDK 和 Windows 环境。

---

## 技术栈

| 组件 | 技术 |
|---|---|
| 蒙版渲染 | `UpdateLayeredWindow` + `WS_EX_LAYERED` `WS_EX_TRANSPARENT` |
| 点击穿透 | `WS_EX_TRANSPARENT` 扩展样式 |
| 窗口跟踪 | `SetWinEventHook` 事件驱动 + 250ms 轮询兜底 |
| 位图缓存 | `Bitmap` 尺寸不变时复用，零帧分配 |
| 框架 | .NET 6 WinForms，单文件 exe |

---

## 开发笔记

详细踩坑记录见 **[DEV.md](./DEV.md)**。

---

## 许可

MIT License.
