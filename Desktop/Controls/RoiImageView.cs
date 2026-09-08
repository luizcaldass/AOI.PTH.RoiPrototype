using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AOI.PTH.Desktop.Recipes;

namespace AOI.PTH.Desktop.Controls;

public sealed class RoiImageView : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(BitmapSource), typeof(RoiImageView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RoisProperty = DependencyProperty.Register(nameof(Rois), typeof(ObservableCollection<RecipeRoi>), typeof(RoiImageView), new FrameworkPropertyMetadata(null, OnRoisChanged));
    public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register(nameof(Selected), typeof(RecipeRoi), typeof(RoiImageView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public BitmapSource? Source { get => (BitmapSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public ObservableCollection<RecipeRoi>? Rois { get => (ObservableCollection<RecipeRoi>?)GetValue(RoisProperty); set => SetValue(RoisProperty, value); }
    public RecipeRoi? Selected { get => (RecipeRoi?)GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    public bool AllowDrawing { get; set; }
    public event Action<Rect>? RegionDrawn;
    private Point? start;
    private Point end;
    private readonly HashSet<RecipeRoi> observed = [];
    private static void OnRoisChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        var view = (RoiImageView)obj;
        if (args.OldValue is ObservableCollection<RecipeRoi> old) old.CollectionChanged -= view.CollectionChanged;
        if (args.NewValue is ObservableCollection<RecipeRoi> current) current.CollectionChanged += view.CollectionChanged;
        view.Observe();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => Observe();
    private void Observe()
    {
        foreach (var roi in observed) roi.PropertyChanged -= RoiChanged;
        observed.Clear();
        if (Rois is not null) foreach (var roi in Rois) { observed.Add(roi); roi.PropertyChanged += RoiChanged; }
        InvalidateVisual();
    }
    private void RoiChanged(object? sender, PropertyChangedEventArgs args) => InvalidateVisual();
    private (double Scale, double X, double Y) Transform()
    {
        if (Source is null) return (1, 0, 0);
        double scale = Math.Min(ActualWidth / Source.PixelWidth, ActualHeight / Source.PixelHeight);
        return (scale, (ActualWidth - Source.PixelWidth * scale) / 2, (ActualHeight - Source.PixelHeight * scale) / 2);
    }
    private Point ImagePoint(Point point)
    {
        var (s, x, y) = Transform();
        return new(Math.Clamp((point.X - x) / s, 0, Source!.PixelWidth), Math.Clamp((point.Y - y) / s, 0, Source!.PixelHeight));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(16, 31, 48)), null, new Rect(RenderSize));
        if (Source is null) return;
        var (s, x, y) = Transform();
        dc.DrawImage(Source, new Rect(x, y, Source.PixelWidth * s, Source.PixelHeight * s));
        dc.PushClip(new RectangleGeometry(new Rect(x, y, Source.PixelWidth * s, Source.PixelHeight * s)));
        if (Rois is not null) foreach (var roi in Rois)
        {
            if (roi.Width <= 0 || roi.Height <= 0) continue;
            var brush = roi == Selected ? Brushes.Orange : roi.Enabled ? Brushes.LimeGreen : Brushes.Gray;
            dc.DrawRectangle(null, new Pen(brush, roi == Selected ? 3 : 1.5), new Rect(x + roi.X * s, y + roi.Y * s, roi.Width * s, roi.Height * s));
            var label = new FormattedText(roi.Reference ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var p = new Point(x + roi.X * s, Math.Max(y, y + roi.Y * s - 19));
            dc.DrawRectangle(Brushes.Black, null, new Rect(p.X, p.Y, label.Width + 6, 18)); dc.DrawText(label, new Point(p.X + 3, p.Y));
        }
        if (start is Point a) dc.DrawRectangle(null, new Pen(Brushes.Orange, 2), new Rect(new Point(x + a.X * s, y + a.Y * s), new Point(x + end.X * s, y + end.Y * s)));
        dc.Pop();
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Source is null || Transform().Scale <= 0) return;
        var p = ImagePoint(e.GetPosition(this));
        if (AllowDrawing) { start = end = p; CaptureMouse(); }
        else if (Rois is not null) Selected = Rois.LastOrDefault(r => r.Width > 0 && r.Height > 0 && new Rect(r.X, r.Y, r.Width, r.Height).Contains(p));
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (start is null) return;
        end = ImagePoint(e.GetPosition(this)); InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (start is not Point a) return;
        end = ImagePoint(e.GetPosition(this));
        var rect = new Rect(a, end);
        start = null; ReleaseMouseCapture(); InvalidateVisual();
        if (rect.Width >= 2 && rect.Height >= 2) RegionDrawn?.Invoke(rect);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { start = null; InvalidateVisual(); base.OnLostMouseCapture(e); }
}
