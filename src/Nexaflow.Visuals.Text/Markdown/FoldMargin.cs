using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A handle beside each line a fold starts on — the content's own say of what can be folded, a method's body, a comment block — pointing
/// down while what it folds shows and across while it is folded away; a press folds it or opens it again, through the view of the
/// document (<see cref="Nexaflow.Markdown.Editing.ContentEditor.Folds"/>), so what is folded is the content's, not the margin's.
/// </summary>
public sealed class FoldMargin : ContentMargin
{
    /// <summary>How wide the column of handles is.</summary>
    private const double Wide = 14;

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(FoldMargin),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>What the handles are drawn in: the theme's muted text, unless set.</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FoldMargin()
    {
        SetResourceReference(ForegroundProperty, "TextMutedBrush");
        Cursor = Cursors.Hand;
    }

    protected override Size MeasureOverride(Size availableSize) => Surface is null ? default : new Size(Wide, 0);

    protected override void Draw(DrawingContext drawing, IReadOnlyList<ShownLine> lines)
    {
        if (Surface is not { } surface) return;

        var ink = Foreground ?? surface.TypedIn.Ink;
        var pen = new Pen(ink, 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();

        var offered = surface.FoldsOffered();
        var folds = surface.Editor.Folds;

        foreach (var line in lines)
        {
            if (Starting(surface, offered, line) is not { } fold) continue;

            var size = Math.Min(line.Height, surface.TypedIn.Size) * 0.5;
            var (x, y) = (Wide / 2, line.Top + (Math.Min(line.Height, surface.TypedIn.Size * 1.3) / 2));

            // Down, while what it folds shows; across, while it is folded away.
            var turned = folds.IsFolded(fold.Start);
            var chevron = turned
                ? new[] { new Point(x - (size / 4), y - (size / 2)), new Point(x + (size / 4), y), new Point(x - (size / 4), y + (size / 2)) }
                : new[] { new Point(x - (size / 2), y - (size / 4)), new Point(x, y + (size / 4)), new Point(x + (size / 2), y - (size / 4)) };

            drawing.DrawLine(pen, chevron[0], chevron[1]);
            drawing.DrawLine(pen, chevron[1], chevron[2]);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        base.OnMouseLeftButtonDown(args);
        if (Surface is not { } surface || LineAt(args.GetPosition(this).Y) is not { } line) return;
        if (Starting(surface, surface.FoldsOffered(), line) is not { } fold) return;

        surface.Editor.Folds.Toggle(fold.Start, fold.End);
        args.Handled = true;
    }

    /// <summary>The outermost fold the content offers starting on <paramref name="line"/>, or null where none does.</summary>
    private static (int Start, int End)? Starting(NexaTextSurface surface, IReadOnlyList<(int Start, int End)> offered, ShownLine line)
    {
        if (offered.Count == 0) return null;

        var document = surface.Document.Current;
        var (start, end) = document.LineAt(document.OffsetOf(line.Number - 1));

        // The first offered starting on or after the line's start; the outermost of those on the line is the one that reaches furthest.
        var (low, high) = (0, offered.Count);
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (offered[middle].Start < start) low = middle + 1;
            else high = middle;
        }

        (int Start, int End)? found = null;
        for (var index = low; index < offered.Count && offered[index].Start <= end; index++)
            if (document.LineOf(offered[index].End) > line.Number - 1 && (found is null || offered[index].End > found.Value.End)) found = offered[index];

        return found;
    }
}
