using Avalonia.Controls; using Avalonia.Input; using Avalonia.Interactivity; using Avalonia.Platform.Storage; using KinokoTAS.Core;
namespace KinokoTAS.App;
public partial class MainWindow : Window {
    public TasProject? Project {get;private set;}
    private bool dirty,allowClose,busy;
    private string? sourcePath;
    public MainWindow() {
        InitializeComponent();InitializeGamePanel();InitializeLibrary();InitializeFrameTools();ActionPicker.ItemsSource=Replay.Labels;
        Timeline.CellClicked+=(frame,action)=>{if(busy||gameCommand)return;SelectFrame(frame);FollowLatest.IsChecked=false;if(action>=0){if(Timeline.LiveMasks is null)Project?.SetRange(frame,frame,action,!Project.Down(frame,action));else StatusLabel.Text="先关闭“录制”开关，再修改已录制的输入。";}};
        Timeline.FrameActivated+=async frame=>{if(!busy)await SeekGame(frame);};
        Timeline.BookmarkRequested+=frame=>{if(busy||gameCommand)return;try{AddBookmarkAt(frame,BookmarkName.Text??"");}catch(Exception ex){StatusLabel.Text=ex.Message;}};
        Timeline.Scrolled+=delta=>{FollowLatest.IsChecked=false;FrameScroll.Value=Math.Clamp(FrameScroll.Value+delta,0,FrameScroll.Maximum);};
        Closing+=async (_,e)=> {
            if(allowClose)return;
            if(closePending){e.Cancel=true;return;}
            if(gameCommand||busy){e.Cancel=true;seeking?.Cancel();StatusLabel.Text="正在结束当前操作，请稍后再次关闭。";return;}
            e.Cancel=true;
            closePending=true;
            try{Timeline.FinishPainting();if(!await ConfirmSaveChangesAsync())return;await EndGame(false);allowClose=true;Avalonia.Threading.Dispatcher.UIThread.Post(Close);}
            catch(Exception ex){StatusLabel.Text="退出失败："+ex.Message+"；会话文件已保留。";}
            finally{closePending=false;}
        };
        AddHandler(KeyDownEvent,EditorShortcut,RoutingStrategies.Tunnel);
        KeyDown+=async (_,e)=> {
            if(e.Handled||dialogHost?.IsOpen==true||busy||gameCommand)return;
            if(e.Source is TextBox || e.Source is NumericUpDown)return;
            if(!e.KeyModifiers.HasFlag(KeyModifiers.Control))return;
            if(e.Key==Key.O){e.Handled=true;await OpenPicker();}
            if(e.Key==Key.S){e.Handled=true;await Operate(()=>SaveRecordingAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift)));} 
            if(e.Key==Key.Z){e.Handled=true;Project?.Undo();}
            if(e.Key==Key.Y){e.Handled=true;Project?.Redo();}
        };
    }
    private async Task<bool> ConfirmDiscard() {
        return await ConfirmContentAsync("未保存的修改","放弃未保存的输入草稿？原始录制不会被修改。","放弃修改","返回编辑");
    }
    private async Task OpenPicker() {
        if(busy||gameCommand)return;
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="打开录制或 TAS 项目",AllowMultiple=false,FileTypeFilter=[new("6kinoko TAS"){Patterns=["*.krec","*.ktas"]}]});
        if(files.Count>0 && files[0].TryGetLocalPath() is string p) {
            var previous=Project;await OpenPathAsync(p);
            if(!ReferenceEquals(previous,Project) && sourcePath==Path.GetFullPath(p) && Project?.Source.Count>0)await Operate(()=>LaunchGame(false));
        }
    }
    public async Task OpenPathAsync(string path) {
        Timeline.FinishPainting();
        if(busy || !await ConfirmSaveChangesAsync())return;
        busy=true;StatusLabel.Text="正在校验并读取录制…";
        try {
            var loaded=await Task.Run(()=>Path.GetExtension(path).Equals(".ktas",StringComparison.OrdinalIgnoreCase)?TasProject.Load(path):new TasProject(Replay.Load(path),Path.GetFileName(path)));
            if(game is not null)await EndGame(false);
            if(Project is not null)Project.Changed-=OnChanged;
            bookmarkLayouts.Clear();recoveries.Clear();Project=loaded;sourcePath=Path.GetFullPath(path);dirty=false;documentUnsaved=false;Project.Changed+=OnChanged;
            recordingSavePath=Path.GetExtension(path).Equals(".krec",StringComparison.OrdinalIgnoreCase)?Path.GetFullPath(path):null;
            LoadBookmarksFor(Project.Source,sourcePath,Project.FrameCount);
            Timeline.LiveMasks=null;Timeline.Project=Project;Timeline.FirstFrame=0;Timeline.SelectedFrame=0;FrameScroll.Value=0;
            FrameScroll.Maximum=Math.Max(0,Project.FrameCount-1);FrameScroll.ViewportSize=20;
            JumpFrame.Maximum=RangeStart.Maximum=RangeEnd.Maximum=Math.Max(0,Project.FrameCount-1);
            RangeStart.Value=RangeEnd.Value=JumpFrame.Value=0;
            SaveButton.IsEnabled=ExportButton.IsEnabled=true;
            HoldButton.IsEnabled=ReleaseButton.IsEnabled=Project.Source.Count>0;
            StatusLabel.Text=$"已打开 {Project.FrameCount:N0} 帧 · 黄色标记表示待应用编辑 · Ctrl+S 保存 .krec";Refresh();
        }catch(Exception ex){StatusLabel.Text="打开失败："+ex.Message;}
        finally{busy=false;}
    }
    private void OnChanged(){dirty=true;Refresh();}
    private void Refresh() {
        BindLayoutProject();
        RefreshEditWorkflow();
        if(Project is null)return;
        DocumentLabel.Text=$"{Project.SourceName}  ·  {Project.FrameCount:N0} 帧 / {Project.FrameCount/60.0:F2} 秒  ·  {Project.EditCount:N0} 处编辑";
        int frame=Math.Clamp(Timeline.SelectedFrame,0,Math.Max(0,Project.FrameCount-1));
        Timeline.SelectedFrame=frame;FrameLabel.Text=Project.FrameCount==0?"空录制":frame.ToString("D6");
        if(Project.FrameCount>0){
            int original=Project.SourceFrame(frame);
            FrameDetails.Text=original<0?"新增空白帧 · 应用修改后生成校验值":$"时间 {frame/60.0:F3} 秒\n原始来源帧 {original}\n原始 RNG 前 {Project.Source.RandomBefore(original):X8}\n原始 RNG 后 {Project.Source.RandomAfter(original):X8}\n原始检查值\n{Project.Source.Checkpoint(original):X16}";
        }
        ValidationLabel.Text=Project.InvalidFrom is int first ? $"第 {first:N0} 帧起有编辑。F5 应用到录制；Ctrl+S 应用并保存 .krec。" : "点击时间轴输入格修改按键；单击帧号选择，双击查看游戏画面。";
        Timeline.InvalidateVisual();
    }
    private void SelectFrame(int f) {
        if(Timeline.FrameCount==0)return;
        f=Math.Clamp(f,0,Timeline.FrameCount-1);Timeline.SelectedFrame=f;JumpFrame.Value=f;RangeStart.Value=RangeEnd.Value=f;
        int rows=Math.Max(1,(int)((Timeline.Bounds.Width-TimelineControl.FrameWidth)/TimelineControl.CellWidth));
        if(f<Timeline.FirstFrame || f>=Timeline.FirstFrame+rows)FrameScroll.Value=f;
        if(Timeline.LiveMasks is null)Refresh();else Timeline.InvalidateVisual();
    }
    private async Task SaveProject(bool duringConfirmation=false) {
        if(Project is null || busy || (gameCommand && !duringConfirmation))return;
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="导出编辑草稿（不是可回放录制）",SuggestedFileName=Path.GetFileNameWithoutExtension(Project.SourceName)+".ktas",DefaultExtension="ktas",FileTypeChoices=[new("编辑草稿（待应用）"){Patterns=["*.ktas"]}]});
        if(file?.TryGetLocalPath() is not string path)return;
        if(!Path.GetExtension(path).Equals(".ktas",StringComparison.OrdinalIgnoreCase)){StatusLabel.Text="项目必须使用 .ktas 扩展名。";return;}
        try{Project.Save(path);RecordingLibrary.SaveBookmarks(path+".bookmarks.json",bookmarks);StatusLabel.Text="编辑草稿已导出："+path+"；录制仍需用 Ctrl+S 保存为 .krec。";Refresh();}catch(Exception ex){StatusLabel.Text="草稿导出失败："+ex.Message;}
    }
    private async void ExportClick(object? s,RoutedEventArgs e) {
        if(Project is null || busy || gameCommand)return;
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="导出未修改的原始录制",SuggestedFileName="original.krec",DefaultExtension="krec"});
        if(file?.TryGetLocalPath() is not string path)return;
        if(Path.GetFullPath(path).Equals(sourcePath,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)){StatusLabel.Text="请使用不同路径，原始文件保持只读。";return;}
        try{Project.ExportSource(path);StatusLabel.Text="已导出原始录制（不包含项目中的输入编辑）。";}catch(Exception ex){StatusLabel.Text="导出失败："+ex.Message;}
    }
    private void EditRange(bool down) {
        if(Project is null || busy || gameCommand)return;
        if(game?.IsLive==true){StatusLabel.Text="先关闭“录制”开关，再修改已录制的输入。";return;}
        try{Project.SetRange((int)(RangeStart.Value??0),(int)(RangeEnd.Value??0),ActionPicker.SelectedIndex,down);StatusLabel.Text="区间已更新；点击“应用修改”重新模拟。";}catch(Exception){StatusLabel.Text="请确认起止帧顺序和动作选择。";}
    }
    private async void OpenClick(object? s,RoutedEventArgs e)=>await OpenPicker();
    private async void SaveClick(object? s,RoutedEventArgs e)=>await SaveProject();
    private void UndoClick(object? s,RoutedEventArgs e){if(!busy&&!gameCommand)Project?.Undo();}
    private void RedoClick(object? s,RoutedEventArgs e){if(!busy&&!gameCommand)Project?.Redo();}
    private void HoldClick(object? s,RoutedEventArgs e)=>EditRange(true);
    private void ReleaseClick(object? s,RoutedEventArgs e)=>EditRange(false);
    private async void PreviousClick(object? s,RoutedEventArgs e){FollowLatest.IsChecked=false;SelectFrame(Timeline.SelectedFrame-1);await SeekGame(Timeline.SelectedFrame);}
    private async void NextClick(object? s,RoutedEventArgs e){FollowLatest.IsChecked=false;SelectFrame(Timeline.SelectedFrame+1);await SeekGame(Timeline.SelectedFrame);}
    private async void JumpClick(object? s,RoutedEventArgs e){FollowLatest.IsChecked=false;SelectFrame((int)(JumpFrame.Value??0));await SeekGame(Timeline.SelectedFrame);}
    private void ScrollChanged(object? s,Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) {if(Timeline is not null){Timeline.FirstFrame=(int)e.NewValue;Timeline.InvalidateVisual();}}
}
