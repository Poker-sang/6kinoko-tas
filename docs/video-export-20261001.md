# 无声视频导出

源码提交：`894792a`（初版 `f8b072a`）。游戏侧无需修改。

交付：`artifacts/windows-editor-42-aot/6kinokoTAS.App.exe`，Windows x64 NativeAOT、自包含。

文件栏二级菜单提供“导出视频…”。必须先保存 .krec；整段或包含两端的帧区间生成 H.264 / 60 FPS / 无音轨 MP4。FFmpeg 自动查找或手动选择，设置使用源生成 JSON。独立静音回放逐帧等待对应 Completed 后读取 RGBA，不使用屏幕录制，不替换当前游戏会话。奇数尺寸补齐偶数边界。

每次导出保存独立初始存档和录制副本；取消和失败保留会话、编码日志及临时输出。成功才替换指定视频。此版本不导出声音，也不支持未保存编辑直接导出。

验证（代理执行自动检查，不代表真实游戏游玩验证）：

- NativeAOT 回归：`artifacts/windows-aot-checks-07/KinokoTAS.Checks.exe`，全部通过；日志 `artifacts/checks-aot-95.log`。
- 合成游戏及编码器验证包含两端的帧范围、精确帧投递、取消及编码失败保留已有文件和诊断日志。
- 无界面编辑器验证独立导出不替换当前游戏进程，不修改已保存录制。
- 实际 FFmpeg 对合成游戏帧编码成功；ffprobe 确认 H.264、60/1 FPS、5 帧、仅视频流。证据 `artifacts/checks-aot-95/video-probe.json`；1×1 合成图像补齐为 2×2。
- 编辑器 AOT 发布成功，无编译/裁剪警告；日志 `artifacts/aot-publish-96.log`，构建记录 `artifacts/build-96/publish.binlog`。

真实游戏的导出画面及其他平台运行尚待用户验证。所有旧产物和原录制保留。
