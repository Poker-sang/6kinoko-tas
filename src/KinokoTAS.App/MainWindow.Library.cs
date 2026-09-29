using Avalonia.Controls; using Avalonia.Interactivity; using Avalonia.Platform.Storage;
using KinokoTAS.Core; using System.Collections.ObjectModel; using System.Diagnostics;
namespace KinokoTAS.App;
public partial class MainWindow {
    readonly ObservableCollection<FrameBookmark> bookmarks=[];
    string? bookmarkFile,lastSaved;
    void InitializeLibrary(){BookmarkList.ItemsSource=bookmarks;UpdateGamePath();}
    void UpdateGamePath(){GamePathLabel.Text=gameExe is null?"当前游戏：尚未选择（首次开始时选择一次）":"当前游戏："+gameExe;ToolTip.SetTip(GamePathLabel,gameExe);}
    void ShowSaved(string path){lastSaved=path;SavedPathLabel.Text="已保存："+path;OpenSavedButton.IsEnabled=true;}
    string BookmarkCache(Replay replay)=>Path.Combine(Path.GetDirectoryName(SettingsPath)!,"bookmarks",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(replay.Bytes.Span))+".json");
    void LoadBookmarksFor(Replay replay,string path) {
        bookmarkFile=BookmarkCache(replay);bookmarks.Clear();
        var stored=File.Exists(bookmarkFile)?bookmarkFile:path+".bookmarks.json";
        foreach(var mark in RecordingLibrary.LoadBookmarks(stored,replay.Count))bookmarks.Add(mark);
    }
    void PersistBookmarks(){if(bookmarkFile is not null)RecordingLibrary.SaveBookmarks(bookmarkFile,bookmarks);}
    public async Task AddBookmarkAsync(string name) {
        int frame;
        if(game is not null){await game.PauseAsync(default);frame=checked((int)(game.ReadState()!.Completed-1));}
        else if(Project?.Source.Count>0)frame=Timeline.SelectedFrame;
        else throw new InvalidOperationException("先新建或打开录制。");
        if(frame<0)throw new InvalidOperationException("请等待首帧完成。");
        var mark=new FrameBookmark(frame,string.IsNullOrWhiteSpace(name)?$"重点 {bookmarks.Count+1}":name.Trim());
        bookmarks.Add(mark);PersistBookmarks();BookmarkList.SelectedItem=mark;StatusLabel.Text=$"已添加书签：{mark}（已暂停）";
    }
    async void AddBookmarkClick(object? s,RoutedEventArgs e)=>await Operate(()=>AddBookmarkAsync(BookmarkName.Text??""));
    async void ReturnBookmarkClick(object? s,RoutedEventArgs e)=>await Operate(ReturnSelectedBookmarkAsync);
    public async Task ReturnSelectedBookmarkAsync(){
        if(BookmarkList.SelectedItem is not FrameBookmark mark)return;
        if(game?.IsLive==true){
            if(dirty && !await ConfirmDiscard())return;dirty=false;
            var live=game;await EndGame(true);await AttachGameSessionAsync(live.ReopenBranch());
        }
        if(game is null){await LaunchGame(false);if(game is null)return;}
        using var cancel=new CancellationTokenSource();seeking=cancel;
        try{GameStatus.Text=$"正在重播返回：{mark.Name}";await game.SeekAsync(mark.Frame,cancel.Token);SelectFrame(mark.Frame);}
        finally{seeking=null;}
    }
    void RemoveBookmarkClick(object? s,RoutedEventArgs e){try{if(BookmarkList.SelectedItem is FrameBookmark mark){bookmarks.Remove(mark);PersistBookmarks();}}catch(Exception ex){StatusLabel.Text=ex.Message;}}
    async void SaveRecordingClick(object? s,RoutedEventArgs e)=>await Operate(SaveRecordingAsync);
    async Task SaveRecordingAsync() {
        if(game is not null)await game.PauseAsync(default);
        if(game is null && Project is null)throw new InvalidOperationException("先新建或打开录制。");
        var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title="选择保存位置（将新建独立录制文件夹，包含初始存档和书签）",AllowMultiple=false});
        if(folders.Count==0 || folders[0].TryGetLocalPath() is not string folder)return;
        var initial=game is not null?Path.Combine(game.SessionDirectory,"initial"):await InitialDirectory(sourcePath);
        if(initial is null)return;
        Replay replay;
        if(game is not null){var active=game;await EndGame(false);replay=Replay.Load(active.BranchPath);}
        else replay=Project!.Source;
        var output=RecordingLibrary.SaveBundle(folder,replay,initial,bookmarks.Where(m=>m.Frame<replay.Count));ShowSaved(output);
        if(!dirty)await OpenPathAsync(output);
        StatusLabel.Text="录制及初始存档、书签已保存。输入草稿需单独保存项目。";
    }
    void OpenSavedClick(object? s,RoutedEventArgs e){try{if(lastSaved is not null)Process.Start(new ProcessStartInfo(Path.GetDirectoryName(lastSaved)!){UseShellExecute=true});}catch(Exception ex){StatusLabel.Text="打开目录失败："+ex.Message;}}
}
