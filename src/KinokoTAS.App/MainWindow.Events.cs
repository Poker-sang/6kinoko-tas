using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private void OnTimelineCellClicked(int frame, int action)
    {
        if (_busy || _gameCommand)
            return;

        SelectFrame(frame);
        FollowLatest.IsChecked = false;
        if (action >= 0)
            if (Timeline.LiveMasks is null)
                Project?.SetRange(frame, frame, action, !Project.Down(frame, action));

            else
                StatusLabel.Text = "先关闭“录制”开关，再修改已录制的输入。";
    }

    private async void OnTimelineFrameActivated(int frame)
    {
        if (!_busy)
            await SeekGame(frame);
    }

    private void OnTimelineBookmarkRequested(int frame)
    {
        if (_busy || _gameCommand)
            return;

        try
        {
            AddBookmarkAt(frame, BookmarkName.Text ?? "");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
    }

    private void OnTimelineScrolled(int delta)
    {
        FollowLatest.IsChecked = false;
        FrameScroll.Value = Math.Clamp(FrameScroll.Value + delta, 0, FrameScroll.Maximum);
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
            return;

        if (_videoExport is not null)
        {
            e.Cancel = true;
            _videoExport.Cancel();
            StatusLabel.Text = "正在取消视频导出，请稍后再次关闭。";
            return;
        }

        if (_closePending)
        {
            e.Cancel = true;
            return;
        }

        if (_gameCommand || _busy)
        {
            e.Cancel = true;
            _seeking?.Cancel();
            StatusLabel.Text = "正在结束当前操作，请稍后再次关闭。";
            return;
        }

        e.Cancel = true;
        _closePending = true;
        try
        {
            Timeline.FinishPainting();
            if (!await ConfirmSaveChangesAsync())
                return;

            await EndGame(false);
            _allowClose = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(Close);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "退出失败：" + ex.Message + "；会话文件已保留。";
        }
        finally
        {
            _closePending = false;
        }
    }

    private async void OnDocumentShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _dialogs.IsOpen || _busy || _gameCommand)
            return;

        if (e.Source is TextBox || e.Source is NumericUpDown)
            return;

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        if (e.Key == Key.O)
        {
            e.Handled = true;
            await OpenPicker();
        }

        if (e.Key == Key.S)
        {
            e.Handled = true;
            await Operate(() => SaveRecordingAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift)));
        }

        if (e.Key == Key.Z)
        {
            e.Handled = true;
            Project?.Undo();
        }

        if (e.Key == Key.Y)
        {
            e.Handled = true;
            Project?.Redo();
        }
    }
}
