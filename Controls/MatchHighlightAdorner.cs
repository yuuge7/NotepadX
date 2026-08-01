using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using NotepadX.Services;

namespace NotepadX.Controls;

/// <summary>
/// Paints every visible search match behind the text. A TextBox can only render one
/// selection, so the rest are drawn on the adorner layer above it in a translucent colour.
/// </summary>
public sealed class MatchHighlightAdorner : Adorner
{
    private readonly TextBox _editor;
    private IReadOnlyList<SearchHit> _hits = [];

    public Brush HighlightBrush { get; set; } = new SolidColorBrush(Color.FromArgb(80, 255, 200, 0));

    public MatchHighlightAdorner(TextBox editor) : base(editor)
    {
        _editor = editor;
        IsHitTestVisible = false;
    }

    public void SetHits(IReadOnlyList<SearchHit> hits)
    {
        _hits = hits;
        InvalidateVisual();
    }

    public void Clear() => SetHits([]);

    protected override void OnRender(DrawingContext dc)
    {
        if (_hits.Count == 0) return;

        double width = _editor.ActualWidth;
        double height = _editor.ActualHeight;
        if (width <= 0 || height <= 0) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));

        string text = _editor.Text;
        foreach (var hit in _hits)
        {
            if (hit.Index < 0 || hit.End > text.Length) continue;
            DrawHit(dc, text, hit, height);
        }

        dc.Pop();
    }

    /// <summary>A match can straddle wrapped lines, so it is painted one visual row at a time.</summary>
    private void DrawHit(DrawingContext dc, string text, SearchHit hit, double height)
    {
        int firstLine, lastLine;
        try
        {
            firstLine = _editor.GetLineIndexFromCharacterIndex(hit.Index);
            lastLine = _editor.GetLineIndexFromCharacterIndex(Math.Max(hit.Index, hit.End - 1));
        }
        catch (Exception)
        {
            return;
        }

        if (firstLine < 0 || lastLine < firstLine) return;

        for (int line = firstLine; line <= lastLine; line++)
        {
            int lineStart = _editor.GetCharacterIndexFromLineIndex(line);
            if (lineStart < 0) continue;

            int lineLength = _editor.GetLineLength(line);
            int segmentStart = Math.Max(hit.Index, lineStart);
            int segmentEnd = Math.Min(hit.End, lineStart + lineLength);
            if (segmentEnd <= segmentStart) continue;

            var left = _editor.GetRectFromCharacterIndex(segmentStart);
            var right = _editor.GetRectFromCharacterIndex(segmentEnd);
            if (left.IsEmpty || right.IsEmpty) continue;
            if (double.IsInfinity(left.Top) || double.IsInfinity(right.Left)) continue;
            if (left.Top > height || left.Top + left.Height < 0) continue;

            double w = Math.Max(1, right.Left - left.Left);
            dc.DrawRoundedRectangle(HighlightBrush, null,
                new Rect(left.Left, left.Top, w, left.Height), 2, 2);
        }
    }
}
