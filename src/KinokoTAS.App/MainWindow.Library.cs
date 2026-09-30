using Avalonia.Controls; using Avalonia.Interactivity; using Avalonia.Platform.Storage;
using KinokoTAS.Core; using System.Collections.ObjectModel; using System.Diagnostics;
using Avalonia.Input; using Avalonia.VisualTree;
namespace KinokoTAS.App;
public partial class MainWindow {
    readonly ObservableCollection<FrameBookmark> bookmarks=[];
    string? bookmarkFile,lastSaved;
    void InitializeLibrary(){BookmarkList.ItemsSource=bookmarks;Timeline.Bookmarks=bookmarks;bookmarks.CollectionChanged+=(_,_)=>Timeline.InvalidateVisual();FrameScroll.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent,(_,_)=>{if(!followScroll)FollowLatest.IsChecked=false;},RoutingStrategies.Tunnel,true);UpdateGamePath();
        BookmarkList.AddHandler(InputElement.PointerPressedEvent,BookmarkPointerPressed,RoutingStrategies.Tunnel,true);
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
            BookmarkList.ContextMenu=new ContextMenu{ItemsSource=new[]{remove}};
            BookmarkList.ContextMenu.Open(BookmarkList);e.Handled=true;
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
        var marks=File.Exists(stored)?RecordingLibrary.LoadBookmarks(stored,frameCount??replay.Count):Path.GetExtension(path).Equals(".krec",StringComparison.OrdinalIgnoreCase)&&RecordingPackage.IsPackage(path)?RecordingPackage.Load(path).Bookmarks:[];
        foreach(var mark in marks)bookmarks.Add(mark);
    }
    void PersistBookmarks(){if(bookmarkFile is not null && Project?.HasLayoutChanges!=true)RecordingLibrary.SaveBookmarks(bookmarkFile,bookmarks);}
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
        if(Project?.HasLayoutChanges==true)throw new InvalidOperationException("请先应用帧布局修改，再返回书签画面。");
        if(BookmarkList.SelectedItem is not FrameBookmark mark)return;
        if(game is null){await LaunchGame(false);if(game is null)return;}
        using var cancel=new CancellationTokenSource();seeking=cancel;seekTarget=mark.Frame+1;
        try{GameStatus.Text=$"正在重播返回：{mark.Name}";await game.SeekAsync(mark.Frame,cancel.Token);UpdatePlaybackProject();RefreshGameView();SelectFrame(mark.Frame);}
        finally{seeking=null;}
    }
    void RemoveBookmark(FrameBookmark mark){if(busy||gameCommand)return;try{bookmarks.Remove(mark);PersistBookmarks();}catch(Exception ex){StatusLabel.Text=ex.Message;}}
    void RemoveBookmarkClick(object? s,RoutedEventArgs e){if(BookmarkList.SelectedItem is FrameBookmark mark)RemoveBookmark(mark);}
    async void SaveRecordingClick(object? s,RoutedEventArgs e)=>await Operate(SaveRecordingAsync);
    async Task SaveRecordingAsync() {
        if(Project?.InvalidFrom is not null)throw new InvalidOperationException("输入修改尚未执行。请先点击“应用修改”，或另存输入草稿项目。");
        if(game is not null)await game.PauseAsync(default);
        if(game is null && Project is null)throw new InvalidOperationException("先新建或打开录制。");
        var selected=await StorageProvider.SaveFilePickerAsync(new(){Title="保存单文件录制（包含初始存档与书签）",SuggestedFileName="录制-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".krec",DefaultExtension="krec",FileTypeChoices=[new("完整录制"){Patterns=["*.krec"]}]});
        if(selected?.TryGetLocalPath() is not string output)return;
        if(!Path.GetExtension(output).Equals(".krec",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("请使用 .krec 扩展名。");
        if(string.Equals(Path.GetFullPath(output),sourcePath,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new IOException("请选择新文件名，原录制保持不变。");
        var initial=game is not null?Path.Combine(game.SessionDirectory,"initial"):await InitialDirectory(sourcePath);
        if(initial is null)return;
        Replay replay;
        if(game is not null){var active=game;await EndGame(false);replay=Replay.Load(active.CurrentRecordingPath);}
        else replay=Project!.Source;
        RecordingPackage.Save(output,replay,initial,bookmarks.Where(m=>m.Frame<replay.Count));ShowSaved(output);
        if(!dirty)await OpenPathAsync(output);
        StatusLabel.Text="录制及初始存档、书签已保存。输入草稿需单独保存项目。";
    }
    void OpenSavedClick(object? s,RoutedEventArgs e){try{if(lastSaved is not null)Process.Start(new ProcessStartInfo(Path.GetDirectoryName(lastSaved)!){UseShellExecute=true});}catch(Exception ex){StatusLabel.Text="打开目录失败："+ex.Message;}}
}
