# Kinoko TAS

C# / .NET 10 + Avalonia 12 的《魔理沙与六个蘑菇》TAS 编辑器，配合 [6kinoko-modern](https://github.com/Poker-sang/6kinoko-modern) 使用。不含游戏程序和原版 DAT。

## 使用

安装 .NET 10，构建或下载编辑器。首次新建/打开录制时选择支持 TAS 的 kinoko_modern_gpu.exe；游戏目录需自备原版三个 DAT。

- 新建录制、打开录制、保存单文件 .krec（含初始存档和书签）。兼容旧 KINORPL1 录制。
- 独立游戏窗口或可选内嵌预览；暂停、逐帧、从头重播、关闭后重新启动游戏。
- 横向实时输入时间轴，播放游标、跟随最新帧、可命名书签。
- 帧号单击选中，双击定位；右键添加书签。回退目前从头重播模拟。
- 定位后切换录制，覆盖当前帧之后的内容；旧来源保留为内部恢复副本。
- 保存时选择单个 .krec 文件；内部 sessions 工作文件不必随录制分享。

游戏窗口 F9 播放/暂停、F10 前进一帧。工具栏“…”包含更换游戏和内嵌预览。
时间轴输入格修改仍是 .ktas 草稿，尚未支持编辑后重新模拟；完整快照和快速倒带也未实现。
新封装 .krec 由本编辑器解包后交给游戏，游戏旧 CLI 不直接识别该封装。

## 开发与检查

```powershell
dotnet build KinokoTAS.slnx -c Release
dotnet run --project src/KinokoTAS.App
dotnet run --project tests/KinokoTAS.Checks -c Release -- artifacts/checks-unique
```

检查使用模拟引擎和 Avalonia 无窗口后端，不运行真实游戏。当前主要验证 Windows；其他平台编辑器运行未验证。
详细记录见 docs/，其中旧版交接路径为历史记录。

## 许可证

GPL-3.0，见 LICENSE。移植的 Pixeval ContentDialog / CommandBar 样式保留原版权声明，见 licenses/Pixeval。
第三方包保持各自许可证；游戏资源不包含在仓库中。
