# TAS 首版收尾

2026-09-30：用户确认新版不限速定位“效果很好”，认为可以收尾。
这是用户实测反馈；不记作代理游玩或跨平台验证。本次仅整理文档，
沿用已构建、已检查的版本，不改程序或录制格式。

## 当前版本

- 编辑器：`C:/WorkSpace/6kinoko-tas/artifacts/windows-editor-20/KinokoTAS.App.exe`
  （源码 `57f7064`，需 .NET 10）。
- 游戏：`C:/WorkSpace/6kinoko-modern/runtime-builds/modern-x64-tas-fast-seek-03/kinoko_modern_gpu.exe`
  （源码 `d64dac9d`）。游戏路径需选择此新版以启用不限速定位。
- 编辑器构建和完整合成检查：`artifacts/build-51`、`artifacts/checks-51`。
- 游戏构建、四项契约检查、隐藏窗口 GPU 检查和 DAT 校验：
  `C:/WorkSpace/6kinoko-modern/build-runs/modern-x64-tas-fast-seek-03`。
- 两项目主分支均为 `master`，构建、日志、旧分支和录制工作文件继续保留。

## 完成范围

新建、打开和保存录制；单文件 .krec 封装初始存档与书签；实时横向
时间轴、播放游标、跟随、重点帧；暂停、逐帧、重播、定位和进程重启；
独立游戏窗口与可选内嵌预览；随时接管覆盖及当前会话内恢复旧来源；
输入编辑、拖动绘制、插入删除帧和撤销重做；重新模拟与完整回放验证；
取消、进度、记住游戏路径、倍速播放和不限速定位。

## 保留限制

向后定位仍从初始存档重新模拟，没有完整运行状态快照或画面历史缓存。
恢复覆盖历史属于当前编辑器会话，跨重启恢复尚未实现。
未执行的输入草稿保存为 .ktas；正式分享使用 .krec，不需附带 sessions。
封装 .krec 由编辑器解包，游戏旧 CLI 不直接读取该封装。
编辑器发布包仍依赖 .NET 10；Linux/macOS 编辑器未做新的人工验证。
上述项目不阻塞首版收尾，后续按实际需求另行推进。
