using Avalonia.Interactivity;
using KinokoTAS.Core;
namespace KinokoTAS.App;
public partial class MainWindow {
    LiveTimeline liveTimeline=new();string? timelineSource;bool followScroll;
    void UpdatePlaybackProject() {
        if(game?.PlaybackSource is not string path || timelineSource==path)return;
        var replay=Replay.Load(path);
        // Preserve a loaded project's draft when connecting its unchanged source.
        if(Project is null || !Project.Source.Bytes.Span.SequenceEqual(replay.Bytes.Span)){
            if(Project is not null)Project.Changed-=OnChanged;
            Project=new TasProject(replay,"当前录制.krec");Project.Changed+=OnChanged;dirty=false;Timeline.Project=Project;
        }
        sourcePath=path;timelineSource=path;Timeline.LiveMasks=null;
        SaveButton.IsEnabled=ExportButton.IsEnabled=true;HoldButton.IsEnabled=ReleaseButton.IsEnabled=replay.Count>0;Refresh();
    }
    void RefreshTimeline(SessionState state) {
        if(game is null)return;
        if(game.IsLive){if(savedLiveBranch!=game.BranchPath || savedLiveFrames!=state.Completed)documentUnsaved=true;liveTimeline.Read(game.BranchPath,state.Completed);Timeline.LiveMasks=liveTimeline.Masks;}
        else UpdatePlaybackProject();
        Timeline.Playhead=checked((int)state.Completed-1);Timeline.Bookmarks=bookmarks;
        int count=Timeline.FrameCount;
        JumpFrame.Maximum=RangeStart.Maximum=RangeEnd.Maximum=Math.Max(0,count-1);
        FrameScroll.Maximum=Math.Max(0,count-1);FrameScroll.ViewportSize=Math.Max(1,(Timeline.Bounds.Width-TimelineControl.FrameWidth)/TimelineControl.CellWidth);
        if(FollowLatest.IsChecked==true){int target=game.IsLive?count-1:Timeline.Playhead;int visible=Math.Max(1,(int)FrameScroll.ViewportSize);if(target<Timeline.FirstFrame||target>=Timeline.FirstFrame+visible-4){followScroll=true;FrameScroll.Value=Math.Max(0,target-visible+5);followScroll=false;}}
        DocumentLabel.Text=$"当前录制 · {count:N0} 帧 · 红线：当前帧 · 金色：书签 · 暗色：未录制";
        Timeline.InvalidateVisual();
    }
    public async Task ToggleRecordingAsync() {
        if(game is null)throw new InvalidOperationException("先新建或打开录制。");
        if(game.IsLive){documentUnsaved=HasUnsavedChanges;using var cancel=new CancellationTokenSource();seeking=cancel;try{await game.SwitchToPlaybackAsync(cancel.Token);UpdatePlaybackProject();}finally{seeking=null;}}
        else {
            if(Project?.InvalidFrom is not null)throw new InvalidOperationException("请先应用输入修改，或撤销草稿后再接管录制。");
            await game.PauseAsync(default);var recovery=CaptureRecovery();
            await game.TakeoverAsync();documentUnsaved=true;recoveries.Push(recovery);var boundary=game.ReadState()!.Completed;
            foreach(var mark in bookmarks.Where(m=>m.Frame>=boundary).ToArray())bookmarks.Remove(mark);
            liveTimeline=new();timelineSource=null;FollowLatest.IsChecked=true;await game.ResumeAsync(selectedSpeed,default);
        }
        RecordToggle.IsChecked=game.IsLive;RefreshGameView();
        if(game.IsLive) {
            gameKeys.Clear();game.Input(0);
            await FocusGameWindowAsync();
        }
    }
    public async Task ReplayAllAsync(){RequireAppliedLayout();if(game is null)throw new InvalidOperationException("先新建或打开录制。");await game.ReplayAllAsync();UpdatePlaybackProject();FollowLatest.IsChecked=true;RefreshGameView();}
    public async Task RestartGameAsync() {
        if(Project?.InvalidFrom is not null){await ApplyEditsAsync();return;}
        if(game is null){await LaunchGame(false);return;}
        using var cancel=new CancellationTokenSource();seeking=cancel;seekTarget=game.ReadState()?.Completed??1;
        try{GameStatus.Text="重新启动游戏，正在恢复位置…";await game.RestartAsync(cancel.Token);timelineSource=null;previewCount=-1;orderedGameWindow=0;UpdatePlaybackProject();RefreshGameView();}
        finally{seeking=null;}
    }
    async void RestartGameClick(object? s,RoutedEventArgs e)=>await Operate(RestartGameAsync);
    async void ReplayAllClick(object? s,RoutedEventArgs e)=>await Operate(ReplayAllAsync);
}
