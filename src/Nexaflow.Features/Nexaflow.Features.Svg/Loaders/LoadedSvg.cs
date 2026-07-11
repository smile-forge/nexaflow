using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Svg.Loaders;

/// <summary>
/// The result of loading an SVG file: a <b>frozen</b> <see cref="DrawingImage"/> ready to bind to an
/// <c>Image</c>, plus the metadata the footer shows. Built off the UI thread by <see cref="SvgLoader"/>;
/// <see cref="Image"/> must be frozen so it can cross to the UI thread.
/// </summary>
public sealed class LoadedSvg
{
    /// <summary>The frozen vector artwork. An empty drawing if the file held nothing renderable.</summary>
    public required DrawingImage Image { get; init; }

    /// <summary>The rendered extent in device-independent pixels — the fallback "actual size" when the
    /// document's own width/height are percentages or absent.</summary>
    public required Rect Bounds { get; init; }

    /// <summary>The authored root <c>width</c>/<c>height</c> attributes, verbatim (e.g. "512", "24px",
    /// "100%"); null when the document omits them.</summary>
    public string? Width { get; init; }
    public string? Height { get; init; }

    /// <summary>The authored <c>viewBox</c> attribute, verbatim; null when absent.</summary>
    public string? ViewBox { get; init; }

    /// <summary>Count of drawable shape elements (path/rect/circle/ellipse/line/polyline/polygon/text/
    /// image/use) — a rough "how much is in here" figure for the footer.</summary>
    public int ElementCount { get; init; }
}
