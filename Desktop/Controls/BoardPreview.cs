using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AOI.PTH.Desktop.Controls;

// Procedural illustration, not a camera image or the output of a detector.
public sealed class BoardPreview : FrameworkElement
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(nameof(Mode), typeof(string), typeof(BoardPreview), new FrameworkPropertyMetadata("Board", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(BoardPreview), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    private static SolidColorBrush Brush(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
    private static readonly Brush Board = Brush("#205C52"), Trace = Brush("#538677"), Gold = Brush("#C3B06D"), Text = Brush("#BDDBCC"), Dark = Brush("#182A32"), Amber = Brush("#F0B349");
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var full = Mode == "Board";
        double w = full ? 640 : 210, h = full ? 350 : 110;
        double scale = Math.Min(ActualWidth / w, ActualHeight / h);
        dc.PushTransform(new TranslateTransform((ActualWidth - w * scale) / 2, (ActualHeight - h * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        if (full) DrawBoard(dc); else DrawDetail(dc);
        dc.Pop(); dc.Pop();
    }
    private void DrawBoard(DrawingContext d)
    {
        d.DrawRoundedRectangle(Board, new Pen(Trace, 2), new Rect(22, 18, 596, 314), 12, 12);
        for (int i = 0; i < 9; i++)
        {
            double y = 47 + i * 31;
            d.DrawLine(new Pen(Trace, 1.5), new(42, y), new(593, y));
            for (int j = 0; j < 13; j++) d.DrawEllipse(Gold, null, new(56 + j * 43, y), 3.5, 3.5);
        }
        for (int i = 0; i < 11; i++) d.DrawLine(new Pen(Trace, 1), new(60 + 48 * i, 38), new(77 + 48 * i, 301));
        foreach (var p in new[] { new Point(39, 35), new Point(601, 35), new Point(39, 315), new Point(601, 315) })
        { d.DrawEllipse(Gold, null, p, 8, 8); d.DrawEllipse(Dark, null, p, 4, 4); }
        Chip(d, new(65, 80, 112, 54)); Chip(d, new(214, 68, 75, 55)); Chip(d, new(325, 83, 95, 77));
        Chip(d, new(431, 231, 93, 43)); Chip(d, new(222, 215, 68, 68));
        d.DrawEllipse(Brush("#293E46"), new Pen(Brush("#A9BDBA"), 4), new(510, 101), 30, 30);
        d.DrawLine(new Pen(Text, 3), new(490, 101), new(530, 101));
        for (int i = 0; i < 9; i++) d.DrawRectangle(Gold, null, new Rect(550 + i % 3 * 15, 170 + i / 3 * 24, 9, 16));
        d.DrawRoundedRectangle(Brush("#B5A781"), null, new Rect(103, 225, 64, 22), 4, 4);
        foreach (int x in new[] { 113, 126, 148 }) d.DrawRectangle(Brush("#564446"), null, new Rect(x, 225, 6, 22));
        d.DrawEllipse(Gold, null, new(346, 227), 6, 6); d.DrawEllipse(Gold, null, new(401, 227), 6, 6);
        Write(d, "PTH / FONTE-24V   REV.B", 64, 292, 11, Text);
        var boxes = new[] { new Rect(330, 205, 90, 45), new Rect(88, 212, 95, 47), new Rect(472, 62, 76, 80) };
        var labels = new[] { "JP3", "R12", "C07" };
        for (int i = 0; i < boxes.Length; i++)
        {
            var b = boxes[i]; bool active = SelectedIndex == i;
            d.DrawRectangle(active ? Brush("#22F0B349") : null, new Pen(active ? Amber : Brush("#85ACBA"), active ? 3 : 1.5), b);
            d.DrawRoundedRectangle(active ? Amber : Brush("#244556"), null, new Rect(b.X, b.Y - 23, 42, 21), 2, 2);
            Write(d, labels[i], b.X + 7, b.Y - 22, 12, active ? Dark : Brushes.White);
        }
    }
    private void DrawDetail(DrawingContext d)
    {
        var difference = Mode == "Difference";
        d.DrawRoundedRectangle(difference ? Brush("#152334") : Board, null, new Rect(3, 3, 204, 104), 5, 5);
        for (int i = 0; i < 4; i++) d.DrawLine(new Pen(difference ? Brush("#23364B") : Trace, 1), new(12, 18 + i * 23), new(198, 18 + i * 23));
        if (SelectedIndex == 0)
        {
            d.DrawEllipse(Gold, null, new(50, 57), 9, 9); d.DrawEllipse(Gold, null, new(160, 57), 9, 9);
            if (Mode == "Reference") d.DrawLine(new Pen(Brush("#D9DFD8"), 7), new(50, 57), new(160, 57));
            if (difference) d.DrawRoundedRectangle(Brush("#D79535"), null, new Rect(51, 52, 108, 10), 3, 3);
        }
        else if (SelectedIndex == 1)
        {
            d.DrawRoundedRectangle(difference ? Amber : Mode == "Reference" ? Brush("#B5A781") : Brush("#667B79"), null, new Rect(51, 39, 108, 34), 5, 5);
            if (!difference) foreach (int x in new[] { 65, 85, 126 }) d.DrawRectangle(Brush("#543B41"), null, new Rect(x, 39, 8, 34));
        }
        else
        {
            d.DrawEllipse(difference ? Amber : Dark, new Pen(difference ? Amber : Text, 3), new(Mode == "Reference" ? 101 : 113, 54), 32, 32);
            if (!difference) d.DrawLine(new Pen(Text, 2), new(87, 54), new(118, 54));
        }
    }
    private static void Chip(DrawingContext d, Rect rect)
    {
        for (int i = 8; i < rect.Width - 4; i += 12)
        { d.DrawRectangle(Gold, null, new Rect(rect.X + i, rect.Y - 5, 5, rect.Height + 10)); }
        d.DrawRoundedRectangle(Dark, new Pen(Brush("#82998D"), 1.5), rect, 3, 3);
    }
    private void Write(DrawingContext d, string text, double x, double y, int size, Brush brush) => d.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
}
