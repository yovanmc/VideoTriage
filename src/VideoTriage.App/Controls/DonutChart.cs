using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VideoTriage.App.ViewModels;

namespace VideoTriage.App.Controls;

public sealed record DonutSlice(double StartAngle, double SweepAngle, string Color);

public sealed class DonutChart : FrameworkElement
{
    private static readonly Dictionary<string, Brush> _brushCache = new();
    private static readonly BrushConverter _brushConverter = new();

    private static Brush GetBrush(string color)
    {
        if (!_brushCache.TryGetValue(color, out var brush))
        {
            brush = (Brush)_brushConverter.ConvertFromString(color)!;
            brush.Freeze();
            _brushCache[color] = brush;
        }
        return brush;
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IReadOnlyList<SummarySegment>),
            typeof(DonutChart),
            new FrameworkPropertyMetadata(
                Array.Empty<SummarySegment>(),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<SummarySegment> ItemsSource
    {
        get => (IReadOnlyList<SummarySegment>)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    // Drives the sweep-in reveal: each slice's start and sweep angle is scaled by this value
    // so the ring grows clockwise from nothing to full. Defaults to 1.0 (fully drawn) so the
    // chart still renders correctly if the animation never runs (e.g. in tests).
    public static readonly DependencyProperty AnimatedProgressProperty =
        DependencyProperty.Register(
            nameof(AnimatedProgress),
            typeof(double),
            typeof(DonutChart),
            new FrameworkPropertyMetadata(
                1.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public double AnimatedProgress
    {
        get => (double)GetValue(AnimatedProgressProperty);
        set => SetValue(AnimatedProgressProperty, value);
    }

    public DonutChart()
    {
        Loaded += (_, _) =>
        {
            BeginAnimation(
                AnimatedProgressProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        };
    }

    public static IReadOnlyList<DonutSlice> BuildSlices(
        IReadOnlyList<SummarySegment> segments)
    {
        var positive = segments.Where(x => x.Count > 0).ToArray();
        var total = positive.Sum(x => x.Count);
        if (total == 0)
            return [new DonutSlice(0, 360, "#3A3F4B")];

        var start = 0d;
        var slices = new List<DonutSlice>();
        foreach (var segment in positive)
        {
            var sweep = 360d * segment.Count / total;
            slices.Add(new DonutSlice(start, sweep, segment.Color));
            start += sweep;
        }

        return slices;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 4);
        var thickness = Math.Max(8, radius * 0.28);
        var progress = Math.Clamp(AnimatedProgress, 0, 1);

        foreach (var slice in BuildSlices(ItemsSource))
        {
            var sweep = slice.SweepAngle * progress;
            if (sweep <= 0)
                continue;

            drawingContext.DrawGeometry(
                null,
                new Pen(GetBrush(slice.Color), thickness)
                {
                    StartLineCap = PenLineCap.Flat,
                    EndLineCap = PenLineCap.Flat
                },
                Arc(center, radius - thickness / 2, slice.StartAngle * progress, sweep));
        }
    }

    private static Geometry Arc(
        Point center,
        double radius,
        double startAngle,
        double sweepAngle)
    {
        if (sweepAngle >= 359.999)
            return new EllipseGeometry(center, radius, radius);

        Point At(double degrees)
        {
            var radians = (degrees - 90) * Math.PI / 180;
            return new Point(
                center.X + radius * Math.Cos(radians),
                center.Y + radius * Math.Sin(radians));
        }

        var figure = new PathFigure { StartPoint = At(startAngle), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = At(startAngle + sweepAngle),
            Size = new Size(radius, radius),
            IsLargeArc = sweepAngle > 180,
            SweepDirection = SweepDirection.Clockwise
        });
        return new PathGeometry([figure]);
    }
}
