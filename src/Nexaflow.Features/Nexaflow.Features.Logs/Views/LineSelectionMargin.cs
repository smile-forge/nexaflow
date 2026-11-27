using Nexaflow.Features.Logs.ViewModels;
using Nexaflow.Localization;
using Nexaflow.Visuals.Text.Markdown;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Logs.Views;

/// <summary>
/// A thin margin beside the log where a press picks a whole line out, or lets it go: a bar beside each line picked out, a lighter one
/// beside the line under the pointer. The lines are the page's, numbered as lines of the whole log, so a line stays picked out whatever is
/// read in before it or however a window onto a long log moves.
/// </summary>
public sealed class LineSelectionMargin : ContentMargin
{
    /// <summary>How wide it stands.</summary>
    private const double Wide = 14;

    private readonly LogViewModel _log;

    /// <summary>The line under the pointer, numbered as a line of the whole log — or null, where it is over none.</summary>
    private long? _hovered;

    // Themed (Log.Selection / Log.SelectionHover), resolved where it is drawn, so a theme switch retints it.
    private Brush SelectionBrush => (Brush)FindResource("Log.Selection");
    private Brush HoverBrush     => (Brush)FindResource("Log.SelectionHover");

    public LineSelectionMargin(LogViewModel log)
    {
        _log    = log;
        Cursor  = Cursors.Hand;
        ToolTip = Str.Get("Logs.Margin.SelectLineTip");
    }

    protected override Size MeasureOverride(Size availableSize) => new(Wide, 0);

    protected override void Draw(DrawingContext drawing, IReadOnlyList<ShownLine> lines)
    {
        // The whole of it takes a press, whatever is drawn on it.
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        foreach (var line in lines)
        {
            var number = Numbered(line);
            var brush = _log.IsLineSelected(number) ? SelectionBrush : number == _hovered ? HoverBrush : null;

            if (brush is not null) drawing.DrawRectangle(brush, null, new Rect(1, line.Top, Math.Max(0, ActualWidth - 2), line.Height));
        }
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);

        var hovered = LineAt(args.GetPosition(this).Y) is { } line ? Numbered(line) : (long?)null;
        if (hovered == _hovered) return;

        _hovered = hovered;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs args)
    {
        base.OnMouseLeave(args);

        _hovered = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        base.OnMouseLeftButtonDown(args);
        if (LineAt(args.GetPosition(this).Y) is not { } line) return;

        _log.ToggleLineSelection(Numbered(line));
        InvalidateVisual();
        args.Handled = true;
    }

    /// <summary><paramref name="line"/>, a line on the page, numbered as a line of the whole log from one.</summary>
    private long Numbered(ShownLine line) => _log.FirstLineNumber - 1 + line.Number;
}
