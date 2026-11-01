using System.Collections.Immutable;
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
            PictureLayer picture => picture with { Placement = picture.Placement with { OffsetX = picture.Placement.OffsetX + dx, OffsetY = picture.Placement.OffsetY + dy } },
            MarkupLayer markup => markup with { Elements = [.. markup.Elements.Select(e => e.Offset(dx, dy))] },
            GroupLayer group => group with { Children = [.. group.Children.Select(c => Moved(c, width, height, dx, dy))] },
            _ => layer,
        };
        // A mask lies over the canvas: it takes the new canvas's size, and moves with its layer unless it is unlinked.
        if (moved.Mask is { } mask)
            moved = moved with { Mask = mask with { Coverage = mask.Coverage.Shifted(mask.Linked ? dx : 0, mask.Linked ? dy : 0, width, height) } };
        return moved;
    }

    /// <summary><paramref name="layer"/> copied whole — a group with all it holds — every copy a layer of its own.</summary>
    public static Layer Duplicate(Layer layer, string name) => Copied(layer) with { Name = name };

    private static Layer Copied(Layer layer) => layer switch
    {
        GroupLayer group => group with { Id = Guid.NewGuid(), Children = [.. group.Children.Select(Copied)] },
        _ => layer with { Id = Guid.NewGuid() },
    };

    /// <summary>The rectangle a coverage's covered pixels lie in, within the canvas; null where nothing is covered.</summary>
    public static SKRectI? CoveredArea(TiledCoverage coverage) => coverage.Bounds is { IsEmpty: false } bounds ? bounds : null;
}
