using Avalonia.Controls;
using KinokoTAS.App.Controls;
namespace KinokoTAS.App;
public partial class MainWindow {
    bool closePending,documentUnsaved;
    long saveRevision,savedLiveFrames;
    string? savedLiveBranch;
    public bool HasUnsavedChanges {
        get {
            if(Project?.InvalidFrom is not null || dirty || documentUnsaved)return true;
            if(game?.IsLive!=true)return false;
            if(savedLiveBranch!=game.BranchPath)return true;
            // An unreadable progress mailbox must not mark the recording clean
            // or throw out of the UI timer while updating the title.
            try {var state=game.ReadState();return state is null || savedLiveFrames!=state.Completed;}
            catch(IOException){return true;}
            catch(UnauthorizedAccessException){return true;}
        }
    }
    async Task<bool> ConfirmSaveChangesAsync() {
        if(dialogHost?.IsOpen==true)return false;
        if(game?.IsRunning==true){gameKeys.Clear();game.Input(0);await game.PauseAsync(default);}
        if(!HasUnsavedChanges)return true;
        dialogHost??=new ContentDialogHost(this);
        var dialog=new ContentDialog{Title="保存当前录制？",Content=Project?.InvalidFrom is not null?"时间轴有未应用的编辑。保存会先应用并验证，再写入 .krec；失败或取消会保留当前编辑。":"当前录制有未保存的内容。保存为 .krec 后再继续。",PrimaryButtonText=Project?.InvalidFrom is not null?"应用并保存":"保存",SecondaryButtonText="不保存",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close,IsLightDismissEnabled=false};
        var body=Content as Control;var enabled=body?.IsEnabled??true;
        if(body is not null)body.IsEnabled=false;
        ContentDialogResult result;
        try{result=await dialog.ShowAsync(dialogHost);}
        finally{if(body is not null)body.IsEnabled=enabled;}
        if(result==ContentDialogResult.Secondary)return true;
        if(result!=ContentDialogResult.Primary)return false;
        var previous=saveRevision;
        await SaveRecordingAsync();
        return saveRevision!=previous;
    }
}
