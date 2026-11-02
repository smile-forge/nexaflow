using System.Collections.Immutable;

namespace Nexaflow.IO.Common.OpenRaster;

/// <summary>
/// One element of an OpenRaster layer stack (<c>stack.xml</c>), with the baseline attributes every kind shares as OpenRaster
/// 0.0.6 and Krita write them, and what the element held that the baseline does not name, kept as written.
/// </summary>
public abstract record OpenRasterEntry
{
    public string Name { get; init; } = "";

    /// <summary>0…1.</summary>
    public double Opacity { get; init; } = 1;

    public bool Visible { get; init; } = true;

    /// <summary>The <c>composite-op</c> as written; null where none is, which is <c>svg:src-over</c>.</summary>
    public string? CompositeOp { get; init; }

    /// <summary><c>selected="true"</c>: one of the layers the application that wrote it had active.</summary>
    public bool Selected { get; init; }

    /// <summary><c>edit-locked="true"</c>: locked against edits.</summary>
    public bool EditLocked { get; init; }

    /// <summary>Attributes and child elements the baseline does not name — another application's, or Nexaflow's own.</summary>
    public OpenRasterKept Kept { get; init; } = OpenRasterKept.None;
}

/// <summary>A <c>layer</c>: a PNG in the package, placed with its top-left at (<see cref="X"/>, <see cref="Y"/>).</summary>
public sealed record OpenRasterLayerEntry : OpenRasterEntry
{
    /// <summary>The PNG's path inside the package (<c>src</c>).</summary>
    public string Source { get; init; } = "";

    public int X { get; init; }

    public int Y { get; init; }

    /// <summary><c>alpha-preserve="true"</c>: Krita's inherit alpha beside a blend other than Normal.</summary>
    public bool AlphaPreserve { get; init; }
}

/// <summary>A <c>stack</c>: layers held together, the uppermost first, composited on a surface of their own
/// (<c>isolation="isolate"</c>) or straight onto what lies beneath (<c>auto</c>).</summary>
public sealed record OpenRasterStackEntry : OpenRasterEntry
{
    /// <summary>The uppermost first, as the file holds them.</summary>
    /// <remarks>Never default: an empty stack holds none.</remarks>
    public ImmutableArray<OpenRasterEntry> Children
    {
        get => _children.IsDefault ? [] : _children;
        init => _children = value;
    }

    private readonly ImmutableArray<OpenRasterEntry> _children = [];

    /// <summary><c>isolate</c> (the default) or <c>auto</c>.</summary>
    public string Isolation { get; init; } = "isolate";

    public bool Isolated => !string.Equals(Isolation, "auto", StringComparison.OrdinalIgnoreCase);

    /// <summary>The offset a stack was written with — deprecated since 0.0.6, read as written and never written.</summary>
    public int X { get; init; }

    public int Y { get; init; }
}

/// <summary>An element the baseline does not name in a stack — Krita's <c>filter</c>, say — kept as written, in its place.</summary>
public sealed record OpenRasterOtherEntry : OpenRasterEntry
{
    /// <summary>Its element's own name (<c>filter</c>).</summary>
    public string ElementName { get; init; } = "";

    /// <summary>Its <c>type</c>, where it has one (<c>applications:krita:levels</c>).</summary>
    public string? Type { get; init; }

    /// <summary>The element, as written.</summary>
    public string Element { get; init; } = "";
}

/// <summary>The <c>image</c> of a <c>stack.xml</c>: the canvas's size, its resolution where given, the format version it was
/// written as, its root stack, and what else it held, kept as written.</summary>
public sealed record OpenRasterImage
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Pixels per inch across and down, where the file gives them.</summary>
    public double? XResolution { get; init; }

    public double? YResolution { get; init; }

    public string Version { get; init; } = OpenRasterPackage.WrittenVersion;

    public OpenRasterStackEntry Root { get; init; } = new();

    public OpenRasterKept Kept { get; init; } = OpenRasterKept.None;
}
