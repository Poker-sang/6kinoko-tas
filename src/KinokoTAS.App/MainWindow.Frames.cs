using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KinokoTAS.Core;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private static readonly double[] _Speeds = [0.25, 0.5, 0.75, 1, 2, 4];
    private TasProject? _paintingProject, _layoutProject;
    private bool _paintingDown;
    private bool _changingSpeed;
    private bool _frameToolsReady;
    private double _selectedSpeed = 1;

    private readonly Dictionary<TasProject, Dictionary<long, (FrameBookmark[] Forward, FrameBookmark[] Backward)>>
        _bookmarkLayouts = [];

    private void InitializeFrameTools()
    {
        _frameToolsReady = true;
        Timeline.BeginPainting = (frame, action) =>
        {
            if (_busy || _gameCommand || Project is null || Timeline.LiveMasks is not null)
                return false;

            _paintingProject = Project;
            _paintingDown = !Project.Down(frame, action);
            Project.BeginPaint();
            return true;
        };
        Timeline.PaintRange += (first, last, action) => _paintingProject?.SetRange(first, last, action, _paintingDown);
        Timeline.PaintCompleted += () =>
        {
            _paintingProject?.EndPaint();
            _paintingProject = null;
        };
    }

    private void BindLayoutProject()
    {
        if (ReferenceEquals(Project, _layoutProject))
            return;

        _layoutProject?.LayoutChanged -= OnFrameLayoutChanged;

        _layoutProject = Project;
        _layoutProject?.LayoutChanged += OnFrameLayoutChanged;
    }

    private void OnFrameLayoutChanged(FrameLayoutChange change)
    {
        if (Project is null)
            return;

        if (!_bookmarkLayouts.TryGetValue(Project, out var history))
        {
            history = [];
            _bookmarkLayouts.Add(Project, history);
        }

        history.TryGetValue(change.Id, out var saved);
        var removed = change.Forward ? change.Removed : change.Inserted;
        var inserted = change.Forward ? change.Inserted : change.Removed;
        var lost = _bookmarks.Where(mark => mark.Frame >= change.First && mark.Frame < change.First + removed)
            .ToArray();
        var retained = _bookmarks.Except(lost).Select(mark =>
                mark.Frame >= change.First + removed ? mark with { Frame = mark.Frame + inserted - removed } : mark)
            .ToList();
        retained.AddRange((change.Forward ? saved.Backward : saved.Forward) ?? []);
        history[change.Id] = change.Forward ? (lost, saved.Backward ?? []) : (saved.Forward ?? [], lost);
        _bookmarks.Clear();
        foreach (var mark in retained.OrderBy(mark => mark.Frame))
            _bookmarks.Add(mark);

        var maximum = Math.Max(0, Project.FrameCount - 1);
        FrameScroll.Maximum = maximum;
        JumpFrame.Maximum = RangeStart.Maximum = RangeEnd.Maximum = maximum;
        Timeline.SelectedFrame = Math.Clamp(Timeline.SelectedFrame, 0, Math.Max(0, Project.FrameCount - 1));
    }

    private bool CanEditFrames()
    {
        if (Project is null || _busy || _gameCommand)
            return false;

        if (_game?.IsLive == true)
        {
            StatusLabel.Text = "先关闭录制开关，再编辑帧。";
            return false;
        }

        Timeline.FinishPainting();
        return true;
    }

    private void RequireAppliedLayout()
    {
        if (Project?.InvalidFrom is not null)
            throw new InvalidOperationException("时间轴编辑尚未应用。请点“应用到录制”（F5），或 Ctrl+S 应用并保存，再查看修改后的画面。");
    }

    public void InsertEmptyFrames(int before, int count)
    {
        if (!CanEditFrames())
            return;

        BindLayoutProject();
        Project!.InsertFrames(before, count);
        FollowLatest.IsChecked = false;
        SelectFrame(before);
        StatusLabel.Text = $"已插入 {count} 帧空白输入；应用修改后生效。";
    }

    public void DeleteFrameRange(int first, int last)
    {
        if (!CanEditFrames())
            return;

        BindLayoutProject();
        Project!.DeleteFrames(first, checked(last - first + 1));
        FollowLatest.IsChecked = false;
        SelectFrame(first);
        StatusLabel.Text = "已删除选定帧；应用修改后生效，Ctrl+Z 可撤销。";
    }

    private void InsertFramesClick(object? sender, RoutedEventArgs args)
    {
        try
        {
            InsertEmptyFrames(Timeline.SelectedFrame, (int) (InsertCount.Value ?? 1));
        }
        catch (Exception error)
        {
            StatusLabel.Text = error.Message;
        }
    }

    private void AppendFramesClick(object? sender, RoutedEventArgs args)
    {
        try
        {
            if (Project is not null)
                InsertEmptyFrames(Project.FrameCount, (int) (InsertCount.Value ?? 1));
        }
        catch (Exception error)
        {
            StatusLabel.Text = error.Message;
        }
    }

    private void DeleteFramesClick(object? sender, RoutedEventArgs args)
    {
        try
        {
            DeleteFrameRange((int) (RangeStart.Value ?? 0), (int) (RangeEnd.Value ?? 0));
        }
        catch (Exception error)
        {
            StatusLabel.Text = error.Message;
        }
    }

    private async void SpeedChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!_frameToolsReady || _changingSpeed || sender is not ComboBox picker || picker.SelectedIndex < 0)
            return;

        double previous = _selectedSpeed, next = _Speeds[picker.SelectedIndex];
        if (_gameCommand || _busy)
        {
            _changingSpeed = true;
            picker.SelectedIndex = Array.IndexOf(_Speeds, previous);
            _changingSpeed = false;
            return;
        }

        await Operate(async () =>
        {
            try
            {
                if (_game is not null)
                    await _game.SetSpeedAsync(next);

                _selectedSpeed = next;
                StatusLabel.Text = $"播放/录制速度 {next:0.##}×；模拟时间保持每秒 60 帧。";
            }
            catch
            {
                _changingSpeed = true;
                picker.SelectedIndex = Array.IndexOf(_Speeds, previous);
                _changingSpeed = false;
                throw;
            }
        });
    }
}
