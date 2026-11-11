using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DiskLens.Core;

namespace DiskLens.App.Controls;

// A bounded binary treemap. Rendering is independent of the full directory tree size.
public sealed class SpaceMap : FrameworkElement
{
    public static readonly DependencyProperty NodeProperty = DependencyProperty.Register(nameof(Node), typeof(DirectoryNode), typeof(SpaceMap), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public DirectoryNode? Node { get => (DirectoryNode?)GetValue(NodeProperty); set => SetValue(NodeProperty, value); }
    public event Action<DirectoryNode>? Navigate;
    private readonly List<(Rect bounds, DirectoryNode? node, string title, long bytes)> hits = [];
    private static readonly string[] Palette = ["#5373DC", "#6388DC", "#5EA7B7", "#7A8ACB", "#98ACDC", "#729DC6", "#A3BFCE", "#B0ABCE"];
    public SpaceMap() { ClipToBounds = true; MinHeight = 200; Focusable = true; Cursor = Cursors.Hand; }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); hits.Clear();
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Node == null || Node.TotalBytes == 0) { DrawLabel(dc, "扫描后，空间分布会显示在这里", new Rect(0, 0, ActualWidth, ActualHeight), new SolidColorBrush(Color.FromRgb(115, 128, 150)), 13); return; }
        var ordered = Node.Children.Where(x => x.TotalBytes > 0).OrderByDescending(x => x.TotalBytes).ToList();
        var entries = ordered.Take(60).Select(x => new MapEntry(x, x.Name, x.TotalBytes)).ToList();
        var rest = ordered.Skip(60).Sum(x => x.TotalBytes);
        if (rest > 0) entries.Add(new(null, "其他文件夹", rest));
        if (Node.DirectBytes > 0) entries.Add(new(null, "本层文件", Node.DirectBytes));
        entries = entries.OrderByDescending(x => x.Bytes).ToList();
        Layout(dc, entries, new Rect(0, 0, ActualWidth, ActualHeight), 0);
    }

    private void Layout(DrawingContext dc, List<MapEntry> entries, Rect bounds, int index)
    {
        if (entries.Count == 0 || bounds.Width < 1 || bounds.Height < 1) return;
        if (entries.Count == 1)
        {
            var entry = entries[0];
            var box = new Rect(bounds.X + 2, bounds.Y + 2, Math.Max(0, bounds.Width - 4), Math.Max(0, bounds.Height - 4));
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Palette[index % Palette.Length]));
            dc.DrawRoundedRectangle(brush, null, box, 5, 5);
            hits.Add((box, entry.Node, entry.Title, entry.Bytes));
            if (box.Width > 60 && box.Height > 42)
            {
                DrawLabel(dc, entry.Title, new Rect(box.X + 12, box.Y + 10, Math.Max(0, box.Width - 24), 24), Brushes.White, 13);
                if (box.Height > 65) DrawLabel(dc, SizeFormatter.Format(entry.Bytes), new Rect(box.X + 12, box.Y + 34, Math.Max(0, box.Width - 24), 24), Brushes.White, 17);
            }
            return;
        }
        var total = entries.Sum(x => (double)x.Bytes);
        double partial = 0; var split = 0;
        do { partial += entries[split++].Bytes; } while (split < entries.Count - 1 && partial < total / 2);
        var ratio = partial / total;
        if (bounds.Width >= bounds.Height)
        {
            var width = bounds.Width * ratio;
            Layout(dc, entries.GetRange(0, split), new Rect(bounds.X, bounds.Y, width, bounds.Height), index);
            Layout(dc, entries.GetRange(split, entries.Count - split), new Rect(bounds.X + width, bounds.Y, bounds.Width - width, bounds.Height), index + split);
        }
        else
        {
            var height = bounds.Height * ratio;
            Layout(dc, entries.GetRange(0, split), new Rect(bounds.X, bounds.Y, bounds.Width, height), index);
            Layout(dc, entries.GetRange(split, entries.Count - split), new Rect(bounds.X, bounds.Y + height, bounds.Width, bounds.Height - height), index + split);
        }
    }

    private void DrawLabel(DrawingContext dc, string text, Rect box, Brush brush, double size)
    {
        if (box.Width <= 0) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = box.Width, MaxTextHeight = Math.Max(size * 1.5, box.Height), Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(formatted, box.TopLeft);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var hit = hits.FirstOrDefault(x => x.bounds.Contains(point));
        ToolTip = hit.title == null ? null : $"{hit.node?.FullPath ?? hit.title}\n{SizeFormatter.Format(hit.bytes)}";
        Cursor = hit.node != null ? Cursors.Hand : Cursors.Arrow;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var hit = hits.FirstOrDefault(x => x.bounds.Contains(e.GetPosition(this)));
        if (hit.node != null) Navigate?.Invoke(hit.node);
    }
    private sealed record MapEntry(DirectoryNode? Node, string Title, long Bytes);
}
