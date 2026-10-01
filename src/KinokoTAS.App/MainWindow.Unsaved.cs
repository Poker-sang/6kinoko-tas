using System;
using System.IO;
using System.Threading.Tasks;
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
        if (_dialogs.IsOpen)
            return false;

        await PauseForDialogAsync();
        if (!HasUnsavedChanges)
            return true;

        var result = await _dialogs.AskSaveChangesAsync(Project?.InvalidFrom is not null);
        if (result == ContentDialogResult.Secondary)
            return true;

        if (result != ContentDialogResult.Primary)
            return false;

        var previous = _saveRevision;
        await SaveRecordingAsync();
        return _saveRevision != previous;
    }
}
