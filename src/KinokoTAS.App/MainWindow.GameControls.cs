using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace KinokoTAS.App;

public partial class MainWindow
{
    private uint CurrentMask() => _input.ReadMask(GamePanel.IsFocused);

    private async Task FocusGameWindowAsync()
    {
        if (_game is null)
            return;

        if (!_game.ExternalWindow)
        {
            GamePanel.Focus();
            return;
        }

        GameWindowOrder.GrantActivation(_game.GameProcessId);
        await _game.FocusGameAsync();
        // A bridge acknowledgement confirms the request, not Windows foreground ownership.
        if (OperatingSystem.IsWindows() && !GameWindowOrder.Activate(_game.GameWindowHandle))
            GameStatus.Text = "Windows 未允许切换焦点，请点击游戏窗口。";
    }

    private async void PlayGameClick(object? s, RoutedEventArgs e) => await Operate(async () =>
    {
        RequireAppliedLayout();
        if (_game is null)
            throw new InvalidOperationException("先启动会话。");

        await _game.ResumeAsync(_selectedSpeed, CancellationToken.None);
        await FocusGameWindowAsync();
    });

    private async void PauseGameClick(object? s, RoutedEventArgs e)
    {
        _seeking?.Cancel();
        try
        {
            if (_game is not null)
                await _game.PauseAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            GameStatus.Text = ex.Message;
        }
        finally
        {
            RefreshGameView();
        }
    }

    private async void StepGameClick(object? s, RoutedEventArgs e) => await Operate(async () =>
    {
        RequireAppliedLayout();
        if (_game is not null)
        {
            var mask = CurrentMask();
            await _game.StepAsync([.. Enumerable.Range(0, 19).Select(i => (mask & (1u << i)) != 0)],
                CancellationToken.None);
        }
    });

    private async void TakeoverClick(object? s, RoutedEventArgs e) => await Operate(ToggleRecordingAsync);

    private void GamePointerPressed(object? s, PointerPressedEventArgs e)
    {
        GamePanel.Focus();
        e.Handled = true;
    }

    private void GameKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        _input.Press(e.Key);
        e.Handled = true;
    }

    private void GameKeyUp(object? s, KeyEventArgs e)
    {
        _input.Release(e.Key);
        e.Handled = true;
    }

    private void GameLostFocus(object? s, RoutedEventArgs e)
    {
        _input.Clear();
        _game?.Input(0);
    }

    private async void NewRecordingClick(object? s, RoutedEventArgs e) => await Operate(NewRecordingAsync);
}
