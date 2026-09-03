using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Text measurement shared by the diagram renderers.  Every renderer measured its own labels with a
/// private copy of the same <see cref="FormattedText"/> call; they live here once so a renderer that
/// needs a size agrees with every other, and so the canvas-measuring renderers and the layout-driven
/// ones can be reasoned about together.
///
/// This is the *pixel* measurement.  The Sugiyama layout deliberately uses a char-width heuristic
/// instead (<see cref="NodeLabelMetrics"/>, <see cref="ClassBoxMetrics"/>) so that the space it
/// reserves is stable and WPF-free — do not swap one for the other.
/// </summary>
internal static class DiagramText
{
    /// <summary>The face every diagram label is drawn in.</summary>
    internal static readonly FontFamily BodyFont = new("Segoe UI");

    /// <summary>Rendered width of one line of text.</summary>
    internal static double Measure(string text, double fontSize, FontWeight? weight = null)
    {
        var ft = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(BodyFont, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            fontSize, Brushes.Black, 1.0);
        return ft.Width;
    }

    /// <summary>Width of the widest line and the number of lines, for text carrying <c>\n</c> breaks.</summary>
    internal static (double w, int lines) MeasureBlock(string text, double fontSize)
    {
        var parts = text.Split('\n');
        double w = 0;
        foreach (var p in parts) w = Math.Max(w, Measure(p, fontSize));
        return (w, parts.Length);
    }

    /// <summary>Explicit line count (a <c>\n</c>-separated block is at least one line).</summary>
    internal static int LineCount(string text) => text.Split('\n').Length;
}
