using Avalonia; using Avalonia.Controls; using Avalonia.Input; using Avalonia.Media; using KinokoTAS.Core;
namespace KinokoTAS.App;
/// <summary>Draw only visible frame columns; no million-row control collection.</summary>
public sealed class TimelineControl : Control {
    public const double RowHeight=17,HeaderHeight=23,FrameWidth=76,CellWidth=12;
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
        Text(c,"动作 / 帧 →",new(6,5),Muted,11);
        for(int a=0;a<Replay.ActionCount;a++)Text(c,Replay.Labels[a],new(6,HeaderHeight+a*RowHeight+2),Muted,11);
        if(Project is null)return;
        int visible=Math.Max(0,(int)((Bounds.Width-FrameWidth)/CellWidth)+1);
        for(int column=0;column<visible;column++) {
            int frame=FirstFrame+column;if(frame>=Project.Source.Count)break;
            double x=FrameWidth+column*CellWidth;
            if(frame==SelectedFrame)c.FillRectangle(Selected,new Rect(x,0,CellWidth,Bounds.Height));
            if(frame%5==0||frame==SelectedFrame)Text(c,frame.ToString(),new(x+1,5),frame==SelectedFrame?Brushes.White:Muted,10);
            for(int a=0;a<Replay.ActionCount;a++) {
                double y=HeaderHeight+a*RowHeight;
                c.DrawRectangle(null,new Pen(Brush.Parse("#243040"),0.5),new Rect(x,y,CellWidth,RowHeight));
                if(Project.Down(frame,a))c.FillRectangle(Active,new Rect(x+1,y+1,CellWidth-2,RowHeight-2));
                if(Project.IsEdited(frame,a))c.FillRectangle(Edited,new Rect(x+2,y+2,3,3));
            }
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        base.OnPointerPressed(e);
        if(Project is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        var p=e.GetPosition(this);if(p.X<FrameWidth || p.Y<0)return;
        int f=FirstFrame+(int)((p.X-FrameWidth)/CellWidth);
        int action=p.Y<HeaderHeight?-1:(int)((p.Y-HeaderHeight)/RowHeight);
        if(f>=Project.Source.Count || action>=Replay.ActionCount)return;
        CellClicked?.Invoke(f,action);e.Handled=true;
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {Scrolled?.Invoke(-(int)((e.Delta.X!=0?e.Delta.X:e.Delta.Y)*5));e.Handled=true;}
}
