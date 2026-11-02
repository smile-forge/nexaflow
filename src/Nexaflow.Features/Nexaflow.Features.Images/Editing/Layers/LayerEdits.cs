using System.Collections.Immutable;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Selection;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>Edits that reach every layer of a layered picture: its canvas resized or cropped, a layer copied whole.</summary>
public static class LayerEdits
{
    /// <summary>
    /// The picture on a <paramref name="width"/> × <paramref name="height"/> canvas, everything on it moved by
    /// (<paramref name="dx"/>, <paramref name="dy"/>) — Canvas size with an anchor, Crop to selection with the selection's corner
    /// taken back to the origin: pixels, pictures, annotations and linked masks move; nothing is resampled. The selection goes.
    /// </summary>
    public static LayeredPicture Resized(LayeredPicture picture, int width, int height, int dx, int dy)
    {
        LayeredPicture.RequireSize(width, height);
        return picture with
        {
            Width = width, Height = height,
            Layers = [.. picture.Layers.Select(l => Moved(l, width, height, dx, dy))],
            Selection = TiledCoverage.Clear(width, height),
        };
    }

    private static Layer Moved(Layer layer, int width, int height, int dx, int dy)
    {
        var moved = layer switch
        {
            PixelLayer pixels => pixels with { Pixels = pixels.Pixels.Moved(dx, dy) },
            PictureLayer picture => picture with { Placement = picture.Placement.Translated(dx, dy) },
            MarkupLayer markup => markup with { Placement = markup.Placement.Translated(dx, dy) },
            GroupLayer group => group with { Children = [.. group.Children.Select(c => Moved(c, width, height, dx, dy))] },
            _ => layer,
        };
        // A mask lies over the canvas: it takes the new canvas's size, and moves with its layer unless it is unlinked.
        if (moved.Mask is { } mask)
            moved = moved with { Mask = mask with { Coverage = mask.Coverage.Shifted(mask.Linked ? dx : 0, mask.Linked ? dy : 0, width, height) } };
        return moved;
    }

    /// <summary>
    /// <paramref name="layer"/> moved by (<paramref name="dx"/>, <paramref name="dy"/>) on a <paramref name="width"/> ×
    /// <paramref name="height"/> canvas — what the Move tool does: pixels, pictures, annotations, a group's layers and a linked mask move.
    /// </summary>
    public static Layer MovedBy(Layer layer, int width, int height, int dx, int dy) => Moved(layer, width, height, dx, dy);

    /// <summary>
    /// <paramref name="layer"/> transformed by <paramref name="matrix"/> on a <paramref name="width"/> × <paramref name="height"/>
    /// canvas — scaled, turned, flipped. A picture or markup layer's placement takes it, so it is drawn afresh from its source or its
    /// annotations and never softens; pixels are resampled once from what they were; a group's layers each take it; a linked mask
    /// goes with its layer, what lay outside it staying as it was.
    /// </summary>
    public static Layer Transformed(Layer layer, SKMatrix matrix, int width, int height)
    {
        var transformed = layer switch
        {
            PixelLayer pixels    => pixels with { Pixels = pixels.Pixels.Transformed(matrix, width, height) },
            PictureLayer picture => picture with { Placement = picture.Placement.Then(matrix) },
            MarkupLayer markup   => markup with { Placement = markup.Placement.Then(matrix) },
            GroupLayer group     => group with { Children = [.. group.Children.Select(c => Transformed(c, matrix, width, height))] },
            _                    => layer,
        };
        if (transformed.Mask is { Linked: true } mask)
            transformed = transformed with { Mask = mask with { Coverage = MappedCoverage(mask.Coverage, matrix) } };
        return transformed;
    }

    /// <summary><paramref name="coverage"/> carried by <paramref name="matrix"/>, keeping the value it has outside what it holds — a
    /// mask that shows everything still shows what the transform uncovers.</summary>
    public static TiledCoverage MappedCoverage(TiledCoverage coverage, SKMatrix matrix)
        => coverage.Outside == 255
            ? CoverageMapping.Map(coverage.Inverted(), matrix, coverage.Width, coverage.Height).Inverted()
            : CoverageMapping.Map(coverage, matrix, coverage.Width, coverage.Height);

    /// <summary><paramref name="layer"/> copied whole — a group with all it holds — every copy a layer of its own.</summary>
    public static Layer Duplicate(Layer layer, string name) => Copied(layer) with { Name = name };

    private static Layer Copied(Layer layer) => layer switch
    {
        GroupLayer group => group with { Id = Guid.NewGuid(), Children = [.. group.Children.Select(Copied)] },
        _ => layer with { Id = Guid.NewGuid() },
    };

    /// <summary>The rectangle a coverage's covered pixels lie in, within the canvas; null where nothing is covered.</summary>
    public static SKRectI? CoveredArea(TiledCoverage coverage) => coverage.Bounds is { IsEmpty: false } bounds ? bounds : null;

    /// <summary>
    /// A picture layer named <paramref name="name"/> showing the picture at <paramref name="source"/> — <paramref name="size"/>
    /// upright — fitted inside a <paramref name="width"/> × <paramref name="height"/> canvas and centred on it: at its own size
    /// where it fits, scaled down where it does not.
    /// </summary>
    public static PictureLayer Fitted(PictureSource source, SKSize size, int width, int height, string name)
    {
        double scale = Math.Min(1, Math.Min(width / size.Width, height / size.Height));
        return new PictureLayer
        {
            Name      = name,
            Source    = source,
            Placement = LayerPlacement.At((width - size.Width * scale) / 2, (height - size.Height * scale) / 2, scale),
        };
    }
}
