using System;
using Avalonia.Interactivity;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private void Refresh()
    {
        BindLayoutProject();
        RefreshEditWorkflow();
        if (Project is null)
            return;

        DocumentLabel.Text =
            $"{Project.SourceName}  ·  {Project.FrameCount:N0} 帧 / {Project.FrameCount / 60.0:F2} 秒  ·  {Project.EditCount:N0} 处编辑";
        var frame = Math.Clamp(Timeline.SelectedFrame, 0, Math.Max(0, Project.FrameCount - 1));
        Timeline.SelectedFrame = frame;
        FrameLabel.Text = Project.FrameCount == 0 ? "空录制" : frame.ToString("D6");
        if (Project.FrameCount > 0)
        {
            var original = Project.SourceFrame(frame);
            FrameDetails.Text = original < 0
                ? "新增空白帧 · 应用修改后生成校验值"
                : $"时间 {frame / 60.0:F3} 秒\n原始来源帧 {original}\n原始 RNG 前 {Project.Source.RandomBefore(original):X8}\n原始 RNG 后 {Project.Source.RandomAfter(original):X8}\n原始检查值\n{Project.Source.Checkpoint(original):X16}";
        }

        ValidationLabel.Text = Project.InvalidFrom is { } first
            ? $"第 {first:N0} 帧起有编辑。F5 应用到录制；Ctrl+S 应用并保存 .krec。"
            : "点击时间轴输入格修改按键；单击帧号选择，双击查看游戏画面。";
        Timeline.InvalidateVisual();
    }

    private void SelectFrame(int f)
    {
        if (Timeline.FrameCount == 0)
            return;

        f = Math.Clamp(f, 0, Timeline.FrameCount - 1);
        Timeline.SelectedFrame = f;
        JumpFrame.Value = f;
        RangeStart.Value = RangeEnd.Value = f;
        var rows = Math.Max(1,
            (int) ((Timeline.Bounds.Width - TimelineControl.FrameWidth) / TimelineControl.CellWidth));
        if (f < Timeline.FirstFrame || f >= Timeline.FirstFrame + rows)
            FrameScroll.Value = f;

        if (Timeline.LiveMasks is null)
            Refresh();

        else
            Timeline.InvalidateVisual();
    }

    private void EditRange(bool down)
    {
        if (Project is null || _busy || _gameCommand)
            return;

        if (_game?.IsLive == true)
        {
            StatusLabel.Text = "先关闭“录制”开关，再修改已录制的输入。";
            return;
        }

        try
        {
            Project.SetRange((int) (RangeStart.Value ?? 0), (int) (RangeEnd.Value ?? 0), ActionPicker.SelectedIndex,
                down);
            StatusLabel.Text = "区间已更新；点击“应用修改”重新模拟。";
        }
        catch (Exception)
        {
            StatusLabel.Text = "请确认起止帧顺序和动作选择。";
        }
    }

    private void UndoClick(object? s, RoutedEventArgs e)
    {
        if (!_busy && !_gameCommand)
            Project?.Undo();
    }

    private void RedoClick(object? s, RoutedEventArgs e)
    {
        if (!_busy && !_gameCommand)
            Project?.Redo();
    }

    private void HoldClick(object? s, RoutedEventArgs e) => EditRange(true);

    private void ReleaseClick(object? s, RoutedEventArgs e) => EditRange(false);

    private async void PreviousClick(object? s, RoutedEventArgs e)
    {
        FollowLatest.IsChecked = false;
        SelectFrame(Timeline.SelectedFrame - 1);
        await SeekGame(Timeline.SelectedFrame);
    }

    private async void NextClick(object? s, RoutedEventArgs e)
    {
        FollowLatest.IsChecked = false;
        SelectFrame(Timeline.SelectedFrame + 1);
        await SeekGame(Timeline.SelectedFrame);
    }

    private async void JumpClick(object? s, RoutedEventArgs e)
    {
        FollowLatest.IsChecked = false;
        SelectFrame((int) (JumpFrame.Value ?? 0));
        await SeekGame(Timeline.SelectedFrame);
    }

    private void ScrollChanged(object? s, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (Timeline is not null)
        {
            Timeline.FirstFrame = (int) e.NewValue;
            Timeline.InvalidateVisual();
        }
    }
}
