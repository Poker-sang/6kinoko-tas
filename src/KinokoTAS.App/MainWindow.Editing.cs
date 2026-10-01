using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Interactivity;
using KinokoTAS.Core;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private sealed record Recovery(string Path, int Frame, FrameBookmark[] Marks, TasProject? Draft);

    private readonly Stack<Recovery> _recoveries = [];
    private string? _operationProgress;
    private long _seekTarget;

    private sealed class CallbackProgress(Action<SimulationProgress> callback) : IProgress<SimulationProgress>
    {
        public void Report(SimulationProgress value) => callback(value);
    }

    private Recovery CaptureRecovery() => new(_game!.PlaybackSource ?? throw new InvalidOperationException("先完成当前录制。"),
        Math.Max(0, checked((int) (_game.ReadState()?.Completed ?? 1) - 1)), [.. _bookmarks], Project);

    private void AdoptSession(FileGameSession session)
    {
        _game = session;
        _timelineSource = null;
        _liveTimeline = new();
        _previewCount = -1;
        _orderedGameWindow = 0;
        GameImage.IsVisible = !session.ExternalWindow;
        ExternalHint.IsVisible = session.ExternalWindow;
        EmbeddedOption.IsEnabled = false;
        UpdatePlaybackProject();
        _bookmarkFile = BookmarkCache(Project!.Source);
        Refresh();
        RefreshGameView();
    }

    public async Task ApplyEditsAsync()
    {
        if (Project?.InvalidFrom is null)
        {
            StatusLabel.Text = "时间轴没有待应用的编辑。Ctrl+S 保存录制。";
            return;
        }

        if (_game is null)
        {
            await LaunchGame(false);
            if (_game is null)
                throw new OperationCanceledException("已取消连接游戏，编辑尚未应用。");
        }

        if (_game.IsLive)
            throw new InvalidOperationException("先关闭“录制”开关，再编辑已录制的输入。");

        var selectedFrame = Math.Clamp(Timeline.SelectedFrame, 0, Project.FrameCount - 1);
        var draft = Project;
        var old = _game;
        if (old.IsRunning)
            await old.PauseAsync(CancellationToken.None);

        var recovery = CaptureRecovery();
        using var cancel = new CancellationTokenSource();
        _seeking = cancel;
        _busy = true;
        try
        {
            var next = await old.ResimulateAsync(draft, new CallbackProgress(p =>
            {
                _operationProgress =
                    $"{p.Stage}：{p.Completed:N0} / {p.Total:N0} 帧（{100.0 * p.Completed / Math.Max(1, p.Total):F0}%）";
                GameStatus.Text = _operationProgress;
                EditWorkflowLabel.Text = _operationProgress;
            }), cancel.Token);
            try
            {
                await next.SeekAsync(selectedFrame, cancel.Token);
            }
            catch
            {
                await next.DisposeAsync();
                throw;
            }

            // Only adopt the result after a second complete, checkpoint-verified playback.
            await old.DisposeAsync();
            _recoveries.Push(recovery);
            AdoptSession(next);
            PersistBookmarks();
            SelectFrame(selectedFrame);
            _dirty = false;
            StatusLabel.Text = "编辑已应用并验证，已返回所选帧；尚未保存到文件，按 Ctrl+S 保存 .krec。";
        }
        finally
        {
            _busy = false;
            _seeking = null;
            _operationProgress = null;
            RefreshGameView();
        }
    }

    public async Task RestoreOverwriteAsync()
    {
        if (_game is null || !_recoveries.TryPeek(out var saved))
            throw new InvalidOperationException("没有可恢复的覆盖。");

        var old = _game;
        if (old.IsRunning)
            await old.PauseAsync(CancellationToken.None);

        using var cancel = new CancellationTokenSource();
        _seeking = cancel;
        _busy = true;
        _seekTarget = saved.Frame + 1;
        var restored = old.CreatePlaybackSession(saved.Path);
        try
        {
            restored.Progress += Report;
            await restored.StartAsync(cancel.Token);
            await restored.SeekAsync(saved.Frame, cancel.Token);
            restored.Progress -= Report;
            await old.DisposeAsync();
            _recoveries.Pop();
            AdoptSession(restored);
            _bookmarks.Clear();
            foreach (var mark in saved.Marks)
                _bookmarks.Add(mark);

            if (saved.Draft is not null && saved.Draft.Source.Bytes.Span.SequenceEqual(Project!.Source.Bytes.Span))
            {
                Project.Changed -= OnChanged;
                Project = saved.Draft;
                Project.Changed += OnChanged;
                Timeline.Project = Project;
                _dirty = Project.EditCount > 0;
            }

            PersistBookmarks();
            Refresh();
            StatusLabel.Text = "已恢复上次覆盖前的录制、位置、书签及输入草稿；被替换的录制仍保留在会话目录。";
        }
        catch
        {
            await restored.DisposeAsync();
            throw;
        }
        finally
        {
            _busy = false;
            _seeking = null;
            _operationProgress = null;
            RefreshGameView();
        }

        return;

        void Report(SessionState s)
        {
            _operationProgress = $"恢复旧版本：{s.Completed:N0} / {_seekTarget:N0} 帧";
            GameStatus.Text = _operationProgress;
        }
    }

    private async void ApplyEditsClick(object? sender, RoutedEventArgs e) => await Operate(ApplyEditsAsync);

    private async void RestoreOverwriteClick(object? sender, RoutedEventArgs e) => await Operate(RestoreOverwriteAsync);

    private void CancelOperationClick(object? sender, RoutedEventArgs e) => _seeking?.Cancel();

    public void CancelCurrentOperation() => _seeking?.Cancel();

    private void RefreshEditWorkflow()
    {
        var pending = Project?.InvalidFrom is not null;
        var available = !_gameCommand && !_busy && _seeking is null;
        ApplyEditsButton.IsEnabled = pending && available && _game?.IsLive != true;
        SaveRecordingButton.Label = pending ? "应用并保存录制" : "保存录制";
        SaveRecordingButton.IsEnabled = available && (Project is not null || _game is not null);
        UndoButton.IsEnabled = available && Project?.CanUndo == true;
        RedoButton.IsEnabled = available && Project?.CanRedo == true;
        var unsaved = HasUnsavedChanges;
        var filename = RecordingSavePath is not null ? System.IO.Path.GetFileName(RecordingSavePath)
            : Project is not null || _game is not null ? "未命名录制.krec" : null;
        Title = filename is null ? "6kinoko TAS" : $"{filename}{(unsaved ? " *" : "")} — 6kinoko TAS";
        SavedPathLabel.Text = RecordingSavePath is null
            ? "录制尚未保存 · Ctrl+S 选择 .krec 路径"
            : $"{(unsaved ? "有未保存更改" : "当前录制")}：{RecordingSavePath}";
        EditWorkflowLabel.Text = _operationProgress ?? (pending
            ? $"待应用 · {Project!.EditCount:N0} 处编辑 · F5 应用到录制，Ctrl+S 应用并保存 .krec"
            : _game is null && Project is null
                ? "点击输入格编辑按键；F5 应用到录制，Ctrl+S 保存 .krec"
                : unsaved
                    ? "录制未保存 · Ctrl+S 保存 .krec（包含重点和初始存档）"
                    : "录制已保存 · 时间轴没有待应用的编辑");
    }

    private async void EditorShortcut(object? sender, KeyEventArgs e)
    {
        if (_dialogHost?.IsOpen == true)
            return;

        switch (e.Key)
        {
            // Function keys work even when preview owns focus; text-editing keys stay local.
            case Key.Escape when _seeking is not null:
                e.Handled = true;
                _seeking.Cancel();
                return;
            case Key.F9:
                e.Handled = true;
                await Operate(async () =>
                {
                    if (_game is null)
                        return;

                    if (_game.ReadState()?.Phase.EndsWith("paused") == true)
                    {
                        RequireAppliedLayout();
                        await _game.ResumeAsync(_selectedSpeed, CancellationToken.None);
                    }
                    else
                        await _game.PauseAsync(CancellationToken.None);
                });
                break;
            case Key.F10:
                e.Handled = true;
                StepGameClick(sender, e);
                break;
            case Key.F8:
                e.Handled = true;
                await Operate(ToggleRecordingAsync);
                break;
            case Key.F6:
                e.Handled = true;
                await Operate(() => AddBookmarkAsync(BookmarkName.Text ?? ""));
                break;
            case Key.F5:
                e.Handled = true;
                await Operate(ApplyEditsAsync);
                break;
            default:
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
                    e.Key == Key.Z)
                {
                    e.Handled = true;
                    await Operate(RestoreOverwriteAsync);
                }

                break;
            }
        }
    }
}
