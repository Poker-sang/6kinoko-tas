# 代码格式

C# 按根目录 `.editorconfig` 使用 Rider MCP 格式化；AXAML 使用 XAML Styler 及 `Settings.XamlStyler`。范围包括应用、核心、自动检查和 RecordingRecovery 工具；生成文件与构建产物不参与。

单语句的条件或循环可以省略大括号，但语句体另起一行。多语句块、catch 及空块的大括号和语句体分别换行。只有不改变语法绑定、变量作用域时才能省略大括号。

自动属性保持单行，包括 `private set`。包含箭头访问器的属性展开，每个访问器各占一行，例如：

```csharp
public int Frame { get; private set; }

public object? Title
{
    get => GetValue(TitleProperty);
    set => SetValue(TitleProperty, value);
}

if (state is null)
    return;
```

`ImplicitUsings` 在 `Directory.Build.props` 中全局关闭，所需命名空间写在各源文件中。

新增 Rider 配置项及取值根据 JetBrains 官方文档核对：

- [换行和访问器布局](https://www.jetbrains.com/help/resharper/EditorConfig_CSHARP_LineBreaksPageSchema.html)
- [控制语句大括号偏好](https://www.jetbrains.com/help/resharper/EditorConfig_CSHARP_CSharpCodeStylePageImplSchema.html)

AXAML 格式检查：`xstyler -f <file.axaml> -i -c Settings.XamlStyler -p`。

## 2026-10-01 验证

- 普通 Release 构建：零警告、零错误，日志 `artifacts/build-113.log`。
- 4 个 AXAML 文件的 XAML Styler 格式检查通过。
- Windows x64 NativeAOT 自动回归全部通过，日志 `artifacts/checks-aot-114.log`。检查使用合成游戏进程，不代表真实游戏游玩验证。
- NativeAOT 编辑器发布成功，日志 `artifacts/aot-publish-115.log`；程序位于 `artifacts/windows-editor-44-aot/6kinokoTAS.App.exe`。
- `UpdatePlaybackButtons` 使用显式非空状态模式；Rider 错误检查通过。

本轮历史整理为单个提交；先前本地提交保留在备份分支 `codex/format-before-squash-20261001-112`。原录制及所有旧构建产物保留。
