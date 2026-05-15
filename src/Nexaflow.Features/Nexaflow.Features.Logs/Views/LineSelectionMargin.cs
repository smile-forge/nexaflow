using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Logs.Views;

/// <summary>
/// A thin left margin that lets the user click to toggle whole-line selection.
/// Selected lines are indicated by a coloured bar; hovered lines show a lighter
/// preview. Attach via <c>Editor.TextArea.LeftMargins.Insert(0, margin)</c>.
/// </summary>
public sealed class LineSelectionMargin : AbstractMargin
{
    private static readonly SolidColorBrush SelectionBrush =
        new(Color.FromArgb(100, 100, 160, 255));
    private static readonly SolidColorBrush HoverBrush =
        new(Color.FromArgb(50, 100, 160, 255));

    private readonly Action<int>             _toggleSelection;
    private readonly Func<IReadOnlySet<int>> _getSelected;
    private int? _hoveredLine;

    public LineSelectionMargin(
        Action<int>             toggleSelection,
        Func<IReadOnlySet<int>> getSelected)
    {
        _toggleSelection = toggleSelection;
        _getSelected     = getSelected;
        Cursor           = Cursors.Hand;
        ToolTip          = "Click to select/deselect line";
    }

    // ── AbstractMargin wiring ─────────────────────────────────────────────────

    // Subscribe to the TextView events that require a repaint so the margin
    // stays in sync as the user scrolls or the document changes.
    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged  -= OnRedrawRequired;
            oldTextView.ScrollOffsetChanged -= OnRedrawRequired;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged  += OnRedrawRequired;
            newTextView.ScrollOffsetChanged += OnRedrawRequired;
        }
        InvalidateVisual();
    }

    private void OnRedrawRequired(object? sender, EventArgs e) => InvalidateVisual();

    // Ensure clicks register even when we draw no background in some areas.
    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        => new PointHitTestResult(this, hitTestParameters.HitPoint);

    // ── Layout ────────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize) => new(14, 0);

    // ── Rendering ─────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext drawingContext)
    {
        var tv = TextView;
        if (tv is null) return;
        tv.EnsureVisualLines();

        var selected    = _getSelected();
        var renderWidth = ActualWidth;

        foreach (var vl in tv.VisualLines)
        {
            var lineNum = vl.FirstDocumentLine.LineNumber;
            var top     = vl.GetTextLineVisualYPosition(vl.TextLines[0], VisualYPosition.TextTop)
                          - tv.ScrollOffset.Y;
            var height  = vl.Height;
            var rect    = new Rect(1, top, renderWidth - 2, height);

            if (selected.Contains(lineNum))
                drawingContext.DrawRectangle(SelectionBrush, null, rect);
            else if (lineNum == _hoveredLine)
                drawingContext.DrawRectangle(HoverBrush, null, rect);
        }
    }

    // ── Mouse events ──────────────────────────────────────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var line = HitLine(e.GetPosition(this));
        if (line != _hoveredLine) { _hoveredLine = line; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hoveredLine = null;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var lineNum = HitLine(e.GetPosition(this));
        if (lineNum is null) return;

        _toggleSelection(lineNum.Value);

        InvalidateVisual();
        e.Handled = true;
    }

    // ── Hit-testing ───────────────────────────────────────────────────────────

    private int? HitLine(Point marginPoint)
    {
        var tv = TextView;
        if (tv is null) return null;

        // Translate to TextView viewport coordinates, then add scroll offset to get
        // document Y — visual line positions are in document (not viewport) space.
        var posInTv   = this.TranslatePoint(marginPoint, tv);
        var documentY = posInTv.Y + tv.ScrollOffset.Y;

        foreach (var vl in tv.VisualLines)
        {
            var top    = vl.GetTextLineVisualYPosition(vl.TextLines[0], VisualYPosition.TextTop);
            var bottom = top + vl.Height;
            if (documentY >= top && documentY < bottom)
                return vl.FirstDocumentLine.LineNumber;
        }
        return null;
    }
}
