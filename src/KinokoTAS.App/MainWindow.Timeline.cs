using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using KinokoTAS.Core;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private LiveTimeline _liveTimeline = new();
    private string? _timelineSource;
    private bool _followScroll;

    private void UpdatePlaybackProject()
    {
        if (_game?.PlaybackSource is not { } path || _timelineSource == path)
            return;

        var replay = Replay.Load(path);
        // Preserve a loaded project's draft when connecting its unchanged source.
        if (Project is null || !Project.Source.Bytes.Span.SequenceEqual(replay.Bytes.Span))
        {
            Project?.Changed -= OnChanged;

            Project = new TasProject(replay, "当前录制.krec");
            Project.Changed += OnChanged;
            _dirty = false;
            Timeline.Project = Project;
        }

        _sourcePath = path;
        _timelineSource = path;
        Timeline.LiveMasks = null;
        SaveButton.IsEnabled = ExportButton.IsEnabled = true;
        HoldButton.IsEnabled = ReleaseButton.IsEnabled = replay.Count > 0;
        Refresh();
    }

    private void RefreshTimeline(SessionState state)
    {
        if (_game is null)
            return;

        if (_game.IsLive)
        {
            if (_savedLiveBranch != _game.BranchPath || _savedLiveFrames != state.Completed)
                _documentUnsaved = true;

            _liveTimeline.Read(_game.BranchPath, state.Completed);
            Timeline.LiveMasks = _liveTimeline.Masks;
        }
        else
            UpdatePlaybackProject();

        Timeline.Playhead = checked((int) state.Completed - 1);
        Timeline.Bookmarks = _bookmarks;
        var count = Timeline.FrameCount;
        JumpFrame.Maximum = RangeStart.Maximum = RangeEnd.Maximum = Math.Max(0, count - 1);
        FrameScroll.Maximum = Math.Max(0, count - 1);
        FrameScroll.ViewportSize =
            Math.Max(1, (Timeline.Bounds.Width - TimelineControl.FrameWidth) / TimelineControl.CellWidth);
        if (FollowLatest.IsChecked == true)
        {
            var target = _game.IsLive ? count - 1 : Timeline.Playhead;
            var visible = Math.Max(1, (int) FrameScroll.ViewportSize);
            if (target < Timeline.FirstFrame || target >= Timeline.FirstFrame + visible - 4)
            {
                _followScroll = true;
                FrameScroll.Value = Math.Max(0, target - visible + 5);
                _followScroll = false;
            }
        }

        DocumentLabel.Text = $"当前录制 · {count:N0} 帧 · 红线：当前帧 · 金色：书签 · 暗色：未录制";
        Timeline.InvalidateVisual();
    }

    public async Task ToggleRecordingAsync()
    {
        if (_game is null)
            throw new InvalidOperationException("先新建或打开录制。");

        if (_game.IsLive)
        {
            _documentUnsaved = HasUnsavedChanges;
            using var cancel = new CancellationTokenSource();
            _seeking = cancel;
            try
            {
                await _game.SwitchToPlaybackAsync(cancel.Token);
                UpdatePlaybackProject();
            }
            finally
            {
                _seeking = null;
            }
        }
        else
        {
            if (Project?.InvalidFrom is not null)
                throw new InvalidOperationException("请先应用输入修改，或撤销草稿后再接管录制。");

            await _game.PauseAsync(CancellationToken.None);
            var recovery = CaptureRecovery();
            await _game.TakeoverAsync();
            _documentUnsaved = true;
            _recoveries.Push(recovery);
            var boundary = _game.ReadState()!.Completed;
            foreach (var mark in _bookmarks.Where(m => m.Frame >= boundary).ToArray())
                _bookmarks.Remove(mark);

            _liveTimeline = new();
            _timelineSource = null;
            FollowLatest.IsChecked = true;
            await _game.ResumeAsync(_selectedSpeed, CancellationToken.None);
        }

        RecordToggle.IsChecked = _game.IsLive;
        RefreshGameView();
        if (_game.IsLive)
        {
            _gameKeys.Clear();
            _game.Input(0);
            await FocusGameWindowAsync();
        }
    }

    public async Task ReplayAllAsync()
    {
        RequireAppliedLayout();
        if (_game is null)
            throw new InvalidOperationException("先新建或打开录制。");

        await _game.ReplayAllAsync();
        UpdatePlaybackProject();
        FollowLatest.IsChecked = true;
        RefreshGameView();
    }

    public async Task RestartGameAsync()
    {
        if (Project?.InvalidFrom is not null)
            throw new InvalidOperationException("请用时间轴上的“应用到录制”，或 Ctrl+S 应用并保存编辑。");

        if (_game is null)
        {
            await LaunchGame(false);
            return;
        }

        using var cancel = new CancellationTokenSource();
        _seeking = cancel;
        _seekTarget = _game.ReadState()?.Completed ?? 1;
        try
        {
            GameStatus.Text = "重新启动游戏，正在恢复位置…";
            await _game.RestartAsync(cancel.Token);
            _timelineSource = null;
            _previewCount = -1;
            _orderedGameWindow = 0;
            UpdatePlaybackProject();
            RefreshGameView();
        }
        finally
        {
            _seeking = null;
        }
    }

    private async void RestartGameClick(object? s, RoutedEventArgs e) => await Operate(RestartGameAsync);

    private async void ReplayAllClick(object? s, RoutedEventArgs e) => await Operate(ReplayAllAsync);
}
