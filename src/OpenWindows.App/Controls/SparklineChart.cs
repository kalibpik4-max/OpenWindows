using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace OpenWindows.Controls;

public sealed class SparklineChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IEnumerable),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinValueProperty = DependencyProperty.Register(
        nameof(MinValue),
        typeof(double),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue),
        typeof(double),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double MinValue
    {
        get => (double)GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (SparklineChart)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= chart.OnCollectionChanged;
        }
        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += chart.OnCollectionChanged;
        }
        chart.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2 || Values == null)
        {
            return;
        }

        var points = new List<double>();
        foreach (var value in Values)
        {
            try
            {
                points.Add(Convert.ToDouble(value));
            }
            catch
            {
            }
        }
        if (points.Count < 2)
        {
            return;
        }

        var strokeBrush = Stroke
            ?? (TryFindResource("SystemAccentColorPrimaryBrush") as Brush)
            ?? new SolidColorBrush(Color.FromRgb(0x4F, 0x6B, 0xFF));
        var strokeColor = strokeBrush is SolidColorBrush solid
            ? solid.Color
            : Color.FromRgb(0x4F, 0x6B, 0xFF);

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(22, 128, 128, 128)), 1);
        gridPen.Freeze();
        for (var i = 1; i <= 3; i++)
        {
            var y = height * i / 4.0;
            drawingContext.DrawLine(gridPen, new Point(0, y), new Point(width, y));
        }

        var min = MinValue;
        var max = MaxValue;
        if (max - min < 0.001)
        {
            max = min + 1;
        }

        var step = width / (points.Count - 1);
        var mapped = new List<Point>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var value = Math.Clamp(points[i], min, max);
            var x = i * step;
            var y = height - (value - min) / (max - min) * height;
            mapped.Add(new Point(x, y));
        }

        var fillGeometry = new StreamGeometry();
        using (var context = fillGeometry.Open())
        {
            context.BeginFigure(new Point(mapped[0].X, height), true, true);
            foreach (var point in mapped)
            {
                context.LineTo(point, true, false);
            }
            context.LineTo(new Point(mapped[^1].X, height), true, false);
        }
        fillGeometry.Freeze();

        var fillBrush = new LinearGradientBrush(
            Color.FromArgb(80, strokeColor.R, strokeColor.G, strokeColor.B),
            Color.FromArgb(0, strokeColor.R, strokeColor.G, strokeColor.B),
            new Point(0, 0),
            new Point(0, 1));
        fillBrush.Freeze();
        drawingContext.DrawGeometry(fillBrush, null, fillGeometry);

        var lineGeometry = new StreamGeometry();
        using (var context = lineGeometry.Open())
        {
            context.BeginFigure(mapped[0], false, false);
            foreach (var point in mapped.Skip(1))
            {
                context.LineTo(point, true, false);
            }
        }
        lineGeometry.Freeze();

        var linePen = new Pen(strokeBrush, 1.8)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        drawingContext.DrawGeometry(null, linePen, lineGeometry);
    }
}
