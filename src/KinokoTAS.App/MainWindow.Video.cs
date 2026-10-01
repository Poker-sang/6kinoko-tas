using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KinokoTAS.App.Controls;
using KinokoTAS.Core;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private string? _ffmpegExe;
    private CancellationTokenSource? _videoExport;

    public bool IsVideoExporting => _videoExport is not null;

    public int CurrentGameProcessId => _game?.GameProcessId ?? 0;

    private sealed class VideoProgress(Action<VideoExportProgress> report) : IProgress<VideoExportProgress>
    {
        public void Report(VideoExportProgress value) => report(value);
    }

    public void CancelVideoExport() => _videoExport?.Cancel();

    private void CancelVideoExportClick(object? sender, RoutedEventArgs args) => CancelVideoExport();

    private async void ExportVideoClick(object? sender, RoutedEventArgs args)
    {
        if (_videoExport is not null || _dialogHost?.IsOpen == true)
            return;

        try
        {
            var saved = RecordingSavePath;
            if (saved is null || !File.Exists(saved) || !RecordingPackage.IsPackage(saved))
                throw new InvalidOperationException("请先用 Ctrl+S 保存为 .krec，再导出视频。导出使用已保存的录制。");

            var replay = Replay.Load(saved);
            if (replay.Count == 0)
                throw new InvalidOperationException("空录制无法导出视频。");

            _ffmpegExe = File.Exists(_ffmpegExe) ? _ffmpegExe : VideoExporter.FindEncoder();
            if (_ffmpegExe is null)
            {
                var encoder = await StorageProvider.OpenFilePickerAsync(new()
                {
                    Title = "选择 FFmpeg 程序（也可将 ffmpeg 放在 TAS 程序旁）",
                    AllowMultiple = false
                });
                _ffmpegExe = encoder.FirstOrDefault()?.TryGetLocalPath();
                if (_ffmpegExe is null)
                    return;
            }

            if (await PickGame() is null)
                return;

            SaveSettings();
            var range = await ChooseVideoRangeAsync(saved, replay.Count);
            if (range is null)
                return;

            var selected = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "导出无声 MP4",
                SuggestedFileName = Path.GetFileNameWithoutExtension(saved) + ".mp4",
                DefaultExtension = "mp4",
                FileTypeChoices = [new("MP4 视频") { Patterns = ["*.mp4"] }]
            });
            if (selected?.TryGetLocalPath() is not { } output)
                return;

            await ExportVideoToAsync(output, range.Value.First, range.Value.Last, _ffmpegExe);
        }
        catch (OperationCanceledException)
        {
            VideoProgressLabel.Text = "视频导出已取消，原录制和已有视频保留。";
        }
        catch (Exception ex)
        {
            VideoExportBar.IsVisible = true;
            VideoProgressLabel.Text = "视频导出失败：" + ex.Message;
        }
    }

    private async Task<(int First, int Last)?> ChooseVideoRangeAsync(string saved, int count)
    {
        _dialogHost ??= new ContentDialogHost(this);
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
        var body = Content as Control;
        var enabled = body?.IsEnabled ?? true;
        body?.IsEnabled = false;

        try
        {
            if (await dialog.ShowAsync(_dialogHost) != ContentDialogResult.Primary)
                return null;

            return entire.IsChecked == true
                ? (0, count - 1)
                : ((int) (first.Value ?? 0), (int) (last.Value ?? count - 1));
        }
        finally
        {
            body?.IsEnabled = enabled;
        }
    }

    public async Task<VideoExportResult> ExportVideoToAsync(string output, int first, int last, string encoder,
        string? engine = null)
    {
        if (_videoExport is not null)
            throw new InvalidOperationException("已有视频正在导出。");

        var saved = RecordingSavePath ?? throw new InvalidOperationException("请先保存 .krec。");
        var executable = engine ?? _gameExe ?? throw new InvalidOperationException("请先选择游戏程序。");
        using var cancel = new CancellationTokenSource();
        _videoExport = cancel;
        ExportVideoButton.IsEnabled = false;
        VideoExportBar.IsVisible = true;
        CancelVideoExportButton.IsVisible = true;
        try
        {
            var progress = new VideoProgress(p => Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_videoExport, cancel))
                    return;

                VideoProgressLabel.Text =
                    $"{p.Stage} · {p.Completed:N0} / {p.Total:N0} 帧 · {100.0 * p.Completed / p.Total:F0}%";
            }));
            var result = await Task.Run(async () =>
            {
                var package = RecordingPackage.Load(saved);
                var root = Path.Combine(AppContext.BaseDirectory, "video-exports",
                    "export-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(root);
                var initial = Path.Combine(root, "initial");
                package.ExtractInitial(initial);
                var source = Path.Combine(root, "source.krec");
                AtomicFile.Write(source, s => s.Write(package.Replay.Bytes.Span));
                await using var session = new FileGameSession(executable, Path.Combine(root, "session"), source,
                    initial, package.Replay.Identity, false, true);
                return await VideoExporter.ExportAsync(session, encoder, output, first, last, progress, cancel.Token);
            }, cancel.Token);
            VideoProgressLabel.Text = $"已导出 {result.Frames:N0} 帧 · 无声 60 FPS · {result.Path}";
            return result;
        }
        finally
        {
            _videoExport = null;
            ExportVideoButton.IsEnabled = true;
            CancelVideoExportButton.IsVisible = false;
        }
    }
}
