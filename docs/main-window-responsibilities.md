# MainWindow 职责

窗口负责控件更新、用户命令和录制事务之间的协调。`MainWindow.axaml.cs` 是组合入口：创建服务并连接具名事件处理器。XAML 处理器和已有公开接口仍由窗口提供。

| 独立类 | 负责内容 |
| --- | --- |
| `EditorSettingsStore` | 配置路径迁移、源生成 JSON 读写 |
| `GameInputState` | 内嵌操作的按键状态及输入掩码映射 |
| `GamePreviewPresenter` | RGBA 画面显示、去重、位图重建及释放 |
| `EditorDialogService` | 对话框宿主、背景禁用恢复、生命周期取消、命名及保存提示、视频区间选择 |
| `RecordingWorkspace` | 独立会话目录、初始存档提取与验证、创建录制和回放会话、视频导出的保存副本 |
| `BookmarkLayoutHistory` | 插入、删除、撤销和重做时的书签位移及恢复 |

窗口的 partial 文件按 UI 工作流组织：Documents 处理打开和保存；Events 处理启动事件、快捷键及关闭；FrameCommands 处理帧选择和区间操作；GameControls 处理播放、暂停和输入事件；GameView 更新游戏状态。原来的 Game 文件负责会话协调，Editing 负责应用验证与恢复的 UI 流程。

游戏桥接与重播验证继续使用 `FileGameSession`。保存流程先完成修改的模拟和验证，再写入录制包，成功后才清除未保存状态。视频导出使用保存文件的独立副本。

`WorkspaceChecks` 直接检查存档提取隔离、源文件保留、草稿加载、会话准备、初始存档篡改检测和独立视频导出。窗口行为仍由现有 NativeAOT 无界面回归覆盖。

本轮验证：普通构建 `artifacts/build-120.log` 零警告、零错误；NativeAOT 回归 `artifacts/checks-aot-123.log` 全部通过；编辑器 AOT 发布 `artifacts/aot-publish-122.log` 成功，程序位于 `artifacts/windows-editor-45-aot/6kinokoTAS.App.exe`。自动检查使用合成游戏，真实游戏游玩仍由用户验证。本轮职责拆分与忽略规则修正一并提交。
