using System.Collections.Immutable;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.IO.Common.OpenRaster;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>
/// How a layer is composited onto what lies beneath it, as OpenRaster names it (<c>composite-op</c>) — kept as written, so an
/// operation another application wrote that Nexaflow does not know is written back as it was read. Photoshop's blends that
/// OpenRaster has no name for go by the names Krita writes them under.
/// </summary>
public readonly record struct LayerBlend(string Op)
{
    public static LayerBlend Normal { get; } = new("svg:src-over");

    /// <summary>Photoshop's Dissolve: each pixel shown whole or not at all, as likely as it is opaque.</summary>
    public static LayerBlend Dissolve { get; } = new("krita:dissolve");

    /// <summary>OpenRaster's operations and Photoshop's, as Nexaflow offers them, each with the Skia blend that draws it — or
    /// none, where Skia has none and Nexaflow draws it itself (<see cref="LayerBlenders"/>).</summary>
    public static IReadOnlyList<(LayerBlend Blend, SKBlendMode? Skia)> Offered { get; } =
    [
        (Normal, SKBlendMode.SrcOver),
        (Dissolve, null),
        (new("svg:darken"), SKBlendMode.Darken),
        (new("svg:multiply"), SKBlendMode.Multiply),
        (new("svg:color-burn"), SKBlendMode.ColorBurn),
        (new("krita:linear_burn"), null),
        (new("krita:darker color"), null),
        (new("svg:lighten"), SKBlendMode.Lighten),
        (new("svg:screen"), SKBlendMode.Screen),
        (new("svg:color-dodge"), SKBlendMode.ColorDodge),
        (new("krita:linear_dodge"), null),
        (new("krita:lighter color"), null),
        (new("svg:overlay"), SKBlendMode.Overlay),
        (new("svg:soft-light"), SKBlendMode.SoftLight),
        (new("svg:hard-light"), SKBlendMode.HardLight),
        (new("krita:vivid_light"), null),
        (new("krita:linear light"), null),
        (new("krita:pin_light"), null),
        (new("krita:hard_mix_photoshop"), null),
        (new("svg:difference"), SKBlendMode.Difference),
        (new("krita:exclusion"), SKBlendMode.Exclusion),
        (new("krita:subtract"), null),
        (new("krita:divide"), null),
        (new("svg:hue"), SKBlendMode.Hue),
        (new("svg:saturation"), SKBlendMode.Saturation),
        (new("svg:color"), SKBlendMode.Color),
        (new("svg:luminosity"), SKBlendMode.Luminosity),
        (new("svg:plus"), SKBlendMode.Plus),
        (new("svg:dst-in"), SKBlendMode.DstIn),
        (new("svg:dst-out"), SKBlendMode.DstOut),
        (new("svg:dst-atop"), SKBlendMode.DstATop),
    ];

    /// <summary>One of the operations Nexaflow offers.</summary>
    public bool IsOffered
    {
        get
        {
            foreach (var (blend, _) in Offered)
                if (blend == this) return true;
            return false;
        }
    }

    /// <summary>Its name without the application's prefix, words joined by dashes — <c>multiply</c>, <c>linear-burn</c> — how
    /// the strings and the assistant name it.</summary>
    public string Key
    {
        get
        {
            var op = Op ?? "";
            return op[(op.IndexOf(':') + 1)..].Replace(' ', '-').Replace('_', '-');
        }
    }

    /// <summary>Each pixel shown whole or not at all — drawn by the compositor itself, as no blend of colours does it.</summary>
    public bool IsDissolve => this == Dissolve;

    /// <summary>
    /// The Skia blend that draws it, where Skia has one: OpenRaster's own by the table; another application's
    /// (<c>krita:exclusion</c>) by the Skia blend of the same name; null where Nexaflow draws it itself or knows it not.
    /// </summary>
    public SKBlendMode? SkiaMode
    {
        get
        {
            foreach (var (blend, skia) in Offered)
                if (blend == this) return skia;
            return Enum.TryParse<SKBlendMode>(Key.Replace("-", "", StringComparison.Ordinal), ignoreCase: true, out var named) ? named : null;
        }
    }

    /// <summary>
    /// <paramref name="paint"/> made to composite as this blend: through Skia's blend where it has one, else through Nexaflow's
    /// own (<see cref="LayerBlenders"/>); one Nexaflow knows not as Normal. Where <paramref name="clipped"/>, as a layer clipped to
    /// what lies beneath composites — source-atop, the alpha beneath kept (<see cref="LayerBlenders.ClippedFor"/>). Dissolve,
    /// which no blend draws, is left to the compositor and composites as Normal here.
    /// </summary>
    public void ApplyTo(SKPaint paint, bool clipped = false)
    {
        if (clipped)
        {
            paint.Blender = LayerBlenders.ClippedFor(IsDissolve ? Normal : this);
            return;
        }
        if (SkiaMode is { } mode)
        {
            paint.BlendMode = mode;
            return;
        }
        if (LayerBlenders.For(Key) is { } blender)
        {
            paint.Blender = blender;
            return;
        }
        paint.BlendMode = SKBlendMode.SrcOver;
    }
}

/// <summary>
/// A layer's mask: coverage over the canvas, as deep as its picture — white shows the layer, black hides it, grey shows it partly — with
/// whether it is used (<see cref="Enabled"/>) and whether it moves and transforms with its layer (<see cref="Linked"/>).
/// </summary>
public sealed record LayerMask(TiledCoverage Coverage, bool Enabled = true, bool Linked = true);

/// <summary>
/// Where a layer's own coordinates lie on the canvas — a picture's pixels, a markup layer's annotations — as an affine map:
/// scale (negative for a flip), rotation and skew in the first four, the offset in the last two. A transform composes onto it,
/// so the layer is drawn afresh from what it holds rather than from pixels already resampled.
/// </summary>
public readonly record struct LayerPlacement(double ScaleX, double SkewX, double OffsetX, double SkewY, double ScaleY, double OffsetY)
{
    public static LayerPlacement Origin { get; } = new(1, 0, 0, 0, 1, 0);

    /// <summary>At (<paramref name="x"/>, <paramref name="y"/>), scaled by <paramref name="scale"/> and turned clockwise by
    /// <paramref name="rotationDegrees"/> about its own origin.</summary>
    public static LayerPlacement At(double x, double y, double scale = 1, double rotationDegrees = 0)
        => FromMatrix(SKMatrix.CreateScale((float)scale, (float)scale)
                              .PostConcat(SKMatrix.CreateRotationDegrees((float)rotationDegrees))
                              .PostConcat(SKMatrix.CreateTranslation((float)x, (float)y)));

    public static LayerPlacement FromMatrix(SKMatrix m) => new(m.ScaleX, m.SkewX, m.TransX, m.SkewY, m.ScaleY, m.TransY);

    /// <summary>The layer's coordinates to canvas pixels.</summary>
    public SKMatrix ToMatrix() => new((float)ScaleX, (float)SkewX, (float)OffsetX, (float)SkewY, (float)ScaleY, (float)OffsetY, 0, 0, 1);

    /// <summary>This placement, then <paramref name="after"/> on the canvas.</summary>
    public LayerPlacement Then(SKMatrix after) => FromMatrix(ToMatrix().PostConcat(after));

    /// <summary>Moved by (<paramref name="dx"/>, <paramref name="dy"/>) on the canvas.</summary>
    public LayerPlacement Translated(double dx, double dy) => this with { OffsetX = OffsetX + dx, OffsetY = OffsetY + dy };

    /// <summary>The scale it amounts to over an area: how many canvas pixels a pixel of the layer covers, along a side.</summary>
    public double Scale => Math.Sqrt(Math.Abs(ScaleX * ScaleY - SkewX * SkewY));

    /// <summary>How far it turns the layer clockwise, in degrees: what text written on it turns back by to read upright.</summary>
    public double RotationDegrees => Math.Atan2(SkewY, ScaleX) * 180 / Math.PI;
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
    public KeptFromFile Kept { get; init; } = KeptFromFile.None;
}

/// <summary>A picture — a photo, by its file — with its recipe, in its own coordinates, placed on the canvas.</summary>
public sealed record PictureLayer : Layer
{
    public required PictureSource Source { get; init; }

    public LayerPlacement Placement { get; init; } = LayerPlacement.Origin;

    public ImageRecipe Recipe { get; init; } = ImageRecipe.Identity;
}

/// <summary>Pixels, in canvas coordinates, in their picture's colour: what paint, paste and fill make.</summary>
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

    /// <summary>Where the annotations' coordinates lie on the canvas: moved and transformed as a whole, the annotations kept
    /// as drawn, sharp and editable.</summary>
    public LayerPlacement Placement { get; init; } = LayerPlacement.Origin;
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
