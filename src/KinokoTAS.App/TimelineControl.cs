using Avalonia; using Avalonia.Controls; using Avalonia.Input; using Avalonia.Media; using KinokoTAS.Core;
namespace KinokoTAS.App;
/// <summary>Draw only visible rows; no million-row control collection.</summary>
public sealed class TimelineControl : Control {
    public const double RowHeight=27,HeaderHeight=34,FrameWidth=99,CellWidth=64;
    public TasProject? Project {get;set;}
    public int FirstFrame {get;set;}
    public int SelectedFrame {get;set;}
    public event Action<int,int>? CellClicked;
    public event Action<int>? Scrolled;
    private static readonly Typeface Font=new("Segoe UI");
    private static readonly IBrush Muted=Brush.Parse("#8FA3BF"),Active=Brush.Parse("#247665"),Selected=Brush.Parse("#233C52"),Edited=Brush.Parse("#FFC779");
    private static void Text(DrawingContext c,string value,Point p,IBrush brush,double size=12)=>c.DrawText(new FormattedText(value,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Font,size,brush),p);
    public override void Render(DrawingContext c) {
        base.Render(c);c.FillRectangle(Brush.Parse("#151D29"),new Rect(Bounds.Size));
        Text(c,"帧 / 60 Hz",new(10,8),Muted);
        for(int a=0;a<Replay.ActionCount;a++)Text(c,Replay.Labels[a],new(FrameWidth+a*CellWidth+5,8),Muted);
        if(Project is null){Text(c,"打开录制后，这里显示逐帧动作。",new(30,80),Muted,17);return;}
        int visible=Math.Max(0,(int)((Bounds.Height-HeaderHeight)/RowHeight)+1);
        for(int row=0;row<visible;row++) {
            int frame=FirstFrame+row;if(frame>=Project.Source.Count)break;
            double y=HeaderHeight+row*RowHeight;
            if(frame==SelectedFrame)c.FillRectangle(Selected,new Rect(0,y,Bounds.Width,RowHeight));
            else if(frame%2==0)c.FillRectangle(Brush.Parse("#182230"),new Rect(0,y,Bounds.Width,RowHeight));
            Text(c,frame.ToString("D6"),new(12,y+5),frame==SelectedFrame?Brushes.White:Muted);
            for(int a=0;a<Replay.ActionCount;a++) {
                double x=FrameWidth+a*CellWidth;
                if(Project.Down(frame,a)) {
                    c.DrawRectangle(Active,null,new Rect(x+2,y+2,CellWidth-4,RowHeight-4),3,3);
                    Text(c,"●",new(x+25,y+4),Brushes.White);
                }
                if(Project.IsEdited(frame,a))c.FillRectangle(Edited,new Rect(x+3,y+3,4,4));
            }
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        base.OnPointerPressed(e);
        if(Project is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        var p=e.GetPosition(this);if(p.Y<HeaderHeight)return;
        int f=FirstFrame+(int)((p.Y-HeaderHeight)/RowHeight);
        if(f>=Project.Source.Count)return;
        int action=p.X<FrameWidth?-1:(int)((p.X-FrameWidth)/CellWidth);
        if(action>=Replay.ActionCount)return;
        CellClicked?.Invoke(f,action);e.Handled=true;
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {Scrolled?.Invoke(-(int)(e.Delta.Y*4));e.Handled=true;}
}
