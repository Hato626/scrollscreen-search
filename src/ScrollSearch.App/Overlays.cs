using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ScrollSearch.Core;

namespace ScrollSearch.App;

internal sealed class RegionPicker : Window
{
    readonly Canvas canvas = new();
    readonly Rectangle border = new() { Stroke = Brushes.Cyan, StrokeThickness = 3, Fill = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)) };
    Native.POINT start;
    bool dragging;
    public Box? Selection { get; private set; }
    public RegionPicker()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(65, 0, 0, 0));
        Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; Cursor = Cursors.Cross;
        Content = canvas;
        var hint = new TextBlock { Text = "拖动框选识别区域 · Esc 取消", FontSize = 22, Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(220, 18, 29, 43)), Padding = new Thickness(20), IsHitTestVisible = false };
        canvas.Children.Add(hint); Canvas.SetLeft(hint, 40); Canvas.SetTop(hint, 40); canvas.Children.Add(border);
        Loaded += (_, _) => { Native.Place(this, Native.VirtualScreen); Native.Exclude(this); Activate(); };
        MouseLeftButtonDown += (_, _) => { Native.GetCursorPos(out start); dragging = true; CaptureMouse(); Draw(); };
        MouseMove += (_, _) => { if (dragging) Draw(); };
        MouseLeftButtonUp += (_, _) =>
        {
            if (!dragging) return;
            Native.GetCursorPos(out var end); ReleaseMouseCapture();
            var box = new Box(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
            if (box.Width >= 24 && box.Height >= 24) { Selection = box; DialogResult = true; }
            else { dragging = false; border.Width = border.Height = 0; }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; } };
    }
    void Draw()
    {
        Native.GetCursorPos(out var end);
        var a = PointFromScreen(new(start.X, start.Y)); var b = PointFromScreen(new(end.X, end.Y));
        Canvas.SetLeft(border, Math.Min(a.X, b.X)); Canvas.SetTop(border, Math.Min(a.Y, b.Y));
        border.Width = Math.Abs(a.X - b.X); border.Height = Math.Abs(a.Y - b.Y);
    }
}

internal sealed class HighlightOverlay : Window
{
    readonly Canvas canvas = new() { IsHitTestVisible = false };
    public HighlightOverlay()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        Content = canvas; IsHitTestVisible = false;
        SourceInitialized += (_, _) =>
        {
            var handle = Native.Handle(this);
            Native.SetWindowLong(handle, -20, Native.GetWindowLong(handle, -20) | 0x20 | 0x80 | 0x08000000);
            Native.Exclude(this);
        };
    }
    public void Display(Box region, IReadOnlyList<Match> matches)
    {
        canvas.Children.Clear();
        if (!IsVisible) Show();
        Native.Place(this, region);
        var a = PointFromScreen(new(region.X, region.Y)); var b = PointFromScreen(new(region.Right, region.Bottom));
        var outer = new Rectangle { Width = Math.Max(1, b.X - a.X - 2), Height = Math.Max(1, b.Y - a.Y - 2),
            Stroke = new SolidColorBrush(Color.FromArgb(180, 34, 211, 238)), StrokeThickness = 1 };
        canvas.Children.Add(outer);
        foreach (var match in matches)
        {
            var tl = PointFromScreen(new(region.X + match.Bounds.X, region.Y + match.Bounds.Y));
            var br = PointFromScreen(new(region.X + match.Bounds.Right, region.Y + match.Bounds.Bottom));
            var rect = new Rectangle { Width = Math.Max(1, br.X - tl.X), Height = Math.Max(1, br.Y - tl.Y),
                Stroke = Brushes.OrangeRed, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(35, 255, 196, 0)) };
            Canvas.SetLeft(rect, tl.X); Canvas.SetTop(rect, tl.Y); canvas.Children.Add(rect);
        }
    }
}
