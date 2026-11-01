using System.Collections.Immutable;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.IO.Common.OpenRaster;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>
/// How a layer is composited onto what lies beneath it, as OpenRaster names it (<c>composite-op</c>) — kept as written, so an
/// operation another application wrote that Nexaflow does not know is written back as it was read.
/// </summary>
public readonly record struct LayerBlend(string Op)
{
    public static LayerBlend Normal { get; } = new("svg:src-over");

    /// <summary>OpenRaster's operations, as Nexaflow offers them, each with the Skia blend that draws it.</summary>
    public static IReadOnlyList<(LayerBlend Blend, SKBlendMode Skia)> Offered { get; } =
    [
        (Normal, SKBlendMode.SrcOver),
        (new("svg:multiply"), SKBlendMode.Multiply),
        (new("svg:screen"), SKBlendMode.Screen),
        (new("svg:overlay"), SKBlendMode.Overlay),
        (new("svg:darken"), SKBlendMode.Darken),
        (new("svg:lighten"), SKBlendMode.Lighten),
        (new("svg:color-dodge"), SKBlendMode.ColorDodge),
        (new("svg:color-burn"), SKBlendMode.ColorBurn),
        (new("svg:hard-light"), SKBlendMode.HardLight),
        (new("svg:soft-light"), SKBlendMode.SoftLight),
        (new("svg:difference"), SKBlendMode.Difference),
        (new("svg:hue"), SKBlendMode.Hue),
        (new("svg:saturation"), SKBlendMode.Saturation),
        (new("svg:color"), SKBlendMode.Color),
        (new("svg:luminosity"), SKBlendMode.Luminosity),
        (new("svg:plus"), SKBlendMode.Plus),
        (new("svg:dst-in"), SKBlendMode.DstIn),
        (new("svg:dst-out"), SKBlendMode.DstOut),
        (new("svg:dst-atop"), SKBlendMode.DstATop),
    ];

    /// <summary>One of OpenRaster's own operations, which every reader draws.</summary>
    public bool IsOffered
    {
        get
        {
            foreach (var (blend, _) in Offered)
                if (blend == this) return true;
            return false;
        }
    }

    /// <summary>
    /// The Skia blend that draws it: OpenRaster's own by the table; another application's (<c>krita:exclusion</c>) by the Skia
    /// blend of the same name where there is one; else Normal.
    /// </summary>
    public SKBlendMode ToSkia()
    {
        foreach (var (blend, skia) in Offered)
            if (blend == this) return skia;
        int colon = (Op ?? "").IndexOf(':');
        var name = (colon >= 0 ? Op![(colon + 1)..] : Op ?? "").Replace("-", "", StringComparison.Ordinal);
        return Enum.TryParse<SKBlendMode>(name, ignoreCase: true, out var named) ? named : SKBlendMode.SrcOver;
    }
}

/// <summary>
/// A layer's mask: 8-bit coverage over the canvas — white shows the layer, black hides it, grey shows it partly — with
/// whether it is used (<see cref="Enabled"/>) and whether it moves and transforms with its layer (<see cref="Linked"/>).
/// </summary>
public sealed record LayerMask(TiledCoverage Coverage, bool Enabled = true, bool Linked = true);

/// <summary>Where a picture layer's finished picture lies on the canvas: its top-left, its scale, and its turn about that corner.</summary>
public readonly record struct LayerPlacement(double OffsetX, double OffsetY, double Scale = 1, double RotationDegrees = 0)
{
    public static LayerPlacement Origin { get; } = new(0, 0);

    /// <summary>The picture's pixels to canvas pixels.</summary>
    public SKMatrix ToMatrix()
        => SKMatrix.CreateScale((float)Scale, (float)Scale)
                   .PostConcat(SKMatrix.CreateRotationDegrees((float)RotationDegrees))
                   .PostConcat(SKMatrix.CreateTranslation((float)OffsetX, (float)OffsetY));
}

/// <summary>The picture a picture layer shows: a file the file system reads — a photo, or the copy a package keeps — and
/// what it was when the layer was made.</summary>
public sealed record PictureSource(string Path, SourceFingerprint? Fingerprint);

/// <summary>What shows beneath every layer — shown and composited into a save, but not a layer.</summary>
public sealed record LayerBackground(uint Argb)
{
    public static LayerBackground Transparent { get; } = new(0x00000000);
    public static LayerBackground White { get; } = new(0xFFFFFFFF);

    public bool IsTransparent => (Argb >> 24) == 0;

    public SKColor Colour => new(Argb);
}

/// <summary>
/// One layer of a layered picture: what every kind shares — its identity, name, whether it shows, how opaque it is, how it is
/// blended, whether it is locked against edits or its transparency against paint, whether it shows only where the layers
/// beneath it in its group do, and its mask.
/// </summary>
public abstract record Layer
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = "";

    public bool Visible { get; init; } = true;

    /// <summary>0…1.</summary>
    public double Opacity { get; init; } = 1;

    public LayerBlend Blend { get; init; } = LayerBlend.Normal;

    /// <summary>No edit reaches it.</summary>
    public bool Locked { get; init; }

    /// <summary>Paint keeps the transparency it has.</summary>
    public bool AlphaLocked { get; init; }

    /// <summary>Drawn only where the layers beneath it in its group are.</summary>
    public bool ClipsToBelow { get; init; }

    public LayerMask? Mask { get; init; }

    /// <summary>What the file it was read from said of it that Nexaflow does not understand, written back as it was.</summary>
    public OpenRasterKept Kept { get; init; } = OpenRasterKept.None;
}

/// <summary>A picture — a photo, by its file — with its recipe, in its own coordinates, placed on the canvas.</summary>
public sealed record PictureLayer : Layer
{
    public required PictureSource Source { get; init; }

    public LayerPlacement Placement { get; init; } = LayerPlacement.Origin;

    public ImageRecipe Recipe { get; init; } = ImageRecipe.Identity;
}

/// <summary>Pixels, in canvas coordinates: what paint, paste and fill make.</summary>
public sealed record PixelLayer : Layer
{
    public TiledPixels Pixels { get; init; } = TiledPixels.Empty;
}

/// <summary>Annotations, in canvas pixels, as a recipe holds them.</summary>
public sealed record MarkupLayer : Layer
{
    /// <remarks>Never default: a layer with no annotations has none.</remarks>
    public ImmutableArray<MarkupElement> Elements
    {
        get => _elements.IsDefault ? [] : _elements;
        init => _elements = value;
    }

    private readonly ImmutableArray<MarkupElement> _elements = [];
}

/// <summary>Layers held together, bottom first: composited onto a surface of their own (<see cref="Isolated"/>), or straight
/// onto what lies beneath the group, its opacity multiplied in (pass-through).</summary>
public sealed record GroupLayer : Layer
{
    /// <remarks>Never default: an empty group holds none.</remarks>
    public ImmutableArray<Layer> Children
    {
        get => _children.IsDefault ? [] : _children;
        init => _children = value;
    }

    private readonly ImmutableArray<Layer> _children = [];

    public bool Isolated { get; init; } = true;
}

/// <summary>
/// A layer another application made that no reader can draw — an adjustment named by its application, its settings not in
/// the file — listed under its name, left out of what Nexaflow draws, and written back as it was read.
/// </summary>
public sealed record UndrawableLayer : Layer
{
    /// <summary>What it is, as its application named it (<c>applications:krita:levels</c>).</summary>
    public string Kind { get; init; } = "";

    /// <summary>Its element, as written.</summary>
    public string Element { get; init; } = "";
}
