using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KinokoTAS.App.Controls;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private bool _closePending, _documentUnsaved;
    private long _saveRevision, _savedLiveFrames;
    private string? _savedLiveBranch;

    public bool HasUnsavedChanges
    {
        get
        {
            if (Project?.InvalidFrom is not null || _dirty || _documentUnsaved)
                return true;

            if (_game?.IsLive != true)
                return false;

            if (_savedLiveBranch != _game.BranchPath)
                return true;

            // An unreadable progress mailbox must not mark the recording clean
            // or throw out of the UI timer while updating the title.
            try
            {
                var state = _game.ReadState();
                return state is null || _savedLiveFrames != state.Completed;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
    }

    private async Task<bool> ConfirmSaveChangesAsync()
    {
        if (_dialogHost?.IsOpen == true)
            return false;

        if (_game?.IsRunning == true)
        {
            _gameKeys.Clear();
            _game.Input(0);
            await _game.PauseAsync(CancellationToken.None);
        }

        if (!HasUnsavedChanges)
            return true;

        _dialogHost ??= new ContentDialogHost(this);
        var dialog = new ContentDialog
        {
            Title = "保存当前录制？",
            Content =
                Project?.InvalidFrom is not null
                    ? "时间轴有未应用的编辑。保存会先应用并验证，再写入 .krec；失败或取消会保留当前编辑。"
                    : "当前录制有未保存的内容。保存为 .krec 后再继续。",
            PrimaryButtonText = Project?.InvalidFrom is not null ? "应用并保存" : "保存",
            SecondaryButtonText = "不保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            IsLightDismissEnabled = false
        };
        var body = Content as Control;
        var enabled = body?.IsEnabled ?? true;
        body?.IsEnabled = false;

        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync(_dialogHost);
        }
        finally
        {
            body?.IsEnabled = enabled;
        }

        if (result == ContentDialogResult.Secondary)
            return true;

        if (result != ContentDialogResult.Primary)
            return false;

        var previous = _saveRevision;
        await SaveRecordingAsync();
        return _saveRevision != previous;
    }
}
