using Avalonia.Controls;
using Avalonia.Interactivity;
using KinokoTAS.Core;
namespace KinokoTAS.App;
public partial class MainWindow {
    static readonly double[] Speeds=[0.25,0.5,1,2,4];
    TasProject? paintingProject,layoutProject;
    bool paintingDown;
    bool changingSpeed;
    bool frameToolsReady;
    double selectedSpeed=1;
    readonly Dictionary<TasProject,Dictionary<long,(FrameBookmark[] Forward,FrameBookmark[] Backward)>> bookmarkLayouts=[];
    void InitializeFrameTools() {
        frameToolsReady=true;
        Timeline.BeginPainting=(frame,action)=>{
            if(busy||gameCommand||Project is null||Timeline.LiveMasks is not null)return false;
            paintingProject=Project;paintingDown=!Project.Down(frame,action);Project.BeginPaint();return true;
        };
        Timeline.PaintRange+=(first,last,action)=>paintingProject?.SetRange(first,last,action,paintingDown);
        Timeline.PaintCompleted+=()=>{paintingProject?.EndPaint();paintingProject=null;};
    }
    void BindLayoutProject() {
        if(ReferenceEquals(Project,layoutProject))return;
        if(layoutProject is not null)layoutProject.LayoutChanged-=OnFrameLayoutChanged;
        layoutProject=Project;
        if(layoutProject is not null)layoutProject.LayoutChanged+=OnFrameLayoutChanged;
    }
    void OnFrameLayoutChanged(FrameLayoutChange change) {
        if(Project is null)return;
        if(!bookmarkLayouts.TryGetValue(Project,out var history)){history=[];bookmarkLayouts.Add(Project,history);}
        history.TryGetValue(change.Id,out var saved);
        int removed=change.Forward?change.Removed:change.Inserted;
        int inserted=change.Forward?change.Inserted:change.Removed;
        var lost=bookmarks.Where(mark=>mark.Frame>=change.First&&mark.Frame<change.First+removed).ToArray();
        var retained=bookmarks.Except(lost).Select(mark=>mark.Frame>=change.First+removed?mark with {Frame=mark.Frame+inserted-removed}:mark).ToList();
        retained.AddRange((change.Forward?saved.Backward:saved.Forward)??[]);
        history[change.Id]=change.Forward?(lost,saved.Backward??[]):(saved.Forward??[],lost);
        bookmarks.Clear();foreach(var mark in retained.OrderBy(mark=>mark.Frame))bookmarks.Add(mark);
        int maximum=Math.Max(0,Project.FrameCount-1);
        FrameScroll.Maximum=maximum;JumpFrame.Maximum=RangeStart.Maximum=RangeEnd.Maximum=maximum;
        Timeline.SelectedFrame=Math.Clamp(Timeline.SelectedFrame,0,Math.Max(0,Project.FrameCount-1));
    }
    bool CanEditFrames() {
        if(Project is null||busy||gameCommand)return false;
        if(game?.IsLive==true){StatusLabel.Text="先关闭录制开关，再编辑帧。";return false;}
        Timeline.FinishPainting();return true;
    }
    public void InsertEmptyFrames(int before,int count) {
        if(!CanEditFrames())return;
        BindLayoutProject();Project!.InsertFrames(before,count);FollowLatest.IsChecked=false;SelectFrame(before);
        StatusLabel.Text=$"已插入 {count} 帧空白输入；应用修改后生效。";
    }
    public void DeleteFrameRange(int first,int last) {
        if(!CanEditFrames())return;
        BindLayoutProject();Project!.DeleteFrames(first,checked(last-first+1));FollowLatest.IsChecked=false;SelectFrame(first);
        StatusLabel.Text="已删除选定帧；应用修改后生效，Ctrl+Z 可撤销。";
    }
    void InsertFramesClick(object? sender,RoutedEventArgs args){try{InsertEmptyFrames(Timeline.SelectedFrame,(int)(InsertCount.Value??1));}catch(Exception error){StatusLabel.Text=error.Message;}}
    void AppendFramesClick(object? sender,RoutedEventArgs args){try{if(Project is not null)InsertEmptyFrames(Project.FrameCount,(int)(InsertCount.Value??1));}catch(Exception error){StatusLabel.Text=error.Message;}}
    void DeleteFramesClick(object? sender,RoutedEventArgs args){try{DeleteFrameRange((int)(RangeStart.Value??0),(int)(RangeEnd.Value??0));}catch(Exception error){StatusLabel.Text=error.Message;}}
    async void SpeedChanged(object? sender,SelectionChangedEventArgs args) {
        if(!frameToolsReady||changingSpeed||sender is not ComboBox picker||picker.SelectedIndex<0)return;
        double previous=selectedSpeed,next=Speeds[picker.SelectedIndex];
        if(gameCommand||busy){changingSpeed=true;picker.SelectedIndex=Array.IndexOf(Speeds,previous);changingSpeed=false;return;}
        await Operate(async()=>{
            try{if(game is not null)await game.SetSpeedAsync(next);selectedSpeed=next;StatusLabel.Text=$"播放/录制速度 {next:0.##}×；模拟时间保持每秒 60 帧。";}
            catch{changingSpeed=true;picker.SelectedIndex=Array.IndexOf(Speeds,previous);changingSpeed=false;throw;}
        });
    }
}
