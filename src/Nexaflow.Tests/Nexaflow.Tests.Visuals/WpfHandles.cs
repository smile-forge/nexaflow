using System.Windows.Media;
using Nexaflow.Markdown.Layout;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals;

/// <summary>
/// WPF's own brushes and outlines as the medium's handles, and back — what a test building a layout by hand, or asking what one
/// drew, says them as. The medium hands out one ink for one brush, so an ink a test makes from a brush is the same ink a builder
/// made from it, and comparing the two compares the brushes.
/// </summary>
internal static class WpfHandles
{
    /// <summary>The medium everything a test lays here is laid in.</summary>
    public static WpfMedium Medium => WpfMedium.OnThisThread;

    /// <summary>A brush as the medium's ink.</summary>
    public static LayoutInk Ink(this Brush brush) => Medium.Ink(brush);

    /// <summary>An outline as the medium's shape.</summary>
    public static LayoutShape Shape(this Geometry geometry) => WpfShape.Of(geometry);

    /// <summary>A shape the medium made, as the geometry WPF draws.</summary>
    public static Geometry Geometry(this LayoutShape shape) => ((WpfShape)shape).Geometry;
}
