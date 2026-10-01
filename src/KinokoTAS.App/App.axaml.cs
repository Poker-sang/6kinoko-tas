using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace KinokoTAS.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            if (desktop.Args is { Length: > 0 })
                desktop.MainWindow.Opened += async (_, _) =>
                    await ((MainWindow) desktop.MainWindow).OpenPathAsync(desktop.Args[0]);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
