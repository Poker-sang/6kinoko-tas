using Avalonia; using Avalonia.Controls; using Avalonia.Input; using Avalonia.Media; using KinokoTAS.Core;
namespace KinokoTAS.App;
/// <summary>Draw only visible frame columns; no million-row control collection.</summary>
public sealed class TimelineControl : Control {
    public const double RowHeight=17,HeaderHeight=23,FrameWidth=76,CellWidth=12;
    public TasProject? Project {get;set;}
    public IReadOnlyList<uint>? LiveMasks {get;set;}
    public IReadOnlyList<FrameBookmark> Bookmarks {get;set;}=[];
    public int Playhead {get;set;}=-1;
    public int FrameCount=>LiveMasks?.Count??Project?.FrameCount??0;
    public int FirstFrame {get;set;}
    public int SelectedFrame {get;set;}
    public event Action<int,int>? CellClicked;
    public event Action<int>? Scrolled;
    public event Action<int>? FrameActivated;
    public event Action<int>? BookmarkRequested;
    public Func<int,int,bool>? BeginPainting {get;set;}
    public event Action<int,int,int>? PaintRange;
    public event Action? PaintCompleted;
    int paintFrame=-1,paintAction=-1;
    IPointer? paintingPointer;
    public void FinishPainting() {
        if(paintFrame<0)return;
        paintFrame=-1;paintAction=-1;
        var pointer=paintingPointer;paintingPointer=null;
        PaintCompleted?.Invoke();pointer?.Capture(null);
    }
    private static readonly Typeface Font=new("Segoe UI");
    private static readonly IBrush Muted=Brush.Parse("#8FA3BF"),Active=Brush.Parse("#247665"),Selected=Brush.Parse("#233C52"),Edited=Brush.Parse("#FFC779");
    private static void Text(DrawingContext c,string value,Point p,IBrush brush,double size=12)=>c.DrawText(new FormattedText(value,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Font,size,brush),p);
    public override void Render(DrawingContext c) {
        base.Render(c);c.FillRectangle(Brush.Parse("#151D29"),new Rect(Bounds.Size));
        Text(c,"动作 / 帧 →",new(6,5),Muted,11);
        for(int a=0;a<Replay.ActionCount;a++)Text(c,Replay.Labels[a],new(6,HeaderHeight+a*RowHeight+2),Muted,11);
        
        int visible=Math.Max(0,(int)((Bounds.Width-FrameWidth)/CellWidth)+1);
        for(int column=0;column<visible;column++) {
            int frame=FirstFrame+column;bool recorded=frame<FrameCount;
            double x=FrameWidth+column*CellWidth;
            if(!recorded)c.FillRectangle(Brush.Parse("#0C111A"),new Rect(x,HeaderHeight,CellWidth,Bounds.Height-HeaderHeight));
            if(Bookmarks.Any(m=>m.Frame==frame))c.FillRectangle(Brush.Parse("#604D3720"),new Rect(x,0,CellWidth,Bounds.Height));
            if(frame==SelectedFrame)c.FillRectangle(Selected,new Rect(x,0,CellWidth,Bounds.Height));
            if(frame%5==0||frame==SelectedFrame)Text(c,frame.ToString(),new(x+1,5),frame==SelectedFrame?Brushes.White:Muted,10);
            for(int a=0;a<Replay.ActionCount;a++) {
                double y=HeaderHeight+a*RowHeight;
                c.DrawRectangle(null,new Pen(Brush.Parse("#243040"),0.5),new Rect(x,y,CellWidth,RowHeight));
                if(recorded && (LiveMasks is not null?(LiveMasks[frame]&(1u<<a))!=0:Project!.Down(frame,a)))c.FillRectangle(Active,new Rect(x+1,y+1,CellWidth-2,RowHeight-2));
                if(recorded && LiveMasks is null && Project!.IsEdited(frame,a))c.FillRectangle(Edited,new Rect(x+2,y+2,3,3));
            }
            if(Bookmarks.Any(m=>m.Frame==frame))c.FillRectangle(Edited,new Rect(x+2,0,CellWidth-4,4));
        }
        double cursor=FrameWidth+(Playhead-FirstFrame)*CellWidth;
        if(Playhead>=FirstFrame && cursor<Bounds.Width)c.DrawLine(new Pen(Brush.Parse("#FF6A78"),2),new(cursor,0),new(cursor,Bounds.Height));
        double end=FrameWidth+(FrameCount-FirstFrame)*CellWidth;
        if(end>=FrameWidth && end<Bounds.Width)Text(c,"未录制",new(end+6,HeaderHeight+4),Muted,11);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        base.OnPointerPressed(e);
        var buttons=e.GetCurrentPoint(this).Properties;
        if(buttons.IsRightButtonPressed){ContextMenu?.Close();ContextMenu=null;}
        if(!buttons.IsLeftButtonPressed && !buttons.IsRightButtonPressed)return;
        var p=e.GetPosition(this);if(p.X<FrameWidth || p.Y<0)return;
        int f=FirstFrame+(int)((p.X-FrameWidth)/CellWidth);
        int action=p.Y<HeaderHeight?-1:(int)((p.Y-HeaderHeight)/RowHeight);
        if(f>=FrameCount || action>=Replay.ActionCount)return;
        if(buttons.IsRightButtonPressed){
            CellClicked?.Invoke(f,-1);
            var add=new MenuItem{Header=$"在第 {f} 帧添加标记"};
            add.Click+=(_,_)=>BookmarkRequested?.Invoke(f);
            ContextMenu=new ContextMenu{ItemsSource=new[]{add}};ContextMenu.Open(this);
        }else {
            if(action>=0 && BeginPainting?.Invoke(f,action)==true){paintFrame=f;paintAction=action;paintingPointer=e.Pointer;e.Pointer.Capture(this);}
            CellClicked?.Invoke(f,action);
            if(action<0 && e.ClickCount==2)FrameActivated?.Invoke(f);
        }
        e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e){
        base.OnPointerMoved(e);var point=e.GetPosition(this);
        int frame=FirstFrame+(int)((Math.Clamp(point.X,FrameWidth,Math.Max(FrameWidth,Bounds.Width-1))-FrameWidth)/CellWidth);
        if(paintFrame>=0 && FrameCount>0){
            frame=Math.Clamp(frame,0,FrameCount-1);
            PaintRange?.Invoke(Math.Min(paintFrame,frame),Math.Max(paintFrame,frame),paintAction);paintFrame=frame;e.Handled=true;
        }
        ToolTip.SetTip(this,point.X>=FrameWidth?string.Join(" · ",Bookmarks.Where(mark=>mark.Frame==frame).Select(mark=>mark.Name)):null);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);FinishPainting();}
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);FinishPainting();}
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {Scrolled?.Invoke(-(int)((e.Delta.X!=0?e.Delta.X:e.Delta.Y)*5));e.Handled=true;}
}
