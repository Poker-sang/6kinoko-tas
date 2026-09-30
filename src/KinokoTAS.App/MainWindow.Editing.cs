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
        EmbeddedOption.IsEnabled=false;UpdatePlaybackProject();bookmarkFile=BookmarkCache(Project!.Source);PersistBookmarks();Refresh();RefreshGameView();
    }
    public async Task ApplyEditsAsync() {
        if(Project?.InvalidFrom is null)throw new InvalidOperationException("请先修改时间轴上的输入。");
        if(game is null){await LaunchGame(false);if(game is null)return;}
        if(game.IsLive)throw new InvalidOperationException("先关闭“录制”开关，再编辑已录制的输入。");
        var old=game;await old.PauseAsync(default);
        var recovery=CaptureRecovery();
        using var cancel=new CancellationTokenSource();seeking=cancel;busy=true;
        try {
            var next=await old.ResimulateAsync(Project,new CallbackProgress(p=>{
                operationProgress=$"{p.Stage}：{p.Completed:N0} / {p.Total:N0} 帧（{100.0*p.Completed/Math.Max(1,p.Total):F0}%）";
                GameStatus.Text=operationProgress;
            }),cancel.Token);
            // Only adopt the result after a second complete, checkpoint-verified playback.
            await old.DisposeAsync();recoveries.Push(recovery);AdoptSession(next);
            dirty=false;StatusLabel.Text="修改已执行并通过完整回放验证，可以保存 .krec；“恢复上次覆盖”可返回旧版本。";
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
            bookmarks.Clear();foreach(var mark in saved.Marks)bookmarks.Add(mark);PersistBookmarks();
            if(saved.Draft is not null && saved.Draft.Source.Bytes.Span.SequenceEqual(Project!.Source.Bytes.Span)) {
                Project.Changed-=OnChanged;Project=saved.Draft;Project.Changed+=OnChanged;Timeline.Project=Project;dirty=Project.EditCount>0;
            }
            Refresh();StatusLabel.Text="已恢复上次覆盖前的录制、位置、书签及输入草稿；被替换的录制仍保留在会话目录。";
        } catch {await restored.DisposeAsync();throw;}
        finally {busy=false;seeking=null;operationProgress=null;RefreshGameView();}
        void Report(SessionState s){operationProgress=$"恢复旧版本：{s.Completed:N0} / {seekTarget:N0} 帧";GameStatus.Text=operationProgress;}
    }
    async void ApplyEditsClick(object? sender,RoutedEventArgs e)=>await Operate(ApplyEditsAsync);
    async void RestoreOverwriteClick(object? sender,RoutedEventArgs e)=>await Operate(RestoreOverwriteAsync);
    void CancelOperationClick(object? sender,RoutedEventArgs e)=>seeking?.Cancel();
    public void CancelCurrentOperation()=>seeking?.Cancel();
    async void EditorShortcut(object? sender,KeyEventArgs e) {
        if(dialogHost?.IsOpen==true)return;
        // Function keys work even when preview owns focus; text-editing keys stay local.
        if(e.Key==Key.Escape && seeking is not null){e.Handled=true;seeking.Cancel();return;}
        if(e.Key==Key.F9){e.Handled=true;await Operate(async()=>{if(game is null)return;if(game.ReadState()?.Phase.EndsWith("paused")==true)await game.ResumeAsync(selectedSpeed,default);else await game.PauseAsync(default);});}
        else if(e.Key==Key.F10){e.Handled=true;StepGameClick(sender,e);}
        else if(e.Key==Key.F8){e.Handled=true;await Operate(ToggleRecordingAsync);}
        else if(e.Key==Key.F6){e.Handled=true;await Operate(()=>AddBookmarkAsync(BookmarkName.Text??""));}
        else if(e.Key==Key.F5){e.Handled=true;await Operate(ApplyEditsAsync);}
        else if(e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key==Key.Z){e.Handled=true;await Operate(RestoreOverwriteAsync);}
    }
}
