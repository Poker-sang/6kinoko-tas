using Avalonia.Controls;
using KinokoTAS.App.Controls;
namespace KinokoTAS.App;
public partial class MainWindow {
    bool closePending,documentUnsaved;
    long saveRevision,savedLiveFrames;
    string? savedLiveBranch;
    public bool HasUnsavedChanges=>dirty || documentUnsaved ||
        (game?.IsLive==true && (savedLiveBranch!=game.BranchPath || savedLiveFrames!=(game.ReadState()?.Completed??0)));
    async Task<bool> ConfirmSaveChangesAsync() {
        if(dialogHost?.IsOpen==true)return false;
        if(game?.IsRunning==true){gameKeys.Clear();game.Input(0);await game.PauseAsync(default);}
        if(!HasUnsavedChanges)return true;
        dialogHost??=new ContentDialogHost(this);
        var dialog=new ContentDialog{Title="保存当前录制？",Content="当前录制有未保存的内容。",PrimaryButtonText="保存",SecondaryButtonText="不保存",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close,IsLightDismissEnabled=false};
        var body=Content as Control;var enabled=body?.IsEnabled??true;
        if(body is not null)body.IsEnabled=false;
        ContentDialogResult result;
        try{result=await dialog.ShowAsync(dialogHost);}
        finally{if(body is not null)body.IsEnabled=enabled;}
        if(result==ContentDialogResult.Secondary)return true;
        if(result!=ContentDialogResult.Primary)return false;
        var previous=saveRevision;
        if(Project?.InvalidFrom is not null)await SaveProject(true);
        else await SaveRecordingAsync();
        return saveRevision!=previous;
    }
}
