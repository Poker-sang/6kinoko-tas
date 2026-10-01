using Avalonia.Controls; using Avalonia.Interactivity; using Avalonia.Platform.Storage;
using KinokoTAS.Core; using System.Collections.ObjectModel; using System.Diagnostics;
using Avalonia.Input; using Avalonia.VisualTree;
namespace KinokoTAS.App;
public partial class MainWindow {
    readonly ObservableCollection<FrameBookmark> bookmarks=[];
    string? bookmarkFile,lastSaved;
    string? recordingSavePath;
    public string? RecordingSavePath=>recordingSavePath;
    void InitializeLibrary(){BookmarkList.ItemsSource=bookmarks;Timeline.Bookmarks=bookmarks;bookmarks.CollectionChanged+=(_,_)=>Timeline.InvalidateVisual();FrameScroll.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent,(_,_)=>{if(!followScroll)FollowLatest.IsChecked=false;},RoutingStrategies.Tunnel,true);UpdateGamePath();
        BookmarkList.AddHandler(InputElement.PointerPressedEvent,BookmarkPointerPressed,RoutingStrategies.Tunnel,true);
        BookmarkList.ContextRequested+=(_,e)=>{if(BookmarkList.ContextMenu is { } menu){menu.Open(BookmarkList);e.Handled=true;}};
    }
    void BookmarkPointerPressed(object? sender,PointerPressedEventArgs e) {
        if(busy||gameCommand)return;
        var visual=e.Source as Avalonia.Visual;
        var item=visual as ListBoxItem ?? visual?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if(item?.Content is not FrameBookmark mark)return;
        var buttons=e.GetCurrentPoint(BookmarkList).Properties;
        if(buttons.IsRightButtonPressed) {
            BookmarkList.SelectedItem=mark;BookmarkList.ContextMenu?.Close();
            var remove=new MenuItem{Header="删除重点"};
            remove.Click+=(_,_)=>RemoveBookmark(mark);
            var rename=new MenuItem{Header="重命名重点"};
            rename.Click+=async (_,_)=>await Operate(()=>RenameBookmarkAsync(mark));
            BookmarkList.ContextMenu=new ContextMenu{ItemsSource=new[]{rename,remove}};
        }else if(buttons.IsLeftButtonPressed && e.ClickCount==2) {
            BookmarkList.SelectedItem=mark;FollowLatest.IsChecked=false;SelectFrame(mark.Frame);e.Handled=true;
        }
    }
    void UpdateGamePath(){GamePathLabel.Text=gameExe is null?"当前游戏：尚未选择（首次开始时选择一次）":"当前游戏："+gameExe;ToolTip.SetTip(GamePathLabel,gameExe);}
    void ShowSaved(string path){lastSaved=path;SavedPathLabel.Text="已保存："+path;OpenSavedButton.IsEnabled=true;}
    string BookmarkCache(Replay replay)=>Path.Combine(Path.GetDirectoryName(SettingsPath)!,"bookmarks",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(replay.Bytes.Span))+".json");
    void LoadBookmarksFor(Replay replay,string path,int? frameCount=null) {
        bookmarkFile=BookmarkCache(replay);bookmarks.Clear();
        var stored=path.EndsWith(".ktas",StringComparison.OrdinalIgnoreCase)&&File.Exists(path+".bookmarks.json")?path+".bookmarks.json":File.Exists(bookmarkFile)?bookmarkFile:path+".bookmarks.json";
        var packed=Path.GetExtension(path).Equals(".krec",StringComparison.OrdinalIgnoreCase)&&RecordingPackage.IsPackage(path);
        var marks=packed?RecordingPackage.Load(path).Bookmarks:File.Exists(stored)?RecordingLibrary.LoadBookmarks(stored,frameCount??replay.Count):[];
        foreach(var mark in marks)bookmarks.Add(mark);
    }
    void PersistBookmarks(){documentUnsaved=true;if(bookmarkFile is not null && Project?.HasLayoutChanges!=true)RecordingLibrary.SaveBookmarks(bookmarkFile,bookmarks);RefreshEditWorkflow();}
    public async Task AddBookmarkAsync(string name) {
        int frame;
        if(Project?.HasLayoutChanges==true)frame=Timeline.SelectedFrame;
        else if(game is not null){await game.PauseAsync(default);frame=checked((int)(game.ReadState()!.Completed-1));}
        else if(Project?.Source.Count>0)frame=Timeline.SelectedFrame;
        else throw new InvalidOperationException("先新建或打开录制。");
        if(frame<0)throw new InvalidOperationException("请等待首帧完成。");
        AddBookmarkAt(frame,name);
    }
    public void AddBookmarkAt(int frame,string name) {
        if(frame<0 || (frame>=Timeline.FrameCount && game is null))throw new ArgumentOutOfRangeException(nameof(frame));
        var mark=new FrameBookmark(frame,string.IsNullOrWhiteSpace(name)?$"重点 {bookmarks.Count+1}":name.Trim());
        bookmarks.Add(mark);PersistBookmarks();BookmarkList.SelectedItem=mark;StatusLabel.Text=$"已添加书签：{mark}";
    }
    async void AddBookmarkClick(object? s,RoutedEventArgs e)=>await Operate(()=>AddBookmarkAsync(BookmarkName.Text??""));
    async void ReturnBookmarkClick(object? s,RoutedEventArgs e)=>await Operate(ReturnSelectedBookmarkAsync);
    public async Task ReturnSelectedBookmarkAsync(){
        RequireAppliedLayout();
        if(BookmarkList.SelectedItem is not FrameBookmark mark)return;
        if(game is null){await LaunchGame(false);if(game is null)return;}
        using var cancel=new CancellationTokenSource();seeking=cancel;seekTarget=mark.Frame+1;
        try{GameStatus.Text=$"正在重播返回：{mark.Name}";await game.SeekAsync(mark.Frame,cancel.Token);UpdatePlaybackProject();RefreshGameView();SelectFrame(mark.Frame);}
        finally{seeking=null;}
    }
    void RemoveBookmark(FrameBookmark mark){if(busy||gameCommand)return;try{bookmarks.Remove(mark);PersistBookmarks();}catch(Exception ex){StatusLabel.Text=ex.Message;}}
    public async Task RenameBookmarkAsync(FrameBookmark mark) {
        if(!bookmarks.Contains(mark))return;
        var name=await PromptNameAsync("重命名重点",mark.Name);
        if(name is null || !bookmarks.Contains(mark))return;
        var index=bookmarks.IndexOf(mark);var renamed=mark with {Name=name};
        bookmarks[index]=renamed;PersistBookmarks();BookmarkList.SelectedItem=renamed;
    }
    void RemoveBookmarkClick(object? s,RoutedEventArgs e){if(BookmarkList.SelectedItem is FrameBookmark mark)RemoveBookmark(mark);}
    async void SaveRecordingClick(object? s,RoutedEventArgs e)=>await Operate(()=>SaveRecordingAsync());
    async void SaveRecordingAsClick(object? s,RoutedEventArgs e)=>await Operate(()=>SaveRecordingAsync(true));
    public async Task SaveRecordingAsync(bool saveAs=false) {
        if(game?.IsRunning==true)await game.PauseAsync(default);
        if(game is null && Project is null)throw new InvalidOperationException("先新建或打开录制。");
        string? output=saveAs?null:recordingSavePath;
        if(output is null) {
            var selected=await StorageProvider.SaveFilePickerAsync(new(){Title=saveAs?"另存为录制":"保存录制",SuggestedFileName=recordingSavePath is null?"录制-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".krec":Path.GetFileName(recordingSavePath),DefaultExtension="krec",FileTypeChoices=[new("完整录制"){Patterns=["*.krec"]}]});
            output=selected?.TryGetLocalPath();if(output is null)return;
        }
        await SaveRecordingToAsync(output);
    }
    public async Task SaveRecordingToAsync(string output) {
        if(game is null && Project is null)throw new InvalidOperationException("先新建或打开录制。");
        if(!Path.GetExtension(output).Equals(".krec",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("请使用 .krec 扩展名。");
        output=Path.GetFullPath(output);
        if(game is not null && output.StartsWith(Path.GetFullPath(game.SessionDirectory)+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new IOException("请选择会话目录外的保存位置。");
        // Saving always means a playable .krec, never an implicit .ktas fallback.
        // Simulation/verification must succeed before the destination is touched.
        if(Project?.InvalidFrom is not null)await ApplyEditsAsync();
        var initial=game is not null?Path.Combine(game.SessionDirectory,"initial"):await InitialDirectory(sourcePath);
        if(initial is null)return;
        Replay replay;
        if(game is not null)replay=await game.CaptureRecordingAsync();
        else replay=Project!.Source;
        RecordingPackage.Save(output,replay,initial,bookmarks.Where(m=>m.Frame<replay.Count));ShowSaved(output);
        recordingSavePath=Path.GetFullPath(output);
        dirty=false;documentUnsaved=false;saveRevision++;
        savedLiveBranch=game?.BranchPath;savedLiveFrames=replay.Count;
        Refresh();
        StatusLabel.Text="已保存 .krec：录制、初始存档及重点。";
    }
    void OpenSavedClick(object? s,RoutedEventArgs e){try{if(lastSaved is not null)Process.Start(new ProcessStartInfo(Path.GetDirectoryName(lastSaved)!){UseShellExecute=true});}catch(Exception ex){StatusLabel.Text="打开目录失败："+ex.Message;}}
}
