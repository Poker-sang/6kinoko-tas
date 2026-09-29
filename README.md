# Kinoko TAS

独立 C# / .NET 10 + Avalonia 12 输入编辑器；不包含游戏和原版 DAT。

## 已实现

- 校验并打开 KINORPL1 `.krec`，查看 19 个独立动作时间轴。
- 点击修改、区间按住/松开、撤销/重做、帧定位及未保存提示。
- 保存压缩 `.ktas` 项目，完整保留原始录制和稀疏编辑。
- 显示原始 RNG/检查值，标明编辑后检查值失效的位置。
- 导出未修改的原始录制。

**游戏控制尚未接入**：上一帧/下一帧只移动编辑光标。游戏暂停、逐帧、倍速、
重新执行和快照留待后续。编辑结果不伪装成可直接播放的 `.krec`。

## 开发和检查

```powershell
dotnet build KinokoTAS.slnx -c Release
dotnet run --project src/KinokoTAS.App -- "路径/session.krec"
dotnet run --project tests/KinokoTAS.Checks -c Release -- artifacts/checks-unique
```

检查程序验证格式、编辑和保存，并用 Avalonia 无窗口后端测试点击并输出截图。
可传第二个参数只读校验本地真实录制。不运行游戏，不写录制目录。
当前只在 Windows 验证，Linux/macOS 编辑器运行尚未验证。

独立 Git 仓库尚无远程，待用户指定。不会推送到 modern/rebuild。
格式边界见 [docs/formats.md](docs/formats.md)。
