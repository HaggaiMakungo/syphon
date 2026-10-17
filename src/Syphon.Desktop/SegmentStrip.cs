using System.Windows;
using System.Windows.Media;
using Syphon.Engine;

namespace Syphon.Desktop;

/// <summary>Committed progress for each real transfer segment, in file order.</summary>
public sealed class SegmentStrip : FrameworkElement
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(nameof(Snapshot), typeof(StoredDownload), typeof(SegmentStrip), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public StoredDownload? Snapshot { get => (StoredDownload?)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs args) => ((SegmentStrip)owner).Visibility = args.NewValue is StoredDownload task && task.Segments.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
    public SegmentStrip() { Height = 4; Visibility = Visibility.Collapsed; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (Snapshot?.Segments is not { Length: > 1 } segments || ActualWidth <= 0) return;
        var track = (Brush)FindResource("Muted"); var fill = (Brush)FindResource("Accent");
        var width = Math.Max(0, (ActualWidth - (segments.Length - 1) * 4) / segments.Length);
        foreach (var (part, index) in segments.OrderBy(s => s.Start).Select((part, index) => (part, index))) {
            var left = index * (width + 4); drawing.DrawRoundedRectangle(track, null, new Rect(left, 0, width, ActualHeight), 2, 2);
            var fraction = part.Length > 0 ? Math.Clamp((double)part.Committed / part.Length, 0, 1) : 0;
            drawing.DrawRoundedRectangle(fill, null, new Rect(left, 0, width * fraction, ActualHeight), 2, 2);
        }
    }
}
