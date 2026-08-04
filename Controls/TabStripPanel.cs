using System.Windows;
using System.Windows.Controls;

namespace NotepadX.Controls;

/// <summary>
/// Lays tabs out browser-style: they share the strip's width, shrinking from
/// <see cref="MaxTabWidth"/> toward <see cref="MinTabWidth"/> as more open, so a
/// crowded strip stays inside the title bar instead of running off the edge.
/// Once the minimum is reached the panel overflows and the host ScrollViewer takes over.
/// </summary>
public sealed class TabStripPanel : Panel
{
    /// <summary>
    /// Width to divide between the tabs. The panel sits inside a horizontally
    /// scrolling ScrollViewer, so the measure constraint is infinite and useless;
    /// this is bound to the viewport instead.
    /// </summary>
    public static readonly DependencyProperty AvailableWidthProperty =
        DependencyProperty.Register(nameof(AvailableWidth), typeof(double), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MinTabWidthProperty =
        DependencyProperty.Register(nameof(MinTabWidth), typeof(double), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(92.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxTabWidthProperty =
        DependencyProperty.Register(nameof(MaxTabWidth), typeof(double), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double AvailableWidth
    {
        get => (double)GetValue(AvailableWidthProperty);
        set => SetValue(AvailableWidthProperty, value);
    }

    public double MinTabWidth
    {
        get => (double)GetValue(MinTabWidthProperty);
        set => SetValue(MinTabWidthProperty, value);
    }

    public double MaxTabWidth
    {
        get => (double)GetValue(MaxTabWidthProperty);
        set => SetValue(MaxTabWidthProperty, value);
    }

    private double _tabWidth;

    private double TabWidthFor(int count)
    {
        double budget = AvailableWidth;
        if (budget <= 0 || double.IsInfinity(budget) || double.IsNaN(budget))
            budget = MaxTabWidth * count;

        return Math.Max(MinTabWidth, Math.Min(MaxTabWidth, budget / count));
    }

    protected override Size MeasureOverride(Size constraint)
    {
        int count = InternalChildren.Count;
        if (count == 0)
        {
            _tabWidth = 0;
            return new Size(0, 0);
        }

        _tabWidth = TabWidthFor(count);

        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(_tabWidth, constraint.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(_tabWidth * count, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = InternalChildren.Count;
        if (count == 0) return finalSize;

        // finalSize.Width is the panel's own desired width, not the viewport, so the
        // measured width is reused rather than re-divided here.
        double width = _tabWidth > 0 ? _tabWidth : TabWidthFor(count);

        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return new Size(x, finalSize.Height);
    }
}
