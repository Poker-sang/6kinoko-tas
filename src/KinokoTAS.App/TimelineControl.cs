using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using KinokoTAS.Core;

namespace KinokoTAS.App;

/// <summary>
/// Draw only visible frame columns; no million-row control collection.
/// </summary>
public sealed class TimelineControl : Control
{
    public const double RowHeight = 17, HeaderHeight = 23, FrameWidth = 76, CellWidth = 12;

    public TasProject? Project { get; set; }

    public IReadOnlyList<uint>? LiveMasks { get; set; }

    public IReadOnlyList<FrameBookmark> Bookmarks { get; set; } = [];

    public int Playhead { get; set; } = -1;

    public int FrameCount => LiveMasks?.Count ?? Project?.FrameCount ?? 0;

    public int FirstFrame { get; set; }

    public int SelectedFrame { get; set; }

    public event Action<int, int>? CellClicked;

    public event Action<int>? Scrolled;

    public event Action<int>? FrameActivated;

    public event Action<int>? BookmarkRequested;

    public Func<int, int, bool>? BeginPainting { get; set; }

    public event Action<int, int, int>? PaintRange;

    public event Action? PaintCompleted;

    private int _paintFrame = -1, _paintAction = -1;
    private IPointer? _paintingPointer;

    public void FinishPainting()
    {
        if (_paintFrame < 0)
            return;

        _paintFrame = -1;
        _paintAction = -1;
        var pointer = _paintingPointer;
        _paintingPointer = null;
        PaintCompleted?.Invoke();
        pointer?.Capture(null);
    }

    private static readonly Typeface _Font = new("Segoe UI");

    private static readonly IBrush _Muted = Brush.Parse("#8FA3BF"),
        _Active = Brush.Parse("#247665"),
        _Selected = Brush.Parse("#233C52"),
        _Edited = Brush.Parse("#FFC779");

    private static void Text(DrawingContext c, string value, Point p, IBrush brush, double size = 12) => c.DrawText(
        new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _Font,
            size,
            brush), p);

    public override void Render(DrawingContext c)
    {
        base.Render(c);
        c.FillRectangle(Brush.Parse("#151D29"), new Rect(Bounds.Size));
        Text(c, "动作 / 帧 →", new(6, 5), _Muted, 11);
        for (var a = 0; a < Replay.ActionCount; a++)
            Text(c, Replay.Labels[a], new(6, HeaderHeight + a * RowHeight + 2), _Muted, 11);

        var visible = Math.Max(0, (int) ((Bounds.Width - FrameWidth) / CellWidth) + 1);
        for (var column = 0; column < visible; column++)
        {
            var frame = FirstFrame + column;
            var recorded = frame < FrameCount;
            var x = FrameWidth + column * CellWidth;
            if (!recorded)
                c.FillRectangle(Brush.Parse("#0C111A"),
                    new Rect(x, HeaderHeight, CellWidth, Bounds.Height - HeaderHeight));

            if (Bookmarks.Any(m => m.Frame == frame))
                c.FillRectangle(Brush.Parse("#604D3720"), new Rect(x, 0, CellWidth, Bounds.Height));

            if (frame == SelectedFrame)
                c.FillRectangle(_Selected, new Rect(x, 0, CellWidth, Bounds.Height));

            if (frame % 5 == 0 || frame == SelectedFrame)
                Text(c, frame.ToString(), new(x + 1, 5), frame == SelectedFrame ? Brushes.White : _Muted, 10);

            for (var a = 0; a < Replay.ActionCount; a++)
            {
                var y = HeaderHeight + a * RowHeight;
                c.DrawRectangle(null, new Pen(Brush.Parse("#243040"), 0.5), new Rect(x, y, CellWidth, RowHeight));
                if (recorded && (LiveMasks is not null ? (LiveMasks[frame] & (1u << a)) != 0 : Project!.Down(frame, a)))
                    c.FillRectangle(_Active, new Rect(x + 1, y + 1, CellWidth - 2, RowHeight - 2));

                if (recorded && LiveMasks is null && Project!.IsEdited(frame, a))
                    c.FillRectangle(_Edited, new Rect(x + 2, y + 2, 3, 3));
            }

            if (Bookmarks.Any(m => m.Frame == frame))
                c.FillRectangle(_Edited, new Rect(x + 2, 0, CellWidth - 4, 4));
        }

        var cursor = FrameWidth + (Playhead - FirstFrame) * CellWidth;
        if (Playhead >= FirstFrame && cursor < Bounds.Width)
            c.DrawLine(new Pen(Brush.Parse("#FF6A78"), 2), new(cursor, 0), new(cursor, Bounds.Height));

        var end = FrameWidth + (FrameCount - FirstFrame) * CellWidth;
        if (end >= FrameWidth && end < Bounds.Width)
            Text(c, "未录制", new(end + 6, HeaderHeight + 4), _Muted, 11);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var buttons = e.GetCurrentPoint(this).Properties;
        if (buttons.IsRightButtonPressed)
        {
            ContextMenu?.Close();
            ContextMenu = null;
        }

        if (buttons is { IsLeftButtonPressed: false, IsRightButtonPressed: false })
            return;

        var p = e.GetPosition(this);
        if (p.X < FrameWidth || p.Y < 0)
            return;

        var f = FirstFrame + (int) ((p.X - FrameWidth) / CellWidth);
        var action = p.Y < HeaderHeight ? -1 : (int) ((p.Y - HeaderHeight) / RowHeight);
        if (f >= FrameCount || action >= Replay.ActionCount)
            return;

        if (buttons.IsRightButtonPressed)
        {
            CellClicked?.Invoke(f, -1);
            var add = new MenuItem { Header = $"在第 {f} 帧添加标记" };
            add.Click += (_, _) => BookmarkRequested?.Invoke(f);
            ContextMenu = new ContextMenu { ItemsSource = new[] { add } };
            ContextMenu.Open(this);
        }
        else
        {
            if (action >= 0 && BeginPainting?.Invoke(f, action) == true)
            {
                _paintFrame = f;
                _paintAction = action;
                _paintingPointer = e.Pointer;
                e.Pointer.Capture(this);
            }

            CellClicked?.Invoke(f, action);
            if (action < 0 && e.ClickCount == 2)
                FrameActivated?.Invoke(f);
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        var frame = FirstFrame +
                    (int) ((Math.Clamp(point.X, FrameWidth, Math.Max(FrameWidth, Bounds.Width - 1)) - FrameWidth) /
                           CellWidth);
        if (_paintFrame >= 0 && FrameCount > 0)
        {
            frame = Math.Clamp(frame, 0, FrameCount - 1);
            PaintRange?.Invoke(Math.Min(_paintFrame, frame), Math.Max(_paintFrame, frame), _paintAction);
            _paintFrame = frame;
            e.Handled = true;
        }

        var tip = point.X >= FrameWidth
            ? string.Join(" · ", Bookmarks.Where(mark => mark.Frame == frame).Select(mark => mark.Name))
            : null;
        if (string.IsNullOrWhiteSpace(tip))
        {
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, null);
        }
        else
            ToolTip.SetTip(this, tip);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        FinishPainting();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        FinishPainting();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        Scrolled?.Invoke(-(int) ((e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 5));
        e.Handled = true;
    }
}
