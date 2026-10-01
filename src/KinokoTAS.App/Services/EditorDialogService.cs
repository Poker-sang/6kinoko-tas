using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KinokoTAS.App.Controls;

namespace KinokoTAS.App.Services;

internal sealed class EditorDialogService(Window owner)
{
    private ContentDialogHost? _host;

    public bool IsOpen => _host?.IsOpen == true;

    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (IsOpen)
            return ContentDialogResult.None;

        _host ??= new ContentDialogHost(owner);
        var body = owner.Content as Control;
        var enabled = body?.IsEnabled ?? true;
        body?.IsEnabled = false;
        using var lifetime = new CancellationTokenSource();
        owner.Closed += OnClosed;
        try
        {
            return await dialog.ShowAsync(_host, lifetime.Token);
        }
        finally
        {
            owner.Closed -= OnClosed;
            body?.IsEnabled = enabled;
        }

        void OnClosed(object? sender, EventArgs args) => lifetime.Cancel();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = accept,
            CloseButtonText = cancel,
            DefaultButton = ContentDialogButton.Close,
            IsLightDismissEnabled = false
        };
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    public async Task<string?> PromptNameAsync(string title, string current)
    {
        var input = new TextBox
        {
            Text = current,
            MinWidth = 240
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = input,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            IsLightDismissEnabled = false
        };
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);
        return await ShowAsync(dialog) == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text)
            ? input.Text.Trim()
            : null;
    }

    public Task<ContentDialogResult> AskSaveChangesAsync(bool hasEdits) => ShowAsync(new ContentDialog
    {
        Title = "保存当前录制？",
        Content = hasEdits
            ? "时间轴有未应用的编辑。保存会先应用并验证，再写入 .krec；失败或取消会保留当前编辑。"
            : "当前录制有未保存的内容。保存为 .krec 后再继续。",
        PrimaryButtonText = hasEdits ? "应用并保存" : "保存",
        SecondaryButtonText = "不保存",
        CloseButtonText = "取消",
        DefaultButton = ContentDialogButton.Close,
        IsLightDismissEnabled = false
    });

    public async Task<(int First, int Last)?> ChooseVideoRangeAsync(string saved, int count)
    {
        var entire = new CheckBox
        {
            Content = "整段录制",
            IsChecked = true
        };
        var first = new NumericUpDown
        {
            Minimum = 0,
            Maximum = count - 1,
            Value = 0,
            FormatString = "0",
            IsEnabled = false
        };
        var last = new NumericUpDown
        {
            Minimum = 0,
            Maximum = count - 1,
            Value = count - 1,
            FormatString = "0",
            IsEnabled = false
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{Path.GetFileName(saved)} · {count:N0} 帧\n无声 MP4 · 60 FPS\n导出已保存内容；未保存的编辑和录制不会包含。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(entire);
        panel.Children.Add(new TextBlock { Text = "开始帧 / 结束帧（包含两端）" });
        panel.Children.Add(first);
        panel.Children.Add(last);
        var dialog = new ContentDialog
        {
            Title = "导出视频",
            Content = panel,
            PrimaryButtonText = "选择保存位置",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            IsLightDismissEnabled = false
        };
        entire.IsCheckedChanged += (_, _) =>
        {
            first.IsEnabled = last.IsEnabled = entire.IsChecked != true;
            dialog.IsPrimaryButtonEnabled = entire.IsChecked == true || first.Value <= last.Value;
        };
        first.ValueChanged += (_, _) => dialog.IsPrimaryButtonEnabled = first.Value <= last.Value;
        last.ValueChanged += (_, _) => dialog.IsPrimaryButtonEnabled = first.Value <= last.Value;
        if (await ShowAsync(dialog) != ContentDialogResult.Primary)
            return null;

        return entire.IsChecked == true ? (0, count - 1) : ((int) (first.Value ?? 0), (int) (last.Value ?? count - 1));
    }
}
