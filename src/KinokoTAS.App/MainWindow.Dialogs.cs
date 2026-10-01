using System.Threading;
using System.Threading.Tasks;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private async Task PauseForDialogAsync()
    {
        if (_game?.IsRunning != true)
            return;

        _input.Clear();
        _game.Input(0);
        await _game.PauseAsync(CancellationToken.None);
    }

    private async Task<string?> PromptNameAsync(string title, string current)
    {
        if (_dialogs.IsOpen)
            return null;

        await PauseForDialogAsync();
        return await _dialogs.PromptNameAsync(title, current);
    }

    public async Task<bool> ConfirmContentAsync(string title, string message, string accept, string cancel)
    {
        if (_dialogs.IsOpen)
            return false;

        await PauseForDialogAsync();
        return await _dialogs.ConfirmAsync(title, message, accept, cancel);
    }
}
