using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NotepadX.Controls;

/// <summary>
/// Draws logical line numbers beside a TextBox. Word wrap means one logical line can
/// occupy several visual lines, so a number is painted only on the visual line that
/// actually starts a logical one.
/// </summary>
public sealed class LineNumberGutter : FrameworkElement
{
    private TextBox? _editor;
    private int _lineCount = 1;
    private bool _retryPending;

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(LineNumberGutter),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentLineForegroundProperty =
        DependencyProperty.Register(nameof(CurrentLineForeground), typeof(Brush), typeof(LineNumberGutter),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeparatorBrushProperty =
        DependencyProperty.Register(nameof(SeparatorBrush), typeof(Brush), typeof(LineNumberGutter),
            new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush CurrentLineForeground
    {
        get => (Brush)GetValue(CurrentLineForegroundProperty);
        set => SetValue(CurrentLineForegroundProperty, value);
    }

    public Brush SeparatorBrush
    {
        get => (Brush)GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    public LineNumberGutter()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    public void Attach(TextBox? editor)
    {
        if (ReferenceEquals(_editor, editor)) return;

        if (_editor is not null)
        {
            _editor.TextChanged -= OnEditorTextChanged;
            _editor.SelectionChanged -= OnEditorInvalidated;
            _editor.SizeChanged -= OnEditorInvalidated;
            _editor.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnEditorScrolled));
        }

        _editor = editor;

        if (_editor is not null)
        {
            _editor.TextChanged += OnEditorTextChanged;
            _editor.SelectionChanged += OnEditorInvalidated;
            _editor.SizeChanged += OnEditorInvalidated;
            // The inner ScrollViewer is not reachable until the template is applied, but
            // its ScrollChanged is a routed event that reaches the TextBox itself.
            _editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnEditorScrolled));
            RecountLines();
        }

        Refresh();
    }

    /// <summary>Call after anything that changes metrics without changing text, such as zoom.</summary>
    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        RecountLines();
        Refresh();
    }

    private void OnEditorInvalidated(object? sender, EventArgs e) => InvalidateVisual();

    private void OnEditorScrolled(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    private void RecountLines()
    {
        if (_editor is null) { _lineCount = 1; return; }

        int count = 1;
        string text = _editor.Text;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') count++;

        _lineCount = count;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_editor is null || Visibility != Visibility.Visible) return new Size(0, 0);

        int digits = Math.Max(2, _lineCount.ToString(CultureInfo.InvariantCulture).Length);
        var sample = BuildText(new string('8', digits), Foreground);
        return new Size(sample.Width + 20, 0);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var editor = _editor;
        if (editor is null || editor.ActualHeight <= 0) return;

        double width = ActualWidth;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, ActualHeight));

        // Switching tabs reparents the editor, and until it has been laid out again its
        // line queries report an empty document while GetFirstVisibleLineIndex still
        // answers 0. Asking for line 0 of a zero-line box throws, so wait for the retry.
        int visualLines = LineCountOf(editor);
        if (visualLines <= 0) { ScheduleRetry(); return; }

        int firstVisual, lastVisual;
        try
        {
            firstVisual = editor.GetFirstVisibleLineIndex();
            lastVisual = editor.GetLastVisibleLineIndex();
        }
        catch (Exception)
        {
            ScheduleRetry();
            return;
        }

        if (firstVisual < 0 || lastVisual < firstVisual) { ScheduleRetry(); return; }
        if (firstVisual >= visualLines) { ScheduleRetry(); return; }
        lastVisual = Math.Min(lastVisual, visualLines - 1);

        string text = editor.Text;
        int firstChar = CharacterIndexOfLine(editor, firstVisual);
        if (firstChar < 0) { ScheduleRetry(); return; }

        // One O(n) pass to place the first visible line, then increment while walking down.
        int logical = 1;
        for (int i = 0; i < firstChar && i < text.Length; i++)
            if (text[i] == '\n') logical++;

        int caretLine = LogicalLineOf(text, editor.CaretIndex);

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, ActualHeight)));

        for (int visual = firstVisual; visual <= lastVisual; visual++)
        {
            int charIndex = CharacterIndexOfLine(editor, visual);
            if (charIndex < 0) break;

            if (visual > firstVisual)
            {
                int previous = CharacterIndexOfLine(editor, visual - 1);
                if (previous >= 0)
                {
                    for (int i = previous; i < charIndex && i < text.Length; i++)
                        if (text[i] == '\n') logical++;
                }
            }

            // A wrapped continuation does not start a logical line, so it gets no number.
            bool startsLogicalLine = charIndex == 0 || (charIndex - 1 < text.Length && text[charIndex - 1] == '\n');
            if (!startsLogicalLine) continue;

            var rect = editor.GetRectFromCharacterIndex(charIndex);
            if (rect.IsEmpty || double.IsInfinity(rect.Top)) continue;

            bool current = logical == caretLine;
            var formatted = BuildText(logical.ToString(CultureInfo.CurrentCulture),
                current ? CurrentLineForeground : Foreground);

            dc.DrawText(formatted, new Point(width - 10 - formatted.Width, rect.Top));
        }

        dc.Pop();

        var pen = new Pen(SeparatorBrush, 1);
        pen.Freeze();
        dc.DrawLine(pen, new Point(width - 0.5, 0), new Point(width - 0.5, ActualHeight));
    }

    /// <summary>Visual line count, or 0 when the editor has no usable layout yet.</summary>
    private static int LineCountOf(TextBox editor)
    {
        try
        {
            return Math.Max(0, editor.LineCount);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Returns -1 instead of throwing when the layout moves under us mid-render.</summary>
    private static int CharacterIndexOfLine(TextBox editor, int line)
    {
        try
        {
            return editor.GetCharacterIndexFromLineIndex(line);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>
    /// A render triggered by TextChanged can land before the editor has re-laid out, and
    /// the line queries then report nothing. One deferred repaint at layout priority
    /// covers that without turning into a render loop.
    /// </summary>
    private void ScheduleRetry()
    {
        if (_retryPending) return;
        _retryPending = true;

        Dispatcher.BeginInvoke(() =>
        {
            _retryPending = false;
            InvalidateVisual();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static int LogicalLineOf(string text, int index)
    {
        int line = 1;
        int end = Math.Clamp(index, 0, text.Length);
        for (int i = 0; i < end; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    private FormattedText BuildText(string value, Brush brush)
    {
        var editor = _editor;
        var typeface = editor is null
            ? new Typeface("Consolas")
            : new Typeface(editor.FontFamily, editor.FontStyle, editor.FontWeight, editor.FontStretch);

        return new FormattedText(
            value,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            editor?.FontSize ?? 14,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
