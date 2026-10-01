using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KinokoTAS.Core;
namespace KinokoTAS.App;

public partial class MainWindow {
    sealed record Recovery(string Path,int Frame,FrameBookmark[] Marks,TasProject? Draft);
    readonly Stack<Recovery> recoveries=[];
    string? operationProgress;
    long seekTarget;
    sealed class CallbackProgress(Action<SimulationProgress> callback):IProgress<SimulationProgress> {
        public void Report(SimulationProgress value)=>callback(value);
    }
    Recovery CaptureRecovery()=>new(game!.PlaybackSource??throw new InvalidOperationException("先完成当前录制。"),
        Math.Max(0,checked((int)(game.ReadState()?.Completed??1)-1)),bookmarks.ToArray(),Project);
    void AdoptSession(FileGameSession session) {
        game=session;timelineSource=null;liveTimeline=new();previewCount=-1;orderedGameWindow=0;
        GameImage.IsVisible=!session.ExternalWindow;ExternalHint.IsVisible=session.ExternalWindow;
        EmbeddedOption.IsEnabled=false;UpdatePlaybackProject();bookmarkFile=BookmarkCache(Project!.Source);Refresh();RefreshGameView();
    }
    public async Task ApplyEditsAsync() {
        if(Project?.InvalidFrom is null){StatusLabel.Text="时间轴没有待应用的编辑。Ctrl+S 保存录制。";return;}
        if(game is null){await LaunchGame(false);if(game is null)throw new OperationCanceledException("已取消连接游戏，编辑尚未应用。");}
        if(game.IsLive)throw new InvalidOperationException("先关闭“录制”开关，再编辑已录制的输入。");
        var selectedFrame=Math.Clamp(Timeline.SelectedFrame,0,Project.FrameCount-1);
        var draft=Project;
        var old=game;if(old.IsRunning)await old.PauseAsync(default);
        var recovery=CaptureRecovery();
        using var cancel=new CancellationTokenSource();seeking=cancel;busy=true;
        try {
            var next=await old.ResimulateAsync(draft,new CallbackProgress(p=>{
                operationProgress=$"{p.Stage}：{p.Completed:N0} / {p.Total:N0} 帧（{100.0*p.Completed/Math.Max(1,p.Total):F0}%）";
                GameStatus.Text=operationProgress;
                EditWorkflowLabel.Text=operationProgress;
            }),cancel.Token);
            try{await next.SeekAsync(selectedFrame,cancel.Token);}
            catch{await next.DisposeAsync();throw;}
            // Only adopt the result after a second complete, checkpoint-verified playback.
            await old.DisposeAsync();recoveries.Push(recovery);AdoptSession(next);PersistBookmarks();
            SelectFrame(selectedFrame);
            dirty=false;StatusLabel.Text="编辑已应用并验证，已返回所选帧；尚未保存到文件，按 Ctrl+S 保存 .krec。";
        } finally {busy=false;seeking=null;operationProgress=null;RefreshGameView();}
    }
    public async Task RestoreOverwriteAsync() {
        if(game is null || !recoveries.TryPeek(out var saved))throw new InvalidOperationException("没有可恢复的覆盖。");
        var old=game;if(old.IsRunning)await old.PauseAsync(default);
        using var cancel=new CancellationTokenSource();seeking=cancel;busy=true;seekTarget=saved.Frame+1;
        var restored=old.CreatePlaybackSession(saved.Path);
        try {
            restored.Progress+=Report;
            await restored.StartAsync(cancel.Token);await restored.SeekAsync(saved.Frame,cancel.Token);
            restored.Progress-=Report;
            await old.DisposeAsync();recoveries.Pop();AdoptSession(restored);
            bookmarks.Clear();foreach(var mark in saved.Marks)bookmarks.Add(mark);
            if(saved.Draft is not null && saved.Draft.Source.Bytes.Span.SequenceEqual(Project!.Source.Bytes.Span)) {
                Project.Changed-=OnChanged;Project=saved.Draft;Project.Changed+=OnChanged;Timeline.Project=Project;dirty=Project.EditCount>0;
            }
            PersistBookmarks();Refresh();StatusLabel.Text="已恢复上次覆盖前的录制、位置、书签及输入草稿；被替换的录制仍保留在会话目录。";
        } catch {await restored.DisposeAsync();throw;}
        finally {busy=false;seeking=null;operationProgress=null;RefreshGameView();}
        void Report(SessionState s){operationProgress=$"恢复旧版本：{s.Completed:N0} / {seekTarget:N0} 帧";GameStatus.Text=operationProgress;}
    }
    async void ApplyEditsClick(object? sender,RoutedEventArgs e)=>await Operate(ApplyEditsAsync);
    async void RestoreOverwriteClick(object? sender,RoutedEventArgs e)=>await Operate(RestoreOverwriteAsync);
    void CancelOperationClick(object? sender,RoutedEventArgs e)=>seeking?.Cancel();
    public void CancelCurrentOperation()=>seeking?.Cancel();
    void RefreshEditWorkflow() {
        bool pending=Project?.InvalidFrom is not null;
        bool available=!gameCommand && !busy && seeking is null;
        ApplyEditsButton.IsEnabled=pending && available && game?.IsLive!=true;
        SaveRecordingButton.Label=pending?"应用并保存录制":"保存录制";
        SaveRecordingButton.IsEnabled=available && (Project is not null || game is not null);
        UndoButton.IsEnabled=available && Project?.CanUndo==true;
        RedoButton.IsEnabled=available && Project?.CanRedo==true;
        bool unsaved=HasUnsavedChanges;
        var filename=recordingSavePath is not null?System.IO.Path.GetFileName(recordingSavePath)
            :Project is not null || game is not null?"未命名录制.krec":null;
        Title=filename is null?"6kinoko TAS":$"{filename}{(unsaved?" *":"")} — 6kinoko TAS";
        SavedPathLabel.Text=recordingSavePath is null?"录制尚未保存 · Ctrl+S 选择 .krec 路径"
            :$"{(unsaved?"有未保存更改":"当前录制")}：{recordingSavePath}";
        EditWorkflowLabel.Text=operationProgress ?? (pending
            ?$"待应用 · {Project!.EditCount:N0} 处编辑 · F5 应用到录制，Ctrl+S 应用并保存 .krec"
            :game is null && Project is null?"点击输入格编辑按键；F5 应用到录制，Ctrl+S 保存 .krec"
            :unsaved?"录制未保存 · Ctrl+S 保存 .krec（包含重点和初始存档）"
            :"录制已保存 · 时间轴没有待应用的编辑");
    }
    async void EditorShortcut(object? sender,KeyEventArgs e) {
        if(dialogHost?.IsOpen==true)return;
        // Function keys work even when preview owns focus; text-editing keys stay local.
        if(e.Key==Key.Escape && seeking is not null){e.Handled=true;seeking.Cancel();return;}
        if(e.Key==Key.F9){e.Handled=true;await Operate(async()=>{if(game is null)return;if(game.ReadState()?.Phase.EndsWith("paused")==true){RequireAppliedLayout();await game.ResumeAsync(selectedSpeed,default);}else await game.PauseAsync(default);});}
        else if(e.Key==Key.F10){e.Handled=true;StepGameClick(sender,e);}
        else if(e.Key==Key.F8){e.Handled=true;await Operate(ToggleRecordingAsync);}
        else if(e.Key==Key.F6){e.Handled=true;await Operate(()=>AddBookmarkAsync(BookmarkName.Text??""));}
        else if(e.Key==Key.F5){e.Handled=true;await Operate(ApplyEditsAsync);}
        else if(e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key==Key.Z){e.Handled=true;await Operate(RestoreOverwriteAsync);}
    }
}
