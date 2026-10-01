using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KinokoTAS.App.Controls;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private ContentDialogHost? _dialogHost;

    private async Task<string?> PromptNameAsync(string title, string current)
    {
        if (_dialogHost?.IsOpen == true)
            return null;

        if (_game?.IsRunning == true)
        {
            _gameKeys.Clear();
            _game.Input(0);
            await _game.PauseAsync(CancellationToken.None);
        }

        _dialogHost ??= new ContentDialogHost(this);
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
        var body = Content as Control;
        var previous = body?.IsEnabled ?? true;
        body?.IsEnabled = false;

        using var lifetime = new CancellationTokenSource();
        Closed += OnClosed;
        try
        {
            return await dialog.ShowAsync(_dialogHost, lifetime.Token) == ContentDialogResult.Primary &&
                   !string.IsNullOrWhiteSpace(input.Text)
                ? input.Text.Trim()
                : null;
        }
        finally
        {
            Closed -= OnClosed;
            body?.IsEnabled = previous;
        }

        void OnClosed(object? sender, EventArgs args) => lifetime.Cancel();
    }

    public async Task<bool> ConfirmContentAsync(string title, string message, string accept, string cancel)
    {
        if (_dialogHost?.IsOpen == true)
            return false;

        if (_game?.IsRunning == true)
        {
            _gameKeys.Clear();
            _game.Input(0);
            await _game.PauseAsync(CancellationToken.None);
        }

        _dialogHost ??= new ContentDialogHost(this);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = accept,
            CloseButtonText = cancel,
            DefaultButton = ContentDialogButton.Close,
            IsLightDismissEnabled = false
        };
        var body = Content as Control;
        var previous = body?.IsEnabled ?? true;
        body?.IsEnabled = false;

        using var lifetime = new CancellationTokenSource();
        Closed += OnClosed;
        try
        {
            return await dialog.ShowAsync(_dialogHost, lifetime.Token) == ContentDialogResult.Primary;
        }
        finally
        {
            Closed -= OnClosed;
            body?.IsEnabled = previous;
        }

        void OnClosed(object? sender, EventArgs args) => lifetime.Cancel();
    }
}
