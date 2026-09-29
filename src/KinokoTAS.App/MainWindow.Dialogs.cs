using Avalonia.Controls;
using KinokoTAS.App.Controls;
namespace KinokoTAS.App;
public partial class MainWindow {
    ContentDialogHost? dialogHost;
    public async Task<bool> ConfirmContentAsync(string title,string message,string accept,string cancel) {
        if(dialogHost?.IsOpen==true)return false;
        if(game?.IsRunning==true){gameKeys.Clear();game.Input(0);await game.PauseAsync(default);}
        dialogHost??=new ContentDialogHost(this);
        var dialog=new ContentDialog{Title=title,Content=message,PrimaryButtonText=accept,CloseButtonText=cancel,DefaultButton=ContentDialogButton.Close,IsLightDismissEnabled=false};
        var body=Content as Control;var previous=body?.IsEnabled??true;
        if(body is not null)body.IsEnabled=false;
        using var lifetime=new CancellationTokenSource();
        void OnClosed(object? sender,EventArgs args)=>lifetime.Cancel();
        Closed+=OnClosed;
        try{return await dialog.ShowAsync(dialogHost,lifetime.Token)==ContentDialogResult.Primary;}
        finally {Closed-=OnClosed;if(body is not null)body.IsEnabled=previous;}
    }
}
